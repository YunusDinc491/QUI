using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Modules.MatchEngine.Domain;

/// <summary>Rakipli maç ayarları (docs/01 §6–7, §11). Panelden ayarlanabilir hale Faz 8'de gelir.</summary>
public sealed record VersusSettings
{
    public int WinPoints { get; init; } = 10;
    public int BonusPoints { get; init; } = 5;

    /// <summary>Bu doğru sayısı ve üstü ekstra puan getirir (K-51).</summary>
    public int BonusThreshold { get; init; } = 8;

    /// <summary>Eşik ve üstünde beraberlikte puan = doğru sayısı × katsayı. Başlangıç değeri varsayımdır (docs'ta sayı yok).</summary>
    public int DrawCoefficient { get; init; } = 1;

    public int CountdownSeconds { get; init; } = 5;
    public int ReadyTimeoutSeconds { get; init; } = 10;
    public int QueueTimeoutSeconds { get; init; } = 15;

    /// <summary>İki oyuncu da cevaplayınca sonuç bu kadar sonra gösterilir.</summary>
    public int RevealDelayMs { get; init; } = 1000;

    /// <summary>Maç başına kopma havuzu (docs/01 §11).</summary>
    public int DisconnectPoolMs { get; init; } = 30_000;

    public int RoomMinutes { get; init; } = 5;
}

public static class MatchResults
{
    public const string Win = "win";
    public const string Lose = "lose";
    public const string Draw = "draw";
    public const string Cancelled = "cancelled";
}

public sealed record VersusOutcome(string ResultA, string ResultB, int PointsA, int BonusA, int PointsB, int BonusB)
{
    public Guid? Winner(Guid a, Guid b) => ResultA == MatchResults.Win ? a : ResultB == MatchResults.Win ? b : null;
}

public static class VersusScoring
{
    /// <summary>
    /// En çok doğru yapan kazanır: kazanan <c>WinPoints</c>, kaybeden 0; eşik (8) ve üstü doğru yapan herkes ayrıca bonus alır.
    /// Beraberlikte eşik ve üstündeyse iki taraf da doğru × katsayı puan alır, altındaysa puan yoktur.
    /// </summary>
    public static VersusOutcome Compute(int correctA, int correctB, VersusSettings s)
    {
        if (correctA == correctB)
        {
            var draw = correctA >= s.BonusThreshold ? correctA * s.DrawCoefficient : 0;
            return new VersusOutcome(MatchResults.Draw, MatchResults.Draw, draw, 0, draw, 0);
        }

        var aWins = correctA > correctB;
        int Bonus(int correct) => correct >= s.BonusThreshold ? s.BonusPoints : 0;
        var bonusA = Bonus(correctA);
        var bonusB = Bonus(correctB);
        return new VersusOutcome(
            aWins ? MatchResults.Win : MatchResults.Lose,
            aWins ? MatchResults.Lose : MatchResults.Win,
            (aWins ? s.WinPoints : 0) + bonusA, bonusA,
            (aWins ? 0 : s.WinPoints) + bonusB, bonusB);
    }

    /// <summary>Pes/kopma: terk eden kaybeder (0), rakip kazanır; bonus yalnızca eşik aşılmışsa o ana kadarki doğrulara göre.</summary>
    public static VersusOutcome Forfeit(bool aForfeits, int correctA, int correctB, VersusSettings s)
    {
        int Bonus(int correct) => correct >= s.BonusThreshold ? s.BonusPoints : 0;
        var bonusA = Bonus(correctA);
        var bonusB = Bonus(correctB);
        return aForfeits
            ? new VersusOutcome(MatchResults.Lose, MatchResults.Win, 0, 0, s.WinPoints + bonusB, bonusB)
            : new VersusOutcome(MatchResults.Win, MatchResults.Lose, s.WinPoints + bonusA, bonusA, 0, 0);
    }
}

/// <summary>Bot davranışı (docs/01 §8): güç seviyesine ve zorluğa göre doğruluk, 3–12 sn arası rastgele cevap süresi, maç başına küçük sapma.</summary>
public static class BotBrain
{
    public const int MinDelayMs = 3000;
    public const int MaxDelayMs = 12000;

    // Güç (1..3) × zorluk (kolay, orta, zor) → doğru bilme olasılığı
    private static readonly double[][] Accuracy =
    [
        [0.70, 0.45, 0.30], // zayıf
        [0.85, 0.65, 0.45], // orta
        [0.95, 0.80, 0.60], // güçlü
    ];

    /// <summary>Bu maça özel sapma: botun "o günkü formu" (−0,07 … +0,07).</summary>
    public static double NewMatchBias(Random random) => (random.NextDouble() - 0.5) * 0.14;

    public static double CorrectProbability(int power, Difficulty difficulty, double matchBias)
    {
        var row = Accuracy[Math.Clamp(power, 1, 3) - 1];
        var p = row[(int)difficulty - 1] + matchBias;
        return Math.Clamp(p, 0.05, 0.97); // asla "hep doğru" ya da "hep yanlış" değil
    }

    public static int ThinkTimeMs(int speedMinMs, int speedMaxMs, Random random)
    {
        var min = Math.Clamp(speedMinMs, MinDelayMs, MaxDelayMs);
        var max = Math.Clamp(Math.Max(speedMaxMs, min), MinDelayMs, MaxDelayMs);
        return random.Next(min, max + 1);
    }

    /// <summary>Botun işaretleyeceği ekran şıkkı: olasılıkla doğru, değilse rastgele bir yanlış.</summary>
    public static int ChooseOption(int correctDisplayIndex, double correctProbability, Random random)
    {
        if (random.NextDouble() < correctProbability) return correctDisplayIndex;
        var wrong = Enumerable.Range(0, 4).Where(i => i != correctDisplayIndex).ToList();
        return wrong[random.Next(wrong.Count)];
    }
}
