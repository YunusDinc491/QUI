using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SenBilirsin.Modules.Store.Application;
using SenBilirsin.Modules.Users.Api;

namespace SenBilirsin.Modules.Store.Api;

public sealed record PurchaseRequest(int ProductId, string? RequestId);
public sealed record ReportIssueRequest(string? Text, string? ScreenshotUrl);

/// <summary>Mağaza uçları (docs/07 §5): ürünler, satın alma, Ödüllerim, iki aşamalı kod gösterimi, sorun bildirimi.</summary>
public static class StoreEndpoints
{
    public static IEndpointRouteBuilder MapStoreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/store").RequireAuthorization().AddEndpointFilter<ActiveDeviceFilter>();

        group.MapGet("/products", async (HttpContext http, StoreService store, CancellationToken ct) =>
            Results.Ok(await store.ListAsync(ActivePlayer.IdOf(http), ct)));

        group.MapPost("/purchase", async (HttpContext http, PurchaseRequest request, StoreService store, CancellationToken ct) =>
            ToResult(await store.PurchaseAsync(ActivePlayer.IdOf(http), request.ProductId, request.RequestId, ct)));

        group.MapGet("/rewards", async (HttpContext http, StoreService store, CancellationToken ct) =>
            Results.Ok(await store.RewardsAsync(ActivePlayer.IdOf(http), ct)));

        // Kodu gösterme: istemci önce uyarı diyaloğu gösterir, onaydan sonra bu uç çağrılır (her çağrı sayılır)
        group.MapPost("/rewards/{id:long}/reveal", async (HttpContext http, long id, StoreService store, CancellationToken ct) =>
            ToResult(await store.RevealAsync(ActivePlayer.IdOf(http), id, ct)));

        group.MapPost("/rewards/{id:long}/report", async (HttpContext http, long id, ReportIssueRequest request, StoreService store, CancellationToken ct) =>
        {
            var result = await store.ReportIssueAsync(ActivePlayer.IdOf(http), id, request.Text, request.ScreenshotUrl, ct);
            return result.IsSuccess ? Results.NoContent() : Error(result.Error!);
        });

        return app;
    }

    private static IResult ToResult<T>(StoreResult<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Error(result.Error!);

    private static IResult Error(StoreError e) => Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.StatusCode);
}
