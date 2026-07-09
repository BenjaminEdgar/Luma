namespace Luma.App.Services;

/// <summary>Pure state for dock ↔ panel open/close experience. MainWindow applies these
/// values so expand/collapse behavior can be unit-tested without spinning up Avalonia.</summary>
public readonly record struct ShellVisualState(
    bool Expanded,
    bool DockVisible,
    bool PanelVisible,
    double MinWidth,
    double MinHeight,
    double Width,
    double Height,
    double PanelOpacity,
    string PanelTransform,
    bool FocusCompose);

public static class ShellExperience
{
    public const double DockSize = 52;
    public const double ExpandedMinWidth = 480;
    public const double ExpandedMinHeight = 520;
    public const string CollapsedTransform = "translateY(14px) scale(0.96)";
    public const string ExpandedTransform = "translateY(0px) scale(1)";

    /// <summary>Duration of the panel opacity/transform transition (must match MainWindow.axaml).</summary>
    public static readonly TimeSpan PanelTransitionDuration = TimeSpan.FromMilliseconds(300);

    public static (int Width, int Height) ClampPanelSize(int panelWidth, int panelHeight) =>
        (Math.Clamp(panelWidth, (int)ExpandedMinWidth, 3000),
         Math.Clamp(panelHeight, (int)ExpandedMinHeight, 3000));

    /// <summary>Fully open shell: panel visible at stored size, dock hidden, compose focused.</summary>
    public static ShellVisualState Expanded(int panelWidth, int panelHeight)
    {
        var (width, height) = ClampPanelSize(panelWidth, panelHeight);
        return new ShellVisualState(
            Expanded: true,
            DockVisible: false,
            PanelVisible: true,
            MinWidth: ExpandedMinWidth,
            MinHeight: ExpandedMinHeight,
            Width: width,
            Height: height,
            PanelOpacity: 1,
            PanelTransform: ExpandedTransform,
            FocusCompose: true);
    }

    /// <summary>Open seed pose: panel already laid out at full size but transparent/scaled so
    /// the XAML transition can animate into <see cref="Expanded"/>.</summary>
    public static ShellVisualState ExpandSeed(int panelWidth, int panelHeight)
    {
        var open = Expanded(panelWidth, panelHeight);
        return open with { PanelOpacity = 0, PanelTransform = CollapsedTransform, FocusCompose = false };
    }

    /// <summary>Closing animation pose: keep panel and expanded window size visible so opacity/
    /// transform transitions can run; dock stays hidden until <see cref="CollapsedDock"/>.</summary>
    public static ShellVisualState CollapseAnimating(int panelWidth, int panelHeight)
    {
        var (width, height) = ClampPanelSize(panelWidth, panelHeight);
        return new ShellVisualState(
            Expanded: false,
            DockVisible: false,
            PanelVisible: true,
            MinWidth: ExpandedMinWidth,
            MinHeight: ExpandedMinHeight,
            Width: width,
            Height: height,
            PanelOpacity: 0,
            PanelTransform: CollapsedTransform,
            FocusCompose: false);
    }

    /// <summary>Final dock-only shell after the collapse transition finishes.</summary>
    public static ShellVisualState CollapsedDock() => new(
        Expanded: false,
        DockVisible: true,
        PanelVisible: false,
        MinWidth: DockSize,
        MinHeight: DockSize,
        Width: DockSize,
        Height: DockSize,
        PanelOpacity: 0,
        PanelTransform: CollapsedTransform,
        FocusCompose: false);

    /// <summary>Legacy helper used by tests that only need the terminal expanded/collapsed states.</summary>
    public static ShellVisualState ForExpanded(bool expanded, int panelWidth, int panelHeight) =>
        expanded ? Expanded(panelWidth, panelHeight) : CollapsedDock();
}
