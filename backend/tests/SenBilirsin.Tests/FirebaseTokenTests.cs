using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SenBilirsin.Tests;

/// <summary>
/// Firebase kimlik jetonu doğrulamasının güvenlik testleri. Gerçek Firebase olmadan, kendi anahtarımızla imzaladığımız
/// "Firebase biçimli" jetonlar kullanılır (aynı talep adları: user_id, email, email_verified, firebase.sign_in_provider).
/// </summary>
[Collection("api")]
public class FirebaseTokenTests(MobileApiFactory baseFactory)
{
    private const string ProjectId = "test-project";
    private static readonly string Issuer = $"https://securetoken.google.com/{ProjectId}";
    private static readonly SymmetricSecurityKey TrustedKey = NewKey("trusted-key");
    private static readonly SymmetricSecurityKey OtherKey = NewKey("other-key");

    private static SymmetricSecurityKey NewKey(string kid) =>
        new(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) { KeyId = kid };

    /// <summary>Firebase:ProjectId ayarlı ve yalnızca TrustedKey'i güvenen bir uygulama örneği (DevAuth jetonları kullanılmaz).</summary>
    private HttpClient FirebaseClient(string? token)
    {
        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Firebase:ProjectId", ProjectId);
            builder.ConfigureServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    // Google'ın anahtarlarını indirmek yerine test anahtarımıza güven
                    var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    configuration.SigningKeys.Add(TrustedKey);
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                }));
        });
        var client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        return client;
    }

    private static string MakeToken(
        string uid,
        string provider = "google.com",
        bool emailVerified = true,
        string issuer = null!,
        string audience = ProjectId,
        SecurityKey? key = null,
        TimeSpan? lifetime = null,
        bool unsigned = false)
    {
        var now = DateTime.UtcNow;
        var life = lifetime ?? TimeSpan.FromHours(1);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience,
            IssuedAt = now.AddMinutes(-5),
            NotBefore = now.AddMinutes(-5),
            Expires = now.Add(life),
            Claims = new Dictionary<string, object>
            {
                ["user_id"] = uid,
                ["sub"] = uid,
                ["email"] = $"{uid}@test.local",
                ["email_verified"] = emailVerified,
                ["firebase"] = new Dictionary<string, object> { ["sign_in_provider"] = provider },
            },
        };

        if (!unsigned)
            descriptor.SigningCredentials = new SigningCredentials(key ?? TrustedKey, SecurityAlgorithms.HmacSha256);

        var handler = new JsonWebTokenHandler();
        if (unsigned)
        {
            // alg=none: imzasız jeton
            var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
            var payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["iss"] = Issuer, ["aud"] = ProjectId, ["user_id"] = uid, ["sub"] = uid,
                ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            }));
            return $"{header}.{payload}.";
        }

        return handler.CreateToken(descriptor);
    }

    private static object Registration(string username) =>
        new { username, displayName = "Firebase Kullanıcı", avatarId = 1, birthYear = 2000, termsAccepted = true };

    private static string NewUsername() => "f" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task ValidGoogleToken_RegistersAndReadsProfile()
    {
        var uid = Guid.NewGuid().ToString("N");
        var client = FirebaseClient(MakeToken(uid, provider: "google.com"));
        client.DefaultRequestHeaders.Add("X-Device-Id", "dev-1");

        var register = await client.PostAsJsonAsync("/api/users/me", Registration(NewUsername()));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var session = await client.PostAsJsonAsync("/api/users/me/session", new { deviceId = "dev-1", platform = "test" });
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        var me = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task PasswordProvider_WithUnverifiedEmail_IsBlockedFromRegistering()
    {
        var client = FirebaseClient(MakeToken(Guid.NewGuid().ToString("N"), provider: "password", emailVerified: false));

        var response = await client.PostAsJsonAsync("/api/users/me", Registration(NewUsername()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EMAIL_NOT_VERIFIED", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PasswordProvider_WithVerifiedEmail_CanRegister()
    {
        var client = FirebaseClient(MakeToken(Guid.NewGuid().ToString("N"), provider: "password", emailVerified: true));
        var response = await client.PostAsJsonAsync("/api/users/me", Registration(NewUsername()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public static IEnumerable<object[]> RejectedTokens() =>
    [
        ["yanlış hedef kitle (audience)", MakeToken("u1", audience: "baska-proje")],
        ["yanlış yayıncı (issuer)", MakeToken("u2", issuer: "https://securetoken.google.com/baska-proje")],
        ["süresi dolmuş", MakeToken("u3", lifetime: TimeSpan.FromMinutes(-30))],
        ["başka anahtarla imzalı", MakeToken("u4", key: OtherKey)],
        ["imzasız (alg=none)", MakeToken("u5", unsigned: true)],
        ["rastgele metin", "abc.def.ghi"],
    ];

    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task InvalidTokens_AreRejectedWith401(string description, string token)
    {
        var client = FirebaseClient(token);
        var response = await client.GetAsync("/api/users/me");
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"Beklenen 401, gelen {(int)response.StatusCode}: {description}");
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await FirebaseClient(null).GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DevHeader_IsNotAcceptedAsFirebaseToken_WhenSentAsBearer()
    {
        // "Dev ..." şeması yalnızca "Authorization: Dev ..." olarak çalışır; Bearer içinde sahte jeton olarak geçmez
        var client = FirebaseClient("Dev uid=hacker;verified=true");
        var response = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
