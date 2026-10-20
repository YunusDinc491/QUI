using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Catalog.Domain;
using SenBilirsin.Modules.MatchEngine.Data;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Questions.Domain;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.MatchEngine.Application;

/// <summary>Aynı maça aynı anda gelen istekleri sıraya sokar (tek instance mimarisi; ek güvence veritabanı anahtarlarıdır).</summary>
public sealed class MatchLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid matchId, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(matchId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

/// <summary>
/// Bireysel maç (docs/01 §3–5): 10 soru, 15 sn, sunucu zamanlaması, yanlışta eleme, reklamla bir kez devam,
/// joker (tür başına maçta bir, soru başına bir), pes. Doğru cevap yalnızca burada bilinir; istemciye cevaplanana dek gitmez.
/// Bağlantı kopması/terk: süre dolduktan <see cref="MatchSettings.AbandonAfterSeconds"/> sn sonra maç iptal olur, puan verilmez.
/// </summary>
public sealed class SoloMatchService(
    MatchEngineDbContext db,
    IQuestionBank bank,
    ICatalogQueries catalog,
    IJokerWallet wallet,
    IRewardedAdGate adGate,
    MatchLocks locks,
    MatchSettings settings,
    TimeProvider clock,
    Random random)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ============================================================ Başlat

    public async Task<MatchResult<MatchState>> StartAsync(Guid userId, string? modeCode, int? categoryId, CancellationToken ct = default)
    {
        // Devam eden maç varsa (önce süresi geçmişleri temizle) yenisi başlamaz; istemci mevcut maça döner
        var active = await FindActiveAsync(userId, ct);
        if (active is not null)
        {
            using var _ = await locks.AcquireAsync(active.Match.Id, ct);
            var ctx = await LoadAsync(userId, active.Match.Id, ct);
            if (ctx is not null && await ApplyExpiryAsync(ctx, ct) is false && IsActive(ctx.Match))
                return MatchResult<MatchState>.Fail("MATCH_IN_PROGRESS", "Devam eden bir maçın var.", 409, ctx.Match.Id);
        }

        var scope = await ResolveScopeAsync(modeCode, categoryId, ct);
        if (scope.Error is not null) return MatchResult<MatchState>.Fail(scope.Error.Value.Code, scope.Error.Value.Message, scope.Error.Value.Status);

        var picked = await bank.PickAsync(userId, scope.CategoryIds, MatchPlan.PickPlan(settings), null, ct);
        if (picked.PoolTooSmall)
            return MatchResult<MatchState>.Fail("NOT_ENOUGH_QUESTIONS", "Bu kategoride şu an yeterli soru yok. Başka bir kategori dene.", 409);

        var now = Now;
        var match = new Match
        {
            Id = Guid.NewGuid(), Type = MatchKind.Solo, Status = MatchStatus.Running, ModeCode = scope.ModeCode, CategoryId = scope.CategoryId,
            StartedAt = now, CurrentPosition = 1, SeenFilterRelaxed = picked.SeenFilterRelaxed,
        };
        var player = new MatchPlayer { MatchId = match.Id, UserId = userId };
        var questions = picked.Questions.Select((q, i) => new MatchQuestion
        {
            MatchId = match.Id, Position = i + 1, Attempt = 0, QuestionId = q.Id, Slot = MatchPlan.DifficultyAt(i + 1, settings),
            OptionOrder = OptionShuffler.Serialize(OptionShuffler.Shuffle(random)),
        }).ToList();

        db.Matches.Add(match);
        db.Players.Add(player);
        db.Questions.AddRange(questions);
        await ServeAsync(userId, questions[0], now.AddMilliseconds(settings.FirstQuestionLeadMs));
        await db.SaveChangesAsync(ct);

        return MatchResult<MatchState>.Ok(await BuildStateAsync(new Ctx(match, player, questions, [], []), ct));
    }

    // ============================================================ Mevcut maç (yeniden bağlanma)

    public async Task<MatchResult<MatchState>> GetCurrentAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await FindActiveAsync(userId, ct);
        if (active is null) return MatchResult<MatchState>.Fail("NO_ACTIVE_MATCH", "Devam eden maç yok.", 404);

        using var _ = await locks.AcquireAsync(active.Match.Id, ct);
        var ctx = await LoadAsync(userId, active.Match.Id, ct);
        if (ctx is null) return MatchResult<MatchState>.Fail("NO_ACTIVE_MATCH", "Devam eden maç yok.", 404);
        await ApplyExpiryAsync(ctx, ct);
        return IsActive(ctx.Match)
            ? MatchResult<MatchState>.Ok(await BuildStateAsync(ctx, ct))
            : MatchResult<MatchState>.Fail("NO_ACTIVE_MATCH", "Devam eden maç yok.", 404);
    }

    // ============================================================ Cevap

    public async Task<MatchResult<AnswerOutcome>> AnswerAsync(Guid userId, Guid matchId, int position, int? option, int? second, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(matchId, ct);
        var ctx = await LoadAsync(userId, matchId, ct);
        if (ctx is null) return MatchResult<AnswerOutcome>.Fail("MATCH_NOT_FOUND", "Maç bulunamadı.", 404);

        await ApplyExpiryAsync(ctx, ct);
        var current = CurrentQuestion(ctx);

        // Aynı soruya tekrar gelen istek (ilk dokunuş kesin): kayıtlı sonuç aynen döner
        var existing = ctx.Answers.FirstOrDefault(a => a.Position == position && a.Attempt == (ctx.Questions.Where(q => q.Position == position).Max(q => (int?)q.Attempt) ?? 0));
        if (existing is not null) return MatchResult<AnswerOutcome>.Ok(await OutcomeFromStoredAsync(ctx, position, existing, ct));

        if (ctx.Match.Status != MatchStatus.Running)
            return MatchResult<AnswerOutcome>.Fail("MATCH_NOT_RUNNING", "Bu maç artık oynanmıyor.", 409);
        if (position != ctx.Match.CurrentPosition)
            return MatchResult<AnswerOutcome>.Fail("WRONG_POSITION", "Sıradaki soru bu değil.", 409);
        if (!JokerRules.IsValidIndex(option) || !JokerRules.IsValidIndex(second))
            return MatchResult<AnswerOutcome>.Fail("OPTION_INVALID", "Şık 0–3 arasında olmalı.");

        var now = Now;
        var startsAt = current.StartsAt!.Value;
        var endsAt = current.EndsAt!.Value;
        if (now < startsAt.AddMilliseconds(-1000))
            return MatchResult<AnswerOutcome>.Fail("TOO_EARLY", "Soru henüz başlamadı.", 409);

        var doubleActive = ctx.Jokers.Any(j => j.Type == JokerType.Double && j.Position == current.Position && j.Attempt == current.Attempt);
        if (second is not null && !doubleActive)
            return MatchResult<AnswerOutcome>.Fail("DOUBLE_NOT_ACTIVE", "Çift cevap jokeri bu soruda kullanılmadı.");
        if (second is not null && second == option)
            return MatchResult<AnswerOutcome>.Fail("OPTION_INVALID", "İki şık farklı olmalı.");

        var timedOut = now > endsAt.AddMilliseconds(settings.GraceMs);
        if (option is null && !timedOut)
        {
            // Cevapsız "süre doldu" bildirimi, ancak süre gerçekten bitmişse (küçük saat farkı toleransıyla) kabul edilir
            if (now < endsAt.AddMilliseconds(-300)) return MatchResult<AnswerOutcome>.Fail("TOO_EARLY", "Süre henüz dolmadı.", 409);
            timedOut = true;
        }

        var question = (await bank.GetByIdsAsync([current.QuestionId], ct))[0];
        var order = OptionShuffler.Parse(current.OptionOrder);
        var correctDisplay = OptionShuffler.ToDisplay(order, question.CorrectIndex);
        var isCorrect = !timedOut && JokerRules.IsCorrect(correctDisplay, option, doubleActive ? second : null);

        var answer = new MatchAnswer
        {
            MatchId = matchId, UserId = userId, Position = current.Position, Attempt = current.Attempt,
            ChosenDisplay = timedOut ? null : option, SecondDisplay = timedOut || !doubleActive ? null : second,
            IsCorrect = isCorrect, TimedOut = timedOut, AnsweredAt = now,
            ResponseMs = (int)Math.Clamp((now - startsAt).TotalMilliseconds, 0, (endsAt - startsAt).TotalMilliseconds + settings.GraceMs),
        };
        db.Answers.Add(answer);
        ctx.Answers.Add(answer);

        if (isCorrect)
        {
            ctx.Player.CorrectCount++;
            await bank.RecordCorrectAsync(question.Id, ct);
            if (position >= settings.QuestionCount)
            {
                Finalize(ctx, MatchStatus.Finished, MatchEndReasons.Completed, now);
            }
            else
            {
                ctx.Match.CurrentPosition++;
                var next = CurrentQuestion(ctx);
                await ServeAsync(userId, next, now.AddMilliseconds(settings.ResultDisplayMs));
            }
        }
        else if (!ctx.Player.ContinueUsed)
        {
            // Yanlış: bir kez reklamla devam teklifi (istemci reklam hazır değilse hemen "vazgeç" der)
            ctx.Match.Status = MatchStatus.AwaitingContinue;
            ctx.Match.ContinueDeadline = now.AddSeconds(settings.ContinueWindowSeconds);
        }
        else
        {
            Finalize(ctx, MatchStatus.Finished, MatchEndReasons.Eliminated, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return MatchResult<AnswerOutcome>.Fail("ALREADY_ANSWERED", "Bu soru zaten cevaplandı.", 409);
        }

        return MatchResult<AnswerOutcome>.Ok(new AnswerOutcome(position, isCorrect, timedOut, correctDisplay,
            answer.ChosenDisplay, answer.SecondDisplay, question.Explanation, await BuildStateAsync(ctx, ct)));
    }

    // ============================================================ Joker

    public async Task<MatchResult<JokerOutcome>> UseJokerAsync(Guid userId, Guid matchId, int position, JokerType type, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(matchId, ct);
        var ctx = await LoadAsync(userId, matchId, ct);
        if (ctx is null) return MatchResult<JokerOutcome>.Fail("MATCH_NOT_FOUND", "Maç bulunamadı.", 404);

        await ApplyExpiryAsync(ctx, ct);
        if (ctx.Match.Status != MatchStatus.Running) return MatchResult<JokerOutcome>.Fail("MATCH_NOT_RUNNING", "Bu maç artık oynanmıyor.", 409);
        if (position != ctx.Match.CurrentPosition) return MatchResult<JokerOutcome>.Fail("WRONG_POSITION", "Sıradaki soru bu değil.", 409);

        var current = CurrentQuestion(ctx);
        var now = Now;
        if (now < current.StartsAt!.Value.AddMilliseconds(-1000)) return MatchResult<JokerOutcome>.Fail("TOO_EARLY", "Soru henüz başlamadı.", 409);
        if (now > current.EndsAt!.Value.AddMilliseconds(settings.GraceMs)) return MatchResult<JokerOutcome>.Fail("QUESTION_TIMED_OUT", "Bu sorunun süresi doldu.", 409);
        if (ctx.Answers.Any(a => a.Position == current.Position && a.Attempt == current.Attempt))
            return MatchResult<JokerOutcome>.Fail("ALREADY_ANSWERED", "Bu soru zaten cevaplandı.", 409);

        // Aynı jokere tekrar gelen istek: kayıtlı sonuç, ikinci kez harcamadan döner
        var sameQuestionJoker = ctx.Jokers.FirstOrDefault(j => j.Position == current.Position && j.Attempt == current.Attempt);
        if (sameQuestionJoker is not null)
        {
            return sameQuestionJoker.Type == type
                ? MatchResult<JokerOutcome>.Ok(new JokerOutcome(ToView(sameQuestionJoker), await BalancesAsync(userId, ct)))
                : MatchResult<JokerOutcome>.Fail("JOKER_ALREADY_USED_THIS_QUESTION", "Bir soruda en fazla bir joker kullanabilirsin.", 409);
        }

        if (ctx.Jokers.Any(j => j.Type == type))
            return MatchResult<JokerOutcome>.Fail("JOKER_TYPE_USED", "Bu jokeri bu maçta zaten kullandın.", 409);

        var spend = await wallet.TrySpendAsync(userId, type, matchId.ToString(), $"use:{matchId:N}:{(int)type}", ct);
        if (spend == SpendOutcome.Insufficient)
            return MatchResult<JokerOutcome>.Fail("NO_JOKER", "Bu jokerden hakkın kalmadı.", 409);

        var question = (await bank.GetByIdsAsync([current.QuestionId], ct))[0];
        var correctDisplay = OptionShuffler.ToDisplay(OptionShuffler.Parse(current.OptionOrder), question.CorrectIndex);
        var result = type switch
        {
            JokerType.Half => JsonSerializer.Serialize(new { removed = JokerRules.Half(correctDisplay, random) }),
            JokerType.Hint => JsonSerializer.Serialize(new { hint = JokerRules.Hint(correctDisplay, settings.HintCorrectProbability, random) }),
            _ => "{}",
        };

        var joker = new MatchJoker
        {
            MatchId = matchId, UserId = userId, Type = type, Position = current.Position, Attempt = current.Attempt,
            ResultJson = result, UsedAt = now,
        };
        db.Jokers.Add(joker);
        await db.SaveChangesAsync(ct);
        return MatchResult<JokerOutcome>.Ok(new JokerOutcome(ToView(joker), await BalancesAsync(userId, ct)));
    }

    // ============================================================ Reklamla devam / vazgeç / pes

    public async Task<MatchResult<MatchState>> ContinueAsync(Guid userId, Guid matchId, string? adIntentId, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(matchId, ct);
        var ctx = await LoadAsync(userId, matchId, ct);
        if (ctx is null) return MatchResult<MatchState>.Fail("MATCH_NOT_FOUND", "Maç bulunamadı.", 404);

        await ApplyExpiryAsync(ctx, ct);
        if (ctx.Match.Status != MatchStatus.AwaitingContinue || ctx.Player.ContinueUsed)
            return MatchResult<MatchState>.Fail("CONTINUE_NOT_AVAILABLE", "Bu maçta devam hakkı yok.", 409);

        // Yerine kolay bir soru gelir; aynı maçta sorulmuş hiçbir soru tekrar edilmez
        var asked = ctx.Questions.Select(q => q.QuestionId).ToList();
        var category = ctx.Match.CategoryId is { } c ? (IReadOnlyCollection<int>)[c] : await catalog.GetPublishedCategoryIdsAsync(ctx.Match.ModeCode, ct);
        var picked = await bank.PickAsync(userId, category, [(Difficulty.Easy, 1)], asked, ct);
        if (picked.PoolTooSmall)
            return MatchResult<MatchState>.Fail("NO_REPLACEMENT_QUESTION", "Yerine koyacak yeni soru bulunamadı.", 409);

        // Reklam doğrulaması (Faz 7'ye kadar yalnızca test kapısı)
        if (!await adGate.TryConsumeAsync(userId, adIntentId, "continue", ct))
            return MatchResult<MatchState>.Fail("AD_NOT_VERIFIED", "Reklam doğrulanamadı.", 403);

        var now = Now;
        var failed = CurrentQuestion(ctx);
        var replacement = new MatchQuestion
        {
            MatchId = matchId, Position = failed.Position, Attempt = failed.Attempt + 1, QuestionId = picked.Questions[0].Id, Slot = Difficulty.Easy,
            OptionOrder = OptionShuffler.Serialize(OptionShuffler.Shuffle(random)),
        };
        db.Questions.Add(replacement);
        ctx.Questions.Add(replacement);
        ctx.Player.ContinueUsed = true;
        ctx.Match.Status = MatchStatus.Running;
        ctx.Match.ContinueDeadline = null;
        await ServeAsync(userId, replacement, now.AddMilliseconds(settings.FirstQuestionLeadMs));
        await db.SaveChangesAsync(ct);
        return MatchResult<MatchState>.Ok(await BuildStateAsync(ctx, ct));
    }

    public async Task<MatchResult<MatchState>> DeclineContinueAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(matchId, ct);
        var ctx = await LoadAsync(userId, matchId, ct);
        if (ctx is null) return MatchResult<MatchState>.Fail("MATCH_NOT_FOUND", "Maç bulunamadı.", 404);

        if (ctx.Match.Status == MatchStatus.AwaitingContinue)
        {
            Finalize(ctx, MatchStatus.Finished, MatchEndReasons.Eliminated, Now);
            await db.SaveChangesAsync(ct);
        }

        // Zaten bitmişse (örn. süre dolup kesinleşmiş) mevcut sonuç aynen döner
        return ctx.Match.Status is MatchStatus.Running
            ? MatchResult<MatchState>.Fail("MATCH_NOT_AWAITING", "Bu maçta bekleyen bir devam teklifi yok.", 409)
            : MatchResult<MatchState>.Ok(await BuildStateAsync(ctx, ct));
    }

    public async Task<MatchResult<MatchState>> ForfeitAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(matchId, ct);
        var ctx = await LoadAsync(userId, matchId, ct);
        if (ctx is null) return MatchResult<MatchState>.Fail("MATCH_NOT_FOUND", "Maç bulunamadı.", 404);

        await ApplyExpiryAsync(ctx, ct);
        if (IsActive(ctx.Match))
        {
            // Pes: o ana kadarki doğru sayısına göre puan (docs/01 §5). Devam beklerken pes = vazgeçmek.
            var awaiting = ctx.Match.Status == MatchStatus.AwaitingContinue;
            Finalize(ctx, awaiting ? MatchStatus.Finished : MatchStatus.Forfeited, awaiting ? MatchEndReasons.Eliminated : MatchEndReasons.Forfeit, Now);
            await db.SaveChangesAsync(ct);
        }

        return MatchResult<MatchState>.Ok(await BuildStateAsync(ctx, ct));
    }

    // ============================================================ Özet / bildirme

    public async Task<MatchResult<MatchSummary>> GetSummaryAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(matchId, ct);
        var ctx = await LoadAsync(userId, matchId, ct);
        if (ctx is null) return MatchResult<MatchSummary>.Fail("MATCH_NOT_FOUND", "Maç bulunamadı.", 404);

        await ApplyExpiryAsync(ctx, ct);
        if (IsActive(ctx.Match)) return MatchResult<MatchSummary>.Fail("MATCH_NOT_FINISHED", "Maç bitmeden özet görülemez.", 409);

        var served = ctx.Questions.Where(q => q.StartsAt is not null).OrderBy(q => q.Position).ThenBy(q => q.Attempt).ToList();
        var rows = await bank.GetByIdsAsync(served.Select(q => q.QuestionId).Distinct().ToList(), ct);
        var byId = rows.ToDictionary(r => r.Id);

        var items = served.Select(mq =>
        {
            var q = byId[mq.QuestionId];
            var order = OptionShuffler.Parse(mq.OptionOrder);
            var answer = ctx.Answers.FirstOrDefault(a => a.Position == mq.Position && a.Attempt == mq.Attempt);
            var joker = ctx.Jokers.FirstOrDefault(j => j.Position == mq.Position && j.Attempt == mq.Attempt);
            var latest = ctx.Questions.Where(x => x.Position == mq.Position).Max(x => x.Attempt);
            return new SummaryItem(mq.Position, q.Text, order.Select(o => q.Options[o]).ToList(), OptionShuffler.ToDisplay(order, q.CorrectIndex),
                answer?.ChosenDisplay, answer?.SecondDisplay, answer?.IsCorrect ?? false, answer?.TimedOut ?? false, q.Explanation,
                joker is null ? null : JokerRules.ToCode(joker.Type), mq.Attempt < latest);
        }).ToList();

        return MatchResult<MatchSummary>.Ok(new MatchSummary(matchId, ctx.Match.ModeCode, ctx.Match.CategoryId, ToStatusText(ctx.Match.Status),
            ctx.Player.Result ?? "", ctx.Player.CorrectCount, ctx.Player.Points, items));
    }

    public async Task<MatchResult<bool>> ReportAsync(Guid userId, Guid matchId, int position, string? reason, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(userId, matchId, ct);
        if (ctx is null) return MatchResult<bool>.Fail("MATCH_NOT_FOUND", "Maç bulunamadı.", 404);

        // Yalnızca kullanıcıya gösterilmiş bir soru bildirilebilir
        var mq = ctx.Questions.Where(q => q.Position == position && q.StartsAt != null && q.StartsAt <= Now)
            .OrderByDescending(q => q.Attempt).FirstOrDefault();
        if (mq is null) return MatchResult<bool>.Fail("QUESTION_NOT_SHOWN", "Bu soru henüz gösterilmedi.", 409);

        return await bank.ReportAsync(userId, mq.QuestionId, reason, ct) switch
        {
            ReportOutcome.Reported or ReportOutcome.AlreadyReported => MatchResult<bool>.Ok(true),
            ReportOutcome.ReasonInvalid => MatchResult<bool>.Fail("REASON_INVALID", "Bildirim gerekçesi 1–300 karakter olmalı."),
            _ => MatchResult<bool>.Fail("QUESTION_NOT_FOUND", "Soru bulunamadı.", 404),
        };
    }

    public async Task<JokerBalances> GetJokerBalancesAsync(Guid userId, CancellationToken ct = default) => await BalancesAsync(userId, ct);

    /// <summary>Süresi geçmiş devam tekliflerini kesinleştirir, terk edilmiş maçları iptal eder (arka plan işi / Faz 5 için).</summary>
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var ids = await db.Matches.Where(m => m.Status == MatchStatus.AwaitingContinue || m.Status == MatchStatus.Running).Select(m => m.Id).ToListAsync(ct);
        var changed = 0;
        foreach (var id in ids)
        {
            using var _ = await locks.AcquireAsync(id, ct);
            var userId = await db.Players.Where(p => p.MatchId == id).Select(p => p.UserId).FirstAsync(ct);
            var ctx = await LoadAsync(userId, id, ct);
            if (ctx is not null && await ApplyExpiryAsync(ctx, ct)) changed++;
        }

        return changed;
    }

    // ============================================================ İç yardımcılar

    private sealed class Ctx(Match match, MatchPlayer player, List<MatchQuestion> questions, List<MatchAnswer> answers, List<MatchJoker> jokers)
    {
        public Match Match { get; } = match;
        public MatchPlayer Player { get; } = player;
        public List<MatchQuestion> Questions { get; } = questions;
        public List<MatchAnswer> Answers { get; } = answers;
        public List<MatchJoker> Jokers { get; } = jokers;
    }

    private static bool IsActive(Match m) => m.Status is MatchStatus.Running or MatchStatus.AwaitingContinue;

    private async Task<Ctx?> LoadAsync(Guid userId, Guid matchId, CancellationToken ct)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.MatchId == matchId && p.UserId == userId, ct);
        if (player is null) return null;
        var match = await db.Matches.FirstAsync(m => m.Id == matchId, ct);
        return new Ctx(match, player,
            await db.Questions.Where(q => q.MatchId == matchId).ToListAsync(ct),
            await db.Answers.Where(a => a.MatchId == matchId && a.UserId == userId).ToListAsync(ct),
            await db.Jokers.Where(j => j.MatchId == matchId && j.UserId == userId).ToListAsync(ct));
    }

    private async Task<Ctx?> FindActiveAsync(Guid userId, CancellationToken ct)
    {
        var matchId = await (from p in db.Players join m in db.Matches on p.MatchId equals m.Id
                             where p.UserId == userId && (m.Status == MatchStatus.Running || m.Status == MatchStatus.AwaitingContinue)
                             orderby m.StartedAt descending select (Guid?)m.Id).FirstOrDefaultAsync(ct);
        return matchId is null ? null : new Ctx(await db.Matches.FirstAsync(m => m.Id == matchId, ct), null!, [], [], []);
    }

    private static MatchQuestion CurrentQuestion(Ctx ctx) =>
        ctx.Questions.Where(q => q.Position == ctx.Match.CurrentPosition).OrderByDescending(q => q.Attempt).First();

    /// <summary>Soruyu kullanıcıya "gösterildi" olarak işaretler: başlangıç/bitiş anı yazılır, görülenlere eklenir.</summary>
    private async Task ServeAsync(Guid userId, MatchQuestion mq, DateTime startsAt)
    {
        mq.StartsAt = startsAt;
        mq.EndsAt = startsAt.AddSeconds(settings.SecondsPerQuestion);
        await bank.MarkShownAsync(userId, mq.QuestionId);
    }

    private void Finalize(Ctx ctx, MatchStatus status, string reason, DateTime now)
    {
        ctx.Match.Status = status;
        ctx.Match.EndedAt = now;
        ctx.Match.ContinueDeadline = null;
        ctx.Player.Result = reason;
        ctx.Player.Points = status == MatchStatus.Cancelled ? 0 : ScoringRules.SoloPoints(ctx.Player.CorrectCount, settings);
        ctx.Player.SettledAt = now;
    }

    /// <summary>Süre kurallarını tembel uygular: devam penceresi kapandıysa elen, soru bitişinden çok sonra hâlâ ses yoksa iptal et.</summary>
    private async Task<bool> ApplyExpiryAsync(Ctx ctx, CancellationToken ct)
    {
        var now = Now;
        var changed = false;
        if (ctx.Match.Status == MatchStatus.AwaitingContinue && ctx.Match.ContinueDeadline is { } deadline && now > deadline)
        {
            Finalize(ctx, MatchStatus.Finished, MatchEndReasons.Eliminated, deadline);
            changed = true;
        }
        else if (ctx.Match.Status == MatchStatus.Running)
        {
            var current = CurrentQuestion(ctx);
            var answered = ctx.Answers.Any(a => a.Position == current.Position && a.Attempt == current.Attempt);
            if (!answered && current.EndsAt is { } ends && now > ends.AddSeconds(settings.AbandonAfterSeconds))
            {
                // Bağlantı kopması/terk: bireysel maç iptal, puan değişmez (docs/01 §5)
                Finalize(ctx, MatchStatus.Cancelled, MatchEndReasons.Abandoned, now);
                changed = true;
            }
        }

        if (changed) await db.SaveChangesAsync(ct);
        return changed;
    }

    private async Task<MatchState> BuildStateAsync(Ctx ctx, CancellationToken ct)
    {
        var m = ctx.Match;
        QuestionView? view = null;
        ActiveJokerView? active = null;

        if (m.Status == MatchStatus.Running)
        {
            var current = CurrentQuestion(ctx);
            var q = (await bank.GetByIdsAsync([current.QuestionId], ct))[0];
            var order = OptionShuffler.Parse(current.OptionOrder);
            view = new QuestionView(current.Position, settings.QuestionCount, q.Text, order.Select(o => q.Options[o]).ToList(), current.StartsAt!.Value, current.EndsAt!.Value);
            if (ctx.Jokers.FirstOrDefault(j => j.Position == current.Position && j.Attempt == current.Attempt) is { } joker) active = ToView(joker);
        }

        var result = IsActive(m) ? null
            : new MatchResultView(ToStatusText(m.Status), ctx.Player.Result ?? "", ctx.Player.CorrectCount, ctx.Player.Points, ctx.Player.SettledAt is not null);

        return new MatchState(m.Id, m.ModeCode, m.CategoryId, ToStatusText(m.Status), m.CurrentPosition, settings.QuestionCount, ctx.Player.CorrectCount,
            view, active, ctx.Jokers.Select(j => JokerRules.ToCode(j.Type)).ToList(), await BalancesAsync(ctx.Player.UserId, ct),
            m.Status == MatchStatus.AwaitingContinue ? new ContinueOfferView(m.ContinueDeadline!.Value) : null, result, Now);
    }

    private async Task<AnswerOutcome> OutcomeFromStoredAsync(Ctx ctx, int position, MatchAnswer answer, CancellationToken ct)
    {
        var mq = ctx.Questions.First(q => q.Position == position && q.Attempt == answer.Attempt);
        var question = (await bank.GetByIdsAsync([mq.QuestionId], ct))[0];
        var correctDisplay = OptionShuffler.ToDisplay(OptionShuffler.Parse(mq.OptionOrder), question.CorrectIndex);
        return new AnswerOutcome(position, answer.IsCorrect, answer.TimedOut, correctDisplay, answer.ChosenDisplay, answer.SecondDisplay,
            question.Explanation, await BuildStateAsync(ctx, ct));
    }

    private async Task<JokerBalances> BalancesAsync(Guid userId, CancellationToken ct)
    {
        var b = await wallet.GetBalancesAsync(userId, ct);
        return new JokerBalances(b[JokerType.Half], b[JokerType.Double], b[JokerType.Hint]);
    }

    private static ActiveJokerView ToView(MatchJoker joker)
    {
        using var doc = JsonDocument.Parse(joker.ResultJson);
        IReadOnlyList<int>? removed = doc.RootElement.TryGetProperty("removed", out var r) ? r.EnumerateArray().Select(x => x.GetInt32()).ToList() : null;
        int? hint = doc.RootElement.TryGetProperty("hint", out var h) ? h.GetInt32() : null;
        return new ActiveJokerView(JokerRules.ToCode(joker.Type), removed, hint);
    }

    private static string ToStatusText(MatchStatus status) => status switch
    {
        MatchStatus.Running => "running",
        MatchStatus.AwaitingContinue => "awaiting_continue",
        MatchStatus.Finished => "finished",
        MatchStatus.Cancelled => "cancelled",
        MatchStatus.Forfeited => "forfeited",
        _ => "unknown",
    };

    private async Task<(string ModeCode, int? CategoryId, IReadOnlyCollection<int> CategoryIds, (string Code, string Message, int Status)? Error)> ResolveScopeAsync(
        string? modeCode, int? categoryId, CancellationToken ct)
    {
        string[] modes = [ModeCodes.General, ModeCodes.Branch, ModeCodes.Kpss, ModeCodes.Fan];
        if (modeCode is null || !modes.Contains(modeCode)) return ("", null, [], ("MODE_INVALID", "Geçersiz oyun modu.", 400));

        if (modeCode == ModeCodes.General)
        {
            var general = await catalog.GetCategoryAsync(1, ct);
            return general is { IsPublished: true }
                ? (modeCode, general.Id, [general.Id], null)
                : (modeCode, null, [], ("MODE_UNAVAILABLE", "Bu mod henüz kullanıma açılmadı.", 409));
        }

        if (categoryId is { } id)
        {
            var category = await catalog.GetCategoryAsync(id, ct);
            if (category is null || category.ModeCode != modeCode || !category.IsPublished)
                return (modeCode, null, [], ("CATEGORY_UNAVAILABLE", "Bu kategori bulunamadı ya da kullanıma açık değil.", 404));
            return (modeCode, id, [id], null);
        }

        // "Karışık": o moddaki yayındaki tüm alt dallar
        var published = await catalog.GetPublishedCategoryIdsAsync(modeCode, ct);
        return published.Count == 0
            ? (modeCode, null, [], ("MODE_UNAVAILABLE", "Bu modda henüz yayında alt dal yok.", 409))
            : (modeCode, null, published, null);
    }
}
