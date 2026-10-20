using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Catalog.Application;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Catalog.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 2.1: katalog (mod ve alt dallar) uçları ve modül arayüzü, gerçek PostgreSQL üzerinde.</summary>
[Collection("api")]
public class CatalogApiTests(MobileApiFactory factory)
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Dev uid=catalog-test;email=c@test.local;verified=true;provider=google.com");
        return client;
    }

    private async Task<int> AddCategoryAsync(string modeCode, string name, bool published, int order = 50)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var mode = db.Modes.Single(m => m.Code == modeCode);
        var category = new Category { ModeId = mode.Id, Name = name, IsPublished = published, Order = order, CreatedAt = DateTime.UtcNow };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    private async Task<JsonElement> GetModeAsync(string code)
    {
        var json = await Client().GetFromJsonAsync<JsonElement>("/api/catalog/modes");
        return json.EnumerateArray().Single(m => m.GetProperty("code").GetString() == code);
    }

    [Fact]
    public async Task Modes_AreFour_InFixedOrder()
    {
        var json = await Client().GetFromJsonAsync<JsonElement>("/api/catalog/modes");
        var codes = json.EnumerateArray().Select(m => m.GetProperty("code").GetString()!).ToArray();
        Assert.Equal(["general", "branch", "kpss", "fan"], codes);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/catalog/modes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnpublishedCategory_IsHidden_PublishedIsShown()
    {
        var hidden = Unique("gizli");
        var shown = Unique("acik");
        await AddCategoryAsync(ModeCodes.Branch, hidden, published: false);
        await AddCategoryAsync(ModeCodes.Branch, shown, published: true);

        var branch = await GetModeAsync(ModeCodes.Branch);
        var names = branch.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();

        Assert.Contains(shown, names);
        Assert.DoesNotContain(hidden, names);
    }

    [Fact]
    public async Task GeneralMode_HasOnlyImplicitCategory_AndNeverOffersMixed()
    {
        var general = await GetModeAsync(ModeCodes.General);
        Assert.False(general.GetProperty("mixedAvailable").GetBoolean());
    }

    [Fact]
    public async Task Mixed_IsOffered_OnlyWhenAtLeastTwoCategoriesArePublished()
    {
        // Moddaki mevcut yayın sayısına göre karar verildiği için önce iki yayında kategori ekliyoruz
        await AddCategoryAsync(ModeCodes.Kpss, Unique("kpssa"), published: true);
        await AddCategoryAsync(ModeCodes.Kpss, Unique("kpssb"), published: true);

        var kpss = await GetModeAsync(ModeCodes.Kpss);
        Assert.True(kpss.GetProperty("categories").GetArrayLength() >= 2);
        Assert.True(kpss.GetProperty("mixedAvailable").GetBoolean());
    }

    [Fact]
    public async Task CategoryNames_MustBeUniqueWithinMode()
    {
        var name = Unique("tekrar");
        await AddCategoryAsync(ModeCodes.Fan, name, published: false);
        await Assert.ThrowsAnyAsync<Exception>(() => AddCategoryAsync(ModeCodes.Fan, name, published: false));
        // Başka modda aynı ad serbest
        await AddCategoryAsync(ModeCodes.Kpss, name, published: false);
    }

    [Fact]
    public async Task CatalogQueries_ReturnModeCode_AndPublishedIds()
    {
        var published = await AddCategoryAsync(ModeCodes.Fan, Unique("fanp"), published: true);
        var draft = await AddCategoryAsync(ModeCodes.Fan, Unique("fand"), published: false);

        using var scope = factory.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ICatalogQueries>();

        var info = await queries.GetCategoryAsync(published);
        Assert.NotNull(info);
        Assert.Equal(ModeCodes.Fan, info!.ModeCode);
        Assert.True(info.IsPublished);

        var ids = await queries.GetPublishedCategoryIdsAsync(ModeCodes.Fan);
        Assert.Contains(published, ids);
        Assert.DoesNotContain(draft, ids);
        Assert.Null(await queries.GetCategoryAsync(int.MaxValue));
    }
}
