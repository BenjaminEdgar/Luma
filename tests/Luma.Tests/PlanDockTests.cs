using Luma.App.Services;

namespace Luma.Tests;

public sealed class PlanDockTests
{
    [Fact]
    public void LayoutExpandedUsesChecklistPanelSize()
    {
        var layout = PlanDockExperience.Layout(collapsed: false);
        Assert.Equal(PlanDockExperience.ExpandedWidth, layout.Width);
        Assert.Equal(PlanDockExperience.ExpandedHeight, layout.Height);
        Assert.True(layout.MinWidth >= 300);
        Assert.True(layout.MinHeight >= 300);
    }

    [Fact]
    public void LayoutCollapsedIsMiniDock()
    {
        var layout = PlanDockExperience.Layout(collapsed: true);
        Assert.Equal(PlanDockExperience.CollapsedWidth, layout.Width);
        Assert.Equal(PlanDockExperience.CollapsedHeight, layout.Height);
        Assert.True(layout.Height < PlanDockExperience.ExpandedHeight);
        Assert.True(layout.Width < PlanDockExperience.ExpandedWidth);
    }

    [Fact]
    public void ToggleCollapsedFlipsState()
    {
        Assert.True(PlanDockExperience.ToggleCollapsed(currentlyCollapsed: false));
        Assert.False(PlanDockExperience.ToggleCollapsed(currentlyCollapsed: true));
    }

    [Fact]
    public void ShowEditActionsHiddenWhileProgressTracking()
    {
        Assert.True(PlanDockExperience.ShowEditActions(progressTracking: false));
        Assert.False(PlanDockExperience.ShowEditActions(progressTracking: true));
    }

    [Fact]
    public void ChipVisibleDuringModeOrProgressTracking()
    {
        Assert.False(PlanDockExperience.ChipVisible(planModeEnabled: false, progressTracking: false));
        Assert.True(PlanDockExperience.ChipVisible(planModeEnabled: true, progressTracking: false));
        Assert.True(PlanDockExperience.ChipVisible(planModeEnabled: false, progressTracking: true));
        Assert.True(PlanDockExperience.ChipVisible(planModeEnabled: true, progressTracking: true));
    }

    [Fact]
    public void FormatDockCaptionJoinsTitleAndProgress()
    {
        Assert.Equal("Auth fix · 1/3 steps checked",
            PlanDockExperience.FormatDockCaption("Auth fix", "1/3 steps checked"));
        Assert.Equal("Plan", PlanDockExperience.FormatDockCaption(null, null));
        Assert.Equal("Ship", PlanDockExperience.FormatDockCaption("Ship", "  "));
    }

    [Fact]
    public void LiveStepSummaryUpdatesForDockAndExpanded()
    {
        var doc = PlanParser.Parse("# Ship UI\n- [ ] One\n- [ ] Two");
        Assert.Equal("0/2 steps checked", doc.StepSummary);
        Assert.Equal("Ship UI · 0/2 steps checked",
            PlanDockExperience.FormatDockCaption(doc.Title, doc.StepSummary));

        doc.SetStepDone(0, true);
        Assert.Equal("1/2 steps checked", doc.StepSummary);
        Assert.Equal("Ship UI · 1/2 steps checked",
            PlanDockExperience.FormatDockCaption(doc.Title, doc.StepSummary));
    }

    [Fact]
    public void ShippedUiWiresPlanDockCollapse()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains(PlanDockExperience.TogglePlanWindowCommandName, xaml, StringComparison.Ordinal);
        Assert.Contains(PlanDockExperience.PlanChipVisibleProperty, xaml, StringComparison.Ordinal);
        Assert.Contains("Collapse or expand the plan window", xaml, StringComparison.Ordinal);
        // Mode off stays on the + menu only.
        Assert.Contains("PlanModeMenuLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("TogglePlanModeCommand", xaml, StringComparison.Ordinal);

        var mainCs = ReadShipped("src/Luma.App/MainWindow.axaml.cs");
        Assert.Contains("PlanWindowToggleRequested", mainCs, StringComparison.Ordinal);
        Assert.Contains("TogglePlanWindowCollapsed", mainCs, StringComparison.Ordinal);
        Assert.Contains("SetProgressTracking", mainCs, StringComparison.Ordinal);
        Assert.Contains("SetCollapsed", mainCs, StringComparison.Ordinal);

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("TogglePlanWindowCommand", vm, StringComparison.Ordinal);
        Assert.Contains("PlanChipVisible", vm, StringComparison.Ordinal);
        Assert.Contains("PlanWindowToggleRequested", vm, StringComparison.Ordinal);
        // Chip must not call mode toggle.
        Assert.Contains("never turns plan mode off", vm, StringComparison.OrdinalIgnoreCase);

        var planWin = ReadShipped("src/Luma.App/Services/PlanDocumentWindow.cs");
        Assert.Contains("SetCollapsed", planWin, StringComparison.Ordinal);
        Assert.Contains("ToggleCollapsed", planWin, StringComparison.Ordinal);
        Assert.Contains("SetProgressTracking", planWin, StringComparison.Ordinal);
        Assert.Contains("_collapsedDock", planWin, StringComparison.Ordinal);
        Assert.Contains("PlanDockExperience", planWin, StringComparison.Ordinal);
        Assert.Contains("ShowEditActions", planWin, StringComparison.Ordinal);
    }

    private static string ReadShipped(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Luma.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repo root with Luma.slnx");
    }
}
