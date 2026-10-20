using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SenBilirsin.Modules.MatchEngine.Application;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.MatchEngine.Versus;
using SenBilirsin.Modules.Users.Data;
using SenBilirsin.Modules.Users.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 4.3: eşleşme kuyruğu, hazır kontrolü, bot geri dönüşü, arkadaş odası, tek aktif durum kuralı. Sahte saat + gerçek PostgreSQL.</summary>
[Collection("api")]
public class VersusCoordinatorTests(MobileApiFactory factory)
{
    private sealed class Setup
    {
        public required ManualClock Clock { get; init; }
        public required RecordingNotifier Notifier { get; init; }
        public required VersusCoordinator Coordinator { get; init; }
        public required int CategoryId { get; init; }
        public required Guid A { get; init; }
        public required Guid B { get; init; }
        public required Guid C { get; init; }

        public Task AdvanceAsync(double seconds) => Clock.AdvanceAsync(TimeSpan.FromSeconds(seconds), Coordinator.WhenIdleAsync);

        public Task Idle() => Coordinator.WhenIdleAsync();
    }

    private async Task<Guid> NewUserAsync(string name, int avatar)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        var user = new UserAccount
        {
            Id = Guid.NewGuid(), FirebaseUid = Guid.NewGuid().ToString("N"), Username = "k" + Guid.NewGuid().ToString("N")[..10], DisplayName = name,
            AvatarId = avatar, BirthYear = 2000, Status = UserStatus.Active, CreatedAt = DateTime.UtcNow, TermsAcceptedAt = DateTime.UtcNow, TermsVersion = "t",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Setup> NewSetupAsync()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var notifier = new RecordingNotifier();
        var scopes = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var random = new Random(5);
        var matchFactory = new VersusMatchFactory(scopes, notifier, clock, random, new MatchSettings(), new VersusSettings(), NullLoggerFactory.Instance);
        var coordinator = new VersusCoordinator(scopes, matchFactory, notifier, clock, random, new VersusSettings(), NullLogger<VersusCoordinator>.Instance);
        return new Setup
        {
            Clock = clock, Notifier = notifier, Coordinator = coordinator, CategoryId = await VersusHarness.NewCategoryAsync(factory),
            A = await NewUserAsync("Ayşe", 1), B = await NewUserAsync("Berk", 2), C = await NewUserAsync("Ceren", 3),
        };
    }

    // ---------------------------------------------------------------- Rastgele eşleşme

    [Fact]
    public async Task TwoPlayersInTheSameQueue_AreMatched_AndSeeEachOthersPublicInfo()
    {
        var s = await NewSetupAsync();
        Assert.True((await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId)).IsSuccess);
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.MatchFound)); // henüz rakip yok

        Assert.True((await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId)).IsSuccess);

        var a = s.Notifier.For(s.A, MatchEvents.MatchFound).Single().Payload;
        var b = s.Notifier.For(s.B, MatchEvents.MatchFound).Single().Payload;
        Assert.Equal(a.GetProperty("matchId").GetGuid(), b.GetProperty("matchId").GetGuid());
        Assert.True(a.GetProperty("needsReady").GetBoolean());
        Assert.Equal("Berk", a.GetProperty("opponent").GetProperty("name").GetString());
        Assert.Equal(2, a.GetProperty("opponent").GetProperty("avatarId").GetInt32());
        Assert.Equal("Ayşe", b.GetProperty("opponent").GetProperty("name").GetString());
        Assert.False(a.GetProperty("opponent").GetProperty("isBot").GetBoolean());
        // Rakibe e-posta/doğum yılı gibi alanlar sızmaz
        Assert.DoesNotContain("birth", a.GetRawText().ToLowerInvariant());
        Assert.DoesNotContain("email", a.GetRawText().ToLowerInvariant());
    }

    [Fact]
    public async Task TheCountdownStartsOnlyWhenBothAreReady()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId);
        var matchId = s.Notifier.For(s.A, MatchEvents.MatchFound).Single().Payload.GetProperty("matchId").GetGuid();

        await s.Coordinator.ReadyAsync(s.A, matchId);
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.CountdownStarted));

        await s.Coordinator.ReadyAsync(s.B, matchId);
        Assert.Single(s.Notifier.For(s.A, MatchEvents.CountdownStarted));
        Assert.Single(s.Notifier.For(s.B, MatchEvents.CountdownStarted));

        Assert.Equal("NO_PENDING_READY", (await s.Coordinator.ReadyAsync(s.A, matchId)).Error!.Code); // ikinci kez hazır olunamaz
    }

    [Fact]
    public async Task NotReadyWithin10Seconds_CancelsTheMatch_AndFreesBothPlayers()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId);
        var matchId = s.Notifier.For(s.A, MatchEvents.MatchFound).Single().Payload.GetProperty("matchId").GetGuid();
        await s.Coordinator.ReadyAsync(s.A, matchId); // yalnızca A hazır

        await s.AdvanceAsync(9.5);
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.MatchCancelled));
        await s.AdvanceAsync(1);

        Assert.Equal("not_ready", s.Notifier.For(s.A, MatchEvents.MatchCancelled).Single().Payload.GetProperty("reason").GetString());
        Assert.Single(s.Notifier.For(s.B, MatchEvents.MatchCancelled));
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.CountdownStarted));

        // İkisi de yeniden kuyruğa girebilir
        Assert.True((await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId)).IsSuccess);
        Assert.True((await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId)).IsSuccess);
    }

    [Fact]
    public async Task DifferentModeOrCategory_NeverMatch()
    {
        var s = await NewSetupAsync();
        var other = await VersusHarness.NewCategoryAsync(factory);
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.Coordinator.JoinQueueAsync(s.B, "branch", other);

        Assert.Empty(s.Notifier.For(s.A, MatchEvents.MatchFound));
        Assert.Empty(s.Notifier.For(s.B, MatchEvents.MatchFound));
    }

    [Fact]
    public async Task QueueTimeout_After15Seconds_OffersBotSoloOrRetry()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);

        await s.AdvanceAsync(14.5);
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.QueueTimeout));
        await s.AdvanceAsync(1);

        var timeout = s.Notifier.For(s.A, MatchEvents.QueueTimeout).Single().Payload;
        Assert.Equal(["bot", "solo", "retry"], timeout.GetProperty("options").EnumerateArray().Select(x => x.GetString()));

        // Otomatik bot eşleşmesi yok: seçim yapılana kadar hiçbir maç kurulmaz
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.MatchFound));
    }

    [Fact]
    public async Task LeavingTheQueue_CancelsTheTimeout_AndFreesThePlayer()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.Coordinator.LeaveQueueAsync(s.A);
        await s.AdvanceAsync(20);
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.QueueTimeout));

        // Kuyruktan çıkan, sonradan gelenle eşleşmez
        await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId);
        Assert.Empty(s.Notifier.For(s.B, MatchEvents.MatchFound));
    }

    [Fact]
    public async Task FallbackToBot_StartsABotMatchRightAway_MarkedAsBot()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.AdvanceAsync(15.5);

        Assert.True((await s.Coordinator.ChooseFallbackAsync(s.A, "bot")).IsSuccess);

        var found = s.Notifier.For(s.A, MatchEvents.MatchFound).Single().Payload;
        Assert.True(found.GetProperty("opponent").GetProperty("isBot").GetBoolean());
        Assert.False(found.GetProperty("needsReady").GetBoolean());
        Assert.EndsWith("Bot", found.GetProperty("opponent").GetProperty("name").GetString());
        Assert.Single(s.Notifier.For(s.A, MatchEvents.CountdownStarted)); // hazır kontrolü yok, geri sayım hemen

        // Bot seçimi için liste de var
        var bots = await s.Coordinator.ListBotsAsync();
        Assert.True(bots.Count >= 3);
    }

    [Fact]
    public async Task FallbackToASpecificBot_UsesThatBot()
    {
        var s = await NewSetupAsync();
        var bot = (await s.Coordinator.ListBotsAsync()).First(b => b.Power == 3);
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.AdvanceAsync(15.5);

        await s.Coordinator.ChooseFallbackAsync(s.A, "bot", bot.Id);

        Assert.Equal(bot.Name, s.Notifier.For(s.A, MatchEvents.MatchFound).Single().Payload.GetProperty("opponent").GetProperty("name").GetString());
        Assert.Equal("BOT_NOT_FOUND", await FallbackErrorAsync(await NewSetupAsync(), Guid.NewGuid()));
    }

    private static async Task<string?> FallbackErrorAsync(Setup s, Guid botId)
    {
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.AdvanceAsync(15.5);
        return (await s.Coordinator.ChooseFallbackAsync(s.A, "bot", botId)).Error?.Code;
    }

    [Fact]
    public async Task Fallback_RetryRejoinsTheQueue_SoloEndsTheSearch_AndRequiresAPendingSearch()
    {
        var s = await NewSetupAsync();
        Assert.Equal("NO_PENDING_SEARCH", (await s.Coordinator.ChooseFallbackAsync(s.A, "bot")).Error!.Code);

        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.AdvanceAsync(15.5);
        Assert.Equal("CHOICE_INVALID", (await s.Coordinator.ChooseFallbackAsync(s.A, "belki")).Error!.Code);

        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId); // tekrar dene seçeneği yerine doğrudan
        await s.AdvanceAsync(15.5);
        Assert.True((await s.Coordinator.ChooseFallbackAsync(s.A, "retry")).IsSuccess);

        // retry kuyruğa geri koydu: B girince eşleşirler
        await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId);
        Assert.Single(s.Notifier.For(s.A, MatchEvents.MatchFound));

        var s2 = await NewSetupAsync();
        await s2.Coordinator.JoinQueueAsync(s2.A, "branch", s2.CategoryId);
        await s2.AdvanceAsync(15.5);
        Assert.True((await s2.Coordinator.ChooseFallbackAsync(s2.A, "solo")).IsSuccess);
        Assert.Empty(s2.Notifier.For(s2.A, MatchEvents.MatchFound)); // bireysel maç REST ile başlar
    }

    [Fact]
    public async Task AUserCanOnlyBeInOneQueueRoomOrMatchAtATime()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        Assert.Equal("ALREADY_ACTIVE", (await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId)).Error!.Code);
        Assert.Equal("ALREADY_ACTIVE", (await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId)).Error!.Code);

        await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId); // eşleşti, hazır bekleniyor
        Assert.Equal("ALREADY_ACTIVE", (await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId)).Error!.Code);
        Assert.Equal("ALREADY_ACTIVE", (await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId)).Error!.Code);
    }

    [Fact]
    public async Task InvalidScope_IsRejected()
    {
        var s = await NewSetupAsync();
        Assert.Equal("MODE_INVALID", (await s.Coordinator.JoinQueueAsync(s.A, "yok", null)).Error!.Code);
        Assert.Equal("CATEGORY_UNAVAILABLE", (await s.Coordinator.JoinQueueAsync(s.A, "branch", 999_999)).Error!.Code);
    }

    // ---------------------------------------------------------------- Arkadaş odası

    [Fact]
    public async Task Room_FriendJoinsWithTheCode_BothSeeMatchFound_AndCountdownStartsWithoutAReadyStep()
    {
        var s = await NewSetupAsync();
        var created = await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId);
        var code = System.Text.Json.JsonSerializer.SerializeToElement(created.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            .GetProperty("code").GetString()!;
        Assert.Equal(6, code.Length);

        Assert.True((await s.Coordinator.JoinRoomAsync(s.B, code.ToLowerInvariant())).IsSuccess); // büyük/küçük harf fark etmez

        Assert.Equal("Berk", s.Notifier.For(s.A, MatchEvents.OpponentJoinedRoom).Single().Payload.GetProperty("opponent").GetProperty("name").GetString());
        var foundA = s.Notifier.For(s.A, MatchEvents.MatchFound).Single().Payload;
        Assert.Equal("friend", foundA.GetProperty("kind").GetString());
        Assert.False(foundA.GetProperty("needsReady").GetBoolean());
        Assert.Single(s.Notifier.For(s.A, MatchEvents.CountdownStarted));
        Assert.Single(s.Notifier.For(s.B, MatchEvents.CountdownStarted));
    }

    [Fact]
    public async Task Room_IsSingleUse_ThirdPersonSeesFull_OwnLinkDoesNothing_UnknownCodeFails()
    {
        var s = await NewSetupAsync();
        var code = RoomCode(await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId));

        Assert.True((await s.Coordinator.JoinRoomAsync(s.A, code)).IsSuccess); // kendi bağlantısı: etkisiz
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.MatchFound));

        Assert.True((await s.Coordinator.JoinRoomAsync(s.B, code)).IsSuccess);
        Assert.Equal("ROOM_FULL", (await s.Coordinator.JoinRoomAsync(s.C, code)).Error!.Code);
        Assert.Equal("ROOM_NOT_FOUND", (await s.Coordinator.JoinRoomAsync(s.C, "ZZZZZZ")).Error!.Code);
    }

    [Fact]
    public async Task Room_ExpiresAfterFiveMinutes_AndTheOwnerIsNotified()
    {
        var s = await NewSetupAsync();
        var code = RoomCode(await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId));

        await s.AdvanceAsync(299);
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.RoomClosed));
        await s.AdvanceAsync(2);

        Assert.Equal("expired", s.Notifier.For(s.A, MatchEvents.RoomClosed).Single().Payload.GetProperty("reason").GetString());
        Assert.Equal("ROOM_NOT_FOUND", (await s.Coordinator.JoinRoomAsync(s.B, code)).Error!.Code);
        // Oda kapanınca kurucu serbest
        Assert.True((await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId)).IsSuccess);
    }

    [Fact]
    public async Task Room_OwnerLeaving_ClosesIt()
    {
        var s = await NewSetupAsync();
        var code = RoomCode(await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId));
        await s.Coordinator.LeaveRoomAsync(s.A);

        Assert.Equal("ROOM_NOT_FOUND", (await s.Coordinator.JoinRoomAsync(s.B, code)).Error!.Code);
        Assert.True((await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId)).IsSuccess);
    }

    [Fact]
    public async Task Room_OwnerDisconnecting_ClosesIt()
    {
        var s = await NewSetupAsync();
        var code = RoomCode(await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId));
        await s.Coordinator.OnDisconnectedAsync(s.A);
        Assert.Equal("ROOM_NOT_FOUND", (await s.Coordinator.JoinRoomAsync(s.B, code)).Error!.Code);
    }

    private static string RoomCode(MatchResult<object> created) =>
        System.Text.Json.JsonSerializer.SerializeToElement(created.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)).GetProperty("code").GetString()!;

    // ---------------------------------------------------------------- Süren maç ve bağlantı

    [Fact]
    public async Task ActionsWithoutAMatch_ReturnNoActiveMatch()
    {
        var s = await NewSetupAsync();
        Assert.Equal("NO_ACTIVE_MATCH", (await s.Coordinator.SubmitAnswerAsync(s.A, 1, 0, null)).Error!.Code);
        Assert.Equal("NO_ACTIVE_MATCH", (await s.Coordinator.ForfeitAsync(s.A)).Error!.Code);
        Assert.Null(await s.Coordinator.GetStateAsync(s.A));
    }

    [Fact]
    public async Task DisconnectingWhileQueued_RemovesThePlayer()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.Coordinator.OnDisconnectedAsync(s.A);

        await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId);
        Assert.Empty(s.Notifier.For(s.B, MatchEvents.MatchFound));
    }

    [Fact]
    public async Task DisconnectingBeforeReady_CancelsTheMatch()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId);

        await s.Coordinator.OnDisconnectedAsync(s.A);

        Assert.Equal("not_ready", s.Notifier.For(s.B, MatchEvents.MatchCancelled).Single().Payload.GetProperty("reason").GetString());
        Assert.False(s.Coordinator.IsInMatch(s.B));
    }

    [Fact]
    public async Task InMatch_ActionsAreRoutedToTheMatch_DisconnectAndReconnectWork_AndFinishingFreesThePlayers()
    {
        var s = await NewSetupAsync();
        var code = RoomCode(await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId));
        await s.Coordinator.JoinRoomAsync(s.B, code);
        await s.AdvanceAsync(5.1);
        Assert.Single(s.Notifier.For(s.A, MatchEvents.QuestionStarted));

        // Cevap yönlendirme
        Assert.True((await s.Coordinator.SubmitAnswerAsync(s.A, 1, 0, null)).IsSuccess);
        Assert.Single(s.Notifier.For(s.B, MatchEvents.OpponentAnswered));

        // Kopma / yeniden bağlanma
        await s.Coordinator.OnDisconnectedAsync(s.B);
        Assert.Single(s.Notifier.For(s.A, MatchEvents.OpponentDisconnected));
        var state = await s.Coordinator.OnReconnectedAsync(s.B);
        Assert.NotNull(state);
        Assert.Single(s.Notifier.For(s.A, MatchEvents.OpponentReconnected));

        // Pes → maç biter, ikisi de serbest kalır
        Assert.True((await s.Coordinator.ForfeitAsync(s.A)).IsSuccess);
        Assert.False(s.Coordinator.IsInMatch(s.A));
        Assert.False(s.Coordinator.IsInMatch(s.B));
        Assert.True((await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId)).IsSuccess);
    }

    [Fact]
    public async Task ServerShutdown_CancelsRunningMatchesWithoutPoints()
    {
        var s = await NewSetupAsync();
        var code = RoomCode(await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId));
        await s.Coordinator.JoinRoomAsync(s.B, code);

        await s.Coordinator.CancelAllAsync(MatchEndReasonsVersus.ServerRestart);

        Assert.Equal("server_restart", s.Notifier.For(s.A, MatchEvents.MatchCancelled).Single().Payload.GetProperty("reason").GetString());
        Assert.False(s.Coordinator.IsInMatch(s.A));
    }

    [Fact]
    public async Task StaleVersusMatches_AreCancelledOnStartup_ButSoloMatchesAreKept()
    {
        var s = await NewSetupAsync();
        var code = RoomCode(await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId));
        await s.Coordinator.JoinRoomAsync(s.B, code);
        var matchId = s.Coordinator.RunnerOf(s.A)!.MatchId; // veritabanında "Running"

        using var scope = factory.Services.CreateScope();
        var cancelled = await scope.ServiceProvider.GetRequiredService<VersusStore>().CancelStaleAsync();
        Assert.True(cancelled >= 1);

        var db = scope.ServiceProvider.GetRequiredService<SenBilirsin.Modules.MatchEngine.Data.MatchEngineDbContext>();
        var match = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Matches, m => m.Id == matchId);
        Assert.Equal(MatchStatus.Cancelled, match.Status);
    }

    // ---------------------------------------------------------------- Rövanş

    private async Task<Guid> FinishedFriendMatchAsync(Setup s)
    {
        var code = RoomCode(await s.Coordinator.CreateRoomAsync(s.A, "branch", s.CategoryId));
        await s.Coordinator.JoinRoomAsync(s.B, code);
        var matchId = s.Coordinator.RunnerOf(s.A)!.MatchId;
        await s.AdvanceAsync(5.1);
        await s.Coordinator.ForfeitAsync(s.A);
        await s.Idle();
        return matchId;
    }

    [Fact]
    public async Task Rematch_AcceptedByTheOpponent_StartsANewMatchInTheSameCategory()
    {
        var s = await NewSetupAsync();
        var first = await FinishedFriendMatchAsync(s);

        Assert.True((await s.Coordinator.RequestRematchAsync(s.A, first)).IsSuccess);
        Assert.Equal(first, s.Notifier.For(s.B, MatchEvents.RematchRequested).Single().Payload.GetProperty("matchId").GetGuid());

        Assert.True((await s.Coordinator.RespondRematchAsync(s.B, first, true)).IsSuccess);

        Assert.True(s.Notifier.For(s.A, MatchEvents.RematchResult).Single().Payload.GetProperty("accepted").GetBoolean());
        var found = s.Notifier.For(s.B, MatchEvents.MatchFound).Last().Payload;
        Assert.NotEqual(first, found.GetProperty("matchId").GetGuid());
        Assert.Equal(s.CategoryId, found.GetProperty("categoryId").GetInt32());
        Assert.Equal(2, s.Notifier.For(s.A, MatchEvents.CountdownStarted).Count); // ilk maç + rövanş
        Assert.True(s.Coordinator.IsInMatch(s.A));
        Assert.True(s.Coordinator.IsInMatch(s.B));
    }

    [Fact]
    public async Task Rematch_Declined_NotifiesTheRequester_AndStartsNothing()
    {
        var s = await NewSetupAsync();
        var first = await FinishedFriendMatchAsync(s);
        await s.Coordinator.RequestRematchAsync(s.A, first);

        Assert.True((await s.Coordinator.RespondRematchAsync(s.B, first, false)).IsSuccess);

        var result = s.Notifier.For(s.A, MatchEvents.RematchResult).Single().Payload;
        Assert.False(result.GetProperty("accepted").GetBoolean());
        Assert.Equal("declined", result.GetProperty("reason").GetString());
        Assert.False(s.Coordinator.IsInMatch(s.A));
        Assert.Equal("NO_REMATCH", (await s.Coordinator.RespondRematchAsync(s.B, first, true)).Error!.Code); // istek artık yok
    }

    [Fact]
    public async Task Rematch_NotAnsweredIn30Seconds_TimesOut()
    {
        var s = await NewSetupAsync();
        var first = await FinishedFriendMatchAsync(s);
        await s.Coordinator.RequestRematchAsync(s.A, first);

        await s.AdvanceAsync(29);
        Assert.Empty(s.Notifier.For(s.A, MatchEvents.RematchResult));
        await s.AdvanceAsync(2);

        Assert.Equal("timeout", s.Notifier.For(s.A, MatchEvents.RematchResult).Single().Payload.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Rematch_BothRequesting_AcceptsAutomatically()
    {
        var s = await NewSetupAsync();
        var first = await FinishedFriendMatchAsync(s);
        await s.Coordinator.RequestRematchAsync(s.A, first);
        await s.Coordinator.RequestRematchAsync(s.B, first);

        Assert.True(s.Coordinator.IsInMatch(s.A));
        Assert.True(s.Coordinator.IsInMatch(s.B));
    }

    [Fact]
    public async Task Rematch_IsOnlyPossibleForTwoMinutes_ForTheRightMatch_AndNotForCancelledMatches()
    {
        var s = await NewSetupAsync();
        var first = await FinishedFriendMatchAsync(s);
        Assert.Equal("NO_REMATCH", (await s.Coordinator.RequestRematchAsync(s.A, Guid.NewGuid())).Error!.Code);
        Assert.Equal("NO_REMATCH", (await s.Coordinator.RequestRematchAsync(s.C, first)).Error!.Code);

        await s.AdvanceAsync(121);
        Assert.Equal("NO_REMATCH", (await s.Coordinator.RequestRematchAsync(s.A, first)).Error!.Code);

        // İptal edilen maçın rövanşı yok
        var s2 = await NewSetupAsync();
        var code = RoomCode(await s2.Coordinator.CreateRoomAsync(s2.A, "branch", s2.CategoryId));
        await s2.Coordinator.JoinRoomAsync(s2.B, code);
        var cancelled = s2.Coordinator.RunnerOf(s2.A)!.MatchId;
        await s2.Coordinator.CancelAllAsync(MatchEndReasonsVersus.ServerRestart);
        Assert.Equal("NO_REMATCH", (await s2.Coordinator.RequestRematchAsync(s2.A, cancelled)).Error!.Code);
    }

    [Fact]
    public async Task Rematch_WhenTheOpponentIsBusy_IsRefused()
    {
        var s = await NewSetupAsync();
        var first = await FinishedFriendMatchAsync(s);
        await s.Coordinator.JoinQueueAsync(s.B, "branch", s.CategoryId); // B başka işte

        Assert.Equal("OPPONENT_BUSY", (await s.Coordinator.RequestRematchAsync(s.A, first)).Error!.Code);
    }

    [Fact]
    public async Task Rematch_AgainstABot_StartsImmediatelyWithTheSameBot()
    {
        var s = await NewSetupAsync();
        await s.Coordinator.JoinQueueAsync(s.A, "branch", s.CategoryId);
        await s.AdvanceAsync(15.5);
        await s.Coordinator.ChooseFallbackAsync(s.A, "bot");
        var botName = s.Notifier.For(s.A, MatchEvents.MatchFound).Single().Payload.GetProperty("opponent").GetProperty("name").GetString();
        var first = s.Coordinator.RunnerOf(s.A)!.MatchId;
        await s.Coordinator.ForfeitAsync(s.A);
        await s.Idle();

        Assert.True((await s.Coordinator.RequestRematchAsync(s.A, first)).IsSuccess);

        var found = s.Notifier.For(s.A, MatchEvents.MatchFound).Last().Payload;
        Assert.Equal(botName, found.GetProperty("opponent").GetProperty("name").GetString());
        Assert.True(s.Coordinator.IsInMatch(s.A));
    }
}
