using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Catalog.Domain;

namespace SenBilirsin.Modules.Catalog.Application;

public sealed record CatalogError(string Code, string Message, int StatusCode = 400);

public sealed class CatalogResult<T>
{
    public T? Value { get; private init; }
    public CatalogError? Error { get; private init; }
    public bool IsSuccess => Error is null;

    public static CatalogResult<T> Ok(T value) => new() { Value = value };
    public static CatalogResult<T> Fail(string code, string message, int status = 400) => new() { Error = new CatalogError(code, message, status) };
}

public sealed record CategoryInput(int ModeId, string? Name, string? ImageUrl, string? ColorHex, int? Order = null);

public sealed record AdminCategory(int Id, int ModeId, string ModeCode, string Name, string? ImageUrl, string? ColorHex,
    int Order, bool IsPublished);

/// <summary>Yönetim paneli için kategori işlemleri (docs/05 §2). Yayın kararı adminindir; soru sayısına bağlı engel yoktur.</summary>
public sealed partial class CatalogAdminService(CatalogDbContext db, TimeProvider clock)
{
    public const int NameMin = 2;
    public const int NameMax = 40;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public async Task<IReadOnlyList<AdminCategory>> ListAsync(CancellationToken ct = default) =>
        await (from c in db.Categories.AsNoTracking()
               join m in db.Modes.AsNoTracking() on c.ModeId equals m.Id
               orderby m.Order, c.Order, c.Id
               select new AdminCategory(c.Id, c.ModeId, m.Code, c.Name, c.ImageUrl, c.ColorHex, c.Order, c.IsPublished))
            .ToListAsync(ct);

    public async Task<CatalogResult<AdminCategory>> CreateAsync(CategoryInput input, CancellationToken ct = default)
    {
        var mode = await db.Modes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == input.ModeId, ct);
        if (mode is null) return CatalogResult<AdminCategory>.Fail("MODE_NOT_FOUND", "Mod bulunamadı.", 404);
        if (mode.Code == ModeCodes.General)
            return CatalogResult<AdminCategory>.Fail("GENERAL_SINGLE_CATEGORY", "Genel modun tek bir kategorisi vardır; yenisi eklenemez.");

        var problem = Validate(input);
        if (problem is not null) return CatalogResult<AdminCategory>.Fail(problem.Value.Code, problem.Value.Message);

        var name = input.Name!.Trim();
        if (await db.Categories.AnyAsync(c => c.ModeId == mode.Id && c.Name == name, ct))
            return CatalogResult<AdminCategory>.Fail("CATEGORY_NAME_TAKEN", "Bu modda aynı adlı bir kategori zaten var.", 409);

        var order = input.Order ?? (await db.Categories.Where(c => c.ModeId == mode.Id).MaxAsync(c => (int?)c.Order, ct) ?? 0) + 1;
        var category = new Category
        {
            ModeId = mode.Id,
            Name = name,
            ImageUrl = Clean(input.ImageUrl),
            ColorHex = Clean(input.ColorHex),
            Order = order,
            IsPublished = false, // yayın ayrı bir adımdır
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return CatalogResult<AdminCategory>.Ok(ToAdmin(category, mode.Code));
    }

    public async Task<CatalogResult<AdminCategory>> UpdateAsync(int id, CategoryInput input, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return CatalogResult<AdminCategory>.Fail("CATEGORY_NOT_FOUND", "Kategori bulunamadı.", 404);

        var problem = Validate(input);
        if (problem is not null) return CatalogResult<AdminCategory>.Fail(problem.Value.Code, problem.Value.Message);

        var name = input.Name!.Trim();
        if (id == CatalogDbContext.GeneralCategoryId && name != category.Name)
            return CatalogResult<AdminCategory>.Fail("GENERAL_RENAME", "Genel kategorisinin adı değiştirilemez.");
        if (name != category.Name && await db.Categories.AnyAsync(c => c.ModeId == category.ModeId && c.Name == name && c.Id != id, ct))
            return CatalogResult<AdminCategory>.Fail("CATEGORY_NAME_TAKEN", "Bu modda aynı adlı bir kategori zaten var.", 409);

        category.Name = name;
        category.ImageUrl = Clean(input.ImageUrl);
        category.ColorHex = Clean(input.ColorHex);
        if (input.Order is { } order) category.Order = order;
        await db.SaveChangesAsync(ct);

        var modeCode = await db.Modes.Where(m => m.Id == category.ModeId).Select(m => m.Code).FirstAsync(ct);
        return CatalogResult<AdminCategory>.Ok(ToAdmin(category, modeCode));
    }

    public async Task<CatalogResult<AdminCategory>> SetPublishedAsync(int id, bool published, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return CatalogResult<AdminCategory>.Fail("CATEGORY_NOT_FOUND", "Kategori bulunamadı.", 404);

        category.IsPublished = published;
        await db.SaveChangesAsync(ct);
        var modeCode = await db.Modes.Where(m => m.Id == category.ModeId).Select(m => m.Code).FirstAsync(ct);
        return CatalogResult<AdminCategory>.Ok(ToAdmin(category, modeCode));
    }

    private static (string Code, string Message)? Validate(CategoryInput input)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length < NameMin) return ("NAME_TOO_SHORT", $"Kategori adı en az {NameMin} karakter olmalı.");
        if (name.Length > NameMax) return ("NAME_TOO_LONG", $"Kategori adı en fazla {NameMax} karakter olabilir.");
        if (Clean(input.ColorHex) is { } color && !HexColor().IsMatch(color))
            return ("COLOR_INVALID", "Renk #RRGGBB biçiminde olmalı (örn. #1E88E5).");
        if (Clean(input.ImageUrl) is { Length: > 512 })
            return ("IMAGE_URL_TOO_LONG", "Görsel adresi çok uzun.");
        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AdminCategory ToAdmin(Category c, string modeCode) =>
        new(c.Id, c.ModeId, modeCode, c.Name, c.ImageUrl, c.ColorHex, c.Order, c.IsPublished);
}
