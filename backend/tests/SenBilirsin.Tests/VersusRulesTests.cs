using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 4.1: rakipli maç puanlaması ve bot davranışı (saf mantık).</summary>
public class VersusRulesTests
{
    private static readonly VersusSettings S = new();

    // ---- Puanlama ----

    [Theory]
    [InlineData(6, 4, 10, 0)]    // kazanan 10, kaybeden 0, eşik altı → bonus yok
    [InlineData(7, 3, 10, 0)]
    [InlineData(8, 5, 15, 0)]    // 8 doğru: kazanan 10 + 5 bonus
    [InlineData(10, 0, 15, 0)]
    [InlineData(9, 8, 15, 5)]    // kaybeden de 8 doğru yaptıysa bonusu alır
    [InlineData(10, 9, 15, 5)]
    [InlineData(1, 0, 10, 0)]
    public void Compute_WinnerGetsWinPoints_AndAnyoneAtThresholdGetsTheBonus(int a, int b, int expectedA, int expectedB)
    {
        var outcome = VersusScoring.Compute(a, b, S);
        Assert.Equal(MatchResults.Win, outcome.ResultA);
        Assert.Equal(MatchResults.Lose, outcome.ResultB);
        Assert.Equal(expectedA, outcome.PointsA);
        Assert.Equal(expectedB, outcome.PointsB);

        // Simetri: taraflar yer değişince sonuç da yer değiştirir
        var mirrored = VersusScoring.Compute(b, a, S);
        Assert.Equal(expectedA, mirrored.PointsB);
        Assert.Equal(expectedB, mirrored.PointsA);
        Assert.Equal(MatchResults.Win, mirrored.ResultB);
    }

    [Theory]
    [InlineData(8, 8, 8)]
    [InlineData(9, 9, 9)]
    [InlineData(10, 10, 10)]
    public void Compute_DrawAtOrAboveThreshold_PaysCorrectTimesCoefficient(int a, int b, int expected)
    {
        var outcome = VersusScoring.Compute(a, b, S);
        Assert.Equal(MatchResults.Draw, outcome.ResultA);
        Assert.Equal(expected, outcome.PointsA);
        Assert.Equal(expected, outcome.PointsB);
        Assert.Equal(0, outcome.BonusA);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void Compute_DrawBelowThreshold_PaysNothing(int correct)
    {
        var outcome = VersusScoring.Compute(correct, correct, S);
        Assert.Equal(MatchResults.Draw, outcome.ResultB);
        Assert.Equal(0, outcome.PointsA + outcome.PointsB);
    }

    [Fact]
    public void Compute_RespectsChangedSettings()
    {
        var custom = S with { WinPoints = 20, BonusPoints = 7, BonusThreshold = 6, DrawCoefficient = 3 };
        Assert.Equal(27, VersusScoring.Compute(6, 2, custom).PointsA);
        Assert.Equal(18, VersusScoring.Compute(6, 6, custom).PointsA);
        Assert.Equal(0, VersusScoring.Compute(5, 5, custom).PointsA);
    }

    [Fact]
    public void Compute_WinnerIdMapsToTheRightPlayer()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Equal(a, VersusScoring.Compute(7, 3, S).Winner(a, b));
        Assert.Equal(b, VersusScoring.Compute(3, 7, S).Winner(a, b));
        Assert.Null(VersusScoring.Compute(5, 5, S).Winner(a, b));
    }

    [Fact]
    public void Forfeit_LosesImmediately_OpponentWins()
    {
        var aQuits = VersusScoring.Forfeit(true, 4, 3, S);
        Assert.Equal(MatchResults.Lose, aQuits.ResultA);
        Assert.Equal(0, aQuits.PointsA);
        Assert.Equal(10, aQuits.PointsB);

        // Rakip eşiği aşmışsa bonusunu yine alır; terk edenin bonusu yok
        var bQuits = VersusScoring.Forfeit(false, 8, 9, S);
        Assert.Equal(MatchResults.Win, bQuits.ResultA);
        Assert.Equal(15, bQuits.PointsA);
        Assert.Equal(0, bQuits.PointsB);
    }

    // ---- Bot ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Bot_IsMoreAccurateOnEasyThanOnHard(int power)
    {
        var easy = BotBrain.CorrectProbability(power, Difficulty.Easy, 0);
        var medium = BotBrain.CorrectProbability(power, Difficulty.Medium, 0);
        var hard = BotBrain.CorrectProbability(power, Difficulty.Hard, 0);
        Assert.True(easy > medium && medium > hard);
    }

    [Fact]
    public void Bot_StrongerPowerIsMoreAccurate_AtEveryDifficulty()
    {
        foreach (var d in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
        {
            Assert.True(BotBrain.CorrectProbability(1, d, 0) < BotBrain.CorrectProbability(2, d, 0));
            Assert.True(BotBrain.CorrectProbability(2, d, 0) < BotBrain.CorrectProbability(3, d, 0));
        }
    }

    [Fact]
    public void Bot_NeverAlwaysRightNorAlwaysWrong_EvenWithExtremeBias()
    {
        Assert.Equal(0.97, BotBrain.CorrectProbability(3, Difficulty.Easy, 5), 3);
        Assert.Equal(0.05, BotBrain.CorrectProbability(1, Difficulty.Hard, -5), 3);
    }

    [Fact]
    public void Bot_MatchBiasStaysSmall()
    {
        var random = new Random(4);
        for (var i = 0; i < 1000; i++) Assert.InRange(BotBrain.NewMatchBias(random), -0.07, 0.07);
    }

    [Fact]
    public void Bot_ThinkTimeStaysBetween3And12Seconds_AndWithinTheProfileRange()
    {
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            Assert.InRange(BotBrain.ThinkTimeMs(5000, 12000, random), 5000, 12000);
            Assert.InRange(BotBrain.ThinkTimeMs(0, 99999, random), 3000, 12000); // profil sınırın dışına taşsa bile kırpılır
        }

        Assert.Equal(4000, BotBrain.ThinkTimeMs(4000, 4000, random));
    }

    [Fact]
    public void Bot_ChoosesTheRightOptionAtTheConfiguredRate_AndWrongOnesAreNeverTheCorrectIndex()
    {
        var random = new Random(11);
        const int runs = 10000;
        var hits = Enumerable.Range(0, runs).Count(_ => BotBrain.ChooseOption(2, 0.65, random) == 2);
        Assert.InRange((double)hits / runs, 0.62, 0.68);

        var wrongSeen = Enumerable.Range(0, 500).Select(_ => BotBrain.ChooseOption(2, 0.0, random)).Distinct().OrderBy(x => x);
        Assert.Equal([0, 1, 3], wrongSeen);
    }
}
