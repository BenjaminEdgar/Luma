using Luma.App.Services;

namespace Luma.Tests;

public sealed class ClarifyingQuestionParserTests
{
    [Fact]
    public void PlainAnswerHasNoQuestion()
    {
        var (text, question) = ClarifyingQuestionParser.Extract("This button opens the settings panel.");
        Assert.Equal("This button opens the settings panel.", text);
        Assert.Null(question);
    }

    [Fact]
    public void TrailingDirectiveIsExtractedAndStripped()
    {
        var raw = "This looks like a null reference in your event handler.\nASK_USER: What's the project directory so I can check the file?";
        var (text, question) = ClarifyingQuestionParser.Extract(raw);
        Assert.Equal("This looks like a null reference in your event handler.", text);
        Assert.Equal("What's the project directory so I can check the file?", question);
    }

    [Fact]
    public void DirectiveAsOnlyLineFallsBackToLeadIn()
    {
        var (text, question) = ClarifyingQuestionParser.Extract("ASK_USER: What tone should this email have?");
        Assert.Equal("One quick question before I continue:", text);
        Assert.Equal("What tone should this email have?", question);
    }

    [Fact]
    public void CaseAndWhitespaceInsideLineIsTolerated()
    {
        var raw = "Sure, I can draft that.\n  ASK_USER:   Who is the recipient?  ";
        var (text, question) = ClarifyingQuestionParser.Extract(raw);
        Assert.Equal("Sure, I can draft that.", text);
        Assert.Equal("Who is the recipient?", question);
    }

    [Fact]
    public void MentionOfAskUserMidLineIsNotTreatedAsDirective()
    {
        var raw = "Note: ASK_USER: is the directive format for clarifying questions.";
        var (text, question) = ClarifyingQuestionParser.Extract(raw);
        Assert.Equal(raw, text);
        Assert.Null(question);
    }
}
