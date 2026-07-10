using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void AttachWriteAudit(ChatMessage answer, WorkspaceSnapshot snapshot)
    {
        var root = _livePairRoot ?? WorkingDirectory;
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            var changes = WorkspaceWriteAuditor.Diff(root, snapshot);
            // Final poll so the mini-map matches the audit list.
            try
            {
                LivePairMap.MergeInto(LivePairFiles, LivePairMap.Scan(root, snapshot));
                NotifyLivePairChanged();
            }
            catch { /* live map is best-effort */ }
            if (changes.Count == 0) return;
            answer.SetFileChanges(changes);
            _livePairAnswer = answer;
            var names = string.Join(", ", changes.Take(4).Select(c => c.RelativePath));
            OutcomeMemory.Record(OutcomeKind.Write,
                changes.Count == 1 ? $"Edited {changes[0].RelativePath}" : $"Changed {changes.Count} files ({names})",
                root,
                changes.Select(c => c.RelativePath).Take(8));
            RefreshOutcomeChips();
        }
        catch { /* audit is best-effort */ }
    }

    /// <summary>Starts the live coding pair mini-map for an agent turn with a project folder.</summary>
    private void BeginLivePair(string? root, WorkspaceSnapshot snapshot, ChatMessage answer)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            // No project folder - clear any stale map from a previous turn.
            if (!_livePairActive && LivePairFiles.Count == 0) return;
            DismissLivePair();
            return;
        }

        _livePairRoot = root;
        _livePairSnapshot = snapshot;
        _livePairAnswer = answer;
        LivePairFiles.Clear();
        ActiveLivePairFile = null;
        _livePairUserPicked = false;
        _livePairActive = true;
        if (!_livePairTicker.IsEnabled) _livePairTicker.Start();
        NotifyLivePairChanged();
        // Immediate scan so create/write mid-turn shows up without waiting a full tick.
        PollLivePair();
    }

    /// <summary>Stops polling but keeps the mini-map until dismiss or next turn.</summary>
    private void EndLivePairWatch()
    {
        if (!_livePairActive) return;
        _livePairActive = false;
        _livePairTicker.Stop();
        // One last scan so late flushes land before the audit card freezes.
        if (_livePairSnapshot is not null && !string.IsNullOrWhiteSpace(_livePairRoot))
        {
            try
            {
                LivePairMap.MergeInto(LivePairFiles, LivePairMap.Scan(_livePairRoot, _livePairSnapshot));
                RefreshActiveLivePair();
            }
            catch { /* ignore */ }
        }
        NotifyLivePairChanged();
    }

    private void PollLivePair()
    {
        if (!_livePairActive || _livePairSnapshot is null || string.IsNullOrWhiteSpace(_livePairRoot))
            return;
        try
        {
            // Cool previous pulse first; MergeInto re-marks files that just grew.
            foreach (var file in LivePairFiles) file.IsFresh = false;
            var scan = LivePairMap.Scan(_livePairRoot, _livePairSnapshot);
            LivePairMap.MergeInto(LivePairFiles, scan);
            RefreshActiveLivePair();
            NotifyLivePairChanged();
        }
        catch { /* best-effort */ }
    }

    private void RefreshActiveLivePair()
    {
        if (LivePairFiles.Count == 0)
        {
            ActiveLivePairFile = null;
            return;
        }

        // Auto-follow hottest/freshest until the user picks a chip; then stick to their choice.
        if (!_livePairUserPicked)
        {
            ActiveLivePairFile = LivePairMap.PickActive(LivePairFiles, null);
            return;
        }

        ActiveLivePairFile = LivePairMap.PickActive(LivePairFiles, ActiveLivePairFile);
    }

    private void JumpLivePairFile(object? parameter)
    {
        if (parameter is not LivePairFile file) return;

        // Always show this file's live unified preview first.
        _livePairUserPicked = true;
        ActiveLivePairFile = LivePairFiles.FirstOrDefault(f =>
            string.Equals(f.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase)) ?? file;
        OnPropertyChanged(nameof(LivePairSubtitle));

        // Prefer the answer from this pair session; fall back to newest message with that path.
        ChatMessage? target = null;
        FileChangeRecord? record = null;
        if (_livePairAnswer is { HasFileChanges: true } answer)
        {
            record = answer.FileChanges.FirstOrDefault(c =>
                string.Equals(c.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (record is not null) target = answer;
        }
        if (target is null)
        {
            for (var i = Messages.Count - 1; i >= 0; i--)
            {
                var msg = Messages[i];
                if (!msg.HasFileChanges) continue;
                record = msg.FileChanges.FirstOrDefault(c =>
                    string.Equals(c.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase));
                if (record is null) continue;
                target = msg;
                break;
            }
        }

        if (target is null || record is null)
        {
            // Turn still running or audit not attached yet - keep preview, pulse the chip.
            file.IsFresh = true;
            return;
        }

        // Clear previous focus highlights.
        foreach (var msg in Messages)
            foreach (var change in msg.FileChanges)
                change.IsFocused = false;
        record.IsFocused = true;
        LivePairJumpRequested?.Invoke(target, file.RelativePath);

        // Auto-clear highlight after a moment.
        var focused = record;
        _ = ClearFocusLaterAsync(focused);
    }

    private static async Task ClearFocusLaterAsync(FileChangeRecord focused)
    {
        try
        {
            await Task.Delay(2800);
            await Dispatcher.UIThread.InvokeAsync(() => focused.IsFocused = false);
        }
        catch { /* ignore */ }
    }

    private void DismissLivePair()
    {
        _livePairActive = false;
        _livePairTicker.Stop();
        _livePairSnapshot = null;
        _livePairRoot = null;
        _livePairAnswer = null;
        LivePairFiles.Clear();
        ActiveLivePairFile = null;
        _livePairUserPicked = false;
        NotifyLivePairChanged();
    }

    private void NotifyLivePairChanged()
    {
        OnPropertyChanged(nameof(HasLivePair));
        OnPropertyChanged(nameof(ShowLivePair));
        OnPropertyChanged(nameof(IsLivePairWatching));
        OnPropertyChanged(nameof(LivePairTitle));
        OnPropertyChanged(nameof(LivePairSubtitle));
        OnPropertyChanged(nameof(HasLivePairPreview));
        OnPropertyChanged(nameof(LivePairPreviewLines));
        OnPropertyChanged(nameof(ActiveLivePairFile));
    }
}
