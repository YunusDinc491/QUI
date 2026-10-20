using SenBilirsin.Modules.Questions.Domain;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.MatchEngine.Domain;

/// <summary>
/// Maç kuralları ve ayarlar (docs/01). Şimdilik sabit varsayılanlar; admin panelinden ayarlanabilir hale
/// Faz 8'de gelir (<c>scoring_rules</c>). Mantık bu ayarları parametre olarak aldığı için tek yerden değişir.
/// </summary>
public sealed record MatchSettings
{
    public int QuestionCount { get; init; } = 10;
    public int SecondsPerQuestion { get; init; } = 15;

    /// <summary>Süre dolduktan sonra gelen cevaba tanınan ağ toleransı.</summary>
    public int GraceMs { get; init; } = 1500;

    /// <summary>Bir sorunun sonucu ekranda kalır, sonra sıradaki soru başlar (docs/01 §4).</summary>
    public int ResultDisplayMs { get; init; } = 3000;

    /// <summary>Bireysel maçta ilk sorunun, istek yanıtından sonra başlama payı (ekranın çizilmesi için).</summary>
    public int FirstQuestionLeadMs { get; init; } = 2000;

    /// <summary>Zorluk sırası: 1–4 kolay, 5–7 orta, 8–10 zor (ayarlanabilir).</summary>
    public int EasyCount { get; init; } = 4;

    public int MediumCount { get; init; } = 3;

    /// <summary>Bireysel puan merdiveni: doğru sayısı → puan. Listede olmayan sayılar 0 puandır.</summary>
    public IReadOnlyDictionary<int, int> SoloLadder { get; init; } = new Dictionary<int, int> { [10] = 100, [9] = 10, [8] = 5 };

    /// <summary>İpucu jokerinin doğru şıkkı göstermesi olasılığı.</summary>
    public double HintCorrectProbability { get; init; } = 0.75;

    /// <summary>Yanlış cevaptan sonra "reklamla devam" kararı için bekleme süresi.</summary>
    public int ContinueWindowSeconds { get; init; } = 45;

    /// <summary>Bir sorunun bitişinden bu kadar sonra hâlâ cevap/istek yoksa bireysel maç terk edilmiş sayılır (iptal, puan yok).</summary>
    public int AbandonAfterSeconds { get; init; } = 120;

    public int HardCount => QuestionCount - EasyCount - MediumCount;
}

public static class ScoringRules
{
    /// <summary>Bireysel puan: 10 doğru = 100, 9 = 10, 8 = 5, 7 ve altı = 0 (varsayılan merdiven).</summary>
    public static int SoloPoints(int correctCount, MatchSettings settings) =>
        settings.SoloLadder.TryGetValue(correctCount, out var points) ? points : 0;
}

public static class MatchPlan
{
    /// <summary>1 tabanlı sıradaki sorunun zorluğu.</summary>
    public static Difficulty DifficultyAt(int position, MatchSettings settings)
    {
        if (position < 1 || position > settings.QuestionCount) throw new ArgumentOutOfRangeException(nameof(position));
        if (position <= settings.EasyCount) return Difficulty.Easy;
        return position <= settings.EasyCount + settings.MediumCount ? Difficulty.Medium : Difficulty.Hard;
    }

    /// <summary>Soru bankasına verilecek seçim planı (zorluk × adet).</summary>
    public static IReadOnlyList<(Difficulty, int)> PickPlan(MatchSettings settings) =>
        [(Difficulty.Easy, settings.EasyCount), (Difficulty.Medium, settings.MediumCount), (Difficulty.Hard, settings.HardCount)];
}

/// <summary>Şık sırası: <c>order[i]</c>, ekranda i. sırada gösterilen şıkkın özgün indeksidir (sunucu karıştırır).</summary>
public static class OptionShuffler
{
    public static int[] Shuffle(Random random)
    {
        int[] order = [0, 1, 2, 3];
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    public static string Serialize(int[] order) => string.Join(',', order);

    public static int[] Parse(string text) => text.Split(',').Select(int.Parse).ToArray();

    /// <summary>Ekrandaki sıradan (display) özgün şık indeksine.</summary>
    public static int ToOriginal(int[] order, int displayIndex) => order[displayIndex];

    /// <summary>Özgün şık indeksinin ekrandaki sırası.</summary>
    public static int ToDisplay(int[] order, int originalIndex) => Array.IndexOf(order, originalIndex);

    public static bool IsPermutation(int[] order) => order.Length == 4 && order.OrderBy(x => x).SequenceEqual([0, 1, 2, 3]);
}

/// <summary>Joker etkileri (docs/01 §10). Hepsi ekrandaki (karışık) şık sırasına göre çalışır.</summary>
public static class JokerRules
{
    /// <summary>Yarı yarıya: 3 yanlış şıktan rastgele 2'si elenir. Doğru şık hiçbir zaman elenmez.</summary>
    public static int[] Half(int correctDisplayIndex, Random random)
    {
        var wrong = Enumerable.Range(0, 4).Where(i => i != correctDisplayIndex).ToList();
        var first = random.Next(wrong.Count);
        var removed = new List<int> { wrong[first] };
        wrong.RemoveAt(first);
        removed.Add(wrong[random.Next(wrong.Count)]);
        removed.Sort();
        return [.. removed];
    }

    /// <summary>İpucu: olasılıkla doğru şıkkı, değilse rastgele bir yanlış şıkkı gösterir.</summary>
    public static int Hint(int correctDisplayIndex, double correctProbability, Random random)
    {
        if (random.NextDouble() < correctProbability) return correctDisplayIndex;
        var wrong = Enumerable.Range(0, 4).Where(i => i != correctDisplayIndex).ToList();
        return wrong[random.Next(wrong.Count)];
    }

    /// <summary>Çift cevap: işaretlenen iki şıktan biri doğruysa doğru sayılır.</summary>
    public static bool IsCorrect(int correctDisplayIndex, int? chosen, int? second) =>
        chosen == correctDisplayIndex || second == correctDisplayIndex;

    public static bool IsValidIndex(int? index) => index is null or (>= 0 and <= 3);

    public static string ToCode(JokerType type) => type switch
    {
        JokerType.Half => "half",
        JokerType.Double => "double",
        JokerType.Hint => "hint",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static bool TryParse(string? code, out JokerType type)
    {
        type = code switch { "half" => JokerType.Half, "double" => JokerType.Double, "hint" => JokerType.Hint, _ => 0 };
        return type != 0;
    }
}
