using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Tournament.Application;
using SenBilirsin.Modules.Tournament.Data;
using SenBilirsin.Modules.Tournament.Domain;
using SenBilirsin.Modules.Users.Api;
using SenBilirsin.Modules.Users.Application;

namespace SenBilirsin.Modules.Tournament.Api;

public sealed record RewardTierDto(int RankFrom, int RankTo, int Amount);

public sealed record LeaderboardRowDto(int Rank, Guid UserId, string Name, int AvatarId, int? FrameId, int Points, bool IsMe, int Reward);

/// <summary>Turnuva ekranı uçları (docs/07 §2): bitişe geri sayım, ödül tablosu, ilk 50, kendi sıran, önceki turun ilk 10'u.</summary>
public static class TournamentEndpoints
{
    public const int TopCount = 50;

    public static IEndpointRouteBuilder MapTournamentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tournament").RequireAuthorization().AddEndpointFilter<ActiveDeviceFilter>();

        group.MapGet("/current", async (HttpContext http, PeriodService periods, ScoreService scores, TournamentDbContext db,
            IPlayerDirectory directory, TimeProvider time, CancellationToken ct) =>
        {
            var me = ActivePlayer.IdOf(http);
            var period = await periods.GetCurrentAsync(ct);
            var standings = await scores.GetStandingsAsync(period, TopCount, me, ct);
            var tiers = await db.Rewards.AsNoTracking().OrderBy(t => t.RankFrom).ToListAsync(ct);

            return Results.Ok(new
            {
                periodId = period.Id, startsAt = period.StartsAt, endsAt = period.EndsAt, serverNow = time.GetUtcNow().UtcDateTime,
                rewards = tiers.Select(t => new RewardTierDto(t.RankFrom, t.RankTo, t.Amount)),
                top = await Rows(standings.Top, tiers, directory, me, ct),
                me = standings.Me is { } mine ? new { rank = mine.Rank, points = mine.Points, reward = RewardFor(tiers, mine.Rank) } : null,
            });
        });

        // İlk açılışta gösterilen "önceki turun ilk 10'u" penceresi
        group.MapGet("/previous", async (HttpContext http, PeriodService periods, ScoreService scores, TournamentDbContext db,
            IPlayerDirectory directory, CancellationToken ct) =>
        {
            var me = ActivePlayer.IdOf(http);
            var period = await periods.GetPreviousAsync(ct);
            if (period is null) return Results.Ok(new { periodId = (int?)null });

            var standings = await scores.GetStandingsAsync(period, 10, me, ct);
            var tiers = await db.Rewards.AsNoTracking().OrderBy(t => t.RankFrom).ToListAsync(ct);
            return Results.Ok(new
            {
                periodId = (int?)period.Id, startsAt = period.StartsAt, endsAt = period.EndsAt, distributed = period.Status == PeriodStatus.Distributed,
                top = await Rows(standings.Top, tiers, directory, me, ct),
                me = standings.Me is { } mine ? new { rank = mine.Rank, points = mine.Points, reward = RewardFor(tiers, mine.Rank) } : null,
            });
        });

        return app;
    }

    public static int RewardFor(IReadOnlyCollection<RewardTier> tiers, int rank) =>
        tiers.Where(t => rank >= t.RankFrom && rank <= t.RankTo).Select(t => t.Amount).DefaultIfEmpty(0).Max();

    private static async Task<IReadOnlyList<LeaderboardRowDto>> Rows(IReadOnlyList<StandingRow> rows, IReadOnlyCollection<RewardTier> tiers,
        IPlayerDirectory directory, Guid me, CancellationToken ct)
    {
        var people = await directory.GetAsync(rows.Select(r => r.UserId), ct);
        return rows.Select(r =>
        {
            var p = people.GetValueOrDefault(r.UserId);
            return new LeaderboardRowDto(r.Rank, r.UserId, p?.DisplayName ?? "Oyuncu", p?.AvatarId ?? 1, p?.FrameId, r.Points, r.UserId == me, RewardFor(tiers, r.Rank));
        }).ToList();
    }
}
