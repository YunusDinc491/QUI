namespace SenBilirsin.Modules.Administration.Domain;

public static class AdminRoles
{
    /// <summary>Tek rol: Yönetici (docs/05 §9). Rol alanı ileride editör/destek eklenebilsin diye baştan vardır.</summary>
    public const string Admin = "admin";
}

/// <summary>Yönetim paneli kullanıcısı. Mobil kullanıcılardan ayrı kimlik (docs/08 §6).</summary>
public class AdminUser
{
    public Guid Id { get; set; }

    /// <summary>Küçük harfe çevrilmiş, benzersiz e-posta.</summary>
    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    /// <summary>TOTP gizli anahtarı, AES-GCM ile şifreli (<c>SecretProtector</c>).</summary>
    public string TotpSecretProtected { get; set; } = "";

    /// <summary>En son kabul edilen TOTP adımı; aynı kodun yeniden kullanımını engeller.</summary>
    public long LastTotpStep { get; set; }

    public string Role { get; set; } = AdminRoles.Admin;
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>Her yönetici işleminin denetim kaydı (kim, ne, ne zaman, önce/sonra).</summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>İşlemi yapan yönetici; başarısız girişte kullanıcı bilinmiyorsa boş.</summary>
    public Guid? AdminId { get; set; }

    /// <summary>Örn. "login", "login_failed", "question.create".</summary>
    public string Action { get; set; } = "";

    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }

    /// <summary>JSON (önceki hal). Gizli bilgi (parola, anahtar) asla yazılmaz.</summary>
    public string? Before { get; set; }

    public string? After { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}
