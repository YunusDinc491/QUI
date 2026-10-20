using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using SenBilirsin.Modules.Administration.Api;
using SenBilirsin.Modules.Administration.Application;
using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.AdminApi.Endpoints;

public record CategoryRequest(int ModeId, string? Name, string? ImageUrl, string? ColorHex, int? Order);

/// <summary>Kategorideki soru sayıları: yayındakiler zorluğa göre, taslak ve arşiv toplamı (docs/05 §2).</summary>
public record CategoryCounts(int Easy, int Medium, int Hard, int Draft, int Archived);

public record CategoryListItem(AdminCategory Category, CategoryCounts Counts);

/// <summary>Kategori yönetimi uçları. Her yazma işlemi denetim kaydına düşer.</summary>
public static class CatalogAdminEndpoints
{
    public static IEndpointRouteBuilder MapCatalogAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization(AdminAuthEndpoints.PolicyName);

        group.MapGet("/", async (CatalogAdminService catalog, QuestionService questions, CancellationToken ct) =>
        {
            var categories = await catalog.ListAsync(ct);
            var counts = await questions.GetCountsAsync(null, ct);
            return Results.Ok(categories.Select(c => new CategoryListItem(c, CountsFor(counts, c.Id))));
        });

        group.MapPost("/", async (CategoryRequest request, ClaimsPrincipal user, CatalogAdminService catalog, AuditLogger audit, CancellationToken ct) =>
        {
            var result = await catalog.CreateAsync(ToInput(request), ct);
            if (!result.IsSuccess) return Error(result.Error!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "category.create", "category", result.Value!.Id.ToString(),
                after: Json(result.Value), ct: ct);
            return Results.Created($"/api/categories/{result.Value.Id}", result.Value);
        });

        group.MapPut("/{id:int}", async (int id, CategoryRequest request, ClaimsPrincipal user, CatalogAdminService catalog, AuditLogger audit, CancellationToken ct) =>
        {
            var before = (await catalog.ListAsync(ct)).FirstOrDefault(c => c.Id == id);
            var result = await catalog.UpdateAsync(id, ToInput(request), ct);
            if (!result.IsSuccess) return Error(result.Error!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "category.update", "category", id.ToString(),
                before: Json(before), after: Json(result.Value), ct: ct);
            return Results.Ok(result.Value);
        });

        group.MapPost("/{id:int}/publish", (int id, ClaimsPrincipal user, CatalogAdminService catalog, AuditLogger audit, CancellationToken ct) =>
            SetPublished(id, true, user, catalog, audit, ct));
        group.MapPost("/{id:int}/unpublish", (int id, ClaimsPrincipal user, CatalogAdminService catalog, AuditLogger audit, CancellationToken ct) =>
            SetPublished(id, false, user, catalog, audit, ct));

        return app;
    }

    private static async Task<IResult> SetPublished(int id, bool published, ClaimsPrincipal user, CatalogAdminService catalog, AuditLogger audit, CancellationToken ct)
    {
        var result = await catalog.SetPublishedAsync(id, published, ct);
        if (!result.IsSuccess) return Error(result.Error!);
        await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), published ? "category.publish" : "category.unpublish", "category",
            id.ToString(), after: Json(new { published }), ct: ct);
        return Results.Ok(result.Value);
    }

    private static CategoryInput ToInput(CategoryRequest r) => new(r.ModeId, r.Name, r.ImageUrl, r.ColorHex, r.Order);

    private static CategoryCounts CountsFor(IReadOnlyList<QuestionCount> counts, int categoryId)
    {
        int Sum(Func<QuestionCount, bool> predicate) => counts.Where(c => c.CategoryId == categoryId).Where(predicate).Sum(c => c.Count);
        return new CategoryCounts(
            Sum(c => c.Status == QuestionStatus.Published && c.Difficulty == Difficulty.Easy),
            Sum(c => c.Status == QuestionStatus.Published && c.Difficulty == Difficulty.Medium),
            Sum(c => c.Status == QuestionStatus.Published && c.Difficulty == Difficulty.Hard),
            Sum(c => c.Status == QuestionStatus.Draft),
            Sum(c => c.Status == QuestionStatus.Archived));
    }

    private static IResult Error(CatalogError e) => Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.StatusCode);

    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Denetim kaydındaki önce/sonra alanları: okunur (camelCase, enum metin) JSON.</summary>
    internal static string Json(object? value) => JsonSerializer.Serialize(value, AuditJson);
}
