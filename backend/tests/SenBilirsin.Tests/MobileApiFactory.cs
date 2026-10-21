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
public class MobileApiFactory : WebApplicationFactory<MobileApiMarker>
{
    public const string TestDatabase = "senbilirsin_test";

    private readonly string _database;
    private readonly bool _fakeClock;

    /// <summary>Testlerde zamanı elle ilerletmek için (soru süresi, terk, devam penceresi).</summary>
    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    public MobileApiFactory() : this(TestDatabase, fakeClock: true)
    {
    }

    /// <summary>
    /// <paramref name="database"/>: bu fabrikanın kendi veritabanı (paralel çalışan koleksiyonlar birbirinin veritabanını silmesin).
    /// <paramref name="fakeClock"/> false ise gerçek saat kullanılır (SignalR zaman aşımları sahte saat sıçramalarından etkilenmesin).
    /// </summary>
    protected MobileApiFactory(string database, bool fakeClock)
    {
        _database = database;
        _fakeClock = fakeClock;
        ConnectionString = BuildTestConnectionString(database);
        ResetDatabase(ConnectionString, database);
    }

    private string ConnectionString { get; }

    /// <summary>Test sunucusunda rastgele eşleşme kuyruğu zaman aşımı (kural 15 sn; testte kısa).</summary>
    protected virtual int QueueTimeoutSeconds => 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: DevAuth (sahte kimlik) ve otomatik migration açık (appsettings.Development.json)
        builder.UseEnvironment("Development");
        // Reklamla devam testleri için Faz 3 test kapısı (üretimde kapalıdır)
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ads:TestMode"] = "true",
            ["Tournament:Jobs"] = "false",
            ["Store:Jobs"] = "false",
            ["Coupons:EncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            ["ConnectionStrings:Default"] = ConnectionString, // bellek içi ayar ortam değişkenini ezer: her fabrika kendi veritabanını kullanır
        }));
        builder.ConfigureServices(services =>
        {
            if (_fakeClock) services.AddSingleton<TimeProvider>(Clock);
            // Gerçek zamanlı SignalR testleri hızlı olsun: kısa geri sayım/kuyruk/gösterim süreleri (kurallar aynı)
            services.AddSingleton(new SenBilirsin.Modules.MatchEngine.Domain.VersusSettings { CountdownSeconds = 1, QueueTimeoutSeconds = QueueTimeoutSeconds, RevealDelayMs = 200 });
        });
    }

    private static string BuildTestConnectionString(string database)
    {
        var envPath = DevEnvFile.FindUpwards(".env")
                      ?? throw new InvalidOperationException("Depo kökünde .env bulunamadı (.env.example'dan oluştur).");
        var line = File.ReadAllLines(envPath).FirstOrDefault(l => l.StartsWith("ConnectionStrings__Default=", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException(".env içinde ConnectionStrings__Default yok.");
        var builder = new NpgsqlConnectionStringBuilder(line["ConnectionStrings__Default=".Length..]) { Database = database };
        return builder.ConnectionString;
    }

    private static void ResetDatabase(string testConnectionString, string database)
    {
        var admin = new NpgsqlConnectionStringBuilder(testConnectionString) { Database = "postgres", Pooling = false };
        using var connection = new NpgsqlConnection(admin.ConnectionString);
        connection.Open();

        using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", connection))
            drop.ExecuteNonQuery();
        using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", connection))
            create.ExecuteNonQuery();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<MobileApiFactory>;

/// <summary>
/// SignalR testleri için: ayrı veritabanı ve <b>gerçek saat</b>. SignalR sunucusu bağlantı zaman aşımlarını TimeProvider ile ölçer;
/// diğer testlerin sahte saati ileri sarması canlı bağlantıları "zaman aşımına uğramış" gösterip testi dalgalı yapıyordu.
/// </summary>
public sealed class HubApiFactory() : MobileApiFactory("senbilirsin_hub_test", fakeClock: false)
{
    // Paralel koleksiyonlar CPU'yu paylaşırken iki istemcinin kuyruğa sırayla girmesi 2 sn'den uzun sürebilir
    protected override int QueueTimeoutSeconds => 6;
}

[CollectionDefinition("hub")]
public class HubCollection : ICollectionFixture<HubApiFactory>;
