using System.Text;

namespace SenBilirsin.Modules.Questions.Domain;

/// <summary>Doğrulama sonucu: Code makine tarafından, Message kullanıcıya (admin) Türkçe gösterilir.</summary>
public sealed record RuleResult(bool IsValid, string? Code = null, string? Message = null)
{
    public static readonly RuleResult Ok = new(true);
    public static RuleResult Fail(string code, string message) => new(false, code, message);
}

/// <summary>Soru girişi kuralları ve tekrar tespiti için metin sadeleştirme/benzerlik (docs/05).</summary>
public static class QuestionRules
{
    public const int TextMin = 8;
    public const int TextMax = 500;
    public const int OptionMax = 150;
    public const int ExplanationMax = 1000;
    public const double SimilarityThreshold = 0.85;

    public static RuleResult Check(string? text, IReadOnlyList<string?>? options, int correctIndex, Difficulty difficulty, string? explanation)
    {
        var t = (text ?? "").Trim();
        if (t.Length < TextMin) return RuleResult.Fail("TEXT_TOO_SHORT", $"Soru metni en az {TextMin} karakter olmalı.");
        if (t.Length > TextMax) return RuleResult.Fail("TEXT_TOO_LONG", $"Soru metni en fazla {TextMax} karakter olabilir.");

        if (options is null || options.Count != 4)
            return RuleResult.Fail("OPTIONS_COUNT", "Tam olarak 4 şık gerekli (A, B, C, D).");

        var cleaned = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var o = (options[i] ?? "").Trim();
            if (o.Length == 0) return RuleResult.Fail("OPTION_EMPTY", $"{Letter(i)} şıkkı boş olamaz.");
            if (o.Length > OptionMax) return RuleResult.Fail("OPTION_TOO_LONG", $"{Letter(i)} şıkkı en fazla {OptionMax} karakter olabilir.");
            cleaned.Add(Normalize(o));
        }

        if (cleaned.Distinct().Count() != 4)
            return RuleResult.Fail("OPTIONS_DUPLICATE", "Şıklar birbirinden farklı olmalı.");

        if (correctIndex is < 0 or > 3) return RuleResult.Fail("CORRECT_INVALID", "Doğru şık A–D arasında olmalı.");
        if (!Enum.IsDefined(difficulty)) return RuleResult.Fail("DIFFICULTY_INVALID", "Zorluk kolay/orta/zor olmalı.");
        if (explanation is not null && explanation.Trim().Length > ExplanationMax)
            return RuleResult.Fail("EXPLANATION_TOO_LONG", $"Açıklama en fazla {ExplanationMax} karakter olabilir.");

        return RuleResult.Ok;
    }

    public static string Letter(int index) => "ABCD"[index].ToString();

    /// <summary>Küçük harf, Türkçe harfleri ascii'ye indir, harf/rakam dışını boşluk yap, boşlukları teke indir.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true;
        foreach (var raw in text)
        {
            var c = raw switch
            {
                'İ' or 'I' or 'ı' => 'i',
                'Ç' or 'ç' => 'c',
                'Ğ' or 'ğ' => 'g',
                'Ö' or 'ö' => 'o',
                'Ş' or 'ş' => 's',
                'Ü' or 'ü' => 'u',
                _ => char.ToLowerInvariant(raw),
            };

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>İki sadeleştirilmiş metnin kelime kümesi benzerliği (Jaccard, 0–1). Çok kısa metinlerde yalnızca tam eşitlik 1 sayılır.</summary>
    public static double Similarity(string normalizedA, string normalizedB)
    {
        if (normalizedA == normalizedB) return 1.0;
        var a = Tokens(normalizedA);
        var b = Tokens(normalizedB);
        if (a.Count < 4 || b.Count < 4) return 0.0; // kısa sorularda "Başkent neresi?" gibi benzerlikler yanlış alarm üretir
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> Tokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 2).ToHashSet();
}
