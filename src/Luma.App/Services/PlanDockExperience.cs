namespace Luma.App.Services;

/// <summary>Pure helpers for plan-window collapse/expand (testable without Avalonia).</summary>
public static class PlanDockExperience
{
    public const double ExpandedWidth = 440;
    public const double ExpandedHeight = 520;
    public const double ExpandedMinWidth = 360;
    public const double ExpandedMinHeight = 320;

    public const double CollapsedWidth = 300;
    public const double CollapsedHeight = 52;
    public const double CollapsedMinWidth = 220;
    public const double CollapsedMinHeight = 48;

    /// <summary>Chip / menu wiring strings used by XAML and string-based UI tests.</summary>
    public const string TogglePlanWindowCommandName = "TogglePlanWindowCommand";
    public const string PlanChipVisibleProperty = "PlanChipVisible";
    public const string PlanChipTooltip =
        "Collapse or expand the plan window. Turn plan mode off from the + menu.";

    public static bool ShowEditActions(bool progressTracking) => !progressTracking;

    public static bool ChipVisible(bool planModeEnabled, bool progressTracking) =>
        planModeEnabled || progressTracking;

    public static bool ToggleCollapsed(bool currentlyCollapsed) => !currentlyCollapsed;

    public static (double Width, double Height, double MinWidth, double MinHeight) Layout(bool collapsed) =>
        collapsed
            ? (CollapsedWidth, CollapsedHeight, CollapsedMinWidth, CollapsedMinHeight)
            : (ExpandedWidth, ExpandedHeight, ExpandedMinWidth, ExpandedMinHeight);

    /// <summary>Short label for the mini dock: title · progress.</summary>
    public static string FormatDockCaption(string? title, string? stepSummary)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "Plan" : title.Trim();
        var s = string.IsNullOrWhiteSpace(stepSummary) ? null : stepSummary.Trim();
        return s is null ? t : $"{t} · {s}";
    }
}
