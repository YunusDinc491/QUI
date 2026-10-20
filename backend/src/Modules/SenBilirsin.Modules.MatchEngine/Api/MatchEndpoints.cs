using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SenBilirsin.Modules.MatchEngine.Application;
using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.MatchEngine.Versus;
using SenBilirsin.Modules.Users.Api;

namespace SenBilirsin.Modules.MatchEngine.Api;

public sealed record StartSoloRequest(string? Mode, int? CategoryId);
public sealed record AnswerRequest(int Position, int? Option, int? Second);
public sealed record JokerRequest(int Position, string? Type);
public sealed record ContinueRequest(string? AdIntentId);
public sealed record ReportRequest(int Position, string? Reason);

/// <summary>
/// Bireysel maç uçları (docs/04 — Faz 3'te bireysel maç REST ile yürür; SignalR rakipli maç için Faz 4'te devreye girer).
/// Hepsi aktif cihazdan çağrılmalıdır (tek cihaz kuralı). Hata gövdesi <c>{code, message}</c>.
/// </summary>
public static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<ActiveDeviceFilter>();

        api.MapGet("/jokers", async (HttpContext http, SoloMatchService matches, CancellationToken ct) =>
            Results.Ok(await matches.GetJokerBalancesAsync(ActivePlayer.IdOf(http), ct)));

        // Bota karşı oynarken seçilebilecek aktif botlar
        api.MapGet("/bots", async (VersusCoordinator coordinator) => Results.Ok(await coordinator.ListBotsAsync()));

        var group = api.MapGroup("/matches");

        group.MapPost("/solo", async (HttpContext http, StartSoloRequest request, SoloMatchService matches, CancellationToken ct) =>
            ToResult(await matches.StartAsync(ActivePlayer.IdOf(http), request.Mode, request.CategoryId, ct), created: true));

        group.MapGet("/current", async (HttpContext http, SoloMatchService matches, CancellationToken ct) =>
            ToResult(await matches.GetCurrentAsync(ActivePlayer.IdOf(http), ct)));

        group.MapPost("/{id:guid}/answer", async (HttpContext http, Guid id, AnswerRequest request, SoloMatchService matches, CancellationToken ct) =>
            ToResult(await matches.AnswerAsync(ActivePlayer.IdOf(http), id, request.Position, request.Option, request.Second, ct)));

        group.MapPost("/{id:guid}/joker", async (HttpContext http, Guid id, JokerRequest request, SoloMatchService matches, CancellationToken ct) =>
        {
            if (!JokerRules.TryParse(request.Type, out var type))
                return Results.Json(new { code = "JOKER_INVALID", message = "Joker türü half, double veya hint olmalı." }, statusCode: 400);
            return ToResult(await matches.UseJokerAsync(ActivePlayer.IdOf(http), id, request.Position, type, ct));
        });

        group.MapPost("/{id:guid}/continue", async (HttpContext http, Guid id, ContinueRequest request, SoloMatchService matches, CancellationToken ct) =>
            ToResult(await matches.ContinueAsync(ActivePlayer.IdOf(http), id, request.AdIntentId, ct)));

        group.MapPost("/{id:guid}/decline", async (HttpContext http, Guid id, SoloMatchService matches, CancellationToken ct) =>
            ToResult(await matches.DeclineContinueAsync(ActivePlayer.IdOf(http), id, ct)));

        group.MapPost("/{id:guid}/forfeit", async (HttpContext http, Guid id, SoloMatchService matches, CancellationToken ct) =>
            ToResult(await matches.ForfeitAsync(ActivePlayer.IdOf(http), id, ct)));

        group.MapGet("/{id:guid}/summary", async (HttpContext http, Guid id, SoloMatchService matches, CancellationToken ct) =>
            ToResult(await matches.GetSummaryAsync(ActivePlayer.IdOf(http), id, ct)));

        group.MapPost("/{id:guid}/report", async (HttpContext http, Guid id, ReportRequest request, SoloMatchService matches, CancellationToken ct) =>
        {
            var result = await matches.ReportAsync(ActivePlayer.IdOf(http), id, request.Position, request.Reason, ct);
            return result.IsSuccess ? Results.NoContent() : Error(result.Error!);
        });

        return app;
    }

    private static IResult ToResult<T>(MatchResult<T> result, bool created = false) =>
        result.IsSuccess ? (created ? Results.Json(result.Value, statusCode: 201) : Results.Ok(result.Value)) : Error(result.Error!);

    private static IResult Error(MatchError e) =>
        Results.Json(new { code = e.Code, message = e.Message, matchId = e.MatchId }, statusCode: e.StatusCode);
}
