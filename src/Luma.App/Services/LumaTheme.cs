using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Luma.App.Services;

/// <summary>Selectable visual themes for the main shell and code-built windows.</summary>
public enum UiThemeId
{
    /// <summary>White → blue gradients, blue outlines (default).</summary>
    Blue = 0,
    /// <summary>Aurora violet → cyan (previous default).</summary>
    Colorful = 1,
    /// <summary>White → mint glass, emerald accents.</summary>
    Emerald = 2,
}

/// <summary>
/// Luma design system. Call <see cref="Apply"/> at startup and when the user changes theme.
/// App.axaml resources + these statics stay in sync.
/// </summary>
public static class LumaTheme
{
    public static UiThemeId CurrentId { get; private set; } = UiThemeId.Blue;

    public static Color AccentStart { get; private set; }
    public static Color AccentEnd { get; private set; }
    public static Color AccentSoft { get; private set; }
    public static Color InkPanel { get; private set; }
    public static Color InkPanelDeep { get; private set; }
    public static Color TextBright { get; private set; }
    public static Color TextBody { get; private set; }
    public static Color TextMuted { get; private set; }
    public static Color GlassFill { get; private set; }
    public static Color GlassFillStrong { get; private set; }
    public static Color BorderAccent { get; private set; }
    public static Color BorderSoft { get; private set; }
    public static Color Danger { get; private set; }
    public static Color DangerFill { get; private set; }
    public static Color DangerHot { get; private set; }
    public static Color WarnFill { get; private set; }
    public static Color WarnText { get; private set; }
    public static Color LiveGreen { get; private set; }

    public static IBrush AccentSoftBrush { get; private set; } = Brushes.DodgerBlue;
    public static IBrush AccentBrush { get; private set; } = Brushes.DodgerBlue;
    public static IBrush TextBrightBrush { get; private set; } = Brushes.Black;
    public static IBrush TextBodyBrush { get; private set; } = Brushes.Black;
    public static IBrush TextMutedBrush { get; private set; } = Brushes.Gray;
    public static IBrush GlassFillBrush { get; private set; } = Brushes.White;
    public static IBrush GlassFillStrongBrush { get; private set; } = Brushes.White;
    public static IBrush BorderAccentBrush { get; private set; } = Brushes.LightGray;
    public static IBrush LiveGreenBrush { get; private set; } = Brushes.LimeGreen;

    public static BoxShadows FloatingShadow { get; private set; }
    public static BoxShadows SoftShadow { get; private set; }

    public const double FloatingCornerRadius = 20;
    public static string SectionLabelColor { get; private set; } = "#64748B";

    public static IReadOnlyList<(UiThemeId Id, string Label)> ThemeChoices { get; } =
    [
        (UiThemeId.Blue, "Blue — white & blue (default)"),
        (UiThemeId.Colorful, "Colorful — violet & cyan"),
        (UiThemeId.Emerald, "Emerald — white & mint"),
    ];

    static LumaTheme()
    {
        // Defaults before Apply (tests / early static use).
        ApplyPalette(BluePalette(), UiThemeId.Blue, app: null);
    }

    public static void ApplyFromSettings()
    {
        var id = ParseThemeId(AppSettings.Current.UiTheme);
        Apply(id, Application.Current);
    }

    public static void Apply(UiThemeId id, Application? app = null)
    {
        app ??= Application.Current;
        ApplyPalette(PaletteFor(id), id, app);
    }

    public static UiThemeId ParseThemeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return UiThemeId.Blue;
        if (int.TryParse(value.Trim(), out var n) && Enum.IsDefined(typeof(UiThemeId), n))
            return (UiThemeId)n;
        if (Enum.TryParse<UiThemeId>(value.Trim(), ignoreCase: true, out var named))
            return named;
        if (value.Contains("emerald", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("mint", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("green", StringComparison.OrdinalIgnoreCase))
            return UiThemeId.Emerald;
        if (value.Contains("color", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("aurora", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("violet", StringComparison.OrdinalIgnoreCase))
            return UiThemeId.Colorful;
        return UiThemeId.Blue;
    }

    public static string ThemeIdToSetting(UiThemeId id) => id.ToString();

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

    public static IBrush CreatePanelFillBrush()
    {
        var p = PaletteFor(CurrentId);
        return Linear(p.PanelFill0, p.PanelFill1, p.PanelFill2, vertical: true);
    }

    private static ThemePalette PaletteFor(UiThemeId id) => id switch
    {
        UiThemeId.Colorful => ColorfulPalette(),
        UiThemeId.Emerald => EmeraldPalette(),
        _ => BluePalette(),
    };

    private static void ApplyPalette(ThemePalette p, UiThemeId id, Application? app)
    {
        CurrentId = id;
        AccentStart = p.AccentStart;
        AccentEnd = p.AccentEnd;
        AccentSoft = p.AccentSoft;
        InkPanel = p.InkPanel;
        InkPanelDeep = p.InkPanelDeep;
        TextBright = p.TextBright;
        TextBody = p.TextBody;
        TextMuted = p.TextMuted;
        GlassFill = p.GlassFill;
        GlassFillStrong = p.GlassFillStrong;
        BorderAccent = p.BorderAccent;
        BorderSoft = p.BorderSoft;
        Danger = p.Danger;
        DangerFill = p.DangerFill;
        DangerHot = p.DangerHot;
        WarnFill = p.WarnFill;
        WarnText = p.WarnText;
        LiveGreen = p.LiveGreen;
        SectionLabelColor = p.SectionLabelHex;

        AccentSoftBrush = new SolidColorBrush(AccentSoft);
        AccentBrush = new SolidColorBrush(AccentStart);
        TextBrightBrush = new SolidColorBrush(TextBright);
        TextBodyBrush = new SolidColorBrush(TextBody);
        TextMutedBrush = new SolidColorBrush(TextMuted);
        GlassFillBrush = new SolidColorBrush(GlassFill);
        GlassFillStrongBrush = new SolidColorBrush(GlassFillStrong);
        BorderAccentBrush = new SolidColorBrush(BorderAccent);
        LiveGreenBrush = new SolidColorBrush(LiveGreen);

        FloatingShadow = BoxShadows.Parse(p.FloatingShadow);
        SoftShadow = BoxShadows.Parse(p.SoftShadow);

        if (app?.Resources is null) return;
        var r = app.Resources;

        r["AccentStart"] = AccentStart;
        r["AccentEnd"] = AccentEnd;
        r["AccentSoft"] = AccentSoft;
        r["InkPanel"] = InkPanel;
        r["TextBright"] = TextBright;
        r["TextBody"] = TextBody;
        r["TextMuted"] = TextMuted;

        r["AccentBrush"] = new SolidColorBrush(AccentStart);
        r["AccentSoftBrush"] = new SolidColorBrush(AccentSoft);
        r["AccentEndBrush"] = new SolidColorBrush(AccentEnd);
        r["TextBrightBrush"] = new SolidColorBrush(TextBright);
        r["TextBodyBrush"] = new SolidColorBrush(TextBody);
        r["TextMutedBrush"] = new SolidColorBrush(TextMuted);

        r["AccentGradient"] = Linear(AccentStart, AccentSoft, AccentEnd, horizontal: true);
        r["AccentGradientBright"] = Linear(p.AccentBright0, p.AccentBright1, p.AccentBright2, horizontal: true);
        r["StarMarkBrush"] = Linear(p.Star0, AccentStart, AccentSoft, AccentEnd);
        r["UserBubbleBrush"] = Linear(p.UserBubble0, p.UserBubble1);
        r["AssistantBubbleBrush"] = Linear(p.AssistantBubble0, p.AssistantBubble1);
        r["StreamEnergyBrush"] = StreamEnergy(AccentStart, AccentEnd);
        r["ModuleInsetBrush"] = Linear(
            Color.FromArgb(0x88, 255, 255, 255),
            Color.FromArgb(0x00, 255, 255, 255),
            vertical: true);
        r["SplitBrainBorderBrush"] = Linear(
            Color.FromArgb(0xAA, AccentStart.R, AccentStart.G, AccentStart.B),
            Color.FromArgb(0xAA, AccentEnd.R, AccentEnd.G, AccentEnd.B),
            horizontal: true);
        r["PanelFillBrush"] = Linear(p.PanelFill0, p.PanelFill1, p.PanelFill2, vertical: true);
        r["SurfaceGlassBrush"] = new SolidColorBrush(p.SurfaceGlass);
        r["QuietGlassBrush"] = new SolidColorBrush(p.QuietGlass);
        r["ComposeFillBrush"] = new SolidColorBrush(p.ComposeFill);
        r["PanelBorderBrush"] = new SolidColorBrush(BorderAccent);
        r["PanelBorderBusyBrush"] = new SolidColorBrush(p.PanelBorderBusy);
        r["PanelBorderWritingBrush"] = new SolidColorBrush(p.PanelBorderWriting);
        r["HeaderSheenBrush"] = Sheen(AccentStart, AccentEnd);
        r["PrismEdgeBrush"] = Sheen(AccentStart, AccentEnd);
        r["PrismEdgeVerticalBrush"] = Linear(
            Color.FromArgb(0x88, AccentStart.R, AccentStart.G, AccentStart.B),
            Color.FromArgb(0x00, BorderAccent.R, BorderAccent.G, BorderAccent.B),
            vertical: true);
        r["ComposeBorderBrush"] = new SolidColorBrush(BorderSoft);
        r["ComposeBorderListeningBrush"] = Linear(AccentStart, AccentSoft, AccentEnd, horizontal: true);
        r["DockGlowBrush"] = Radial(
            Color.FromArgb(0xCC, AccentStart.R, AccentStart.G, AccentStart.B),
            Color.FromArgb(0x88, AccentSoft.R, AccentSoft.G, AccentSoft.B),
            Color.FromArgb(0x44, AccentEnd.R, AccentEnd.G, AccentEnd.B));
        r["OrbTopBrush"] = Radial(Color.FromArgb(0x55, AccentStart.R, AccentStart.G, AccentStart.B));
        r["OrbMidBrush"] = Radial(Color.FromArgb(0x33, AccentEnd.R, AccentEnd.G, AccentEnd.B), centerX: 0.8, centerY: 0.4);
        r["OrbBottomBrush"] = Radial(p.OrbBottom, centerX: 0.5, centerY: 0.95);
        r["ThinkingBrush"] = Conic(AccentStart, AccentEnd);
    }

    private static ThemePalette BluePalette() => new()
    {
        // White → blue glass
        AccentStart = Color.Parse("#2563EB"),      // blue-600
        AccentEnd = Color.Parse("#38BDF8"),        // sky-400
        AccentSoft = Color.Parse("#3B82F6"),       // blue-500
        AccentBright0 = Color.Parse("#3B82F6"),
        AccentBright1 = Color.Parse("#60A5FA"),
        AccentBright2 = Color.Parse("#7DD3FC"),
        Star0 = Color.Parse("#93C5FD"),
        InkPanel = Color.Parse("#F0F7FF"),
        InkPanelDeep = Color.Parse("#DBEAFE"),
        TextBright = Color.Parse("#0F172A"),
        TextBody = Color.Parse("#1E293B"),
        TextMuted = Color.Parse("#64748B"),
        GlassFill = Color.Parse("#F8FBFF"),
        GlassFillStrong = Color.Parse("#FFFFFF"),
        BorderAccent = Color.Parse("#BFDBFE"),
        BorderSoft = Color.Parse("#DBEAFE"),
        SurfaceGlass = Color.Parse("#FFFFFF"),
        QuietGlass = Color.Parse("#F0F7FF"),
        ComposeFill = Color.Parse("#FFFFFF"),
        PanelFill0 = Color.Parse("#FFFFFF"),
        PanelFill1 = Color.Parse("#F0F7FF"),
        PanelFill2 = Color.Parse("#E0EFFF"),
        UserBubble0 = Color.Parse("#F8FBFF"),
        UserBubble1 = Color.Parse("#DBEAFE"),
        AssistantBubble0 = Color.Parse("#FFFFFF"),
        AssistantBubble1 = Color.Parse("#F0F7FF"),
        PanelBorderBusy = Color.Parse("#93C5FD"),
        PanelBorderWriting = Color.Parse("#34D399"),
        OrbBottom = Color.Parse("#28BFDBFE"),
        Danger = Color.Parse("#E5455A"),
        DangerFill = Color.Parse("#FEE2E2"),
        DangerHot = Color.Parse("#FECACA"),
        WarnFill = Color.Parse("#FEF3C7"),
        WarnText = Color.Parse("#B45309"),
        LiveGreen = Color.Parse("#10B981"),
        SectionLabelHex = "#64748B",
        FloatingShadow = "0 20 50 0 #140F172A, 0 0 40 0 #222563EB",
        SoftShadow = "0 10 28 0 #120F172A",
    };

    private static ThemePalette ColorfulPalette() => new()
    {
        // Aurora: violet → cyan (previous default)
        AccentStart = Color.Parse("#7C4DFF"),
        AccentEnd = Color.Parse("#00E5FF"),
        AccentSoft = Color.Parse("#2563FF"),
        AccentBright0 = Color.Parse("#9470FF"),
        AccentBright1 = Color.Parse("#3B82F6"),
        AccentBright2 = Color.Parse("#22EEFF"),
        Star0 = Color.Parse("#A78BFF"),
        InkPanel = Color.Parse("#F2F4FF"),
        InkPanelDeep = Color.Parse("#E8E4F8"),
        TextBright = Color.Parse("#080F23"),
        TextBody = Color.Parse("#1E293B"),
        TextMuted = Color.Parse("#6B7280"),
        GlassFill = Color.Parse("#F8F7FF"),
        GlassFillStrong = Color.Parse("#FFFFFF"),
        BorderAccent = Color.Parse("#E8E4F8"),
        BorderSoft = Color.Parse("#E0DCF5"),
        SurfaceGlass = Color.Parse("#FFFFFF"),
        QuietGlass = Color.Parse("#F8F7FF"),
        ComposeFill = Color.Parse("#FFFFFF"),
        PanelFill0 = Color.Parse("#FFFEFF"),
        PanelFill1 = Color.Parse("#F2F4FF"),
        PanelFill2 = Color.Parse("#F8F0FF"),
        UserBubble0 = Color.Parse("#FBFAFF"),
        UserBubble1 = Color.Parse("#F3F0FF"),
        AssistantBubble0 = Color.Parse("#FFFFFF"),
        AssistantBubble1 = Color.Parse("#F8F7FF"),
        PanelBorderBusy = Color.Parse("#B8A6FF"),
        PanelBorderWriting = Color.Parse("#7FFFDA"),
        OrbBottom = Color.Parse("#28FFB6C7"),
        Danger = Color.Parse("#E5455A"),
        DangerFill = Color.Parse("#FEE2E2"),
        DangerHot = Color.Parse("#FECACA"),
        WarnFill = Color.Parse("#FEF3C7"),
        WarnText = Color.Parse("#B45309"),
        LiveGreen = Color.Parse("#10B981"),
        SectionLabelHex = "#6B7280",
        FloatingShadow = "0 20 50 0 #14080F23, 0 0 40 0 #227C4DFF",
        SoftShadow = "0 10 28 0 #12080F23",
    };

    private static ThemePalette EmeraldPalette() => new()
    {
        // White → mint glass, emerald accents
        AccentStart = Color.Parse("#059669"),      // emerald-600
        AccentEnd = Color.Parse("#34D399"),        // emerald-400
        AccentSoft = Color.Parse("#10B981"),       // emerald-500
        AccentBright0 = Color.Parse("#10B981"),
        AccentBright1 = Color.Parse("#34D399"),
        AccentBright2 = Color.Parse("#6EE7B7"),
        Star0 = Color.Parse("#6EE7B7"),
        InkPanel = Color.Parse("#F0FDF8"),
        InkPanelDeep = Color.Parse("#D1FAE5"),
        TextBright = Color.Parse("#0F172A"),
        TextBody = Color.Parse("#1E293B"),
        TextMuted = Color.Parse("#64748B"),
        GlassFill = Color.Parse("#F7FDFB"),
        GlassFillStrong = Color.Parse("#FFFFFF"),
        BorderAccent = Color.Parse("#A7F3D0"),
        BorderSoft = Color.Parse("#D1FAE5"),
        SurfaceGlass = Color.Parse("#FFFFFF"),
        QuietGlass = Color.Parse("#F0FDF8"),
        ComposeFill = Color.Parse("#FFFFFF"),
        PanelFill0 = Color.Parse("#FFFFFF"),
        PanelFill1 = Color.Parse("#F0FDF8"),
        PanelFill2 = Color.Parse("#DCFCE7"),
        UserBubble0 = Color.Parse("#F7FDFB"),
        UserBubble1 = Color.Parse("#D1FAE5"),
        AssistantBubble0 = Color.Parse("#FFFFFF"),
        AssistantBubble1 = Color.Parse("#F0FDF8"),
        PanelBorderBusy = Color.Parse("#6EE7B7"),
        PanelBorderWriting = Color.Parse("#2DD4BF"),
        OrbBottom = Color.Parse("#28D1FAE5"),
        Danger = Color.Parse("#E5455A"),
        DangerFill = Color.Parse("#FEE2E2"),
        DangerHot = Color.Parse("#FECACA"),
        WarnFill = Color.Parse("#FEF3C7"),
        WarnText = Color.Parse("#B45309"),
        LiveGreen = Color.Parse("#059669"),
        SectionLabelHex = "#64748B",
        FloatingShadow = "0 20 50 0 #140F172A, 0 0 40 0 #22059669",
        SoftShadow = "0 10 28 0 #120F172A",
    };

    private sealed class ThemePalette
    {
        public Color AccentStart, AccentEnd, AccentSoft;
        public Color AccentBright0, AccentBright1, AccentBright2, Star0;
        public Color InkPanel, InkPanelDeep, TextBright, TextBody, TextMuted;
        public Color GlassFill, GlassFillStrong, BorderAccent, BorderSoft;
        public Color SurfaceGlass, QuietGlass, ComposeFill;
        public Color PanelFill0, PanelFill1, PanelFill2;
        public Color UserBubble0, UserBubble1, AssistantBubble0, AssistantBubble1;
        public Color PanelBorderBusy, PanelBorderWriting, OrbBottom;
        public Color Danger, DangerFill, DangerHot, WarnFill, WarnText, LiveGreen;
        public string SectionLabelHex = "#64748B";
        public string FloatingShadow = "";
        public string SoftShadow = "";
    }

    private static LinearGradientBrush Linear(Color a, Color b, Color? c = null, Color? d = null, bool horizontal = false, bool vertical = false)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = horizontal
                ? new RelativePoint(1, 0, RelativeUnit.Relative)
                : vertical
                    ? new RelativePoint(0.5, 1, RelativeUnit.Relative)
                    : new RelativePoint(1, 1, RelativeUnit.Relative),
        };
        brush.GradientStops.Add(new GradientStop(a, 0));
        if (c is null)
        {
            brush.GradientStops.Add(new GradientStop(b, 1));
        }
        else if (d is null)
        {
            brush.GradientStops.Add(new GradientStop(b, 0.45));
            brush.GradientStops.Add(new GradientStop(c.Value, 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(b, 0.4));
            brush.GradientStops.Add(new GradientStop(c.Value, 0.75));
            brush.GradientStops.Add(new GradientStop(d.Value, 1));
        }
        return brush;
    }

    private static LinearGradientBrush StreamEnergy(Color start, Color end) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, start.R, start.G, start.B), 0),
            new GradientStop(Color.FromArgb(255, start.R, start.G, start.B), 0.35),
            new GradientStop(Color.FromArgb(255, end.R, end.G, end.B), 0.75),
            new GradientStop(Color.FromArgb(0, end.R, end.G, end.B), 1),
        },
    };

    private static LinearGradientBrush Sheen(Color start, Color end) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, start.R, start.G, start.B), 0),
            new GradientStop(Color.FromArgb(0x88, start.R, start.G, start.B), 0.35),
            new GradientStop(Color.FromArgb(0x88, end.R, end.G, end.B), 0.7),
            new GradientStop(Color.FromArgb(0, end.R, end.G, end.B), 1),
        },
    };

    private static RadialGradientBrush Radial(Color core, Color? mid = null, Color? outer = null, double centerX = 0.5, double centerY = 0.5) => new()
    {
        Center = new RelativePoint(centerX, centerY, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(centerX, centerY, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(core, 0),
            new GradientStop(mid ?? Color.FromArgb(0, core.R, core.G, core.B), mid is null ? 1 : 0.55),
            new GradientStop(outer ?? Colors.Transparent, 1),
        },
    };

    private static ConicGradientBrush Conic(Color start, Color end) => new()
    {
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, start.R, start.G, start.B), 0),
            new GradientStop(start, 0.45),
            new GradientStop(end, 0.85),
            new GradientStop(Color.FromArgb(0, start.R, start.G, start.B), 1),
        },
    };
}
