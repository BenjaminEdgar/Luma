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
    public void OverlongLinesAreTruncatedNotDropped()
    {
        var longLine = "Explain the complicated build failure cascading through modules " + new string('x', 40);
        var result = SuggestionParser.Parse($"{longLine}\nShort tip");
        Assert.Equal(2, result.Count);
        Assert.True(result[0].Length <= SuggestionParser.MaxLength);
        Assert.EndsWith("…", result[0]);
        Assert.Equal("Short tip", result[1]);
    }

    [Fact]
    public void NumberWithoutSeparatorIsKept()
    {
        var result = SuggestionParser.Parse("Explain the 404 error");
        Assert.Equal(["Explain the 404 error"], result);
    }

    [Fact]
    public void DotSeparatedOneLinerSplitsIntoChips()
    {
        var result = SuggestionParser.Parse("Explain this error · Fix the test · Open logs");
        Assert.Equal(3, result.Count);
        Assert.Contains("Explain this error", result);
        Assert.Contains("Fix the test", result);
    }

    [Fact]
    public void IsOnlySeedsDetectsInstantPlaceholders()
    {
        Assert.True(SuggestionParser.IsOnlySeeds(SuggestionPrompts.InstantSeeds));
        Assert.False(SuggestionParser.IsOnlySeeds(["Explain this error", "Fix the build"]));
        Assert.False(SuggestionParser.IsOnlySeeds([]));
    }

    [Fact]
    public void FromScreenPromptAsksForExactLineCount()
    {
        var prompt = SuggestionPrompts.FromScreen(3);
        Assert.Contains("exactly 3", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verb-led", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("What might I want", prompt);
    }
}
