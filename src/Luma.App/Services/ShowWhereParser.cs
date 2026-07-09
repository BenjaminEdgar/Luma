using System.Globalization;
using System.Text.RegularExpressions;

namespace Luma.App.Services;

/// <summary>Parses optional SHOW_WHERE directives so Luma can point at the screen.</summary>
public static partial class ShowWhereParser
{
    // SHOW_WHERE: 0.12,0.40,0.20,0.15
    // SHOW_WHERE: Apply button | 0.12,0.40,0.20,0.15
    [GeneratedRegex(
        @"(?:\r?\n|^)[ \t]*SHOW_WHERE:[ \t]*(?:(?<label>[^|\r\n]+?)\s*\|\s*)?(?<x>0?\.\d+|1(?:\.0+)?|0)\s*,\s*(?<y>0?\.\d+|1(?:\.0+)?|0)\s*,\s*(?<w>0?\.\d+|1(?:\.0+)?|0)\s*,\s*(?<h>0?\.\d+|1(?:\.0+)?|0)[ \t]*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static (string Text, ShowWhereTarget? Target) Extract(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (string.Empty, null);
        var match = Pattern().Match(raw);
        if (!match.Success) return (raw, null);

        var label = match.Groups["label"].Success ? match.Groups["label"].Value.Trim() : null;
        if (!TryParse(match.Groups["x"].Value, out var x) ||
            !TryParse(match.Groups["y"].Value, out var y) ||
            !TryParse(match.Groups["w"].Value, out var w) ||
            !TryParse(match.Groups["h"].Value, out var h))
            return (raw, null);

        var cleaned = (raw[..match.Index] + raw[(match.Index + match.Length)..]).Trim();
        return (cleaned, new ShowWhereTarget(x, y, w, h, string.IsNullOrWhiteSpace(label) ? null : label));
    }

    private static bool TryParse(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

public sealed record ShowWhereTarget(double X, double Y, double Width, double Height, string? Label);
