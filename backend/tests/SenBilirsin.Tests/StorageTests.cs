using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SenBilirsin.Modules.Administration.Storage;

namespace SenBilirsin.Tests;

/// <summary>Faz 2.7: dosya depolama soyutlaması ve görsel yükleme ucu.</summary>
[Collection("admin-api")]
public class StorageTests(AdminApiFactory factory)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBP"u8, .. "VP8 "u8];

    private static MultipartFormDataContent Form(byte[] bytes, string fileName = "x.png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    // ---- Görsel türü tanıma ----

    [Fact]
    public void Detect_RecognisesPngJpegWebp_ByMagicBytes()
    {
        Assert.Equal("png", ImageValidator.Detect(Png)!.Extension);
        Assert.Equal("jpg", ImageValidator.Detect(Jpeg)!.Extension);
        Assert.Equal("image/webp", ImageValidator.Detect(Webp)!.ContentType);
    }

    [Fact]
    public void Detect_RejectsSvgTextAndShortInput()
    {
        Assert.Null(ImageValidator.Detect("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8));
        Assert.Null(ImageValidator.Detect("merhaba dunya, bu bir gorsel degil"u8));
        Assert.Null(ImageValidator.Detect([0x89, 0x50]));
        Assert.Null(ImageValidator.Detect([]));
    }

    // ---- Yerel depolama ----

    [Fact]
    public async Task LocalStorage_SavesReadsDeletes_AndBuildsPublicUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "qui-storage-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStorage(root, "http://x.test/uploads/");
        try
        {
            await storage.SaveAsync("categories/a1.png", new MemoryStream(Png), "image/png");
            Assert.Equal(Png, await File.ReadAllBytesAsync(Path.Combine(root, "categories", "a1.png")));
            Assert.Equal("http://x.test/uploads/categories/a1.png", storage.PublicUrl("categories/a1.png"));

            await storage.DeleteAsync("categories/a1.png");
            Assert.False(File.Exists(Path.Combine(root, "categories", "a1.png")));
            await storage.DeleteAsync("categories/yok.png"); // olmayan dosya hata vermez
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("../disari.png")]
    [InlineData("categories/../../disari.png")]
    [InlineData("/mutlak/yol.png")]
    [InlineData("categories\\ters.png")]
    [InlineData("bosluk var.png")]
    [InlineData("")]
    public async Task LocalStorage_RejectsUnsafeKeys(string key)
    {
        var storage = new LocalFileStorage(Path.Combine(Path.GetTempPath(), "qui-storage-" + Guid.NewGuid().ToString("N")), "http://x.test/uploads");
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(key, new MemoryStream(Png), "image/png"));
        Assert.Throws<ArgumentException>(() => storage.PublicUrl(key));
        Assert.False(StorageKey.IsSafe(key));
    }

    [Fact]
    public void R2Storage_RequiresItsSettings()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["R2:Bucket"] = "b" }).Build();
        var ex = Assert.Throws<InvalidOperationException>(() => { _ = new R2FileStorage(config); });
        Assert.Contains("R2:", ex.Message);
    }

    // ---- Yükleme ucu ----

    [Fact]
    public async Task Upload_RequiresLogin()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/uploads/image?folder=categories", Form(Png));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_StoresRealImage_ReturnsUrl_AndServesItBack()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/uploads/image?folder=categories", Form(Png));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var key = body.GetProperty("key").GetString()!;
        Assert.StartsWith("categories/", key);
        Assert.EndsWith(".png", key);
        Assert.StartsWith("http://test.local/uploads/categories/", body.GetProperty("url").GetString());

        // Dosya diskte, ve /uploads altından sunuluyor (nosniff ile)
        Assert.True(File.Exists(Path.Combine(factory.UploadsPath, key)));
        var served = await client.GetAsync("/uploads/" + key);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());
        Assert.Contains("nosniff", served.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task Upload_IgnoresClientFileNameAndDeclaredType_UsesDetectedType()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        // İstemci "html" adı ve "image/png" türü bildirse de içerik JPEG ise .jpg olarak saklanır
        var response = await client.PostAsync("/api/uploads/image?folder=avatars", Form(Jpeg, "../../evil.html"));
        var key = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()).GetProperty("key").GetString()!;
        Assert.StartsWith("avatars/", key);
        Assert.EndsWith(".jpg", key);
        Assert.DoesNotContain("evil", key);
    }

    [Fact]
    public async Task Upload_RejectsFakeImages_Svg_TooLarge_UnknownFolder_AndMissingFile()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        async Task<string> CodeOf(HttpResponseMessage r) =>
            JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync()).GetProperty("code").GetString()!;

        var text = await client.PostAsync("/api/uploads/image?folder=categories", Form("bu bir resim degil"u8.ToArray(), "a.png"));
        Assert.Equal("FILE_TYPE_INVALID", await CodeOf(text));

        var svg = await client.PostAsync("/api/uploads/image?folder=categories", Form("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "a.svg"));
        Assert.Equal("FILE_TYPE_INVALID", await CodeOf(svg));

        var big = new byte[ImageValidator.MaxBytes + 1];
        Png.CopyTo(big, 0);
        Assert.Equal("FILE_TOO_LARGE", await CodeOf(await client.PostAsync("/api/uploads/image?folder=categories", Form(big))));

        Assert.Equal("FOLDER_INVALID", await CodeOf(await client.PostAsync("/api/uploads/image?folder=../etc", Form(Png))));
        Assert.Equal("FOLDER_INVALID", await CodeOf(await client.PostAsync("/api/uploads/image", Form(Png))));
        Assert.Equal("FILE_MISSING", await CodeOf(await client.PostAsync("/api/uploads/image?folder=categories", new MultipartFormDataContent { { new StringContent("x"), "baska-alan" } })));
    }
}
