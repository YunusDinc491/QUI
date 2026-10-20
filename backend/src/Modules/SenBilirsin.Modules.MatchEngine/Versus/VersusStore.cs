using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.MatchEngine.Data;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.MatchEngine.Versus;

/// <summary>Rakipli maçın veritabanı kayıtları (koşucu bellekte çalışır; kalıcı kayıt buradan yazılır).</summary>
public sealed class VersusStore(MatchEngineDbContext db, TimeProvider clock)
{
    public async Task CreateMatchAsync(VersusMatchSpec spec, IReadOnlyList<int[]> orders, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        db.Matches.Add(new Match
        {
            Id = spec.MatchId, Type = spec.Kind, Status = MatchStatus.Running, ModeCode = spec.ModeCode, CategoryId = spec.CategoryId,
            StartedAt = now, CurrentPosition = 1, SeenFilterRelaxed = spec.SeenFilterRelaxed,
        });
        foreach (var p in new[] { spec.A, spec.B })
            db.Players.Add(new MatchPlayer { MatchId = spec.MatchId, UserId = p.Id, IsBot = p.IsBot });
        for (var i = 0; i < spec.Questions.Count; i++)
        {
            db.Questions.Add(new MatchQuestion
            {
                MatchId = spec.MatchId, Position = i + 1, Attempt = 0, QuestionId = spec.Questions[i].Id,
                Slot = spec.Questions[i].Difficulty, OptionOrder = OptionShuffler.Serialize(orders[i]),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SaveRoundAsync(Guid matchId, int position, DateTime startsAt, DateTime endsAt, IEnumerable<MatchAnswer> answers, CancellationToken ct = default)
    {
        var mq = await db.Questions.FirstAsync(q => q.MatchId == matchId && q.Position == position && q.Attempt == 0, ct);
        mq.StartsAt = startsAt;
        mq.EndsAt = endsAt;
        db.Answers.AddRange(answers);
        await db.SaveChangesAsync(ct);

        await db.Matches.Where(m => m.Id == matchId).ExecuteUpdateAsync(s => s.SetProperty(m => m.CurrentPosition, position), ct);
    }

    public async Task SaveJokerAsync(MatchJoker joker, CancellationToken ct = default)
    {
        db.Jokers.Add(joker);
        await db.SaveChangesAsync(ct);
    }

    public sealed record PlayerFinal(Guid UserId, int Correct, int Points, int Bonus, string Result, int DisconnectMsUsed);

    public async Task FinishAsync(Guid matchId, MatchStatus status, Guid? winner, IEnumerable<PlayerFinal> players, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var match = await db.Matches.FirstAsync(m => m.Id == matchId, ct);
        match.Status = status;
        match.EndedAt = now;
        match.WinnerUserId = winner;
        foreach (var f in players)
        {
            var row = await db.Players.FirstAsync(p => p.MatchId == matchId && p.UserId == f.UserId, ct);
            row.CorrectCount = f.Correct;
            row.Points = f.Points;
            row.Bonus = f.Bonus;
            row.Result = f.Result;
            row.DisconnectMsUsed = f.DisconnectMsUsed;
            row.SettledAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sunucu yeniden başladığında yarım kalmış rakipli maçları iptal eder (puan yok, K-43).</summary>
    public async Task<int> CancelStaleAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await db.Matches.Where(m => m.Type != MatchKind.Solo && m.Status == MatchStatus.Running).Select(m => m.Id).ToListAsync(ct);
        if (ids.Count == 0) return 0;
        await db.Matches.Where(m => ids.Contains(m.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, MatchStatus.Cancelled).SetProperty(m => m.EndedAt, now), ct);
        await db.Players.Where(p => ids.Contains(p.MatchId)).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Result, MatchResults.Cancelled).SetProperty(p => p.Points, 0).SetProperty(p => p.SettledAt, now), ct);
        return ids.Count;
    }

    public static MatchJoker NewJoker(Guid matchId, Guid userId, JokerType type, int position, string resultJson, DateTime at) => new()
    {
        MatchId = matchId, UserId = userId, Type = type, Position = position, Attempt = 0, ResultJson = resultJson, UsedAt = at,
    };
}
