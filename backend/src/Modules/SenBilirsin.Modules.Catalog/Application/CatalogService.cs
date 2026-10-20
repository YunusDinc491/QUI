using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Catalog.Data;
using SenBilirsin.Modules.Catalog.Domain;

namespace SenBilirsin.Modules.Catalog.Application;

public sealed record CategoryInfo(int Id, int ModeId, string ModeCode, string Name, string? ImageUrl, string? ColorHex, bool IsPublished);

/// <summary>Diğer modüllerin kullanacağı dar okuma arayüzü (modüller birbirinin tablosuna dokunmaz).</summary>
public interface ICatalogQueries
{
    Task<CategoryInfo?> GetCategoryAsync(int categoryId, CancellationToken ct = default);

    /// <summary>Tüm kategoriler (yayında olmayanlar dahil), mod koduyla; içe aktarma gibi yönetim işleri için.</summary>
    Task<IReadOnlyList<CategoryInfo>> GetAllCategoriesAsync(CancellationToken ct = default);

    /// <summary>Verilen moddaki yayında olan kategori kimlikleri ("Karışık" için).</summary>
    Task<IReadOnlyList<int>> GetPublishedCategoryIdsAsync(string modeCode, CancellationToken ct = default);
}

public sealed record PublicCategory(int Id, string Name, string? ImageUrl, string? ColorHex);

public sealed record PublicMode(int Id, string Code, string Name, bool MixedAvailable, IReadOnlyList<PublicCategory> Categories);

public sealed class CatalogService(CatalogDbContext db) : ICatalogQueries
{
    /// <summary>Mobil uygulamanın göreceği liste: yalnızca yayındaki kategoriler; kategorisi olmayan mod boş listeyle gelir.</summary>
    public async Task<IReadOnlyList<PublicMode>> GetPublicCatalogAsync(CancellationToken ct = default)
    {
        var modes = await db.Modes.AsNoTracking().OrderBy(m => m.Order).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().Where(c => c.IsPublished)
            .OrderBy(c => c.Order).ThenBy(c => c.Name).ToListAsync(ct);

        return modes.Select(m =>
        {
            var list = categories.Where(c => c.ModeId == m.Id)
                .Select(c => new PublicCategory(c.Id, c.Name, c.ImageUrl, c.ColorHex)).ToList();
            // "Karışık": genel modda gerek yok (tek kategori); diğerlerinde en az 2 yayında alt dal varsa anlamlı
            var mixed = m.Code != ModeCodes.General && list.Count >= 2;
            return new PublicMode(m.Id, m.Code, m.Name, mixed, list);
        }).ToList();
    }

    public async Task<CategoryInfo?> GetCategoryAsync(int categoryId, CancellationToken ct = default) =>
        await (from c in db.Categories.AsNoTracking()
               join m in db.Modes.AsNoTracking() on c.ModeId equals m.Id
               where c.Id == categoryId
               select new CategoryInfo(c.Id, c.ModeId, m.Code, c.Name, c.ImageUrl, c.ColorHex, c.IsPublished))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CategoryInfo>> GetAllCategoriesAsync(CancellationToken ct = default) =>
        await (from c in db.Categories.AsNoTracking()
               join m in db.Modes.AsNoTracking() on c.ModeId equals m.Id
               orderby m.Order, c.Order, c.Id
               select new CategoryInfo(c.Id, c.ModeId, m.Code, c.Name, c.ImageUrl, c.ColorHex, c.IsPublished))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> GetPublishedCategoryIdsAsync(string modeCode, CancellationToken ct = default) =>
        await (from c in db.Categories.AsNoTracking()
               join m in db.Modes.AsNoTracking() on c.ModeId equals m.Id
               where m.Code == modeCode && c.IsPublished
               orderby c.Order, c.Id
               select c.Id).ToListAsync(ct);
}
