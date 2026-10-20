using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Data;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 5: hediye puanı defteri (wallet_ledger) — gerçek PostgreSQL üzerinde.</summary>
[Collection("api")]
public class PointsWalletTests(MobileApiFactory factory)
{
    private async Task<T> With<T>(Func<IPointsWallet, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPointsWallet>());
    }

    [Fact]
    public async Task Grant_AddsPoints_AndIsIdempotentPerKey()
    {
        var user = Guid.NewGuid();
        Assert.True(await With(w => w.GrantAsync(user, 100, PointsLedgerTypes.Tournament, "period", "1", $"t:{user:N}")));
        Assert.False(await With(w => w.GrantAsync(user, 100, PointsLedgerTypes.Tournament, "period", "1", $"t:{user:N}")));
        Assert.Equal(100, await With(w => w.GetBalanceAsync(user)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => With(w => w.GrantAsync(user, 0, "x", null, null, "k0")));
    }

    [Fact]
    public async Task Spend_ChecksBalance_AndIsIdempotent()
    {
        var user = Guid.NewGuid();
        await With(w => w.GrantAsync(user, 50, PointsLedgerTypes.Tournament, null, null, $"g:{user:N}"));

        Assert.Equal(SpendOutcome.Insufficient, await With(w => w.TrySpendAsync(user, 60, PointsLedgerTypes.Purchase, null, null, "s1" + user)));
        Assert.Equal(SpendOutcome.Spent, await With(w => w.TrySpendAsync(user, 30, PointsLedgerTypes.Purchase, "order", "o1", "s2" + user)));
        Assert.Equal(SpendOutcome.AlreadySpent, await With(w => w.TrySpendAsync(user, 30, PointsLedgerTypes.Purchase, "order", "o1", "s2" + user)));
        Assert.Equal(20, await With(w => w.GetBalanceAsync(user)));
    }

    [Fact]
    public async Task ParallelSpends_NeverExceedTheBalance()
    {
        var user = Guid.NewGuid();
        await With(w => w.GrantAsync(user, 100, PointsLedgerTypes.Tournament, null, null, $"g:{user:N}"));

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => With(w => w.TrySpendAsync(user, 30, PointsLedgerTypes.Purchase, null, null, $"p{i}:{user:N}"))));

        Assert.Equal(3, results.Count(r => r == SpendOutcome.Spent));
        Assert.Equal(7, results.Count(r => r == SpendOutcome.Insufficient));
        Assert.Equal(10, await With(w => w.GetBalanceAsync(user)));
    }

    [Fact]
    public async Task AdminAdjust_RequiresAReason_AllowsNegativeBalance_AndIsIdempotent()
    {
        var user = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => With(w => w.AdjustAsync(user, -10, "a1", " ")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => With(w => w.AdjustAsync(user, 0, "a2", "neden")));

        Assert.True(await With(w => w.AdjustAsync(user, -40, $"adj:{user:N}", "Hile iadesi")));
        Assert.False(await With(w => w.AdjustAsync(user, -40, $"adj:{user:N}", "Hile iadesi")));
        Assert.Equal(-40, await With(w => w.GetBalanceAsync(user)));

        var history = await With(w => w.GetHistoryAsync(user, 1, 10));
        Assert.Equal("Hile iadesi", history.Single().Note);
        Assert.Equal(PointsLedgerTypes.Admin, history.Single().Type);
    }

    [Fact]
    public async Task History_IsNewestFirst_Paged_AndBalanceEqualsTheLedgerSum()
    {
        var user = Guid.NewGuid();
        for (var i = 1; i <= 5; i++) await With(w => w.GrantAsync(user, i * 10, PointsLedgerTypes.Tournament, "period", i.ToString(), $"h{i}:{user:N}"));
        await With(w => w.TrySpendAsync(user, 25, PointsLedgerTypes.Purchase, null, null, $"hs:{user:N}"));

        var page1 = await With(w => w.GetHistoryAsync(user, 1, 4));
        var page2 = await With(w => w.GetHistoryAsync(user, 2, 4));
        Assert.Equal(4, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Equal(-25, page1[0].Amount); // en yeni
        Assert.True(page1[0].Id > page1[1].Id);

        using var scope = factory.Services.CreateScope();
        var sum = await scope.ServiceProvider.GetRequiredService<WalletDbContext>().PointsLedger.Where(x => x.UserId == user).SumAsync(x => x.Amount);
        Assert.Equal(sum, await With(w => w.GetBalanceAsync(user)));
        Assert.Equal(125, sum);
    }
}
