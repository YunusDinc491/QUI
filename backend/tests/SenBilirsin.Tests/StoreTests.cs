using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Store.Application;
using SenBilirsin.Modules.Store.Data;
using SenBilirsin.Modules.Store.Domain;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 6: mağaza — şifreli kupon stoğu, atomik/idempotent satın alma, Ödüllerim, kod gösterimi, sorun bildirimi.</summary>
[Collection("api")]
public class StoreTests(MobileApiFactory factory)
{
    private IServiceScope Scope() => factory.Services.CreateScope();

    private static ProductInput CouponProduct(int price = 50, string? name = null) =>
        new(ProductType.Coupon, name ?? "Kupon " + Guid.NewGuid().ToString("N")[..6], null, price, null, true, null, null, null);

    private async Task<int> NewProductAsync(ProductInput input)
    {
        using var scope = Scope();
        var result = await scope.ServiceProvider.GetRequiredService<StoreAdminService>().CreateAsync(input);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!.Id;
    }

    private async Task<int> NewCouponWithStockAsync(int codes, int price = 50)
    {
        var id = await NewProductAsync(CouponProduct(price));
        using var scope = Scope();
        var upload = await scope.ServiceProvider.GetRequiredService<StoreAdminService>().AddCodesAsync(id, Enumerable.Range(0, codes).Select(i => $"KOD-{Guid.NewGuid():N}-{i}"));
        Assert.Equal(codes, upload.Value!.Added);
        return id;
    }

    private async Task GiveAsync(Guid user, long points)
    {
        using var scope = Scope();
        await scope.ServiceProvider.GetRequiredService<IPointsWallet>().GrantAsync(user, points, PointsLedgerTypes.Tournament, null, null, $"seed:{user:N}:{Guid.NewGuid():N}");
    }

    private async Task<long> BalanceAsync(Guid user)
    {
        using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<IPointsWallet>().GetBalanceAsync(user);
    }

    private async Task<StoreResult<PurchaseResult>> BuyAsync(Guid user, int product, string? requestId = null)
    {
        using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<StoreService>().PurchaseAsync(user, product, requestId ?? Guid.NewGuid().ToString("N"));
    }

    private async Task<int> AvailableAsync(int productId)
    {
        using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<StoreDbContext>().Coupons.CountAsync(c => c.ProductId == productId && c.Status == CouponStatus.Available);
    }

    // ---------------------------------------------------------------- Şifreleme

    [Fact]
    public void Cipher_RoundTrips_RandomizesCiphertext_RejectsTampering_AndNeedsA32ByteKey()
    {
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var cipher = new CouponCipher(key);
        var a = cipher.Encrypt("GIFT-1234-ABCD");
        var b = cipher.Encrypt("GIFT-1234-ABCD");

        Assert.NotEqual(a, b);
        Assert.DoesNotContain("GIFT", a);
        Assert.Equal("GIFT-1234-ABCD", cipher.Decrypt(a));

        var bytes = Convert.FromBase64String(a);
        bytes[^1] ^= 1;
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => cipher.Decrypt(Convert.ToBase64String(bytes)));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => new CouponCipher(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))).Decrypt(a));

        Assert.Throws<InvalidOperationException>(() => new CouponCipher(null));
        Assert.Throws<InvalidOperationException>(() => new CouponCipher("QUJD"));
        Assert.Equal(CouponCipher.Hash("kod"), CouponCipher.Hash(" kod "));
        Assert.DoesNotContain("kod", CouponCipher.Hash("kod"), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- Yönetim: ürün ve stok

    [Fact]
    public async Task AdminProducts_AreValidated_AndCodesAreStoredEncryptedWithoutDuplicates()
    {
        using var scope = Scope();
        var admin = scope.ServiceProvider.GetRequiredService<StoreAdminService>();
        Assert.Equal("PRODUCT_INVALID", (await admin.CreateAsync(CouponProduct(price: 0))).Error!.Code);
        Assert.Equal("PRODUCT_INVALID", (await admin.CreateAsync(CouponProduct() with { Name = " " })).Error!.Code);
        Assert.Equal("PRODUCT_INVALID", (await admin.CreateAsync(new ProductInput(ProductType.JokerPack, "Paket", null, 10, null, true, null, null, null))).Error!.Code);
        Assert.Equal("PRODUCT_INVALID", (await admin.CreateAsync(new ProductInput(ProductType.Avatar, "Avatar", null, 10, null, true, null, null, null))).Error!.Code);

        var id = await NewProductAsync(CouponProduct());
        var upload = (await admin.AddCodesAsync(id, ["AAA-111", "BBB-222", " AAA-111 ", "", "  ", new string('x', 500)])).Value!;
        Assert.Equal((2, 1, 3), (upload.Added, upload.Duplicates, upload.Invalid));

        // Veritabanında düz metin kod yok
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var stored = await db.Coupons.AsNoTracking().Where(c => c.ProductId == id).Select(c => c.CodeEncrypted).ToListAsync();
        Assert.All(stored, s => { Assert.DoesNotContain("AAA", s); Assert.DoesNotContain("BBB", s); });

        // Kupon olmayan ürüne kod yüklenemez, olmayan ürüne de
        var pack = await NewProductAsync(new ProductInput(ProductType.JokerPack, "Paket", null, 10, null, true, JokerType.Hint, 3, null));
        Assert.Equal("NOT_A_COUPON", (await admin.AddCodesAsync(pack, ["X"])).Error!.Code);
        Assert.Equal("PRODUCT_NOT_FOUND", (await admin.AddCodesAsync(999_999, ["X"])).Error!.Code);

        // Tür sonradan değişmez
        Assert.Equal("PRODUCT_INVALID", (await admin.UpdateAsync(id, CouponProduct() with { Type = ProductType.JokerPack })).Error!.Code);
    }

    // ---------------------------------------------------------------- Satın alma

    [Fact]
    public async Task BuyingACoupon_ChargesThePoints_AssignsADistinctCode_AndRevealShowsItOnlyToTheOwner()
    {
        var product = await NewCouponWithStockAsync(2, price: 40);
        var buyer = Guid.NewGuid();
        await GiveAsync(buyer, 100);

        var result = await BuyAsync(buyer, product);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value!.Balance);
        Assert.False(result.Value.AlreadyPurchased);
        Assert.Equal(1, await AvailableAsync(product));

        using var scope = Scope();
        var store = scope.ServiceProvider.GetRequiredService<StoreService>();
        var rewards = await store.RewardsAsync(buyer);
        Assert.Single(rewards);
        Assert.False(rewards[0].Revealed);
        Assert.False(rewards[0].CanReportIssue);

        var revealed = await store.RevealAsync(buyer, result.Value.RewardId);
        Assert.StartsWith("KOD-", revealed.Value!.Code);
        Assert.Equal(1, revealed.Value.RevealCount);
        Assert.Equal("REWARD_NOT_FOUND", (await store.RevealAsync(Guid.NewGuid(), result.Value.RewardId)).Error!.Code); // başkası göremez

        // İkinci gösterim sayılır; ilk gösterim 48 saatlik süreyi başlatır
        Assert.Equal(2, (await store.RevealAsync(buyer, result.Value.RewardId)).Value!.RevealCount);
        var after = (await store.RewardsAsync(buyer))[0];
        Assert.True(after.Revealed && after.CanReportIssue);
        Assert.Equal(TimeSpan.FromHours(48), after.ReportDeadline - (await scope.ServiceProvider.GetRequiredService<StoreDbContext>().Rewards.AsNoTracking().SingleAsync(r => r.Id == result.Value.RewardId)).RevealedAt);
    }

    [Fact]
    public async Task InsufficientPoints_OrNoStock_ChargesNothingAndKeepsTheCodes()
    {
        var product = await NewCouponWithStockAsync(1, price: 40);
        var poor = Guid.NewGuid();
        await GiveAsync(poor, 10);

        var broke = await BuyAsync(poor, product);
        Assert.Equal("INSUFFICIENT_POINTS", broke.Error!.Code);
        Assert.Equal(10, await BalanceAsync(poor));
        Assert.Equal(1, await AvailableAsync(product)); // ayrılan kod geri bırakıldı

        var empty = await NewProductAsync(CouponProduct(20)); // hiç kodu yok
        var rich = Guid.NewGuid();
        await GiveAsync(rich, 100);
        Assert.Equal("OUT_OF_STOCK", (await BuyAsync(rich, empty)).Error!.Code);
        Assert.Equal(100, await BalanceAsync(rich));

        Assert.Equal("PRODUCT_NOT_FOUND", (await BuyAsync(rich, 999_999)).Error!.Code);
        Assert.Equal("REQUEST_ID_INVALID", (await BuyAsync(rich, product, requestId: " ")).Error!.Code);
    }

    [Fact]
    public async Task SameRequestId_IsAPurchaseOnlyOnce_EvenWhenSentInParallel()
    {
        var product = await NewCouponWithStockAsync(5, price: 30);
        var buyer = Guid.NewGuid();
        await GiveAsync(buyer, 200);
        var requestId = Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => BuyAsync(buyer, product, requestId)));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Code));
        Assert.Equal(1, results.Count(r => !r.Value!.AlreadyPurchased));
        Assert.Equal(170, await BalanceAsync(buyer));          // tek kez 30 düştü
        Assert.Equal(4, await AvailableAsync(product));         // yalnızca bir kod gitti
        using var scope = Scope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<StoreDbContext>().Rewards.CountAsync(r => r.UserId == buyer));
    }

    [Fact]
    public async Task ManyBuyersForFewCodes_NeverSellTheSameCodeTwice_AndChargeOnlyTheWinners()
    {
        var product = await NewCouponWithStockAsync(3, price: 25);
        var buyers = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        foreach (var b in buyers) await GiveAsync(b, 100);

        var results = await Task.WhenAll(buyers.Select(b => BuyAsync(b, product)));

        Assert.Equal(3, results.Count(r => r.IsSuccess));
        Assert.Equal(7, results.Count(r => r.Error?.Code == "OUT_OF_STOCK"));
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var sold = await db.Coupons.AsNoTracking().Where(c => c.ProductId == product && c.Status == CouponStatus.Sold).ToListAsync();
        Assert.Equal(3, sold.Count);
        Assert.Equal(3, sold.Select(c => c.SoldTo).Distinct().Count());
        Assert.Equal(0, await AvailableAsync(product));
        Assert.Equal(0, await db.Coupons.CountAsync(c => c.ProductId == product && c.Status == CouponStatus.Reserved));

        // Yalnızca kazananlar ödedi; kaybedenlerin bakiyesi dokunulmadı
        var balances = new List<long>();
        foreach (var b in buyers) balances.Add(await BalanceAsync(b));
        Assert.Equal(3, balances.Count(x => x == 75));
        Assert.Equal(7, balances.Count(x => x == 100));
    }

    [Fact]
    public async Task JokerPack_GrantsJokers_AvatarAndFrame_UnlockOnce_WithoutDoubleCharging()
    {
        var pack = await NewProductAsync(new ProductInput(ProductType.JokerPack, "Ipucu x3", null, 15, null, true, JokerType.Hint, 3, null));
        var avatar = await NewProductAsync(new ProductInput(ProductType.Avatar, "Ejderha", null, 60, null, true, null, null, 11));
        var frame = await NewProductAsync(new ProductInput(ProductType.Frame, "Altın", null, 40, null, true, null, null, 2));
        var buyer = Guid.NewGuid();
        await GiveAsync(buyer, 300);

        Assert.True((await BuyAsync(buyer, pack)).IsSuccess);
        using (var scope = Scope())
            Assert.Equal(5 + 3, (await scope.ServiceProvider.GetRequiredService<IJokerWallet>().GetBalancesAsync(buyer))[JokerType.Hint]);

        Assert.True((await BuyAsync(buyer, avatar)).IsSuccess);
        Assert.Equal("ALREADY_OWNED", (await BuyAsync(buyer, avatar)).Error!.Code);
        Assert.True((await BuyAsync(buyer, frame)).IsSuccess);
        Assert.Equal("ALREADY_OWNED", (await BuyAsync(buyer, frame)).Error!.Code);
        Assert.Equal(300 - 15 - 60 - 40, await BalanceAsync(buyer));

        // Joker paketi sınırsız satın alınır (limit yok, K-33/docs/07)
        Assert.True((await BuyAsync(buyer, pack)).IsSuccess);
        using var check = Scope();
        Assert.Equal(5 + 6, (await check.ServiceProvider.GetRequiredService<IJokerWallet>().GetBalancesAsync(buyer))[JokerType.Hint]);

        var list = await check.ServiceProvider.GetRequiredService<StoreService>().ListAsync(buyer);
        Assert.True(list.Single(p => p.Id == avatar).Owned);
        Assert.Equal("hint", list.Single(p => p.Id == pack).JokerType);
    }

    [Fact]
    public async Task ProductList_ShowsOnlyActiveProducts_AndStockForCoupons()
    {
        var withStock = await NewCouponWithStockAsync(1);
        var without = await NewProductAsync(CouponProduct());
        var hidden = await NewProductAsync(CouponProduct() with { IsActive = false });

        using var scope = Scope();
        var list = await scope.ServiceProvider.GetRequiredService<StoreService>().ListAsync(Guid.NewGuid());
        Assert.True(list.Single(p => p.Id == withStock).InStock);
        Assert.False(list.Single(p => p.Id == without).InStock);
        Assert.DoesNotContain(list, p => p.Id == hidden);
    }

    // ---------------------------------------------------------------- Yarım kalan işlemler

    [Fact]
    public async Task StaleReservations_AreCompletedWhenPaid_AndReleasedWhenNot()
    {
        var product = await NewCouponWithStockAsync(2, price: 20);
        var paid = Guid.NewGuid();
        var unpaid = Guid.NewGuid();
        await GiveAsync(paid, 100);

        long paidCode, unpaidCode;
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            var codes = await db.Coupons.Where(c => c.ProductId == product).OrderBy(c => c.Id).ToListAsync();
            var paidKey = $"purchase:{paid:N}:crash1";
            var unpaidKey = $"purchase:{unpaid:N}:crash2";
            (paidCode, unpaidCode) = (codes[0].Id, codes[1].Id);
            // "Çökme" benzetimi: kodlar ayrıldı; ilkinin puanı düşülmüş, ikincinin düşülmemiş; satış kaydı yok
            codes[0].Status = CouponStatus.Reserved; codes[0].ReservedAt = DateTime.UtcNow.AddMinutes(-10); codes[0].ReservedForKey = paidKey;
            codes[1].Status = CouponStatus.Reserved; codes[1].ReservedAt = DateTime.UtcNow.AddMinutes(-10); codes[1].ReservedForKey = unpaidKey;
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPointsWallet>().TrySpendAsync(paid, 20, PointsLedgerTypes.Purchase, "product", product.ToString(), paidKey);
        }

        using var recover = Scope();
        var store = new StoreService(recover.ServiceProvider.GetRequiredService<StoreDbContext>(), recover.ServiceProvider.GetRequiredService<CouponCipher>(),
            recover.ServiceProvider.GetRequiredService<IPointsWallet>(), recover.ServiceProvider.GetRequiredService<IJokerWallet>(), new FakeClock(DateTimeOffset.UtcNow));
        Assert.True(await store.RecoverStaleReservationsAsync() >= 2);

        var db2 = recover.ServiceProvider.GetRequiredService<StoreDbContext>();
        Assert.Equal(CouponStatus.Sold, (await db2.Coupons.AsNoTracking().SingleAsync(c => c.Id == paidCode)).Status);
        Assert.Equal(paid, (await db2.Coupons.AsNoTracking().SingleAsync(c => c.Id == paidCode)).SoldTo);
        Assert.Single(await store.RewardsAsync(paid));                     // ödeyen kullanıcıya kod teslim edildi
        Assert.Equal(CouponStatus.Available, (await db2.Coupons.AsNoTracking().SingleAsync(c => c.Id == unpaidCode)).Status); // ödemeyen için kod serbest
        Assert.Empty(await store.RewardsAsync(unpaid));
    }

    // ---------------------------------------------------------------- Sorun bildirimi

    private async Task<(Guid User, long RewardId, int Product)> BoughtAndRevealedAsync(int codes = 3)
    {
        var product = await NewCouponWithStockAsync(codes, price: 10);
        var user = Guid.NewGuid();
        await GiveAsync(user, 100);
        var bought = await BuyAsync(user, product);
        return (user, bought.Value!.RewardId, product);
    }

    [Fact]
    public async Task IssueReport_NeedsRevealFirst_Text_OnlyOnce_AndOnlyWithin48Hours()
    {
        var (user, reward, _) = await BoughtAndRevealedAsync();
        using var scope = Scope();
        var store = scope.ServiceProvider.GetRequiredService<StoreService>();

        Assert.Equal("NOT_REVEALED", (await store.ReportIssueAsync(user, reward, "Çalışmıyor diyor", null)).Error!.Code);
        await store.RevealAsync(user, reward);
        Assert.Equal("TEXT_INVALID", (await store.ReportIssueAsync(user, reward, "kısa", null)).Error!.Code);
        Assert.Equal("REWARD_NOT_FOUND", (await store.ReportIssueAsync(Guid.NewGuid(), reward, "Başkasının ödülü için", null)).Error!.Code);

        Assert.True((await store.ReportIssueAsync(user, reward, "Kod geçersiz hatası veriyor", "https://x/y.png")).IsSuccess);
        Assert.Equal("ISSUE_ALREADY_REPORTED", (await store.ReportIssueAsync(user, reward, "Tekrar bildirim deniyorum", null)).Error!.Code);
        Assert.Equal("open", (await store.RewardsAsync(user))[0].IssueStatus);

        // 48 saat sonra yeni bir ödül için pencere kapalı
        var (late, lateReward, _) = await BoughtAndRevealedAsync();
        await store.RevealAsync(late, lateReward);
        var future = new StoreService(scope.ServiceProvider.GetRequiredService<StoreDbContext>(), scope.ServiceProvider.GetRequiredService<CouponCipher>(),
            scope.ServiceProvider.GetRequiredService<IPointsWallet>(), scope.ServiceProvider.GetRequiredService<IJokerWallet>(), new FakeClock(DateTimeOffset.UtcNow.AddHours(49)));
        Assert.Equal("ISSUE_WINDOW_CLOSED", (await future.ReportIssueAsync(late, lateReward, "Çok geç bildiriyorum", null)).Error!.Code);
    }

    [Fact]
    public async Task ResolvingAnIssue_ReplacesTheCode_MarksTheOldOneInvalid_AndAllowsOneReplacementPerMonth()
    {
        var product = await NewCouponWithStockAsync(4, price: 10);
        var user = Guid.NewGuid();
        await GiveAsync(user, 100);
        var first = (await BuyAsync(user, product)).Value!.RewardId;
        var second = (await BuyAsync(user, product)).Value!.RewardId;
        using var scope = Scope();
        var store = scope.ServiceProvider.GetRequiredService<StoreService>();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var oldCode = (await store.RevealAsync(user, first)).Value!.Code;
        await store.RevealAsync(user, second);
        await store.ReportIssueAsync(user, first, "İlk kod çalışmıyor", null);
        await store.ReportIssueAsync(user, second, "İkinci kod da çalışmıyor", null);
        Assert.Equal(2, (await new StoreAdminService(db, scope.ServiceProvider.GetRequiredService<CouponCipher>(), TimeProvider.System).IssuesAsync()).Count(i => i.UserId == user));

        Assert.True((await store.ResolveIssueAsync(first, replace: true)).IsSuccess);
        var reward = await db.Rewards.AsNoTracking().SingleAsync(r => r.Id == first);
        Assert.Equal((IssueStatus.Resolved, 1), (reward.IssueStatus, reward.ReplacedCount));
        Assert.Null(reward.RevealedAt);                                                   // yeni kod yeniden gösterilmeli
        Assert.Equal(1, await db.Coupons.AsNoTracking().CountAsync(c => c.ProductId == product && c.Status == CouponStatus.Invalid)); // eski kod bir daha satılmaz
        Assert.NotEqual(oldCode, (await store.RevealAsync(user, first)).Value!.Code);

        // Ayda en fazla 1 değişim
        Assert.Equal("MONTHLY_LIMIT", (await store.ResolveIssueAsync(second, replace: true)).Error!.Code);
        Assert.True((await store.ResolveIssueAsync(second, replace: false)).IsSuccess);   // reddet
        Assert.Equal("rejected", (await store.RewardsAsync(user)).Single(r => r.Id == second).IssueStatus);
        Assert.Equal("NO_OPEN_ISSUE", (await store.ResolveIssueAsync(second, replace: false)).Error!.Code);
    }

    // ---------------------------------------------------------------- Uçlar

    private async Task<(HttpClient Client, Guid UserId)> RegisteredAsync()
    {
        var uid = Guid.NewGuid().ToString("N");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Dev uid={uid};email={uid}@test.local;verified=true;provider=google.com");
        client.DefaultRequestHeaders.Add("X-Device-Id", "d1");
        await client.PostAsJsonAsync("/api/users/me", new { username = "s" + uid[..10], displayName = "Mağaza", avatarId = 1, birthYear = 2000, termsAccepted = true });
        await client.PostAsJsonAsync("/api/users/me/session", new { deviceId = "d1", platform = "test" });
        var me = await client.GetFromJsonAsync<JsonElement>("/api/users/me");
        return (client, me.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Endpoints_PurchaseRevealAndReport_WorkEndToEnd_AndRequireLogin()
    {
        var product = await NewCouponWithStockAsync(1, price: 20);
        var (client, user) = await RegisteredAsync();
        await GiveAsync(user, 50);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/store/products");
        Assert.Contains(list.EnumerateArray(), p => p.GetProperty("id").GetInt32() == product && p.GetProperty("inStock").GetBoolean());

        var requestId = Guid.NewGuid().ToString("N");
        var buy = await client.PostAsJsonAsync("/api/store/purchase", new { productId = product, requestId });
        Assert.Equal(HttpStatusCode.OK, buy.StatusCode);
        var rewardId = (await buy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rewardId").GetInt64();
        Assert.Equal(30, (await client.GetFromJsonAsync<JsonElement>("/api/wallet")).GetProperty("balance").GetInt64());

        var again = await client.PostAsJsonAsync("/api/store/purchase", new { productId = product, requestId });
        Assert.True((await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyPurchased").GetBoolean());

        var rewards = await client.GetFromJsonAsync<JsonElement>("/api/store/rewards");
        Assert.Equal(1, rewards.GetArrayLength());
        Assert.False(rewards[0].GetProperty("revealed").GetBoolean());
        Assert.DoesNotContain("KOD-", rewards.GetRawText()); // liste kodu göstermez

        var reveal = await client.PostAsync($"/api/store/rewards/{rewardId}/reveal", null);
        Assert.StartsWith("KOD-", (await reveal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var report = await client.PostAsJsonAsync($"/api/store/rewards/{rewardId}/report", new { text = "Kod geçersiz diyor" });
        Assert.Equal(HttpStatusCode.NoContent, report.StatusCode);
        var dup = await client.PostAsJsonAsync($"/api/store/rewards/{rewardId}/report", new { text = "Bir daha bildiriyorum" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        // Stok bitti, bakiye 30 → ikinci kupon alınamaz
        var out2 = await client.PostAsJsonAsync("/api/store/purchase", new { productId = product, requestId = Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Conflict, out2.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/store/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync($"/api/store/rewards/{rewardId}/reveal", null)).StatusCode);
    }
}
