using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Administration.Data;

namespace SenBilirsin.Modules.Administration.Api;

/// <summary>Denetim kaydını panelden görüntüleme (docs/05 §9). Salt okunur.</summary>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit-logs", async (AdministrationDbContext db, string? action, string? entity, int? page, int? pageSize, CancellationToken ct) =>
        {
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var number = Math.Max(1, page ?? 1);

            var query = db.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);
            if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(l => l.Entity == entity);

            var total = await query.CountAsync(ct);
            var items = await (from l in query
                               join a in db.AdminUsers.AsNoTracking() on l.AdminId equals a.Id into admins
                               from a in admins.DefaultIfEmpty()
                               orderby l.Id descending
                               select new { l.Id, l.AdminId, adminEmail = a == null ? null : a.Email, l.Action, l.Entity, l.EntityId, l.Before, l.After, l.Detail, l.CreatedAt })
                .Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new { items, total, page = number, pageSize = size });
        }).RequireAuthorization(AdminAuthEndpoints.PolicyName);

        return app;
    }
}
