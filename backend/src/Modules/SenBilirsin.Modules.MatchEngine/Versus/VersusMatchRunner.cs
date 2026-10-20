using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SenBilirsin.Modules.MatchEngine.Application;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.MatchEngine.Versus;

/// <summary>
/// Süren bir rakipli maçı bellekte yürüten "actor" (docs/02 §3: tek instance, durum bellekte; kalıcı kayıt <see cref="VersusStore"/>).
/// Tüm durum değişiklikleri tek kilit altında sıraya girer; zamanlayıcılar <see cref="TimeProvider"/> ile kurulur (testte sahte saat).
/// Süreyi sunucu ölçer; doğru cevap bir sorunun sonucu açıklanana kadar istemciye gitmez.
/// </summary>
public sealed class VersusMatchRunner
{
    private enum Phase { Countdown, Question, Between, Over }

    private sealed class PlayerState(VersusPlayerSpec spec, double botBias)
    {
        public VersusPlayerSpec Spec { get; } = spec;
        public double BotBias { get; } = botBias;
        public bool Connected { get; set; } = true;
        public int Correct { get; set; }
        public int DisconnectUsedMs { get; set; }

        /// <summary>Maç duraklamışken bu oyuncu kopuk olduğu andan beri havuz harcıyor.</summary>
        public DateTime? PauseStartedAt { get; set; }

        public int PoolToken { get; set; }
        public HashSet<JokerType> UsedJokers { get; } = [];
        public Guid Id => Spec.Id;
        public bool IsBot => Spec.IsBot;
    }

    private sealed record RoundAnswer(int? Option, int? Second, bool Correct, bool TimedOut, int ResponseMs, DateTime At);

    private sealed class Round(int position, BankQuestion question, int[] order, DateTime startsAt, DateTime endsAt)
    {
        public int Position { get; } = position;
        public BankQuestion Question { get; } = question;
        public int[] Order { get; } = order;
        public DateTime StartsAt { get; } = startsAt;
        public DateTime EndsAt { get; } = endsAt;
        public int CorrectDisplay { get; } = OptionShuffler.ToDisplay(order, question.CorrectIndex);
        public Dictionary<Guid, RoundAnswer> Answers { get; } = [];
        public Dictionary<Guid, (JokerType Type, JokerGrant Grant)> Jokers { get; } = [];
        public bool Revealed { get; set; }
        public List<(Guid Player, int[] Removed)> Removed { get; } = [];
    }

    private readonly VersusMatchSpec _spec;
    private readonly IServiceScopeFactory _scopes;
    private readonly IMatchNotifier _notifier;
    private readonly TimeProvider _clock;
    private readonly Random _random;
    private readonly MatchSettings _match;
    private readonly VersusSettings _versus;
    private readonly ILogger _log;
    private readonly Action<VersusMatchRunner> _onEnded;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ITimer> _timers = [];
    private readonly ConcurrentDictionary<Task, byte> _pending = new();
    private readonly PlayerState _a;
    private readonly PlayerState _b;
    private readonly List<int[]> _orders;

    private Phase _phase = Phase.Countdown;
    private Round? _round;
    private int _position;
    private bool _paused;
    private Func<Task>? _pendingNext;
    private DateTime _pendingAt;
    private int _nextToken;

    public VersusMatchRunner(VersusMatchSpec spec, IReadOnlyList<int[]> orders, IServiceScopeFactory scopes, IMatchNotifier notifier,
        TimeProvider clock, Random random, MatchSettings match, VersusSettings versus, ILogger log, Action<VersusMatchRunner> onEnded)
    {
        _spec = spec;
        _orders = orders.ToList();
        _scopes = scopes;
        _notifier = notifier;
        _clock = clock;
        _random = random;
        _match = match;
        _versus = versus;
        _log = log;
        _onEnded = onEnded;
        _a = new PlayerState(spec.A, spec.A.IsBot ? BotBrain.NewMatchBias(random) : 0);
        _b = new PlayerState(spec.B, spec.B.IsBot ? BotBrain.NewMatchBias(random) : 0);
    }

    public Guid MatchId => _spec.MatchId;
    public VersusMatchSpec Spec => _spec;

    /// <summary>Maç normal yoldan (cevaplar/pes/kopma kaybı) bitti; iptal edilmediyse true.</summary>
    public bool FinishedNormally { get; private set; }
    public bool IsOver => _phase == Phase.Over;
    public IReadOnlyList<Guid> HumanIds => new[] { _a, _b }.Where(p => !p.IsBot).Select(p => p.Id).ToList();
    public bool HasPlayer(Guid userId) => _a.Id == userId || _b.Id == userId;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private IEnumerable<PlayerState> Players => [_a, _b];
    private IEnumerable<PlayerState> Humans => Players.Where(p => !p.IsBot);
    private PlayerState Me(Guid userId) => _a.Id == userId ? _a : _b;
    private PlayerState Other(PlayerState p) => ReferenceEquals(p, _a) ? _b : _a;

    // ================================================================ Başlangıç

    /// <summary>Geri sayımı başlatır (5 sn), sonra ilk soru gelir.</summary>
    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var startsAt = Now.AddSeconds(_versus.CountdownSeconds);
            foreach (var p in Humans)
                await SendAsync(p, MatchEvents.CountdownStarted, new { matchId = MatchId, startsAt, seconds = _versus.CountdownSeconds, serverNow = Now });
            ScheduleNext(() => StartRoundAsync(1), startsAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ================================================================ Soru döngüsü

    private async Task StartRoundAsync(int position)
    {
        var now = Now;
        var q = _spec.Questions[position - 1];
        _position = position;
        _round = new Round(position, q, _orders[position - 1], now, now.AddSeconds(_match.SecondsPerQuestion));
        _phase = Phase.Question;

        foreach (var p in Humans)
        {
            var balances = await BalancesAsync(p.Id);
            await using (var scope = _scopes.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IQuestionBank>().MarkShownAsync(p.Id, q.Id);
            await SendAsync(p, MatchEvents.QuestionStarted, QuestionPayload(p, balances));
        }

        foreach (var bot in Players.Where(p => p.IsBot))
        {
            var think = BotBrain.ThinkTimeMs(bot.Spec.Bot!.SpeedMinMs, bot.Spec.Bot.SpeedMaxMs, _random);
            if (think < _match.SecondsPerQuestion * 1000)
            {
                var botId = bot.Id;
                At(now.AddMilliseconds(think), () => BotAnswerAsync(position, botId));
            }
        }

        At(_round.EndsAt.AddMilliseconds(_match.GraceMs), () => RevealAsync(position));
    }

    private object QuestionPayload(PlayerState p, JokerBalances balances)
    {
        var r = _round!;
        return new
        {
            matchId = MatchId, position = r.Position, total = _spec.Questions.Count, text = r.Question.Text,
            options = r.Order.Select(o => r.Question.Options[o]).ToList(), startsAt = r.StartsAt, endsAt = r.EndsAt, serverNow = Now,
            usedJokerTypes = p.UsedJokers.Select(JokerRules.ToCode).ToList(), jokers = balances,
            activeJoker = r.Jokers.TryGetValue(p.Id, out var j) ? j.Grant : null,
            yourScore = p.Correct, opponentScore = Other(p).Correct,
        };
    }

    private async Task BotAnswerAsync(int position, Guid botId)
    {
        var r = _round;
        if (r is null || r.Position != position || r.Revealed || _phase != Phase.Question) return;
        var bot = Me(botId);
        var p = BotBrain.CorrectProbability(bot.Spec.Bot!.Power, r.Question.Difficulty, bot.BotBias);
        var option = BotBrain.ChooseOption(r.CorrectDisplay, p, _random);
        await RecordAnswerAsync(bot, r, option, null, Now);
    }

    private async Task RecordAnswerAsync(PlayerState p, Round r, int? option, int? second, DateTime at)
    {
        var timedOut = option is null || at > r.EndsAt.AddMilliseconds(_match.GraceMs);
        var doubleActive = r.Jokers.TryGetValue(p.Id, out var j) && j.Type == JokerType.Double;
        var correct = !timedOut && JokerRules.IsCorrect(r.CorrectDisplay, option, doubleActive ? second : null);
        r.Answers[p.Id] = new RoundAnswer(timedOut ? null : option, timedOut || !doubleActive ? null : second, correct, timedOut,
            (int)Math.Clamp((at - r.StartsAt).TotalMilliseconds, 0, (r.EndsAt - r.StartsAt).TotalMilliseconds + _match.GraceMs), at);

        var other = Other(p);
        if (!other.IsBot) await SendAsync(other, MatchEvents.OpponentAnswered, new { position = r.Position });

        // İki taraf da cevapladıysa kısa bir gecikmeyle sonuç gösterilir (docs/01 §4)
        if (Players.All(x => r.Answers.ContainsKey(x.Id)))
            At(at.AddMilliseconds(_versus.RevealDelayMs), () => RevealAsync(r.Position));
    }

    private async Task RevealAsync(int position)
    {
        var r = _round;
        if (r is null || r.Position != position || r.Revealed || _phase != Phase.Question) return;
        r.Revealed = true;
        _phase = Phase.Between;

        var answerRows = new List<MatchAnswer>();
        foreach (var p in Players)
        {
            if (!r.Answers.TryGetValue(p.Id, out var a))
            {
                a = new RoundAnswer(null, null, false, true, _match.SecondsPerQuestion * 1000, r.EndsAt);
                r.Answers[p.Id] = a;
            }

            if (a.Correct) p.Correct++;
            answerRows.Add(new MatchAnswer
            {
                MatchId = MatchId, UserId = p.Id, Position = position, Attempt = 0, ChosenDisplay = a.Option, SecondDisplay = a.Second,
                IsCorrect = a.Correct, TimedOut = a.TimedOut, ResponseMs = a.ResponseMs, AnsweredAt = a.At,
            });
            if (a.Correct && !p.IsBot)
                await using (var scope = _scopes.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<IQuestionBank>().RecordCorrectAsync(r.Question.Id);
        }

        await using (var scope = _scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<VersusStore>().SaveRoundAsync(MatchId, position, r.StartsAt, r.EndsAt, answerRows);

        foreach (var p in Humans)
        {
            var mine = r.Answers[p.Id];
            var theirs = r.Answers[Other(p).Id];
            await SendAsync(p, MatchEvents.QuestionResult, new
            {
                position, correctOption = r.CorrectDisplay, explanation = r.Question.Explanation,
                yourOption = mine.Option, yourSecond = mine.Second, yourCorrect = mine.Correct, yourTimedOut = mine.TimedOut,
                opponentOption = theirs.Option, opponentCorrect = theirs.Correct,
                yourScore = p.Correct, opponentScore = Other(p).Correct, serverNow = Now,
            });
        }

        var at = Now.AddMilliseconds(_match.ResultDisplayMs);
        ScheduleNext(position >= _spec.Questions.Count ? () => FinishAsync(null, MatchEndReasonsVersus.Normal) : () => StartRoundAsync(position + 1), at);
    }

    // ================================================================ Oyuncu eylemleri

    public async Task<MatchResult<bool>> SubmitAnswerAsync(Guid userId, int position, int? option, int? second)
    {
        await _gate.WaitAsync();
        try
        {
            if (!HasPlayer(userId)) return MatchResult<bool>.Fail("NOT_IN_MATCH", "Bu maçta değilsin.", 404);
            var p = Me(userId);
            var r = _round;
            if (_phase == Phase.Over) return MatchResult<bool>.Fail("MATCH_NOT_RUNNING", "Bu maç artık oynanmıyor.", 409);
            if (r is null || _phase != Phase.Question || r.Position != position)
                return MatchResult<bool>.Fail("WRONG_POSITION", "Sıradaki soru bu değil.", 409);
            if (r.Answers.ContainsKey(userId)) return MatchResult<bool>.Ok(true); // ilk dokunuş kesin: tekrar yok sayılır
            if (!JokerRules.IsValidIndex(option) || !JokerRules.IsValidIndex(second))
                return MatchResult<bool>.Fail("OPTION_INVALID", "Şık 0–3 arasında olmalı.");

            var now = Now;
            var doubleActive = r.Jokers.TryGetValue(userId, out var j) && j.Type == JokerType.Double;
            if (second is not null && !doubleActive) return MatchResult<bool>.Fail("DOUBLE_NOT_ACTIVE", "Çift cevap jokeri bu soruda kullanılmadı.");
            if (second is not null && second == option) return MatchResult<bool>.Fail("OPTION_INVALID", "İki şık farklı olmalı.");
            if (option is null && now < r.EndsAt.AddMilliseconds(-300)) return MatchResult<bool>.Fail("TOO_EARLY", "Süre henüz dolmadı.", 409);

            await RecordAnswerAsync(p, r, option, second, now);
            return MatchResult<bool>.Ok(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MatchResult<JokerOutcome>> UseJokerAsync(Guid userId, int position, JokerType type)
    {
        await _gate.WaitAsync();
        try
        {
            if (!HasPlayer(userId)) return MatchResult<JokerOutcome>.Fail("NOT_IN_MATCH", "Bu maçta değilsin.", 404);
            var p = Me(userId);
            var r = _round;
            if (_phase != Phase.Question || r is null || r.Position != position)
                return MatchResult<JokerOutcome>.Fail("WRONG_POSITION", "Sıradaki soru bu değil.", 409);
            if (Now > r.EndsAt.AddMilliseconds(_match.GraceMs)) return MatchResult<JokerOutcome>.Fail("QUESTION_TIMED_OUT", "Bu sorunun süresi doldu.", 409);
            if (r.Answers.ContainsKey(userId)) return MatchResult<JokerOutcome>.Fail("ALREADY_ANSWERED", "Bu soru zaten cevaplandı.", 409);

            if (r.Jokers.TryGetValue(userId, out var existing))
            {
                return existing.Type == type
                    ? MatchResult<JokerOutcome>.Ok(new JokerOutcome(ToView(existing.Grant), await BalancesAsync(userId)))
                    : MatchResult<JokerOutcome>.Fail("JOKER_ALREADY_USED_THIS_QUESTION", "Bir soruda en fazla bir joker kullanabilirsin.", 409);
            }

            if (p.UsedJokers.Contains(type)) return MatchResult<JokerOutcome>.Fail("JOKER_TYPE_USED", "Bu jokeri bu maçta zaten kullandın.", 409);

            await using var scope = _scopes.CreateAsyncScope();
            var wallet = scope.ServiceProvider.GetRequiredService<IJokerWallet>();
            var spend = await wallet.TrySpendAsync(userId, type, MatchId.ToString(), $"use:{MatchId:N}:{userId:N}:{(int)type}");
            if (spend == SpendOutcome.Insufficient) return MatchResult<JokerOutcome>.Fail("NO_JOKER", "Bu jokerden hakkın kalmadı.", 409);

            JokerGrant grant = type switch
            {
                JokerType.Half => new JokerGrant("half", null, JokerRules.Half(r.CorrectDisplay, _random)),
                JokerType.Hint => new JokerGrant("hint", JokerRules.Hint(r.CorrectDisplay, _match.HintCorrectProbability, _random), null),
                _ => new JokerGrant("double", null, null),
            };
            r.Jokers[userId] = (type, grant);
            p.UsedJokers.Add(type);

            var json = JsonSerializer.Serialize(new { removed = grant.Removed, hint = grant.Hint });
            await scope.ServiceProvider.GetRequiredService<VersusStore>().SaveJokerAsync(VersusStore.NewJoker(MatchId, userId, type, position, json, Now));

            var balances = await BalancesAsync(userId);
            // Joker yalnızca kullanana bildirilir; rakip görmez (docs/01 §10)
            await SendAsync(p, MatchEvents.JokerResult, new { position, joker = grant, jokers = balances });
            return MatchResult<JokerOutcome>.Ok(new JokerOutcome(ToView(grant), balances));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ActiveJokerView ToView(JokerGrant g) => new(g.Type, g.Removed, g.Hint);

    /// <summary>Pes: anında kayıp (docs/01 §11).</summary>
    public async Task ForfeitAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!HasPlayer(userId) || _phase == Phase.Over) return;
            await FinishAsync(Me(userId), MatchEndReasonsVersus.Forfeit);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ================================================================ Kopma ve yeniden bağlanma

    public async Task PlayerDisconnectedAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!HasPlayer(userId) || _phase == Phase.Over) return;
            var p = Me(userId);
            if (p.IsBot || !p.Connected) return;
            p.Connected = false;

            // İki gerçek oyuncunun ikisi de koptuysa maç iptal (docs/01 §11); botlu maçta tek insan koparsa havuz kuralı işler
            if (Humans.Count() == 2 && Humans.All(h => !h.Connected))
            {
                await CancelLockedAsync(MatchEndReasonsVersus.BothDisconnected);
                return;
            }

            var other = Other(p);
            if (!other.IsBot)
                await SendAsync(other, MatchEvents.OpponentDisconnected, new { remainingMs = PoolRemainingMs(p) });

            // Soru açıkken süre durmaz; sorular arasında maç duraklar ve havuz azalmaya başlar
            if (_phase is Phase.Between or Phase.Countdown) EnterPause();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Yeniden bağlanan oyuncuya gönderilecek tam durum özetini döndürür.</summary>
    public async Task<object?> PlayerReconnectedAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!HasPlayer(userId) || _phase == Phase.Over) return null;
            var p = Me(userId);
            if (p.Connected) return StateFor(p);
            p.Connected = true;

            if (p.PauseStartedAt is { } started)
            {
                p.DisconnectUsedMs = Math.Min(_versus.DisconnectPoolMs, p.DisconnectUsedMs + (int)(Now - started).TotalMilliseconds);
                p.PauseStartedAt = null;
                p.PoolToken++;
            }

            var other = Other(p);
            if (!other.IsBot) await SendAsync(other, MatchEvents.OpponentReconnected, new { });

            if (_paused && Humans.All(h => h.Connected))
            {
                _paused = false;
                var resumeAt = _pendingAt > Now ? _pendingAt : Now.AddSeconds(1);
                if (_pendingNext is not null) ScheduleNext(_pendingNext, resumeAt);
            }

            return StateFor(p);
        }
        finally
        {
            _gate.Release();
        }
    }

    private int PoolRemainingMs(PlayerState p)
    {
        var used = p.DisconnectUsedMs + (p.PauseStartedAt is { } s ? (int)(Now - s).TotalMilliseconds : 0);
        return Math.Max(0, _versus.DisconnectPoolMs - used);
    }

    private void EnterPause()
    {
        if (_paused) return;
        _paused = true;
        _nextToken++; // bekleyen "sıradaki adım" zamanlayıcısını geçersiz kıl
        foreach (var p in Humans.Where(h => !h.Connected))
        {
            p.PauseStartedAt ??= Now;
            var token = ++p.PoolToken;
            var id = p.Id;
            At(Now.AddMilliseconds(PoolRemainingMs(p)), () => PoolExpiredAsync(id, token));
        }

        foreach (var p in Humans.Where(h => h.Connected))
            _ = SendAsync(p, MatchEvents.OpponentDisconnected, new { remainingMs = PoolRemainingMs(Other(p)) });
    }

    private async Task PoolExpiredAsync(Guid userId, int token)
    {
        var p = Me(userId);
        if (p.Connected || p.PoolToken != token || _phase == Phase.Over) return;
        p.DisconnectUsedMs = _versus.DisconnectPoolMs;
        await FinishAsync(p, MatchEndReasonsVersus.Disconnect);
    }

    // ================================================================ Sonraki adım / duraklama

    /// <summary>Bir sonraki adımı (soru ya da bitiş) zamanlar; kopuk oyuncu varsa maç duraklar.</summary>
    private void ScheduleNext(Func<Task> next, DateTime at)
    {
        _pendingNext = next;
        _pendingAt = at;
        if (Humans.Any(h => !h.Connected))
        {
            _paused = false; // EnterPause yeniden kurar
            EnterPause();
            return;
        }

        var token = ++_nextToken;
        At(at, async () =>
        {
            if (token != _nextToken || _paused) return;
            _pendingNext = null;
            await next();
        });
    }

    // ================================================================ Bitiş

    private async Task FinishAsync(PlayerState? forfeiter, string reason)
    {
        if (_phase == Phase.Over) return;
        _phase = Phase.Over;
        FinishedNormally = true;
        DisposeTimers();

        var outcome = forfeiter is null
            ? VersusScoring.Compute(_a.Correct, _b.Correct, _versus)
            : VersusScoring.Forfeit(ReferenceEquals(forfeiter, _a), _a.Correct, _b.Correct, _versus);
        var winner = outcome.Winner(_a.Id, _b.Id);

        await using (var scope = _scopes.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<VersusStore>().FinishAsync(MatchId, MatchStatus.Finished, winner, new[]
            {
                new VersusStore.PlayerFinal(_a.Id, _a.Correct, outcome.PointsA, outcome.BonusA, outcome.ResultA, _a.DisconnectUsedMs),
                new VersusStore.PlayerFinal(_b.Id, _b.Correct, outcome.PointsB, outcome.BonusB, outcome.ResultB, _b.DisconnectUsedMs),
            });
        }

        // Kesinleşen puanı turnuvaya ilet (bot ve 0 puan hariç)
        await using (var creditScope = _scopes.CreateAsyncScope())
        {
            var sink = creditScope.ServiceProvider.GetRequiredService<IMatchPointsSink>();
            foreach (var p in Humans)
            {
                var points = ReferenceEquals(p, _a) ? outcome.PointsA : outcome.PointsB;
                if (points > 0) await sink.CreditAsync(MatchId, p.Id, points, Now);
            }
        }

        foreach (var p in Humans)
        {
            var mine = ReferenceEquals(p, _a) ? (outcome.ResultA, outcome.PointsA, outcome.BonusA) : (outcome.ResultB, outcome.PointsB, outcome.BonusB);
            await SendAsync(p, MatchEvents.MatchFinished, new
            {
                matchId = MatchId, result = mine.Item1, points = mine.Item2, bonus = mine.Item3, reason,
                yourCorrect = p.Correct, opponentCorrect = Other(p).Correct, total = _spec.Questions.Count,
                opponent = new { name = Other(p).Spec.Name, avatarId = Other(p).Spec.AvatarId, isBot = Other(p).IsBot },
            });
        }

        _onEnded(this);
    }

    /// <summary>Maçı iptal eder: puan yok (sunucu yeniden başladı, hazır olunmadı…). Dışarıdan çağrılır.</summary>
    public async Task CancelAsync(string reason)
    {
        await _gate.WaitAsync();
        try
        {
            await CancelLockedAsync(reason);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Kilit zaten alınmışken iptal (iki oyuncu birden koptu vb.).</summary>
    private async Task CancelLockedAsync(string reason)
    {
        if (_phase == Phase.Over) return;
        _phase = Phase.Over;
        DisposeTimers();
        await using (var scope = _scopes.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<VersusStore>().FinishAsync(MatchId, MatchStatus.Cancelled, null, new[]
            {
                new VersusStore.PlayerFinal(_a.Id, _a.Correct, 0, 0, MatchResults.Cancelled, _a.DisconnectUsedMs),
                new VersusStore.PlayerFinal(_b.Id, _b.Correct, 0, 0, MatchResults.Cancelled, _b.DisconnectUsedMs),
            });
        }

        foreach (var p in Humans) await SendAsync(p, MatchEvents.MatchCancelled, new { matchId = MatchId, reason });
        _onEnded(this);
    }

    // ================================================================ Durum özeti

    public async Task<object?> GetStateAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            return HasPlayer(userId) ? StateFor(Me(userId)) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private object StateFor(PlayerState p)
    {
        var r = _round;
        var other = Other(p);
        return new
        {
            matchId = MatchId, phase = _phase.ToString().ToLowerInvariant(), paused = _paused, position = _position, total = _spec.Questions.Count,
            yourScore = p.Correct, opponentScore = other.Correct, serverNow = Now,
            opponent = new { name = other.Spec.Name, avatarId = other.Spec.AvatarId, frameId = other.Spec.FrameId, isBot = other.IsBot },
            question = _phase == Phase.Question && r is not null
                ? new
                {
                    r.Position, text = r.Question.Text, options = r.Order.Select(o => r.Question.Options[o]).ToList(), startsAt = r.StartsAt, endsAt = r.EndsAt,
                    answered = r.Answers.ContainsKey(p.Id), activeJoker = r.Jokers.TryGetValue(p.Id, out var j) ? j.Grant : null,
                }
                : null,
            usedJokerTypes = p.UsedJokers.Select(JokerRules.ToCode).ToList(),
            opponentRemainingPoolMs = other.IsBot ? (int?)null : PoolRemainingMs(other), opponentConnected = other.Connected,
        };
    }

    // ================================================================ Yardımcılar

    private async Task<JokerBalances> BalancesAsync(Guid userId)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var b = await scope.ServiceProvider.GetRequiredService<IJokerWallet>().GetBalancesAsync(userId);
        return new JokerBalances(b[JokerType.Half], b[JokerType.Double], b[JokerType.Hint]);
    }

    private Task SendAsync(PlayerState p, string eventName, object payload) => p.IsBot ? Task.CompletedTask : SafeSendAsync(p.Id, eventName, payload);

    private async Task SafeSendAsync(Guid userId, string eventName, object payload)
    {
        try
        {
            await _notifier.SendAsync(userId, eventName, payload);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Olay gönderilemedi: {Event} → {User}", eventName, userId);
        }
    }

    /// <summary>Verilen anda kilit altında çalışacak bir işlem zamanlar.</summary>
    private void At(DateTime when, Func<Task> action)
    {
        var due = when - Now;
        if (due < TimeSpan.Zero) due = TimeSpan.Zero;
        var timer = _clock.CreateTimer(_ => Track(RunLockedAsync(action)), null, due, Timeout.InfiniteTimeSpan);
        lock (_timers) _timers.Add(timer);
    }

    private async Task RunLockedAsync(Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            if (_phase != Phase.Over) await action();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Maç {Match} zamanlayıcı işlemi başarısız.", MatchId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Track(Task task)
    {
        _pending[task] = 0;
        task.ContinueWith(t => _pending.TryRemove(t, out _), TaskScheduler.Default);
    }

    /// <summary>Testler için: zamanlayıcılardan doğan bekleyen işler bitene kadar bekler.</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            var tasks = _pending.Keys.ToArray();
            if (tasks.Length == 0) return;
            await Task.WhenAll(tasks);
        }
    }

    private void DisposeTimers()
    {
        lock (_timers)
        {
            foreach (var t in _timers) t.Dispose();
            _timers.Clear();
        }
    }
}
