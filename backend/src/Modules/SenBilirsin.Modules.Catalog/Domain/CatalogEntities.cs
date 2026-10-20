namespace SenBilirsin.Modules.Catalog.Domain;

/// <summary>Oyun modu: genel / branş / kpss / fan (K-13). Sabit 4 kayıt, tohumlanır.</summary>
public class Mode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Order { get; set; }
}

/// <summary>
/// Modun alt dalı (örn. Branş → Tarih). Yalnızca <see cref="IsPublished"/> olanlar kullanıcıya görünür.
/// "Genel sorular" modunun tek örtük kategorisi vardır ("Genel").
/// </summary>
public class Category
{
    public int Id { get; set; }
    public int ModeId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Görsel adresi (R2/yerel depolama). Tasarım aşamasına kadar boş olabilir.</summary>
    public string? ImageUrl { get; set; }

    public string? ColorHex { get; set; }
    public int Order { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class ModeCodes
{
    public const string General = "general";
    public const string Branch = "branch";
    public const string Kpss = "kpss";
    public const string Fan = "fan";
}
