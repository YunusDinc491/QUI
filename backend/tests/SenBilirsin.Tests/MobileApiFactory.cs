using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SenBilirsin.MobileApi.Configuration;

namespace SenBilirsin.Tests;

/// <summary>
/// Entegrasyon testleri için MobileApi'yi gerçek bir PostgreSQL üzerinde çalıştırır.
/// Gereken: <c>docker compose up -d</c> (Postgres 5433) ve depo kökünde <c>.env</c>.
/// Test veritabanı (<c>senbilirsin_test</c>) her çalıştırmada silinip yeniden oluşturulur; geliştirme veritabanına dokunulmaz.
/// </summary>
public sealed class MobileApiFactory : WebApplicationFactory<MobileApiMarker>
{
    public const string TestDatabase = "senbilirsin_test";

    /// <summary>Testlerde zamanı elle ilerletmek için (soru süresi, terk, devam penceresi).</summary>
    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    public MobileApiFactory()
    {
        var connectionString = BuildTestConnectionString();
        ResetDatabase(connectionString);
        // Ortam değişkeni Program.cs çalışmadan önce okunur; en güvenilir yol budur
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: DevAuth (sahte kimlik) ve otomatik migration açık (appsettings.Development.json)
        builder.UseEnvironment("Development");
        // Reklamla devam testleri için Faz 3 test kapısı (üretimde kapalıdır)
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Ads:TestMode"] = "true" }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            // Gerçek zamanlı SignalR testleri hızlı olsun: kısa geri sayım/kuyruk/gösterim süreleri (kurallar aynı)
            services.AddSingleton(new SenBilirsin.Modules.MatchEngine.Domain.VersusSettings { CountdownSeconds = 1, QueueTimeoutSeconds = 2, RevealDelayMs = 200 });
        });
    }

    private static string BuildTestConnectionString()
    {
        var envPath = DevEnvFile.FindUpwards(".env")
                      ?? throw new InvalidOperationException("Depo kökünde .env bulunamadı (.env.example'dan oluştur).");
        var line = File.ReadAllLines(envPath).FirstOrDefault(l => l.StartsWith("ConnectionStrings__Default=", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException(".env içinde ConnectionStrings__Default yok.");
        var builder = new NpgsqlConnectionStringBuilder(line["ConnectionStrings__Default=".Length..]) { Database = TestDatabase };
        return builder.ConnectionString;
    }

    private static void ResetDatabase(string testConnectionString)
    {
        var admin = new NpgsqlConnectionStringBuilder(testConnectionString) { Database = "postgres", Pooling = false };
        using var connection = new NpgsqlConnection(admin.ConnectionString);
        connection.Open();

        using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {TestDatabase} WITH (FORCE)", connection))
            drop.ExecuteNonQuery();
        using (var create = new NpgsqlCommand($"CREATE DATABASE {TestDatabase}", connection))
            create.ExecuteNonQuery();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<MobileApiFactory>;
