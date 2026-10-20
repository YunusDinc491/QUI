using System.Security.Claims;
using System.Text.Json;

namespace SenBilirsin.Modules.Users.Api;

/// <summary>
/// İstekteki Firebase kimliği. Firebase jetonunda uid "user_id" (ve "sub"), e-posta durumu "email_verified",
/// giriş yöntemi ise "firebase" nesnesinin içindeki "sign_in_provider" alanındadır.
/// Geliştirme kimlik işleyicisi (DevAuth) aynı adlarla talep üretir.
/// </summary>
public sealed record CurrentIdentity(string Uid, string? Email, bool EmailVerified, string? Provider)
{
    public static CurrentIdentity? From(ClaimsPrincipal principal)
    {
        var uid = principal.FindFirstValue("user_id") ?? principal.FindFirstValue("sub")
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(uid)) return null;

        var email = principal.FindFirstValue("email");
        var verified = string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        var provider = principal.FindFirstValue("sign_in_provider") ?? ReadFirebaseProvider(principal.FindFirstValue("firebase"));
        return new CurrentIdentity(uid, email, verified, provider);
    }

    private static string? ReadFirebaseProvider(string? firebaseClaimJson)
    {
        if (string.IsNullOrEmpty(firebaseClaimJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(firebaseClaimJson);
            return doc.RootElement.TryGetProperty("sign_in_provider", out var p) ? p.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
