using Avalonia.Input.Platform;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string? BuildTurnContext(string prompt) =>
        ContextAttachments.BuildTaskContext(ClipboardSnippet, _attachedFilePaths, WorkingDirectory, prompt);

    private void ConsumeEphemeralAttachments()
    {
        // Clipboard + explicit file pins apply once, then clear so they don't leak into every turn.
        ClearClipboardSnippet();
        ClearAttachedFiles();
    }

    private async Task UseClipboardAsync()
    {
        try
        {
            if (_owner.Clipboard is null) return;
            var text = await ClipboardExtensions.TryGetTextAsync(_owner.Clipboard);
            if (string.IsNullOrWhiteSpace(text)) return;
            ClipboardSnippet = text.Trim();
        }
        catch { /* clipboard unavailable */ }
    }

    private void ClearClipboardSnippet()
    {
        ClipboardSnippet = null;
    }

    public async Task AttachFilesFromPickerAsync()
    {
        if (AttachFilesRequested is null) return;
        var paths = await AttachFilesRequested();
        foreach (var path in paths)
            AddAttachedFile(path);
    }

    public void AddAttachedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var full = Path.GetFullPath(path);
        if (_attachedFilePaths.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
        _attachedFilePaths.Add(full);
        var label = WorkingDirectory is not null
            ? Path.GetRelativePath(WorkingDirectory, full).Replace('\\', '/')
            : Path.GetFileName(full);
        AttachedFileLabels.Add(label);
        OnPropertyChanged(nameof(HasAttachedFiles));
        ClearAttachedFilesCommand.RaiseCanExecuteChanged();
    }

    private void ClearAttachedFiles()
    {
        _attachedFilePaths.Clear();
        AttachedFileLabels.Clear();
        OnPropertyChanged(nameof(HasAttachedFiles));
        ClearAttachedFilesCommand.RaiseCanExecuteChanged();
    }

    private void UndoFileChange(object? parameter)
    {
        if (parameter is not FileChangeRecord change || string.IsNullOrWhiteSpace(WorkingDirectory)) return;
        if (!change.CanUndo) return;
        try
        {
            WorkspaceWriteAuditor.Undo(WorkingDirectory, change);
            OutcomeMemory.Record(OutcomeKind.Undo, $"Undid {change.Kind.ToString().ToLowerInvariant()} {change.RelativePath}",
                WorkingDirectory, [change.RelativePath, change.Kind.ToString()]);
            RefreshOutcomeChips();
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"Could not undo {change.RelativePath}: {ex.Message}") { IsError = true });
        }
    }

    private void RefreshOutcomeChips(string? promptHint = null)
    {
        var chips = OutcomeMemory.SuggestChips(promptHint ?? Question, max: 3);
        if (chips.Count == 0) return;
        // Merge into suggestions without wiping AI chips if present - prefer outcome chips first.
        var existing = Suggestions.ToList();
        Suggestions.Clear();
        foreach (var chip in chips) Suggestions.Add(chip);
        foreach (var chip in existing)
            if (!Suggestions.Contains(chip, StringComparer.OrdinalIgnoreCase) && Suggestions.Count < 6)
                Suggestions.Add(chip);
    }
}
