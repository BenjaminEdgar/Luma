namespace Luma.App.Services;

/// <summary>Parses a screen-change digest reply into summary bullets and action chips.</summary>
public static class ScreenDigestParser
{
    public static (string Summary, IReadOnlyList<string> Actions) Parse(string raw, int maxBullets = 3, int maxActions = 3)
    {
        var bullets = new List<string>();
        var actions = new List<string>();
        var inActions = false;
        var sawContent = false;

        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                // Blank line after content separates summary bullets from action chips.
                if (sawContent && bullets.Count > 0) inActions = true;
                continue;
            }

            var looksLikeBullet = trimmed.StartsWith('-') || trimmed.StartsWith('*') || trimmed.StartsWith('•');
            var text = trimmed.TrimStart('-', '*', '•').Trim();
            text = StripLeadingNumber(text).Trim().Trim('"', '\'', '`');
            if (text.Length == 0) continue;
            sawContent = true;

            if (text.EndsWith(':') && text.Length < 24)
            {
                inActions = text.Contains("action", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("next", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("try", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("chip", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (text.StartsWith("ASK_USER", StringComparison.OrdinalIgnoreCase)) continue;

            if (!inActions && bullets.Count >= maxBullets) inActions = true;

            if (!inActions && (looksLikeBullet || bullets.Count == 0))
            {
                if (text.Length is > 0 and <= 90 && bullets.Count < maxBullets
                    && !bullets.Contains(text, StringComparer.OrdinalIgnoreCase))
                    bullets.Add(text);
                // Non-bullet lines after at least one bullet flip to actions.
                if (!looksLikeBullet && bullets.Count > 0) inActions = true;
                else continue;
            }

            if (inActions || !looksLikeBullet)
            {
                if (text.Length is >= 4 and <= 56
                    && actions.Count < maxActions
                    && !actions.Contains(text, StringComparer.OrdinalIgnoreCase)
                    && !bullets.Contains(text, StringComparer.OrdinalIgnoreCase))
                    actions.Add(text);
            }
        }

        if (bullets.Count == 0 && actions.Count == 0)
        {
            foreach (var suggestion in SuggestionParser.Parse(raw, maxActions))
                actions.Add(suggestion);
        }

        var summary = bullets.Count == 0
            ? "Your screen looks different than last time."
            : string.Join('\n', bullets.Select(b => "• " + b));
        return (summary, actions);
    }

    private static string StripLeadingNumber(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsDigit(text[index])) index++;
        if (index == 0 || index >= text.Length || (text[index] != '.' && text[index] != ')')) return text;
        return text[(index + 1)..].Trim();
    }
}
