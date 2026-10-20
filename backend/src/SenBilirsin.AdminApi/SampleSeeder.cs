using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Catalog.Domain;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.AdminApi;

public sealed record SeedResult(int Created, int SkippedInvalid, int SkippedDuplicates, string? Error);

/// <summary>
/// Yerel geliştirme/test için örnek sorular (Genel + Branş/Tarih, 60 soru). Gerçek içerik DEĞİLDİR;
/// yayına çıkmadan önce arşivlenir/silinir. Yalnızca <c>seed-samples</c> komutuyla ve yalnızca Development ortamında çalışır.
/// Tekrar çalıştırmak güvenlidir: aynı sorular tekrar eklenmez.
/// </summary>
public static class SampleSeeder
{
    public const string Name = "seed-samples";

    /// <summary>Katalog tohumundaki "Branş seçmeli" modunun kimliği.</summary>
    private const int BranchModeId = 2;

    public static async Task<SeedResult> RunAsync(IServiceProvider services, string? csvPath = null, CancellationToken ct = default)
    {
        csvPath ??= Path.Combine(AppContext.BaseDirectory, "Seed", "sample-questions.csv");
        if (!File.Exists(csvPath)) return new SeedResult(0, 0, 0, "Örnek soru dosyası bulunamadı: " + csvPath);

        using var scope = services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogAdminService>();
        var import = scope.ServiceProvider.GetRequiredService<QuestionImportService>();

        // "Tarih" (branş) kategorisi yoksa oluştur; Genel ve Tarih yayına alınır ki uygulamada görünsün
        var categories = await catalog.ListAsync(ct);
        var history = categories.FirstOrDefault(c => c.ModeCode == ModeCodes.Branch && c.Name == "Tarih");
        if (history is null)
        {
            var created = await catalog.CreateAsync(new CategoryInput(BranchModeId, "Tarih", null, "#8D6E63"), ct);
            if (!created.IsSuccess) return new SeedResult(0, 0, 0, created.Error!.Message);
            history = created.Value!;
        }

        foreach (var id in new[] { CatalogDbContext.GeneralCategoryId, history.Id })
            await catalog.SetPublishedAsync(id, true, ct);

        var csv = await File.ReadAllTextAsync(csvPath, ct);
        var result = await import.CommitAsync(csv, includeDuplicates: false, QuestionStatus.Published, ct);
        return new SeedResult(result.Created, result.SkippedInvalid, result.SkippedDuplicates, result.FatalError);
    }

    /// <summary>Komut satırı girişi: <c>dotnet run --project backend/src/SenBilirsin.AdminApi -- seed-samples</c>.</summary>
    public static async Task<int> RunCommandAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            Console.Error.WriteLine("Örnek sorular yalnızca Development ortamında yüklenir (gerçek veriyle karışmasın).");
            return 1;
        }

        await app.Services.MigrateAllAsync();
        var result = await RunAsync(app.Services);
        if (result.Error is not null)
        {
            Console.Error.WriteLine("Örnek sorular yüklenemedi: " + result.Error);
            return 2;
        }

        Console.WriteLine($"Örnek sorular: {result.Created} eklendi, {result.SkippedDuplicates} zaten vardı, {result.SkippedInvalid} hatalı.");
        return 0;
    }
}
