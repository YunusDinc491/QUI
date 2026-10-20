using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SenBilirsin.Modules.Wallet.Data;

/// <summary>Yalnızca tasarım zamanı (dotnet ef migrations ...); gerçek veritabanına bağlanmaz.</summary>
public class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only", WalletModule.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new WalletDbContext(options);
    }
}
