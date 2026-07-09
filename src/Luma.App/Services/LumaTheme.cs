using Avalonia;
using Avalonia.Media;

namespace Luma.App.Services;

/// <summary>Single source of truth for Luma's violet-glass design system colors and
/// reusable chrome builders used by code-constructed secondary windows.</summary>
public static class LumaTheme
{
    // Core palette (matches App.axaml resources).
    public static readonly Color AccentStart = Color.Parse("#8A63F5");
    public static readonly Color AccentEnd = Color.Parse("#4F7CFF");
    public static readonly Color AccentSoft = Color.Parse("#B3A6FF");
    public static readonly Color InkPanel = Color.Parse("#181A24");
    public static readonly Color InkPanelDeep = Color.Parse("#14161E");
    public static readonly Color TextBright = Color.Parse("#F7F6FF");
    public static readonly Color TextBody = Color.Parse("#E4E2F0");
    public static readonly Color TextMuted = Color.Parse("#A8A4BE");
    public static readonly Color GlassFill = Color.Parse("#F5181A24");
    public static readonly Color GlassFillStrong = Color.Parse("#F2181A24");
    public static readonly Color BorderAccent = Color.Parse("#668A63F5");
    public static readonly Color BorderSoft = Color.Parse("#44FFFFFF");
    public static readonly Color Danger = Color.Parse("#FF6B7A");
    public static readonly Color DangerFill = Color.Parse("#CC361D29");
    public static readonly Color DangerHot = Color.Parse("#F2E33B4E");
    public static readonly Color WarnFill = Color.Parse("#28FFB347");
    public static readonly Color WarnText = Color.Parse("#FFFFD39A");

    public static IBrush AccentSoftBrush { get; } = new SolidColorBrush(AccentSoft);
    public static IBrush TextBrightBrush { get; } = new SolidColorBrush(TextBright);
    public static IBrush TextBodyBrush { get; } = new SolidColorBrush(TextBody);
    public static IBrush TextMutedBrush { get; } = new SolidColorBrush(TextMuted);
    public static IBrush GlassFillBrush { get; } = new SolidColorBrush(GlassFill);
    public static IBrush BorderAccentBrush { get; } = new SolidColorBrush(BorderAccent);

    /// <summary>Prismatic top-to-bottom border used by floating dialogs and the main shell.</summary>
    public static IBrush CreatePanelBorderBrush() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#998A63F5"), 0),
            new GradientStop(BorderSoft, 0.35),
            new GradientStop(Color.Parse("#28FFFFFF"), 1),
        },
    };

    public static IBrush CreateAccentGradient() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(AccentStart, 0),
            new GradientStop(AccentEnd, 1),
        },
    };

    public static BoxShadows FloatingShadow { get; } =
        BoxShadows.Parse("0 18 52 0 #99000000, 0 0 28 0 #338A63F5");

    public static BoxShadows SoftShadow { get; } =
        BoxShadows.Parse("0 18 52 0 #99000000");

    public const double FloatingCornerRadius = 20;
    public const string SectionLabelColor = "#C9BFFF";
}
