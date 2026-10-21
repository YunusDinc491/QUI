using System.Text.Json.Serialization;
using SenBilirsin.AdminApi;
using SenBilirsin.AdminApi.Configuration;
using SenBilirsin.AdminApi.Endpoints;
using SenBilirsin.Modules.Administration;
using SenBilirsin.Modules.Administration.Api;
using SenBilirsin.Modules.Catalog;
using SenBilirsin.Modules.Questions;
using SenBilirsin.Modules.Store;
using SenBilirsin.Modules.Tournament;
using SenBilirsin.Modules.Users;
using SenBilirsin.Modules.Wallet;

// Geliştirmede depo kökündeki .env dosyasını ortam değişkeni olarak yükle (üretimde yüklenmez)
DevEnvFile.LoadIfDevelopment();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddAdministrationModule();
builder.Services.AddCatalogModule();
builder.Services.AddQuestionsModule();
builder.Services.AddUsersModule();      // oyuncu adları (yalnızca okuma)
builder.Services.AddWalletModule();
builder.Services.AddStoreModule();       // kupon kodları şifreli; anahtar Coupons__EncryptionKey (mobil API ile aynı olmalı)
builder.Services.AddTournamentModule(); // iş (dağıtım) mobil API'de çalışır; burada yalnızca yönetim

// Zorluk/durum gibi sıralamalar JSON'da okunur metin olarak ("Easy", "Published") gider ve gelir
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Admin web (React) farklı bir adresten çağırdığı için CORS: izinli adresler ayardan okunur.
// Geliştirmede varsayılan Vite adresi; üretimde Cloudflare Pages adresi Cors__AllowedOrigins ile verilir.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Yerel komut: ilk yöneticiyi oluştur (sunucu başlatılmaz)
if (args.Length > 0 && args[0] == FirstAdminCommand.Name)
    return await FirstAdminCommand.RunAsync(app, args);
// Yerel komut: geliştirme için örnek sorular
if (args.Length > 0 && args[0] == SampleSeeder.Name)
    return await SampleSeeder.RunCommandAsync(app);

// Geliştirmede (veya Database:AutoMigrate=true ise) bekleyen migration'ları uygula. Üretimde elle çalıştırılır.
if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
    await app.Services.MigrateAllAsync();

app.UseCors();
app.UseAdministrationUploads();
app.UseAuthentication();
app.UseAuthorization();

// Railway / uptime izleme için sağlık ucu
app.MapHealthChecks("/health");
app.MapGet("/", () => "QU¿ AdminApi");
app.MapAdminAuthEndpoints();
app.MapAuditEndpoints();
app.MapUploadEndpoints();
app.MapCatalogAdminEndpoints();
app.MapQuestionAdminEndpoints();
app.MapTournamentAdminEndpoints();
app.MapStoreAdminEndpoints();

app.Run();
return 0;

/// <summary>AdminApi'nin kullandığı tüm modüllerin migration'larını uygular.</summary>
static class MigrationExtensions
{
    public static async Task MigrateAllAsync(this IServiceProvider services)
    {
        await services.MigrateAdministrationAsync();
        await services.MigrateCatalogAsync();
        await services.MigrateQuestionsAsync();
        await services.MigrateUsersAsync();
        await services.MigrateWalletAsync();
        await services.MigrateTournamentAsync();
        await services.MigrateStoreAsync();
    }
}

/// <summary>Entegrasyon testlerinde (WebApplicationFactory) bu derlemeyi göstermek için işaretçi tip.</summary>
public sealed class AdminApiMarker;
