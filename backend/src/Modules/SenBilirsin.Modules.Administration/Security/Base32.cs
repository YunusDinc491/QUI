using System.Text;

namespace SenBilirsin.Modules.Administration.Security;

/// <summary>RFC 4648 Base32 (dolgusuz, büyük harf). Kimlik doğrulayıcı uygulamaların (TOTP) gizli anahtar biçimi.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        var result = new List<byte>(text.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var raw in text.Trim().TrimEnd('=').ToUpperInvariant())
        {
            if (raw == ' ' || raw == '-') continue;
            var value = Alphabet.IndexOf(raw);
            if (value < 0) throw new FormatException("Geçersiz Base32 karakteri.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return result.ToArray();
    }
}
