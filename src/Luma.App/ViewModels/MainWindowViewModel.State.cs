using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void TogglePlanMode()
    {
        PlanModeEnabled = !PlanModeEnabled;
        if (PlanModeEnabled)
            PlanUpdated?.Invoke();
        else
            PlanMode.Active = false;
    }

    /// <summary>Chip action: collapse/expand plan window only - never turns plan mode off.</summary>
    private void TogglePlanWindow()
    {
        if (!PlanChipVisible) return;
        PlanWindowToggleRequested?.Invoke();
    }

    /// <summary>Hands the approved plan into a normal coding turn in this chat, then exits plan mode.
    /// Plan window stays open and checks steps off as the agent emits PLAN: progress updates.</summary>
    private async Task ImplementApprovedPlanAsync()
    {
        if (!PlanModeEnabled || !Plan.CanImplement || _busy) return;
        var planMarkdown = Plan.Markdown.Trim();
        if (string.IsNullOrWhiteSpace(planMarkdown)) return;

        var prompt = PlanMode.BuildImplementPrompt(planMarkdown);
        // Track before leaving plan mode so the window stays open for check-offs.
        PlanProgressTracking = true;
        PlanModeEnabled = false;
        PlanMode.Active = false;
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
        if (string.IsNullOrWhiteSpace(planMarkdown)) return;
        if (!PlanModeEnabled && !PlanProgressTracking) return;

        Plan.ReplaceFromMarkdown(planMarkdown);
        ImplementPlanCommand.RaiseCanExecuteChanged();
        PlanUpdated?.Invoke();
        // Leave a short note in chat when the reply was only a plan block (planning turns).
        if (answer is not null && PlanModeEnabled && string.IsNullOrWhiteSpace(answer.Text))
            answer.Text = PlanMode.FormatChatNote(Plan.Title);
    }

    private void ToggleSplitBrain() => SplitBrainEnabled = !SplitBrainEnabled;

    private void ChooseSplitBrainSide(object? parameter)
    {
        // Parameter format: message via Tag isn't easy - use SplitBrainChoice record
        if (parameter is not SplitBrainChoice choice) return;
        if (choice.Message.SplitBrain is null) return;
        choice.Message.SplitBrain.Choose(choice.Side);
        choice.Message.Text = choice.Message.SplitBrain.MergedText;
        if (choice.Side is "A" or "B")
        {
            OutcomeMemory.Record(OutcomeKind.Note, $"Kept {choice.Side} in split-brain",
                choice.Side == "A" ? choice.Message.SplitBrain.ProviderA : choice.Message.SplitBrain.ProviderB);
        }
    }

    private async Task RunSplitBrainTurnAsync(string prompt, bool attachCaptures)
    {
        var providers = AvailableProviderIndices();
        if (providers.Count < 2)
        {
            // Need two brains; fall back to normal turn.
            await RunTurnAsync(prompt, attachCaptures: attachCaptures);
            return;
        }

        var (region, context) = ChatCaptureAttachment.ForFirstRequest(attachCaptures, _regionPath, _contextPath);
        var user = new ChatMessage("user", prompt);
        AttachCaptureToMessage(user, region, context);
        Messages.Add(user);
        var answer = new ChatMessage("assistant", "Running split-brain...", isPending: true)
        {
            Caption = $"✦ {Providers[providers[0]]} + {Providers[providers[1]]}",
        };
        Messages.Add(answer);
        SetBusy(true);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        var writeSnapshot = WorkspaceWriteAuditor.Capture(WorkingDirectory);
        BeginLivePair(WorkingDirectory, writeSnapshot, answer);
        try
        {
            var taskContext = BuildTurnContext(prompt);
            var history = Messages.Take(Messages.Count - 2).ToArray();
            var reqA = new AiRequest(SplitBrainPrompts.Explainer(prompt), region, context, history)
            { WorkingDirectory = WorkingDirectory, TaskContext = taskContext };
            var reqB = new AiRequest(SplitBrainPrompts.Implementer(prompt), region, context, history)
            { WorkingDirectory = WorkingDirectory, TaskContext = taskContext };
            var clientA = _clientFactory.Create((AiProvider)providers[0]);
            var clientB = _clientFactory.Create((AiProvider)providers[1]);
            var taskA = clientA.AskAsync(reqA, null, cts.Token);
            var taskB = clientB.AskAsync(reqB, null, cts.Token);
            await Task.WhenAll(taskA, taskB);
            var textA = ChatStreamTextPolicy.ApplyFinal(ShowWhereParser.Extract(await taskA).Text).Text;
            var textB = ChatStreamTextPolicy.ApplyFinal(ShowWhereParser.Extract(await taskB).Text).Text;
            var split = new SplitBrainResult
            {
                ProviderA = Providers[providers[0]],
                ProviderB = Providers[providers[1]],
                TextA = textA,
                TextB = textB,
            };
            answer.SplitBrain = split;
            answer.Text = split.MergedText;
            answer.Caption = $"✦ {split.ProviderA} (explain) · {split.ProviderB} (implement)";
            AttachWriteAudit(answer, writeSnapshot);
            ConsumeEphemeralAttachments();
            RefreshOutcomeChips(prompt);
            _ = GenerateFollowUpSuggestionsAsync();
        }
        catch (OperationCanceledException)
        {
            answer.Caption = "✦ split-brain stopped";
            if (string.IsNullOrWhiteSpace(answer.Text)) answer.Text = "*Stopped.*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Caption = "✦ split-brain error";
            answer.Text = ex.Message;
        }
        finally
        {
            EndLivePairWatch();
            answer.IsPending = false;
            answer.IsStreaming = false;
            _requestCts = null;
            SetBusy(false);
        }
    }

    public sealed record SplitBrainChoice(ChatMessage Message, string Side);

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

    private void ToggleChaosMode()
    {
        AppSettings.Current.ChaosMode = !AppSettings.Current.ChaosMode;
        AppSettings.Current.Save();
        if (!AppSettings.Current.ChaosMode)
        {
            _focusUntilUtc = null;
            _chaosTicker.Stop();
        }
        NotifyChaosChanged();
    }

    private void ToggleLeanChat()
    {
        AppSettings.Current.LeanChatMode = !AppSettings.Current.LeanChatMode;
        AppSettings.Current.Save();
        NotifyLeanChatChanged();
    }

    private void NotifyLeanChatChanged()
    {
        OnPropertyChanged(nameof(LeanChatEnabled));
        OnPropertyChanged(nameof(LeanChatMenuLabel));
        OnPropertyChanged(nameof(LeanChatChipLabel));
        NotifySurfaceStateChanged();
    }

    private void CycleChaosTone()
    {
        if (!ChaosModeEnabled) return;
        AppSettings.Current.ChaosTone = (int)ChaosMode.NextTone(ChaosTone);
        AppSettings.Current.Save();
        NotifyChaosChanged();
    }

    private async Task RoastUiAsync()
    {
        if (!ChaosModeEnabled || _busy) return;
        if (IsFocusLocked)
        {
            Messages.Add(new ChatMessage("assistant", ChaosMode.PomodoroBlockedMessage(_focusUntilUtc!.Value - DateTimeOffset.UtcNow))
            { Caption = "🍅 Focus lock" });
            return;
        }
        // Prefer a fresh screen if we have nothing; otherwise roast current capture.
        if (!HasCapture)
        {
            if (!await CaptureFullScreenForContextAsync()) return;
            ScreenExplanationReadyToShow?.Invoke();
        }
        await RunTurnAsync(ChaosMode.RoastUiPrompt(), "Roast my UI", attachCaptures: true);
    }

    private async Task ArgueWithYourselfAsync()
    {
        if (!ChaosModeEnabled || _busy) return;
        var topic = Question.Trim();
        if (string.IsNullOrWhiteSpace(topic))
            topic = "What should I do next about what's on my screen / in this project?";
        Question = string.Empty;

        var providers = AvailableProviderIndices();
        if (providers.Count == 0) return;

        Messages.Add(new ChatMessage("user", $"Argue with yourself: {topic}"));
        var answer = new ChatMessage("assistant", "Warming up the debate stage...", isPending: true)
        {
            Caption = "✦ Chaos debate",
        };
        Messages.Add(answer);
        SetBusy(true);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        try
        {
            if (providers.Count >= 2)
            {
                var a = providers[0];
                var b = providers[1];
                var clientA = _clientFactory.Create((AiProvider)a);
                var clientB = _clientFactory.Create((AiProvider)b);
                var reqA = new AiRequest(ChaosMode.DualDebatePrompt(topic, "Side A - ship the bold option"), _regionPath, _contextPath, [])
                { WorkingDirectory = WorkingDirectory, TaskContext = BuildTurnContext(topic) };
                var reqB = new AiRequest(ChaosMode.DualDebatePrompt(topic, "Side B - ship the careful option"), _regionPath, _contextPath, [])
                { WorkingDirectory = WorkingDirectory, TaskContext = BuildTurnContext(topic) };
                var taskA = clientA.AskAsync(reqA, null, cts.Token);
                var taskB = clientB.AskAsync(reqB, null, cts.Token);
                await Task.WhenAll(taskA, taskB);
                answer.Text = ChaosMode.FormatDualDebate(Providers[a], await taskA, Providers[b], await taskB);
                answer.Caption = $"✦ {Providers[a]} vs {Providers[b]}";
            }
            else
            {
                var text = await _clientFactory.Create((AiProvider)providers[0])
                    .AskAsync(new AiRequest(ChaosMode.DebatePrompt(topic), _regionPath, _contextPath, [])
                    {
                        WorkingDirectory = WorkingDirectory,
                        TaskContext = BuildTurnContext(topic),
                    }, partial => Dispatcher.UIThread.Post(() =>
                    {
                        answer.IsPending = false;
                        answer.IsStreaming = true;
                        answer.Text = ChatStreamTextPolicy.ApplyPartial(partial).Text;
                    }), cts.Token);
                ApplyFinalAnswerText(answer, text);
                answer.Caption = $"✦ {Providers[providers[0]]} (solo debate)";
            }
            ConsumeEphemeralAttachments();
            _ = GenerateFollowUpSuggestionsAsync();
        }
        catch (OperationCanceledException)
        {
            answer.Caption = "✦ debate stopped";
            if (string.IsNullOrWhiteSpace(answer.Text)) answer.Text = "*Stopped.*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Caption = "✦ debate error";
            answer.Text = ex.Message;
        }
        finally
        {
            answer.IsPending = false;
            answer.IsStreaming = false;
            _requestCts = null;
            SetBusy(false);
        }
    }

    private List<int> AvailableProviderIndices()
    {
        var list = new List<int>();
        void Consider(int index, ProviderDiagnostic? d)
        {
            if (d?.IsAvailable != false) list.Add(index);
        }
        // Prefer selected first, then others.
        var order = new[] { SelectedProviderIndex, 0, 1, 2 }.Distinct();
        foreach (var i in order)
        {
            ProviderDiagnostic? d = i switch { 0 => _claudeDiagnostic, 1 => _codexDiagnostic, _ => _grokDiagnostic };
            Consider(i, d);
        }
        return list;
    }

    private void TogglePomodoro()
    {
        if (!ChaosModeEnabled) return;
        if (IsFocusLocked)
        {
            _focusUntilUtc = null;
            _chaosTicker.Stop();
        }
        else
        {
            var minutes = AppSettings.Current.ChaosPomodoroMinutes;
            if (minutes < 1) minutes = ChaosMode.DefaultPomodoroMinutes;
            _focusUntilUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
            _chaosTicker.Start();
        }
        NotifyChaosChanged();
    }

    private void TickChaosFocus()
    {
        if (!IsFocusLocked)
        {
            _focusUntilUtc = null;
            _chaosTicker.Stop();
        }
        NotifyChaosChanged();
    }

    private void NotifyChaosChanged()
    {
        OnPropertyChanged(nameof(ChaosModeEnabled));
        OnPropertyChanged(nameof(ChaosTone));
        OnPropertyChanged(nameof(ChaosToneChipLabel));
        OnPropertyChanged(nameof(ChaosModeMenuLabel));
        OnPropertyChanged(nameof(IsFocusLocked));
        OnPropertyChanged(nameof(PomodoroLabel));
        OnPropertyChanged(nameof(PomodoroMenuLabel));
        NotifySurfaceStateChanged();
        ExplainSelectionCommand.RaiseCanExecuteChanged();
        ExplainScreenCommand.RaiseCanExecuteChanged();
        CycleChaosToneCommand.RaiseCanExecuteChanged();
        RoastUiCommand.RaiseCanExecuteChanged();
        ArgueWithYourselfCommand.RaiseCanExecuteChanged();
        TogglePomodoroCommand.RaiseCanExecuteChanged();
    }

    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(HasAssistantMemory));
        OnPropertyChanged(nameof(AssistantMemoryPreview));
        SelectedEffortIndex = AppSettings.EffortToIndex(AppSettings.Current.ChatReasoningEffort);
        NotifyChaosChanged();
        NotifyLeanChatChanged();
        NotifyModelPickerChanged();
    }

    private void SelectProvider(object? parameter)
    {
        if (!TryParseMenuIndex(parameter, out var index)) return;
        SelectedProviderIndex = Math.Clamp(index, 0, Providers.Count - 1);
        AppSettings.Current.Provider = SelectedProviderIndex;
        AppSettings.Current.Save();
    }

    private void SelectEffort(object? parameter)
    {
        if (!TryParseMenuIndex(parameter, out var index)) return;
        SelectedEffortIndex = index;
    }

    private static bool TryParseMenuIndex(object? parameter, out int index)
    {
        switch (parameter)
        {
            case int i:
                index = i;
                return true;
            case string s when int.TryParse(s, out index):
                return true;
            default:
                index = 0;
                return false;
        }
    }

    private void NotifyModelPickerChanged()
    {
        OnPropertyChanged(nameof(ModelPickerLabel));
        OnPropertyChanged(nameof(ProviderMenuClaude));
        OnPropertyChanged(nameof(ProviderMenuCodex));
        OnPropertyChanged(nameof(ProviderMenuGrok));
        OnPropertyChanged(nameof(EffortMenuLow));
        OnPropertyChanged(nameof(EffortMenuMedium));
        OnPropertyChanged(nameof(EffortMenuHigh));
    }

    private static string MenuCheck(bool selected, string label) => selected ? $"✓  {label}" : $"    {label}";

    private void DisposeMessages()
    {
        foreach (var message in Messages)
            message.Dispose();
        Messages.Clear();
    }
}
