using System.Text.RegularExpressions;

namespace SenBilirsin.Modules.Users.Domain;

/// <summary>Doğrulama sonucu: Code makine tarafından, Message kullanıcıya gösterilmek üzere Türkçedir.</summary>
public sealed record RuleResult(bool IsValid, string? Code = null, string? Message = null)
{
    public static readonly RuleResult Ok = new(true);
    public static RuleResult Fail(string code, string message) => new(false, code, message);
}

/// <summary>Kullanıcı adı, görünen ad ve doğum yılı kuralları (docs/08).</summary>
public static partial class ProfileRules
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 16;
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 24;
    public const int MinimumAge = 13;
    public const int MaximumAge = 100;

    private static readonly HashSet<string> ReservedUsernames =
    [
        "admin", "administrator", "moderator", "senbilirsin", "qui", "destek", "support", "bot", "sistem", "system", "root",
    ];

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex UsernameChars();

    // Harf (Türkçe dahil), rakam, boşluk, nokta, alt çizgi, tire
    [GeneratedRegex(@"^[\p{L}\p{N} ._\-]+$")]
    private static partial Regex DisplayNameChars();

    /// <summary>Kullanıcı adını küçük harfe çevirir (benzersizlik küçük harf üzerindendir).</summary>
    public static string NormalizeUsername(string? raw) => (raw ?? "").Trim().ToLowerInvariant();

    public static RuleResult CheckUsername(string? raw, ProfanityFilter profanity)
    {
        var u = NormalizeUsername(raw);
        if (u.Length < UsernameMinLength)
            return RuleResult.Fail("USERNAME_TOO_SHORT", $"Kullanıcı adı en az {UsernameMinLength} karakter olmalı.");
        if (u.Length > UsernameMaxLength)
            return RuleResult.Fail("USERNAME_TOO_LONG", $"Kullanıcı adı en fazla {UsernameMaxLength} karakter olabilir.");
        if (!UsernameChars().IsMatch(u))
            return RuleResult.Fail("USERNAME_INVALID_CHARS", "Kullanıcı adında yalnızca küçük harf (a-z), rakam ve alt çizgi kullanılabilir.");
        if (ReservedUsernames.Contains(u))
            return RuleResult.Fail("USERNAME_RESERVED", "Bu kullanıcı adı kullanılamaz.");
        if (profanity.ContainsProfanity(u))
            return RuleResult.Fail("USERNAME_PROFANE", "Bu kullanıcı adı uygun değil.");
        return RuleResult.Ok;
    }

    public static string NormalizeDisplayName(string? raw)
    {
        // Baştaki/sondaki boşlukları at, ardışık boşlukları teke indir
        var trimmed = (raw ?? "").Trim();
        return Regex.Replace(trimmed, @"\s{2,}", " ");
    }

    public static RuleResult CheckDisplayName(string? raw, ProfanityFilter profanity)
    {
        var d = NormalizeDisplayName(raw);
        if (d.Length < DisplayNameMinLength)
            return RuleResult.Fail("DISPLAYNAME_TOO_SHORT", $"Görünen ad en az {DisplayNameMinLength} karakter olmalı.");
        if (d.Length > DisplayNameMaxLength)
            return RuleResult.Fail("DISPLAYNAME_TOO_LONG", $"Görünen ad en fazla {DisplayNameMaxLength} karakter olabilir.");
        if (!DisplayNameChars().IsMatch(d))
            return RuleResult.Fail("DISPLAYNAME_INVALID_CHARS", "Görünen ad yalnızca harf, rakam, boşluk, nokta, alt çizgi ve tire içerebilir.");
        if (profanity.ContainsProfanity(d))
            return RuleResult.Fail("DISPLAYNAME_PROFANE", "Bu görünen ad uygun değil.");
        return RuleResult.Ok;
    }

    /// <summary>Doğum yılından yaklaşık yaş (yıl bazlı): en az 13, en çok 100 (K-36).</summary>
    public static RuleResult CheckBirthYear(int birthYear, int currentYear)
    {
        var age = currentYear - birthYear;
        if (age < MinimumAge)
            return RuleResult.Fail("AGE_TOO_YOUNG", $"Uygulamayı kullanmak için en az {MinimumAge} yaşında olmalısın.");
        if (age > MaximumAge)
            return RuleResult.Fail("BIRTHYEAR_INVALID", "Doğum yılı geçersiz.");
        return RuleResult.Ok;
    }
}
