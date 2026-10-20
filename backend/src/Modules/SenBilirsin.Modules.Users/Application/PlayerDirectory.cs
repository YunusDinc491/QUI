using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Users.Data;
using SenBilirsin.Modules.Users.Domain;

namespace SenBilirsin.Modules.Users.Application;

/// <summary>Rakibe gösterilen herkese açık oyuncu bilgisi (e-posta, doğum yılı vb. asla yok).</summary>
public sealed record PlayerInfo(Guid Id, string DisplayName, int AvatarId, int? FrameId);

/// <summary>Diğer modüllerin (maç motoru) oyuncu adı/avatarı okumak için kullandığı dar arayüz.</summary>
public interface IPlayerDirectory
{
    Task<IReadOnlyDictionary<Guid, PlayerInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>Firebase kimliğinden oyun kullanıcısı (kayıtsız/silinmiş/banlı ise null).</summary>
    Task<(Guid Id, string? ActiveDeviceId, UserStatus Status)?> FindByFirebaseUidAsync(string firebaseUid, CancellationToken ct = default);
}

public sealed class PlayerDirectory(UsersDbContext db) : IPlayerDirectory
{
    public async Task<IReadOnlyDictionary<Guid, PlayerInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        var rows = await db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .Select(u => new PlayerInfo(u.Id, u.DisplayName, u.AvatarId, u.FrameId)).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id);
    }

    public async Task<(Guid Id, string? ActiveDeviceId, UserStatus Status)?> FindByFirebaseUidAsync(string firebaseUid, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, ct);
        return user is null ? null : (user.Id, user.ActiveDeviceId, user.Status);
    }
}
