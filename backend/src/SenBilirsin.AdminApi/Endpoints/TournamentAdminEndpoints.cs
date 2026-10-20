using System.Security.Claims;
using SenBilirsin.Modules.Administration.Api;
using SenBilirsin.Modules.Administration.Application;
using SenBilirsin.Modules.Tournament.Application;
using SenBilirsin.Modules.Users.Application;

namespace SenBilirsin.AdminApi.Endpoints;

public record DisqualifyRequest(Guid UserId, string? Reason);
public record RewardsRequest(RewardInput[]? Tiers);

/// <summary>Turnuva yönetimi (docs/05 §6, docs/07 §2–3): dönemler, inceleme sıralaması, men, ödül tablosu, bütçe göstergesi.</summary>
public static class TournamentAdminEndpoints
{
    public static IEndpointRouteBuilder MapTournamentAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tournament").RequireAuthorization(AdminAuthEndpoints.PolicyName);

        group.MapGet("/periods", async (TournamentAdminService admin, int? take, CancellationToken ct) =>
            Results.Ok(await admin.ListPeriodsAsync(take ?? 20, ct)));

        group.MapGet("/periods/{id:int}/standings", async (int id, TournamentAdminService admin, IPlayerDirectory directory, int? take, CancellationToken ct) =>
        {
            var rows = await admin.StandingsAsync(id, take ?? 100, ct);
            var people = await directory.GetAsync(rows.Select(r => r.UserId), ct);
            return Results.Ok(rows.Select(r => new
            {
                r.Rank, r.UserId, r.Points, r.MatchCount, r.Disqualified, r.DisqualificationReason,
                name = people.GetValueOrDefault(r.UserId)?.DisplayName ?? "(silinmiş)",
            }));
        });

        group.MapPost("/periods/{id:int}/disqualify", async (int id, DisqualifyRequest request, ClaimsPrincipal user, TournamentAdminService admin, AuditLogger audit, CancellationToken ct) =>
        {
            var adminId = AdminAuthEndpoints.AdminIdOf(user);
            var result = await admin.DisqualifyAsync(id, request.UserId, request.Reason, adminId, ct);
            if (!result.IsSuccess) return Error(result.Error!);
            if (result.Value) await audit.WriteAsync(adminId, "tournament.disqualify", "period", id.ToString(),
                after: CatalogAdminEndpoints.Json(new { request.UserId, request.Reason }), ct: ct);
            return Results.Ok(new { disqualified = true, changed = result.Value });
        });

        group.MapDelete("/periods/{id:int}/disqualify/{userId:guid}", async (int id, Guid userId, ClaimsPrincipal user, TournamentAdminService admin, AuditLogger audit, CancellationToken ct) =>
        {
            var result = await admin.LiftDisqualificationAsync(id, userId, ct);
            if (!result.IsSuccess) return Error(result.Error!);
            if (result.Value) await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "tournament.disqualify_lifted", "period", id.ToString(),
                before: CatalogAdminEndpoints.Json(new { userId }), ct: ct);
            return Results.Ok(new { lifted = result.Value });
        });

        group.MapGet("/rewards", async (TournamentAdminService admin, CancellationToken ct) =>
            Results.Ok(new { tiers = await admin.GetRewardsAsync(ct), budget = await admin.BudgetAsync(ct) }));

        group.MapPut("/rewards", async (RewardsRequest request, ClaimsPrincipal user, TournamentAdminService admin, AuditLogger audit, CancellationToken ct) =>
        {
            var before = await admin.GetRewardsAsync(ct);
            var result = await admin.ReplaceRewardsAsync(request.Tiers, ct);
            if (!result.IsSuccess) return Error(result.Error!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "tournament.rewards_update", "reward_table", null,
                before: CatalogAdminEndpoints.Json(before), after: CatalogAdminEndpoints.Json(result.Value), ct: ct);
            return Results.Ok(new { tiers = result.Value, budget = await admin.BudgetAsync(ct) });
        });

        return app;
    }

    private static IResult Error(TournamentError e) => Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.StatusCode);
}
