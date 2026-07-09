namespace Luma.App.Services;

/// <summary>Turns a provider's reply to a TaskKind.Suggest request into short, clean chips.
/// Providers are asked for bare lines but still occasionally add bullets, numbering, or a
/// lead-in sentence, so parsing stays defensive and recovers overlong lines by truncating.</summary>
public static class SuggestionParser
{
    public const int MaxLength = 56;

    public static IReadOnlyList<string> Parse(string raw, int maxSuggestions = 3)
    {
        maxSuggestions = Math.Clamp(maxSuggestions, 1, 8);
        var suggestions = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return suggestions;

        foreach (var line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            foreach (var candidate in ExpandLine(line))
            {
                var text = Normalize(candidate);
                if (text is null) continue;
                if (suggestions.Contains(text, StringComparer.OrdinalIgnoreCase)) continue;
                suggestions.Add(text);
                if (suggestions.Count >= maxSuggestions) return suggestions;
            }
        }
        return suggestions;
    }

    /// <summary>True when the list is only the static instant seeds (not yet AI-refined).</summary>
    public static bool IsOnlySeeds(IEnumerable<string> chips)
    {
        var list = chips.ToList();
        if (list.Count == 0) return false;
        if (list.Count > SuggestionPrompts.InstantSeeds.Count) return false;
        return list.All(c => SuggestionPrompts.InstantSeeds.Contains(c, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ExpandLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) yield break;
        // Some models dump "A · B · C" or "A; B; C" on one line — split if each piece is short.
        if (trimmed.Contains('·') || trimmed.Contains(';'))
        {
            var parts = trimmed.Split(['·', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && parts.All(p => p.Length is > 0 and <= MaxLength + 10))
            {
                foreach (var p in parts) yield return p;
                yield break;
            }
        }
        yield return trimmed;
    }

    private static string? Normalize(string raw)
    {
        var text = StripLeadingNumber(raw.Trim().TrimStart('-', '*', '•', '–', '—').Trim())
            .Trim()
            .Trim('"', '\'', '`', '“', '”');
        if (text.Length == 0) return null;
        if (text.EndsWith(':') && text.Length < 28) return null; // lead-ins like "Here are three suggestions:"
        if (text.StartsWith("ASK_USER", StringComparison.OrdinalIgnoreCase)) return null;
        if (text.StartsWith("NEED_SCREEN", StringComparison.OrdinalIgnoreCase)) return null;
        if (text.StartsWith("SHOW_WHERE", StringComparison.OrdinalIgnoreCase)) return null;
        if (text.StartsWith("Here are", StringComparison.OrdinalIgnoreCase)) return null;
        if (text.StartsWith("Sure,", StringComparison.OrdinalIgnoreCase)) return null;

        if (text.Length > MaxLength)
            text = TruncateAtWord(text, MaxLength);
        if (text.Length < 3) return null;
        return text;
    }

    private static string TruncateAtWord(string text, int max)
    {
        if (text.Length <= max) return text;
        var slice = text[..max].TrimEnd();
        var space = slice.LastIndexOf(' ');
        if (space >= 12) slice = slice[..space];
        return slice.TrimEnd(',', '.', ';', ':', '-') + "…";
    }

    private static string StripLeadingNumber(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsDigit(text[index])) index++;
        if (index == 0 || index >= text.Length || (text[index] != '.' && text[index] != ')')) return text;
        return text[(index + 1)..].TrimStart();
    }
}
