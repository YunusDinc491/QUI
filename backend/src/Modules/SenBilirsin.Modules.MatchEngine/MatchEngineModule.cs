using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SenBilirsin.Modules.MatchEngine.Application;
using SenBilirsin.Modules.MatchEngine.Data;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.MatchEngine.Versus;

namespace SenBilirsin.Modules.MatchEngine;

/// <summary>
/// Maç motorunun kayıt noktası. Soru bankası, katalog, joker cüzdanı modüllerinin de kayıtlı olması gerekir.
/// </summary>
public static class MatchEngineModule
{
    public static IServiceCollection AddMatchEngineModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(Random.Shared);
        services.AddDbContext<MatchEngineDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());

        services.TryAddSingleton(new MatchSettings());
        services.TryAddSingleton<MatchLocks>();
        services.TryAddSingleton<IRewardedAdGate, TestRewardedAdGate>();
        services.AddScoped<MatchScopeResolver>();
        services.AddScoped<SoloMatchService>();

        // Rakipli maç (Faz 4). Olay iletici (SignalR) host tarafından kaydedilir; yoksa olaylar yutulur.
        services.TryAddSingleton(new VersusSettings());
        services.TryAddSingleton<IMatchNotifier, NullMatchNotifier>();
        services.AddScoped<VersusStore>();
        services.TryAddSingleton<VersusMatchFactory>();
        services.TryAddSingleton<VersusCoordinator>();
        services.AddHostedService<VersusLifecycleService>();
        return services;
    }

    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", MatchEngineDbContext.Schema);

    public static async Task MigrateMatchEngineAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MatchEngineDbContext>().Database.MigrateAsync(ct);
    }
}

/// <summary>Olay iletici tanımlanmamışsa (örn. yalnızca admin host) olayları yutar.</summary>
internal sealed class NullMatchNotifier : IMatchNotifier
{
    public Task SendAsync(Guid userId, string eventName, object payload) => Task.CompletedTask;
}
