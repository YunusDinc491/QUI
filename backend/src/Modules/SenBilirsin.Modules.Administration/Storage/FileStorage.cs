using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace SenBilirsin.Modules.Administration.Storage;

/// <summary>
/// Görsel gibi dosyaların saklandığı yer (docs/02). Geliştirmede yerel klasör, üretimde Cloudflare R2.
/// Anahtar (key) "klasor/dosya.png" biçimindedir; herkese açık adres <see cref="PublicUrl"/> ile alınır.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    string PublicUrl(string key);
}

/// <summary>Anahtarın güvenli olup olmadığını denetler (klasör dışına çıkma, tuhaf karakterler).</summary>
public static class StorageKey
{
    public static bool IsSafe(string key) =>
        key.Length is > 0 and <= 200
        && !key.StartsWith('/')
        && !key.Contains("..")
        && !key.Contains('\\')
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.');

    public static void Ensure(string key)
    {
        if (!IsSafe(key)) throw new ArgumentException("Geçersiz dosya anahtarı.", nameof(key));
    }
}

/// <summary>Geliştirme için: dosyaları yerel bir klasöre yazar; AdminApi onları <c>/uploads</c> altında sunar.</summary>
public sealed class LocalFileStorage(string rootPath, string publicBaseUrl) : IFileStorage
{
    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public string PublicUrl(string key)
    {
        StorageKey.Ensure(key);
        return $"{publicBaseUrl.TrimEnd('/')}/{key}";
    }

    private string PathFor(string key)
    {
        StorageKey.Ensure(key);
        var full = Path.GetFullPath(Path.Combine(RootPath, key));
        // Savunma katmanı: çözümlenen yol kök klasörün altında kalmalı
        if (!full.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Geçersiz dosya anahtarı.", nameof(key));
        return full;
    }
}

/// <summary>
/// Cloudflare R2 (S3 uyumlu). Gerekli ayarlar: R2:AccountId, R2:AccessKeyId, R2:SecretAccessKey, R2:Bucket,
/// R2:PublicBaseUrl (bucket'ın herkese açık adresi / özel alan adı).
/// </summary>
public sealed class R2FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly string _publicBaseUrl;

    public R2FileStorage(IConfiguration config)
    {
        string Need(string name) => config[$"R2:{name}"] is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException($"R2:{name} tanımlı değil (Storage:Provider=R2 için gerekli).");

        _bucket = Need("Bucket");
        _publicBaseUrl = Need("PublicBaseUrl");
        var s3 = new AmazonS3Config
        {
            ServiceURL = $"https://{Need("AccountId")}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            // R2, SDK 4.x'in varsayılan ek sağlama toplamı başlıklarını sevmez; yalnızca gerektiğinde hesaplansın
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        _client = new AmazonS3Client(new BasicAWSCredentials(Need("AccessKeyId"), Need("SecretAccessKey")), s3);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        StorageKey.Ensure(key);
        // Akışsız imzalama için belleğe al (görseller küçük: en çok 1 MB)
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = buffer,
            ContentType = contentType,
            DisablePayloadSigning = false,
        }, ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        StorageKey.Ensure(key);
        await _client.DeleteObjectAsync(_bucket, key, ct);
    }

    public string PublicUrl(string key)
    {
        StorageKey.Ensure(key);
        return $"{_publicBaseUrl.TrimEnd('/')}/{key}";
    }

    public void Dispose() => _client.Dispose();
}
