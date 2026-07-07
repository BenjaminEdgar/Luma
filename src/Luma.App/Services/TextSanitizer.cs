using System.Text;

namespace Luma.App.Services;

public static class TextSanitizer
{
    static TextSanitizer() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var normalized = text.Normalize(NormalizationForm.FormC);
        if (!LooksBroken(normalized)) return normalized.Replace('\u00A0', ' ');

        try
        {
            var bytes = Encoding.GetEncoding(1252).GetBytes(normalized);
            var repaired = Encoding.UTF8.GetString(bytes);
            return Better(normalized, repaired).Replace('\u00A0', ' ');
        }
        catch
        {
            return normalized.Replace('\u00A0', ' ');
        }
    }

    private static bool LooksBroken(string text) =>
        text.Contains('\u00C3') || text.Contains('\u00C2') || text.Contains('\u00E2') || text.Contains('\uFFFD');

    private static string Better(string original, string repaired)
    {
        var originalScore = Score(original);
        var repairedScore = Score(repaired);
        return repairedScore < originalScore ? repaired : original;
    }

    private static int Score(string text)
    {
        var score = 0;
        foreach (var c in text)
        {
            if (c is '\u00C3' or '\u00C2' or '\u00E2' or '\uFFFD') score += 5;
            else if (c is '\u0080' or '\u0081' or '\u0082' or '\u0083' or '\u0084' or '\u0085' or '\u0086' or '\u0087' or '\u0088' or '\u0089' or '\u008A' or '\u008B' or '\u008C' or '\u008D' or '\u008E' or '\u008F')
                score += 2;
        }
        return score;
    }
}
