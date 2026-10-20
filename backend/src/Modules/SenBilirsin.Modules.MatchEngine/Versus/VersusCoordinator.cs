using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SenBilirsin.Modules.MatchEngine.Application;
using SenBilirsin.Modules.MatchEngine.Data;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Users.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.MatchEngine.Versus;

public sealed record BotInfo(Guid Id, string Name, int AvatarId, int Power);

/// <summary>
/// Rakipli maç düzenleyicisi (docs/01 §7–9, docs/04): rastgele eşleşme kuyruğu (mod+alt dal, 15 sn), kuyruk zaman aşımı seçenekleri,
/// hazır kontrolü, bota karşı oynama, arkadaş odası (kod, 5 dk, tek rakip) ve süren maçlara eylem yönlendirme.
/// Bir oyuncu aynı anda tek kuyrukta/odada/maçta olabilir. Durum bellekte (tek instance, docs/02 §3).
/// </summary>
public sealed class VersusCoordinator(
    IServiceScopeFactory scopes,
    VersusMatchFactory matchFactory,
    IMatchNotifier notifier,
    TimeProvider clock,
    Random random,
    VersusSettings settings,
    ILogger<VersusCoordinator> log)
{
    private sealed record QueueEntry(Guid UserId, string Mode, int? CategoryId, string Key) { public ITimer? Timer { get; set; } }

    private sealed record TimedOutSearch(string Mode, int? CategoryId, DateTime ExpiresAt);

    private sealed class ReadyCheck(VersusMatchRunner runner, Guid a, Guid b)
    {
        public VersusMatchRunner Runner { get; } = runner;
        public Guid A { get; } = a;
        public Guid B { get; } = b;
        public bool ReadyA { get; set; }
        public bool ReadyB { get; set; }
        public ITimer? Timer { get; set; }
    }

    private sealed class Room(string code, Guid owner, string mode, int? categoryId, DateTime expiresAt)
    {
        public string Code { get; } = code;
        public Guid Owner { get; } = owner;
        public string Mode { get; } = mode;
        public int? CategoryId { get; } = categoryId;
        public DateTime ExpiresAt { get; } = expiresAt;
        public ITimer? Timer { get; set; }

        /// <summary>Rakip geldi, maç başladı: oda dolu (ikinci gelen "Oda dolu" görür); süresi dolunca silinir.</summary>
        public bool Full { get; set; }
    }

    /// <summary>Biten maçın rövanşı için hatırlanan bilgi (2 dk geçerli).</summary>
    private sealed record RecentMatch(Guid MatchId, Guid Self, Guid Opponent, bool BotMatch, BotProfile? Bot, MatchKind Kind, string Mode, int? CategoryId, DateTime ExpiresAt);

    private sealed class RematchRequest(Guid matchId, Guid requester, Guid target)
    {
        public Guid MatchId { get; } = matchId;
        public Guid Requester { get; } = requester;
        public Guid Target { get; } = target;
        public ITimer? Timer { get; set; }
    }

    private const string RoomAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // karıştırılan 0/O, 1/I yok

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, List<QueueEntry>> _queues = [];
    private readonly Dictionary<Guid, QueueEntry> _queued = [];
    private readonly Dictionary<Guid, TimedOutSearch> _timedOut = [];
    private readonly Dictionary<string, Room> _rooms = [];
    private readonly Dictionary<Guid, string> _roomOf = [];
    private readonly ConcurrentDictionary<Guid, VersusMatchRunner> _byUser = new();
    private readonly ConcurrentDictionary<Guid, ReadyCheck> _readyChecks = new(); // matchId → kontrol
    private readonly ConcurrentDictionary<Task, byte> _pending = new();
    private readonly ConcurrentDictionary<Guid, RecentMatch> _recent = new(); // kullanıcı → son biten maç
    private readonly Dictionary<Guid, RematchRequest> _rematches = []; // maç kimliği → bekleyen istek

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public bool IsInMatch(Guid userId) => _byUser.ContainsKey(userId);

    // ================================================================ Rastgele eşleşme

    public async Task<MatchResult<bool>> JoinQueueAsync(Guid userId, string? mode, int? categoryId)
    {
        await _gate.WaitAsync();
        try
        {
            if (IsBusy(userId)) return MatchResult<bool>.Fail("ALREADY_ACTIVE", "Zaten bir kuyrukta, odada ya da maçtasın.", 409);
            var scope = await ResolveAsync(mode, categoryId);
            if (scope.Error is { } e) return MatchResult<bool>.Fail(e.Code, e.Message, e.Status);

            _timedOut.Remove(userId);
            var key = $"{scope.ModeCode}:{scope.CategoryId?.ToString() ?? "mix"}";
            var waiting = _queues.GetValueOrDefault(key)?.FirstOrDefault();
            if (waiting is not null)
            {
                RemoveFromQueue(waiting.UserId);
                var error = await StartPairAsync(MatchKind.Random, scope, waiting.UserId, userId, needsReady: true);
                if (error is not null)
                {
                    await SendAsync(waiting.UserId, MatchEvents.MatchCancelled, new { reason = error });
                    return MatchResult<bool>.Fail(error, "Bu kategoride şu an yeterli soru yok.", 409);
                }

                return MatchResult<bool>.Ok(true);
            }

            var entry = new QueueEntry(userId, scope.ModeCode, scope.CategoryId, key);
            entry.Timer = clock.CreateTimer(_ => Track(RunLockedAsync(() => QueueTimeoutAsync(userId))), null,
                TimeSpan.FromSeconds(settings.QueueTimeoutSeconds), Timeout.InfiniteTimeSpan);
            _queued[userId] = entry;
            (_queues.TryGetValue(key, out var list) ? list : _queues[key] = []).Add(entry);
            return MatchResult<bool>.Ok(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MatchResult<bool>> LeaveQueueAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            RemoveFromQueue(userId);
            _timedOut.Remove(userId);
            return MatchResult<bool>.Ok(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task QueueTimeoutAsync(Guid userId)
    {
        if (!_queued.TryGetValue(userId, out var entry)) return;
        RemoveFromQueue(userId);
        _timedOut[userId] = new TimedOutSearch(entry.Mode, entry.CategoryId, Now.AddMinutes(2));
        await SendAsync(userId, MatchEvents.QueueTimeout, new { options = new[] { "bot", "solo", "retry" }, mode = entry.Mode, categoryId = entry.CategoryId });
    }

    /// <summary>15 sn sonunda bulunamayınca seçim: <c>bot</c> (isteğe bağlı bot kimliği), <c>solo</c> (istemci REST ile oynar) ya da <c>retry</c>.</summary>
    public async Task<MatchResult<bool>> ChooseFallbackAsync(Guid userId, string? choice, Guid? botId = null)
    {
        TimedOutSearch? search;
        await _gate.WaitAsync();
        try
        {
            if (!_timedOut.TryGetValue(userId, out search) || search.ExpiresAt < Now)
                return MatchResult<bool>.Fail("NO_PENDING_SEARCH", "Bekleyen bir arama yok.", 409);
            if (IsBusy(userId)) return MatchResult<bool>.Fail("ALREADY_ACTIVE", "Zaten bir kuyrukta, odada ya da maçtasın.", 409);
            _timedOut.Remove(userId);
        }
        finally
        {
            _gate.Release();
        }

        switch (choice)
        {
            case "retry":
                return await JoinQueueAsync(userId, search.Mode, search.CategoryId);
            case "solo":
                return MatchResult<bool>.Ok(true); // istemci POST /api/matches/solo çağırır
            case "bot":
                return await StartBotMatchAsync(userId, search.Mode, search.CategoryId, botId);
            default:
                return MatchResult<bool>.Fail("CHOICE_INVALID", "Seçim bot, solo ya da retry olmalı.");
        }
    }

    private async Task<MatchResult<bool>> StartBotMatchAsync(Guid userId, string mode, int? categoryId, Guid? botId)
    {
        await _gate.WaitAsync();
        try
        {
            if (IsBusy(userId)) return MatchResult<bool>.Fail("ALREADY_ACTIVE", "Zaten bir kuyrukta, odada ya da maçtasın.", 409);
            var scope = await ResolveAsync(mode, categoryId);
            if (scope.Error is { } e) return MatchResult<bool>.Fail(e.Code, e.Message, e.Status);

            BotProfile? bot;
            await using (var s = scopes.CreateAsyncScope())
            {
                var bots = await s.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Bots.AsNoTracking().Where(b => b.IsActive).ToListAsync();
                bot = botId is { } id ? bots.FirstOrDefault(b => b.Id == id) : bots.Count == 0 ? null : bots[random.Next(bots.Count)];
            }

            if (bot is null) return MatchResult<bool>.Fail("BOT_NOT_FOUND", "Bot bulunamadı.", 404);
            var error = await StartPairAsync(MatchKind.Bot, scope, userId, bot.Id, needsReady: false, bot);
            return error is null ? MatchResult<bool>.Ok(true) : MatchResult<bool>.Fail(error, "Bu kategoride şu an yeterli soru yok.", 409);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BotInfo>> ListBotsAsync()
    {
        await using var s = scopes.CreateAsyncScope();
        var bots = await s.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Bots.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Power).ToListAsync();
        return bots.Select(b => new BotInfo(b.Id, b.Name, b.AvatarId, b.Power)).ToList();
    }

    // ================================================================ Arkadaş odası

    public async Task<MatchResult<object>> CreateRoomAsync(Guid userId, string? mode, int? categoryId)
    {
        await _gate.WaitAsync();
        try
        {
            if (IsBusy(userId)) return MatchResult<object>.Fail("ALREADY_ACTIVE", "Zaten bir kuyrukta, odada ya da maçtasın.", 409);
            var scope = await ResolveAsync(mode, categoryId);
            if (scope.Error is { } e) return MatchResult<object>.Fail(e.Code, e.Message, e.Status);

            string code;
            do code = NewRoomCode(); while (_rooms.ContainsKey(code));
            var expires = Now.AddMinutes(settings.RoomMinutes);
            var room = new Room(code, userId, scope.ModeCode, scope.CategoryId, expires);
            room.Timer = clock.CreateTimer(_ => Track(RunLockedAsync(() => RoomExpiredAsync(code))), null, TimeSpan.FromMinutes(settings.RoomMinutes), Timeout.InfiniteTimeSpan);
            _rooms[code] = room;
            _roomOf[userId] = code;
            return MatchResult<object>.Ok(new { code, expiresAt = expires, mode = scope.ModeCode, categoryId = scope.CategoryId });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MatchResult<bool>> JoinRoomAsync(Guid userId, string? code)
    {
        await _gate.WaitAsync();
        try
        {
            var key = (code ?? "").Trim().ToUpperInvariant();
            if (!_rooms.TryGetValue(key, out var room) || room.ExpiresAt < Now)
                return MatchResult<bool>.Fail("ROOM_NOT_FOUND", "Oda bulunamadı ya da süresi doldu.", 404);
            if (room.Owner == userId) return MatchResult<bool>.Ok(true); // kendi bağlantısına tıklamak bir şey yapmaz
            if (room.Full) return MatchResult<bool>.Fail("ROOM_FULL", "Oda dolu.", 409);
            if (IsBusy(userId)) return MatchResult<bool>.Fail("ALREADY_ACTIVE", "Zaten bir kuyrukta, odada ya da maçtasın.", 409);

            var scope = await ResolveAsync(room.Mode, room.CategoryId);
            if (scope.Error is { } e) return MatchResult<bool>.Fail(e.Code, e.Message, e.Status);

            var info = await PlayerInfoAsync(userId);
            var error = await StartPairAsync(MatchKind.Friend, scope, room.Owner, userId, needsReady: false, preface: async () =>
                await SendAsync(room.Owner, MatchEvents.OpponentJoinedRoom, new { opponent = new { name = info.DisplayName, avatarId = info.AvatarId, frameId = info.FrameId } }));
            if (error is not null) return MatchResult<bool>.Fail(error, "Bu kategoride şu an yeterli soru yok.", 409);

            // Tek rakip: oda dolu işaretlenir, ikinci gelen "Oda dolu" görür; kurucu artık maçtadır
            room.Full = true;
            _roomOf.Remove(room.Owner);
            return MatchResult<bool>.Ok(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Kurucu çıkarsa oda kapanır.</summary>
    public async Task<MatchResult<bool>> LeaveRoomAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_roomOf.TryGetValue(userId, out var code) && _rooms.TryGetValue(code, out var room) && !room.Full) CloseRoom(room);
            return MatchResult<bool>.Ok(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RoomExpiredAsync(string code)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;
        CloseRoom(room);
        if (!room.Full) await SendAsync(room.Owner, MatchEvents.RoomClosed, new { reason = "expired" });
    }

    private void CloseRoom(Room room)
    {
        room.Timer?.Dispose();
        _rooms.Remove(room.Code);
        _roomOf.Remove(room.Owner);
    }

    private static string NewRoomCode() => string.Create(6, RoomAlphabet, (span, alphabet) =>
    {
        for (var i = 0; i < span.Length; i++) span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    });

    // ================================================================ Maç kurma, hazır kontrolü

    /// <summary>İki taraf için maçı kurar ve MatchFound gönderir. Hata kodu ya da null döner.</summary>
    private async Task<string?> StartPairAsync(MatchKind kind, MatchScope scope, Guid a, Guid b, bool needsReady, BotProfile? bot = null, Func<Task>? preface = null)
    {
        var specA = await PlayerSpecAsync(a);
        var specB = bot is null ? await PlayerSpecAsync(b) : new VersusPlayerSpec(bot.Id, bot.Name, bot.AvatarId, null, true, bot);

        var (runner, error) = await matchFactory.CreateAsync(kind, scope.ModeCode, scope.CategoryId, scope.CategoryIds, specA, specB, OnEnded);
        if (runner is null) return error;

        _byUser[a] = runner;
        if (bot is null) _byUser[b] = runner;

        if (preface is not null) await preface();
        foreach (var (me, other) in new[] { (specA, specB), (specB, specA) })
        {
            if (me.IsBot) continue;
            await SendAsync(me.Id, MatchEvents.MatchFound, new
            {
                matchId = runner.MatchId, kind = kind.ToString().ToLowerInvariant(), mode = scope.ModeCode, categoryId = scope.CategoryId,
                needsReady = needsReady, readyTimeoutSeconds = settings.ReadyTimeoutSeconds,
                opponent = new { id = other.IsBot ? (Guid?)null : other.Id, name = other.Name, avatarId = other.AvatarId, frameId = other.FrameId, isBot = other.IsBot },
            });
        }

        if (!needsReady)
        {
            await runner.StartAsync();
            return null;
        }

        var check = new ReadyCheck(runner, a, b);
        var matchId = runner.MatchId;
        check.Timer = clock.CreateTimer(_ => Track(RunLockedAsync(() => ReadyTimeoutAsync(matchId))), null,
            TimeSpan.FromSeconds(settings.ReadyTimeoutSeconds), Timeout.InfiniteTimeSpan);
        _readyChecks[matchId] = check;
        return null;
    }

    /// <summary>Maça hazır (docs/04 <c>Ready(matchId)</c>): ikisi de hazırsa 5 sn geri sayım başlar.</summary>
    public async Task<MatchResult<bool>> ReadyAsync(Guid userId, Guid matchId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_readyChecks.TryGetValue(matchId, out var check) || (check.A != userId && check.B != userId))
                return MatchResult<bool>.Fail("NO_PENDING_READY", "Bekleyen bir hazır kontrolü yok.", 409);
            if (check.A == userId) check.ReadyA = true; else check.ReadyB = true;
            if (check.ReadyA && check.ReadyB)
            {
                check.Timer?.Dispose();
                _readyChecks.TryRemove(matchId, out _);
                await check.Runner.StartAsync();
            }

            return MatchResult<bool>.Ok(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReadyTimeoutAsync(Guid matchId)
    {
        if (!_readyChecks.TryRemove(matchId, out var check)) return;
        await check.Runner.CancelAsync(MatchEndReasonsVersus.NotReady);
    }

    // ================================================================ Rövanş

    /// <summary>
    /// Maç bitince "rövanş" (docs/01 §9): aynı kategori, aynı rakip; bota karşıysa hemen, insana karşıysa karşı taraf onaylarsa yeni maç.
    /// İki taraf aynı anda isterse otomatik kabul edilir.
    /// </summary>
    public async Task<MatchResult<bool>> RequestRematchAsync(Guid userId, Guid matchId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_recent.TryGetValue(userId, out var recent) || recent.MatchId != matchId || recent.ExpiresAt < Now)
                return MatchResult<bool>.Fail("NO_REMATCH", "Rövanş süresi doldu ya da bu maç için rövanş yok.", 409);
            if (IsBusy(userId)) return MatchResult<bool>.Fail("ALREADY_ACTIVE", "Zaten bir kuyrukta, odada ya da maçtasın.", 409);

            var scope = await ResolveAsync(recent.Mode, recent.CategoryId);
            if (scope.Error is { } e) return MatchResult<bool>.Fail(e.Code, e.Message, e.Status);

            if (recent.BotMatch)
            {
                _recent.TryRemove(userId, out _);
                var botError = await StartPairAsync(MatchKind.Bot, scope, userId, recent.Opponent, needsReady: false, recent.Bot);
                return botError is null ? MatchResult<bool>.Ok(true) : MatchResult<bool>.Fail(botError, "Bu kategoride şu an yeterli soru yok.", 409);
            }

            if (_rematches.TryGetValue(matchId, out var pending) && pending.Requester == recent.Opponent)
                return await AcceptRematchAsync(pending, scope);
            if (IsBusy(recent.Opponent)) return MatchResult<bool>.Fail("OPPONENT_BUSY", "Rakip şu an başka bir oyunda.", 409);

            var request = new RematchRequest(matchId, userId, recent.Opponent);
            request.Timer = clock.CreateTimer(_ => Track(RunLockedAsync(() => RematchTimeoutAsync(matchId, request))), null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
            _rematches[matchId] = request;
            await SendAsync(recent.Opponent, MatchEvents.RematchRequested, new { matchId });
            return MatchResult<bool>.Ok(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MatchResult<bool>> RespondRematchAsync(Guid userId, Guid matchId, bool accept)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_rematches.TryGetValue(matchId, out var request) || request.Target != userId)
                return MatchResult<bool>.Fail("NO_REMATCH", "Bekleyen bir rövanş isteği yok.", 409);

            if (!accept)
            {
                request.Timer?.Dispose();
                _rematches.Remove(matchId);
                await SendAsync(request.Requester, MatchEvents.RematchResult, new { matchId, accepted = false, reason = "declined" });
                return MatchResult<bool>.Ok(true);
            }

            if (IsBusy(userId) || IsBusy(request.Requester)) return MatchResult<bool>.Fail("ALREADY_ACTIVE", "Zaten bir kuyrukta, odada ya da maçtasın.", 409);
            var recent = _recent.GetValueOrDefault(userId);
            if (recent is null) return MatchResult<bool>.Fail("NO_REMATCH", "Rövanş süresi doldu.", 409);
            var scope = await ResolveAsync(recent.Mode, recent.CategoryId);
            if (scope.Error is { } e) return MatchResult<bool>.Fail(e.Code, e.Message, e.Status);
            return await AcceptRematchAsync(request, scope);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MatchResult<bool>> AcceptRematchAsync(RematchRequest request, MatchScope scope)
    {
        request.Timer?.Dispose();
        _rematches.Remove(request.MatchId);
        _recent.TryRemove(request.Requester, out _);
        _recent.TryRemove(request.Target, out _);
        await SendAsync(request.Requester, MatchEvents.RematchResult, new { matchId = request.MatchId, accepted = true });
        await SendAsync(request.Target, MatchEvents.RematchResult, new { matchId = request.MatchId, accepted = true });
        var error = await StartPairAsync(MatchKind.Friend, scope, request.Requester, request.Target, needsReady: false);
        return error is null ? MatchResult<bool>.Ok(true) : MatchResult<bool>.Fail(error, "Bu kategoride şu an yeterli soru yok.", 409);
    }

    private async Task RematchTimeoutAsync(Guid matchId, RematchRequest request)
    {
        if (!_rematches.TryGetValue(matchId, out var current) || !ReferenceEquals(current, request)) return;
        _rematches.Remove(matchId);
        await SendAsync(request.Requester, MatchEvents.RematchResult, new { matchId, accepted = false, reason = "timeout" });
    }

    // ================================================================ Süren maç eylemleri

    public Task<MatchResult<bool>> SubmitAnswerAsync(Guid userId, int position, int? option, int? second) =>
        _byUser.TryGetValue(userId, out var r) ? r.SubmitAnswerAsync(userId, position, option, second) : NoMatch<bool>();

    public Task<MatchResult<JokerOutcome>> UseJokerAsync(Guid userId, int position, JokerType type) =>
        _byUser.TryGetValue(userId, out var r) ? r.UseJokerAsync(userId, position, type) : NoMatch<JokerOutcome>();

    public async Task<MatchResult<bool>> ForfeitAsync(Guid userId)
    {
        if (!_byUser.TryGetValue(userId, out var r)) return await NoMatch<bool>();
        if (_readyChecks.ContainsKey(r.MatchId)) await r.CancelAsync(MatchEndReasonsVersus.NotReady); // henüz başlamadı: iptal
        else await r.ForfeitAsync(userId);
        return MatchResult<bool>.Ok(true);
    }

    public async Task<object?> GetStateAsync(Guid userId) => _byUser.TryGetValue(userId, out var r) ? await r.GetStateAsync(userId) : null;

    /// <summary>Bağlantı koptu: kuyruktan/odadan çıkarır, maçtaysa kopma kuralını işletir.</summary>
    public async Task OnDisconnectedAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            RemoveFromQueue(userId);
            _timedOut.Remove(userId);
            if (_roomOf.TryGetValue(userId, out var code) && _rooms.TryGetValue(code, out var room) && !room.Full) CloseRoom(room);
        }
        finally
        {
            _gate.Release();
        }

        if (!_byUser.TryGetValue(userId, out var runner)) return;
        if (_readyChecks.ContainsKey(runner.MatchId)) await runner.CancelAsync(MatchEndReasonsVersus.NotReady);
        else await runner.PlayerDisconnectedAsync(userId);
    }

    /// <summary>Yeniden bağlanan oyuncunun maçı varsa tam durum özetini döndürür.</summary>
    public async Task<object?> OnReconnectedAsync(Guid userId) =>
        _byUser.TryGetValue(userId, out var runner) ? await runner.PlayerReconnectedAsync(userId) : null;

    /// <summary>Sunucu kapanırken süren maçları iptal eder (puan yok, K-43).</summary>
    public async Task CancelAllAsync(string reason)
    {
        foreach (var runner in _byUser.Values.Distinct()) await runner.CancelAsync(reason);
        await _gate.WaitAsync();
        try
        {
            foreach (var e in _queued.Values.ToList()) RemoveFromQueue(e.UserId);
            foreach (var room in _rooms.Values.ToList()) CloseRoom(room);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ================================================================ Yardımcılar

    private bool IsBusy(Guid userId) => _queued.ContainsKey(userId) || _roomOf.ContainsKey(userId) || _byUser.ContainsKey(userId);

    private void OnEnded(VersusMatchRunner runner)
    {
        if (runner.FinishedNormally)
        {
            var spec = runner.Spec;
            var expires = Now.AddMinutes(2);
            foreach (var (me, other) in new[] { (spec.A, spec.B), (spec.B, spec.A) })
            {
                if (me.IsBot) continue;
                _recent[me.Id] = new RecentMatch(spec.MatchId, me.Id, other.Id, other.IsBot, other.Bot, spec.Kind, spec.ModeCode, spec.CategoryId, expires);
            }
        }
        foreach (var id in _byUser.Where(kv => ReferenceEquals(kv.Value, runner)).Select(kv => kv.Key).ToList()) _byUser.TryRemove(id, out _);
        if (_readyChecks.TryRemove(runner.MatchId, out var check)) check.Timer?.Dispose();
    }

    private void RemoveFromQueue(Guid userId)
    {
        if (!_queued.Remove(userId, out var entry)) return;
        entry.Timer?.Dispose();
        if (_queues.TryGetValue(entry.Key, out var list))
        {
            list.RemoveAll(x => x.UserId == userId);
            if (list.Count == 0) _queues.Remove(entry.Key);
        }
    }

    private async Task<MatchScope> ResolveAsync(string? mode, int? categoryId)
    {
        await using var s = scopes.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<MatchScopeResolver>().ResolveAsync(mode, categoryId, CancellationToken.None);
    }

    private async Task<PlayerInfo> PlayerInfoAsync(Guid userId)
    {
        await using var s = scopes.CreateAsyncScope();
        var found = await s.ServiceProvider.GetRequiredService<IPlayerDirectory>().GetAsync([userId]);
        return found.GetValueOrDefault(userId) ?? new PlayerInfo(userId, "Oyuncu", 1, null);
    }

    private async Task<VersusPlayerSpec> PlayerSpecAsync(Guid userId)
    {
        var i = await PlayerInfoAsync(userId);
        return new VersusPlayerSpec(i.Id, i.DisplayName, i.AvatarId, i.FrameId, false, null);
    }

    private static Task<MatchResult<T>> NoMatch<T>() => Task.FromResult(MatchResult<T>.Fail("NO_ACTIVE_MATCH", "Devam eden rakipli maç yok.", 404));

    private async Task SendAsync(Guid userId, string eventName, object payload)
    {
        try
        {
            await notifier.SendAsync(userId, eventName, payload);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Olay gönderilemedi: {Event} → {User}", eventName, userId);
        }
    }

    private async Task RunLockedAsync(Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Koordinatör zamanlayıcı işlemi başarısız.");
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
            var runners = _byUser.Values.Distinct().Select(r => r.WhenIdleAsync()).ToArray();
            if (tasks.Length == 0 && runners.All(t => t.IsCompleted)) { await Task.WhenAll(runners); if (_pending.IsEmpty) return; continue; }
            await Task.WhenAll(tasks.Concat(runners));
        }
    }

    /// <summary>Testler için: bir kullanıcının süren maç koşucusu.</summary>
    public VersusMatchRunner? RunnerOf(Guid userId) => _byUser.GetValueOrDefault(userId);
}
