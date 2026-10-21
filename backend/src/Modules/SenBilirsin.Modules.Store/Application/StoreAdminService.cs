using Microsoft.EntityFrameworkCore;
using SenBilirsin.Modules.Store.Data;
using SenBilirsin.Modules.Store.Domain;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.Store.Application;

public sealed record ProductInput(ProductType Type, string? Name, string? Description, int Price, string? ImageUrl, bool IsActive,
    JokerType? JokerType, int? JokerAmount, int? ItemRefId);

public sealed record AdminProduct(int Id, ProductType Type, string Name, string? Description, int Price, string? ImageUrl, bool IsActive,
    JokerType? JokerType, int? JokerAmount, int? ItemRefId, int Available, int Sold);

public sealed record CodeUploadResult(int Added, int Duplicates, int Invalid);

public sealed record AdminIssue(long RewardId, Guid UserId, int ProductId, string ProductName, string? Text, string? ScreenshotUrl, DateTime ReportedAt, int UserRewardCount, int UserIssueCount);

/// <summary>Yönetim paneli için mağaza işlemleri: ürünler, kupon stoğu (şifreli yükleme), sorun kuyruğu (docs/05, docs/07 §5).</summary>
public sealed class StoreAdminService(StoreDbContext db, CouponCipher cipher, TimeProvider clock)
{
    public const int MaxCodeLength = 200;

    public async Task<IReadOnlyList<AdminProduct>> ListAsync(CancellationToken ct = default)
    {
        var products = await db.Products.AsNoTracking().OrderBy(p => p.Type).ThenBy(p => p.Id).ToListAsync(ct);
        var stock = await db.Coupons.AsNoTracking().GroupBy(c => new { c.ProductId, c.Status }).Select(g => new { g.Key.ProductId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        return products.Select(p => new AdminProduct(p.Id, p.Type, p.Name, p.Description, p.Price, p.ImageUrl, p.IsActive, p.JokerType, p.JokerAmount, p.ItemRefId,
            stock.Where(s => s.ProductId == p.Id && s.Status == CouponStatus.Available).Sum(s => s.Count),
            stock.Where(s => s.ProductId == p.Id && s.Status == CouponStatus.Sold).Sum(s => s.Count))).ToList();
    }

    public async Task<StoreResult<AdminProduct>> CreateAsync(ProductInput input, CancellationToken ct = default)
    {
        var problem = Validate(input);
        if (problem is not null) return StoreResult<AdminProduct>.Fail("PRODUCT_INVALID", problem);
        var product = new Product { CreatedAt = clock.GetUtcNow().UtcDateTime };
        Apply(product, input);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return StoreResult<AdminProduct>.Ok((await ListAsync(ct)).First(p => p.Id == product.Id));
    }

    public async Task<StoreResult<AdminProduct>> UpdateAsync(int id, ProductInput input, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return StoreResult<AdminProduct>.Fail("PRODUCT_NOT_FOUND", "Ürün bulunamadı.", 404);
        if (input.Type != product.Type) return StoreResult<AdminProduct>.Fail("PRODUCT_INVALID", "Ürün türü sonradan değiştirilemez.");
        var problem = Validate(input);
        if (problem is not null) return StoreResult<AdminProduct>.Fail("PRODUCT_INVALID", problem);
        Apply(product, input);
        await db.SaveChangesAsync(ct);
        return StoreResult<AdminProduct>.Ok((await ListAsync(ct)).First(p => p.Id == id));
    }

    private static string? Validate(ProductInput i)
    {
        if (!Enum.IsDefined(i.Type)) return "Ürün türü geçersiz.";
        var name = (i.Name ?? "").Trim();
        if (name.Length is < 2 or > 80) return "Ürün adı 2–80 karakter olmalı.";
        if (i.Price is < 1 or > 1_000_000) return "Fiyat 1–1.000.000 hediye puanı arasında olmalı.";
        if (i.Type == ProductType.JokerPack && (i.JokerType is null || !Enum.IsDefined(i.JokerType.Value) || i.JokerAmount is null or < 1 or > 1000))
            return "Joker paketinde joker türü ve 1–1000 arası adet gerekli.";
        if (i.Type is ProductType.Avatar or ProductType.Frame && i.ItemRefId is null or < 1)
            return "Avatar/çerçeve ürününde ilgili avatar/çerçeve kimliği gerekli.";
        return null;
    }

    private static void Apply(Product p, ProductInput i)
    {
        p.Type = i.Type;
        p.Name = i.Name!.Trim();
        p.Description = string.IsNullOrWhiteSpace(i.Description) ? null : i.Description.Trim();
        p.Price = i.Price;
        p.ImageUrl = string.IsNullOrWhiteSpace(i.ImageUrl) ? null : i.ImageUrl.Trim();
        p.IsActive = i.IsActive;
        p.JokerType = i.Type == ProductType.JokerPack ? i.JokerType : null;
        p.JokerAmount = i.Type == ProductType.JokerPack ? i.JokerAmount : null;
        p.ItemRefId = i.Type is ProductType.Avatar or ProductType.Frame ? i.ItemRefId : null;
    }

    /// <summary>Kupon kodlarını yükler (satır başına bir kod; CSV/yapıştırma). Kodlar şifrelenir; aynı ürüne aynı kod ikinci kez eklenmez.</summary>
    public async Task<StoreResult<CodeUploadResult>> AddCodesAsync(int productId, IEnumerable<string?> codes, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return StoreResult<CodeUploadResult>.Fail("PRODUCT_NOT_FOUND", "Ürün bulunamadı.", 404);
        if (product.Type != ProductType.Coupon) return StoreResult<CodeUploadResult>.Fail("NOT_A_COUPON", "Kod yalnızca kupon ürününe yüklenir.", 409);

        var existing = (await db.Coupons.AsNoTracking().Where(c => c.ProductId == productId).Select(c => c.CodeHash).ToListAsync(ct)).ToHashSet();
        int added = 0, duplicates = 0, invalid = 0;
        foreach (var raw in codes)
        {
            var code = (raw ?? "").Trim();
            if (code.Length is 0 or > MaxCodeLength) { invalid++; continue; }
            var hash = CouponCipher.Hash(code);
            if (!existing.Add(hash)) { duplicates++; continue; }
            db.Coupons.Add(new CouponCode { ProductId = productId, CodeEncrypted = cipher.Encrypt(code), CodeHash = hash, Status = CouponStatus.Available });
            added++;
        }

        await db.SaveChangesAsync(ct);
        return StoreResult<CodeUploadResult>.Ok(new CodeUploadResult(added, duplicates, invalid));
    }

    /// <summary>Açık "kod çalışmıyor" bildirimleri; kullanıcı başına aldığı kod / bildirdiği sorun oranı yanında gösterilir (tekrar eden itirazcılar için).</summary>
    public async Task<IReadOnlyList<AdminIssue>> IssuesAsync(CancellationToken ct = default)
    {
        var open = await (from r in db.Rewards.AsNoTracking() join p in db.Products.AsNoTracking() on r.ProductId equals p.Id
                          where r.IssueStatus == IssueStatus.Open orderby r.IssueReportedAt select new { r, p.Name }).ToListAsync(ct);
        var users = open.Select(o => o.r.UserId).Distinct().ToList();
        var counts = await db.Rewards.AsNoTracking().Where(r => users.Contains(r.UserId) && r.CouponCodeId != null)
            .GroupBy(r => r.UserId).Select(g => new { g.Key, Total = g.Count(), Issues = g.Count(x => x.IssueStatus != IssueStatus.None) }).ToListAsync(ct);
        return open.Select(o =>
        {
            var c = counts.FirstOrDefault(x => x.Key == o.r.UserId);
            return new AdminIssue(o.r.Id, o.r.UserId, o.r.ProductId, o.Name, o.r.IssueText, o.r.IssueScreenshotUrl, o.r.IssueReportedAt ?? o.r.CreatedAt, c?.Total ?? 0, c?.Issues ?? 0);
        }).ToList();
    }
}
