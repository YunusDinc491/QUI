using System.Security.Claims;
using SenBilirsin.Modules.Administration.Api;
using SenBilirsin.Modules.Administration.Application;
using SenBilirsin.Modules.Store.Application;
using SenBilirsin.Modules.Store.Domain;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.AdminApi.Endpoints;

public record ProductRequest(ProductType Type, string? Name, string? Description, int Price, string? ImageUrl, bool IsActive, JokerType? JokerType, int? JokerAmount, int? ItemRefId);
public record CodesRequest(string[]? Codes, string? Text);
public record ResolveRequest(bool Replace);

/// <summary>Mağaza yönetimi (docs/05, docs/07 §5): ürünler, şifreli kupon yükleme, "kod çalışmıyor" kuyruğu. Kodlar hiçbir yanıtta ve denetim kaydında yer almaz.</summary>
public static class StoreAdminEndpoints
{
    public static IEndpointRouteBuilder MapStoreAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/store").RequireAuthorization(AdminAuthEndpoints.PolicyName);

        group.MapGet("/products", async (StoreAdminService admin, CancellationToken ct) => Results.Ok(await admin.ListAsync(ct)));

        group.MapPost("/products", async (ProductRequest request, ClaimsPrincipal user, StoreAdminService admin, AuditLogger audit, CancellationToken ct) =>
        {
            var result = await admin.CreateAsync(ToInput(request), ct);
            if (!result.IsSuccess) return Error(result.Error!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "store.product_create", "store_product", result.Value!.Id.ToString(), after: CatalogAdminEndpoints.Json(request), ct: ct);
            return Results.Created($"/api/store/products/{result.Value.Id}", result.Value);
        });

        group.MapPut("/products/{id:int}", async (int id, ProductRequest request, ClaimsPrincipal user, StoreAdminService admin, AuditLogger audit, CancellationToken ct) =>
        {
            var before = (await admin.ListAsync(ct)).FirstOrDefault(p => p.Id == id);
            var result = await admin.UpdateAsync(id, ToInput(request), ct);
            if (!result.IsSuccess) return Error(result.Error!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "store.product_update", "store_product", id.ToString(),
                before: CatalogAdminEndpoints.Json(before), after: CatalogAdminEndpoints.Json(result.Value), ct: ct);
            return Results.Ok(result.Value);
        });

        // Kod yükleme: satır satır metin (CSV'den yapıştırma) ya da dizi. Kodların kendisi denetim kaydına YAZILMAZ, yalnızca sayıları.
        group.MapPost("/products/{id:int}/codes", async (int id, CodesRequest request, ClaimsPrincipal user, StoreAdminService admin, AuditLogger audit, CancellationToken ct) =>
        {
            var lines = request.Codes ?? (request.Text ?? "").Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length > 5000) return Results.Json(new { code = "TOO_MANY_CODES", message = "Tek seferde en fazla 5000 kod yüklenebilir." }, statusCode: 400);
            var result = await admin.AddCodesAsync(id, lines, ct);
            if (!result.IsSuccess) return Error(result.Error!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "store.codes_upload", "store_product", id.ToString(), after: CatalogAdminEndpoints.Json(result.Value), ct: ct);
            return Results.Ok(result.Value);
        });

        group.MapGet("/issues", async (StoreAdminService admin, CancellationToken ct) => Results.Ok(await admin.IssuesAsync(ct)));

        group.MapPost("/issues/{rewardId:long}/resolve", async (long rewardId, ResolveRequest request, ClaimsPrincipal user, StoreService store, AuditLogger audit, CancellationToken ct) =>
        {
            var result = await store.ResolveIssueAsync(rewardId, request.Replace, ct);
            if (!result.IsSuccess) return Error(result.Error!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), request.Replace ? "store.issue_replaced" : "store.issue_rejected", "user_reward", rewardId.ToString(), ct: ct);
            return Results.Ok(new { resolved = true, replaced = request.Replace });
        });

        return app;
    }

    private static ProductInput ToInput(ProductRequest r) => new(r.Type, r.Name, r.Description, r.Price, r.ImageUrl, r.IsActive, r.JokerType, r.JokerAmount, r.ItemRefId);

    private static IResult Error(StoreError e) => Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.StatusCode);
}
