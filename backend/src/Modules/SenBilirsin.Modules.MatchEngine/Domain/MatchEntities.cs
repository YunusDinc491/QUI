using SenBilirsin.Modules.Questions.Domain;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Modules.MatchEngine.Domain;

public enum MatchKind
{
    Solo = 1,
    // Faz 4: Random, Bot, Friend
}

public enum MatchStatus
{
    /// <summary>Oynanıyor.</summary>
    Running = 1,

    /// <summary>Bireysel: yanlış cevaptan sonra "reklamla devam" kararı bekleniyor.</summary>
    AwaitingContinue = 2,

    /// <summary>Normal bitti (10 doğru ya da elendi).</summary>
    Finished = 3,

    /// <summary>İptal (terk edildi / bağlantı koptu): puan verilmez.</summary>
    Cancelled = 4,

    /// <summary>Oyuncu bilerek çıktı (pes): o ana kadarki doğru sayısına göre puan.</summary>
    Forfeited = 5,
}

public static class MatchEndReasons
{
    public const string Completed = "completed";
    public const string Eliminated = "eliminated";
    public const string Forfeit = "forfeit";
    public const string Abandoned = "abandoned";
}

/// <summary>Maç kaydı (docs/03 <c>matches</c>). Oyuncu bilgisi <see cref="MatchPlayer"/> satırlarındadır.</summary>
public class Match
{
    public Guid Id { get; set; }
    public MatchKind Type { get; set; }
    public MatchStatus Status { get; set; }

    /// <summary>Katalog modu kodu (general/branch/kpss/fan).</summary>
    public string ModeCode { get; set; } = "";

    /// <summary>Seçilen alt dal; "Karışık" maçta boş.</summary>
    public int? CategoryId { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>Sırası gelen soru (1 tabanlı).</summary>
    public int CurrentPosition { get; set; }

    /// <summary>Reklamla devam kararı için son an.</summary>
    public DateTime? ContinueDeadline { get; set; }

    /// <summary>Yeterli "görülmemiş" soru yoktu; kısıt gevşetildi (panel uyarısı için).</summary>
    public bool SeenFilterRelaxed { get; set; }
}

public class MatchPlayer
{
    public Guid MatchId { get; set; }
    public Guid UserId { get; set; }
    public int CorrectCount { get; set; }
    public int Points { get; set; }

    /// <summary><see cref="MatchEndReasons"/>; maç sürerken boş.</summary>
    public string? Result { get; set; }

    public bool ContinueUsed { get; set; }

    /// <summary>Puan kesinleşti (devam penceresi kapandı / maç bitti). Faz 5 dönem puanını yalnızca bu satırlardan işler.</summary>
    public DateTime? SettledAt { get; set; }
}

/// <summary>Maçın bir sırasındaki soru. Reklamla devam edilirse aynı sıraya <see cref="Attempt"/> artırılarak yeni (kolay) soru konur.</summary>
public class MatchQuestion
{
    public Guid MatchId { get; set; }
    public int Position { get; set; }
    public int Attempt { get; set; }
    public int QuestionId { get; set; }
    public Difficulty Slot { get; set; }

    /// <summary>Ekrandaki şık sırası ("2,0,3,1"): i. ekran şıkkı özgün <c>order[i]</c> şıkkıdır.</summary>
    public string OptionOrder { get; set; } = "";

    /// <summary>Soru ekrana geldi (sunucu saati); yoksa henüz gösterilmedi.</summary>
    public DateTime? StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }
}

public class MatchAnswer
{
    public Guid MatchId { get; set; }
    public Guid UserId { get; set; }
    public int Position { get; set; }
    public int Attempt { get; set; }

    /// <summary>Seçilen ekran şıkkı (0–3); süre dolduysa boş.</summary>
    public int? ChosenDisplay { get; set; }

    /// <summary>Çift cevap jokerinde ikinci işaret.</summary>
    public int? SecondDisplay { get; set; }

    public bool IsCorrect { get; set; }
    public bool TimedOut { get; set; }
    public int ResponseMs { get; set; }
    public DateTime AnsweredAt { get; set; }
}

/// <summary>Maçta kullanılan joker. Bir maçta her tür bir kez, bir soruda en çok bir joker (K-52).</summary>
public class MatchJoker
{
    public Guid MatchId { get; set; }
    public Guid UserId { get; set; }
    public JokerType Type { get; set; }
    public int Position { get; set; }
    public int Attempt { get; set; }

    /// <summary>Sonuç JSON'u: <c>{"removed":[0,2]}</c> / <c>{"hint":1}</c> / <c>{}</c>.</summary>
    public string ResultJson { get; set; } = "{}";

    public DateTime UsedAt { get; set; }
}
