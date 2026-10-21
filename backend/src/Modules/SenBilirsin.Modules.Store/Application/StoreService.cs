using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SenBilirsin.Modules.Store.Data;
using SenBilirsin.Modules.Store.Domain;
using SenBilirsin.Modules.Wallet.Application;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.Store.Application;

/// <summary>Kupon kodlarını AES-256-GCM ile şifreler (docs/07 §5, docs/08 §7). Anahtar <c>Coupons:EncryptionKey</c> (Base64, 32 bayt) ortam değişkeninden gelir.</summary>
public sealed class CouponCipher
{
    private readonly byte[] _key;

    public CouponCipher(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException("Coupons:EncryptionKey tanımlı değil (Base64, 32 bayt). .env.example'a bakın.");
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32) throw new InvalidOperationException("Coupons:EncryptionKey tam olarak 32 bayt (Base64) olmalı.");
    }

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, data, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Decrypt(string protectedText)
    {
        var all = Convert.FromBase64String(protectedText);
        var data = new byte[all.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), data);
        return Encoding.UTF8.GetString(data);
    }

    /// <summary>Kodun yinelenmesini yakalamak için (kodun kendisini açığa çıkarmayan) SHA-256 özeti.</summary>
    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));
}

/// <summary>
/// Aynı satın alma anahtarının (kullanıcı + işlem kimliği) aynı anda iki kez işlenmesini engeller (çift tıklama / ağ tekrarı).
/// Tek instance mimarisi için yeterli; veritabanı benzersiz anahtarı ek güvencedir.
/// </summary>
public sealed class PurchaseLocks
{
    private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        var gate = _stripes[(uint)key.GetHashCode() % _stripes.Length];
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

public sealed record StoreError(string Code, string Message, int StatusCode = 400);

public sealed class StoreResult<T>
{
    public T? Value { get; private init; }
    public StoreError? Error { get; private init; }
    public bool IsSuccess => Error is null;
    public static StoreResult<T> Ok(T value) => new() { Value = value };
    public static StoreResult<T> Fail(string code, string message, int status = 400) => new() { Error = new StoreError(code, message, status) };
}

public sealed record ProductView(int Id, ProductType Type, string Name, string? Description, int Price, string? ImageUrl, bool InStock, bool Owned, string? JokerType, int? JokerAmount);

public sealed record PurchaseResult(long RewardId, int ProductId, int PricePaid, long Balance, bool AlreadyPurchased);

public sealed record RewardView(long Id, int ProductId, string ProductName, ProductType Type, int PricePaid, DateTime CreatedAt, bool Revealed,
    int RevealCount, string IssueStatus, DateTime? IssueReportedAt, bool CanReportIssue, DateTime? ReportDeadline);

public sealed record RevealedCode(long RewardId, string Code, int RevealCount);

/// <summary>
/// Mağaza (docs/07 §5): ürün listesi, atomik satın alma, "Ödüllerim", iki aşamalı kod gösterimi, sorun bildirimi.
/// Bakiye ve joker değişimi yalnızca defter kaydıyla yapılır; her satın alma benzersiz işlem anahtarıyla idempotenttir.
/// </summary>
public sealed class StoreService(StoreDbContext db, CouponCipher cipher, IPointsWallet points, IJokerWallet jokers, TimeProvider clock, PurchaseLocks? locks = null)
{
    /// <summary>Kod gösterildikten sonra sorun bildirme süresi (docs/07 §5).</summary>
    public static readonly TimeSpan IssueWindow = TimeSpan.FromHours(48);

    /// <summary>Ayrılmış bir kodun, ödeme tamamlanmazsa serbest bırakılma süresi.</summary>
    public static readonly TimeSpan ReservationTimeout = TimeSpan.FromMinutes(5);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ================================================================ Liste

    public async Task<IReadOnlyList<ProductView>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Type).ThenBy(p => p.Price).ToListAsync(ct);
        var stock = await db.Coupons.AsNoTracking().Where(c => c.Status == CouponStatus.Available).GroupBy(c => c.ProductId).Select(g => g.Key).ToListAsync(ct);
        var owned = await db.Items.AsNoTracking().Where(i => i.UserId == userId).Select(i => i.ProductId).ToListAsync(ct);

        return products.Select(p => new ProductView(p.Id, p.Type, p.Name, p.Description, p.Price, p.ImageUrl,
            p.Type != ProductType.Coupon || stock.Contains(p.Id), owned.Contains(p.Id),
            p.JokerType is { } j ? j.ToString().ToLowerInvariant() : null, p.JokerAmount)).ToList();
    }

    // ================================================================ Satın alma

    /// <param name="requestId">İstemcinin ürettiği işlem kimliği: aynı kimlikle tekrar gelen istek ikinci kez satın almaz.</param>
    public async Task<StoreResult<PurchaseResult>> PurchaseAsync(Guid userId, int productId, string? requestId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64) return StoreResult<PurchaseResult>.Fail("REQUEST_ID_INVALID", "İşlem kimliği gerekli.");
        var key = $"purchase:{userId:N}:{requestId}";
        using var _ = await (locks ?? new PurchaseLocks()).AcquireAsync(key, ct);

        // Aynı işlem daha önce tamamlandıysa aynı sonuç (çift tıklama / ağ tekrarı)
        var existing = await db.Rewards.AsNoTracking().FirstOrDefaultAsync(r => r.PurchaseKey == key, ct);
        if (existing is not null) return StoreResult<PurchaseResult>.Ok(new PurchaseResult(existing.Id, existing.ProductId, existing.PricePaid, await points.GetBalanceAsync(userId, ct), true));

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null || !product.IsActive) return StoreResult<PurchaseResult>.Fail("PRODUCT_NOT_FOUND", "Ürün bulunamadı.", 404);

        if (product.Type is ProductType.Avatar or ProductType.Frame && await db.Items.AnyAsync(i => i.UserId == userId && i.ProductId == productId, ct))
            return StoreResult<PurchaseResult>.Fail("ALREADY_OWNED", "Bu ürüne zaten sahipsin.", 409);

        return product.Type switch
        {
            ProductType.Coupon => await BuyCouponAsync(userId, product, key, ct),
            _ => await BuyInstantAsync(userId, product, key, ct),
        };
    }

    private async Task<StoreResult<PurchaseResult>> BuyCouponAsync(Guid userId, Product product, string key, CancellationToken ct)
    {
        // 1) Stoktan bir kod AYIR (FOR UPDATE SKIP LOCKED: aynı kodu iki kişi alamaz, bekleme de olmaz)
        long codeId;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var found = await db.Database.SqlQuery<long>($@"
                SELECT id AS ""Value"" FROM store.coupon_codes
                WHERE product_id = {product.Id} AND status = {(int)CouponStatus.Available}
                ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
            if (found.Count == 0) return StoreResult<PurchaseResult>.Fail("OUT_OF_STOCK", "Bu ürünün stoğu tükendi.", 409);

            codeId = found[0];
            await db.Coupons.Where(c => c.Id == codeId).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CouponStatus.Reserved).SetProperty(c => c.ReservedAt, Now).SetProperty(c => c.ReservedForKey, key), ct);
            await tx.CommitAsync(ct);
        }

        // 2) Puanı defterden düş (idempotent anahtar)
        var spend = await points.TrySpendAsync(userId, product.Price, PointsLedgerTypes.Purchase, "product", product.Id.ToString(), key, ct);
        if (spend == SpendOutcome.Insufficient)
        {
            await db.Coupons.Where(c => c.Id == codeId && c.Status == CouponStatus.Reserved).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CouponStatus.Available).SetProperty(c => c.ReservedAt, (DateTime?)null).SetProperty(c => c.ReservedForKey, (string?)null), ct);
            return StoreResult<PurchaseResult>.Fail("INSUFFICIENT_POINTS", "Hediye puanın yetersiz.", 409);
        }

        // 3) Satışı tamamla: kodu kullanıcıya bağla, ödül satırını yaz
        return await CompleteCouponAsync(userId, product, codeId, key, ct);
    }

    private async Task<StoreResult<PurchaseResult>> CompleteCouponAsync(Guid userId, Product product, long codeId, string key, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reward = new UserReward { UserId = userId, ProductId = product.Id, CouponCodeId = codeId, PurchaseKey = key, PricePaid = product.Price, CreatedAt = Now };
        db.Rewards.Add(reward);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(reward).State = EntityState.Detached;
            var again = await db.Rewards.AsNoTracking().FirstAsync(r => r.PurchaseKey == key, ct);
            return StoreResult<PurchaseResult>.Ok(new PurchaseResult(again.Id, again.ProductId, again.PricePaid, await points.GetBalanceAsync(userId, ct), true));
        }

        await db.Coupons.Where(c => c.Id == codeId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, CouponStatus.Sold).SetProperty(c => c.SoldTo, userId).SetProperty(c => c.SoldAt, Now), ct);
        await tx.CommitAsync(ct);
        return StoreResult<PurchaseResult>.Ok(new PurchaseResult(reward.Id, product.Id, product.Price, await points.GetBalanceAsync(userId, ct), false));
    }

    /// <summary>Joker paketi, avatar ve çerçeve: stok yok; puan düşer, sonra ürün teslim edilir (her adım idempotent).</summary>
    private async Task<StoreResult<PurchaseResult>> BuyInstantAsync(Guid userId, Product product, string key, CancellationToken ct)
    {
        var spend = await points.TrySpendAsync(userId, product.Price, PointsLedgerTypes.Purchase, "product", product.Id.ToString(), key, ct);
        if (spend == SpendOutcome.Insufficient) return StoreResult<PurchaseResult>.Fail("INSUFFICIENT_POINTS", "Hediye puanın yetersiz.", 409);

        if (product.Type == ProductType.JokerPack && product.JokerType is { } type && product.JokerAmount is > 0 and var amount)
            await jokers.GrantAsync(userId, type, amount, LedgerTypes.Purchase, product.Id.ToString(), $"store:{key}", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (product.Type is ProductType.Avatar or ProductType.Frame && !await db.Items.AnyAsync(i => i.UserId == userId && i.ProductId == product.Id, ct))
            db.Items.Add(new UserItem { UserId = userId, ProductId = product.Id, CreatedAt = Now });

        var reward = new UserReward { UserId = userId, ProductId = product.Id, PurchaseKey = key, PricePaid = product.Price, CreatedAt = Now };
        db.Rewards.Add(reward);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var again = await db.Rewards.AsNoTracking().FirstAsync(r => r.PurchaseKey == key, ct);
            return StoreResult<PurchaseResult>.Ok(new PurchaseResult(again.Id, again.ProductId, again.PricePaid, await points.GetBalanceAsync(userId, ct), true));
        }

        return StoreResult<PurchaseResult>.Ok(new PurchaseResult(reward.Id, product.Id, product.Price, await points.GetBalanceAsync(userId, ct), false));
    }

    /// <summary>
    /// Yarım kalan satın almaları toparlar (arka plan işi): ayrılalı <see cref="ReservationTimeout"/> geçmiş kodlar için,
    /// puan zaten düşülmüşse satış tamamlanır, düşülmemişse kod serbest bırakılır.
    /// </summary>
    public async Task<int> RecoverStaleReservationsAsync(CancellationToken ct = default)
    {
        var cutoff = Now - ReservationTimeout;
        var stale = await db.Coupons.Where(c => c.Status == CouponStatus.Reserved && c.ReservedAt < cutoff).ToListAsync(ct);
        foreach (var code in stale)
        {
            var key = code.ReservedForKey!;
            var done = await db.Rewards.AsNoTracking().FirstOrDefaultAsync(r => r.PurchaseKey == key, ct);
            if (done is not null)
            {
                // Satış başka bir kodla tamamlanmış; bu kod artıktan ayrılmış kalmış → serbest bırak
                if (done.CouponCodeId != code.Id)
                {
                    code.Status = CouponStatus.Available;
                    code.ReservedAt = null;
                    code.ReservedForKey = null;
                    await db.SaveChangesAsync(ct);
                }

                continue;
            }

            if (await points.HasEntryAsync(key, ct))
            {
                // Puan düşülmüş ama satış kaydı yazılamamış: kullanıcıya teslim et
                var userId = Guid.Parse(key.Split(':')[1]);
                var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == code.ProductId, ct);
                await CompleteCouponAsync(userId, product, code.Id, key, ct);
            }
            else
            {
                code.Status = CouponStatus.Available;
                code.ReservedAt = null;
                code.ReservedForKey = null;
                await db.SaveChangesAsync(ct);
            }
        }

        return stale.Count;
    }

    // ================================================================ Ödüllerim

    public async Task<IReadOnlyList<RewardView>> RewardsAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await (from r in db.Rewards.AsNoTracking() join p in db.Products.AsNoTracking() on r.ProductId equals p.Id
                          where r.UserId == userId orderby r.Id descending select new { r, p }).ToListAsync(ct);
        return rows.Select(x => ToView(x.r, x.p)).ToList();
    }

    private RewardView ToView(UserReward r, Product p)
    {
        var deadline = r.RevealedAt?.Add(IssueWindow);
        var canReport = p.Type == ProductType.Coupon && r.RevealedAt is not null && r.IssueStatus == IssueStatus.None && Now <= deadline;
        return new RewardView(r.Id, r.ProductId, p.Name, p.Type, r.PricePaid, r.CreatedAt, r.RevealedAt is not null, r.RevealCount,
            r.IssueStatus.ToString().ToLowerInvariant(), r.IssueReportedAt, canReport, deadline);
    }

    /// <summary>Kodu gösterir (iki aşamalı arayüzün ikinci adımı). İlk gösterim zamanı 48 saatlik sorun bildirme süresini başlatır; her gösterim sayılır.</summary>
    public async Task<StoreResult<RevealedCode>> RevealAsync(Guid userId, long rewardId, CancellationToken ct = default)
    {
        var reward = await db.Rewards.FirstOrDefaultAsync(r => r.Id == rewardId && r.UserId == userId, ct);
        if (reward is null) return StoreResult<RevealedCode>.Fail("REWARD_NOT_FOUND", "Ödül bulunamadı.", 404);
        if (reward.CouponCodeId is not { } codeId) return StoreResult<RevealedCode>.Fail("NO_CODE", "Bu ödülün gösterilecek bir kodu yok.", 409);

        var code = await db.Coupons.AsNoTracking().FirstAsync(c => c.Id == codeId, ct);
        reward.RevealedAt ??= Now;
        reward.RevealCount++;
        await db.SaveChangesAsync(ct);
        return StoreResult<RevealedCode>.Ok(new RevealedCode(reward.Id, cipher.Decrypt(code.CodeEncrypted), reward.RevealCount));
    }

    /// <summary>"Kod çalışmıyor": gösterimden sonra 48 saat içinde, ödül başına bir kez (docs/07 §5).</summary>
    public async Task<StoreResult<bool>> ReportIssueAsync(Guid userId, long rewardId, string? text, string? screenshotUrl, CancellationToken ct = default)
    {
        var reward = await db.Rewards.FirstOrDefaultAsync(r => r.Id == rewardId && r.UserId == userId, ct);
        if (reward is null) return StoreResult<bool>.Fail("REWARD_NOT_FOUND", "Ödül bulunamadı.", 404);
        if (reward.CouponCodeId is null) return StoreResult<bool>.Fail("NO_CODE", "Bu ödülde kod yok.", 409);
        if (reward.RevealedAt is null) return StoreResult<bool>.Fail("NOT_REVEALED", "Önce kodu göstermelisin.", 409);
        if (Now > reward.RevealedAt.Value.Add(IssueWindow)) return StoreResult<bool>.Fail("ISSUE_WINDOW_CLOSED", "Sorun bildirme süresi (48 saat) doldu.", 409);
        if (reward.IssueStatus != IssueStatus.None) return StoreResult<bool>.Fail("ISSUE_ALREADY_REPORTED", "Bu ödül için zaten bir sorun bildirdin.", 409);
        var description = (text ?? "").Trim();
        if (description.Length is < 5 or > 500) return StoreResult<bool>.Fail("TEXT_INVALID", "Sorunu 5–500 karakterle anlat.");

        reward.IssueStatus = IssueStatus.Open;
        reward.IssueReportedAt = Now;
        reward.IssueText = description;
        reward.IssueScreenshotUrl = string.IsNullOrWhiteSpace(screenshotUrl) ? null : screenshotUrl.Trim();
        await db.SaveChangesAsync(ct);
        return StoreResult<bool>.Ok(true);
    }

    // ================================================================ Yönetim

    public async Task<IReadOnlyList<UserReward>> OpenIssuesAsync(CancellationToken ct = default) =>
        await db.Rewards.AsNoTracking().Where(r => r.IssueStatus == IssueStatus.Open).OrderBy(r => r.IssueReportedAt).ToListAsync(ct);

    /// <summary>
    /// Sorun çözümü: yeni kod verilir (eski kod geçersiz sayılır, kullanıcı yeniden göstermeli) ya da talep reddedilir.
    /// Kullanıcı başına ayda en fazla 1 değişim (docs/07 §5).
    /// </summary>
    public async Task<StoreResult<bool>> ResolveIssueAsync(long rewardId, bool replace, CancellationToken ct = default)
    {
        var reward = await db.Rewards.FirstOrDefaultAsync(r => r.Id == rewardId, ct);
        if (reward is null) return StoreResult<bool>.Fail("REWARD_NOT_FOUND", "Ödül bulunamadı.", 404);
        if (reward.IssueStatus != IssueStatus.Open) return StoreResult<bool>.Fail("NO_OPEN_ISSUE", "Bu ödülde açık bir sorun yok.", 409);

        if (!replace)
        {
            reward.IssueStatus = IssueStatus.Rejected;
            await db.SaveChangesAsync(ct);
            return StoreResult<bool>.Ok(true);
        }

        var monthStart = new DateTime(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        if (await db.Rewards.AnyAsync(r => r.UserId == reward.UserId && r.LastReplacedAt >= monthStart, ct))
            return StoreResult<bool>.Fail("MONTHLY_LIMIT", "Bu kullanıcı bu ay zaten bir kod değişimi aldı.", 409);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var found = await db.Database.SqlQuery<long>($@"
            SELECT id AS ""Value"" FROM store.coupon_codes
            WHERE product_id = {reward.ProductId} AND status = {(int)CouponStatus.Available}
            ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        if (found.Count == 0) return StoreResult<bool>.Fail("OUT_OF_STOCK", "Değişim için stokta kod yok.", 409);

        var oldId = reward.CouponCodeId;
        await db.Coupons.Where(c => c.Id == found[0]).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, CouponStatus.Sold).SetProperty(c => c.SoldTo, reward.UserId).SetProperty(c => c.SoldAt, Now), ct);
        if (oldId is { } old)
            await db.Coupons.Where(c => c.Id == old).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CouponStatus.Invalid).SetProperty(c => c.Note, "Kullanıcı bildirimi üzerine değiştirildi"), ct);

        reward.CouponCodeId = found[0];
        reward.ReplacedCount++;
        reward.LastReplacedAt = Now;
        reward.IssueStatus = IssueStatus.Resolved;
        reward.RevealedAt = null; // yeni kod yeniden gösterilmeli
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StoreResult<bool>.Ok(true);
    }
}
