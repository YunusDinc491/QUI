namespace SenBilirsin.Modules.Questions.Domain;

public enum Difficulty
{
    Easy = 1,
    Medium = 2,
    Hard = 3,
}

public enum QuestionStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

/// <summary>
/// Soru bankası kaydı (K-39): 4 şık, tek doğru şık, 3 zorluk, opsiyonel açıklama.
/// Doğru cevap yalnızca sunucuda tutulur; oyun sırasında istemciye gönderilmez (mimari ilke 1).
/// </summary>
public class Question
{
    public int Id { get; set; }

    /// <summary>Katalog modülündeki kategori (modüller arası yabancı anahtar yok; kontrol ICatalogQueries ile yapılır).</summary>
    public int CategoryId { get; set; }

    public string Text { get; set; } = "";
    public string OptionA { get; set; } = "";
    public string OptionB { get; set; } = "";
    public string OptionC { get; set; } = "";
    public string OptionD { get; set; } = "";

    /// <summary>Doğru şık: 0=A, 1=B, 2=C, 3=D.</summary>
    public int CorrectIndex { get; set; }

    public Difficulty Difficulty { get; set; }
    public string? Explanation { get; set; }
    public QuestionStatus Status { get; set; }

    /// <summary>Tekrar tespiti için sadeleştirilmiş soru metni (küçük harf, aksansız, noktalamasız).</summary>
    public string NormalizedText { get; set; } = "";

    // Soru istatistikleri (basit sayaçlar, K-39); Faz 3'te oyun tarafından artırılır
    public int ShownCount { get; set; }
    public int CorrectCount { get; set; }
    public int ReportCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string[] Options => [OptionA, OptionB, OptionC, OptionD];
}

/// <summary>Kullanıcının gördüğü soru (tekrar görmemesi için son 200 tutulur). Modüller arası yabancı anahtar yoktur.</summary>
public class UserSeenQuestion
{
    public Guid UserId { get; set; }
    public int QuestionId { get; set; }
    public DateTime SeenAt { get; set; }
}

/// <summary>Oyuncunun bildirdiği soru (docs/05 "Bildirilen sorular"). Aynı soruya birden fazla bildirim tek satırda sayaçla birleşir.</summary>
public class QuestionReport
{
    public const int ReasonMax = 300;

    public long Id { get; set; }
    public int QuestionId { get; set; }
    public Guid UserId { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "open";
    public DateTime CreatedAt { get; set; }
}
