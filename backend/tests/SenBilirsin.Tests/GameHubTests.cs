using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.MatchEngine.Data;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Questions.Data;

namespace SenBilirsin.Tests;

/// <summary>Faz 4.4: SignalR hub'ı uçtan uca (gerçek SignalR istemcisi + test sunucusu + PostgreSQL).</summary>
[Collection("api")]
public class GameHubTests(MobileApiFactory factory)
{
    private sealed class Client(HubConnection connection, Guid userId) : IAsyncDisposable
    {
        public HubConnection Connection { get; } = connection;
        public Guid UserId { get; } = userId;
        private readonly ConcurrentQueue<(string Name, JsonElement Payload)> _events = new();

        public void Listen(params string[] names)
        {
            foreach (var name in names)
            {
                var n = name;
                Connection.On<JsonElement>(n, payload => _events.Enqueue((n, payload)));
            }
        }

        public IReadOnlyList<JsonElement> All(string name) => _events.Where(e => e.Name == name).Select(e => e.Payload).ToList();

        public async Task<JsonElement> WaitAsync(string name, int timeoutMs = 8000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                var found = All(name);
                if (found.Count > 0) return found[0];
                await Task.Delay(25);
            }

            throw new TimeoutException($"'{name}' olayı {timeoutMs} ms içinde gelmedi. Gelenler: {string.Join(", ", _events.Select(e => e.Name))}");
        }

        public async Task<JsonElement> InvokeAsync(string method, params object?[] args)
        {
            var result = await Connection.InvokeCoreAsync<JsonElement>(method, args);
            return result;
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }

    private async Task<(string Auth, string Device, Guid UserId)> RegisterAsync(string device = "device-1")
    {
        var uid = Guid.NewGuid().ToString("N");
        var auth = $"Dev uid={uid};email={uid}@test.local;verified=true;provider=google.com";
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("Authorization", auth);
        http.DefaultRequestHeaders.Add("X-Device-Id", device);
        var register = await http.PostAsJsonAsync("/api/users/me", new { username = "h" + uid[..10], displayName = "Hub " + uid[..4], avatarId = 1, birthYear = 2000, termsAccepted = true });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        await http.PostAsJsonAsync("/api/users/me/session", new { deviceId = device, platform = "test" });
        var me = await http.GetFromJsonAsync<JsonElement>("/api/users/me");
        return (auth, device, me.GetProperty("id").GetGuid());
    }

    private Client Connect((string Auth, string Device, Guid UserId) user, string? deviceOverride = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/game"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Headers["Authorization"] = user.Auth;
                options.Headers["X-Device-Id"] = deviceOverride ?? user.Device;
            })
            .Build();
        var client = new Client(connection, user.UserId);
        client.Listen("MatchFound", "CountdownStarted", "QuestionStarted", "OpponentAnswered", "QuestionResult", "JokerResult", "OpponentDisconnected",
            "OpponentReconnected", "MatchFinished", "MatchCancelled", "QueueTimeout", "OpponentJoinedRoom", "RoomClosed", "SessionClosed", "MatchState");
        return client;
    }

    private async Task<int> CorrectDisplayAsync(Guid matchId, int position)
    {
        using var scope = factory.Services.CreateScope();
        var mq = await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Questions.AsNoTracking()
            .SingleAsync(q => q.MatchId == matchId && q.Position == position && q.Attempt == 0);
        var q = await scope.ServiceProvider.GetRequiredService<QuestionsDbContext>().Questions.AsNoTracking().SingleAsync(x => x.Id == mq.QuestionId);
        return OptionShuffler.ToDisplay(OptionShuffler.Parse(mq.OptionOrder), q.CorrectIndex);
    }

    [Fact]
    public async Task Echo_WorksForAnAuthenticatedActiveDevice()
    {
        var user = await RegisterAsync();
        await using var client = Connect(user);
        await client.Connection.StartAsync();
        Assert.Equal("echo: merhaba", await client.Connection.InvokeAsync<string>("Echo", "merhaba"));
    }

    [Theory]
    [InlineData("baska-cihaz")]  // aktif cihaz değil
    [InlineData("")]             // cihaz kimliği yok
    public async Task Connection_IsRejected_ForAnInactiveDeviceOrMissingDeviceId(string device)
    {
        var user = await RegisterAsync("device-1");
        await using var client = Connect(user, device);
        await client.Connection.StartAsync(); // el sıkışma tamamlanır ama sunucu bağlantıyı hemen keser
        await Task.Delay(300);
        Assert.NotEqual(HubConnectionState.Connected, client.Connection.State);
    }

    [Fact]
    public async Task Connection_IsRejected_WithoutLogin()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/game"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            }).Build();
        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task ASecondConnection_ClosesTheFirst_WithSessionClosed()
    {
        var user = await RegisterAsync();
        await using var first = Connect(user);
        await first.Connection.StartAsync();
        await using var second = Connect(user);
        await second.Connection.StartAsync();

        var closed = await first.WaitAsync("SessionClosed");
        Assert.Equal("new_connection", closed.GetProperty("reason").GetString());
        Assert.Equal("echo: x", await second.Connection.InvokeAsync<string>("Echo", "x"));
    }

    [Fact]
    public async Task RandomMatch_EndToEnd_QueueReadyCountdownQuestionAnswerResultForfeit()
    {
        var category = await VersusHarness.NewCategoryAsync(factory);
        var ua = await RegisterAsync();
        var ub = await RegisterAsync();
        await using var a = Connect(ua);
        await using var b = Connect(ub);
        await a.Connection.StartAsync();
        await b.Connection.StartAsync();

        Assert.True((await a.InvokeAsync("JoinQueue", "branch", category)).GetProperty("ok").GetBoolean());
        Assert.True((await b.InvokeAsync("JoinQueue", "branch", category)).GetProperty("ok").GetBoolean());

        var foundA = await a.WaitAsync("MatchFound");
        var foundB = await b.WaitAsync("MatchFound");
        var matchId = foundA.GetProperty("matchId").GetGuid();
        Assert.Equal(matchId, foundB.GetProperty("matchId").GetGuid());
        Assert.StartsWith("Hub ", foundA.GetProperty("opponent").GetProperty("name").GetString());

        Assert.True((await a.InvokeAsync("Ready", matchId)).GetProperty("ok").GetBoolean());
        Assert.True((await b.InvokeAsync("Ready", matchId)).GetProperty("ok").GetBoolean());
        await a.WaitAsync("CountdownStarted");

        var qa = await a.WaitAsync("QuestionStarted");
        var qb = await b.WaitAsync("QuestionStarted");
        Assert.Equal(qa.GetProperty("options").GetRawText(), qb.GetProperty("options").GetRawText());
        Assert.DoesNotContain("correct", qa.GetRawText().ToLowerInvariant());

        var right = await CorrectDisplayAsync(matchId, 1);
        Assert.True((await a.InvokeAsync("SubmitAnswer", matchId, 1, right, null)).GetProperty("ok").GetBoolean());
        Assert.True((await b.InvokeAsync("SubmitAnswer", matchId, 1, (right + 1) % 4, null)).GetProperty("ok").GetBoolean());

        await b.WaitAsync("OpponentAnswered");
        var result = await a.WaitAsync("QuestionResult");
        Assert.Equal(right, result.GetProperty("correctOption").GetInt32());
        Assert.True(result.GetProperty("yourCorrect").GetBoolean());
        Assert.False(result.GetProperty("opponentCorrect").GetBoolean());

        // Yanlış matchId ile eylem reddedilir
        Assert.Equal("NO_ACTIVE_MATCH", (await a.InvokeAsync("SubmitAnswer", Guid.NewGuid(), 2, 0, null)).GetProperty("code").GetString());

        // B pes eder: A kazanır
        Assert.True((await b.InvokeAsync("Forfeit", matchId)).GetProperty("ok").GetBoolean());
        var finished = await a.WaitAsync("MatchFinished");
        Assert.Equal("win", finished.GetProperty("result").GetString());
        Assert.Equal("forfeit", finished.GetProperty("reason").GetString());
        Assert.Equal("lose", (await b.WaitAsync("MatchFinished")).GetProperty("result").GetString());
    }

    [Fact]
    public async Task Disconnect_NotifiesTheOpponent_AndReconnectingRestoresTheMatchState()
    {
        var category = await VersusHarness.NewCategoryAsync(factory);
        var ua = await RegisterAsync();
        var ub = await RegisterAsync();
        await using var a = Connect(ua);
        await using var b = Connect(ub);
        await a.Connection.StartAsync();
        await b.Connection.StartAsync();
        await a.InvokeAsync("JoinQueue", "branch", category);
        await b.InvokeAsync("JoinQueue", "branch", category);
        var matchId = (await a.WaitAsync("MatchFound")).GetProperty("matchId").GetGuid();
        await a.InvokeAsync("Ready", matchId);
        await b.InvokeAsync("Ready", matchId);
        await a.WaitAsync("QuestionStarted");

        await b.Connection.StopAsync();
        var notice = await a.WaitAsync("OpponentDisconnected");
        Assert.Equal(30000, notice.GetProperty("remainingMs").GetInt32());

        // B yeni bir bağlantıyla döner: otomatik tam durum özeti gelir
        await using var b2 = Connect(ub);
        await b2.Connection.StartAsync();
        var state = await b2.WaitAsync("MatchState");
        Assert.Equal(matchId, state.GetProperty("matchId").GetGuid());
        Assert.Equal("question", state.GetProperty("phase").GetString());
        Assert.Equal(1, state.GetProperty("question").GetProperty("position").GetInt32());
        await a.WaitAsync("OpponentReconnected");

        // Dönen oyuncu maça devam eder
        var right = await CorrectDisplayAsync(matchId, 1);
        Assert.True((await b2.InvokeAsync("SubmitAnswer", matchId, 1, right, null)).GetProperty("ok").GetBoolean());
        await b2.InvokeAsync("Forfeit", matchId);
    }

    [Fact]
    public async Task AppBackgrounded_CountsAsAnImmediateDisconnect_AndForegroundedResumes()
    {
        var category = await VersusHarness.NewCategoryAsync(factory);
        var ua = await RegisterAsync();
        var ub = await RegisterAsync();
        await using var a = Connect(ua);
        await using var b = Connect(ub);
        await a.Connection.StartAsync();
        await b.Connection.StartAsync();
        await a.InvokeAsync("JoinQueue", "branch", category);
        await b.InvokeAsync("JoinQueue", "branch", category);
        var matchId = (await a.WaitAsync("MatchFound")).GetProperty("matchId").GetGuid();
        await a.InvokeAsync("Ready", matchId);
        await b.InvokeAsync("Ready", matchId);
        await a.WaitAsync("QuestionStarted");

        await b.InvokeAsync("AppBackgrounded");
        await a.WaitAsync("OpponentDisconnected");

        var resumed = await b.InvokeAsync("AppForegrounded");
        Assert.True(resumed.GetProperty("ok").GetBoolean());
        await a.WaitAsync("OpponentReconnected");
        await b.InvokeAsync("Forfeit", matchId);
    }

    [Fact]
    public async Task QueueTimeout_ThenBotFallback_StartsABotMatch()
    {
        var category = await VersusHarness.NewCategoryAsync(factory);
        var user = await RegisterAsync();
        await using var a = Connect(user);
        await a.Connection.StartAsync();
        await a.InvokeAsync("JoinQueue", "branch", category);

        var timeout = await a.WaitAsync("QueueTimeout", 6000); // test sunucusunda kuyruk süresi 2 sn
        Assert.Equal(3, timeout.GetProperty("options").GetArrayLength());

        Assert.True((await a.InvokeAsync("ChooseFallback", "bot", null)).GetProperty("ok").GetBoolean());
        var found = await a.WaitAsync("MatchFound");
        Assert.True(found.GetProperty("opponent").GetProperty("isBot").GetBoolean());
        await a.WaitAsync("QuestionStarted");
        await a.InvokeAsync("Forfeit", found.GetProperty("matchId").GetGuid());
    }

    [Fact]
    public async Task FriendRoom_CreateAndJoin_StartsAFriendMatch()
    {
        var category = await VersusHarness.NewCategoryAsync(factory);
        var ua = await RegisterAsync();
        var ub = await RegisterAsync();
        await using var a = Connect(ua);
        await using var b = Connect(ub);
        await a.Connection.StartAsync();
        await b.Connection.StartAsync();

        var created = await a.InvokeAsync("CreateRoom", "branch", category);
        var code = created.GetProperty("data").GetProperty("code").GetString()!;
        Assert.True((await b.InvokeAsync("JoinRoom", code)).GetProperty("ok").GetBoolean());

        await a.WaitAsync("OpponentJoinedRoom");
        var found = await b.WaitAsync("MatchFound");
        Assert.Equal("friend", found.GetProperty("kind").GetString());
        await a.WaitAsync("QuestionStarted");
        await a.InvokeAsync("Forfeit", found.GetProperty("matchId").GetGuid());
    }

    [Fact]
    public async Task Bots_Endpoint_ListsTheActiveBots_ForTheClientPicker()
    {
        var user = await RegisterAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("Authorization", user.Auth);
        http.DefaultRequestHeaders.Add("X-Device-Id", user.Device);

        var bots = await http.GetFromJsonAsync<JsonElement>("/api/bots");
        Assert.True(bots.GetArrayLength() >= 3);
        Assert.All(bots.EnumerateArray(), b => Assert.EndsWith("Bot", b.GetProperty("name").GetString()));
    }
}
