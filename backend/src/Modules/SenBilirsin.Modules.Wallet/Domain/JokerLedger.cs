namespace SenBilirsin.Modules.Wallet.Domain;

/// <summary>Joker türleri (docs/01 §10). Sayısal değerler veritabanında saklanır; değiştirilmez.</summary>
public enum JokerType
{
    /// <summary>Yarı yarıya: 2 yanlış şık elenir.</summary>
    Half = 1,

    /// <summary>Çift cevap: iki şık işaretlenir, biri doğruysa doğru sayılır.</summary>
    Double = 2,

    /// <summary>İpucu: olasılıkla doğru şıkkı, değilse yanlış bir şıkkı gösterir.</summary>
    Hint = 3,
}

/// <summary>Defter kaydı türleri (docs/03 <c>joker_ledger.type</c>).</summary>
public static class LedgerTypes
{
    public const string Gift = "gift";
    public const string Ad = "ad";
    public const string Purchase = "purchase";
    public const string Use = "use";
    public const string Admin = "admin";
    public const string Refund = "refund";
}

/// <summary>
/// Joker defteri satırı. Bakiye = bir kullanıcının satırlarının <see cref="Delta"/> toplamıdır; bakiye ayrıca saklanmaz
/// (mimari ilke 2). <see cref="IdempotencyKey"/> benzersizdir: aynı işlem iki kez yazılamaz.
/// </summary>
public class JokerLedgerEntry
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public JokerType JokerType { get; set; }

    /// <summary>+ ekleme, − harcama.</summary>
    public int Delta { get; set; }

    public string Type { get; set; } = LedgerTypes.Gift;

    /// <summary>İlgili kayıt (örn. maç kimliği, reklam niyeti).</summary>
    public string? RefId { get; set; }

    public string IdempotencyKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public static class PointsLedgerTypes
{
    public const string Tournament = "tournament";
    public const string Purchase = "purchase";
    public const string Refund = "refund";
    public const string Admin = "admin";
}

/// <summary>
/// Hediye puanı defteri satırı (docs/03 <c>wallet_ledger</c>). Yalnızca eklenir; bakiye = <see cref="Amount"/> toplamı.
/// <see cref="IdempotencyKey"/> benzersizdir: aynı işlem iki kez uygulanmaz.
/// </summary>
public class PointsLedgerEntry
{
    public long Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>+ kazanç, − harcama.</summary>
    public long Amount { get; set; }

    public string Type { get; set; } = PointsLedgerTypes.Tournament;

    /// <summary>İlgili kayıt türü ve kimliği (örn. "period" / dönem kimliği, "order" / sipariş).</summary>
    public string? RefType { get; set; }

    public string? RefId { get; set; }
    public string IdempotencyKey { get; set; } = "";

    /// <summary>Yönetici düzeltmesinde gerekçe.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
