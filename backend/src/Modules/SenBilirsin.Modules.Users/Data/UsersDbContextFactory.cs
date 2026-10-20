using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SenBilirsin.Modules.Users.Data;

/// <summary>
/// Yalnızca tasarım zamanı (dotnet ef migrations ...) için. Gerçek veritabanına bağlanmaz;
/// migration oluşturmak için sahte bir bağlantı dizesi yeterlidir.
/// </summary>
public class UsersDbContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    public UsersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<UsersDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only", UsersModule.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new UsersDbContext(options);
    }
}
