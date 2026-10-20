using Microsoft.EntityFrameworkCore;
using Npgsql;
using SenBilirsin.Modules.Wallet.Data;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.Wallet.Application;

public enum SpendOutcome
{
    /// <summary>Joker bu çağrıyla harcandı.</summary>
    Spent,

    /// <summary>Aynı işlem kimliğiyle daha önce harcanmıştı; tekrar düşülmedi (idempotent).</summary>
    AlreadySpent,

    /// <summary>Bakiye yetersiz.</summary>
    Insufficient,
}

/// <summary>Diğer modüllerin kullanacağı joker cüzdanı arayüzü.</summary>
public interface IJokerWallet
{
    Task<IReadOnlyDictionary<JokerType, int>> GetBalancesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Bakiye yeterliyse 1 joker düşer. Aynı <paramref name="idempotencyKey"/> ikinci kez düşmez.</summary>
    Task<SpendOutcome> TrySpendAsync(Guid userId, JokerType type, string refId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Joker ekler (reklam, mağaza, admin, iade). Aynı anahtar ikinci kez eklemez; eklendiyse true.</summary>
    Task<bool> GrantAsync(Guid userId, JokerType type, int amount, string ledgerType, string? refId, string idempotencyKey, CancellationToken ct = default);
}

/// <summary>
/// Joker defteri işlemleri (docs/07, mimari ilke 2): bakiye yalnızca defter toplamıdır; her kayıt benzersiz işlem
/// anahtarına sahiptir; harcama kullanıcı başına kilitlenerek yapılır (aynı anda iki harcama bakiyeyi aşamaz).
/// Yeni hesaba her jokerden <see cref="WelcomeGiftPerType"/> adet hediye, ilk bakiye sorgusunda bir kez verilir.
/// </summary>
public sealed class JokerWallet(WalletDbContext db, TimeProvider clock) : IJokerWallet
{
    public const int WelcomeGiftPerType = 5;

    public async Task<IReadOnlyDictionary<JokerType, int>> GetBalancesAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureWelcomeGiftAsync(userId, ct);
        return await BalancesAsync(userId, ct);
    }

    public async Task<SpendOutcome> TrySpendAsync(Guid userId, JokerType type, string refId, string idempotencyKey, CancellationToken ct = default)
    {
        await EnsureWelcomeGiftAsync(userId, ct);

        // Kullanıcı başına kilit: bakiye kontrolü ile düşme arasına başka harcama giremez
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey(userId)})", ct);

        if (await db.JokerLedger.AnyAsync(x => x.IdempotencyKey == idempotencyKey, ct))
            return SpendOutcome.AlreadySpent;

        var balance = await db.JokerLedger.Where(x => x.UserId == userId && x.JokerType == type).SumAsync(x => (int?)x.Delta, ct) ?? 0;
        if (balance < 1) return SpendOutcome.Insufficient;

        db.JokerLedger.Add(NewEntry(userId, type, -1, LedgerTypes.Use, refId, idempotencyKey));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return SpendOutcome.Spent;
    }

    public async Task<bool> GrantAsync(Guid userId, JokerType type, int amount, string ledgerType, string? refId, string idempotencyKey, CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Ekleme miktarı pozitif olmalı.");
        return await TryInsertAsync(NewEntry(userId, type, amount, ledgerType, refId, idempotencyKey), ct);
    }

    private async Task EnsureWelcomeGiftAsync(Guid userId, CancellationToken ct)
    {
        foreach (var type in Enum.GetValues<JokerType>())
            await TryInsertAsync(NewEntry(userId, type, WelcomeGiftPerType, LedgerTypes.Gift, "welcome", $"gift:welcome:{userId:N}:{(int)type}"), ct);
    }

    /// <summary>Satırı ekler; aynı anahtar zaten varsa (benzersizlik ihlali) false döner.</summary>
    private async Task<bool> TryInsertAsync(JokerLedgerEntry entry, CancellationToken ct)
    {
        if (await db.JokerLedger.AnyAsync(x => x.IdempotencyKey == entry.IdempotencyKey, ct)) return false;
        db.JokerLedger.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı anda başka istek aynı anahtarı yazdı: sonuç aynı, tekrar eklenmedi
            db.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    private async Task<IReadOnlyDictionary<JokerType, int>> BalancesAsync(Guid userId, CancellationToken ct)
    {
        var sums = await db.JokerLedger.AsNoTracking().Where(x => x.UserId == userId)
            .GroupBy(x => x.JokerType).Select(g => new { g.Key, Sum = g.Sum(x => x.Delta) }).ToListAsync(ct);
        return Enum.GetValues<JokerType>().ToDictionary(t => t, t => sums.FirstOrDefault(s => s.Key == t)?.Sum ?? 0);
    }

    private JokerLedgerEntry NewEntry(Guid userId, JokerType type, int delta, string ledgerType, string? refId, string key) => new()
    {
        UserId = userId,
        JokerType = type,
        Delta = delta,
        Type = ledgerType,
        RefId = refId,
        IdempotencyKey = key,
        CreatedAt = clock.GetUtcNow().UtcDateTime,
    };

    /// <summary>Kullanıcı kimliğinden 64 bitlik danışma kilidi anahtarı.</summary>
    private static long LockKey(Guid userId) => BitConverter.ToInt64(userId.ToByteArray(), 0);
}
