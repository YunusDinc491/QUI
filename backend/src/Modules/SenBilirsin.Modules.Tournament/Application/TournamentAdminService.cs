using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Tournament.Data;
using SenBilirsin.Modules.Tournament.Domain;

namespace SenBilirsin.Modules.Tournament.Application;

public sealed record AdminPeriod(int Id, DateTime StartsAt, DateTime EndsAt, string Phase, bool ReviewDue, int Participants, long TotalPoints, DateTime? DistributedAt);

public sealed record AdminStanding(int? Rank, Guid UserId, int Points, int MatchCount, bool Disqualified, string? DisqualificationReason);

public sealed record RewardInput(int RankFrom, int RankTo, int Amount);

public sealed record TournamentError(string Code, string Message, int StatusCode = 400);

public sealed class TournamentResult<T>
{
    public T? Value { get; private init; }
    public TournamentError? Error { get; private init; }
    public bool IsSuccess => Error is null;
    public static TournamentResult<T> Ok(T value) => new() { Value = value };
    public static TournamentResult<T> Fail(string code, string message, int status = 400) => new() { Error = new TournamentError(code, message, status) };
}

public sealed record BudgetEstimate(int PerPeriodPoints, int PerMonthPoints, decimal PerMonthLira, int PeriodsPerMonth, decimal LiraPerPoint);

/// <summary>Yönetim paneli için turnuva işlemleri: inceleme, men, ödül tablosu, bütçe göstergesi (docs/05, docs/07 §2–3).</summary>
public sealed class TournamentAdminService(TournamentDbContext db, ScoreService scores, TimeProvider time)
{
    /// <summary>Bitişten bu kadar önce inceleme hatırlatması başlar (docs/07 §2).</summary>
    public static readonly TimeSpan ReviewLead = TimeSpan.FromHours(1);

    public const decimal LiraPerPoint = 0.05m;
    public const int PeriodsPerMonth = 15;

    public async Task<IReadOnlyList<AdminPeriod>> ListPeriodsAsync(int take = 20, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var periods = await db.Periods.AsNoTracking().OrderByDescending(p => p.StartsAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var ids = periods.Select(p => p.Id).ToList();
        var sums = await db.Scores.AsNoTracking().Where(s => ids.Contains(s.PeriodId)).GroupBy(s => s.PeriodId)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(x => (long)x.Points) }).ToListAsync(ct);

        return periods.Select(p =>
        {
            var s = sums.FirstOrDefault(x => x.Key == p.Id);
            var phase = p.Status == PeriodStatus.Distributed ? "distributed" : now >= p.EndsAt ? "awaiting_distribution" : now >= p.EndsAt - ReviewLead ? "review" : "open";
            return new AdminPeriod(p.Id, p.StartsAt, p.EndsAt, phase, phase == "review", s?.Count ?? 0, s?.Total ?? 0, p.DistributedAt);
        }).ToList();
    }

    /// <summary>İnceleme sıralaması: men edilenler de listelenir (sırasız, işaretli) ki yönetici görebilsin ve men kaldırabilsin.</summary>
    public async Task<IReadOnlyList<AdminStanding>> StandingsAsync(int periodId, int take, CancellationToken ct = default)
    {
        var ranked = await scores.RankedAsync(periodId, ct);
        var banned = await db.Disqualifications.AsNoTracking().Where(d => d.PeriodId == periodId).ToListAsync(ct);
        var bannedScores = await db.Scores.AsNoTracking().Where(s => s.PeriodId == periodId && banned.Select(b => b.UserId).Contains(s.UserId)).ToListAsync(ct);

        var rows = ranked.Take(Math.Clamp(take, 1, 500)).Select(r => new AdminStanding(r.Rank, r.UserId, r.Points, r.MatchCount, false, null)).ToList();
        rows.AddRange(banned.Select(b =>
        {
            var s = bannedScores.FirstOrDefault(x => x.UserId == b.UserId);
            return new AdminStanding(null, b.UserId, s?.Points ?? 0, s?.MatchCount ?? 0, true, b.Reason);
        }));
        return rows;
    }

    public async Task<TournamentResult<bool>> DisqualifyAsync(int periodId, Guid userId, string? reason, Guid? adminId, CancellationToken ct = default)
    {
        var period = await db.Periods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId, ct);
        if (period is null) return TournamentResult<bool>.Fail("PERIOD_NOT_FOUND", "Dönem bulunamadı.", 404);
        if (period.Status == PeriodStatus.Distributed) return TournamentResult<bool>.Fail("PERIOD_DISTRIBUTED", "Ödüller dağıtılmış dönemde men uygulanamaz (dağıtım geri alınmaz).", 409);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300) return TournamentResult<bool>.Fail("REASON_INVALID", "Gerekçe 1–300 karakter olmalı.");
        if (await db.Disqualifications.AnyAsync(d => d.PeriodId == periodId && d.UserId == userId, ct)) return TournamentResult<bool>.Ok(false); // zaten men edilmiş

        db.Disqualifications.Add(new Disqualification
        {
            PeriodId = periodId, UserId = userId, Reason = reason.Trim(), AdminId = adminId, CreatedAt = time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
        return TournamentResult<bool>.Ok(true);
    }

    public async Task<TournamentResult<bool>> LiftDisqualificationAsync(int periodId, Guid userId, CancellationToken ct = default)
    {
        var period = await db.Periods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId, ct);
        if (period is null) return TournamentResult<bool>.Fail("PERIOD_NOT_FOUND", "Dönem bulunamadı.", 404);
        if (period.Status == PeriodStatus.Distributed) return TournamentResult<bool>.Fail("PERIOD_DISTRIBUTED", "Ödüller dağıtılmış dönemde men kaldırılamaz.", 409);
        var removed = await db.Disqualifications.Where(d => d.PeriodId == periodId && d.UserId == userId).ExecuteDeleteAsync(ct);
        return TournamentResult<bool>.Ok(removed > 0);
    }

    public async Task<IReadOnlyList<RewardInput>> GetRewardsAsync(CancellationToken ct = default) =>
        await db.Rewards.AsNoTracking().OrderBy(r => r.RankFrom).Select(r => new RewardInput(r.RankFrom, r.RankTo, r.Amount)).ToListAsync(ct);

    /// <summary>Ödül tablosunu bütünüyle değiştirir. Satırlar çakışmamalı; sıralar 1'den başlar, miktarlar pozitif.</summary>
    public async Task<TournamentResult<IReadOnlyList<RewardInput>>> ReplaceRewardsAsync(IReadOnlyList<RewardInput>? input, CancellationToken ct = default)
    {
        if (input is null || input.Count == 0 || input.Count > 100)
            return TournamentResult<IReadOnlyList<RewardInput>>.Fail("REWARDS_INVALID", "Ödül tablosu 1–100 satır olmalı.");
        var sorted = input.OrderBy(r => r.RankFrom).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var r = sorted[i];
            if (r.RankFrom < 1 || r.RankTo < r.RankFrom) return TournamentResult<IReadOnlyList<RewardInput>>.Fail("REWARDS_INVALID", $"Geçersiz sıra aralığı: {r.RankFrom}–{r.RankTo}.");
            if (r.Amount <= 0 || r.Amount > 100_000) return TournamentResult<IReadOnlyList<RewardInput>>.Fail("REWARDS_INVALID", "Ödül miktarı 1–100000 arasında olmalı.");
            if (i > 0 && r.RankFrom <= sorted[i - 1].RankTo) return TournamentResult<IReadOnlyList<RewardInput>>.Fail("REWARDS_OVERLAP", "Sıra aralıkları çakışıyor.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Rewards.ExecuteDeleteAsync(ct);
        db.Rewards.AddRange(sorted.Select(r => new RewardTier { RankFrom = r.RankFrom, RankTo = r.RankTo, Amount = r.Amount }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TournamentResult<IReadOnlyList<RewardInput>>.Ok(await GetRewardsAsync(ct));
    }

    /// <summary>"Bu ödül tablosuyla aylık maksimum maliyet" göstergesi (docs/07 §3): en kötü durum, tablonun tamamı dolu.</summary>
    public async Task<BudgetEstimate> BudgetAsync(CancellationToken ct = default)
    {
        var tiers = await db.Rewards.AsNoTracking().ToListAsync(ct);
        var perPeriod = tiers.Sum(t => t.Amount * (t.RankTo - t.RankFrom + 1));
        var perMonth = perPeriod * PeriodsPerMonth;
        return new BudgetEstimate(perPeriod, perMonth, perMonth * LiraPerPoint, PeriodsPerMonth, LiraPerPoint);
    }
}
