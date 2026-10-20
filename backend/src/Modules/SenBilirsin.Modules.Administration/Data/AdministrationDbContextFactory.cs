using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SenBilirsin.Modules.Administration.Data;

/// <summary>Yalnızca tasarım zamanı (dotnet ef migrations ...); gerçek veritabanına bağlanmaz.</summary>
public class AdministrationDbContextFactory : IDesignTimeDbContextFactory<AdministrationDbContext>
{
    public AdministrationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only", AdministrationModule.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AdministrationDbContext(options);
    }
}
