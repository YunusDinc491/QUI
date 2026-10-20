using SenBilirsin.Modules.Users.Domain;

namespace SenBilirsin.Tests;

public class ProfileRulesTests
{
    private static readonly ProfanityFilter Filter = new();

    [Theory]
    [InlineData("ali_123")]
    [InlineData("abc")]
    [InlineData("a1234567890123456")] // 17 karakter -> aşağıdaki testte reddedilir; burada geçerli sayılmaz
    public void Username_Length_And_Chars(string name)
    {
        var result = ProfileRules.CheckUsername(name, Filter);
        // 17 karakterlik örnek hariç hepsi geçerli olmalı
        Assert.Equal(name.Length <= 16, result.IsValid);
    }

    [Theory]
    [InlineData("ab", "USERNAME_TOO_SHORT")]
    [InlineData("abcdefghijklmnopq", "USERNAME_TOO_LONG")]
    [InlineData("ali-1", "USERNAME_INVALID_CHARS")]
    [InlineData("şahin", "USERNAME_INVALID_CHARS")]
    [InlineData("ali veli", "USERNAME_INVALID_CHARS")]
    [InlineData("admin", "USERNAME_RESERVED")]
    [InlineData("SenBilirsin", "USERNAME_RESERVED")]
    [InlineData("orospu", "USERNAME_PROFANE")]
    [InlineData("s1ktir", "USERNAME_PROFANE")]
    [InlineData(null, "USERNAME_TOO_SHORT")]
    public void Username_Invalid_ReturnsCode(string? name, string expectedCode)
    {
        var result = ProfileRules.CheckUsername(name, Filter);
        Assert.False(result.IsValid);
        Assert.Equal(expectedCode, result.Code);
    }

    [Fact]
    public void Username_IsLowercasedBeforeChecks()
    {
        Assert.True(ProfileRules.CheckUsername("ALI_123", Filter).IsValid);
        Assert.Equal("ali_123", ProfileRules.NormalizeUsername("  ALI_123 "));
    }

    [Theory]
    [InlineData("Ayşe", true)]
    [InlineData("Ali Veli", true)]
    [InlineData("Ömer.K_1-2", true)]
    [InlineData("A", false)]
    [InlineData("Bu görünen ad yirmi dördü aşıyor", false)]
    [InlineData("Ali<script>", false)]
    [InlineData("Ali😀", false)]
    [InlineData("O.r.o.s.p.u", false)]
    public void DisplayName_Rules(string name, bool valid) =>
        Assert.Equal(valid, ProfileRules.CheckDisplayName(name, Filter).IsValid);

    [Fact]
    public void DisplayName_CollapsesWhitespace() =>
        Assert.Equal("Ali Veli", ProfileRules.NormalizeDisplayName("  Ali    Veli  "));

    [Theory]
    [InlineData(2013, true)]   // 13 yaş: sınırda geçerli
    [InlineData(2014, false)]  // 12 yaş
    [InlineData(1926, true)]   // 100 yaş
    [InlineData(1925, false)]  // 101 yaş
    public void BirthYear_AgeLimits(int birthYear, bool valid) =>
        Assert.Equal(valid, ProfileRules.CheckBirthYear(birthYear, 2026).IsValid);

    [Theory]
    [InlineData("orospu")]
    [InlineData("0r0spu")]
    [InlineData("o.r.o.s.p.u")]
    [InlineData("Siktir_git")]
    [InlineData("AMK")]
    [InlineData("s1k")]
    [InlineData("yarrak")]
    [InlineData("SİKTİR")]
    [InlineData("siiiiiktir")]
    public void Profanity_IsDetected(string text) => Assert.True(Filter.ContainsProfanity(text));

    [Theory]
    [InlineData("musik")]          // içinde "sik" geçen masum sözcük
    [InlineData("oynayarak")]      // "-yarak" eki
    [InlineData("Amina")]          // gerçek bir isim
    [InlineData("Ahmet")]
    [InlineData("basiksin")]
    [InlineData("kamkat")]
    [InlineData("gotham")]
    [InlineData("Şükrü Çağlar Öztürk")]
    [InlineData("")]
    public void Profanity_DoesNotFlagInnocentText(string text) => Assert.False(Filter.ContainsProfanity(text));
}
