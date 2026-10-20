using Microsoft.EntityFrameworkCore;
using Npgsql;
using SenBilirsin.Modules.Users.Data;
using SenBilirsin.Modules.Users.Domain;

namespace SenBilirsin.Modules.Users.Application;

public sealed record ServiceError(string Code, string Message, int StatusCode = 400);

public sealed class ServiceResult<T>
{
    public T? Value { get; private init; }
    public ServiceError? Error { get; private init; }
    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Ok(T value) => new() { Value = value };
    public static ServiceResult<T> Fail(ServiceError error) => new() { Error = error };
}

public sealed record RegisterRequest(string? Username, string? DisplayName, int AvatarId, int BirthYear, bool TermsAccepted);

public sealed record UpdateProfileRequest(string? DisplayName, int? AvatarId);

/// <summary>Kullanıcı kaydı, profil ve cihaz oturumu işlemleri (Faz 1).</summary>
public sealed class UserService(UsersDbContext db, ProfanityFilter profanity, TimeProvider clock)
{
    /// <summary>Onaylanan koşul metni sürümü. Yasal metinler hazırlanınca (Faz 9) güncellenecek.</summary>
    public const string CurrentTermsVersion = "taslak-2026-10-20";

    public async Task<RuleResult> CheckUsernameAvailabilityAsync(string? raw, CancellationToken ct)
    {
        var rule = ProfileRules.CheckUsername(raw, profanity);
        if (!rule.IsValid) return rule;

        var username = ProfileRules.NormalizeUsername(raw);
        var taken = await db.Users.AnyAsync(x => x.Username == username, ct);
        return taken ? RuleResult.Fail("USERNAME_TAKEN", "Bu kullanıcı adı alınmış.") : RuleResult.Ok;
    }

    /// <summary>Silinmiş hesaplar anonimleştirildiği için uid'leri eşleşmez; ek filtre gerekmez.</summary>
    public Task<UserAccount?> FindByFirebaseUidAsync(string firebaseUid, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(x => x.FirebaseUid == firebaseUid, ct);

    public Task<List<Avatar>> GetAvatarsAsync(CancellationToken ct) =>
        db.Avatars.Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(ct);

    public async Task<ServiceResult<UserAccount>> RegisterAsync(string firebaseUid, RegisterRequest request, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(x => x.FirebaseUid == firebaseUid, ct))
            return Fail("ALREADY_REGISTERED", "Bu hesap zaten kayıtlı.", 409);

        if (!request.TermsAccepted)
            return Fail("TERMS_NOT_ACCEPTED", "Devam etmek için gizlilik politikasını ve koşulları kabul etmelisin.");

        var username = ProfileRules.CheckUsername(request.Username, profanity);
        if (!username.IsValid) return Fail(username.Code!, username.Message!);

        var displayName = ProfileRules.CheckDisplayName(request.DisplayName, profanity);
        if (!displayName.IsValid) return Fail(displayName.Code!, displayName.Message!);

        var now = clock.GetUtcNow().UtcDateTime;
        var age = ProfileRules.CheckBirthYear(request.BirthYear, now.Year);
        if (!age.IsValid) return Fail(age.Code!, age.Message!);

        var avatarCheck = await ValidateAvatarAsync(request.AvatarId, ct);
        if (avatarCheck is not null) return ServiceResult<UserAccount>.Fail(avatarCheck);

        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            FirebaseUid = firebaseUid,
            Username = ProfileRules.NormalizeUsername(request.Username),
            DisplayName = ProfileRules.NormalizeDisplayName(request.DisplayName),
            AvatarId = request.AvatarId,
            BirthYear = request.BirthYear,
            Status = UserStatus.Active,
            CreatedAt = now,
            TermsAcceptedAt = now,
            TermsVersion = CurrentTermsVersion,
        };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // İki kişi aynı anda aynı kullanıcı adını almaya çalışırsa veritabanı benzersizliği son söz sahibidir
            db.Entry(user).State = EntityState.Detached;
            return pg.ConstraintName?.Contains("firebase_uid") == true
                ? Fail("ALREADY_REGISTERED", "Bu hesap zaten kayıtlı.", 409)
                : Fail("USERNAME_TAKEN", "Bu kullanıcı adı alınmış.", 409);
        }

        return ServiceResult<UserAccount>.Ok(user);
    }

    public async Task<ServiceResult<UserAccount>> UpdateProfileAsync(UserAccount user, UpdateProfileRequest request, CancellationToken ct)
    {
        if (user.Status != UserStatus.Active)
            return Fail("ACCOUNT_NOT_ACTIVE", "Bu hesap şu an değişiklik yapamaz.", 403);

        if (request.DisplayName is not null)
        {
            var displayName = ProfileRules.CheckDisplayName(request.DisplayName, profanity);
            if (!displayName.IsValid) return Fail(displayName.Code!, displayName.Message!);
            user.DisplayName = ProfileRules.NormalizeDisplayName(request.DisplayName);
        }

        if (request.AvatarId is not null)
        {
            var avatarCheck = await ValidateAvatarAsync(request.AvatarId.Value, ct);
            if (avatarCheck is not null) return ServiceResult<UserAccount>.Fail(avatarCheck);
            user.AvatarId = request.AvatarId.Value;
        }

        await db.SaveChangesAsync(ct);
        return ServiceResult<UserAccount>.Ok(user);
    }

    /// <summary>Tek cihaz kuralı: bu cihaz hesabın aktif cihazı olur, öncekinin oturumu bir sonraki istekte düşer.</summary>
    public async Task SetActiveDeviceAsync(UserAccount user, string deviceId, CancellationToken ct)
    {
        user.ActiveDeviceId = deviceId;
        user.ActiveDeviceSince = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Hesap silme (K-37): kişisel alanlar anonimleştirilir, kimlik bağı koparılır.
    /// Firebase hesabını istemci siler (yeniden doğrulamadan sonra).
    /// </summary>
    public async Task DeleteAccountAsync(UserAccount user, CancellationToken ct)
    {
        user.Username = $"deleted_{user.Id.ToString("N")[..8]}";
        user.DisplayName = "Silinmiş kullanıcı";
        user.FirebaseUid = $"deleted:{user.Id}";
        user.BirthYear = 0;
        user.FrameId = null;
        user.ActiveDeviceId = null;
        user.ActiveDeviceSince = null;
        user.Status = UserStatus.Deleted;
        user.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    private async Task<ServiceError?> ValidateAvatarAsync(int avatarId, CancellationToken ct)
    {
        var avatar = await db.Avatars.FirstOrDefaultAsync(x => x.Id == avatarId, ct);
        if (avatar is null || !avatar.IsActive)
            return new ServiceError("AVATAR_NOT_FOUND", "Avatar bulunamadı.");

        // Ücretli avatarlar mağaza (Faz 6) gelene kadar seçilemez
        if (avatar.Price > 0)
            return new ServiceError("AVATAR_LOCKED", "Bu avatar henüz açılmamış.", 403);

        return null;
    }

    private static ServiceResult<UserAccount> Fail(string code, string message, int status = 400) =>
        ServiceResult<UserAccount>.Fail(new ServiceError(code, message, status));
}
