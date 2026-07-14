using System.Globalization;
using System.Text;
using Luma.Ocr.Results;

namespace Luma.App.Services;

/// <summary>Formats on-device OCR results for provider prompts (testable, no Avalonia).</summary>
public static class LocalOcrPrompt
{
    public const int DefaultMaxChars = 12_000;

    /// <summary>Blocks below this confidence are noise on rendered screen text.</summary>
    public const float MinUsefulConfidence = 0.5f;

    /// <summary>
    /// Builds a prompt block: full text grouped into visual rows (reads like the screen),
    /// then compact per-block normalized coordinates for pointing (SHOW_WHERE).
    /// Returns null when there is nothing useful to send.
    /// </summary>
    public static string? Format(OcrResult? result, string label = "Screen", int maxChars = DefaultMaxChars)
    {
        if (result is null || (string.IsNullOrWhiteSpace(result.FullText) && result.Blocks.Count == 0))
            return null;

        var rows = result.GroupIntoRows();
        var kept = rows.SelectMany(r => r).Where(b =>
            b.Confidence >= MinUsefulConfidence && !string.IsNullOrWhiteSpace(b.Text)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"LOCAL OCR — {label} ({result.ImageSize.Width}×{result.ImageSize.Height} px, on-device):");
        sb.AppendLine("Exact transcription of visible text; prefer it over reading pixels. Rows read left-to-right like the screen.");
        sb.AppendLine("@x,y,w,h are 0–1 fractions of this image (same system as SHOW_WHERE when the image is the full screen).");
        sb.AppendLine();
        sb.AppendLine("Full text:");
        if (rows.Count == 0)
        {
            sb.AppendLine(string.IsNullOrWhiteSpace(result.FullText) ? "(none)" : result.FullText.Trim());
        }
        else
        {
            // Text gets at most half the budget so block coordinates always fit too.
            var textBudget = maxChars > 0 ? maxChars / 2 : int.MaxValue;
            var written = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                var line = string.Join("  ", rows[i]
                    .Where(b => !string.IsNullOrWhiteSpace(b.Text))
                    .Select(b => CleanText(b.Text)));
                if (line.Length == 0) continue;
                if (written + line.Length > textBudget)
                {
                    sb.AppendLine($"(+{rows.Count - i} more rows omitted)");
                    break;
                }
                sb.AppendLine(line);
                written += line.Length + 2;
            }
        }
        sb.AppendLine();
        sb.AppendLine("Blocks (reading order):");

        // Emit whole block lines until the budget is hit — never cut a line mid-way.
        var headerLength = sb.Length;
        var blockBudget = maxChars > 0 ? maxChars - headerLength : int.MaxValue;
        var emitted = 0;
        foreach (var b in kept)
        {
            var text = CleanText(b.Text);
            if (text.Length > 120) text = text[..120] + "…";
            var line = $"[{b.Index}] \"{text}\" @{N(b.Normalized.X)},{N(b.Normalized.Y)},{N(b.Normalized.Width)},{N(b.Normalized.Height)}";
            if (blockBudget - line.Length - 2 < 0) break;
            sb.AppendLine(line);
            blockBudget -= line.Length + 2;
            emitted++;
        }
        var omitted = kept.Count - emitted;
        var lowConf = result.Blocks.Count - kept.Count;
        if (omitted > 0 || lowConf > 0)
        {
            var parts = new List<string>();
            if (omitted > 0) parts.Add($"{omitted} more");
            if (lowConf > 0) parts.Add($"{lowConf} low-confidence");
            sb.AppendLine($"(+{string.Join(", ", parts)} blocks omitted)");
        }

        return sb.ToString().TrimEnd();
    }

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string CleanText(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>Joins region + full-screen OCR sections.</summary>
    public static string? Combine(string? focusSection, string? screenSection, int maxChars = DefaultMaxChars)
    {
        if (string.IsNullOrWhiteSpace(focusSection) && string.IsNullOrWhiteSpace(screenSection))
            return null;
        if (string.IsNullOrWhiteSpace(focusSection)) return Trim(screenSection!, maxChars);
        if (string.IsNullOrWhiteSpace(screenSection)) return Trim(focusSection!, maxChars);
        return Trim(focusSection!.TrimEnd() + "\n\n" + screenSection.TrimEnd(), maxChars);
    }

    private static string Trim(string s, int maxChars)
    {
        if (maxChars > 0 && s.Length > maxChars)
            return s[..maxChars] + "\n…[OCR trimmed]";
        return s;
    }
}
