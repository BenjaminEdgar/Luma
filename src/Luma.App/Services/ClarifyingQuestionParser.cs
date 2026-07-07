using System.Text.RegularExpressions;

namespace Luma.App.Services;

/// <summary>Parses the optional trailing "ASK_USER: ..." directive the AI can emit when it
/// needs one specific piece of information before it can give a complete answer.</summary>
public static partial class ClarifyingQuestionParser
{
    [GeneratedRegex(@"(?:\r?\n|^)[ \t]*ASK_USER:[ \t]*(?<q>[^\r\n]+)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Pattern();

    public static (string Text, string? Question) Extract(string text)
    {
        var match = Pattern().Match(text);
        if (!match.Success) return (text, null);
        var question = match.Groups["q"].Value.Trim();
        var cleaned = (text[..match.Index] + text[(match.Index + match.Length)..]).Trim();
        return (string.IsNullOrWhiteSpace(cleaned) ? "One quick question before I continue:" : cleaned, question);
    }
}
