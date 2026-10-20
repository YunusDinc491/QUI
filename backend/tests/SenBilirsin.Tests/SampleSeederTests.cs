using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.AdminApi;
using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 2.8: örnek soru seti geçerli, dengeli ve tekrar çalıştırılabilir olmalı.</summary>
[Collection("admin-api")]
public class SampleSeederTests(AdminApiFactory factory)
{
    [Fact]
    public async Task Seed_AddsAll60Questions_ValidAndBalanced_ThenIsIdempotent()
    {
        var first = await SampleSeeder.RunAsync(factory.Services);
        Assert.Null(first.Error);
        Assert.Equal(60, first.Created);       // dosyadaki tüm satırlar kurallara uygun ve birbirinden farklı
        Assert.Equal(0, first.SkippedInvalid);
        Assert.Equal(0, first.SkippedDuplicates);

        using var scope = factory.Services.CreateScope();
        var questions = scope.ServiceProvider.GetRequiredService<QuestionService>();
        var counts = await questions.GetCountsAsync();
        int Published(int category, Difficulty d) =>
            counts.Where(c => c.CategoryId == category && c.Difficulty == d && c.Status == QuestionStatus.Published).Sum(c => c.Count);

        // Genel: en az 14 kolay / 10 orta / 6 zor (diğer testler ek soru eklemiş olabilir)
        Assert.True(Published(CatalogDbContext.GeneralCategoryId, Difficulty.Easy) >= 14);
        Assert.True(Published(CatalogDbContext.GeneralCategoryId, Difficulty.Medium) >= 10);
        Assert.True(Published(CatalogDbContext.GeneralCategoryId, Difficulty.Hard) >= 6);

        // Her maç 4 kolay + 3 orta + 3 zor ister; son 200 hariç tutma kuralı için de yeterli çeşit olmalı
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogAdminService>();
        var history = (await catalog.ListAsync()).Single(c => c.ModeCode == "branch" && c.Name == "Tarih");
        Assert.True(history.IsPublished);
        Assert.Equal(12, Published(history.Id, Difficulty.Easy));
        Assert.Equal(10, Published(history.Id, Difficulty.Medium));
        Assert.Equal(8, Published(history.Id, Difficulty.Hard));

        // İkinci çalıştırma yeni soru eklemez
        var second = await SampleSeeder.RunAsync(factory.Services);
        Assert.Equal(0, second.Created);
        Assert.Equal(60, second.SkippedDuplicates);
    }

    [Fact]
    public async Task Seed_ReportsMissingFile()
    {
        var result = await SampleSeeder.RunAsync(factory.Services, csvPath: Path.Combine(Path.GetTempPath(), "yok-" + Guid.NewGuid().ToString("N") + ".csv"));
        Assert.NotNull(result.Error);
    }
}
