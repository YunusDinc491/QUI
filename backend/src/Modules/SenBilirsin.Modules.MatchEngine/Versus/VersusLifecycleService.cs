using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SenBilirsin.Modules.MatchEngine.Versus;

/// <summary>
/// Açılışta: önceki çalışmadan kalan yarım rakipli maçlar iptal edilir. Kapanışta: süren maçlar iptal edilir (puan yok, K-43).
/// </summary>
public sealed class VersusLifecycleService(IServiceScopeFactory scopes, VersusCoordinator coordinator, ILogger<VersusLifecycleService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var cancelled = await scope.ServiceProvider.GetRequiredService<VersusStore>().CancelStaleAsync(cancellationToken);
            if (cancelled > 0) log.LogWarning("{Count} yarım kalmış rakipli maç iptal edildi.", cancelled);
        }
        catch (Exception ex)
        {
            // Veritabanı henüz hazır değilse (ilk kurulum/migration öncesi) açılışı engelleme
            log.LogWarning(ex, "Yarım kalmış maçlar kontrol edilemedi.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.CancelAllAsync(MatchEndReasonsVersus.ServerRestart);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Kapanışta maçlar iptal edilemedi.");
        }
    }
}
