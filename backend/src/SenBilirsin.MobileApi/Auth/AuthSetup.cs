using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SenBilirsin.MobileApi.Auth;

/// <summary>
/// Kimlik doğrulama: Firebase kimlik jetonları (JWT) her istekte doğrulanır (docs/02, 08).
/// Yalnızca geliştirmede ve "Auth:DevBypass=true" ile, Firebase hesabı olmadan elle test için
/// "Authorization: Dev uid=...;email=...;verified=true;provider=password" başlığı da kabul edilir.
/// </summary>
public static class AuthSetup
{
    private const string SmartScheme = "Smart";
    private const string DevScheme = "DevAuth";

    public static IServiceCollection AddSenBilirsinAuthentication(
        this IServiceCollection services, IConfiguration config, IWebHostEnvironment env, ILogger logger)
    {
        var projectId = config["Firebase:ProjectId"];
        var devBypass = env.IsDevelopment() && config.GetValue<bool>("Auth:DevBypass");

        if (string.IsNullOrWhiteSpace(projectId))
            logger.LogWarning("Firebase:ProjectId tanımlı değil; gerçek Firebase jetonları doğrulanamaz (yalnızca DevBypass çalışır).");
        if (devBypass)
            logger.LogWarning("Auth:DevBypass AÇIK (yalnızca geliştirme). Üretimde asla açılmamalı.");

        var auth = services.AddAuthentication(SmartScheme);

        // Hangi şemayla doğrulanacağını isteğe bakarak seç
        auth.AddPolicyScheme(SmartScheme, SmartScheme, options =>
        {
            options.ForwardDefaultSelector = context =>
                devBypass && DevAuthHandler.DevCredential(context.Request) is not null
                    ? DevScheme
                    : JwtBearerDefaults.AuthenticationScheme;
        });

        auth.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

        // Ayarlar (Firebase:ProjectId) seçenekler ilk kullanıldığında okunur; böylece testler proje kimliğini değiştirebilir
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, configuration) =>
            {
                var id = configuration["Firebase:ProjectId"];
                options.MapInboundClaims = false; // "sub", "user_id", "email_verified" adlarını olduğu gibi koru
                var authority = $"https://securetoken.google.com/{(string.IsNullOrWhiteSpace(id) ? "not-configured" : id)}";
                options.Authority = authority;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = id,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                };

                // SignalR (WebSocket) jetonu sorgu dizesinde gönderir
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });

        if (devBypass)
            auth.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevScheme, null);

        services.AddAuthorization();
        return services;
    }
}

/// <summary>Geliştirme için sahte kimlik: Firebase jetonuyla aynı talep adlarını üretir (CurrentIdentity ile uyumlu).</summary>
public sealed class DevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = DevCredential(Request);
        if (header is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = header["Dev ".Length..]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        if (!parts.TryGetValue("uid", out var uid) || string.IsNullOrWhiteSpace(uid))
            return Task.FromResult(AuthenticateResult.Fail("Dev kimliğinde uid yok."));

        var claims = new List<Claim> { new("user_id", uid), new("sub", uid) };
        if (parts.TryGetValue("email", out var email)) claims.Add(new Claim("email", email));
        claims.Add(new Claim("email_verified", parts.TryGetValue("verified", out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false"));
        if (parts.TryGetValue("provider", out var provider)) claims.Add(new Claim("sign_in_provider", provider));

        var identity = new ClaimsIdentity(claims, DevSchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), DevSchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private const string DevSchemeName = "DevAuth";

    /// <summary>
    /// Geliştirme kimliği: <c>Authorization: Dev uid=...</c> başlığı; SignalR istemcileri ise jetonu
    /// <c>Bearer Dev …</c> ya da /hubs altında <c>?access_token=Dev …</c> olarak gönderir. Yoksa null.
    /// </summary>
    public static string? DevCredential(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Dev ", StringComparison.Ordinal)) return header;

        // "Bearer Dev …" ve ?access_token=Dev … yalnızca SignalR bağlantısında (/hubs) kabul edilir; normal uçlarda
        // Dev kimliği jeton gibi gönderilirse reddedilmeye devam eder (FirebaseTokenTests).
        if (!request.Path.StartsWithSegments("/hubs")) return null;
        if (header.StartsWith("Bearer Dev ", StringComparison.Ordinal)) return header["Bearer ".Length..];
        var token = request.Query["access_token"].ToString();
        return token.StartsWith("Dev ", StringComparison.Ordinal) ? token : null;
    }
}
