using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Administration.Data;
using SenBilirsin.Modules.Administration.Domain;
using SenBilirsin.Modules.Administration.Security;

namespace SenBilirsin.Modules.Administration.Application;

public record LoginResult(bool Success, string? Token, DateTime? ExpiresAt, string? Error, int StatusCode)
{
    public static LoginResult Fail(string error, int status) => new(false, null, null, error, status);
}

public record CreatedAdmin(Guid Id, string Email, string Password, string TotpSecret, string OtpAuthUri);

/// <summary>
/// Yönetici girişi: e-posta + parola + TOTP kodu tek adımda doğrulanır (docs/05, docs/08 §6).
/// Hatalı denemede hangi alanın yanlış olduğu söylenmez; 5 ardışık hatadan sonra hesap 15 dk kilitlenir.
/// Parola özeti ASP.NET Identity'nin PasswordHasher'ı ile (PBKDF2) üretilir.
/// </summary>
public class AdminAuthService(AdministrationDbContext db, SecretProtector protector, AdminTokenService tokens,
    AuditLogger audit, TimeProvider clock)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public const int MinPasswordLength = 12;
    public const string GenericFailure = "E-posta, parola veya doğrulama kodu hatalı.";

    private static readonly PasswordHasher<AdminUser> Hasher = new();

    // Kullanıcı yoksa da aynı sürede hash doğrulaması yapılsın (zamanlama farkıyla hesap keşfi olmasın)
    private static readonly string DummyHash = Hasher.HashPassword(new AdminUser(), "dummy-password-for-timing");

    public static string? CheckPassword(string? password) =>
        password is null || password.Length < MinPasswordLength
            ? $"Parola en az {MinPasswordLength} karakter olmalı."
            : null;

    public async Task<LoginResult> LoginAsync(string? email, string? password, string? code, string? ip, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var admin = await db.AdminUsers.SingleOrDefaultAsync(a => a.Email == normalized, ct);

        if (admin is null || !admin.IsActive)
        {
            Hasher.VerifyHashedPassword(new AdminUser(), DummyHash, password ?? "");
            await audit.WriteAsync(null, "login_failed", "admin_user", null, detail: $"bilinmeyen/pasif hesap; ip={ip}", ct: ct);
            return LoginResult.Fail(GenericFailure, 401);
        }

        if (admin.LockedUntil is { } until && until > now.UtcDateTime)
        {
            await audit.WriteAsync(admin.Id, "login_locked", "admin_user", admin.Id.ToString(), detail: $"ip={ip}", ct: ct);
            return LoginResult.Fail("Çok fazla hatalı deneme. Bir süre sonra tekrar deneyin.", 429);
        }

        var passwordOk = Hasher.VerifyHashedPassword(admin, admin.PasswordHash, password ?? "") != PasswordVerificationResult.Failed;
        var secret = Base32.Decode(protector.Unprotect(admin.TotpSecretProtected));
        var totpOk = TotpService.TryVerify(secret, code, now, admin.LastTotpStep, out var step);

        if (!passwordOk || !totpOk)
        {
            admin.FailedLoginCount++;
            if (admin.FailedLoginCount >= MaxFailedAttempts)
            {
                admin.LockedUntil = now.UtcDateTime + LockDuration;
                admin.FailedLoginCount = 0;
            }

            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(admin.Id, "login_failed", "admin_user", admin.Id.ToString(), detail: $"ip={ip}", ct: ct);
            return LoginResult.Fail(GenericFailure, 401);
        }

        admin.FailedLoginCount = 0;
        admin.LockedUntil = null;
        admin.LastTotpStep = step;
        admin.LastLoginAt = now.UtcDateTime;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(admin.Id, "login", "admin_user", admin.Id.ToString(), detail: $"ip={ip}", ct: ct);

        var (token, expires) = tokens.Issue(admin);
        return new LoginResult(true, token, expires, null, 200);
    }

    public async Task<LoginResult> ChangePasswordAsync(Guid adminId, string? current, string? newPassword, CancellationToken ct = default)
    {
        var admin = await db.AdminUsers.SingleOrDefaultAsync(a => a.Id == adminId && a.IsActive, ct);
        if (admin is null) return LoginResult.Fail("Yönetici bulunamadı.", 404);

        if (Hasher.VerifyHashedPassword(admin, admin.PasswordHash, current ?? "") == PasswordVerificationResult.Failed)
            return LoginResult.Fail("Mevcut parola hatalı.", 400);
        if (CheckPassword(newPassword) is { } problem) return LoginResult.Fail(problem, 400);

        admin.PasswordHash = Hasher.HashPassword(admin, newPassword!);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(admin.Id, "password_changed", "admin_user", admin.Id.ToString(), ct: ct);
        return new LoginResult(true, null, null, null, 200);
    }

    /// <summary>
    /// Yeni yönetici oluşturur. Parola verilmezse rastgele üretilir. Dönen bilgi ekrana/sohbete DEĞİL
    /// yalnızca git'e girmeyen yerel bir dosyaya yazılmalıdır (bkz. <c>FirstAdminCommand</c>).
    /// </summary>
    public async Task<CreatedAdmin> CreateAdminAsync(string email, string? password = null, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (!normalized.Contains('@')) throw new ArgumentException("Geçerli bir e-posta girin.", nameof(email));
        if (await db.AdminUsers.AnyAsync(a => a.Email == normalized, ct))
            throw new InvalidOperationException("Bu e-posta ile bir yönetici zaten var.");

        var plain = password ?? GeneratePassword();
        if (CheckPassword(plain) is { } problem) throw new ArgumentException(problem, nameof(password));

        var totpSecret = TotpService.GenerateSecret();
        var admin = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            TotpSecretProtected = protector.Protect(totpSecret),
            Role = AdminRoles.Admin,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        admin.PasswordHash = Hasher.HashPassword(admin, plain);
        db.AdminUsers.Add(admin);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(admin.Id, "admin_created", "admin_user", admin.Id.ToString(), detail: "yerel komut", ct: ct);

        return new CreatedAdmin(admin.Id, normalized, plain, totpSecret, TotpService.OtpAuthUri("QU¿ Yönetim", normalized, totpSecret));
    }

    private static string GeneratePassword()
    {
        // Karıştırılabilen karakterler (0/O, 1/l/I) çıkarıldı; 20 karakter ≈ 110 bit
        const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(20, chars, (span, alphabet) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        });
    }
}
