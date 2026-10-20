using SenBilirsin.Modules.Questions.Application;

namespace SenBilirsin.Tests;

/// <summary>Faz 2.4: CSV okuyucu birim testleri.</summary>
public class CsvParserTests
{
    [Fact]
    public void Parses_CommaSeparated_WithHeader()
    {
        var rows = CsvParser.Parse("a,b,c\n1,2,3\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void DetectsSemicolon_FromTurkishExcel()
    {
        var rows = CsvParser.Parse("soru;a;b\r\nBaşkent neresidir?;Ankara;İzmir\r\n");
        Assert.Equal(["Başkent neresidir?", "Ankara", "İzmir"], rows[1]);
    }

    [Fact]
    public void HandlesQuotes_EscapedQuotes_DelimitersAndNewlinesInsideFields()
    {
        var rows = CsvParser.Parse("x,y\n\"merhaba, dünya\",\"o \"\"dedi\"\"\"\n\"iki\nsatır\",z\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal("merhaba, dünya", rows[1][0]);
        Assert.Equal("o \"dedi\"", rows[1][1]);
        Assert.Equal("iki\nsatır", rows[2][0]);
    }

    [Fact]
    public void StripsBom_SkipsBlankLines_AndHandlesMissingFinalNewline()
    {
        var rows = CsvParser.Parse("﻿a,b\n\n1,2");
        Assert.Equal(2, rows.Count);
        Assert.Equal("a", rows[0][0]);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void TrimsUnquotedFields_ButKeepsQuotedWhitespace()
    {
        var rows = CsvParser.Parse("a,b\n  x  ,\"  y  \"\n");
        Assert.Equal("x", rows[1][0]);
        Assert.Equal("  y  ", rows[1][1]);
    }

    [Fact]
    public void EmptyInput_GivesNoRows() => Assert.Empty(CsvParser.Parse(""));
}
