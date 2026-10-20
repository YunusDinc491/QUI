using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SenBilirsin.Modules.Administration.Data;

namespace SenBilirsin.Tests;

/// <summary>Faz 2.4: yönetim API'si — kategori ve soru uçları, tekrar uyarısı, CSV toplu yükleme, denetim kaydı.</summary>
[Collection("admin-api")]
public class AdminCatalogQuestionTests(AdminApiFactory factory)
{
    private const int BranchModeId = 2;

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private static string Words() =>
        string.Join(' ', Enumerable.Range(0, 4).Select(_ => Guid.NewGuid().ToString("N")[..8])) + " sorusu nedir";

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    private static async Task<int> NewCategoryAsync(HttpClient client, string? name = null, int modeId = BranchModeId)
    {
        var response = await client.PostAsJsonAsync("/api/categories", new { modeId, name = name ?? Unique("Kat") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetInt32();
    }

    private static object Question(int categoryId, string? text = null, string difficulty = "Easy", string? status = null, int correct = 1) =>
        new { categoryId, text = text ?? Words(), options = new[] { "Bir", "İki", "Üç", "Dört" }, correctIndex = correct, difficulty, status };

    [Fact]
    public async Task AllAdminEndpoints_RequireLogin()
    {
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/categories", "/api/questions", "/api/audit-logs" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/questions/import/preview", new { csv = "x" })).StatusCode);
    }

    // ---- Kategoriler ----

    [Fact]
    public async Task Category_CreateEditPublish_AndListShowsCounts()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var name = Unique("Tarih");
        var id = await NewCategoryAsync(client, name);

        // Yeni kategori gizli başlar
        var created = (await Json(await client.GetAsync("/api/categories"))).EnumerateArray()
            .Single(c => c.GetProperty("category").GetProperty("id").GetInt32() == id);
        Assert.False(created.GetProperty("category").GetProperty("isPublished").GetBoolean());
        Assert.Equal("branch", created.GetProperty("category").GetProperty("modeCode").GetString());

        var put = await client.PutAsJsonAsync($"/api/categories/{id}", new { modeId = BranchModeId, name = name + " 2", colorHex = "#1E88E5" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("#1E88E5", (await Json(put)).GetProperty("colorHex").GetString());

        var publish = await client.PostAsync($"/api/categories/{id}/publish", null);
        Assert.True((await Json(publish)).GetProperty("isPublished").GetBoolean());

        await client.PostAsJsonAsync("/api/questions", Question(id, difficulty: "Hard", status: "Published"));
        await client.PostAsJsonAsync("/api/questions", Question(id));
        var item = (await Json(await client.GetAsync("/api/categories"))).EnumerateArray()
            .Single(c => c.GetProperty("category").GetProperty("id").GetInt32() == id);
        var counts = item.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("hard").GetInt32());
        Assert.Equal(1, counts.GetProperty("draft").GetInt32());

        var unpublish = await client.PostAsync($"/api/categories/{id}/unpublish", null);
        Assert.False((await Json(unpublish)).GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task Category_Rules_DuplicateName_GeneralMode_BadColor_Missing()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var name = Unique("Cografya");
        await NewCategoryAsync(client, name);

        var dup = await client.PostAsJsonAsync("/api/categories", new { modeId = BranchModeId, name });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("CATEGORY_NAME_TAKEN", (await Json(dup)).GetProperty("code").GetString());

        // Aynı ad başka modda olabilir
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/categories", new { modeId = 3, name })).StatusCode);

        var general = await client.PostAsJsonAsync("/api/categories", new { modeId = 1, name = "Yeni" });
        Assert.Equal("GENERAL_SINGLE_CATEGORY", (await Json(general)).GetProperty("code").GetString());

        var color = await client.PostAsJsonAsync("/api/categories", new { modeId = BranchModeId, name = Unique("Renk"), colorHex = "kirmizi" });
        Assert.Equal("COLOR_INVALID", (await Json(color)).GetProperty("code").GetString());

        var tooShort = await client.PostAsJsonAsync("/api/categories", new { modeId = BranchModeId, name = "x" });
        Assert.Equal("NAME_TOO_SHORT", (await Json(tooShort)).GetProperty("code").GetString());

        var noMode = await client.PostAsJsonAsync("/api/categories", new { modeId = 99, name = Unique("Mod") });
        Assert.Equal(HttpStatusCode.NotFound, noMode.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsync("/api/categories/999999/publish", null)).StatusCode);

        var rename = await client.PutAsJsonAsync("/api/categories/1", new { modeId = 1, name = "Başka" });
        Assert.Equal("GENERAL_RENAME", (await Json(rename)).GetProperty("code").GetString());
    }

    // ---- Sorular ----

    [Fact]
    public async Task Question_CreateReadUpdateStatus_RoundTrip_ShowsCorrectIndexToAdmin()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await NewCategoryAsync(client);

        var created = await client.PostAsJsonAsync("/api/questions", Question(category, correct: 2));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await Json(created);
        var id = dto.GetProperty("id").GetInt32();
        Assert.Equal(2, dto.GetProperty("correctIndex").GetInt32());
        Assert.Equal("Draft", dto.GetProperty("status").GetString());
        Assert.Equal("Easy", dto.GetProperty("difficulty").GetString());

        var update = await client.PutAsJsonAsync($"/api/questions/{id}", Question(category, dto.GetProperty("text").GetString(), "Medium", correct: 0));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Medium", (await Json(update)).GetProperty("difficulty").GetString());

        var status = await client.PostAsJsonAsync($"/api/questions/{id}/status", new { status = "Published" });
        Assert.Equal("Published", (await Json(status)).GetProperty("status").GetString());

        var read = await Json(await client.GetAsync($"/api/questions/{id}"));
        Assert.Equal("Published", read.GetProperty("status").GetString());
        Assert.Equal(0, read.GetProperty("correctIndex").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/questions/999999")).StatusCode);
    }

    [Fact]
    public async Task Question_InvalidInput_Returns400WithCode_AndUnknownCategory404()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await NewCategoryAsync(client);

        var tooShort = await client.PostAsJsonAsync("/api/questions", Question(category, "kısa"));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal("TEXT_TOO_SHORT", (await Json(tooShort)).GetProperty("code").GetString());

        var noCategory = await client.PostAsJsonAsync("/api/questions", Question(999999));
        Assert.Equal(HttpStatusCode.NotFound, noCategory.StatusCode);
    }

    [Fact]
    public async Task Question_Duplicate_Returns409WithMatches_AndCanBeForced()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await NewCategoryAsync(client);
        var text = Words();
        await client.PostAsJsonAsync("/api/questions", Question(category, text));

        var again = await client.PostAsJsonAsync("/api/questions", Question(category, text.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var body = await Json(again);
        Assert.Equal("DUPLICATE_SUSPECTED", body.GetProperty("code").GetString());
        Assert.Single(body.GetProperty("duplicates").EnumerateArray());

        // Yazarken anlık kontrol
        var check = await Json(await client.PostAsJsonAsync("/api/questions/check-duplicate", new { categoryId = category, text }));
        Assert.Single(check.GetProperty("duplicates").EnumerateArray());

        var forced = await client.PostAsJsonAsync("/api/questions", new
        {
            categoryId = category, text, options = new[] { "Bir", "İki", "Üç", "Dört" }, correctIndex = 0, difficulty = "Easy", ignoreDuplicates = true,
        });
        Assert.Equal(HttpStatusCode.Created, forced.StatusCode);
    }

    [Fact]
    public async Task Question_List_FiltersAndPages()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await NewCategoryAsync(client);
        for (var i = 0; i < 3; i++) await client.PostAsJsonAsync("/api/questions", Question(category, difficulty: i == 0 ? "Hard" : "Easy"));

        var all = await Json(await client.GetAsync($"/api/questions?categoryId={category}"));
        Assert.Equal(3, all.GetProperty("total").GetInt32());

        var hard = await Json(await client.GetAsync($"/api/questions?categoryId={category}&difficulty=Hard"));
        Assert.Equal(1, hard.GetProperty("total").GetInt32());

        var paged = await Json(await client.GetAsync($"/api/questions?categoryId={category}&page=2&pageSize=2"));
        Assert.Single(paged.GetProperty("items").EnumerateArray());

        var published = await Json(await client.GetAsync($"/api/questions?categoryId={category}&status=Published"));
        Assert.Equal(0, published.GetProperty("total").GetInt32());
    }

    // ---- Toplu yükleme ----

    private const string Header = "mod;kategori;zorluk;soru;A;B;C;D;dogru;aciklama";

    private static string CsvLine(string category, string question, string difficulty = "kolay", string correct = "B", string mode = "branş") =>
        $"{mode};{category};{difficulty};{question};Bir;İki;Üç;Dört;{correct};";

    [Fact]
    public async Task Import_Preview_ReportsRowByRow_ErrorsAndDuplicates()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = Unique("Ice");
        await NewCategoryAsync(client, category);
        var existing = Words();
        var existingCategoryId = await NewCategoryAsync(client, category + "B");
        await client.PostAsJsonAsync("/api/questions", Question(existingCategoryId, existing));

        var fresh = Words();
        var csv = string.Join("\n",
            Header,
            CsvLine(category, fresh),                                  // 1: ok
            CsvLine(category, Words(), difficulty: "çok zor"),         // 2: zorluk hatalı
            CsvLine("YokBoyleKategori", Words()),                      // 3: kategori yok
            CsvLine(category, Words(), correct: "E"),                  // 4: doğru şık hatalı
            CsvLine(category, fresh.ToUpperInvariant()),               // 5: dosyada tekrar (1. satırla aynı)
            CsvLine(category + "B", existing));                        // 6: veritabanında tekrar

        var preview = await Json(await client.PostAsJsonAsync("/api/questions/import/preview", new { csv }));
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("fatalError").ValueKind);
        Assert.Equal(6, preview.GetProperty("total").GetInt32());
        Assert.Equal(1, preview.GetProperty("valid").GetInt32());
        Assert.Equal(3, preview.GetProperty("invalid").GetInt32());
        Assert.Equal(2, preview.GetProperty("duplicates").GetInt32());

        var rows = preview.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal("ok", rows[0].GetProperty("status").GetString());
        Assert.Contains("Zorluk", rows[1].GetProperty("errors")[0].GetString());
        Assert.Contains("Kategori bulunamadı", rows[2].GetProperty("errors")[0].GetString());
        Assert.Contains("Doğru şık", rows[3].GetProperty("errors")[0].GetString());
        Assert.Contains("1. satır", rows[4].GetProperty("fileDuplicateOf").GetString());
        Assert.Equal("duplicate", rows[5].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Import_Commit_AddsValidRows_AsDraft_SkipsDuplicatesUnlessAsked()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = Unique("Ice");
        var categoryId = await NewCategoryAsync(client, category);
        var existing = Words();
        await client.PostAsJsonAsync("/api/questions", Question(categoryId, existing));

        var csv = string.Join("\n", Header,
            CsvLine(category, Words()),
            CsvLine(category, Words(), difficulty: "zor", correct: "D"),
            CsvLine(category, existing),
            CsvLine(category, Words(), difficulty: "yanlis"));

        var first = await Json(await client.PostAsJsonAsync("/api/questions/import/commit", new { csv }));
        Assert.Equal(2, first.GetProperty("created").GetInt32());
        Assert.Equal(1, first.GetProperty("skippedInvalid").GetInt32());
        Assert.Equal(1, first.GetProperty("skippedDuplicates").GetInt32());

        var list = await Json(await client.GetAsync($"/api/questions?categoryId={categoryId}&status=Draft"));
        Assert.Equal(3, list.GetProperty("total").GetInt32()); // 1 mevcut + 2 yeni, hepsi taslak

        // Tekrarlar da istenirse eklenir, doğrudan yayınlanabilir
        var second = await Json(await client.PostAsJsonAsync("/api/questions/import/commit",
            new { csv = string.Join("\n", Header, CsvLine(category, existing)), includeDuplicates = true, publish = true }));
        Assert.Equal(1, second.GetProperty("created").GetInt32());
        Assert.Equal(1, (await Json(await client.GetAsync($"/api/questions?categoryId={categoryId}&status=Published"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Import_Rejects_MissingColumns_EmptyFile_AndTooManyRows()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var empty = await Json(await client.PostAsJsonAsync("/api/questions/import/preview", new { csv = "" }));
        Assert.Contains("boş", empty.GetProperty("fatalError").GetString());

        var missing = await Json(await client.PostAsJsonAsync("/api/questions/import/preview", new { csv = "kategori;soru\nx;y" }));
        Assert.Contains("Eksik sütun", missing.GetProperty("fatalError").GetString());

        var many = string.Join("\n", new[] { Header }.Concat(Enumerable.Repeat(CsvLine("x", "yeterince uzun soru metni"), 501)));
        var tooMany = await Json(await client.PostAsJsonAsync("/api/questions/import/preview", new { csv = many }));
        Assert.Contains("en fazla 500", tooMany.GetProperty("fatalError").GetString());

        var commit = await client.PostAsJsonAsync("/api/questions/import/commit", new { csv = "" });
        Assert.Equal(HttpStatusCode.BadRequest, commit.StatusCode);
    }

    [Fact]
    public async Task Import_GeneralMode_CategoryCanBeOmitted_AndColumnOrderIsFree()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var text = Words();
        var csv = $"Soru,Zorluk,Doğru,A,B,C,D,Mod,Kategori\n{text},Orta,c,Bir,İki,Üç,Dört,Genel,\n";

        var preview = await Json(await client.PostAsJsonAsync("/api/questions/import/preview", new { csv }));
        Assert.Equal(1, preview.GetProperty("valid").GetInt32());

        var commit = await Json(await client.PostAsJsonAsync("/api/questions/import/commit", new { csv }));
        Assert.Equal(1, commit.GetProperty("created").GetInt32());
        var list = await Json(await client.GetAsync($"/api/questions?categoryId=1&search={Uri.EscapeDataString(text[..20])}"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(2, list.GetProperty("items")[0].GetProperty("correctIndex").GetInt32());
    }

    // ---- Denetim ----

    [Fact]
    public async Task Writes_AreAuditLogged_WithBeforeAndAfter()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var category = await NewCategoryAsync(client);
        var created = await Json(await client.PostAsJsonAsync("/api/questions", Question(category)));
        var id = created.GetProperty("id").GetInt32();
        await client.PostAsJsonAsync($"/api/questions/{id}/status", new { status = "Published" });

        var logs = await Json(await client.GetAsync("/api/audit-logs?entity=question&pageSize=200"));
        var mine = logs.GetProperty("items").EnumerateArray().Where(l => l.GetProperty("entityId").GetString() == id.ToString()).ToList();
        Assert.Contains(mine, l => l.GetProperty("action").GetString() == "question.create");

        var statusLog = mine.Single(l => l.GetProperty("action").GetString() == "question.status");
        Assert.Contains("Draft", statusLog.GetProperty("before").GetString());
        Assert.Contains("Published", statusLog.GetProperty("after").GetString());
        Assert.StartsWith("api-", statusLog.GetProperty("adminEmail").GetString());
    }
}
