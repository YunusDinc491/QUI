using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Catalog.Data;

namespace SenBilirsin.Modules.Catalog;

/// <summary>Katalog modülünün kayıt noktası. Başka modüller yalnızca <see cref="ICatalogQueries"/> arayüzünü kullanır.</summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddDbContext<CatalogDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CatalogService>();
        services.AddScoped<CatalogAdminService>();
        services.AddScoped<ICatalogQueries>(sp => sp.GetRequiredService<CatalogService>());
        return services;
    }

    /// <summary>Migration geçmiş tablosu da modülün şemasında durur.</summary>
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.Schema);

    public static async Task MigrateCatalogAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(ct);
    }
}
