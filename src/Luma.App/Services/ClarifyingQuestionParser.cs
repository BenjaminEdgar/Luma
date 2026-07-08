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
        var result = ExtractDetailed(text);
        return (result.Text, result.Question);
    }

    public static ClarifyingQuestionResult ExtractDetailed(string text)
    {
        var match = Pattern().Match(text);
        if (!match.Success) return new(text, null, []);
        var parts = match.Groups["q"].Value.Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var question = parts.FirstOrDefault()?.Trim();
        var cleaned = (text[..match.Index] + text[(match.Index + match.Length)..]).Trim();
        return new(
            string.IsNullOrWhiteSpace(cleaned) ? "One quick question before I continue:" : cleaned,
            question,
            parts.Skip(1).Take(4).ToArray());
    }
}

public sealed record ClarifyingQuestionResult(string Text, string? Question, IReadOnlyList<string> Choices);
