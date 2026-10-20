using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using SenBilirsin.Modules.Administration.Api;
using SenBilirsin.Modules.Administration.Application;
using SenBilirsin.Modules.Administration.Data;
using SenBilirsin.Modules.Administration.Domain;
using SenBilirsin.Modules.Administration.Security;
using SenBilirsin.Modules.Administration.Storage;

namespace SenBilirsin.Modules.Administration;

/// <summary>Yönetim modülünün kayıt noktası: yönetici kimliği (parola + TOTP), JWT oturumu, denetim kaydı.</summary>
public static class AdministrationModule
{
    public static IServiceCollection AddAdministrationModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<AdministrationDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());

        // Anahtarlar ilk kullanıldığı anda ayardan okunur (testlerde ayar sonradan eklenebilsin)
        services.AddSingleton(sp => new SecretProtector(sp.GetRequiredService<IConfiguration>()["Admin:SecretKey"]));
        services.AddScoped<AdminTokenService>();
        services.AddScoped<AuditLogger>();
        services.AddScoped<AdminAuthService>();

        // Dosya depolama: Storage:Provider = Local (varsayılan, geliştirme) | R2 (üretim)
        services.AddSingleton<IFileStorage>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            if (string.Equals(config["Storage:Provider"], "R2", StringComparison.OrdinalIgnoreCase))
                return new R2FileStorage(config);
            var root = config["Storage:LocalPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), ".local", "uploads");
            return new LocalFileStorage(root, config["Storage:PublicBaseUrl"] ?? "http://localhost:5090/uploads");
        });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, config) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = AdminTokenService.Issuer,
                    ValidAudience = AdminTokenService.Audience,
                    IssuerSigningKey = AdminTokenService.KeyFrom(config),
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "sub",
                    RoleClaimType = ClaimTypes.Role,
                };
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminAuthEndpoints.PolicyName, p => p.RequireAuthenticatedUser().RequireRole(AdminRoles.Admin));

        return services;
    }

    /// <summary>Yerel depolama kullanılıyorsa yüklenen dosyaları <c>/uploads</c> altında sunar (geliştirme).</summary>
    public static WebApplication UseAdministrationUploads(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IFileStorage>() is LocalFileStorage local)
        {
            Directory.CreateDirectory(local.RootPath);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(local.RootPath),
                RequestPath = "/uploads",
                OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff",
            });
        }

        return app;
    }

    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", AdministrationDbContext.Schema);

    public static async Task MigrateAdministrationAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().Database.MigrateAsync(ct);
    }
}
