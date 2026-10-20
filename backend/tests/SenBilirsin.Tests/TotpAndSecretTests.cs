using System.Security.Cryptography;
using System.Text;
using SenBilirsin.Modules.Administration.Security;

namespace SenBilirsin.Tests;

/// <summary>Faz 2.3: TOTP (RFC 6238), Base32 ve sır şifreleme birim testleri.</summary>
public class TotpAndSecretTests
{
    // RFC 6238 ek-B test anahtarı (SHA-1): ASCII "12345678901234567890"
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]          // RFC: 94287082 → son 6 hane
    [InlineData(1111111109L, "081804")]  // 07081804
    [InlineData(1111111111L, "050471")]  // 14050471
    [InlineData(1234567890L, "005924")]  // 89005924
    [InlineData(2000000000L, "279037")]  // 69279037
    public void ComputeCode_MatchesRfc6238Vectors(long unixSeconds, string expected)
    {
        var step = TotpService.StepFor(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        Assert.Equal(expected, TotpService.ComputeCode(RfcSecret, step));
    }

    [Fact]
    public void Verify_AcceptsCurrentAndNeighbourSteps_ButNotFarOnes()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = TotpService.StepFor(now);

        Assert.True(TotpService.TryVerify(RfcSecret, TotpService.ComputeCode(RfcSecret, step), now, 0, out var matched));
        Assert.Equal(step, matched);
        Assert.True(TotpService.TryVerify(RfcSecret, TotpService.ComputeCode(RfcSecret, step - 1), now, 0, out _));
        Assert.True(TotpService.TryVerify(RfcSecret, TotpService.ComputeCode(RfcSecret, step + 1), now, 0, out _));
        Assert.False(TotpService.TryVerify(RfcSecret, TotpService.ComputeCode(RfcSecret, step + 3), now, 0, out _));
        Assert.False(TotpService.TryVerify(RfcSecret, TotpService.ComputeCode(RfcSecret, step - 3), now, 0, out _));
    }

    [Fact]
    public void Verify_RejectsReplayOfAlreadyUsedStep()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = TotpService.StepFor(now);
        var code = TotpService.ComputeCode(RfcSecret, step);

        Assert.True(TotpService.TryVerify(RfcSecret, code, now, step - 1, out _));
        Assert.False(TotpService.TryVerify(RfcSecret, code, now, step, out _)); // aynı adım artık kullanılamaz
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public void Verify_RejectsMalformedCodes(string? code) =>
        Assert.False(TotpService.TryVerify(RfcSecret, code, DateTimeOffset.UtcNow, 0, out _));

    [Fact]
    public void Verify_IgnoresSpacesInCode()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var code = TotpService.ComputeCode(RfcSecret, TotpService.StepFor(now));
        Assert.True(TotpService.TryVerify(RfcSecret, code[..3] + " " + code[3..], now, 0, out _));
    }

    [Fact]
    public void Base32_RoundTrips_AndMatchesKnownVector()
    {
        // RFC 4648: "foobar" → MZXW6YTBOI
        Assert.Equal("MZXW6YTBOI", Base32.Encode(Encoding.ASCII.GetBytes("foobar")));
        Assert.Equal("foobar", Encoding.ASCII.GetString(Base32.Decode("mzxw6ytboi")));

        var random = RandomNumberGenerator.GetBytes(20);
        Assert.Equal(random, Base32.Decode(Base32.Encode(random)));
        Assert.Throws<FormatException>(() => Base32.Decode("ABC1"));
    }

    [Fact]
    public void GenerateSecret_IsRandom_AndOtpAuthUriContainsIt()
    {
        var a = TotpService.GenerateSecret();
        Assert.NotEqual(a, TotpService.GenerateSecret());
        Assert.Equal(20, Base32.Decode(a).Length);

        var uri = TotpService.OtpAuthUri("QU¿ Yönetim", "a@b.com", a);
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("secret=" + a, uri);
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void SecretProtector_RoundTrips_AndCiphertextIsRandomized()
    {
        var protector = new SecretProtector(NewKey());
        var one = protector.Protect("JBSWY3DPEHPK3PXP");
        var two = protector.Protect("JBSWY3DPEHPK3PXP");

        Assert.NotEqual(one, two);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", one);
        Assert.Equal("JBSWY3DPEHPK3PXP", protector.Unprotect(one));
    }

    [Fact]
    public void SecretProtector_RejectsTamperedData_AndWrongKey()
    {
        var protector = new SecretProtector(NewKey());
        var bytes = Convert.FromBase64String(protector.Protect("gizli"));
        bytes[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(bytes)));

        var other = new SecretProtector(NewKey());
        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(protector.Protect("gizli")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("QUJD")] // 3 bayt: çok kısa
    public void SecretProtector_RequiresA32ByteKey(string? key) =>
        Assert.Throws<InvalidOperationException>(() => new SecretProtector(key));
}
