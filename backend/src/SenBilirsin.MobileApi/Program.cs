using SenBilirsin.MobileApi.Auth;
using SenBilirsin.MobileApi.Configuration;
using SenBilirsin.MobileApi.Hubs;
using SenBilirsin.Modules.Users;
using SenBilirsin.Modules.Users.Api;

// Geliştirmede depo kökündeki .env dosyasını ortam değişkeni olarak yükle (üretimde yüklenmez)
DevEnvFile.LoadIfDevelopment();

var builder = WebApplication.CreateBuilder(args);

using var startupLoggerFactory = LoggerFactory.Create(l => l.AddConsole());
var startupLogger = startupLoggerFactory.CreateLogger("Startup");

builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
builder.Services.AddSenBilirsinAuthentication(builder.Configuration, builder.Environment, startupLogger);
builder.Services.AddUsersModule();

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
    await app.Services.MigrateUsersAsync();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Railway / uptime izleme için sağlık ucu
app.MapHealthChecks("/health");
app.MapGet("/", () => "SenBilirsin MobileApi");

// Uygulama açılışında: zorunlu güncelleme (min sürüm) ve bakım modu (docs/02). Şimdilik ayar dosyasından;
// admin panelinden yönetilecek hale Faz 8'de gelir.
app.MapGet("/api/config", (IConfiguration config) => Results.Ok(new
{
    minSupportedVersion = config["App:MinSupportedVersion"] ?? "0.0.0",
    maintenanceMode = config.GetValue<bool>("App:MaintenanceMode"),
    maintenanceMessage = config["App:MaintenanceMessage"] ?? "",
}));

app.MapUsersEndpoints();

// Faz 0: Flutter <-> SignalR prototipi için echo hub'ı (oyun mantığı Faz 3-4'te gelecek)
app.MapHub<GameHub>("/hubs/game");

app.Run();

/// <summary>Entegrasyon testlerinde (WebApplicationFactory) bu derlemeyi göstermek için işaretçi tip.</summary>
public sealed class MobileApiMarker;
