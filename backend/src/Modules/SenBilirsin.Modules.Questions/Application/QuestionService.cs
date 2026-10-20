using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Questions.Data;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Modules.Questions.Application;

public sealed record QuestionInput(
    int CategoryId,
    string? Text,
    IReadOnlyList<string?>? Options,
    int CorrectIndex,
    Difficulty Difficulty,
    string? Explanation,
    QuestionStatus Status = QuestionStatus.Draft);

public sealed record DuplicateMatch(int QuestionId, int CategoryId, string Text, double Similarity);

public sealed record QuestionError(string Code, string Message, int StatusCode = 400, IReadOnlyList<DuplicateMatch>? Duplicates = null);

public sealed class QuestionResult<T>
{
    public T? Value { get; private init; }
    public QuestionError? Error { get; private init; }
    public bool IsSuccess => Error is null;

    public static QuestionResult<T> Ok(T value) => new() { Value = value };
    public static QuestionResult<T> Fail(QuestionError error) => new() { Error = error };
}

public sealed record QuestionFilter(int? CategoryId = null, Difficulty? Difficulty = null, QuestionStatus? Status = null, string? Search = null, int Page = 1, int PageSize = 25);

public sealed record QuestionPage(IReadOnlyList<Question> Items, int Total, int Page, int PageSize);

public sealed record QuestionCount(int CategoryId, Difficulty Difficulty, QuestionStatus Status, int Count);

/// <summary>Soru bankası işlemleri: ekleme, düzenleme, durum, arama, sayaçlar, tekrar uyarısı (admin API kullanır).</summary>
public sealed class QuestionService(QuestionsDbContext db, ICatalogQueries catalog, TimeProvider clock)
{
    public async Task<QuestionResult<Question>> CreateAsync(QuestionInput input, bool ignoreDuplicates = false, CancellationToken ct = default)
    {
        var check = await ValidateAsync(input, ct);
        if (check is not null) return QuestionResult<Question>.Fail(check);

        var normalized = QuestionRules.Normalize(input.Text!);
        if (!ignoreDuplicates)
        {
            var duplicates = await FindDuplicatesAsync(normalized, input.CategoryId, excludeId: null, ct);
            if (duplicates.Count > 0) return QuestionResult<Question>.Fail(DuplicateError(duplicates));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var question = new Question { CreatedAt = now };
        Apply(question, input, normalized, now);
        db.Questions.Add(question);
        await db.SaveChangesAsync(ct);
        return QuestionResult<Question>.Ok(question);
    }

    public async Task<QuestionResult<Question>> UpdateAsync(int id, QuestionInput input, bool ignoreDuplicates = false, CancellationToken ct = default)
    {
        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (question is null) return QuestionResult<Question>.Fail(new QuestionError("QUESTION_NOT_FOUND", "Soru bulunamadı.", 404));

        var check = await ValidateAsync(input, ct);
        if (check is not null) return QuestionResult<Question>.Fail(check);

        var normalized = QuestionRules.Normalize(input.Text!);
        if (!ignoreDuplicates && normalized != question.NormalizedText)
        {
            var duplicates = await FindDuplicatesAsync(normalized, input.CategoryId, excludeId: id, ct);
            if (duplicates.Count > 0) return QuestionResult<Question>.Fail(DuplicateError(duplicates));
        }

        Apply(question, input, normalized, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return QuestionResult<Question>.Ok(question);
    }

    /// <summary>Yayınla / taslağa al / arşivle. Soru silinmez, arşive alınır (docs/05).</summary>
    public async Task<QuestionResult<Question>> SetStatusAsync(int id, QuestionStatus status, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status)) return QuestionResult<Question>.Fail(new QuestionError("STATUS_INVALID", "Durum geçersiz."));
        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (question is null) return QuestionResult<Question>.Fail(new QuestionError("QUESTION_NOT_FOUND", "Soru bulunamadı.", 404));

        question.Status = status;
        question.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return QuestionResult<Question>.Ok(question);
    }

    public Task<Question?> GetAsync(int id, CancellationToken ct = default) =>
        db.Questions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct);

    public async Task<QuestionPage> SearchAsync(QuestionFilter filter, CancellationToken ct = default)
    {
        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 100);

        var query = db.Questions.AsNoTracking().AsQueryable();
        if (filter.CategoryId is { } c) query = query.Where(q => q.CategoryId == c);
        if (filter.Difficulty is { } d) query = query.Where(q => q.Difficulty == d);
        if (filter.Status is { } s) query = query.Where(q => q.Status == s);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var norm = QuestionRules.Normalize(filter.Search);
            query = query.Where(q => q.NormalizedText.Contains(norm));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(q => q.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new QuestionPage(items, total, page, size);
    }

    /// <summary>Kategori × zorluk × durum başına soru sayıları (panelde "Tarih: 40 kolay, 25 orta…" göstergesi için).</summary>
    public async Task<IReadOnlyList<QuestionCount>> GetCountsAsync(int? categoryId = null, CancellationToken ct = default)
    {
        var query = db.Questions.AsNoTracking().AsQueryable();
        if (categoryId is { } c) query = query.Where(q => q.CategoryId == c);

        var rows = await query.GroupBy(q => new { q.CategoryId, q.Difficulty, q.Status })
            .Select(g => new { g.Key.CategoryId, g.Key.Difficulty, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r => new QuestionCount(r.CategoryId, r.Difficulty, r.Status, r.Count)).ToList();
    }

    /// <summary>Aynı sadeleştirilmiş metin (herhangi bir kategoride) veya aynı kategoride çok benzer metin.</summary>
    public async Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(string normalizedText, int categoryId, int? excludeId, CancellationToken ct = default)
    {
        var candidates = await db.Questions.AsNoTracking()
            .Where(q => (q.NormalizedText == normalizedText || q.CategoryId == categoryId) && q.Id != (excludeId ?? -1))
            .Select(q => new { q.Id, q.CategoryId, q.Text, q.NormalizedText })
            .Take(5000)
            .ToListAsync(ct);

        var matches = new List<DuplicateMatch>();
        foreach (var c in candidates)
        {
            var similarity = QuestionRules.Similarity(normalizedText, c.NormalizedText);
            if (similarity >= QuestionRules.SimilarityThreshold)
                matches.Add(new DuplicateMatch(c.Id, c.CategoryId, c.Text, Math.Round(similarity, 2)));
        }

        return matches.OrderByDescending(m => m.Similarity).Take(5).ToList();
    }

    private async Task<QuestionError?> ValidateAsync(QuestionInput input, CancellationToken ct)
    {
        var rule = QuestionRules.Check(input.Text, input.Options, input.CorrectIndex, input.Difficulty, input.Explanation);
        if (!rule.IsValid) return new QuestionError(rule.Code!, rule.Message!);
        if (!Enum.IsDefined(input.Status))
            return new QuestionError("STATUS_INVALID", "Durum geçersiz.");

        var category = await catalog.GetCategoryAsync(input.CategoryId, ct);
        return category is null ? new QuestionError("CATEGORY_NOT_FOUND", "Kategori bulunamadı.", 404) : null;
    }

    private static QuestionError DuplicateError(IReadOnlyList<DuplicateMatch> duplicates) =>
        new("DUPLICATE_SUSPECTED", "Benzer bir soru zaten var. Yine de eklemek için onayla.", 409, duplicates);

    private static void Apply(Question q, QuestionInput input, string normalized, DateTime now)
    {
        q.CategoryId = input.CategoryId;
        q.Text = input.Text!.Trim();
        q.OptionA = input.Options![0]!.Trim();
        q.OptionB = input.Options[1]!.Trim();
        q.OptionC = input.Options[2]!.Trim();
        q.OptionD = input.Options[3]!.Trim();
        q.CorrectIndex = input.CorrectIndex;
        q.Difficulty = input.Difficulty;
        q.Explanation = string.IsNullOrWhiteSpace(input.Explanation) ? null : input.Explanation.Trim();
        q.Status = input.Status;
        q.NormalizedText = normalized;
        q.UpdatedAt = now;
    }
}
