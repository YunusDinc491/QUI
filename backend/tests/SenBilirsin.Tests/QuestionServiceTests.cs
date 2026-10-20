using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Catalog.Domain;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 2.2: soru bankası servisi, gerçek PostgreSQL üzerinde (kategori doğrulaması dahil).</summary>
[Collection("api")]
public class QuestionServiceTests(MobileApiFactory factory)
{
    private static string Words() => string.Join(' ', Enumerable.Range(0, 4).Select(_ => Guid.NewGuid().ToString("N")[..8])) + " sorusu nedir";

    private async Task<int> NewCategoryAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var mode = db.Modes.Single(m => m.Code == ModeCodes.Branch);
        var category = new Category { ModeId = mode.Id, Name = "T" + Guid.NewGuid().ToString("N")[..10], CreatedAt = DateTime.UtcNow };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static QuestionInput Input(int categoryId, string? text = null, Difficulty difficulty = Difficulty.Easy,
        QuestionStatus status = QuestionStatus.Draft, int correct = 1) =>
        new(categoryId, text ?? Words(), ["Bir", "İki", "Üç", "Dört"], correct, difficulty, null, status);

    private async Task<T> WithService<T>(Func<QuestionService, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<QuestionService>());
    }

    [Fact]
    public async Task Create_StoresTrimmedFields_NormalizedText_AndDefaults()
    {
        var category = await NewCategoryAsync();
        var result = await WithService(s => s.CreateAsync(new QuestionInput(
            category, "  Hangisi doğrudur ve neden öyledir?  ", [" A ", "B", "C", "D"], 2, Difficulty.Hard, "  açıklama  ")));

        Assert.True(result.IsSuccess);
        var q = result.Value!;
        Assert.Equal("Hangisi doğrudur ve neden öyledir?", q.Text);
        Assert.Equal("A", q.OptionA);
        Assert.Equal("hangisi dogrudur ve neden oyledir", q.NormalizedText);
        Assert.Equal(2, q.CorrectIndex);
        Assert.Equal("açıklama", q.Explanation);
        Assert.Equal(QuestionStatus.Draft, q.Status);
        Assert.Equal(0, q.ShownCount);
    }

    [Fact]
    public async Task Create_InvalidInput_ReturnsRuleError()
    {
        var category = await NewCategoryAsync();
        var result = await WithService(s => s.CreateAsync(Input(category, text: "kısa")));
        Assert.False(result.IsSuccess);
        Assert.Equal("TEXT_TOO_SHORT", result.Error!.Code);
    }

    [Fact]
    public async Task Create_UnknownCategory_Returns404()
    {
        var result = await WithService(s => s.CreateAsync(Input(int.MaxValue)));
        Assert.Equal("CATEGORY_NOT_FOUND", result.Error!.Code);
        Assert.Equal(404, result.Error.StatusCode);
    }

    [Fact]
    public async Task Create_Duplicate_IsFlagged_UnlessIgnored()
    {
        var category = await NewCategoryAsync();
        var text = Words();
        Assert.True((await WithService(s => s.CreateAsync(Input(category, text)))).IsSuccess);

        // Aynı soru, farklı harf büyüklüğü/noktalama ile
        var again = await WithService(s => s.CreateAsync(Input(category, text.ToUpperInvariant() + "?")));
        Assert.False(again.IsSuccess);
        Assert.Equal("DUPLICATE_SUSPECTED", again.Error!.Code);
        Assert.Equal(409, again.Error.StatusCode);
        Assert.Single(again.Error.Duplicates!);

        var forced = await WithService(s => s.CreateAsync(Input(category, text), ignoreDuplicates: true));
        Assert.True(forced.IsSuccess);
    }

    [Fact]
    public async Task Update_ChangesFields_AndDoesNotFlagItselfAsDuplicate()
    {
        var category = await NewCategoryAsync();
        var created = (await WithService(s => s.CreateAsync(Input(category)))).Value!;

        // Aynı metinle güncelleme (yalnızca zorluk/şık değişir) kendi kendini tekrar saymamalı
        var sameText = created.Text;
        var updated = await WithService(s => s.UpdateAsync(created.Id, Input(category, sameText, Difficulty.Medium, correct: 3)));
        Assert.True(updated.IsSuccess);
        Assert.Equal(Difficulty.Medium, updated.Value!.Difficulty);
        Assert.Equal(3, updated.Value.CorrectIndex);
    }

    [Fact]
    public async Task Update_ToAnotherQuestionsText_IsFlagged_AndMissingId_Returns404()
    {
        var category = await NewCategoryAsync();
        var first = (await WithService(s => s.CreateAsync(Input(category)))).Value!;
        var second = (await WithService(s => s.CreateAsync(Input(category)))).Value!;

        var clash = await WithService(s => s.UpdateAsync(second.Id, Input(category, first.Text)));
        Assert.Equal("DUPLICATE_SUSPECTED", clash.Error!.Code);

        var missing = await WithService(s => s.UpdateAsync(int.MaxValue, Input(category)));
        Assert.Equal("QUESTION_NOT_FOUND", missing.Error!.Code);
    }

    [Fact]
    public async Task Status_CanPublishArchiveAndReturnToDraft()
    {
        var category = await NewCategoryAsync();
        var q = (await WithService(s => s.CreateAsync(Input(category)))).Value!;

        Assert.Equal(QuestionStatus.Published, (await WithService(s => s.SetStatusAsync(q.Id, QuestionStatus.Published))).Value!.Status);
        Assert.Equal(QuestionStatus.Archived, (await WithService(s => s.SetStatusAsync(q.Id, QuestionStatus.Archived))).Value!.Status);
        Assert.Equal(QuestionStatus.Draft, (await WithService(s => s.SetStatusAsync(q.Id, QuestionStatus.Draft))).Value!.Status);
        Assert.Equal("STATUS_INVALID", (await WithService(s => s.SetStatusAsync(q.Id, (QuestionStatus)9))).Error!.Code);
        Assert.Equal("QUESTION_NOT_FOUND", (await WithService(s => s.SetStatusAsync(int.MaxValue, QuestionStatus.Published))).Error!.Code);
    }

    [Fact]
    public async Task Search_FiltersByCategoryDifficultyStatusAndText_WithPaging()
    {
        var category = await NewCategoryAsync();
        var uniqueWord = Guid.NewGuid().ToString("N")[..10];
        for (var i = 0; i < 5; i++)
            Assert.True((await WithService(s => s.CreateAsync(Input(category, $"{uniqueWord} {Words()}", i < 3 ? Difficulty.Easy : Difficulty.Hard,
                i == 0 ? QuestionStatus.Published : QuestionStatus.Draft)))).IsSuccess);

        var all = await WithService(s => s.SearchAsync(new QuestionFilter(CategoryId: category)));
        Assert.Equal(5, all.Total);

        Assert.Equal(3, (await WithService(s => s.SearchAsync(new QuestionFilter(category, Difficulty.Easy)))).Total);
        Assert.Equal(1, (await WithService(s => s.SearchAsync(new QuestionFilter(category, Status: QuestionStatus.Published)))).Total);
        Assert.Equal(5, (await WithService(s => s.SearchAsync(new QuestionFilter(Search: uniqueWord.ToUpperInvariant())))).Total);

        var page2 = await WithService(s => s.SearchAsync(new QuestionFilter(category, Page: 2, PageSize: 2)));
        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(5, page2.Total);

        // Sayfa boyutu üst sınırla kısıtlanır
        Assert.Equal(100, (await WithService(s => s.SearchAsync(new QuestionFilter(category, PageSize: 5000)))).PageSize);
    }

    [Fact]
    public async Task Counts_GroupByDifficultyAndStatus()
    {
        var category = await NewCategoryAsync();
        await WithService(s => s.CreateAsync(Input(category, difficulty: Difficulty.Easy, status: QuestionStatus.Published)));
        await WithService(s => s.CreateAsync(Input(category, difficulty: Difficulty.Easy, status: QuestionStatus.Published)));
        await WithService(s => s.CreateAsync(Input(category, difficulty: Difficulty.Hard)));

        var counts = await WithService(s => s.GetCountsAsync(category));
        Assert.Equal(2, counts.Single(c => c.Difficulty == Difficulty.Easy && c.Status == QuestionStatus.Published).Count);
        Assert.Equal(1, counts.Single(c => c.Difficulty == Difficulty.Hard && c.Status == QuestionStatus.Draft).Count);
    }
}
