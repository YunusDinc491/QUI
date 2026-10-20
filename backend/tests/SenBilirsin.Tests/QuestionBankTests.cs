using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Catalog.Domain;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Questions.Data;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 3.2: soru seçimi (4/3/3, son 200 hariç, gevşetme), görülen kaydı, sayaçlar, soru bildirme.</summary>
[Collection("api")]
public class QuestionBankTests(MobileApiFactory factory)
{
    private static readonly (Difficulty, int)[] Plan = [(Difficulty.Easy, 4), (Difficulty.Medium, 3), (Difficulty.Hard, 3)];

    private async Task<int> NewCategoryAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var category = new Category { ModeId = db.Modes.Single(m => m.Code == ModeCodes.Branch).Id, Name = "B" + Guid.NewGuid().ToString("N")[..10], IsPublished = true, CreatedAt = DateTime.UtcNow };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>Doğrudan veritabanına soru ekler (hızlı); metinler benzersizdir.</summary>
    private async Task<List<int>> AddQuestionsAsync(int categoryId, Difficulty difficulty, int count, QuestionStatus status = QuestionStatus.Published)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsDbContext>();
        var list = Enumerable.Range(0, count).Select(i => new Question
        {
            CategoryId = categoryId, Text = $"Soru {Guid.NewGuid():N} metni", OptionA = "A", OptionB = "B", OptionC = "C", OptionD = "D",
            CorrectIndex = i % 4, Difficulty = difficulty, Status = status, NormalizedText = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }).ToList();
        db.Questions.AddRange(list);
        await db.SaveChangesAsync();
        return list.Select(q => q.Id).ToList();
    }

    private async Task<T> WithBank<T>(Func<QuestionBank, QuestionsDbContext, Task<T>> action, FakeClock? clock = null, int seed = 7)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsDbContext>();
        return await action(new QuestionBank(db, clock ?? new FakeClock(DateTimeOffset.UtcNow), new Random(seed)), db);
    }

    private async Task WithBank(Func<QuestionBank, QuestionsDbContext, Task> action, FakeClock? clock = null) =>
        await WithBank<bool>(async (bank, db) => { await action(bank, db); return true; }, clock);

    [Fact]
    public async Task Pick_Returns4Easy3Medium3Hard_InOrder_Distinct_PublishedOnly()
    {
        var category = await NewCategoryAsync();
        await AddQuestionsAsync(category, Difficulty.Easy, 12);
        await AddQuestionsAsync(category, Difficulty.Medium, 9);
        await AddQuestionsAsync(category, Difficulty.Hard, 9);
        var drafts = await AddQuestionsAsync(category, Difficulty.Easy, 5, QuestionStatus.Draft);

        var result = await WithBank((bank, _) => bank.PickAsync(Guid.NewGuid(), [category], Plan));

        Assert.False(result.PoolTooSmall);
        Assert.False(result.SeenFilterRelaxed);
        Assert.Equal(10, result.Questions.Count);
        Assert.Equal(10, result.Questions.Select(q => q.Id).Distinct().Count());
        Assert.Equal([Difficulty.Easy, Difficulty.Easy, Difficulty.Easy, Difficulty.Easy, Difficulty.Medium, Difficulty.Medium, Difficulty.Medium,
            Difficulty.Hard, Difficulty.Hard, Difficulty.Hard], result.Questions.Select(q => q.Difficulty));
        Assert.DoesNotContain(result.Questions, q => drafts.Contains(q.Id));
        Assert.All(result.Questions, q => Assert.Equal(category, q.CategoryId));
    }

    [Fact]
    public async Task Pick_PrefersUnseenQuestions_AndIsNotRelaxedWhenEnoughRemain()
    {
        var category = await NewCategoryAsync();
        var easy = await AddQuestionsAsync(category, Difficulty.Easy, 10);
        await AddQuestionsAsync(category, Difficulty.Medium, 6);
        await AddQuestionsAsync(category, Difficulty.Hard, 6);
        var user = Guid.NewGuid();
        foreach (var id in easy.Take(6)) await WithBank((bank, _) => bank.MarkShownAsync(user, id));

        var result = await WithBank((bank, _) => bank.PickAsync(user, [category], Plan));

        Assert.False(result.SeenFilterRelaxed);
        var pickedEasy = result.Questions.Where(q => q.Difficulty == Difficulty.Easy).Select(q => q.Id).ToList();
        Assert.Equal(4, pickedEasy.Count);
        Assert.All(pickedEasy, id => Assert.DoesNotContain(id, easy.Take(6)));
    }

    [Fact]
    public async Task Pick_RelaxesSeenFilter_WhenTooFewUnseen_AndReusesOldestSeenFirst()
    {
        var category = await NewCategoryAsync();
        var easy = await AddQuestionsAsync(category, Difficulty.Easy, 6);
        await AddQuestionsAsync(category, Difficulty.Medium, 3);
        await AddQuestionsAsync(category, Difficulty.Hard, 3);
        var user = Guid.NewGuid();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        // easy[0] en eski, easy[3] en yeni görülen; easy[4], easy[5] hiç görülmedi
        foreach (var id in easy.Take(4))
        {
            await WithBank((bank, _) => bank.MarkShownAsync(user, id), clock);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var result = await WithBank((bank, _) => bank.PickAsync(user, [category], Plan));

        Assert.True(result.SeenFilterRelaxed);
        Assert.False(result.PoolTooSmall);
        var pickedEasy = result.Questions.Where(q => q.Difficulty == Difficulty.Easy).Select(q => q.Id).ToHashSet();
        // 6 kolay var, 4 isteniyor: 2 görülmemiş + en eski 2 görülen
        Assert.Contains(easy[4], pickedEasy);
        Assert.Contains(easy[5], pickedEasy);
        Assert.Contains(easy[0], pickedEasy);
        Assert.Contains(easy[1], pickedEasy);
    }

    [Fact]
    public async Task Pick_NeverReturnsExplicitlyExcluded_AndReportsPoolTooSmall()
    {
        var category = await NewCategoryAsync();
        var easy = await AddQuestionsAsync(category, Difficulty.Easy, 4);
        await AddQuestionsAsync(category, Difficulty.Medium, 3);
        await AddQuestionsAsync(category, Difficulty.Hard, 3);

        var result = await WithBank((bank, _) => bank.PickAsync(Guid.NewGuid(), [category], Plan, excludeIds: [easy[0]]));

        Assert.DoesNotContain(result.Questions, q => q.Id == easy[0]);
        Assert.True(result.PoolTooSmall);
        Assert.Equal(9, result.Questions.Count);
    }

    [Fact]
    public async Task Pick_BorrowsFromNeighbourDifficulty_WhenADifficultyIsMissing()
    {
        var category = await NewCategoryAsync();
        await AddQuestionsAsync(category, Difficulty.Easy, 12); // orta ve zor hiç yok

        var result = await WithBank((bank, _) => bank.PickAsync(Guid.NewGuid(), [category], Plan));

        Assert.False(result.PoolTooSmall);
        Assert.Equal(10, result.Questions.Count);
        Assert.Equal(10, result.Questions.Select(q => q.Id).Distinct().Count());
    }

    [Fact]
    public async Task Pick_AcrossSeveralCategories_ForMixedMode()
    {
        var a = await NewCategoryAsync();
        var b = await NewCategoryAsync();
        await AddQuestionsAsync(a, Difficulty.Easy, 3);
        await AddQuestionsAsync(b, Difficulty.Easy, 3);
        await AddQuestionsAsync(a, Difficulty.Medium, 2);
        await AddQuestionsAsync(b, Difficulty.Medium, 2);
        await AddQuestionsAsync(a, Difficulty.Hard, 2);
        await AddQuestionsAsync(b, Difficulty.Hard, 2);

        var result = await WithBank((bank, _) => bank.PickAsync(Guid.NewGuid(), [a, b], Plan));

        Assert.False(result.PoolTooSmall);
        Assert.Equal(10, result.Questions.Count);
        Assert.All(result.Questions, q => Assert.Contains(q.CategoryId, new[] { a, b }));
    }

    [Fact]
    public async Task MarkShown_KeepsOnlyLast200_AndDropsTheOldest()
    {
        var user = Guid.NewGuid();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        for (var id = 1; id <= 205; id++)
        {
            await WithBank((bank, _) => bank.MarkShownAsync(user, id), clock);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsDbContext>();
        var kept = await db.SeenQuestions.Where(s => s.UserId == user).Select(s => s.QuestionId).ToListAsync();
        Assert.Equal(200, kept.Count);
        Assert.DoesNotContain(1, kept);
        Assert.DoesNotContain(5, kept);
        Assert.Contains(6, kept);
        Assert.Contains(205, kept);
    }

    [Fact]
    public async Task MarkShown_SameQuestionTwice_DoesNotDuplicate_AndCountersMove()
    {
        var category = await NewCategoryAsync();
        var id = (await AddQuestionsAsync(category, Difficulty.Easy, 1))[0];
        var user = Guid.NewGuid();

        await WithBank((bank, _) => bank.MarkShownAsync(user, id));
        await WithBank((bank, _) => bank.MarkShownAsync(user, id));
        await WithBank((bank, _) => bank.RecordCorrectAsync(id));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsDbContext>();
        Assert.Equal(1, await db.SeenQuestions.CountAsync(s => s.UserId == user));
        var q = await db.Questions.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(2, q.ShownCount);
        Assert.Equal(1, q.CorrectCount);
    }

    [Fact]
    public async Task Report_IsRecordedOncePerUserAndQuestion_AndCountsDistinctUsers()
    {
        var category = await NewCategoryAsync();
        var id = (await AddQuestionsAsync(category, Difficulty.Easy, 1))[0];
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        Assert.Equal(ReportOutcome.Reported, await WithBank((b, _) => b.ReportAsync(u1, id, "Doğru cevap yanlış görünüyor")));
        Assert.Equal(ReportOutcome.AlreadyReported, await WithBank((b, _) => b.ReportAsync(u1, id, "tekrar")));
        Assert.Equal(ReportOutcome.Reported, await WithBank((b, _) => b.ReportAsync(u2, id, "Yazım hatası var")));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsDbContext>();
        Assert.Equal(2, (await db.Questions.AsNoTracking().SingleAsync(x => x.Id == id)).ReportCount);
        Assert.Equal(2, await db.Reports.CountAsync(r => r.QuestionId == id));
    }

    [Fact]
    public async Task Report_ValidatesReasonAndQuestion()
    {
        var category = await NewCategoryAsync();
        var id = (await AddQuestionsAsync(category, Difficulty.Easy, 1))[0];
        var user = Guid.NewGuid();

        Assert.Equal(ReportOutcome.ReasonInvalid, await WithBank((b, _) => b.ReportAsync(user, id, "  ")));
        Assert.Equal(ReportOutcome.ReasonInvalid, await WithBank((b, _) => b.ReportAsync(user, id, null)));
        Assert.Equal(ReportOutcome.ReasonInvalid, await WithBank((b, _) => b.ReportAsync(user, id, new string('x', QuestionReport.ReasonMax + 1))));
        Assert.Equal(ReportOutcome.QuestionNotFound, await WithBank((b, _) => b.ReportAsync(user, int.MaxValue, "yok")));
    }

    [Fact]
    public async Task GetByIds_ReturnsCorrectIndexForServerUse()
    {
        var category = await NewCategoryAsync();
        var ids = await AddQuestionsAsync(category, Difficulty.Hard, 3);

        var rows = await WithBank((b, _) => b.GetByIdsAsync(ids));

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(4, r.Options.Count));
        Assert.Contains(rows, r => r.CorrectIndex == 2);
    }
}
