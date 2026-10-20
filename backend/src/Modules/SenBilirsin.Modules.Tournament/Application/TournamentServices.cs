using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SenBilirsin.Modules.MatchEngine.Application;
using SenBilirsin.Modules.Tournament.Data;
using SenBilirsin.Modules.Tournament.Domain;
using SenBilirsin.Modules.Users.Application;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.Tournament.Application;

/// <summary>Dönem sınırları. İlk dönem <c>Tournament:FirstPeriodStart</c> (Türkiye saati) anında başlar; her 48 saatte bir yenisi gelir.</summary>
public sealed class TournamentClock(IConfiguration config)
{
    public static readonly TimeSpan PeriodLength = TimeSpan.FromHours(48);

    /// <summary>Türkiye sabit UTC+3'tür (2016'dan beri yaz saati yok).</summary>
    public static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public const string DefaultFirstPeriodStart = "2026-01-01T00:00:00+03:00";

    public DateTime FirstPeriodStartUtc => DateTimeOffset.Parse(config["Tournament:FirstPeriodStart"] ?? DefaultFirstPeriodStart).UtcDateTime;

    /// <summary>Verilen anı kapsayan dönemin başlangıcı (UTC).</summary>
    public DateTime PeriodStartFor(DateTime utc)
    {
        var first = FirstPeriodStartUtc;
        if (utc < first) return first;
        var index = (long)Math.Floor((utc - first) / PeriodLength);
        return first + index * PeriodLength;
    }
}

public sealed record StandingRow(int Rank, Guid UserId, int Points, int MatchCount);

public sealed record Standings(int PeriodId, DateTime StartsAt, DateTime EndsAt, IReadOnlyList<StandingRow> Top, StandingRow? Me);

public sealed class PeriodService(TournamentDbContext db, TournamentClock clock, TimeProvider time)
{
    public async Task<Period> EnsureForAsync(DateTime utc, CancellationToken ct = default)
    {
        var start = clock.PeriodStartFor(utc);
        var existing = await db.Periods.FirstOrDefaultAsync(p => p.StartsAt == start, ct);
        if (existing is not null) return existing;

        var period = new Period { StartsAt = start, EndsAt = start + TournamentClock.PeriodLength, Status = PeriodStatus.Open };
        db.Periods.Add(period);
        try
        {
            await db.SaveChangesAsync(ct);
            return period;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(period).State = EntityState.Detached;
            return await db.Periods.AsNoTracking().FirstAsync(p => p.StartsAt == start, ct);
        }
    }

    public Task<Period> GetCurrentAsync(CancellationToken ct = default) => EnsureForAsync(time.GetUtcNow().UtcDateTime, ct);

    public Task<Period?> GetAsync(int id, CancellationToken ct = default) => db.Periods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>Bitmiş en son dönem (ilk açılışta "önceki turun ilk 10'u" için).</summary>
    public async Task<Period?> GetPreviousAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return await db.Periods.AsNoTracking().Where(p => p.EndsAt <= now).OrderByDescending(p => p.EndsAt).FirstOrDefaultAsync(ct);
    }
}

/// <summary>Maç puanını döneme yazar ve sıralamayı verir (docs/07 §2).</summary>
public sealed class ScoreService(TournamentDbContext db, PeriodService periods, IPlayerScores playerScores, TimeProvider time)
{
    /// <summary>
    /// Maç puanını bitiş anındaki döneme yazar. Aynı (maç, oyuncu) ikinci kez yazılmaz. Dönem zaten dağıtıldıysa
    /// (geç gelen kayıt) puan açık olan şimdiki döneme yazılır. Toplam puan ayrıca yansıtılır; yansıtılamazsa iş tekrar dener.
    /// </summary>
    public async Task<bool> CreditAsync(Guid matchId, Guid userId, int points, DateTime settledAt, CancellationToken ct = default)
    {
        if (points <= 0) return false;
        if (await db.Entries.AnyAsync(e => e.MatchId == matchId && e.UserId == userId, ct)) return false;

        var period = await periods.EnsureForAsync(settledAt, ct);
        if (period.Status == PeriodStatus.Distributed) period = await periods.GetCurrentAsync(ct);

        var entry = new PeriodScoreEntry
        {
            PeriodId = period.Id, UserId = userId, MatchId = matchId, Points = points, CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Entries.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(entry).State = EntityState.Detached;
            return false; // aynı anda başka istek yazdı
        }

        await db.Database.ExecuteSqlAsync($@"
            INSERT INTO tournament.period_scores (period_id, user_id, points, match_count)
            VALUES ({period.Id}, {userId}, {points}, 1)
            ON CONFLICT (period_id, user_id) DO UPDATE SET points = tournament.period_scores.points + EXCLUDED.points, match_count = tournament.period_scores.match_count + 1", ct);
        await tx.CommitAsync(ct);

        await ApplyTotalAsync(entry, ct);
        return true;
    }

    private async Task ApplyTotalAsync(PeriodScoreEntry entry, CancellationToken ct)
    {
        // Önce işaretleme (koşullu güncelleme) → yalnızca bir çağrı artırır; artırma başarısız olursa işaret geri alınır
        var claimed = await db.Entries.Where(e => e.Id == entry.Id && !e.TotalApplied).ExecuteUpdateAsync(s => s.SetProperty(e => e.TotalApplied, true), ct);
        if (claimed == 0) return;
        try
        {
            await playerScores.AddTotalScoreAsync(entry.UserId, entry.Points, ct);
        }
        catch
        {
            await db.Entries.Where(e => e.Id == entry.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.TotalApplied, false), ct);
            throw;
        }
    }

    /// <summary>Toplam puana yansıtılamamış kayıtları tekrar dener (arka plan işi).</summary>
    public async Task<int> ApplyPendingTotalsAsync(CancellationToken ct = default)
    {
        var pending = await db.Entries.Where(e => !e.TotalApplied).OrderBy(e => e.Id).Take(200).ToListAsync(ct);
        foreach (var e in pending) await ApplyTotalAsync(e, ct);
        return pending.Count;
    }

    /// <summary>
    /// Sıralama: men edilenler hariç, puanı 0'dan büyükler; eşit puan aynı sırayı paylaşır (1, 2, 2, 4). İlk <paramref name="take"/> kişi ve
    /// (verildiyse) kullanıcının kendi sırası döner.
    /// </summary>
    public async Task<Standings> GetStandingsAsync(Period period, int take, Guid? me, CancellationToken ct = default)
    {
        var all = await RankedAsync(period.Id, ct);
        var top = all.Where(r => r.Rank <= int.MaxValue).Take(take).ToList();
        var mine = me is { } id ? all.FirstOrDefault(r => r.UserId == id) : null;
        return new Standings(period.Id, period.StartsAt, period.EndsAt, top, mine);
    }

    /// <summary>Tüm sıralanmış satırlar (dağıtım ve inceleme için).</summary>
    public async Task<IReadOnlyList<StandingRow>> RankedAsync(int periodId, CancellationToken ct = default)
    {
        var banned = await db.Disqualifications.AsNoTracking().Where(d => d.PeriodId == periodId).Select(d => d.UserId).ToListAsync(ct);
        var rows = await db.Scores.AsNoTracking().Where(s => s.PeriodId == periodId && s.Points > 0 && !banned.Contains(s.UserId))
            .OrderByDescending(s => s.Points).ThenBy(s => s.UserId).ToListAsync(ct);

        var result = new List<StandingRow>(rows.Count);
        var rank = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (i == 0 || rows[i].Points != rows[i - 1].Points) rank = i + 1; // eşitler aynı sırayı paylaşır
            result.Add(new StandingRow(rank, rows[i].UserId, rows[i].Points, rows[i].MatchCount));
        }

        return result;
    }
}

public sealed record DistributionResult(int PeriodId, int Recipients, long TotalPoints, bool AlreadyDistributed);

/// <summary>
/// Dönem kapanışında ödül dağıtımı (docs/07 §2): bitişten 30 sn sonra (yazma kilidi), dönem başına kilitli ve idempotent;
/// ödüller hediye puanı defterine <c>tournament:{dönem}:{kullanıcı}</c> anahtarıyla yazılır. Geri alınmaz.
/// </summary>
public sealed class DistributionService(TournamentDbContext db, ScoreService scores, IPointsWallet wallet, TimeProvider time)
{
    public static readonly TimeSpan WriteLock = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<DistributionResult>> DistributeDueAsync(CancellationToken ct = default)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - WriteLock;
        var due = await db.Periods.AsNoTracking().Where(p => p.Status != PeriodStatus.Distributed && p.EndsAt <= cutoff).OrderBy(p => p.StartsAt).Select(p => p.Id).ToListAsync(ct);
        var results = new List<DistributionResult>();
        foreach (var id in due) results.Add(await DistributeAsync(id, ct));
        return results;
    }

    public async Task<DistributionResult> DistributeAsync(int periodId, CancellationToken ct = default)
    {
        // Dönem başına tek çalıştırıcı: danışma kilidi (iki işlem aynı anda dağıtamaz)
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey(periodId)})", ct);

        var period = await db.Periods.FirstAsync(p => p.Id == periodId, ct);
        if (period.Status == PeriodStatus.Distributed) return new DistributionResult(periodId, 0, 0, true);

        var tiers = await db.Rewards.AsNoTracking().ToListAsync(ct);
        long total = 0;
        var recipients = 0;
        foreach (var row in await scores.RankedAsync(periodId, ct))
        {
            var amount = tiers.Where(t => row.Rank >= t.RankFrom && row.Rank <= t.RankTo).Select(t => t.Amount).DefaultIfEmpty(0).Max();
            if (amount <= 0) continue;
            // Defter anahtarı benzersiz: çökme sonrası yeniden denemede aynı ödül ikinci kez yazılmaz
            await wallet.GrantAsync(row.UserId, amount, PointsLedgerTypes.Tournament, "period", periodId.ToString(), $"tournament:{periodId}:{row.UserId:N}", $"Turnuva ödülü ({row.Rank}. sıra)", ct);
            total += amount;
            recipients++;
        }

        period.Status = PeriodStatus.Distributed;
        period.DistributedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DistributionResult(periodId, recipients, total, false);
    }

    private static long LockKey(int periodId) => 0x5452_4E00_0000_0000L + periodId; // "TRN" önekli, dönem başına benzersiz
}

/// <summary>Maçlardan kesinleşen puanı turnuvaya aktaran alıcı (<see cref="IMatchPointsSink"/>).</summary>
public sealed class TournamentPointsSink(ScoreService scores) : IMatchPointsSink
{
    public Task CreditAsync(Guid matchId, Guid userId, int points, DateTime settledAt, CancellationToken ct = default) =>
        scores.CreditAsync(matchId, userId, points, settledAt, ct);
}
