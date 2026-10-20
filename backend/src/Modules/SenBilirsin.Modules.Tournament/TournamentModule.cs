using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SenBilirsin.Modules.MatchEngine.Application;
using SenBilirsin.Modules.Tournament.Application;
using SenBilirsin.Modules.Tournament.Data;

namespace SenBilirsin.Modules.Tournament;

/// <summary>Turnuva modülünün kayıt noktası (dönemler, sıralama, ödül dağıtımı). Cüzdan, kullanıcı ve maç motoru modülleri de kayıtlı olmalıdır.</summary>
public static class TournamentModule
{
    public static IServiceCollection AddTournamentModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<TournamentDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());
        services.TryAddSingleton<TournamentClock>();
        services.AddScoped<PeriodService>();
        services.AddScoped<ScoreService>();
        services.AddScoped<DistributionService>();
        services.AddScoped<TournamentAdminService>();

        // Maçlardan kesinleşen puan turnuvaya akar (MatchEngine'in boş varsayılanının yerine)
        services.Replace(ServiceDescriptor.Scoped<IMatchPointsSink, TournamentPointsSink>());
        return services;
    }

    /// <summary>Arka plan işleri: dönem oluşturma, kapanışta ödül dağıtımı, bekleyen toplam puan yansıtma (host başına bir kez eklenir).</summary>
    public static IServiceCollection AddTournamentJobs(this IServiceCollection services) =>
        services.AddHostedService<TournamentJobsService>();

    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", TournamentDbContext.Schema);

    public static async Task MigrateTournamentAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TournamentDbContext>().Database.MigrateAsync(ct);
    }
}

/// <summary>Her 30 sn: şimdiki dönemi hazırla, süresi dolan dönemleri dağıt, bekleyen toplam puanları yansıt. Hatalar işi durdurmaz.</summary>
public sealed class TournamentJobsService(IServiceScopeFactory scopes, IConfiguration config, ILogger<TournamentJobsService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Testlerde kapatılabilsin: Tournament:Jobs=false
        if (!config.GetValue("Tournament:Jobs", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await RunOnceAsync(scopes, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Turnuva işi başarısız (bir sonraki turda yeniden denenecek).");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task RunOnceAsync(IServiceScopeFactory scopes, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PeriodService>().GetCurrentAsync(ct);
        await scope.ServiceProvider.GetRequiredService<DistributionService>().DistributeDueAsync(ct);
        await scope.ServiceProvider.GetRequiredService<ScoreService>().ApplyPendingTotalsAsync(ct);
    }
}
