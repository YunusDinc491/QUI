using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.Store.Domain;

public enum ProductType
{
    /// <summary>Kupon / hediye kartı: kod listesi, tükenebilir.</summary>
    Coupon = 1,

    /// <summary>Joker paketi: joker bakiyesine eklenir.</summary>
    JokerPack = 2,

    /// <summary>Avatar: kullanıcıya bir kez açılır.</summary>
    Avatar = 3,

    /// <summary>Avatar çerçevesi: kullanıcıya bir kez açılır.</summary>
    Frame = 4,
}

public class Product
{
    public int Id { get; set; }
    public ProductType Type { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Hediye puanı cinsinden fiyat.</summary>
    public int Price { get; set; }

    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Joker paketinde: hangi joker ve kaç adet.</summary>
    public JokerType? JokerType { get; set; }

    public int? JokerAmount { get; set; }

    /// <summary>Avatar / çerçeve ürününde: Users modülündeki avatar (ya da çerçeve) kimliği.</summary>
    public int? ItemRefId { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum CouponStatus
{
    Available = 0,

    /// <summary>Bir satın alma işlemi için ayrıldı (ödeme tamamlanınca satılır, olmazsa serbest kalır).</summary>
    Reserved = 1,
    Sold = 2,

    /// <summary>Çalışmayan / harcanmış olduğu anlaşılan kod; bir daha satılmaz.</summary>
    Invalid = 3,
}

public class CouponCode
{
    public long Id { get; set; }
    public int ProductId { get; set; }

    /// <summary>AES-GCM ile şifreli kod (düz metin hiçbir yerde saklanmaz).</summary>
    public string CodeEncrypted { get; set; } = "";

    /// <summary>Aynı kodun iki kez yüklenmesini engellemek için SHA-256 özeti (kodun kendisi değil).</summary>
    public string CodeHash { get; set; } = "";

    public CouponStatus Status { get; set; }
    public Guid? SoldTo { get; set; }
    public DateTime? SoldAt { get; set; }

    /// <summary>Ayırma anı ve hangi satın alma anahtarı için ayrıldığı (yarım kalan işlemi tamamlamak/serbest bırakmak için).</summary>
    public DateTime? ReservedAt { get; set; }

    public string? ReservedForKey { get; set; }
    public string? Note { get; set; }
}

public enum IssueStatus
{
    None = 0,
    Open = 1,
    Resolved = 2,
    Rejected = 3,
}

/// <summary>"Ödüllerim" satırı: kullanıcının satın aldığı ürün (kuponsa ilgili kod).</summary>
public class UserReward
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public int ProductId { get; set; }
    public long? CouponCodeId { get; set; }

    /// <summary>Satın alma işlem anahtarı; benzersizdir (çift tıklama/ağ tekrarı çift satın alma yapmaz).</summary>
    public string PurchaseKey { get; set; } = "";

    public int PricePaid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevealedAt { get; set; }
    public int RevealCount { get; set; }

    public IssueStatus IssueStatus { get; set; }
    public DateTime? IssueReportedAt { get; set; }
    public string? IssueText { get; set; }
    public string? IssueScreenshotUrl { get; set; }
    public int ReplacedCount { get; set; }
    public DateTime? LastReplacedAt { get; set; }
}

/// <summary>Kullanıcıya açılan avatar / çerçeve (bir kez).</summary>
public class UserItem
{
    public Guid UserId { get; set; }
    public int ProductId { get; set; }
    public DateTime CreatedAt { get; set; }
}
