using System.Security.Cryptography;

namespace SenBilirsin.Modules.Administration.Security;

/// <summary>
/// Veritabanında saklanan küçük sırları (TOTP anahtarı) AES-256-GCM ile şifreler (docs/08 §7).
/// Anahtar ortam değişkeninden gelir (<c>Admin:SecretKey</c>, Base64, 32 bayt); veritabanında düz metin sır tutulmaz.
/// Biçim: Base64( nonce(12) || etiket(16) || şifreli veri ).
/// </summary>
public sealed class SecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public SecretProtector(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException("Admin:SecretKey tanımlı değil (Base64, 32 bayt). .env.example'a bakın.");
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32)
            throw new InvalidOperationException("Admin:SecretKey tam olarak 32 bayt (Base64) olmalı.");
    }

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedText)
    {
        var all = Convert.FromBase64String(protectedText);
        if (all.Length < NonceSize + TagSize) throw new CryptographicException("Şifreli veri geçersiz.");
        var nonce = all.AsSpan(0, NonceSize);
        var tag = all.AsSpan(NonceSize, TagSize);
        var cipher = all.AsSpan(NonceSize + TagSize);
        var data = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, data);
        return System.Text.Encoding.UTF8.GetString(data);
    }
}
