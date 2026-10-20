namespace SenBilirsin.Modules.Tournament.Domain;

public enum PeriodStatus
{
    /// <summary>Puan yazılıyor.</summary>
    Open = 0,

    /// <summary>Ödüller dağıtıldı; tablo kapandı.</summary>
    Distributed = 3,
}

/// <summary>Turnuva dönemi: 48 saatlik, Türkiye saatiyle 00:00'da başlayıp biten (docs/07 §2).</summary>
public class Period
{
    public int Id { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public PeriodStatus Status { get; set; }
    public DateTime? DistributedAt { get; set; }
}

/// <summary>Bir maçın bir oyuncuya döneme yazdığı puan. (maç, oyuncu) benzersizdir: aynı maç iki kez yazılamaz.</summary>
public class PeriodScoreEntry
{
    public long Id { get; set; }
    public int PeriodId { get; set; }
    public Guid UserId { get; set; }
    public Guid MatchId { get; set; }
    public int Points { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Toplam puana (profil göstergesi) yansıtıldı mı; yansıtılmadıysa iş tekrar dener.</summary>
    public bool TotalApplied { get; set; }
}

/// <summary>Bir oyuncunun bir dönemdeki toplam puanı (sıralama bu tablodan okunur).</summary>
public class PeriodScore
{
    public int PeriodId { get; set; }
    public Guid UserId { get; set; }
    public int Points { get; set; }
    public int MatchCount { get; set; }
}

/// <summary>Ödül tablosu satırı: <see cref="RankFrom"/>–<see cref="RankTo"/> arası sıralara <see cref="Amount"/> hediye puanı.</summary>
public class RewardTier
{
    public int Id { get; set; }
    public int RankFrom { get; set; }
    public int RankTo { get; set; }
    public int Amount { get; set; }
}

/// <summary>Bir dönemden men: sıralamadan ve ödülden çıkar, alttaki sıra yükselir (docs/07 §2).</summary>
public class Disqualification
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public Guid UserId { get; set; }
    public string Reason { get; set; } = "";
    public Guid? AdminId { get; set; }
    public DateTime CreatedAt { get; set; }
}
