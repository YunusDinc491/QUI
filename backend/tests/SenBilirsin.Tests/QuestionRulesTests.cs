using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.Tests;

public class QuestionRulesTests
{
    private static readonly string[] Opts = ["Ankara", "İstanbul", "İzmir", "Bursa"];

    [Fact]
    public void ValidQuestion_Passes() =>
        Assert.True(QuestionRules.Check("Türkiye'nin başkenti neresidir?", Opts, 0, Difficulty.Easy, null).IsValid);

    [Theory]
    [InlineData("kısa", "TEXT_TOO_SHORT")]
    [InlineData(null, "TEXT_TOO_SHORT")]
    public void TextRules(string? text, string code) =>
        Assert.Equal(code, QuestionRules.Check(text, Opts, 0, Difficulty.Easy, null).Code);

    [Fact]
    public void TextTooLong_Fails() =>
        Assert.Equal("TEXT_TOO_LONG", QuestionRules.Check(new string('a', 501), Opts, 0, Difficulty.Easy, null).Code);

    [Fact]
    public void OptionRules()
    {
        const string text = "Türkiye'nin başkenti neresidir?";
        Assert.Equal("OPTIONS_COUNT", QuestionRules.Check(text, ["a", "b", "c"], 0, Difficulty.Easy, null).Code);
        Assert.Equal("OPTIONS_COUNT", QuestionRules.Check(text, null, 0, Difficulty.Easy, null).Code);
        Assert.Equal("OPTION_EMPTY", QuestionRules.Check(text, ["a", "b", " ", "d"], 0, Difficulty.Easy, null).Code);
        Assert.Equal("OPTION_TOO_LONG", QuestionRules.Check(text, ["a", "b", "c", new string('x', 151)], 0, Difficulty.Easy, null).Code);
        // "Ankara" ve "ankara." aynı sayılır (büyük/küçük harf ve noktalama fark etmez)
        Assert.Equal("OPTIONS_DUPLICATE", QuestionRules.Check(text, ["Ankara", "ankara.", "İzmir", "Bursa"], 0, Difficulty.Easy, null).Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void CorrectIndexOutOfRange_Fails(int index) =>
        Assert.Equal("CORRECT_INVALID", QuestionRules.Check("Türkiye'nin başkenti neresidir?", Opts, index, Difficulty.Easy, null).Code);

    [Fact]
    public void InvalidDifficultyAndLongExplanation_Fail()
    {
        const string text = "Türkiye'nin başkenti neresidir?";
        Assert.Equal("DIFFICULTY_INVALID", QuestionRules.Check(text, Opts, 0, (Difficulty)9, null).Code);
        Assert.Equal("EXPLANATION_TOO_LONG", QuestionRules.Check(text, Opts, 0, Difficulty.Easy, new string('a', 1001)).Code);
    }

    [Fact]
    public void Normalize_RemovesCaseAccentsAndPunctuation()
    {
        Assert.Equal("turkiye nin baskenti neresidir", QuestionRules.Normalize("Türkiye'nin  BAŞKENTİ neresidir?"));
        Assert.Equal("isik", QuestionRules.Normalize("IŞIK")); // I/İ/ı hepsi i
        Assert.Equal("2 3 5", QuestionRules.Normalize("2, 3; 5!"));
    }

    [Fact]
    public void Similarity_ExactAfterNormalization_IsOne() =>
        Assert.Equal(1.0, QuestionRules.Similarity(
            QuestionRules.Normalize("Türkiye'nin başkenti neresidir?"),
            QuestionRules.Normalize("TÜRKİYE NİN BAŞKENTİ NERESİDİR")));

    [Fact]
    public void Similarity_OneExtraWordInLongQuestion_IsHigh()
    {
        var a = QuestionRules.Normalize("Osmanlı Devleti nin kurucusu olan kişi hangi beyliğin başında bulunmuştur");
        var b = QuestionRules.Normalize("Osmanlı Devleti nin kurucusu olan kişi hangi beyliğin başında bulunmuştur sizce");
        Assert.True(QuestionRules.Similarity(a, b) >= QuestionRules.SimilarityThreshold);
    }

    [Fact]
    public void Similarity_DifferentOrShortQuestions_IsLow()
    {
        Assert.True(QuestionRules.Similarity(
            QuestionRules.Normalize("Dünyanın en büyük okyanusu hangisidir"),
            QuestionRules.Normalize("Türkiye'nin en uzun nehri hangisidir")) < QuestionRules.SimilarityThreshold);
        // 4 kelimeden kısa sorular yalnızca tam eşleşmede benzer sayılır
        Assert.Equal(0.0, QuestionRules.Similarity("baskent neresi", "baskent nerede"));
    }
}
