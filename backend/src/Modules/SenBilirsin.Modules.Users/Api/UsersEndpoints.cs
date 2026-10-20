using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SenBilirsin.Modules.Users.Application;
using SenBilirsin.Modules.Users.Domain;

namespace SenBilirsin.Modules.Users.Api;

public sealed record ApiError(string Code, string Message);

public sealed record ProfileDto(Guid Id, string Username, string DisplayName, int AvatarId, int? FrameId, long TotalScore, string Status);

public sealed record AvatarDto(int Id, string Name, string? ImageUrl, string ColorHex, int Price);

public sealed record StartSessionRequest(string? DeviceId, string? Platform);

/// <summary>Faz 1 kullanıcı uçları (docs/04): kayıt, profil, oturum/cihaz, hesap silme.</summary>
public static class UsersEndpoints
{
    private const string UserItemKey = "users.current";

    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/username-available", CheckUsername);
        group.MapGet("/avatars", GetAvatars);
        group.MapPost("/me", Register);
        group.MapPost("/me/session", StartSession);

        // Aşağıdakiler yalnızca aktif cihazdan çağrılabilir (tek cihaz kuralı)
        var guarded = group.MapGroup("/me").AddEndpointFilter<ActiveDeviceFilter>();
        guarded.MapGet("", GetMe);
        guarded.MapPatch("", UpdateMe);
        guarded.MapDelete("", DeleteMe);

        return app;
    }

    private static async Task<IResult> CheckUsername(string? username, UserService users, CancellationToken ct)
    {
        var result = await users.CheckUsernameAvailabilityAsync(username, ct);
        return Results.Ok(new { available = result.IsValid, code = result.Code, message = result.Message });
    }

    private static async Task<IResult> GetAvatars(UserService users, CancellationToken ct)
    {
        var avatars = await users.GetAvatarsAsync(ct);
        return Results.Ok(avatars.Select(a => new AvatarDto(a.Id, a.Name, a.ImageUrl, a.ColorHex, a.Price)));
    }

    private static async Task<IResult> Register(HttpContext http, RegisterRequest request, UserService users, CancellationToken ct)
    {
        var identity = CurrentIdentity.From(http.User);
        if (identity is null) return Results.Unauthorized();

        // E-posta/parola ile kayıtta e-posta doğrulaması zorunludur (docs/08)
        if (identity.Provider == "password" && !identity.EmailVerified)
            return Results.Json(new ApiError("EMAIL_NOT_VERIFIED", "Devam etmek için e-posta adresini doğrulamalısın."), statusCode: 403);

        var result = await users.RegisterAsync(identity.Uid, request, ct);
        return result.IsSuccess
            ? Results.Created("/api/users/me", ToDto(result.Value!))
            : ToResult(result.Error!);
    }

    private static async Task<IResult> StartSession(HttpContext http, StartSessionRequest request, UserService users, CancellationToken ct)
    {
        var identity = CurrentIdentity.From(http.User);
        if (identity is null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 128)
            return Results.BadRequest(new ApiError("DEVICE_ID_INVALID", "Cihaz kimliği geçersiz."));

        var user = await users.FindByFirebaseUidAsync(identity.Uid, ct);
        if (user is null || user.Status == UserStatus.Deleted)
            return Results.NotFound(new ApiError("NOT_REGISTERED", "Önce kayıt tamamlanmalı."));

        await users.SetActiveDeviceAsync(user, request.DeviceId, ct);
        return Results.Ok(ToDto(user));
    }

    private static IResult GetMe(HttpContext http) => Results.Ok(ToDto(CurrentUser(http)));

    private static async Task<IResult> UpdateMe(HttpContext http, UpdateProfileRequest request, UserService users, CancellationToken ct)
    {
        var result = await users.UpdateProfileAsync(CurrentUser(http), request, ct);
        return result.IsSuccess ? Results.Ok(ToDto(result.Value!)) : ToResult(result.Error!);
    }

    private static async Task<IResult> DeleteMe(HttpContext http, UserService users, CancellationToken ct)
    {
        await users.DeleteAccountAsync(CurrentUser(http), ct);
        return Results.NoContent();
    }

    internal static UserAccount CurrentUser(HttpContext http) => (UserAccount)http.Items[UserItemKey]!;

    internal static void SetCurrentUser(HttpContext http, UserAccount user) => http.Items[UserItemKey] = user;

    private static ProfileDto ToDto(UserAccount u) =>
        new(u.Id, u.Username, u.DisplayName, u.AvatarId, u.FrameId, u.TotalScore, u.Status.ToString());

    private static IResult ToResult(ServiceError error) =>
        Results.Json(new ApiError(error.Code, error.Message), statusCode: error.StatusCode);
}

/// <summary>
/// Tek cihaz kuralı (K-38): istek, hesabın aktif cihazından (X-Device-Id) gelmiyorsa 401 SESSION_REPLACED döner.
/// Başka cihazdan giriş yapıldığında önceki cihaz bir sonraki istekte oturumunun düştüğünü böyle öğrenir.
/// </summary>
public sealed class ActiveDeviceFilter(UserService users) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var identity = CurrentIdentity.From(http.User);
        if (identity is null) return Results.Unauthorized();

        var user = await users.FindByFirebaseUidAsync(identity.Uid, http.RequestAborted);
        if (user is null || user.Status == UserStatus.Deleted)
            return Results.NotFound(new ApiError("NOT_REGISTERED", "Önce kayıt tamamlanmalı."));

        var deviceId = http.Request.Headers["X-Device-Id"].ToString();
        if (string.IsNullOrEmpty(deviceId) || user.ActiveDeviceId != deviceId)
            return Results.Json(new ApiError("SESSION_REPLACED", "Hesabına başka bir cihazdan giriş yapıldı."), statusCode: StatusCodes.Status401Unauthorized);

        UsersEndpoints.SetCurrentUser(http, user);
        return await next(context);
    }
}
