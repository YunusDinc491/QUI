using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Store.Application;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 6: yönetim paneli mağaza uçları — ürünler, şifreli kod yükleme, sorun kuyruğu.</summary>
[Collection("admin-api")]
public class AdminStoreTests(AdminApiFactory factory)
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync());

    private static object Coupon(string name, int price = 30) => new { type = "Coupon", name, price, isActive = true };

    [Fact]
    public async Task AllStoreAdminEndpoints_RequireLogin()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/store/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/store/issues")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/store/products/1/codes", new { codes = new[] { "x" } })).StatusCode);
    }

    [Fact]
    public async Task Products_AreValidated_Created_Updated_AndCodesAreUploadedWithoutEverBeingEchoed()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var name = "Kupon " + Guid.NewGuid().ToString("N")[..6];

        Assert.Equal("PRODUCT_INVALID", (await Json(await client.PostAsJsonAsync("/api/store/products", Coupon(name, price: 0)))).GetProperty("code").GetString());

        var created = await client.PostAsJsonAsync("/api/store/products", Coupon(name));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetInt32();

        var upload = await Json(await client.PostAsJsonAsync($"/api/store/products/{id}/codes", new { text = "GIZLI-AAA-111\nGIZLI-BBB-222\r\nGIZLI-AAA-111;GIZLI-CCC-333" }));
        Assert.Equal((3, 1, 0), (upload.GetProperty("added").GetInt32(), upload.GetProperty("duplicates").GetInt32(), upload.GetProperty("invalid").GetInt32()));
        Assert.DoesNotContain("GIZLI", upload.GetRawText());

        var list = await Json(await client.GetAsync("/api/store/products"));
        var mine = list.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == id);
        Assert.Equal(3, mine.GetProperty("available").GetInt32());
        Assert.DoesNotContain("GIZLI", list.GetRawText());

        var updated = await client.PutAsJsonAsync($"/api/store/products/{id}", new { type = "Coupon", name, price = 45, isActive = false });
        Assert.Equal(45, (await Json(updated)).GetProperty("price").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/store/products/999999", Coupon("Yok"))).StatusCode);
        var pack = await Json(await client.PostAsJsonAsync("/api/store/products", new { type = "JokerPack", name = "Paket", price = 10, isActive = true, jokerType = "Hint", jokerAmount = 3 }));
        var notCoupon = await client.PostAsJsonAsync($"/api/store/products/{pack.GetProperty("id").GetInt32()}/codes", new { codes = new[] { "x" } });
        Assert.Equal(HttpStatusCode.Conflict, notCoupon.StatusCode);
        Assert.Equal("NOT_A_COUPON", (await Json(notCoupon)).GetProperty("code").GetString());

        // Denetim kaydı: yükleme işlemi var ama kodlar yok
        var logs = await Json(await client.GetAsync("/api/audit-logs?entity=store_product&pageSize=50"));
        Assert.Contains(logs.GetProperty("items").EnumerateArray(), l => l.GetProperty("action").GetString() == "store.codes_upload");
        Assert.DoesNotContain("GIZLI", logs.GetRawText());
    }

    [Fact]
    public async Task IssueQueue_ShowsOpenReports_AndResolvingReplacesTheCode()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = (await Json(await client.PostAsJsonAsync("/api/store/products", Coupon("Sorunlu " + Guid.NewGuid().ToString("N")[..5], price: 10)))).GetProperty("id").GetInt32();
        await client.PostAsJsonAsync($"/api/store/products/{id}/codes", new { codes = new[] { "ESKI-KOD-1", "YENI-KOD-2" } });

        var user = Guid.NewGuid();
        long rewardId;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPointsWallet>().GrantAsync(user, 50, PointsLedgerTypes.Tournament, null, null, $"seed:{user:N}");
            var store = scope.ServiceProvider.GetRequiredService<StoreService>();
            rewardId = (await store.PurchaseAsync(user, id, Guid.NewGuid().ToString("N"))).Value!.RewardId;
            await store.RevealAsync(user, rewardId);
            Assert.True((await store.ReportIssueAsync(user, rewardId, "Kod geçersiz hatası alıyorum", null)).IsSuccess);
        }

        var issues = await Json(await client.GetAsync("/api/store/issues"));
        var issue = issues.EnumerateArray().Single(i => i.GetProperty("rewardId").GetInt64() == rewardId);
        Assert.Equal("Kod geçersiz hatası alıyorum", issue.GetProperty("text").GetString());
        Assert.Equal(1, issue.GetProperty("userRewardCount").GetInt32());
        Assert.DoesNotContain("KOD-", issues.GetRawText());

        var resolve = await Json(await client.PostAsJsonAsync($"/api/store/issues/{rewardId}/resolve", new { replace = true }));
        Assert.True(resolve.GetProperty("replaced").GetBoolean());
        Assert.DoesNotContain(
            (await Json(await client.GetAsync("/api/store/issues"))).EnumerateArray(), i => i.GetProperty("rewardId").GetInt64() == rewardId);

        // Kullanıcı yeni kodu görür ve eskisinden farklıdır
        using (var scope = factory.Services.CreateScope())
            Assert.Equal("YENI-KOD-2", (await scope.ServiceProvider.GetRequiredService<StoreService>().RevealAsync(user, rewardId)).Value!.Code);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/store/issues/{rewardId}/resolve", new { replace = false })).StatusCode);
    }
}
