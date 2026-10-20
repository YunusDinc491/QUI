using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Data;

namespace SenBilirsin.Modules.Wallet;

/// <summary>Cüzdan modülünün kayıt noktası. Başka modüller yalnızca <see cref="IJokerWallet"/> arayüzünü kullanır.</summary>
public static class WalletModule
{
    public static IServiceCollection AddWalletModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<WalletDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<JokerWallet>();
        services.AddScoped<IJokerWallet>(sp => sp.GetRequiredService<JokerWallet>());
        return services;
    }

    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", WalletDbContext.Schema);

    public static async Task MigrateWalletAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WalletDbContext>().Database.MigrateAsync(ct);
    }
}
