using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Questions.Data;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Modules.Questions.Application;

/// <summary>Oyun sunucusunun gördüğü soru (doğru şık dahil). İstemciye asla olduğu gibi gönderilmez.</summary>
public sealed record BankQuestion(int Id, int CategoryId, string Text, IReadOnlyList<string> Options, int CorrectIndex,
    Difficulty Difficulty, string? Explanation);

/// <param name="Questions">İstenen sırayla: önce kolaylar, sonra ortalar, sonra zorlar.</param>
/// <param name="SeenFilterRelaxed">Yeterli "görülmemiş" soru yoktu; görülmüşlerden de seçildi (panele uyarı konusu).</param>
/// <param name="PoolTooSmall">Havuzda istenen toplam sayıda soru yok; <paramref name="Questions"/> eksik kalır.</param>
public sealed record PickResult(IReadOnlyList<BankQuestion> Questions, bool SeenFilterRelaxed, bool PoolTooSmall);

public enum ReportOutcome { Reported, AlreadyReported, QuestionNotFound, ReasonInvalid }

/// <summary>Diğer modüllerin (maç motoru) kullandığı dar soru bankası arayüzü.</summary>
public interface IQuestionBank
{
    /// <summary>
    /// Verilen kategorilerdeki yayındaki sorulardan, zorluk başına istenen sayıda seçer.
    /// Kullanıcının son 200 gördüğü sorular ve <paramref name="excludeIds"/> mümkünse hariç tutulur.
    /// </summary>
    Task<PickResult> PickAsync(Guid userId, IReadOnlyCollection<int> categoryIds,
        IReadOnlyList<(Difficulty Difficulty, int Count)> plan, IReadOnlyCollection<int>? excludeIds = null, CancellationToken ct = default);

    /// <summary>Rakipli maç: iki oyuncunun da son gördükleri sorular mümkünse hariç tutulur (docs/01 §3).</summary>
    Task<PickResult> PickForUsersAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<int> categoryIds,
        IReadOnlyList<(Difficulty Difficulty, int Count)> plan, CancellationToken ct = default);

    Task<IReadOnlyList<BankQuestion>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>Soru kullanıcıya gösterildi: görülenlere ekler (son 200 tutulur) ve gösterilme sayacını artırır.</summary>
    Task MarkShownAsync(Guid userId, int questionId, CancellationToken ct = default);

    /// <summary>Doğru cevaplanan sorunun doğru sayacını artırır.</summary>
    Task RecordCorrectAsync(int questionId, CancellationToken ct = default);

    Task<ReportOutcome> ReportAsync(Guid userId, int questionId, string? reason, CancellationToken ct = default);
}

public sealed class QuestionBank(QuestionsDbContext db, TimeProvider clock, Random random) : IQuestionBank
{
    public const int SeenLimit = 200;

    public Task<PickResult> PickAsync(Guid userId, IReadOnlyCollection<int> categoryIds,
        IReadOnlyList<(Difficulty Difficulty, int Count)> plan, IReadOnlyCollection<int>? excludeIds = null, CancellationToken ct = default) =>
        PickCoreAsync([userId], categoryIds, plan, excludeIds, ct);

    public Task<PickResult> PickForUsersAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<int> categoryIds,
        IReadOnlyList<(Difficulty Difficulty, int Count)> plan, CancellationToken ct = default) =>
        PickCoreAsync(userIds, categoryIds, plan, null, ct);

    private async Task<PickResult> PickCoreAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<int> categoryIds,
        IReadOnlyList<(Difficulty Difficulty, int Count)> plan, IReadOnlyCollection<int>? excludeIds, CancellationToken ct)
    {
        var candidates = await db.Questions.AsNoTracking()
            .Where(q => q.Status == QuestionStatus.Published && categoryIds.Contains(q.CategoryId))
            .Select(q => new { q.Id, q.Difficulty })
            .ToListAsync(ct);

        // Görülme sırası (eskiden yeniye): gevşetme gerekirse en eski görülenler önce yeniden verilir
        var seenOrder = await db.SeenQuestions.AsNoTracking().Where(s => userIds.Contains(s.UserId))
            .GroupBy(s => s.QuestionId).Select(g => new { Id = g.Key, At = g.Max(x => x.SeenAt) })
            .OrderBy(x => x.At).Select(x => x.Id).ToListAsync(ct);
        var seenRank = seenOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var excluded = excludeIds is null ? [] : excludeIds.ToHashSet();

        bool IsFresh(int id) => !seenRank.ContainsKey(id) && !excluded.Contains(id);

        var chosen = new List<(int Id, Difficulty Slot)>();
        var used = new HashSet<int>();
        var relaxed = false;

        // Aynı zorluktan taşan/eksik kalan ihtiyaç için yakın zorluktan ödünç alınır
        static IEnumerable<Difficulty> Order(Difficulty d) => d switch
        {
            Difficulty.Easy => [Difficulty.Easy, Difficulty.Medium, Difficulty.Hard],
            Difficulty.Medium => [Difficulty.Medium, Difficulty.Easy, Difficulty.Hard],
            _ => [Difficulty.Hard, Difficulty.Medium, Difficulty.Easy],
        };

        foreach (var (difficulty, count) in plan)
        {
            var need = count;
            // 1) aynı zorluktan, görülmemiş sorular (rastgele)
            var fresh = candidates.Where(c => c.Difficulty == difficulty && !used.Contains(c.Id) && IsFresh(c.Id)).Select(c => c.Id).ToList();
            foreach (var id in Shuffle(fresh).Take(need))
            {
                chosen.Add((id, difficulty));
                used.Add(id);
                need--;
            }

            // 2) hâlâ eksikse kısıt gevşer: aynı zorluğun görülmüşleri (en eskiler önce), sonra yakın zorluklar
            if (need > 0)
            {
                relaxed = true;
                foreach (var source in Order(difficulty))
                {
                    if (need == 0) break;
                    var pool = candidates.Where(c => c.Difficulty == source && !used.Contains(c.Id) && !excluded.Contains(c.Id))
                        .OrderBy(c => seenRank.TryGetValue(c.Id, out var r) ? r : int.MinValue).Select(c => c.Id).ToList();
                    // Önce yeni olanlar (rastgele), sonra en eski görülenler
                    var unseen = Shuffle(pool.Where(id => !seenRank.ContainsKey(id)).ToList());
                    var seen = pool.Where(id => seenRank.ContainsKey(id));
                    foreach (var id in unseen.Concat(seen).Take(need))
                    {
                        chosen.Add((id, difficulty));
                        used.Add(id);
                        need--;
                    }
                }
            }
        }

        // Açıkça hariç tutulanlar (örn. aynı maçta zaten sorulanlar) hiçbir zaman geri verilmez; havuz yetmezse eksik kalır
        var totalNeeded = plan.Sum(p => p.Count);
        var rows = await GetByIdsAsync(chosen.Select(c => c.Id).ToList(), ct);
        var byId = rows.ToDictionary(r => r.Id);
        var ordered = chosen.Select(c => byId[c.Id]).ToList();
        return new PickResult(ordered, relaxed, chosen.Count < totalNeeded);
    }

    public async Task<IReadOnlyList<BankQuestion>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        var rows = await db.Questions.AsNoTracking().Where(q => ids.Contains(q.Id)).ToListAsync(ct);
        return rows.Select(q => new BankQuestion(q.Id, q.CategoryId, q.Text, q.Options, q.CorrectIndex, q.Difficulty, q.Explanation)).ToList();
    }

    public async Task MarkShownAsync(Guid userId, int questionId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var seen = await db.SeenQuestions.FirstOrDefaultAsync(s => s.UserId == userId && s.QuestionId == questionId, ct);
        if (seen is null) db.SeenQuestions.Add(new UserSeenQuestion { UserId = userId, QuestionId = questionId, SeenAt = now });
        else seen.SeenAt = now;
        await db.SaveChangesAsync(ct);

        // Yalnızca son SeenLimit kayıt tutulur
        var excess = await db.SeenQuestions.CountAsync(s => s.UserId == userId, ct) - SeenLimit;
        if (excess > 0)
        {
            var oldest = await db.SeenQuestions.Where(s => s.UserId == userId).OrderBy(s => s.SeenAt).Take(excess).ToListAsync(ct);
            db.SeenQuestions.RemoveRange(oldest);
            await db.SaveChangesAsync(ct);
        }

        await db.Questions.Where(q => q.Id == questionId).ExecuteUpdateAsync(s => s.SetProperty(q => q.ShownCount, q => q.ShownCount + 1), ct);
    }

    public Task RecordCorrectAsync(int questionId, CancellationToken ct = default) =>
        db.Questions.Where(q => q.Id == questionId).ExecuteUpdateAsync(s => s.SetProperty(q => q.CorrectCount, q => q.CorrectCount + 1), ct);

    public async Task<ReportOutcome> ReportAsync(Guid userId, int questionId, string? reason, CancellationToken ct = default)
    {
        var text = (reason ?? "").Trim();
        if (text.Length == 0 || text.Length > QuestionReport.ReasonMax) return ReportOutcome.ReasonInvalid;
        if (!await db.Questions.AnyAsync(q => q.Id == questionId, ct)) return ReportOutcome.QuestionNotFound;
        if (await db.Reports.AnyAsync(r => r.QuestionId == questionId && r.UserId == userId, ct)) return ReportOutcome.AlreadyReported;

        db.Reports.Add(new QuestionReport { QuestionId = questionId, UserId = userId, Reason = text, CreatedAt = clock.GetUtcNow().UtcDateTime });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı anda gelen ikinci bildirim benzersiz dizine takıldı
            return ReportOutcome.AlreadyReported;
        }

        await db.Questions.Where(q => q.Id == questionId).ExecuteUpdateAsync(s => s.SetProperty(q => q.ReportCount, q => q.ReportCount + 1), ct);
        return ReportOutcome.Reported;
    }

    private List<int> Shuffle(List<int> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
