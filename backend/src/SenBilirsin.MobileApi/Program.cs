using SenBilirsin.MobileApi.Auth;
using SenBilirsin.MobileApi.Configuration;
using SenBilirsin.MobileApi.Hubs;
using SenBilirsin.Modules.Catalog;
using SenBilirsin.Modules.Catalog.Api;
using SenBilirsin.Modules.MatchEngine;
using SenBilirsin.Modules.MatchEngine.Api;
using SenBilirsin.Modules.MatchEngine.Versus;
using SenBilirsin.Modules.Questions;
using SenBilirsin.Modules.Store;
using SenBilirsin.Modules.Store.Api;
using SenBilirsin.Modules.Tournament;
using SenBilirsin.Modules.Tournament.Api;
using SenBilirsin.Modules.Users;
using SenBilirsin.Modules.Users.Api;
using SenBilirsin.Modules.Wallet;
using SenBilirsin.Modules.Wallet.Api;

// Geliştirmede depo kökündeki .env dosyasını ortam değişkeni olarak yükle (üretimde yüklenmez)
DevEnvFile.LoadIfDevelopment();

var builder = WebApplication.CreateBuilder(args);

using var startupLoggerFactory = LoggerFactory.Create(l => l.AddConsole());
var startupLogger = startupLoggerFactory.CreateLogger("Startup");

// Bağlantı kopmasını hızlı fark etmek için kısa keepalive (docs/04 §1)
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(7);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(20);
});
builder.Services.AddHealthChecks();
builder.Services.AddSenBilirsinAuthentication(builder.Configuration, builder.Environment, startupLogger);
builder.Services.AddUsersModule();
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<IMatchNotifier, SignalRMatchNotifier>(); // MatchEngine'in yutan varsayılanından önce kaydedilir
builder.Services.AddCatalogModule();
builder.Services.AddQuestionsModule();
builder.Services.AddWalletModule();
builder.Services.AddMatchEngineModule();
builder.Services.AddTournamentModule();
builder.Services.AddStoreModule();
builder.Services.AddStoreJobs(); // yarım kalan kupon satın almalarını toparlar
builder.Services.AddTournamentJobs(); // dönem açma + kapanışta ödül dağıtımı (tek instance)

// Flutter web önizlemesi (geliştirme) farklı adresten çağırdığı için CORS: izinli adresler ayardan okunur.
// Mobil (Android) uygulama CORS'a tabi değildir. Üretimde Cors__AllowedOrigins ile verilir.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5174"];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

// Geliştirmede (veya Database:AutoMigrate=true ise) bekleyen migration'ları uygula. Üretimde elle çalıştırılır.
if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await app.Services.MigrateUsersAsync();
    await app.Services.MigrateCatalogAsync();
    await app.Services.MigrateQuestionsAsync();
    await app.Services.MigrateWalletAsync();
    await app.Services.MigrateMatchEngineAsync();
    await app.Services.MigrateTournamentAsync();
    await app.Services.MigrateStoreAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Railway / uptime izleme için sağlık ucu
app.MapHealthChecks("/health");
app.MapGet("/", () => "QU¿ MobileApi");

// Uygulama açılışında: zorunlu güncelleme (min sürüm) ve bakım modu (docs/02). Şimdilik ayar dosyasından;
// admin panelinden yönetilecek hale Faz 8'de gelir.
app.MapGet("/api/config", (IConfiguration config) => Results.Ok(new
{
    minSupportedVersion = config["App:MinSupportedVersion"] ?? "0.0.0",
    maintenanceMode = config.GetValue<bool>("App:MaintenanceMode"),
    maintenanceMessage = config["App:MaintenanceMessage"] ?? "",
}));

app.MapUsersEndpoints();
app.MapCatalogEndpoints();
app.MapMatchEndpoints();
app.MapTournamentEndpoints();
app.MapWalletEndpoints();
app.MapStoreEndpoints();

// Oyun hub'ı: rakipli maç (eşleşme, oda, soru akışı). Bireysel maç REST ile (docs/04 §8)
app.MapHub<GameHub>("/hubs/game");

app.Run();

/// <summary>Entegrasyon testlerinde (WebApplicationFactory) bu derlemeyi göstermek için işaretçi tip.</summary>
public sealed class MobileApiMarker;
