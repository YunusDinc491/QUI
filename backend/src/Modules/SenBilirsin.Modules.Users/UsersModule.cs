using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SenBilirsin.Modules.Users.Application;
using SenBilirsin.Modules.Users.Data;
using SenBilirsin.Modules.Users.Domain;

namespace SenBilirsin.Modules.Users;

/// <summary>Users modülünün kayıt noktası. Diğer modüller bu sınıfın dışındakilere doğrudan erişmez.</summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        // Açık fabrika: ProfanityFilter'ın iki kurucusu var; DI "en çok parametreli" olanı seçip boş listelerle kurardı
        services.AddSingleton(_ => new ProfanityFilter());

        // Bağlantı dizesi, uygulama tamamen kurulduktan sonra (istek anında) okunur; böylece testler ayarı değiştirebilir
        services.AddDbContext<UsersDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<UserService>();
        return services;
    }

    /// <summary>Migration geçmiş tablosu da modülün şemasında durur.</summary>
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", UsersDbContext.Schema);

    /// <summary>Bekleyen migration'ları uygular (yalnızca geliştirmede otomatik; üretimde elle çalıştırılır).</summary>
    public static async Task MigrateUsersAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
