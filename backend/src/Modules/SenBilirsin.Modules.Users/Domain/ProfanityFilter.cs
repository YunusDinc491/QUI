using System.Text;

namespace SenBilirsin.Modules.Users.Domain;

/// <summary>
/// Küfür/argo filtresi (kullanıcı adı ve görünen ad için, K-35).
/// Metin normalize edilir (Türkçe harfler, rakamla harf değişimi, ayırıcılar, tekrar eden harfler);
/// uzun kelimeler metnin her yerinde, kısa kelimeler yalnızca tek başına bir sözcük olarak aranır
/// (böylece "musik" gibi masum sözcükler yanlış yakalanmaz).
/// Liste başlangıç tohumudur; ileride admin panelinden düzenlenecek (docs/05, Faz 8).
/// </summary>
public sealed class ProfanityFilter
{
    // Normalize edilmiş (ascii, tekrarsız harf) tohum liste.
    // "yarak" ve "amina" bilerek buraya konmadı: "oynayarak" gibi yaygın eklerde ve "Amina" isminde yanlış yakalar.
    private static readonly string[] SubstringWords =
    [
        "orospu", "siktir", "sikik", "amcik", "aminakoy", "gotveren", "pezevenk",
        "kahpe", "serefsiz", "gavat", "surtuk", "godos",
    ];

    private static readonly string[] WholeTokenWords =
    [
        "amk", "aq", "sik", "got", "pic", "oc", "sg", "mk", "yarak", "ibne",
    ];

    private readonly string[] _substringWords;
    private readonly HashSet<string> _wholeTokenWords;

    public ProfanityFilter() : this(SubstringWords, WholeTokenWords) { }

    public ProfanityFilter(IEnumerable<string> substringWords, IEnumerable<string> wholeTokenWords)
    {
        _substringWords = substringWords.Select(Normalize).Where(w => w.Length > 0).Distinct().ToArray();
        _wholeTokenWords = wholeTokenWords.Select(Normalize).Where(w => w.Length > 0).ToHashSet();
    }

    public bool ContainsProfanity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // 1) Sözcük sözcük: kısa kelimeler tam eşleşmeli, uzunlar içerilebilir.
        foreach (var token in SplitTokens(text))
        {
            var normalizedToken = Normalize(token);
            if (normalizedToken.Length == 0) continue;
            if (_wholeTokenWords.Contains(normalizedToken)) return true;
            if (_substringWords.Any(w => normalizedToken.Contains(w, StringComparison.Ordinal))) return true;
        }

        // 2) Ayırıcıları atlayarak birleşik metin ("o.r.o.s.p.u", "or os pu"): yalnızca uzun kelimeler.
        var joined = Normalize(text);
        return _substringWords.Any(w => joined.Contains(w, StringComparison.Ordinal));
    }

    private static IEnumerable<string> SplitTokens(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            // Harf ve rakamlar ile sık kullanılan "harf yerine geçen" simgeler sözcüğün parçasıdır
            if (char.IsLetterOrDigit(ch) || ch is '@' or '$' or '!')
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Küçük harf, Türkçe harfleri ascii'ye indir, rakam/simge → harf, harf olmayanları at, yinelenenleri tek yap.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        char last = '\0';
        foreach (var raw in text)
        {
            var c = MapChar(raw);
            if (c == '\0') continue;       // harf olmayan (ayırıcı) karakteri at
            if (c == last) continue;       // ardışık aynı harfleri tek yap
            sb.Append(c);
            last = c;
        }

        return sb.ToString();
    }

    private static char MapChar(char c) => c switch
    {
        'İ' or 'I' or 'ı' or 'i' => 'i',
        'Ç' or 'ç' => 'c',
        'Ğ' or 'ğ' => 'g',
        'Ö' or 'ö' => 'o',
        'Ş' or 'ş' => 's',
        'Ü' or 'ü' => 'u',
        '0' => 'o',
        '1' or '!' => 'i',
        '3' => 'e',
        '4' or '@' => 'a',
        '5' or '$' => 's',
        '7' => 't',
        >= 'a' and <= 'z' => c,
        >= 'A' and <= 'Z' => char.ToLowerInvariant(c),
        _ => '\0',
    };
}
