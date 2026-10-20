using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SenBilirsin.Modules.Administration.Application;

namespace SenBilirsin.Modules.Administration.Api;

public record LoginRequest(string? Email, string? Password, string? Code);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public static class AdminAuthEndpoints
{
    public const string PolicyName = "AdminOnly";

    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest request, AdminAuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request.Email, request.Password, request.Code,
                http.Connection.RemoteIpAddress?.ToString(), ct);
            return result.Success
                ? Results.Ok(new { token = result.Token, expiresAt = result.ExpiresAt })
                : Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
        }).AllowAnonymous();

        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = AdminIdOf(user),
            email = user.FindFirstValue("email"),
            role = user.FindFirstValue(ClaimTypes.Role),
        })).RequireAuthorization(PolicyName);

        group.MapPost("/change-password", async (ChangePasswordRequest request, ClaimsPrincipal user, AdminAuthService auth, CancellationToken ct) =>
        {
            var id = AdminIdOf(user);
            if (id is null) return Results.Unauthorized();
            var result = await auth.ChangePasswordAsync(id.Value, request.CurrentPassword, request.NewPassword, ct);
            return result.Success ? Results.NoContent() : Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
        }).RequireAuthorization(PolicyName);

        return app;
    }

    /// <summary>Oturumdaki yöneticinin kimliği (JWT "sub").</summary>
    public static Guid? AdminIdOf(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
