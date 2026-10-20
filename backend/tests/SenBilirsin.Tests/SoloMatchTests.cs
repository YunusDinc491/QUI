using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Catalog.Domain;
using SenBilirsin.Modules.MatchEngine.Data;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Questions.Data;
using SenBilirsin.Modules.Questions.Domain;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 3.4: bireysel maç — başlatma, cevap, süre, puan, joker, reklamla devam, pes, terk. Gerçek PostgreSQL + API.</summary>
[Collection("api")]
public class SoloMatchTests(MobileApiFactory factory)
{
    private sealed record Player(HttpClient Client, Guid UserId);

    // ---------------------------------------------------------------- Yardımcılar

    private async Task<Player> NewPlayerAsync(string deviceId = "device-1")
    {
        var uid = Guid.NewGuid().ToString("N");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Dev uid={uid};email={uid}@test.local;verified=true;provider=google.com");
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
        var register = await client.PostAsJsonAsync("/api/users/me", new { username = "u" + uid[..10], displayName = "Oyuncu", avatarId = 1, birthYear = 2000, termsAccepted = true });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        await client.PostAsJsonAsync("/api/users/me/session", new { deviceId, platform = "test" });
        var me = await client.GetFromJsonAsync<JsonElement>("/api/users/me");
        return new Player(client, me.GetProperty("id").GetGuid());
    }

    /// <summary>Yayında bir branş kategorisi ve yeterli soru (varsayılan 14 kolay / 10 orta / 10 zor) ekler.</summary>
    private async Task<int> NewCategoryAsync(int easy = 14, int medium = 10, int hard = 10, bool published = true, string mode = ModeCodes.Branch)
    {
        using var scope = factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var category = new Category { ModeId = catalog.Modes.Single(m => m.Code == mode).Id, Name = "K" + Guid.NewGuid().ToString("N")[..10], IsPublished = published, CreatedAt = DateTime.UtcNow };
        catalog.Categories.Add(category);
        await catalog.SaveChangesAsync();

        var questions = scope.ServiceProvider.GetRequiredService<QuestionsDbContext>();
        void Add(Difficulty d, int count)
        {
            for (var i = 0; i < count; i++)
                questions.Questions.Add(new Question
                {
                    CategoryId = category.Id, Text = $"Soru {Guid.NewGuid():N}", OptionA = "Bir", OptionB = "İki", OptionC = "Üç", OptionD = "Dört",
                    CorrectIndex = i % 4, Difficulty = d, Status = QuestionStatus.Published, NormalizedText = Guid.NewGuid().ToString("N"),
                    Explanation = "Açıklama", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
        }

        Add(Difficulty.Easy, easy);
        Add(Difficulty.Medium, medium);
        Add(Difficulty.Hard, hard);
        await questions.SaveChangesAsync();
        return category.Id;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement e) => e.GetProperty("code").GetString()!;

    private async Task<(Guid MatchId, JsonElement State)> StartAsync(Player p, int categoryId, string mode = "branch")
    {
        var response = await p.Client.PostAsJsonAsync("/api/matches/solo", new { mode, categoryId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var state = await Json(response);
        return (state.GetProperty("matchId").GetGuid(), state);
    }

    /// <summary>Sunucudaki doğru şıkkı (ekran sırasına çevrilmiş) okur — istemci bunu asla bilemez; yalnızca test okur.</summary>
    private async Task<int> CorrectDisplayAsync(Guid matchId, int position)
    {
        using var scope = factory.Services.CreateScope();
        var matches = scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>();
        var mq = (await matches.Questions.Where(q => q.MatchId == matchId && q.Position == position).ToListAsync()).OrderByDescending(q => q.Attempt).First();
        var question = await scope.ServiceProvider.GetRequiredService<QuestionsDbContext>().Questions.AsNoTracking().SingleAsync(q => q.Id == mq.QuestionId);
        return OptionShuffler.ToDisplay(OptionShuffler.Parse(mq.OptionOrder), question.CorrectIndex);
    }

    private async Task<int> CurrentQuestionIdAsync(Guid matchId, int position)
    {
        using var scope = factory.Services.CreateScope();
        var matches = scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>();
        return (await matches.Questions.Where(q => q.MatchId == matchId && q.Position == position).ToListAsync()).OrderByDescending(q => q.Attempt).First().QuestionId;
    }

    private void AdvanceTo(JsonElement question)
    {
        var startsAt = question.GetProperty("startsAt").GetDateTimeOffset();
        if (startsAt > factory.Clock.GetUtcNow()) factory.Clock.Advance(startsAt - factory.Clock.GetUtcNow());
    }

    /// <summary>Soru başlayana kadar saati ilerletir ve verilen şıkla cevaplar.</summary>
    private async Task<JsonElement> AnswerAsync(Player p, Guid matchId, JsonElement state, int? option, int? second = null, bool correct = false)
    {
        var question = state.GetProperty("question");
        AdvanceTo(question);
        var position = question.GetProperty("position").GetInt32();
        if (correct) option = await CorrectDisplayAsync(matchId, position);
        var response = await p.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position, option, second });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private async Task<int> WrongOptionAsync(Guid matchId, int position) => ((await CorrectDisplayAsync(matchId, position)) + 1) % 4;

    /// <summary>Verilen sayıda soruyu doğru cevaplar; son durumu döndürür.</summary>
    private async Task<JsonElement> PlayCorrectAsync(Player p, Guid matchId, JsonElement state, int count)
    {
        for (var i = 0; i < count; i++)
            state = (await AnswerAsync(p, matchId, state, null, correct: true)).GetProperty("state");
        return state;
    }

    private async Task<(int Points, int Correct, string? Result)> DbResultAsync(Guid matchId)
    {
        using var scope = factory.Services.CreateScope();
        var player = await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Players.AsNoTracking().SingleAsync(x => x.MatchId == matchId);
        return (player.Points, player.CorrectCount, player.Result);
    }

    // ---------------------------------------------------------------- Başlatma

    [Fact]
    public async Task Start_ReturnsFirstQuestion_WithoutAnyCorrectAnswerInformation()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();

        var (_, state) = await StartAsync(player, category);

        Assert.Equal("running", state.GetProperty("status").GetString());
        Assert.Equal(1, state.GetProperty("position").GetInt32());
        Assert.Equal(10, state.GetProperty("totalQuestions").GetInt32());
        var question = state.GetProperty("question");
        Assert.Equal(4, question.GetProperty("options").GetArrayLength());
        Assert.Equal(15, (question.GetProperty("endsAt").GetDateTimeOffset() - question.GetProperty("startsAt").GetDateTimeOffset()).TotalSeconds);
        Assert.Equal(5, state.GetProperty("jokers").GetProperty("half").GetInt32());

        // Doğru cevabı sızdıran hiçbir alan olmamalı
        var raw = state.GetRawText().ToLowerInvariant();
        Assert.DoesNotContain("correct", raw.Replace("correctcount", ""));
        Assert.DoesNotContain("explanation", raw);
    }

    [Theory]
    [InlineData("bilinmeyen")]
    [InlineData(null)]
    public async Task Start_RejectsInvalidMode(string? mode)
    {
        var player = await NewPlayerAsync();
        var response = await player.Client.PostAsJsonAsync("/api/matches/solo", new { mode });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("MODE_INVALID", Code(await Json(response)));
    }

    [Fact]
    public async Task Start_RejectsUnpublishedOrForeignCategory_AndTooSmallPools()
    {
        var player = await NewPlayerAsync();
        var hidden = await NewCategoryAsync(published: false);
        var kpss = await NewCategoryAsync(mode: ModeCodes.Kpss);
        var small = await NewCategoryAsync(3, 3, 3);

        var r1 = await player.Client.PostAsJsonAsync("/api/matches/solo", new { mode = "branch", categoryId = hidden });
        Assert.Equal(HttpStatusCode.NotFound, r1.StatusCode);
        var r2 = await player.Client.PostAsJsonAsync("/api/matches/solo", new { mode = "branch", categoryId = kpss });
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);
        var r3 = await player.Client.PostAsJsonAsync("/api/matches/solo", new { mode = "branch", categoryId = small });
        Assert.Equal(HttpStatusCode.Conflict, r3.StatusCode);
        Assert.Equal("NOT_ENOUGH_QUESTIONS", Code(await Json(r3)));
    }

    [Fact]
    public async Task Start_WhileAnotherMatchIsRunning_Returns409WithTheMatchId_AndCurrentResumesIt()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, _) = await StartAsync(player, category);

        var second = await player.Client.PostAsJsonAsync("/api/matches/solo", new { mode = "branch", categoryId = category });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await Json(second);
        Assert.Equal("MATCH_IN_PROGRESS", Code(body));
        Assert.Equal(matchId, body.GetProperty("matchId").GetGuid());

        var current = await player.Client.GetAsync("/api/matches/current");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(matchId, (await Json(current)).GetProperty("matchId").GetGuid());
    }

    [Fact]
    public async Task Current_WithoutActiveMatch_Returns404()
    {
        var player = await NewPlayerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await player.Client.GetAsync("/api/matches/current")).StatusCode);
    }

    [Fact]
    public async Task MixedMode_DrawsFromEveryPublishedCategoryOfTheMode()
    {
        var player = await NewPlayerAsync();
        // Bu modda yeni iki yayında kategori; her biri tek başına yetersiz (karışık olunca yetmeli)
        var a = await NewCategoryAsync(2, 2, 2, mode: ModeCodes.Fan);
        var b = await NewCategoryAsync(3, 3, 3, mode: ModeCodes.Fan);
        _ = (a, b);

        var response = await player.Client.PostAsJsonAsync("/api/matches/solo", new { mode = "fan" });
        // Başka testlerden kalan fan kategorileri de havuza girer; en az bu ikisiyle 5/5/5 soru vardır → başlamalı
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var state = await Json(response);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("categoryId").ValueKind);
    }

    // ---------------------------------------------------------------- Oynanış ve puan

    [Fact]
    public async Task PerfectGame_TenCorrect_Gives100Points_AndASummary()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);

        state = await PlayCorrectAsync(player, matchId, state, 10);

        Assert.Equal("finished", state.GetProperty("status").GetString());
        var result = state.GetProperty("result");
        Assert.Equal("completed", result.GetProperty("reason").GetString());
        Assert.Equal(100, result.GetProperty("points").GetInt32());
        Assert.Equal(10, result.GetProperty("correctCount").GetInt32());
        Assert.True(result.GetProperty("settled").GetBoolean());
        Assert.Equal((100, 10, "completed"), await DbResultAsync(matchId));

        var summary = await Json(await player.Client.GetAsync($"/api/matches/{matchId}/summary"));
        Assert.Equal(10, summary.GetProperty("items").GetArrayLength());
        Assert.All(summary.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.True(item.GetProperty("isCorrect").GetBoolean());
            Assert.Equal(item.GetProperty("correctOption").GetInt32(), item.GetProperty("chosenOption").GetInt32());
            Assert.Equal("Açıklama", item.GetProperty("explanation").GetString());
        });
    }

    [Theory]
    [InlineData(9, 10)] // 9 doğru, 10. soruda yanlış → 10 puan
    [InlineData(8, 5)]  // 8 doğru → 5 puan
    [InlineData(7, 0)]  // 7 doğru → 0 puan
    [InlineData(0, 0)]
    public async Task WrongAnswer_EliminatesAndPointsFollowTheLadder_AfterDeclining(int correctBefore, int expectedPoints)
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        state = await PlayCorrectAsync(player, matchId, state, correctBefore);

        var position = state.GetProperty("question").GetProperty("position").GetInt32();
        var wrong = await WrongOptionAsync(matchId, position);
        var outcome = await AnswerAsync(player, matchId, state, wrong);
        Assert.False(outcome.GetProperty("isCorrect").GetBoolean());
        Assert.Equal("awaiting_continue", outcome.GetProperty("state").GetProperty("status").GetString());

        var declined = await player.Client.PostAsync($"/api/matches/{matchId}/decline", null);
        var final = await Json(declined);
        Assert.Equal("finished", final.GetProperty("status").GetString());
        Assert.Equal("eliminated", final.GetProperty("result").GetProperty("reason").GetString());
        Assert.Equal(expectedPoints, final.GetProperty("result").GetProperty("points").GetInt32());
        Assert.Equal((expectedPoints, correctBefore, "eliminated"), await DbResultAsync(matchId));
    }

    [Fact]
    public async Task AnswerResult_RevealsTheCorrectOptionAndExplanation_OnlyAfterAnswering()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);

        var outcome = await AnswerAsync(player, matchId, state, null, correct: true);

        Assert.True(outcome.GetProperty("isCorrect").GetBoolean());
        Assert.Equal(await CorrectDisplayAsync(matchId, 1), outcome.GetProperty("correctOption").GetInt32());
        Assert.Equal("Açıklama", outcome.GetProperty("explanation").GetString());
        // Sıradaki soru 3 sn sonra başlar (sonuç ekranı süresi)
        var next = outcome.GetProperty("state").GetProperty("question");
        Assert.Equal(2, next.GetProperty("position").GetInt32());
        Assert.InRange((next.GetProperty("startsAt").GetDateTimeOffset() - outcome.GetProperty("state").GetProperty("serverNow").GetDateTimeOffset()).TotalSeconds, 2.9, 3.1);
    }

    [Fact]
    public async Task DifficultyOrder_FollowsTheFixedPlan_4Easy3Medium3Hard()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, _) = await StartAsync(player, category);

        using var scope = factory.Services.CreateScope();
        var slots = await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Questions.Where(q => q.MatchId == matchId)
            .OrderBy(q => q.Position).Select(q => q.Slot).ToListAsync();
        Assert.Equal([.. Enumerable.Repeat(Difficulty.Easy, 4), .. Enumerable.Repeat(Difficulty.Medium, 3), .. Enumerable.Repeat(Difficulty.Hard, 3)], slots);
    }

    // ---------------------------------------------------------------- Zamanlama

    [Fact]
    public async Task Answer_BeforeTheQuestionStarts_IsRejected_AndNotRecorded()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, _) = await StartAsync(player, category);

        // Saat ilerletilmedi: ilk soru 2 sn sonra başlıyor (tolerans 1 sn)
        var response = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = 0 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("TOO_EARLY", Code(await Json(response)));
    }

    [Fact]
    public async Task Answer_AfterTheDeadlinePlusGrace_CountsAsTimedOutEvenWithTheRightOption()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        var question = state.GetProperty("question");
        AdvanceTo(question);
        factory.Clock.Advance(TimeSpan.FromSeconds(15) + TimeSpan.FromMilliseconds(1600)); // süre + 1,5 sn tolerans geçti

        var correct = await CorrectDisplayAsync(matchId, 1);
        var response = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = correct });
        var body = await Json(response);

        Assert.False(body.GetProperty("isCorrect").GetBoolean());
        Assert.True(body.GetProperty("timedOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("chosenOption").ValueKind);
    }

    [Fact]
    public async Task Answer_JustInsideTheGrace_IsStillAccepted()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));
        factory.Clock.Advance(TimeSpan.FromSeconds(15) + TimeSpan.FromMilliseconds(1000));

        var correct = await CorrectDisplayAsync(matchId, 1);
        var body = await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = correct }));
        Assert.True(body.GetProperty("isCorrect").GetBoolean());
    }

    [Fact]
    public async Task TimeoutNotice_IsOnlyAcceptedOnceTheTimeIsUp()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));
        factory.Clock.Advance(TimeSpan.FromSeconds(5));

        var early = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = (int?)null });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        factory.Clock.Advance(TimeSpan.FromSeconds(10));
        var ontime = await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = (int?)null }));
        Assert.True(ontime.GetProperty("timedOut").GetBoolean());
        Assert.Equal("awaiting_continue", ontime.GetProperty("state").GetProperty("status").GetString());
    }

    [Fact]
    public async Task SecondAnswerToTheSameQuestion_ReturnsTheStoredResult_FirstTapIsFinal()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        var wrong = await WrongOptionAsync(matchId, 1);

        var first = await AnswerAsync(player, matchId, state, wrong);
        Assert.False(first.GetProperty("isCorrect").GetBoolean());

        // Şimdi doğru şıkla "değiştirmeyi" deneyen ikinci istek: sonuç aynı kalır
        var correct = await CorrectDisplayAsync(matchId, 1);
        var again = await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = correct }));
        Assert.False(again.GetProperty("isCorrect").GetBoolean());
        Assert.Equal(wrong, again.GetProperty("chosenOption").GetInt32());
        Assert.Equal(0, (await DbResultAsync(matchId)).Correct);
    }

    [Fact]
    public async Task ParallelIdenticalAnswers_CountOnlyOnce()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));
        var correct = await CorrectDisplayAsync(matchId, 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = correct })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, (await DbResultAsync(matchId)).Correct);
    }

    [Fact]
    public async Task Answer_ForTheWrongPosition_IsRejected()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));

        var response = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 5, option = 0 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("WRONG_POSITION", Code(await Json(response)));

        var bad = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = 7 });
        Assert.Equal("OPTION_INVALID", Code(await Json(bad)));
    }

    // ---------------------------------------------------------------- Reklamla devam

    [Fact]
    public async Task ContinueWithAd_ReplacesTheFailedQuestionWithANewEasyOne_AndAllowsFinishing()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        state = await PlayCorrectAsync(player, matchId, state, 6);

        var failedQuestionId = await CurrentQuestionIdAsync(matchId, 7);
        var wrong = await AnswerAsync(player, matchId, state, await WrongOptionAsync(matchId, 7));
        Assert.Equal("awaiting_continue", wrong.GetProperty("state").GetProperty("status").GetString());
        Assert.True(wrong.GetProperty("state").TryGetProperty("continueOffer", out var offer) && offer.ValueKind == JsonValueKind.Object);

        var cont = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/continue", new { adIntentId = "test-" + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.OK, cont.StatusCode);
        state = await Json(cont);
        Assert.Equal("running", state.GetProperty("status").GetString());
        Assert.Equal(7, state.GetProperty("position").GetInt32());

        // Yerine gelen soru farklı ve "kolay" havuzdan
        var replacementId = await CurrentQuestionIdAsync(matchId, 7);
        Assert.NotEqual(failedQuestionId, replacementId);
        using (var scope = factory.Services.CreateScope())
        {
            var slots = await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Questions
                .Where(q => q.MatchId == matchId && q.Position == 7).OrderBy(q => q.Attempt).Select(q => q.Slot).ToListAsync();
            Assert.Equal([Difficulty.Medium, Difficulty.Easy], slots);
            var all = await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Questions.Where(q => q.MatchId == matchId).Select(q => q.QuestionId).ToListAsync();
            Assert.Equal(all.Count, all.Distinct().Count());
        }

        // 7..10'u doğru oynayıp 10 doğru değil; 6 + 4 = 10 doğru → 100 puan
        state = await PlayCorrectAsync(player, matchId, state, 4);
        Assert.Equal("finished", state.GetProperty("status").GetString());
        Assert.Equal(100, state.GetProperty("result").GetProperty("points").GetInt32());
    }

    [Fact]
    public async Task SecondWrongAnswer_AfterUsingTheContinue_EndsTheMatchImmediately()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        state = await PlayCorrectAsync(player, matchId, state, 2);

        await AnswerAsync(player, matchId, state, await WrongOptionAsync(matchId, 3));
        state = await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/continue", new { adIntentId = "test-" + Guid.NewGuid().ToString("N") }));

        var second = await AnswerAsync(player, matchId, state, await WrongOptionAsync(matchId, 3));
        Assert.Equal("finished", second.GetProperty("state").GetProperty("status").GetString());
        Assert.Equal("eliminated", second.GetProperty("state").GetProperty("result").GetProperty("reason").GetString());

        var again = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/continue", new { adIntentId = "test-x" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Continue_NeedsAVerifiedAd_AndAnAdCanOnlyBeUsedOnce()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        await AnswerAsync(player, matchId, state, await WrongOptionAsync(matchId, 1));

        foreach (var bad in new[] { null, "", "gercek-reklam", new string('t', 200) })
        {
            var rejected = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/continue", new { adIntentId = bad });
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            Assert.Equal("AD_NOT_VERIFIED", Code(await Json(rejected)));
        }

        var intent = "test-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/continue", new { adIntentId = intent })).StatusCode);

        // Aynı reklam niyeti başka bir maçta tekrar kullanılamaz
        var p2 = await NewPlayerAsync();
        _ = p2;
        var other = await StartedWrongMatchAsync(player, category, matchId);
        var reuse = await player.Client.PostAsJsonAsync($"/api/matches/{other}/continue", new { adIntentId = intent });
        Assert.Equal(HttpStatusCode.Forbidden, reuse.StatusCode);
    }

    private async Task<Guid> StartedWrongMatchAsync(Player player, int category, Guid finishedBefore)
    {
        // Önceki maçı bitir (pes), yenisini başlat ve ilk soruda yanlış yap
        await player.Client.PostAsync($"/api/matches/{finishedBefore}/forfeit", null);
        var (matchId, state) = await StartAsync(player, category);
        await AnswerAsync(player, matchId, state, await WrongOptionAsync(matchId, 1));
        return matchId;
    }

    [Fact]
    public async Task ContinueOffer_ExpiresAfterTheWindow_AndTheMatchIsFinalised()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        state = await PlayCorrectAsync(player, matchId, state, 8);
        await AnswerAsync(player, matchId, state, await WrongOptionAsync(matchId, 9));

        factory.Clock.Advance(TimeSpan.FromSeconds(46));

        var late = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/continue", new { adIntentId = "test-" + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal((5, 8, "eliminated"), await DbResultAsync(matchId));
        Assert.Equal(HttpStatusCode.NotFound, (await player.Client.GetAsync("/api/matches/current")).StatusCode);
    }

    // ---------------------------------------------------------------- Pes ve terk

    [Fact]
    public async Task Forfeit_ScoresByTheCorrectAnswersSoFar()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        state = await PlayCorrectAsync(player, matchId, state, 8);

        var forfeited = await Json(await player.Client.PostAsync($"/api/matches/{matchId}/forfeit", null));
        Assert.Equal("forfeited", forfeited.GetProperty("status").GetString());
        Assert.Equal(5, forfeited.GetProperty("result").GetProperty("points").GetInt32());
        Assert.Equal((5, 8, "forfeit"), await DbResultAsync(matchId));

        // Tekrar çağrı zararsız: aynı sonuç
        var again = await Json(await player.Client.PostAsync($"/api/matches/{matchId}/forfeit", null));
        Assert.Equal(5, again.GetProperty("result").GetProperty("points").GetInt32());
    }

    [Fact]
    public async Task Forfeit_WithFewCorrect_GivesNoPoints_AndFreesThePlayerToStartANewMatch()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        await PlayCorrectAsync(player, matchId, state, 3);

        await player.Client.PostAsync($"/api/matches/{matchId}/forfeit", null);
        Assert.Equal((0, 3, "forfeit"), await DbResultAsync(matchId));

        var next = await player.Client.PostAsJsonAsync("/api/matches/solo", new { mode = "branch", categoryId = category });
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
    }

    [Fact]
    public async Task AbandonedMatch_IsCancelledWithoutPoints_AfterTheAbandonWindow()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        state = await PlayCorrectAsync(player, matchId, state, 8); // 8 doğru yapıp ortadan kayboluyor

        factory.Clock.Advance(TimeSpan.FromSeconds(3 + 15 + 121));

        Assert.Equal(HttpStatusCode.NotFound, (await player.Client.GetAsync("/api/matches/current")).StatusCode);
        Assert.Equal((0, 8, "abandoned"), await DbResultAsync(matchId));
        var summary = await Json(await player.Client.GetAsync($"/api/matches/{matchId}/summary"));
        Assert.Equal("cancelled", summary.GetProperty("status").GetString());

        // Yeni maç başlatılabilir
        Assert.Equal(HttpStatusCode.Created, (await player.Client.PostAsJsonAsync("/api/matches/solo", new { mode = "branch", categoryId = category })).StatusCode);
    }

    [Fact]
    public async Task Summary_IsLockedWhileThePlayIsActive_AndPrivateToTheOwner()
    {
        var player = await NewPlayerAsync();
        var stranger = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, _) = await StartAsync(player, category);

        Assert.Equal(HttpStatusCode.Conflict, (await player.Client.GetAsync($"/api/matches/{matchId}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/matches/{matchId}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsync($"/api/matches/{matchId}/forfeit", null)).StatusCode);
    }

    // ---------------------------------------------------------------- Jokerler

    [Fact]
    public async Task HalfJoker_RemovesTwoWrongOptions_AndSpendsOneFromTheBalance()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));

        var response = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "half" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);

        var removed = body.GetProperty("joker").GetProperty("removed").EnumerateArray().Select(x => x.GetInt32()).ToList();
        Assert.Equal(2, removed.Distinct().Count());
        Assert.DoesNotContain(await CorrectDisplayAsync(matchId, 1), removed);
        Assert.Equal(4, body.GetProperty("jokers").GetProperty("half").GetInt32());
        Assert.Equal(5, body.GetProperty("jokers").GetProperty("hint").GetInt32());

        // Yeniden bağlanan istemci aynı durumu görür
        var current = await Json(await player.Client.GetAsync("/api/matches/current"));
        Assert.Equal("half", current.GetProperty("activeJoker").GetProperty("type").GetString());
        Assert.Equal(removed, current.GetProperty("activeJoker").GetProperty("removed").EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public async Task HintJoker_ReturnsAValidOption()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));

        var body = await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "hint" }));
        Assert.InRange(body.GetProperty("joker").GetProperty("hint").GetInt32(), 0, 3);
    }

    [Fact]
    public async Task DoubleJoker_AcceptsTwoOptions_AndCountsAsCorrectIfEitherIs()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));

        // Joker kullanılmadan ikinci şık gönderilemez
        var correct = await CorrectDisplayAsync(matchId, 1);
        var wrong = (correct + 1) % 4;
        var noJoker = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = wrong, second = correct });
        Assert.Equal("DOUBLE_NOT_ACTIVE", Code(await Json(noJoker)));

        Assert.Equal(HttpStatusCode.OK, (await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "double" })).StatusCode);
        var same = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = wrong, second = wrong });
        Assert.Equal("OPTION_INVALID", Code(await Json(same)));

        var outcome = await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/answer", new { position = 1, option = wrong, second = correct }));
        Assert.True(outcome.GetProperty("isCorrect").GetBoolean());
        Assert.Equal(correct, outcome.GetProperty("secondOption").GetInt32());
    }

    [Fact]
    public async Task Joker_OnePerQuestion_OneOfEachTypePerMatch()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));

        Assert.Equal(HttpStatusCode.OK, (await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "half" })).StatusCode);

        // Aynı soruda ikinci joker (başka tür) yok
        var second = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "hint" });
        Assert.Equal("JOKER_ALREADY_USED_THIS_QUESTION", Code(await Json(second)));

        state = (await AnswerAsync(player, matchId, state, null, correct: true)).GetProperty("state");
        AdvanceTo(state.GetProperty("question"));

        // Sonraki soruda aynı tür yeniden kullanılamaz, başka tür kullanılabilir
        var sameType = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 2, type = "half" });
        Assert.Equal("JOKER_TYPE_USED", Code(await Json(sameType)));
        Assert.Equal(HttpStatusCode.OK, (await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 2, type = "hint" })).StatusCode);
    }

    [Fact]
    public async Task Joker_Retry_ReturnsTheStoredResultWithoutChargingAgain()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));

        var first = await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "half" }));
        var retry = await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "half" }));

        Assert.Equal(first.GetProperty("joker").GetRawText(), retry.GetProperty("joker").GetRawText());
        Assert.Equal(4, retry.GetProperty("jokers").GetProperty("half").GetInt32());
        Assert.Equal(4, (await Json(await player.Client.GetAsync("/api/jokers"))).GetProperty("half").GetInt32());
    }

    [Fact]
    public async Task Joker_WithoutBalance_IsRefused()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var wallet = scope.ServiceProvider.GetRequiredService<IJokerWallet>();
            for (var i = 0; i < 5; i++) await wallet.TrySpendAsync(player.UserId, JokerType.Half, "test", $"drain:{player.UserId:N}:{i}");
        }

        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));
        var response = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "half" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NO_JOKER", Code(await Json(response)));
        Assert.Equal(0, (await Json(await player.Client.GetAsync("/api/jokers"))).GetProperty("half").GetInt32());
    }

    [Fact]
    public async Task Joker_RejectsUnknownTypes_LateUse_AndUseAfterAnswering()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);

        var unknown = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "zaman" });
        Assert.Equal("JOKER_INVALID", Code(await Json(unknown)));

        AdvanceTo(state.GetProperty("question"));
        await AnswerAsync(player, matchId, state, null, correct: true);
        var afterAnswer = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "half" });
        Assert.Equal(HttpStatusCode.Conflict, afterAnswer.StatusCode);
        Assert.Equal(5, (await Json(await player.Client.GetAsync("/api/jokers"))).GetProperty("half").GetInt32());
    }

    [Fact]
    public async Task Joker_AfterTheTimeIsUp_IsRefusedAndNotCharged()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));
        factory.Clock.Advance(TimeSpan.FromSeconds(17));

        var late = await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/joker", new { position = 1, type = "hint" });
        Assert.Equal("QUESTION_TIMED_OUT", Code(await Json(late)));
        Assert.Equal(5, (await Json(await player.Client.GetAsync("/api/jokers"))).GetProperty("hint").GetInt32());
    }

    // ---------------------------------------------------------------- Soru bildirme, görülenler, güvenlik

    [Fact]
    public async Task Report_WorksForShownQuestions_Only_AndValidatesTheReason()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        AdvanceTo(state.GetProperty("question"));
        var questionId = await CurrentQuestionIdAsync(matchId, 1);

        Assert.Equal(HttpStatusCode.NoContent, (await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/report", new { position = 1, reason = "Cevap yanlış" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/report", new { position = 1, reason = "tekrar" })).StatusCode);
        Assert.Equal("REASON_INVALID", Code(await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/report", new { position = 1, reason = " " }))));
        Assert.Equal("QUESTION_NOT_SHOWN", Code(await Json(await player.Client.PostAsJsonAsync($"/api/matches/{matchId}/report", new { position = 6, reason = "x" }))));

        using var scope = factory.Services.CreateScope();
        var q = await scope.ServiceProvider.GetRequiredService<QuestionsDbContext>().Questions.AsNoTracking().SingleAsync(x => x.Id == questionId);
        Assert.Equal(1, q.ReportCount);
    }

    [Fact]
    public async Task SecondGameInTheSameCategory_DoesNotRepeatQuestionsFromTheFirst()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync(); // 14 / 10 / 10: ikinci maçta da yeterince "yeni" soru var
        var (first, state) = await StartAsync(player, category);
        state = await PlayCorrectAsync(player, first, state, 10);
        Assert.Equal("finished", state.GetProperty("status").GetString());

        var (second, _) = await StartAsync(player, category);
        using var scope = factory.Services.CreateScope();
        var matches = scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>();
        var a = await matches.Questions.Where(q => q.MatchId == first).Select(q => q.QuestionId).ToListAsync();
        var b = await matches.Questions.Where(q => q.MatchId == second).Select(q => q.QuestionId).ToListAsync();
        Assert.Empty(a.Intersect(b));
    }

    [Fact]
    public async Task ShownQuestions_IncreaseTheirShownCounter()
    {
        var player = await NewPlayerAsync();
        var category = await NewCategoryAsync();
        var (matchId, state) = await StartAsync(player, category);
        await AnswerAsync(player, matchId, state, null, correct: true);

        using var scope = factory.Services.CreateScope();
        var ids = await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Questions
            .Where(q => q.MatchId == matchId && q.StartsAt != null).Select(q => q.QuestionId).ToListAsync();
        Assert.Equal(2, ids.Count); // 1. ve (doğru cevaptan sonra servis edilen) 2. soru
        var counters = await scope.ServiceProvider.GetRequiredService<QuestionsDbContext>().Questions.AsNoTracking().Where(q => ids.Contains(q.Id)).ToListAsync();
        Assert.All(counters, q => Assert.Equal(1, q.ShownCount));
        Assert.Equal(1, counters.Sum(q => q.CorrectCount));
    }

    [Fact]
    public async Task Endpoints_RequireLogin_AndTheActiveDevice()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/matches/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/jokers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/matches/solo", new { mode = "branch" })).StatusCode);

        // Başka cihazdan giriş yapılınca eski cihazın istekleri SESSION_REPLACED döner
        var player = await NewPlayerAsync("device-A");
        var replaced = await player.Client.PostAsJsonAsync("/api/users/me/session", new { deviceId = "device-B", platform = "test" });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var response = await player.Client.GetAsync("/api/jokers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("SESSION_REPLACED", Code(await Json(response)));
    }

    [Fact]
    public async Task Jokers_Endpoint_ShowsTheWelcomeGift()
    {
        var player = await NewPlayerAsync();
        var json = await Json(await player.Client.GetAsync("/api/jokers"));
        Assert.Equal(5, json.GetProperty("half").GetInt32());
        Assert.Equal(5, json.GetProperty("double").GetInt32());
        Assert.Equal(5, json.GetProperty("hint").GetInt32());
    }
}
