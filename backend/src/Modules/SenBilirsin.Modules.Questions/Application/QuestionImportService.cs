using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Modules.Questions.Application;

public static class ImportRowStatus
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Duplicate = "duplicate";
}

/// <summary>Önizlemedeki tek satır. <see cref="Line"/>, dosyadaki veri satırı sırasıdır (başlık hariç, 1'den başlar).</summary>
public sealed record ImportRow(
    int Line,
    string Status,
    IReadOnlyList<string> Errors,
    IReadOnlyList<DuplicateMatch> Duplicates,
    string? FileDuplicateOf,
    string Mode,
    string Category,
    string Text,
    QuestionInput? Input);

public sealed record ImportPreview(string? FatalError, IReadOnlyList<ImportRow> Rows)
{
    public int Total => Rows.Count;
    public int Valid => Rows.Count(r => r.Status == ImportRowStatus.Ok);
    public int Invalid => Rows.Count(r => r.Status == ImportRowStatus.Error);
    public int Duplicates => Rows.Count(r => r.Status == ImportRowStatus.Duplicate);
}

public sealed record ImportCommitResult(string? FatalError, int Created, int SkippedInvalid, int SkippedDuplicates, IReadOnlyList<int> CreatedIds);

/// <summary>
/// CSV ile toplu soru yükleme (docs/05 §2). Önce önizleme (satır satır hata ve tekrar uyarısı), sonra onayla-ekle.
/// Sütunlar: mod, kategori, zorluk, soru, A, B, C, D, dogru, aciklama (başlık satırı zorunlu; sıra serbest).
/// Excel dosyası "CSV UTF-8" olarak kaydedilerek yüklenir.
/// </summary>
public sealed class QuestionImportService(QuestionService questions, ICatalogQueries catalog)
{
    public const int MaxRows = 500;
    public const int MaxChars = 2_000_000;

    private static readonly string[] Required = ["kategori", "zorluk", "soru", "a", "b", "c", "d", "dogru"];

    private static readonly Dictionary<string, string> HeaderAliases = new()
    {
        ["mod"] = "mod", ["mode"] = "mod",
        ["kategori"] = "kategori", ["category"] = "kategori", ["altdal"] = "kategori",
        ["zorluk"] = "zorluk", ["difficulty"] = "zorluk",
        ["soru"] = "soru", ["question"] = "soru", ["metin"] = "soru", ["sorumetni"] = "soru",
        ["a"] = "a", ["sika"] = "a", ["b"] = "b", ["sikb"] = "b", ["c"] = "c", ["sikc"] = "c", ["d"] = "d", ["sikd"] = "d",
        ["dogru"] = "dogru", ["correct"] = "dogru", ["cevap"] = "dogru", ["dogrusik"] = "dogru",
        ["aciklama"] = "aciklama", ["explanation"] = "aciklama",
    };

    private static readonly Dictionary<string, string> ModeAliases = new()
    {
        ["genel"] = "general", ["general"] = "general", ["genelsorular"] = "general",
        ["brans"] = "branch", ["branch"] = "branch", ["branssecmeli"] = "branch",
        ["kpss"] = "kpss", ["kpsstarzi"] = "kpss",
        ["fan"] = "fan", ["fanlaraozel"] = "fan", ["fanlar"] = "fan",
    };

    public async Task<ImportPreview> PreviewAsync(string? csv, CancellationToken ct = default)
    {
        var (fatal, table) = ReadTable(csv);
        if (fatal is not null) return new ImportPreview(fatal, []);

        var categories = await catalog.GetAllCategoriesAsync(ct);
        var rows = new List<ImportRow>();
        var seenInFile = new Dictionary<(int Category, string Text), int>();

        for (var i = 0; i < table!.Count; i++)
        {
            var line = i + 1;
            var cells = table[i];
            string Get(string key) => cells.TryGetValue(key, out var v) ? v : "";

            var errors = new List<string>();
            var modeText = Get("mod");
            var categoryText = Get("kategori");
            var text = Get("soru");

            var category = ResolveCategory(categories, modeText, categoryText, errors);

            var difficulty = ParseDifficulty(Get("zorluk"));
            if (difficulty is null) errors.Add("Zorluk kolay/orta/zor olmalı.");

            var correct = ParseCorrect(Get("dogru"));
            if (correct is null) errors.Add("Doğru şık A, B, C veya D olmalı.");

            var input = new QuestionInput(category?.Id ?? 0, text, [Get("a"), Get("b"), Get("c"), Get("d")],
                correct ?? 0, difficulty ?? Difficulty.Easy, Get("aciklama"));

            if (errors.Count == 0)
            {
                var rule = QuestionRules.Check(input.Text, input.Options, input.CorrectIndex, input.Difficulty, input.Explanation);
                if (!rule.IsValid) errors.Add(rule.Message!);
            }

            if (errors.Count > 0)
            {
                rows.Add(new ImportRow(line, ImportRowStatus.Error, errors, [], null, modeText, categoryText, text, null));
                continue;
            }

            var normalized = QuestionRules.Normalize(text);
            var duplicates = await questions.FindDuplicatesAsync(normalized, category!.Id, null, ct);

            string? fileDuplicate = null;
            if (seenInFile.TryGetValue((category.Id, normalized), out var firstLine))
                fileDuplicate = $"Dosyada {firstLine}. satırla aynı soru.";
            else
                seenInFile[(category.Id, normalized)] = line;

            var status = duplicates.Count > 0 || fileDuplicate is not null ? ImportRowStatus.Duplicate : ImportRowStatus.Ok;
            rows.Add(new ImportRow(line, status, [], duplicates, fileDuplicate, modeText, categoryText, text, input));
        }

        return new ImportPreview(null, rows);
    }

    /// <summary>Geçerli satırları ekler. Tekrar şüphelileri yalnızca <paramref name="includeDuplicates"/> ise eklenir; hatalı satırlar hep atlanır.</summary>
    public async Task<ImportCommitResult> CommitAsync(string? csv, bool includeDuplicates, QuestionStatus status, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status)) return new ImportCommitResult("Durum geçersiz.", 0, 0, 0, []);
        var preview = await PreviewAsync(csv, ct);
        if (preview.FatalError is not null) return new ImportCommitResult(preview.FatalError, 0, 0, 0, []);

        var created = new List<int>();
        var skippedDuplicates = 0;
        foreach (var row in preview.Rows)
        {
            if (row.Status == ImportRowStatus.Error) continue;
            if (row.Status == ImportRowStatus.Duplicate && !includeDuplicates) { skippedDuplicates++; continue; }

            var result = await questions.CreateAsync(row.Input! with { Status = status }, ignoreDuplicates: true, ct);
            if (result.IsSuccess) created.Add(result.Value!.Id);
        }

        return new ImportCommitResult(null, created.Count, preview.Invalid, skippedDuplicates, created);
    }

    private static (string? Fatal, List<Dictionary<string, string>>? Table) ReadTable(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return ("Dosya boş.", null);
        if (csv.Length > MaxChars) return ("Dosya çok büyük (en fazla yaklaşık 2 MB).", null);

        var raw = CsvParser.Parse(csv);
        if (raw.Count < 2) return ("Başlık satırı ve en az bir soru satırı gerekli.", null);

        var headers = raw[0].Select(h => HeaderAliases.GetValueOrDefault(Squash(h), "")).ToArray();
        var missing = Required.Where(r => !headers.Contains(r)).ToList();
        if (missing.Count > 0)
            return ($"Eksik sütun(lar): {string.Join(", ", missing)}. Beklenen başlıklar: mod, kategori, zorluk, soru, A, B, C, D, dogru, aciklama.", null);

        if (raw.Count - 1 > MaxRows) return ($"Tek seferde en fazla {MaxRows} soru yüklenebilir.", null);

        var table = new List<Dictionary<string, string>>();
        foreach (var record in raw.Skip(1))
        {
            var row = new Dictionary<string, string>();
            for (var i = 0; i < headers.Length; i++)
                if (headers[i].Length > 0) row[headers[i]] = i < record.Length ? record[i] : "";
            table.Add(row);
        }

        return (null, table);
    }

    private static CategoryInfo? ResolveCategory(IReadOnlyList<CategoryInfo> all, string modeText, string categoryText, List<string> errors)
    {
        string? modeCode = null;
        if (modeText.Length > 0)
        {
            if (!ModeAliases.TryGetValue(Squash(modeText), out modeCode))
            {
                errors.Add($"Mod tanınmadı: \"{modeText}\" (genel, branş, kpss, fan).");
                return null;
            }
        }

        var name = Squash(categoryText);
        // Genel modda kategori adı yazılmayabilir
        if (name.Length == 0 && modeCode == "general")
            return all.FirstOrDefault(c => c.ModeCode == "general");
        if (name.Length == 0)
        {
            errors.Add("Kategori boş.");
            return null;
        }

        var matches = all.Where(c => Squash(c.Name) == name && (modeCode is null || c.ModeCode == modeCode)).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1)
        {
            errors.Add($"\"{categoryText}\" birden fazla modda var; \"mod\" sütununu doldurun.");
            return null;
        }

        errors.Add($"Kategori bulunamadı: \"{categoryText}\". Önce Kategoriler bölümünden ekleyin.");
        return null;
    }

    private static Difficulty? ParseDifficulty(string value) => Squash(value) switch
    {
        "kolay" or "easy" or "1" => Difficulty.Easy,
        "orta" or "medium" or "2" => Difficulty.Medium,
        "zor" or "hard" or "3" => Difficulty.Hard,
        _ => null,
    };

    private static int? ParseCorrect(string value) => Squash(value) switch
    {
        "a" or "1" => 0,
        "b" or "2" => 1,
        "c" or "3" => 2,
        "d" or "4" => 3,
        _ => null,
    };

    /// <summary>Küçük harf, aksansız, yalnızca harf/rakam (başlık ve değer eşlemesi için).</summary>
    private static string Squash(string text) => QuestionRules.Normalize(text).Replace(" ", "");
}
