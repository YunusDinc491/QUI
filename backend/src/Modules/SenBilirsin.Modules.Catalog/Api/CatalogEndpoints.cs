using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SenBilirsin.Modules.Catalog.Application;

namespace SenBilirsin.Modules.Catalog.Api;

/// <summary>Mobil uygulamanın katalog uçları (docs/04): modlar ve yayındaki alt dallar.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // Kimlik doğrulaması gerekir (kayıtlı kullanıcı); içerik herkese aynıdır
        app.MapGet("/api/catalog/modes", async (CatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetPublicCatalogAsync(ct))).RequireAuthorization();
        return app;
    }
}
