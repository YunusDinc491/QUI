using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SenBilirsin.Modules.Administration.Application;
using SenBilirsin.Modules.Administration.Storage;

namespace SenBilirsin.Modules.Administration.Api;

/// <summary>Görsel yükleme (kategori ikonu, avatar, çerçeve). Yalnızca yönetici; PNG/JPEG/WebP, en çok 1 MB.</summary>
public static class UploadEndpoints
{
    private static readonly string[] Folders = ["categories", "avatars", "frames"];

    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/uploads/image", async (IFormFile? file, string? folder, ClaimsPrincipal user,
            IFileStorage storage, AuditLogger audit, CancellationToken ct) =>
        {
            if (folder is null || !Folders.Contains(folder))
                return Error("FOLDER_INVALID", $"Klasör şunlardan biri olmalı: {string.Join(", ", Folders)}.");
            if (file is null || file.Length == 0)
                return Error("FILE_MISSING", "Dosya seçilmedi.");
            if (file.Length > ImageValidator.MaxBytes)
                return Error("FILE_TOO_LARGE", "Görsel en fazla 1 MB olabilir.");

            await using var stream = file.OpenReadStream();
            var header = new byte[16];
            var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
            var image = ImageValidator.Detect(header.AsSpan(0, read));
            if (image is null)
                return Error("FILE_TYPE_INVALID", "Yalnızca PNG, JPEG veya WebP görsel yüklenebilir.");

            stream.Position = 0;
            var key = $"{folder}/{Guid.NewGuid():N}.{image.Extension}";
            await storage.SaveAsync(key, stream, image.ContentType, ct);

            var url = storage.PublicUrl(key);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "upload.image", "file", key, after: url, ct: ct);
            return Results.Ok(new { key, url });
        }).RequireAuthorization(AdminAuthEndpoints.PolicyName).DisableAntiforgery();

        return app;
    }

    private static IResult Error(string code, string message) => Results.Json(new { code, message }, statusCode: 400);
}
