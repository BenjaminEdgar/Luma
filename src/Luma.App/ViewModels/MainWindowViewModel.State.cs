using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void TogglePlanMode()
    {
        _planCoordinator.ToggleMode();
        if (PlanModeEnabled)
            PlanUpdated?.Invoke();
    }

    /// <summary>Chip action: collapse/expand plan window only - never turns plan mode off.</summary>
    private void TogglePlanWindow()
    {
        if (!PlanChipVisible) return;
        PlanWindowToggleRequested?.Invoke();
    }

    public void ReplacePlanMarkdownFromWindow(string markdown)
    {
        _planCoordinator.ReplaceMarkdown(markdown);
    }

    /// <summary>Hands the approved plan into a normal coding turn in this chat, then exits plan mode.</summary>
    private async Task ImplementApprovedPlanAsync()
    {
        if (!PlanModeEnabled || !Plan.CanImplement || _busy) return;
        var planMarkdown = Plan.Markdown.Trim();
        if (string.IsNullOrWhiteSpace(planMarkdown)) return;

        var prompt = PlanMode.BuildImplementPrompt(planMarkdown);
        // Track before leaving plan mode so the window stays open for check-offs.
        PlanProgressTracking = true;
        PlanModeEnabled = false;
        PlanUpdated?.Invoke();

        try
        {
            var repository = WorkingDirectoryRequested is null ? WorkingDirectory : await WorkingDirectoryRequested();
            if (repository is null)
            {
                // No folder - still run as chat so the plan is not lost; user can set cwd later.
                await RunTurnAsync(prompt, displayPrompt: "Implement approved plan", attachCaptures: false);
                return;
            }

            WorkingDirectory = repository;
            await RunCodeTurnAsync(prompt, repository);
        }
        finally
        {
            PlanProgressTracking = false;
        }
    }

    /// <summary>Applies a PLAN: body during plan mode or live implement progress tracking.</summary>
    private void TryApplyPlanMarkdown(string? planMarkdown, ChatMessage? answer = null)
    {
        if (!_planCoordinator.TryApplyMarkdown(planMarkdown)) return;
        // Leave a short note in chat when the reply was only a plan block (planning turns).
        if (answer is not null && PlanModeEnabled && string.IsNullOrWhiteSpace(answer.Text))
            answer.Text = PlanMode.FormatChatNote(Plan.Title);
    }

    private void ApplyPlanMarkdownFromAnyThread(string? planMarkdown)
    {
        if (string.IsNullOrWhiteSpace(planMarkdown)) return;
        if (Dispatcher.UIThread.CheckAccess()) TryApplyPlanMarkdown(planMarkdown);
        else Dispatcher.UIThread.Post(() => TryApplyPlanMarkdown(planMarkdown));
    }

    private void DisposeMessages()
    {
        foreach (var message in Messages)
            message.Dispose();
        Messages.Clear();
    }
}
