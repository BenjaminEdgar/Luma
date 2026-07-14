namespace Luma.App.Services;

/// <summary>
/// Decides what screen evidence to send to the provider.
/// Prefer on-device OCR text over vision/screenshot tokens whenever OCR is usable.
/// </summary>
public static class ScreenEvidence
{
    public static bool HasUsableOcr(string? localOcrContext) =>
        !string.IsNullOrWhiteSpace(localOcrContext);

    /// <summary>
    /// When local OCR is enabled and prefer-over-vision is on and OCR produced text,
    /// strip image paths so the provider does not burn vision tokens.
    /// UI may still show captures; only the provider request is affected.
    /// </summary>
    public static (string? Region, string? Context) ImagesForProvider(
        string? regionPath,
        string? contextPath,
        string? localOcrContext)
    {
        if (PrefersOcrOverVision(localOcrContext))
            return (null, null);
        return (regionPath, contextPath);
    }

    public static bool PrefersOcrOverVision(string? localOcrContext) =>
        AppSettings.Current.LocalOcrEnabled &&
        AppSettings.Current.LocalOcrPreferOverVision &&
        HasUsableOcr(localOcrContext);

    /// <summary>
    /// True when we should skip the suggestion/digest LLM entirely and use OCR-derived chips/text.
    /// </summary>
    public static bool PreferLocalSuggestions(string? localOcrContext) =>
        PrefersOcrOverVision(localOcrContext);
}
