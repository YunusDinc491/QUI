using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Tournament.Application;
using SenBilirsin.Modules.Users.Data;
using SenBilirsin.Modules.Users.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 5: yönetim paneli turnuva uçları — inceleme, men, ödül tablosu, bütçe göstergesi.</summary>
[Collection("admin-api")]
public class AdminTournamentTests(AdminApiFactory factory)
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync());

    private async Task<Guid> NewUserAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        var user = new UserAccount
        {
            Id = Guid.NewGuid(), FirebaseUid = Guid.NewGuid().ToString("N"), Username = "a" + Guid.NewGuid().ToString("N")[..10], DisplayName = name,
            AvatarId = 1, BirthYear = 2000, CreatedAt = DateTime.UtcNow, TermsAcceptedAt = DateTime.UtcNow, TermsVersion = "t",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Şimdiki döneme verilen oyunculara puan yazar ve dönem kimliğini döndürür.</summary>
    private Task<int> SeedCurrentPeriodAsync(params (Guid User, int Points)[] players) => SeedPeriodAsync(TimeSpan.Zero, players);

    /// <summary><paramref name="offset"/> kadar ileri bir ana denk gelen dönemi (ayrı bir dönem = test yalıtımı) puanlarla doldurur.</summary>
    private async Task<int> SeedPeriodAsync(TimeSpan offset, params (Guid User, int Points)[] players)
    {
        using var scope = factory.Services.CreateScope();
        var scores = scope.ServiceProvider.GetRequiredService<ScoreService>();
        foreach (var (user, points) in players) await scores.CreditAsync(Guid.NewGuid(), user, points, factory.Clock.GetUtcNow().UtcDateTime + offset);
        return (await scope.ServiceProvider.GetRequiredService<PeriodService>().EnsureForAsync(factory.Clock.GetUtcNow().UtcDateTime + offset)).Id;
    }

    [Fact]
    public async Task AllTournamentAdminEndpoints_RequireLogin()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tournament/periods")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tournament/rewards")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/tournament/periods/1/disqualify", new { })).StatusCode);
    }

    [Fact]
    public async Task Periods_ListShowsTheOpenPeriodWithParticipantsAndTotals()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var a = await NewUserAsync("Ali");
        var b = await NewUserAsync("Veli");
        var periodId = await SeedCurrentPeriodAsync((a, 60), (b, 40));

        var list = await Json(await client.GetAsync("/api/tournament/periods"));
        var current = list.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == periodId);
        Assert.Equal("open", current.GetProperty("phase").GetString());
        Assert.True(current.GetProperty("participants").GetInt32() >= 2);
        Assert.True(current.GetProperty("totalPoints").GetInt64() >= 100);
        Assert.Equal(48, (current.GetProperty("endsAt").GetDateTimeOffset() - current.GetProperty("startsAt").GetDateTimeOffset()).TotalHours);
    }

    [Fact]
    public async Task Standings_ShowNames_AndDisqualificationFlow_RemovesAndRestoresThePlayer()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var top = await NewUserAsync("Birinci");
        var second = await NewUserAsync("Ikinci");
        // Diğer testlerin puanlarından büyük olsun diye yüksek puanlar
        var periodId = await SeedCurrentPeriodAsync((top, 900_000), (second, 800_000));

        var before = await Json(await client.GetAsync($"/api/tournament/periods/{periodId}/standings?take=5"));
        Assert.Equal("Birinci", before[0].GetProperty("name").GetString());
        Assert.Equal(1, before[0].GetProperty("rank").GetInt32());

        // Gerekçesiz / olmayan dönem
        Assert.Equal("REASON_INVALID", (await Json(await client.PostAsJsonAsync($"/api/tournament/periods/{periodId}/disqualify", new { userId = top, reason = " " }))).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/tournament/periods/99999/disqualify", new { userId = top, reason = "x" })).StatusCode);

        var banned = await client.PostAsJsonAsync($"/api/tournament/periods/{periodId}/disqualify", new { userId = top, reason = "Hile şüphesi" });
        Assert.True((await Json(banned)).GetProperty("changed").GetBoolean());
        // İkinci istek zararsız
        Assert.False((await Json(await client.PostAsJsonAsync($"/api/tournament/periods/{periodId}/disqualify", new { userId = top, reason = "tekrar" }))).GetProperty("changed").GetBoolean());

        var after = await Json(await client.GetAsync($"/api/tournament/periods/{periodId}/standings?take=5"));
        Assert.Equal("Ikinci", after[0].GetProperty("name").GetString());
        Assert.Equal(1, after[0].GetProperty("rank").GetInt32());                    // alttaki sıra yükseldi
        var flagged = after.EnumerateArray().Single(r => r.GetProperty("userId").GetGuid() == top);
        Assert.True(flagged.GetProperty("disqualified").GetBoolean());
        Assert.Equal("Hile şüphesi", flagged.GetProperty("disqualificationReason").GetString());
        Assert.Equal(JsonValueKind.Null, flagged.GetProperty("rank").ValueKind);

        // Men kaldırılınca eski yerine döner
        Assert.True((await Json(await client.DeleteAsync($"/api/tournament/periods/{periodId}/disqualify/{top}"))).GetProperty("lifted").GetBoolean());
        var restored = await Json(await client.GetAsync($"/api/tournament/periods/{periodId}/standings?take=5"));
        Assert.Equal("Birinci", restored[0].GetProperty("name").GetString());

        // Denetim kaydı
        var logs = await Json(await client.GetAsync("/api/audit-logs?entity=period&pageSize=50"));
        var actions = logs.GetProperty("items").EnumerateArray().Select(l => l.GetProperty("action").GetString()).ToList();
        Assert.Contains("tournament.disqualify", actions);
        Assert.Contains("tournament.disqualify_lifted", actions);
    }

    [Fact]
    public async Task Disqualification_IsRefused_AfterTheRewardsWereDistributed()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var user = await NewUserAsync("Gec");
        var periodId = await SeedPeriodAsync(TimeSpan.FromDays(40), (user, 10)); // ayrı dönem: dağıtım şimdiki dönemi etkilemesin
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DistributionService>().DistributeAsync(periodId);

        var response = await client.PostAsJsonAsync($"/api/tournament/periods/{periodId}/disqualify", new { userId = user, reason = "Çok geç" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PERIOD_DISTRIBUTED", (await Json(response)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/tournament/periods/{periodId}/disqualify/{user}")).StatusCode);
    }

    [Fact]
    public async Task Rewards_ShowTheDraftTableAndTheMonthlyBudget_AndCanBeReplacedWithValidation()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var original = await Json(await client.GetAsync("/api/tournament/rewards"));
        Assert.Equal(10, original.GetProperty("tiers").GetArrayLength());
        var budget = original.GetProperty("budget");
        Assert.Equal(430, budget.GetProperty("perPeriodPoints").GetInt32());           // docs/07 §3 örneği: 430 puan/tur
        Assert.Equal(6450, budget.GetProperty("perMonthPoints").GetInt32());
        Assert.Equal(322.5m, budget.GetProperty("perMonthLira").GetDecimal());          // ≈ ayda ~320 TL

        async Task<string> Put(object tiers) =>
            (await Json(await client.PutAsJsonAsync("/api/tournament/rewards", new { tiers }))).GetProperty("code").GetString()!;

        Assert.Equal("REWARDS_INVALID", await Put(Array.Empty<object>()));
        Assert.Equal("REWARDS_INVALID", await Put(new[] { new { rankFrom = 1, rankTo = 1, amount = 0 } }));
        Assert.Equal("REWARDS_INVALID", await Put(new[] { new { rankFrom = 3, rankTo = 2, amount = 5 } }));
        Assert.Equal("REWARDS_OVERLAP", await Put(new[] { new { rankFrom = 1, rankTo = 3, amount = 5 }, new { rankFrom = 3, rankTo = 4, amount = 2 } }));

        var replaced = await client.PutAsJsonAsync("/api/tournament/rewards", new
        {
            tiers = new[] { new { rankFrom = 1, rankTo = 1, amount = 200 }, new { rankFrom = 2, rankTo = 5, amount = 40 } },
        });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var body = await Json(replaced);
        Assert.Equal(2, body.GetProperty("tiers").GetArrayLength());
        Assert.Equal(360, body.GetProperty("budget").GetProperty("perPeriodPoints").GetInt32()); // 200 + 4×40

        var logs = await Json(await client.GetAsync("/api/audit-logs?entity=reward_table&pageSize=10"));
        Assert.Contains(logs.GetProperty("items").EnumerateArray(), l => l.GetProperty("action").GetString() == "tournament.rewards_update" && l.GetProperty("before").GetString()!.Contains("\"amount\":100"));

        // Eski tabloyu geri yükle (diğer testler varsayılan tabloya güveniyor)
        await client.PutAsJsonAsync("/api/tournament/rewards", new
        {
            tiers = new[]
            {
                new { rankFrom = 1, rankTo = 1, amount = 100 }, new { rankFrom = 2, rankTo = 2, amount = 80 }, new { rankFrom = 3, rankTo = 3, amount = 60 },
                new { rankFrom = 4, rankTo = 4, amount = 50 }, new { rankFrom = 5, rankTo = 5, amount = 40 }, new { rankFrom = 6, rankTo = 6, amount = 30 },
                new { rankFrom = 7, rankTo = 7, amount = 25 }, new { rankFrom = 8, rankTo = 8, amount = 20 }, new { rankFrom = 9, rankTo = 9, amount = 15 },
                new { rankFrom = 10, rankTo = 10, amount = 10 },
            },
        });
    }
}
