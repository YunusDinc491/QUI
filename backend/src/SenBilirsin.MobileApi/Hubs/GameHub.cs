using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SenBilirsin.Modules.MatchEngine.Application;
using SenBilirsin.Modules.MatchEngine.Versus;
using SenBilirsin.Modules.Users.Api;
using SenBilirsin.Modules.Users.Application;
using SenBilirsin.Modules.Users.Domain;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.MobileApi.Hubs;

/// <summary>Hub metotlarının ortak yanıtı: başarı ya da <c>{code, message}</c> hatası.</summary>
public sealed record HubResult(bool Ok, string? Code = null, string? Message = null, object? Data = null)
{
    public static HubResult From<T>(MatchResult<T> r) =>
        r.IsSuccess ? new HubResult(true, Data: r.Value) : new HubResult(false, r.Error!.Code, r.Error.Message);
}

/// <summary>Bir kullanıcı = bir aktif bağlantı (docs/04 §1). Yeni bağlantı eskisini düşürür.</summary>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, string> _byUser = new();
    private readonly ConcurrentDictionary<string, Guid> _byConnection = new();

    /// <summary>Kaydeder; varsa önceki bağlantının kimliğini döndürür.</summary>
    public string? Register(Guid userId, string connectionId)
    {
        var previous = _byUser.TryGetValue(userId, out var old) ? old : null;
        _byUser[userId] = connectionId;
        _byConnection[connectionId] = userId;
        if (previous is not null) _byConnection.TryRemove(previous, out _);
        return previous;
    }

    /// <summary>Bağlantı hâlâ kullanıcının aktif bağlantısıysa kaldırır ve kullanıcıyı döndürür; değilse (yerine yenisi geldiyse) null.</summary>
    public Guid? Unregister(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var userId)) return null;
        return _byUser.TryRemove(new KeyValuePair<Guid, string>(userId, connectionId)) ? userId : null;
    }

    public string? ConnectionOf(Guid userId) => _byUser.TryGetValue(userId, out var c) ? c : null;
    public Guid? UserOf(string connectionId) => _byConnection.TryGetValue(connectionId, out var u) ? u : null;
}

/// <summary>Maç olaylarını SignalR üzerinden kullanıcının aktif bağlantısına iletir.</summary>
public sealed class SignalRMatchNotifier(IHubContext<GameHub> hub, ConnectionRegistry registry) : IMatchNotifier
{
    public Task SendAsync(Guid userId, string eventName, object payload) =>
        registry.ConnectionOf(userId) is { } connection
            ? hub.Clients.Client(connection).SendAsync(eventName, payload)
            : Task.CompletedTask;
}

/// <summary>
/// Oyun hub'ı (docs/04 §2–3). Bağlanırken kimlik (Firebase jetonu / geliştirmede Dev) ve aktif cihaz
/// (<c>X-Device-Id</c> başlığı ya da <c>?deviceId=</c>) doğrulanır; başka cihaz aktifse bağlantı reddedilir.
/// </summary>
[Authorize]
public class GameHub(
    VersusCoordinator coordinator,
    ConnectionRegistry registry,
    IPlayerDirectory directory,
    IHubContext<GameHub> hubContext) : Hub
{
    private const string UserKey = "userId";

    /// <summary>İstemcinin gönderdiği metni geri döndürür (bağlantı testi).</summary>
    public Task<string> Echo(string message) => Task.FromResult($"echo: {message}");

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext()!;
        var identity = CurrentIdentity.From(Context.User!);
        var found = identity is null ? null : await directory.FindByFirebaseUidAsync(identity.Uid);
        var deviceId = http.Request.Headers["X-Device-Id"].ToString();
        if (string.IsNullOrEmpty(deviceId)) deviceId = http.Request.Query["deviceId"].ToString();

        if (found is not { } user || user.Status != UserStatus.Active || string.IsNullOrEmpty(deviceId) || user.ActiveDeviceId != deviceId)
        {
            Context.Abort();
            return;
        }

        Context.Items[UserKey] = user.Id;
        var previous = registry.Register(user.Id, Context.ConnectionId);
        if (previous is not null) await hubContext.Clients.Client(previous).SendAsync("SessionClosed", new { reason = "new_connection" });

        await base.OnConnectedAsync();

        // Süren bir maç varsa kaldığı yerden devam için tam durum özeti
        var state = await coordinator.OnReconnectedAsync(user.Id);
        if (state is not null) await Clients.Caller.SendAsync("MatchState", state);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Yerine yeni bağlantı geldiyse (null) kopma sayılmaz
        if (registry.Unregister(Context.ConnectionId) is { } userId) await coordinator.OnDisconnectedAsync(userId);
        await base.OnDisconnectedAsync(exception);
    }

    private Guid UserId => (Guid)Context.Items[UserKey]!;

    // ---- Eşleşme ----
    public async Task<HubResult> JoinQueue(string? mode, int? categoryId) => HubResult.From(await coordinator.JoinQueueAsync(UserId, mode, categoryId));
    public async Task<HubResult> LeaveQueue() => HubResult.From(await coordinator.LeaveQueueAsync(UserId));
    public async Task<HubResult> ChooseFallback(string? choice, Guid? botId) => HubResult.From(await coordinator.ChooseFallbackAsync(UserId, choice, botId));

    // ---- Arkadaş odası ----
    public async Task<HubResult> CreateRoom(string? mode, int? categoryId) => HubResult.From(await coordinator.CreateRoomAsync(UserId, mode, categoryId));
    public async Task<HubResult> JoinRoom(string? code) => HubResult.From(await coordinator.JoinRoomAsync(UserId, code));
    public async Task<HubResult> LeaveRoom() => HubResult.From(await coordinator.LeaveRoomAsync(UserId));

    // ---- Maç ----
    public async Task<HubResult> Ready(Guid matchId) => HubResult.From(await coordinator.ReadyAsync(UserId, matchId));

    public async Task<HubResult> SubmitAnswer(Guid matchId, int position, int? option, int? second)
    {
        var runner = coordinator.RunnerOf(UserId);
        if (runner is null || runner.MatchId != matchId) return new HubResult(false, "NO_ACTIVE_MATCH", "Devam eden rakipli maç yok.");
        return HubResult.From(await coordinator.SubmitAnswerAsync(UserId, position, option, second));
    }

    public async Task<HubResult> UseJoker(Guid matchId, int position, string? type)
    {
        if (!SenBilirsin.Modules.MatchEngine.Domain.JokerRules.TryParse(type, out var joker))
            return new HubResult(false, "JOKER_INVALID", "Joker türü half, double veya hint olmalı.");
        var runner = coordinator.RunnerOf(UserId);
        if (runner is null || runner.MatchId != matchId) return new HubResult(false, "NO_ACTIVE_MATCH", "Devam eden rakipli maç yok.");
        return HubResult.From(await coordinator.UseJokerAsync(UserId, position, joker));
    }

    public async Task<HubResult> Forfeit(Guid matchId)
    {
        var runner = coordinator.RunnerOf(UserId);
        if (runner is null || runner.MatchId != matchId) return new HubResult(false, "NO_ACTIVE_MATCH", "Devam eden rakipli maç yok.");
        return HubResult.From(await coordinator.ForfeitAsync(UserId));
    }

    /// <summary>Yeniden bağlanınca maça dön: tam durum özeti.</summary>
    public async Task<HubResult> ResumeMatch(Guid matchId)
    {
        var runner = coordinator.RunnerOf(UserId);
        if (runner is null || runner.MatchId != matchId) return new HubResult(false, "NO_ACTIVE_MATCH", "Devam eden rakipli maç yok.");
        return new HubResult(true, Data: await coordinator.GetStateAsync(UserId));
    }

    // ---- Rövanş ----
    public async Task<HubResult> RequestRematch(Guid matchId) => HubResult.From(await coordinator.RequestRematchAsync(UserId, matchId));
    public async Task<HubResult> RespondRematch(Guid matchId, bool accept) => HubResult.From(await coordinator.RespondRematchAsync(UserId, matchId, accept));

    // ---- Uygulama yaşam döngüsü (docs/01 §11) ----
    /// <summary>Uygulama arka plana atıldı: ağ kopmasını beklemeden anında kopma sayılır.</summary>
    public async Task<HubResult> AppBackgrounded()
    {
        await coordinator.OnDisconnectedAsync(UserId);
        return new HubResult(true);
    }

    /// <summary>Uygulama öne döndü: varsa maç kaldığı yerden devam eder.</summary>
    public async Task<HubResult> AppForegrounded() => new(true, Data: await coordinator.OnReconnectedAsync(UserId));
}
