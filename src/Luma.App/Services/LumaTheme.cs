using Avalonia;
using Avalonia.Media;

namespace Luma.App.Services;

/// <summary>Luma Aurora brand system — keep in sync with <c>App.axaml</c> and the brand boards
/// (violet → cyan, soft mist surfaces, deep-space text).</summary>
public static class LumaTheme
{
    public static readonly Color AccentStart = Color.Parse("#7C4DFF");
    public static readonly Color AccentEnd = Color.Parse("#00E5FF");
    public static readonly Color AccentSoft = Color.Parse("#2563FF");
    /// <summary>Mist panel surface from brand board.</summary>
    public static readonly Color InkPanel = Color.Parse("#F2F4FF");
    public static readonly Color InkPanelDeep = Color.Parse("#E8E4F8");
    public static readonly Color TextBright = Color.Parse("#080F23");
    public static readonly Color TextBody = Color.Parse("#1E293B");
    public static readonly Color TextMuted = Color.Parse("#6B7280");
    public static readonly Color GlassFill = Color.Parse("#F8F7FF");
    public static readonly Color GlassFillStrong = Color.Parse("#FFFFFF");
    public static readonly Color BorderAccent = Color.Parse("#E8E4F8");
    public static readonly Color BorderSoft = Color.Parse("#E0DCF5");
    public static readonly Color Danger = Color.Parse("#E5455A");
    public static readonly Color DangerFill = Color.Parse("#FEE2E2");
    public static readonly Color DangerHot = Color.Parse("#FECACA");
    public static readonly Color WarnFill = Color.Parse("#FEF3C7");
    public static readonly Color WarnText = Color.Parse("#B45309");
    public static readonly Color LiveGreen = Color.Parse("#10B981");

    public static IBrush AccentSoftBrush { get; } = new SolidColorBrush(AccentSoft);
    public static IBrush TextBrightBrush { get; } = new SolidColorBrush(TextBright);
    public static IBrush TextBodyBrush { get; } = new SolidColorBrush(TextBody);
    public static IBrush TextMutedBrush { get; } = new SolidColorBrush(TextMuted);
    public static IBrush GlassFillBrush { get; } = new SolidColorBrush(GlassFill);
    public static IBrush GlassFillStrongBrush { get; } = new SolidColorBrush(GlassFillStrong);
    public static IBrush BorderAccentBrush { get; } = new SolidColorBrush(BorderAccent);
    public static IBrush LiveGreenBrush { get; } = new SolidColorBrush(LiveGreen);

    public static IBrush CreatePanelBorderBrush() => new SolidColorBrush(BorderSoft);

    public static IBrush CreateAccentGradient() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(AccentStart, 0),
            new GradientStop(AccentSoft, 0.45),
            new GradientStop(AccentEnd, 1),
        },
    };

    public static IBrush CreatePanelFillBrush() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#FFFFFEFF"), 0),
            new GradientStop(Color.Parse("#FFF2F4FF"), 0.55),
            new GradientStop(Color.Parse("#FFF8F0FF"), 1),
        },
    };

    public static BoxShadows FloatingShadow { get; } =
        BoxShadows.Parse("0 20 50 0 #14080F23, 0 0 40 0 #227C4DFF");

    public static BoxShadows SoftShadow { get; } =
        BoxShadows.Parse("0 10 28 0 #12080F23");

    public const double FloatingCornerRadius = 20;
    public const string SectionLabelColor = "#6B7280";
}
