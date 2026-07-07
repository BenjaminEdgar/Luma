namespace Luma.App.Services;

/// <summary>Turns a provider's reply to a TaskKind.Suggest request into up to three short,
/// clean suggestion chips. Providers are asked for bare lines but still occasionally add
/// bullets, numbering, or a lead-in sentence, so parsing stays defensive.</summary>
public static class SuggestionParser
{
    private const int MaxLength = 56;

    public static IReadOnlyList<string> Parse(string raw, int maxSuggestions = 3)
    {
        var suggestions = new List<string>();
        foreach (var line in raw.Split('\n'))
        {
            var text = StripLeadingNumber(line.Trim().TrimStart('-', '*', '•').Trim()).Trim().Trim('"', '\'', '`');
            if (text.Length == 0 || text.Length > MaxLength) continue;
            if (text.EndsWith(':')) continue; // lead-ins like "Here are three suggestions:"
            if (text.StartsWith("ASK_USER", StringComparison.OrdinalIgnoreCase)) continue;
            if (suggestions.Contains(text, StringComparer.OrdinalIgnoreCase)) continue;
            suggestions.Add(text);
            if (suggestions.Count >= maxSuggestions) break;
        }
        return suggestions;
    }

    private static string StripLeadingNumber(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsDigit(text[index])) index++;
        if (index == 0 || index >= text.Length || (text[index] != '.' && text[index] != ')')) return text;
        return text[(index + 1)..];
    }
}
