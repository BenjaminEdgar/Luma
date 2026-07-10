namespace Luma.App.Services;

/// <summary>
/// Single owner for plan-mode lifecycle: planning, implement progress, window dock state,
/// and PLAN: markdown application.
/// </summary>
public sealed class PlanCoordinator
{
    private bool _updatingDocument;

    public PlanDocument Document { get; } = new();

    public bool ModeEnabled { get; private set; }
    public bool ProgressTracking { get; private set; }
    public bool WindowCollapsed { get; private set; }
    public bool ChipVisible => PlanDockExperience.ChipVisible(ModeEnabled, ProgressTracking);

    public event Action<bool>? ModeChanged;
    public event Action<bool>? ProgressTrackingChanged;
    public event Action<bool>? WindowCollapsedChanged;
    public event Action? PlanUpdated;
    public event Action? StateChanged;

    public PlanCoordinator()
    {
        Document.PropertyChanged += (_, e) =>
        {
            if (_updatingDocument) return;
            if (e.PropertyName is nameof(PlanDocument.Markdown) or nameof(PlanDocument.Title)
                or nameof(PlanDocument.Steps) or nameof(PlanDocument.CanImplement)
                or nameof(PlanDocument.StepSummary) or nameof(PlanDocument.ProgressSummary))
            {
                StateChanged?.Invoke();
                PlanUpdated?.Invoke();
            }
        };
    }

    public void ToggleMode() => SetModeEnabled(!ModeEnabled);

    public void SetModeEnabled(bool enabled)
    {
        if (ModeEnabled == enabled) return;
        ModeEnabled = enabled;
        PlanMode.Active = enabled;
        if (enabled) SetWindowCollapsed(false);
        StateChanged?.Invoke();
        ModeChanged?.Invoke(enabled);
    }

    public void SetProgressTracking(bool tracking)
    {
        if (ProgressTracking == tracking) return;
        ProgressTracking = tracking;
        PlanMode.TrackingProgress = tracking;
        StateChanged?.Invoke();
        ProgressTrackingChanged?.Invoke(tracking);
    }

    public void SetWindowCollapsed(bool collapsed)
    {
        if (WindowCollapsed == collapsed) return;
        WindowCollapsed = collapsed;
        StateChanged?.Invoke();
        WindowCollapsedChanged?.Invoke(collapsed);
    }

    public void Clear()
    {
        MutateDocument(() => Document.Clear());
        StateChanged?.Invoke();
        PlanUpdated?.Invoke();
    }

    public void ReplaceMarkdown(string? planMarkdown)
    {
        MutateDocument(() => Document.ReplaceFromMarkdown(planMarkdown));
        StateChanged?.Invoke();
        PlanUpdated?.Invoke();
    }

    public bool TryApplyMarkdown(string? planMarkdown)
    {
        if (string.IsNullOrWhiteSpace(planMarkdown)) return false;
        if (!ModeEnabled && !ProgressTracking) return false;

        MutateDocument(() => Document.ReplaceFromMarkdown(planMarkdown));
        StateChanged?.Invoke();
        PlanUpdated?.Invoke();
        return true;
    }

    private void MutateDocument(Action action)
    {
        _updatingDocument = true;
        try { action(); }
        finally { _updatingDocument = false; }
    }

    public void Dispose()
    {
        ModeEnabled = false;
        ProgressTracking = false;
        PlanMode.Active = false;
        PlanMode.TrackingProgress = false;
        StateChanged?.Invoke();
    }
}
