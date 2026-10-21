using Microsoft.EntityFrameworkCore;
using Npgsql;
using SenBilirsin.Modules.Wallet.Data;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.Wallet.Application;

public sealed record PointsHistoryItem(long Id, long Amount, string Type, string? RefType, string? RefId, string? Note, DateTime CreatedAt);

/// <summary>Diğer modüllerin kullandığı hediye puanı cüzdanı arayüzü (docs/07 §4).</summary>
public interface IPointsWallet
{
    Task<long> GetBalanceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Bu işlem anahtarıyla defterde kayıt var mı (yarım kalan işlemleri toparlamak için).</summary>
    Task<bool> HasEntryAsync(string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<PointsHistoryItem>> GetHistoryAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Puan ekler (turnuva ödülü, iade, admin). Aynı anahtar ikinci kez eklemez; eklendiyse true.</summary>
    Task<bool> GrantAsync(Guid userId, long amount, string type, string? refType, string? refId, string idempotencyKey, string? note = null, CancellationToken ct = default);

    /// <summary>Bakiye yeterliyse puan düşer (mağaza). Aynı anahtar ikinci kez düşmez.</summary>
    Task<SpendOutcome> TrySpendAsync(Guid userId, long amount, string type, string? refType, string? refId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Yönetici düzeltmesi: eksi bakiyeye izin verir (örn. hile iadesi). Gerekçe zorunlu.</summary>
    Task<bool> AdjustAsync(Guid userId, long amount, string idempotencyKey, string reason, CancellationToken ct = default);
}

/// <summary>
/// Hediye puanı defteri: yalnızca eklenir; bakiye satırların toplamıdır; <c>idempotency_key</c> benzersizdir (mimari ilke 2).
/// Harcama kullanıcı başına kilitlenerek yapılır (aynı anda iki harcama bakiyeyi aşamaz).
/// </summary>
public sealed class PointsWallet(WalletDbContext db, TimeProvider clock) : IPointsWallet
{
    public async Task<long> GetBalanceAsync(Guid userId, CancellationToken ct = default) =>
        await db.PointsLedger.AsNoTracking().Where(x => x.UserId == userId).SumAsync(x => (long?)x.Amount, ct) ?? 0;

    public Task<bool> HasEntryAsync(string idempotencyKey, CancellationToken ct = default) =>
        db.PointsLedger.AsNoTracking().AnyAsync(x => x.IdempotencyKey == idempotencyKey, ct);

    public async Task<IReadOnlyList<PointsHistoryItem>> GetHistoryAsync(Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return await db.PointsLedger.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.Id)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(x => new PointsHistoryItem(x.Id, x.Amount, x.Type, x.RefType, x.RefId, x.Note, x.CreatedAt)).ToListAsync(ct);
    }

    public async Task<bool> GrantAsync(Guid userId, long amount, string type, string? refType, string? refId, string idempotencyKey, string? note = null, CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Ekleme miktarı pozitif olmalı.");
        return await TryInsertAsync(NewEntry(userId, amount, type, refType, refId, idempotencyKey, note), ct);
    }

    public async Task<SpendOutcome> TrySpendAsync(Guid userId, long amount, string type, string? refType, string? refId, string idempotencyKey, CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Harcama miktarı pozitif olmalı.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey(userId)})", ct);

        if (await db.PointsLedger.AnyAsync(x => x.IdempotencyKey == idempotencyKey, ct)) return SpendOutcome.AlreadySpent;
        var balance = await db.PointsLedger.Where(x => x.UserId == userId).SumAsync(x => (long?)x.Amount, ct) ?? 0;
        if (balance < amount) return SpendOutcome.Insufficient;

        db.PointsLedger.Add(NewEntry(userId, -amount, type, refType, refId, idempotencyKey, null));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return SpendOutcome.Spent;
    }

    public async Task<bool> AdjustAsync(Guid userId, long amount, string idempotencyKey, string reason, CancellationToken ct = default)
    {
        if (amount == 0) throw new ArgumentOutOfRangeException(nameof(amount), "Düzeltme miktarı sıfır olamaz.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Gerekçe zorunlu.", nameof(reason));
        return await TryInsertAsync(NewEntry(userId, amount, PointsLedgerTypes.Admin, null, null, idempotencyKey, reason.Trim()), ct);
    }

    private async Task<bool> TryInsertAsync(PointsLedgerEntry entry, CancellationToken ct)
    {
        if (await db.PointsLedger.AnyAsync(x => x.IdempotencyKey == entry.IdempotencyKey, ct)) return false;
        db.PointsLedger.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    private PointsLedgerEntry NewEntry(Guid userId, long amount, string type, string? refType, string? refId, string key, string? note) => new()
    {
        UserId = userId, Amount = amount, Type = type, RefType = refType, RefId = refId, IdempotencyKey = key, Note = note,
        CreatedAt = clock.GetUtcNow().UtcDateTime,
    };

    private static long LockKey(Guid userId) => BitConverter.ToInt64(userId.ToByteArray(), 0);
}
