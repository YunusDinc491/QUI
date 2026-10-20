using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SenBilirsin.Modules.Administration.Security;

/// <summary>
/// Zamana dayalı tek kullanımlık parola (TOTP, RFC 6238; SHA-1, 6 hane, 30 sn). Google Authenticator vb. uygulamalarla uyumlu.
/// Aynı kodun yeniden kullanılması <c>lastUsedStep</c> ile engellenir.
/// </summary>
public static class TotpService
{
    public const int PeriodSeconds = 30;
    public const int Digits = 6;

    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static long StepFor(DateTimeOffset time) => time.ToUnixTimeSeconds() / PeriodSeconds;

    public static string ComputeCode(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Kodu doğrular (saat farkı için ±1 adım tolerans). Başarılıysa eşleşen adımı döndürür;
    /// <paramref name="lastUsedStep"/> veya daha eski bir adım kabul edilmez (tekrar kullanım engeli).
    /// </summary>
    public static bool TryVerify(byte[] secret, string? code, DateTimeOffset now, long lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        var cleaned = (code ?? "").Replace(" ", "").Trim();
        if (cleaned.Length != Digits || !cleaned.All(char.IsAsciiDigit)) return false;

        var current = StepFor(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step <= lastUsedStep) continue;
            // Sabit zamanlı karşılaştırma
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(ComputeCode(secret, step)),
                    System.Text.Encoding.ASCII.GetBytes(cleaned)))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    /// <summary>Kimlik doğrulayıcı uygulamaya eklemek için otpauth adresi (QR/elle giriş).</summary>
    public static string OtpAuthUri(string issuer, string account, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={PeriodSeconds}";
}
