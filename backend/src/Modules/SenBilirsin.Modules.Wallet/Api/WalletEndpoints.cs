using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SenBilirsin.Modules.Users.Api;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.Wallet.Api;

/// <summary>Cüzdan uçları: hediye puanı bakiyesi, joker sayıları ve "Puan geçmişi" (docs/07 §4).</summary>
public static class WalletEndpoints
{
    public static IEndpointRouteBuilder MapWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/wallet").RequireAuthorization().AddEndpointFilter<ActiveDeviceFilter>();

        group.MapGet("/", async (HttpContext http, IPointsWallet points, IJokerWallet jokers, CancellationToken ct) =>
        {
            var user = ActivePlayer.IdOf(http);
            var j = await jokers.GetBalancesAsync(user, ct);
            return Results.Ok(new { balance = await points.GetBalanceAsync(user, ct), jokers = new { half = j[JokerType.Half], @double = j[JokerType.Double], hint = j[JokerType.Hint] } });
        });

        group.MapGet("/history", async (HttpContext http, IPointsWallet points, int? page, int? pageSize, CancellationToken ct) =>
            Results.Ok(await points.GetHistoryAsync(ActivePlayer.IdOf(http), page ?? 1, pageSize ?? 30, ct)));

        return app;
    }
}
