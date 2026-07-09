using System.Text.RegularExpressions;

namespace Luma.App.Services;

/// <summary>Rewrites a compose draft into a clearer prompt without answering it.</summary>
public static class PromptImprove
{
    /// <summary>User-facing request body: instruction + draft. Reply must be the rewrite only.</summary>
    public static string BuildRequest(string draft)
    {
        draft = (draft ?? string.Empty).Trim();
        return
            "Rewrite the user's draft so it is a clearer, more effective prompt for an AI assistant.\n" +
            "Rules:\n" +
            "- Reply with only the improved prompt text (no quotes, labels, bullets, or preamble)\n" +
            "- Keep the user's intent, constraints, file paths, names, and technical terms\n" +
            "- Prefer concrete, actionable language; fill obvious gaps only when safe\n" +
            "- Do not answer the prompt or add commentary\n" +
            "- Keep a similar length unless the draft is vague (then make it specific)\n" +
            "Draft:\n" +
            draft;
    }

    /// <summary>Strips model garnish so the compose box gets clean rewrite text.</summary>
    public static string? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = TextSanitizer.Clean(raw).Trim();
        if (text.Length == 0) return null;

        // Drop a single fenced block wrapper if the model ignored the rules.
        var fence = Regex.Match(text, @"^```(?:\w+)?\s*\r?\n([\s\S]*?)\r?\n```\s*$", RegexOptions.CultureInvariant);
        if (fence.Success) text = fence.Groups[1].Value.Trim();

        // Strip common lead-ins.
        text = Regex.Replace(text,
            @"^(?:improved\s+prompt|rewritten\s+prompt|here(?:'s| is)\s+(?:an?\s+)?improved\s+(?:version|prompt)|prompt)\s*[:\-–—]\s*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        // Unwrap one layer of matching quotes.
        if (text.Length >= 2)
        {
            var a = text[0];
            var b = text[^1];
            if ((a == '"' && b == '"') || (a == '\'' && b == '\'') || (a == '`' && b == '`') ||
                (a == '“' && b == '”') || (a == '‘' && b == '’'))
                text = text[1..^1].Trim();
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
