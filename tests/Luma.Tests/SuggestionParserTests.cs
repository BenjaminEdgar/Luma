using Luma.App.Services;

namespace Luma.Tests;

public sealed class SuggestionParserTests
{
    [Fact]
    public void PlainLinesBecomeSuggestions()
    {
        var result = SuggestionParser.Parse("Explain this build error\nSummarize this article\nDraft a reply to Dana");
        Assert.Equal(["Explain this build error", "Summarize this article", "Draft a reply to Dana"], result);
    }

    [Fact]
    public void BulletsNumbersAndQuotesAreStripped()
    {
        var result = SuggestionParser.Parse("- \"Explain this error\"\n2. Summarize the page\n* `Fix the typo`");
        Assert.Equal(["Explain this error", "Summarize the page", "Fix the typo"], result);
    }

    [Fact]
    public void LeadInsBlanksAndDirectivesAreSkipped()
    {
        var result = SuggestionParser.Parse("Here are three ideas:\n\nExplain this chart\nASK_USER: which file?\nSummarize the thread");
        Assert.Equal(["Explain this chart", "Summarize the thread"], result);
    }

    [Fact]
    public void CapsAtThreeAndDeduplicatesIgnoringCase()
    {
        var result = SuggestionParser.Parse("One\none\nTwo\nThree\nFour");
        Assert.Equal(["One", "Two", "Three"], result);
    }

    [Fact]
    public void OverlongLinesAreDropped()
    {
        var longLine = new string('a', 80);
        var result = SuggestionParser.Parse($"{longLine}\nShort suggestion");
        Assert.Equal(["Short suggestion"], result);
    }

    [Fact]
    public void NumberWithoutSeparatorIsKept()
    {
        var result = SuggestionParser.Parse("Explain the 404 error");
        Assert.Equal(["Explain the 404 error"], result);
    }
}
