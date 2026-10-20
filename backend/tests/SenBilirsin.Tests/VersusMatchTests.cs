using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.MatchEngine.Versus;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace SenBilirsin.Tests;

/// <summary>Faz 4.2: rakipli maç koşucusu — sunucu zamanlaması, sonuç, puan, joker, pes, kopma havuzu, bot. Sahte saat + gerçek PostgreSQL.</summary>
[Collection("api")]
public class VersusMatchTests(MobileApiFactory factory)
{
    private static string Name(RecordingNotifier.Event e) => e.Name;

    // ---------------------------------------------------------------- Akış

    [Fact]
    public async Task FullMatch_FollowsTheTimeline_AndScoresTheWinner()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();

        // 5 sn geri sayım bildirimi iki tarafa gitti, henüz soru yok
        Assert.Single(h.Notifier.For(h.A, MatchEvents.CountdownStarted));
        Assert.Empty(h.Notifier.For(h.A, MatchEvents.QuestionStarted));

        await h.AdvanceAsync(4.9);
        Assert.Empty(h.Notifier.For(h.A, MatchEvents.QuestionStarted));
        await h.AdvanceAsync(0.2);
        var first = h.Notifier.For(h.A, MatchEvents.QuestionStarted).Single().Payload;
        Assert.Equal(1, first.GetProperty("position").GetInt32());
        Assert.Equal(15, (first.GetProperty("endsAt").GetDateTimeOffset() - first.GetProperty("startsAt").GetDateTimeOffset()).TotalSeconds);

        // A 10 doğru, B 7 doğru
        for (var pos = 1; pos <= 10; pos++) await h.PlayRoundAsync(pos, aCorrect: true, bCorrect: pos <= 7);

        Assert.True(h.Ended);
        var a = h.Notifier.For(h.A, MatchEvents.MatchFinished).Single().Payload;
        Assert.Equal("win", a.GetProperty("result").GetString());
        Assert.Equal(15, a.GetProperty("points").GetInt32());   // 10 kazanma + 5 bonus (8+ doğru)
        Assert.Equal(5, a.GetProperty("bonus").GetInt32());
        Assert.Equal(10, a.GetProperty("yourCorrect").GetInt32());
        Assert.Equal(7, a.GetProperty("opponentCorrect").GetInt32());
        var b = h.Notifier.For(h.B, MatchEvents.MatchFinished).Single().Payload;
        Assert.Equal("lose", b.GetProperty("result").GetString());
        Assert.Equal(0, b.GetProperty("points").GetInt32());

        var players = await h.PlayersInDbAsync();
        Assert.Equal((10, 15), (players.Single(p => p.Id == h.A).Correct, players.Single(p => p.Id == h.A).Points));
        Assert.Equal((7, 0), (players.Single(p => p.Id == h.B).Correct, players.Single(p => p.Id == h.B).Points));
        Assert.Equal(MatchStatus.Finished, await h.StatusInDbAsync());
    }

    [Fact]
    public async Task EveryQuestion_HasSameOptionsForBothPlayers_AndNeverLeaksTheAnswerBeforeTheResult()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);

        var qa = h.Notifier.For(h.A, MatchEvents.QuestionStarted).Single().Payload;
        var qb = h.Notifier.For(h.B, MatchEvents.QuestionStarted).Single().Payload;
        Assert.Equal(qa.GetProperty("options").GetRawText(), qb.GetProperty("options").GetRawText());
        Assert.Equal(qa.GetProperty("text").GetString(), qb.GetProperty("text").GetString());
        Assert.DoesNotContain("correct", qa.GetRawText().ToLowerInvariant());

        await h.AnswerAsync(h.A, 1, correct: true);
        // Rakip cevapladı bilgisi var ama cevabı yok
        var opp = h.Notifier.For(h.B, MatchEvents.OpponentAnswered).Single().Payload;
        Assert.Equal(1, opp.GetProperty("position").GetInt32());
        Assert.False(opp.TryGetProperty("option", out _));

        await h.AdvanceAsync(17); // süre (15) + tolerans (1,5)
        var result = h.Notifier.For(h.B, MatchEvents.QuestionResult).Single().Payload;
        Assert.Equal(await h.CorrectDisplayAsync(1), result.GetProperty("correctOption").GetInt32());
        Assert.False(result.GetProperty("yourCorrect").GetBoolean()); // B hiç cevaplamadı → yanlış
        Assert.True(result.GetProperty("opponentCorrect").GetBoolean());
    }

    [Fact]
    public async Task ResultAppearsOneSecondAfterBothAnswered_NotAtTheEndOfTheTimer()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);

        await h.AnswerAsync(h.A, 1, true);
        await h.AnswerAsync(h.B, 1, false);
        await h.AdvanceAsync(0.9);
        Assert.Empty(h.Notifier.For(h.A, MatchEvents.QuestionResult));
        await h.AdvanceAsync(0.2);
        Assert.Single(h.Notifier.For(h.A, MatchEvents.QuestionResult));

        // Sonraki soru 3 sn sonra
        await h.AdvanceAsync(2.7);
        Assert.Single(h.Notifier.For(h.A, MatchEvents.QuestionStarted));
        await h.AdvanceAsync(0.5);
        Assert.Equal(2, h.Notifier.For(h.A, MatchEvents.QuestionStarted).Count);
    }

    [Fact]
    public async Task UnansweredQuestion_IsRevealedAfterTimePlusGrace_AsWrongForBoth()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);

        await h.AdvanceAsync(15);
        Assert.Empty(h.Notifier.For(h.A, MatchEvents.QuestionResult)); // süre + 1,5 sn tolerans dolmadı
        await h.AdvanceAsync(1.6);
        var r = h.Notifier.For(h.A, MatchEvents.QuestionResult).Single().Payload;
        Assert.True(r.GetProperty("yourTimedOut").GetBoolean());
        Assert.False(r.GetProperty("yourCorrect").GetBoolean());
        Assert.Equal(0, r.GetProperty("yourScore").GetInt32());
    }

    [Fact]
    public async Task FirstTapIsFinal_WrongPositionAndEarlyTimeoutAreRejected()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        var right = await h.CorrectDisplayAsync(1);

        Assert.True((await h.Runner.SubmitAnswerAsync(h.A, 1, (right + 1) % 4, null)).IsSuccess);
        // İkinci istek (doğru şık) yok sayılır
        Assert.True((await h.Runner.SubmitAnswerAsync(h.A, 1, right, null)).IsSuccess);
        await h.Runner.WhenIdleAsync();

        Assert.Equal("WRONG_POSITION", (await h.Runner.SubmitAnswerAsync(h.B, 4, 0, null)).Error!.Code);
        Assert.Equal("TOO_EARLY", (await h.Runner.SubmitAnswerAsync(h.B, 1, null, null)).Error!.Code);
        Assert.Equal("OPTION_INVALID", (await h.Runner.SubmitAnswerAsync(h.B, 1, 9, null)).Error!.Code);
        Assert.Equal("NOT_IN_MATCH", (await h.Runner.SubmitAnswerAsync(Guid.NewGuid(), 1, 0, null)).Error!.Code);

        await h.AnswerAsync(h.B, 1, true);
        await h.AdvanceAsync(1.1);
        var a = h.Notifier.For(h.A, MatchEvents.QuestionResult).Single().Payload;
        Assert.False(a.GetProperty("yourCorrect").GetBoolean()); // ilk dokunuş yanlıştı
    }

    [Fact]
    public async Task Draws_PayCorrectCountAtOrAboveThreshold_AndNothingBelow()
    {
        var high = await VersusHarness.CreateAsync(factory);
        await high.Runner.StartAsync();
        await high.AdvanceAsync(5.1);
        for (var pos = 1; pos <= 10; pos++) await high.PlayRoundAsync(pos, pos <= 9, pos <= 9); // 9–9
        var players = await high.PlayersInDbAsync();
        Assert.All(players, p => Assert.Equal((9, 9, "draw"), (p.Correct, p.Points, p.Result)));

        var low = await VersusHarness.CreateAsync(factory);
        await low.Runner.StartAsync();
        await low.AdvanceAsync(5.1);
        for (var pos = 1; pos <= 10; pos++) await low.PlayRoundAsync(pos, pos <= 6, pos <= 6); // 6–6
        Assert.All(await low.PlayersInDbAsync(), p => Assert.Equal((6, 0, "draw"), (p.Correct, p.Points, p.Result)));
    }

    // ---------------------------------------------------------------- Joker, pes

    [Fact]
    public async Task Joker_IsPrivateToItsUser_SpendsTheLedger_AndFollowsTheOnePerQuestionRules()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);

        var used = await h.Runner.UseJokerAsync(h.A, 1, JokerType.Half);
        Assert.True(used.IsSuccess);
        Assert.Equal(2, used.Value!.Joker.Removed!.Count);
        Assert.DoesNotContain(await h.CorrectDisplayAsync(1), used.Value.Joker.Removed);
        Assert.Equal(4, used.Value.Jokers.Half);

        Assert.Single(h.Notifier.For(h.A, MatchEvents.JokerResult));
        Assert.Empty(h.Notifier.For(h.B, MatchEvents.JokerResult)); // rakip görmez

        Assert.Equal("JOKER_ALREADY_USED_THIS_QUESTION", (await h.Runner.UseJokerAsync(h.A, 1, JokerType.Hint)).Error!.Code);
        // Aynı joker tekrar istenirse ikinci kez harcanmaz
        Assert.Equal(4, (await h.Runner.UseJokerAsync(h.A, 1, JokerType.Half)).Value!.Jokers.Half);

        // B bağımsızdır: kendi jokerini kullanabilir
        Assert.True((await h.Runner.UseJokerAsync(h.B, 1, JokerType.Hint)).IsSuccess);

        await h.PlayRoundAsync(1, true, true);
        Assert.Equal("JOKER_TYPE_USED", (await h.Runner.UseJokerAsync(h.A, 2, JokerType.Half)).Error!.Code);
        Assert.True((await h.Runner.UseJokerAsync(h.A, 2, JokerType.Double)).IsSuccess);
    }

    [Fact]
    public async Task DoubleJoker_CountsAsCorrectIfEitherOptionIsRight()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        var right = await h.CorrectDisplayAsync(1);

        Assert.Equal("DOUBLE_NOT_ACTIVE", (await h.Runner.SubmitAnswerAsync(h.A, 1, (right + 1) % 4, right)).Error!.Code);
        Assert.True((await h.Runner.UseJokerAsync(h.A, 1, JokerType.Double)).IsSuccess);
        Assert.True((await h.Runner.SubmitAnswerAsync(h.A, 1, (right + 1) % 4, right)).IsSuccess);
        await h.Runner.SubmitAnswerAsync(h.B, 1, (right + 1) % 4, null);
        await h.AdvanceAsync(1.1);

        var a = h.Notifier.For(h.A, MatchEvents.QuestionResult).Single().Payload;
        Assert.True(a.GetProperty("yourCorrect").GetBoolean());
        Assert.Equal(1, a.GetProperty("yourScore").GetInt32());
    }

    [Fact]
    public async Task Joker_WithoutBalance_IsRefused()
    {
        var h = await VersusHarness.CreateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var wallet = scope.ServiceProvider.GetRequiredService<IJokerWallet>();
            for (var i = 0; i < 5; i++) await wallet.TrySpendAsync(h.A, JokerType.Hint, "t", $"drain:{h.A:N}:{i}");
        }

        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        Assert.Equal("NO_JOKER", (await h.Runner.UseJokerAsync(h.A, 1, JokerType.Hint)).Error!.Code);
    }

    [Fact]
    public async Task Forfeit_EndsTheMatchImmediately_OpponentWins()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        await h.PlayRoundAsync(1, true, true);
        await h.PlayRoundAsync(2, true, false);

        await h.Runner.ForfeitAsync(h.A);

        Assert.True(h.Ended);
        Assert.Equal("lose", h.Notifier.For(h.A, MatchEvents.MatchFinished).Single().Payload.GetProperty("result").GetString());
        var b = h.Notifier.For(h.B, MatchEvents.MatchFinished).Single().Payload;
        Assert.Equal("win", b.GetProperty("result").GetString());
        Assert.Equal(10, b.GetProperty("points").GetInt32());
        Assert.Equal("forfeit", b.GetProperty("reason").GetString());

        // Bittikten sonra zamanlayıcılar yeni soru göndermez
        var before = h.Notifier.For(h.B, MatchEvents.QuestionStarted).Count;
        await h.AdvanceAsync(60);
        Assert.Equal(before, h.Notifier.For(h.B, MatchEvents.QuestionStarted).Count);
    }

    // ---------------------------------------------------------------- Kopma

    [Fact]
    public async Task Disconnect_DuringAQuestion_DoesNotStopTheClock_ThenPausesBetweenQuestions()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        await h.AnswerAsync(h.B, 1, true);

        await h.Runner.PlayerDisconnectedAsync(h.A);
        var notice = h.Notifier.For(h.B, MatchEvents.OpponentDisconnected).Single().Payload;
        Assert.Equal(30000, notice.GetProperty("remainingMs").GetInt32());

        // Soru süresi dolana kadar akış sürer: A cevapsız sayılır
        await h.AdvanceAsync(16.6);
        Assert.Single(h.Notifier.For(h.B, MatchEvents.QuestionResult));

        // Sorular arası: maç duraklar, sıradaki soru gelmez
        await h.AdvanceAsync(10);
        Assert.Single(h.Notifier.For(h.B, MatchEvents.QuestionStarted));
        Assert.False(h.Ended);
    }

    [Fact]
    public async Task Reconnect_ResumesTheMatch_AndOnlyTheTimeAwayIsChargedToThePool()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        await h.PlayRoundAsync(1, true, true);
        Assert.Equal(2, h.Notifier.For(h.A, MatchEvents.QuestionStarted).Count);

        await h.AnswerAsync(h.A, 2, true);
        await h.AnswerAsync(h.B, 2, true);
        await h.AdvanceAsync(1.1);          // 2. soru sonucu gösterildi; şimdi sonuç ekranı
        await h.Runner.PlayerDisconnectedAsync(h.A); // sonuç ekranında kopma → duraklama
        await h.AdvanceAsync(12);           // 12 sn uzakta
        Assert.Equal(2, h.Notifier.For(h.A, MatchEvents.QuestionStarted).Count); // sıradaki soru başlamadı

        var state = await h.Runner.PlayerReconnectedAsync(h.A);
        Assert.NotNull(state);
        Assert.Single(h.Notifier.For(h.B, MatchEvents.OpponentReconnected));

        await h.AdvanceAsync(1.1);          // yeniden başlatma payı 1 sn
        Assert.Equal(3, h.Notifier.For(h.B, MatchEvents.QuestionStarted).Count);

        // Maçı bitir ve havuzdan yalnızca ~12 sn düşüldüğünü doğrula
        await h.Runner.ForfeitAsync(h.B);
        var a = (await h.PlayersInDbAsync()).Single(p => p.Id == h.A);
        Assert.InRange(a.Pool, 11_000, 13_000);
    }

    [Fact]
    public async Task Disconnect_ExhaustingThe30SecondPool_LosesTheMatch()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        await h.PlayRoundAsync(1, true, true);
        await h.AnswerAsync(h.A, 2, true);
        await h.AnswerAsync(h.B, 2, true);
        await h.AdvanceAsync(1.1);
        await h.Runner.PlayerDisconnectedAsync(h.A);

        await h.AdvanceAsync(29);
        Assert.False(h.Ended);
        await h.AdvanceAsync(1.5);

        Assert.True(h.Ended);
        var b = h.Notifier.For(h.B, MatchEvents.MatchFinished).Single().Payload;
        Assert.Equal("win", b.GetProperty("result").GetString());
        Assert.Equal("disconnect", b.GetProperty("reason").GetString());
        Assert.Equal("lose", h.Notifier.For(h.A, MatchEvents.MatchFinished).Single().Payload.GetProperty("result").GetString());
        Assert.Equal(30_000, (await h.PlayersInDbAsync()).Single(p => p.Id == h.A).Pool);
    }

    [Fact]
    public async Task BothDisconnecting_CancelsTheMatch_WithoutPoints()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        await h.PlayRoundAsync(1, true, false);

        await h.Runner.PlayerDisconnectedAsync(h.A);
        await h.Runner.PlayerDisconnectedAsync(h.B);

        Assert.True(h.Ended);
        Assert.Equal(MatchStatus.Cancelled, await h.StatusInDbAsync());
        Assert.Equal("both_disconnected", h.Notifier.For(h.A, MatchEvents.MatchCancelled).Single().Payload.GetProperty("reason").GetString());
        Assert.All(await h.PlayersInDbAsync(), p => Assert.Equal((0, "cancelled"), (p.Points, p.Result)));
    }

    [Fact]
    public async Task Reconnect_DuringAQuestion_ReturnsTheOpenQuestionWithRemainingTime()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);

        await h.Runner.PlayerDisconnectedAsync(h.A);
        await h.AdvanceAsync(6);
        var state = System.Text.Json.JsonSerializer.SerializeToElement(await h.Runner.PlayerReconnectedAsync(h.A), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        var q = state.GetProperty("question");
        Assert.Equal("Soru", q.GetProperty("text").GetString()!.Split(' ')[0]);
        var left = (q.GetProperty("endsAt").GetDateTimeOffset() - state.GetProperty("serverNow").GetDateTimeOffset()).TotalSeconds;
        Assert.InRange(left, 8.5, 9.5); // 15 sn'nin 6 sn'si koptu → ~9 sn kaldı
        Assert.False(state.GetProperty("paused").GetBoolean());
    }

    // ---------------------------------------------------------------- Bot

    [Fact]
    public async Task BotMatch_PlaysItself_AnswersBetween3And12Seconds_AndGetsNoPointsCredited()
    {
        var bot = new BotProfile { Id = Guid.NewGuid(), Name = "Test Bot", AvatarId = 3, Power = 3, SpeedMinMs = 3000, SpeedMaxMs = 12000 };
        var h = await VersusHarness.CreateAsync(factory, bot);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);

        // İnsan her soruda hemen doğru cevaplar; bot kendi hızıyla (3–12 sn) gelir
        var botAnswerTimes = new List<double>();
        for (var pos = 1; pos <= 10; pos++)
        {
            await h.UntilAsync(() => h.Notifier.For(h.A, MatchEvents.QuestionStarted).Count >= pos);
            var started = h.Clock.GetUtcNow();
            await h.AnswerAsync(h.A, pos, true);
            // Bot cevabını bekle: "rakip cevapladı" bildirimi yalnızca insana gider, bu yüzden sonuç olayını bekle
            await h.UntilAsync(() => h.Notifier.For(h.A, MatchEvents.QuestionResult).Count >= pos);
            botAnswerTimes.Add((h.Clock.GetUtcNow() - started).TotalSeconds);
            await h.UntilAsync(() => h.Notifier.For(h.A, MatchEvents.QuestionStarted).Count >= pos + 1 || h.Ended);
        }

        // Sonuç, botun cevabından ~1 sn sonra gelir: bot süresi 3–12 sn → toplam 4–13 sn (adım payı dahil)
        Assert.All(botAnswerTimes, t => Assert.InRange(t, 3.5, 13.5));
        Assert.True(h.Ended);
        var finished = h.Notifier.For(h.A, MatchEvents.MatchFinished).Single().Payload;
        Assert.True(finished.GetProperty("opponent").GetProperty("isBot").GetBoolean());
        Assert.Equal(10, finished.GetProperty("yourCorrect").GetInt32());

        var players = await h.PlayersInDbAsync();
        var botRow = players.Single(p => p.Id == bot.Id);
        Assert.True(botRow.IsBot);
        Assert.False(players.Single(p => p.Id == h.A).IsBot);
        Assert.Empty(h.Notifier.For(bot.Id)); // bota olay gönderilmez
    }

    [Fact]
    public async Task BotMatch_HumanDisconnectIsHandledLikeAHumanMatch_NotCancelled()
    {
        var bot = new BotProfile { Id = Guid.NewGuid(), Name = "Test Bot", AvatarId = 3, Power = 2, SpeedMinMs = 3000, SpeedMaxMs = 6000 };
        var h = await VersusHarness.CreateAsync(factory, bot);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        await h.AdvanceAsync(17);   // soru bitti, sonuç gösterildi
        await h.Runner.PlayerDisconnectedAsync(h.A);
        Assert.False(h.Ended);      // iki gerçek oyuncu yok → iptal değil

        await h.AdvanceAsync(31);
        Assert.True(h.Ended);
        Assert.Equal("lose", h.Notifier.For(h.A, MatchEvents.MatchFinished).Single().Payload.GetProperty("result").GetString());
        Assert.Equal(MatchStatus.Finished, await h.StatusInDbAsync());
    }

    [Fact]
    public async Task ServerRestart_CancelsTheMatchWithoutPoints()
    {
        var h = await VersusHarness.CreateAsync(factory);
        await h.Runner.StartAsync();
        await h.AdvanceAsync(5.1);
        await h.Runner.CancelAsync(MatchEndReasonsVersus.ServerRestart);

        Assert.Equal(MatchStatus.Cancelled, await h.StatusInDbAsync());
        Assert.Equal("server_restart", h.Notifier.For(h.B, MatchEvents.MatchCancelled).Single().Payload.GetProperty("reason").GetString());
    }
}
