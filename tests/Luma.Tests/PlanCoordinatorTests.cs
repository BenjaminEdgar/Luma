using Luma.App.Services;

namespace Luma.Tests;

public sealed class PlanCoordinatorTests
{
    [Fact]
    public void ModeOwnsStaticPromptFlagAndChipVisibility()
    {
        var coordinator = new PlanCoordinator();
        using var cleanup = new PlanModeCleanup();

        Assert.False(coordinator.ModeEnabled);
        Assert.False(coordinator.ChipVisible);

        coordinator.SetModeEnabled(true);

        Assert.True(coordinator.ModeEnabled);
        Assert.True(coordinator.ChipVisible);
        Assert.True(PlanMode.Active);

        coordinator.SetModeEnabled(false);

        Assert.False(coordinator.ModeEnabled);
        Assert.False(coordinator.ChipVisible);
        Assert.False(PlanMode.Active);
    }

    [Fact]
    public void ApplyMarkdownOnlyMutatesPlanDuringPlanOrProgress()
    {
        var coordinator = new PlanCoordinator();
        using var cleanup = new PlanModeCleanup();

        Assert.False(coordinator.TryApplyMarkdown("# Ignored\n- [ ] Step"));
        Assert.False(coordinator.Document.HasContent);

        coordinator.SetModeEnabled(true);
        Assert.True(coordinator.TryApplyMarkdown("# Planned\n- [ ] One"));

        Assert.Equal("Planned", coordinator.Document.Title);
        Assert.Equal("One", coordinator.Document.Steps[0].Text);
    }

    [Fact]
    public void ProgressTrackingKeepsChipVisibleAfterLeavingPlanMode()
    {
        var coordinator = new PlanCoordinator();
        using var cleanup = new PlanModeCleanup();

        coordinator.SetModeEnabled(true);
        coordinator.SetProgressTracking(true);
        coordinator.SetModeEnabled(false);

        Assert.False(coordinator.ModeEnabled);
        Assert.True(coordinator.ProgressTracking);
        Assert.True(coordinator.ChipVisible);
        Assert.False(PlanMode.Active);
        Assert.True(PlanMode.TrackingProgress);

        coordinator.SetProgressTracking(false);

        Assert.False(coordinator.ChipVisible);
        Assert.False(PlanMode.TrackingProgress);
    }

    [Fact]
    public void WindowCollapsedStateRaisesDedicatedEvent()
    {
        var coordinator = new PlanCoordinator();
        bool? observed = null;
        var stateChanges = 0;
        coordinator.WindowCollapsedChanged += collapsed => observed = collapsed;
        coordinator.StateChanged += () => stateChanges++;

        coordinator.SetWindowCollapsed(true);

        Assert.True(coordinator.WindowCollapsed);
        Assert.True(observed);
        Assert.True(stateChanges > 0);
    }

    [Fact]
    public void DirectDocumentEditsStillNotifyCoordinatorState()
    {
        var coordinator = new PlanCoordinator();
        var stateChanges = 0;
        var planUpdates = 0;
        coordinator.StateChanged += () => stateChanges++;
        coordinator.PlanUpdated += () => planUpdates++;

        coordinator.Document.ReplaceFromMarkdown("# Edited in window\n- [ ] Ship");

        Assert.True(coordinator.Document.CanImplement);
        Assert.True(stateChanges > 0);
        Assert.True(planUpdates > 0);
    }

    private sealed class PlanModeCleanup : IDisposable
    {
        public void Dispose()
        {
            PlanMode.Active = false;
            PlanMode.TrackingProgress = false;
        }
    }
}
