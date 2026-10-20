using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SenBilirsin.Modules.Administration.Domain;

namespace SenBilirsin.Modules.Administration.Application;

/// <summary>Yönetici oturum belirteci (JWT). Anahtar <c>Admin:JwtKey</c> (Base64, en az 32 bayt) ortam değişkeninden gelir.</summary>
public class AdminTokenService(IConfiguration config, TimeProvider clock)
{
    public const string Issuer = "qui-admin";
    public const string Audience = "qui-admin-panel";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public static SymmetricSecurityKey KeyFrom(IConfiguration config)
    {
        var base64 = config["Admin:JwtKey"];
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException("Admin:JwtKey tanımlı değil (Base64, en az 32 bayt). .env.example'a bakın.");
        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length < 32) throw new InvalidOperationException("Admin:JwtKey en az 32 bayt olmalı.");
        return new SymmetricSecurityKey(bytes);
    }

    public (string Token, DateTime ExpiresAt) Issue(AdminUser admin)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now + Lifetime;
        var token = new JwtSecurityToken(
            Issuer, Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, admin.Email),
                new Claim(ClaimTypes.Role, admin.Role),
            ],
            notBefore: now, expires: expires,
            signingCredentials: new SigningCredentials(KeyFrom(config), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
