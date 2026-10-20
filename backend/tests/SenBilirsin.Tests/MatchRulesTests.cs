using SenBilirsin.Modules.MatchEngine.Domain;
using SenBilirsin.Modules.Questions.Domain;
using SenBilirsin.Modules.Wallet.Domain;

namespace SenBilirsin.Tests;

/// <summary>Faz 3.3: maç kuralları (saf mantık) — puan merdiveni, zorluk planı, şık karıştırma, joker etkileri.</summary>
public class MatchRulesTests
{
    private static readonly MatchSettings Defaults = new();

    // ---- Puanlama ----

    [Theory]
    [InlineData(10, 100)]
    [InlineData(9, 10)]
    [InlineData(8, 5)]
    [InlineData(7, 0)]
    [InlineData(5, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void SoloPoints_FollowTheLadder(int correct, int expected) =>
        Assert.Equal(expected, ScoringRules.SoloPoints(correct, Defaults));

    [Fact]
    public void SoloPoints_UseACustomLadder_AndNeverGoNegative()
    {
        var custom = Defaults with { SoloLadder = new Dictionary<int, int> { [10] = 250, [7] = 3 } };
        Assert.Equal(250, ScoringRules.SoloPoints(10, custom));
        Assert.Equal(3, ScoringRules.SoloPoints(7, custom));
        Assert.Equal(0, ScoringRules.SoloPoints(9, custom));
        Assert.All(Enumerable.Range(0, 11), c => Assert.True(ScoringRules.SoloPoints(c, Defaults) >= 0));
    }

    [Fact]
    public void SoloLadder_IsMonotonic_MoreCorrectNeverMeansFewerPoints()
    {
        var points = Enumerable.Range(0, 11).Select(c => ScoringRules.SoloPoints(c, Defaults)).ToList();
        Assert.Equal(points.OrderBy(p => p), points);
    }

    // ---- Zorluk planı ----

    [Theory]
    [InlineData(1, Difficulty.Easy)]
    [InlineData(4, Difficulty.Easy)]
    [InlineData(5, Difficulty.Medium)]
    [InlineData(7, Difficulty.Medium)]
    [InlineData(8, Difficulty.Hard)]
    [InlineData(10, Difficulty.Hard)]
    public void DifficultyAt_Is4Easy3Medium3Hard(int position, Difficulty expected) =>
        Assert.Equal(expected, MatchPlan.DifficultyAt(position, Defaults));

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public void DifficultyAt_RejectsOutOfRangePositions(int position) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchPlan.DifficultyAt(position, Defaults));

    [Fact]
    public void PickPlan_Totals10_AndFollowsSettings()
    {
        Assert.Equal([(Difficulty.Easy, 4), (Difficulty.Medium, 3), (Difficulty.Hard, 3)], MatchPlan.PickPlan(Defaults));

        var other = Defaults with { EasyCount = 2, MediumCount = 4 };
        Assert.Equal(10, MatchPlan.PickPlan(other).Sum(p => p.Item2));
        Assert.Equal(Difficulty.Medium, MatchPlan.DifficultyAt(3, other));
        Assert.Equal(Difficulty.Hard, MatchPlan.DifficultyAt(7, other));
    }

    // ---- Şık karıştırma ----

    [Fact]
    public void Shuffle_AlwaysReturnsAPermutation_AndIsDeterministicForASeed()
    {
        var random = new Random(42);
        for (var i = 0; i < 500; i++) Assert.True(OptionShuffler.IsPermutation(OptionShuffler.Shuffle(random)));

        Assert.Equal(OptionShuffler.Shuffle(new Random(5)), OptionShuffler.Shuffle(new Random(5)));
    }

    [Fact]
    public void Shuffle_PutsEveryOptionInEveryPosition_Roughly25Percent()
    {
        var random = new Random(1);
        var counts = new int[4];
        const int runs = 4000;
        for (var i = 0; i < runs; i++) counts[OptionShuffler.ToDisplay(OptionShuffler.Shuffle(random), 0)]++;

        Assert.All(counts, c => Assert.InRange((double)c / runs, 0.21, 0.29));
    }

    [Fact]
    public void Order_RoundTripsThroughTextAndIndexMapping()
    {
        int[] order = [2, 0, 3, 1];
        Assert.Equal(order, OptionShuffler.Parse(OptionShuffler.Serialize(order)));
        Assert.Equal(3, OptionShuffler.ToOriginal(order, 2));
        Assert.Equal(2, OptionShuffler.ToDisplay(order, 3));
        Assert.All(Enumerable.Range(0, 4), o => Assert.Equal(o, OptionShuffler.ToOriginal(order, OptionShuffler.ToDisplay(order, o))));
        Assert.False(OptionShuffler.IsPermutation([0, 1, 1, 3]));
        Assert.False(OptionShuffler.IsPermutation([0, 1, 2]));
    }

    // ---- Jokerler ----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Half_RemovesExactlyTwoDistinctWrongOptions_NeverTheCorrectOne(int correct)
    {
        var random = new Random(correct + 10);
        for (var i = 0; i < 400; i++)
        {
            var removed = JokerRules.Half(correct, random);
            Assert.Equal(2, removed.Length);
            Assert.Equal(2, removed.Distinct().Count());
            Assert.DoesNotContain(correct, removed);
            Assert.All(removed, r => Assert.InRange(r, 0, 3));
            Assert.Equal(removed.OrderBy(x => x), removed);
        }
    }

    [Fact]
    public void Half_EventuallyRemovesEveryWrongOption_NotAlwaysTheSameTwo()
    {
        var random = new Random(3);
        var seen = new HashSet<int>();
        for (var i = 0; i < 200; i++) foreach (var r in JokerRules.Half(2, random)) seen.Add(r);
        Assert.Equal([0, 1, 3], seen.OrderBy(x => x));
    }

    [Fact]
    public void Hint_ShowsTheCorrectOptionAboutAsOftenAsConfigured()
    {
        var random = new Random(99);
        const int runs = 8000;
        var hits = Enumerable.Range(0, runs).Count(_ => JokerRules.Hint(1, Defaults.HintCorrectProbability, random) == 1);
        Assert.InRange((double)hits / runs, 0.72, 0.78);
    }

    [Fact]
    public void Hint_WrongHintIsNeverTheCorrectOption_AndExtremesBehave()
    {
        var random = new Random(8);
        Assert.All(Enumerable.Range(0, 300), _ => Assert.Equal(2, JokerRules.Hint(2, 1.0, random)));
        Assert.All(Enumerable.Range(0, 300), _ => Assert.NotEqual(2, JokerRules.Hint(2, 0.0, random)));

        var wrongSeen = Enumerable.Range(0, 300).Select(_ => JokerRules.Hint(2, 0.0, random)).Distinct().OrderBy(x => x);
        Assert.Equal([0, 1, 3], wrongSeen);
    }

    [Theory]
    [InlineData(1, 1, null, true)]
    [InlineData(1, 0, 1, true)]
    [InlineData(1, 1, 3, true)]
    [InlineData(1, 0, 2, false)]
    [InlineData(1, null, null, false)]
    [InlineData(1, 3, null, false)]
    public void DoubleAnswer_IsCorrectIfEitherChoiceIsCorrect(int correct, int? chosen, int? second, bool expected) =>
        Assert.Equal(expected, JokerRules.IsCorrect(correct, chosen, second));

    [Fact]
    public void JokerCodes_RoundTrip_AndRejectUnknownCodes()
    {
        foreach (var type in Enum.GetValues<JokerType>())
        {
            Assert.True(JokerRules.TryParse(JokerRules.ToCode(type), out var parsed));
            Assert.Equal(type, parsed);
        }

        Assert.False(JokerRules.TryParse("zaman", out _));
        Assert.False(JokerRules.TryParse(null, out _));
        Assert.True(JokerRules.IsValidIndex(null));
        Assert.True(JokerRules.IsValidIndex(3));
        Assert.False(JokerRules.IsValidIndex(4));
        Assert.False(JokerRules.IsValidIndex(-1));
    }
}
