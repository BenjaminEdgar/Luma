using System.Text.RegularExpressions;

namespace Luma.App.Services;

/// <summary>Builds suggestion chips from local OCR text without calling a vision model.</summary>
public static partial class LocalOcrSuggestions
{
    public static IReadOnlyList<string> FromOcrContext(string? ocrContext, int count)
    {
        count = Math.Clamp(count, 1, 5);
        if (string.IsNullOrWhiteSpace(ocrContext))
            return Array.Empty<string>();

        var lines = ExtractUsefulLines(ocrContext);
        if (lines.Count == 0)
        {
            return new[] { "Explain this screen", "Summarize visible text", "What should I do next?" }
                .Take(count)
                .ToList();
        }

        var chips = new List<string>();
        foreach (var line in lines)
        {
            if (chips.Count >= count) break;
            var chip = ToChip(line);
            if (chips.Any(c => string.Equals(c, chip, StringComparison.OrdinalIgnoreCase)))
                continue;
            chips.Add(chip);
        }

        if (chips.Count < count && !chips.Contains("Explain this screen", StringComparer.OrdinalIgnoreCase))
            chips.Add("Explain this screen");
        if (chips.Count < count && !chips.Contains("What should I do next?", StringComparer.OrdinalIgnoreCase))
            chips.Add("What should I do next?");

        return chips.Take(count).ToList();
    }

    public static (string Summary, IReadOnlyList<string> Actions) DigestFromOcr(string? ocrContext)
    {
        var lines = ExtractUsefulLines(ocrContext);
        if (lines.Count == 0)
        {
            return (
                "Screen text was captured locally, but little readable content was found.",
                ["Explain this screen", "Start new chat"]);
        }

        var bullets = lines.Take(3).Select(l => "- " + Truncate(l, 90));
        var summary = "On-device OCR (no vision model):\n" + string.Join("\n", bullets);
        var actions = FromOcrContext(ocrContext, 3).Concat(["Start new chat"]).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        return (summary, actions);
    }

    private static List<string> ExtractUsefulLines(string? ocrContext)
    {
        if (string.IsNullOrWhiteSpace(ocrContext)) return new List<string>();

        var lines = new List<string>();
        // Prefer "Full text:" section when present
        var fullIdx = ocrContext.IndexOf("Full text:", StringComparison.OrdinalIgnoreCase);
        var body = fullIdx >= 0 ? ocrContext[(fullIdx + "Full text:".Length)..] : ocrContext;
        var blocksIdx = body.IndexOf("Blocks (", StringComparison.OrdinalIgnoreCase);
        if (blocksIdx >= 0) body = body[..blocksIdx];

        foreach (var raw in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = raw.Trim();
            if (line.Length < 2 || line.StartsWith('(') || line.StartsWith("LOCAL OCR", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith('[') &&
                (line.Contains("conf=", StringComparison.Ordinal) || line.Contains("\" @", StringComparison.Ordinal)))
            {
                // Block line: [0] "text" @x,y,w,h (or legacy [0] "text" conf=...)
                var m = BlockText().Match(line);
                if (m.Success) line = m.Groups[1].Value.Trim();
                else continue;
            }
            if (line.Equals("(none)", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Length > 120) line = line[..120].TrimEnd() + "…";
            if (lines.Any(x => string.Equals(x, line, StringComparison.OrdinalIgnoreCase))) continue;
            lines.Add(line);
            if (lines.Count >= 12) break;
        }

        return lines;
    }

    private static string ToChip(string line)
    {
        var t = Truncate(line, 42);
        if (LooksLikeError(t))
            return "Fix: " + t;
        if (t.EndsWith('?'))
            return "Answer: " + t.TrimEnd('?');
        return "Explain: " + t;
    }

    private static bool LooksLikeError(string t) =>
        t.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("warning", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "…";

    [GeneratedRegex("\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex BlockText();
}
