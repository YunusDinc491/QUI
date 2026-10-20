namespace SenBilirsin.Modules.Administration.Storage;

public sealed record DetectedImage(string Extension, string ContentType);

/// <summary>
/// Yüklenen dosyanın gerçekten PNG / JPEG / WebP olduğunu ilk baytlarına bakarak doğrular
/// (dosya adına ya da istemcinin bildirdiği türe güvenilmez). SVG bilerek reddedilir: içine betik gömülebilir.
/// </summary>
public static class ImageValidator
{
    public const int MaxBytes = 1_000_000;

    public static DetectedImage? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return new DetectedImage("png", "image/png");
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return new DetectedImage("jpg", "image/jpeg");
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8))
            return new DetectedImage("webp", "image/webp");
        return null;
    }
}
