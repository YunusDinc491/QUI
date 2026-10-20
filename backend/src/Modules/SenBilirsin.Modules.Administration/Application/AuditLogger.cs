using SenBilirsin.Modules.Administration.Data;
using SenBilirsin.Modules.Administration.Domain;

namespace SenBilirsin.Modules.Administration.Application;

/// <summary>Denetim kaydı yazar (docs/05 §9). Önce/sonra alanlarına gizli bilgi konmaz; çağıran sorumludur.</summary>
public class AuditLogger(AdministrationDbContext db, TimeProvider clock)
{
    public async Task WriteAsync(Guid? adminId, string action, string entity, string? entityId = null,
        string? before = null, string? after = null, string? detail = null, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            AdminId = adminId,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Before = before,
            After = after,
            Detail = detail is { Length: > 500 } ? detail[..500] : detail,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
    }
}
