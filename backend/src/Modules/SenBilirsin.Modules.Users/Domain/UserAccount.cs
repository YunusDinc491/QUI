namespace SenBilirsin.Modules.Users.Domain;

public enum UserStatus
{
    Active = 0,
    Banned = 1,
    Deleted = 2,
}

/// <summary>Uygulama kullanıcısı. Kimlik (e-posta/Google) Firebase'de tutulur; burada yalnızca oyun profili vardır.</summary>
public class UserAccount
{
    public Guid Id { get; set; }

    /// <summary>Firebase kullanıcı kimliği (uid). Silinen hesapta anonimleştirilir.</summary>
    public string FirebaseUid { get; set; } = "";

    /// <summary>Benzersiz ve değiştirilemez; küçük harf/rakam/alt çizgi (K-35).</summary>
    public string Username { get; set; } = "";

    /// <summary>Değiştirilebilir görünen ad.</summary>
    public string DisplayName { get; set; } = "";

    public int AvatarId { get; set; }
    public int? FrameId { get; set; }
    public int BirthYear { get; set; }
    public UserStatus Status { get; set; }
    public long TotalScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>Gizlilik politikası/koşullar onay kaydı (KVKK).</summary>
    public DateTime TermsAcceptedAt { get; set; }
    public string TermsVersion { get; set; } = "";

    /// <summary>Tek cihaz kuralı (K-38): hesabın şu an aktif olduğu cihaz.</summary>
    public string? ActiveDeviceId { get; set; }
    public DateTime? ActiveDeviceSince { get; set; }
}

/// <summary>Hazır avatar (profil resmi yüklenmez, K-34). Fiyat 0 ise ücretsizdir.</summary>
public class Avatar
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Görsel adresi (R2). Tasarım aşamasına kadar boş olabilir; ham arayüz ColorHex ile gösterir.</summary>
    public string? ImageUrl { get; set; }

    public string ColorHex { get; set; } = "#9E9E9E";
    public int Price { get; set; }
    public bool IsActive { get; set; } = true;
}
