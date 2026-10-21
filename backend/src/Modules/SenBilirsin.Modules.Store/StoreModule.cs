using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SenBilirsin.Modules.Store.Application;
using SenBilirsin.Modules.Store.Data;

namespace SenBilirsin.Modules.Store;

/// <summary>Mağaza modülünün kayıt noktası. Cüzdan modülü (hediye puanı ve joker defteri) de kayıtlı olmalıdır.</summary>
public static class StoreModule
{
    public static IServiceCollection AddStoreModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<StoreDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());

        // Anahtar ilk kullanıldığı anda ayardan okunur (Coupons__EncryptionKey ortam değişkeni; repoya/loga yazılmaz)
        services.TryAddSingleton(sp => new CouponCipher(sp.GetRequiredService<IConfiguration>()["Coupons:EncryptionKey"]));
        services.TryAddSingleton<PurchaseLocks>();
        services.AddScoped<StoreService>();
        services.AddScoped<StoreAdminService>();
        return services;
    }

    /// <summary>Yarım kalan satın almaları toparlayan arka plan işi (host başına bir kez).</summary>
    public static IServiceCollection AddStoreJobs(this IServiceCollection services) => services.AddHostedService<StoreJobsService>();

    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", StoreDbContext.Schema);

    public static async Task MigrateStoreAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StoreDbContext>().Database.MigrateAsync(ct);
    }
}

/// <summary>Her dakika: 5 dakikadan eski "ayrılmış" kuponları tamamlar ya da serbest bırakır. Hatalar işi durdurmaz.</summary>
public sealed class StoreJobsService(IServiceScopeFactory scopes, IConfiguration config, ILogger<StoreJobsService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Store:Jobs", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var recovered = await scope.ServiceProvider.GetRequiredService<StoreService>().RecoverStaleReservationsAsync(stoppingToken);
                if (recovered > 0) log.LogWarning("{Count} yarım kalmış kupon satın alması toparlandı.", recovered);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Mağaza işi başarısız (bir sonraki turda yeniden denenecek).");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
