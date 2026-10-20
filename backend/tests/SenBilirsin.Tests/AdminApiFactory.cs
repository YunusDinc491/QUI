using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SenBilirsin.Modules.Administration.Application;
using SenBilirsin.Modules.Administration.Security;
using SenBilirsin.MobileApi.Configuration;

namespace SenBilirsin.Tests;

/// <summary>Testlerde zamanı elle ilerletmek için saat (kilitlenme süresi, TOTP adımı).</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// AdminApi'yi gerçek PostgreSQL üzerinde çalıştırır (ayrı veritabanı: <c>senbilirsin_admin_test</c>; her çalıştırmada sıfırlanır).
/// Ayarlar ortam değişkeni yerine bellek içi yapılandırmayla verilir (MobileApiFactory ile çakışmasın).
/// </summary>
public sealed class AdminApiFactory : WebApplicationFactory<AdminApiMarker>
{
    public const string TestDatabase = "senbilirsin_admin_test";

    /// <summary>Yükleme testleri için geçici klasör (depoya yazılmaz).</summary>
    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), "qui-test-uploads-" + Guid.NewGuid().ToString("N"));

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    private readonly string _connectionString;

    public AdminApiFactory()
    {
        _connectionString = BuildTestConnectionString();
        ResetDatabase(_connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connectionString,
            ["Database:AutoMigrate"] = "true",
            ["Storage:LocalPath"] = UploadsPath,
            ["Storage:PublicBaseUrl"] = "http://test.local/uploads",
            // Yalnızca testlere özel, rastgele üretilmiş anahtarlar
            ["Admin:SecretKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            ["Admin:JwtKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
        }));
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    /// <summary>Yeni bir yönetici oluşturup giriş yapmış (Bearer belirteçli) istemci döndürür.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        const string password = "Test-Parola-12345";
        CreatedAdmin admin;
        using (var scope = Services.CreateScope())
            admin = await scope.ServiceProvider.GetRequiredService<AdminAuthService>()
                .CreateAdminAsync($"api-{Guid.NewGuid():N}@example.com", password);

        var client = CreateClient();
        var code = TotpService.ComputeCode(Base32.Decode(admin.TotpSecret), TotpService.StepFor(Clock.GetUtcNow()));
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = admin.Email, password, code });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        return client;
    }

    private static string BuildTestConnectionString()
    {
        var envPath = DevEnvFile.FindUpwards(".env")
                      ?? throw new InvalidOperationException("Depo kökünde .env bulunamadı (.env.example'dan oluştur).");
        var line = File.ReadAllLines(envPath).FirstOrDefault(l => l.StartsWith("ConnectionStrings__Default=", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException(".env içinde ConnectionStrings__Default yok.");
        return new NpgsqlConnectionStringBuilder(line["ConnectionStrings__Default=".Length..]) { Database = TestDatabase }.ConnectionString;
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

[CollectionDefinition("admin-api")]
public class AdminApiCollection : ICollectionFixture<AdminApiFactory>;
