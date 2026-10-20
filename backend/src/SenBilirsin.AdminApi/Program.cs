var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// Admin web (React) farklı bir adresten çağırdığı için CORS: izinli adresler ayardan okunur.
// Geliştirmede varsayılan Vite adresi; üretimde Cloudflare Pages adresi Cors__AllowedOrigins ile verilir.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

// Railway / uptime izleme için sağlık ucu
app.MapHealthChecks("/health");
app.MapGet("/", () => "QU¿ AdminApi");

app.Run();

/// <summary>Entegrasyon testlerinde (WebApplicationFactory) bu derlemeyi göstermek için işaretçi tip.</summary>
public sealed class AdminApiMarker;
