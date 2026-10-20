using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Data;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 3: joker defteri (mimari ilke 2) — gerçek PostgreSQL üzerinde.</summary>
[Collection("api")]
public class JokerWalletTests(MobileApiFactory factory)
{
    private async Task<T> With<T>(Func<IJokerWallet, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IJokerWallet>());
    }

    [Fact]
    public async Task NewUser_GetsFiveOfEachJoker_OnlyOnce()
    {
        var user = Guid.NewGuid();
        var first = await With(w => w.GetBalancesAsync(user));
        Assert.All(Enum.GetValues<JokerType>(), t => Assert.Equal(5, first[t]));

        var again = await With(w => w.GetBalancesAsync(user));
        Assert.All(Enum.GetValues<JokerType>(), t => Assert.Equal(5, again[t]));
    }

    [Fact]
    public async Task WelcomeGift_UnderConcurrentFirstReads_IsGrantedExactlyOnce()
    {
        var user = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => With(w => w.GetBalancesAsync(user))));

        var balances = await With(w => w.GetBalancesAsync(user));
        Assert.All(Enum.GetValues<JokerType>(), t => Assert.Equal(5, balances[t]));
    }

    [Fact]
    public async Task Spend_DecreasesBalance_AndIsIdempotentPerKey()
    {
        var user = Guid.NewGuid();
        Assert.Equal(SpendOutcome.Spent, await With(w => w.TrySpendAsync(user, JokerType.Half, "m1", "use:m1:half")));
        Assert.Equal(SpendOutcome.AlreadySpent, await With(w => w.TrySpendAsync(user, JokerType.Half, "m1", "use:m1:half")));

        var balances = await With(w => w.GetBalancesAsync(user));
        Assert.Equal(4, balances[JokerType.Half]);
        Assert.Equal(5, balances[JokerType.Hint]);
    }

    [Fact]
    public async Task Spend_WithoutBalance_IsRefused_AndDoesNotGoNegative()
    {
        var user = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            Assert.Equal(SpendOutcome.Spent, await With(w => w.TrySpendAsync(user, JokerType.Double, "m", $"use:{i}")));

        Assert.Equal(SpendOutcome.Insufficient, await With(w => w.TrySpendAsync(user, JokerType.Double, "m", "use:extra")));
        Assert.Equal(0, (await With(w => w.GetBalancesAsync(user)))[JokerType.Double]);
    }

    [Fact]
    public async Task ParallelSpends_NeverExceedBalance()
    {
        var user = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => With(w => w.TrySpendAsync(user, JokerType.Hint, "m", $"race:{i}"))));

        Assert.Equal(5, results.Count(r => r == SpendOutcome.Spent));
        Assert.Equal(7, results.Count(r => r == SpendOutcome.Insufficient));
        Assert.Equal(0, (await With(w => w.GetBalancesAsync(user)))[JokerType.Hint]);
    }

    [Fact]
    public async Task ParallelSpends_WithSameKey_ChargeOnlyOnce()
    {
        var user = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => With(w => w.TrySpendAsync(user, JokerType.Half, "m", "same-key"))));

        Assert.Equal(1, results.Count(r => r == SpendOutcome.Spent));
        Assert.Equal(5, results.Count(r => r == SpendOutcome.AlreadySpent));
        Assert.Equal(4, (await With(w => w.GetBalancesAsync(user)))[JokerType.Half]);
    }

    [Fact]
    public async Task Grant_AddsJokers_AndIsIdempotent()
    {
        var user = Guid.NewGuid();
        Assert.True(await With(w => w.GrantAsync(user, JokerType.Hint, 1, LedgerTypes.Ad, "intent-1", "ad:intent-1")));
        Assert.False(await With(w => w.GrantAsync(user, JokerType.Hint, 1, LedgerTypes.Ad, "intent-1", "ad:intent-1")));

        Assert.Equal(6, (await With(w => w.GetBalancesAsync(user)))[JokerType.Hint]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => With(w => w.GrantAsync(user, JokerType.Hint, 0, LedgerTypes.Ad, null, "bad")));
    }

    [Fact]
    public async Task Balance_IsAlwaysTheSumOfTheLedger()
    {
        var user = Guid.NewGuid();
        await With(w => w.TrySpendAsync(user, JokerType.Half, "m", "k1"));
        await With(w => w.GrantAsync(user, JokerType.Half, 3, LedgerTypes.Purchase, "p", "k2"));
        await With(w => w.TrySpendAsync(user, JokerType.Half, "m", "k3"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var ledgerSum = await db.JokerLedger.Where(x => x.UserId == user && x.JokerType == JokerType.Half).SumAsync(x => x.Delta);
        var balance = (await scope.ServiceProvider.GetRequiredService<IJokerWallet>().GetBalancesAsync(user))[JokerType.Half];

        Assert.Equal(5 - 1 + 3 - 1, ledgerSum);
        Assert.Equal(ledgerSum, balance);
        // Her satırın benzersiz bir işlem anahtarı var
        Assert.Equal(await db.JokerLedger.CountAsync(), await db.JokerLedger.Select(x => x.IdempotencyKey).Distinct().CountAsync());
    }
}
