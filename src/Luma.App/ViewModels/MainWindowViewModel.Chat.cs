using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task UseSuggestionAsync(object? parameter)
    {
        if (parameter is not string suggestion || _busy) return;
        _suggestCts?.Cancel();
        Suggestions.Clear();
        if (string.Equals(suggestion, "Start new chat", StringComparison.OrdinalIgnoreCase))
        {
            // Allow force-clear even when CanStartNewChat would normally require messages+idle.
            ForceNewChat();
            return;
        }
        // Outcome chips are phrased as Avoid:/Retry: - strip prefix for the actual prompt.
        var prompt = suggestion;
        if (prompt.StartsWith("Avoid: ", StringComparison.OrdinalIgnoreCase))
            prompt = "Don't do this again; try another approach instead of: " + prompt["Avoid: ".Length..];
        else if (prompt.StartsWith("Retry: ", StringComparison.OrdinalIgnoreCase))
            prompt = "Do this again carefully: " + prompt["Retry: ".Length..];
        // Chips are derived from the ambient capture - attach screen on the first request.
        await RunTurnAsync(prompt, displayPrompt: suggestion, attachCaptures: true);
    }

    private async Task UseCodeActionAsync(object? parameter)
    {
        if (parameter is not string prompt || _busy) return;
        var repository = WorkingDirectoryRequested is null ? WorkingDirectory : await WorkingDirectoryRequested();
        if (repository is null) return;
        _suggestCts?.Cancel();
        Suggestions.Clear();
        WorkingDirectory = repository;
        await RunCodeTurnAsync(prompt, repository);
    }

    private void ForceNewChat()
    {
        if (_busy) return;
        _suggestCts?.Cancel();
        _improveCts?.Cancel();
        _requestCts?.Cancel();
        DisposeMessages();
        Suggestions.Clear();
        Question = string.Empty;
        ClearClipboardSnippet();
        ClearAttachedFiles();
        _planCoordinator.Clear();
        ImplementPlanCommand.RaiseCanExecuteChanged();
        if (_regionPath is not null) ReplaceCapture(ref _regionPath, null);
        _ambientOcrContext = null;
        _ambientOcrSourcePath = null;
        OnPropertyChanged(nameof(CanStartNewChat));
        NewChatCommand.RaiseCanExecuteChanged();
        NotifySurfaceStateChanged();
        if (_contextPath is not null) _ = GenerateSuggestionsAsync();
    }

    private async Task CaptureAsync()
        => await CaptureRegionForContextAsync();

    private async Task ExplainSelectionAsync()
    {
        if (IsFocusLocked)
        {
            Messages.Add(new ChatMessage("assistant", ChaosMode.PomodoroBlockedMessage(_focusUntilUtc!.Value - DateTimeOffset.UtcNow))
            { Caption = "🍅 Focus lock" });
            return;
        }
        _suggestCts?.Cancel();
        Suggestions.Clear();
        if (!await CaptureRegionForContextAsync()) return;
        ScreenExplanationReadyToShow?.Invoke();
        await RunTurnAsync(
            "Explain the selected region clearly. Identify what is shown, what it means in context, and any important " +
            "error, warning, control, data, or next action visible there. Be specific and practical; do not merely describe the pixels.",
            "Explain this selection",
            attachCaptures: true);
    }

    private async Task ExplainScreenAsync()
    {
        if (IsFocusLocked)
        {
            Messages.Add(new ChatMessage("assistant", ChaosMode.PomodoroBlockedMessage(_focusUntilUtc!.Value - DateTimeOffset.UtcNow))
            { Caption = "🍅 Focus lock" });
            return;
        }
        _suggestCts?.Cancel();
        Suggestions.Clear();
        if (!await CaptureFullScreenForContextAsync()) return;
        ScreenExplanationReadyToShow?.Invoke();
        await RunTurnAsync(
            "Explain what is important on this screen. Identify the application or content, summarize what the user is " +
            "looking at, call out any error, warning, unusual state, or likely point of confusion, and suggest the most useful next action. " +
            "Prioritize meaning over a generic visual description.",
            "Explain this screen",
            attachCaptures: true);
    }

    private async Task<bool> CaptureFullScreenForContextAsync()
    {
        SetBusy(true);
        try
        {
            _owner.Hide();
            await Task.Delay(150, _lifetime.Token);
            var path = await _captureService.CaptureScreenAsync(_owner, _lifetime.Token);
            ReplaceCapture(ref _regionPath, null);
            ReplaceCapture(ref _contextPath, path);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"Screen capture failed: {ex.Message}") { IsError = true });
            return false;
        }
        finally { _owner.Show(); _owner.Activate(); SetBusy(false); }
    }

    private async Task<bool> CaptureRegionForContextAsync()
    {
        SetBusy(true);
        try
        {
            _owner.Hide();
            await Task.Delay(150, _lifetime.Token);
            var path = await _captureService.CaptureRegionAsync(_owner, _lifetime.Token);
            if (path is null) return false;
            ReplaceCapture(ref _regionPath, path);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"Capture failed: {ex.Message}") { IsError = true });
            return false;
        }
        finally { _owner.Show(); _owner.Activate(); SetBusy(false); }
    }

    private async Task SendAsync()
    {
        _suggestCts?.Cancel();
        _improveCts?.Cancel();
        Suggestions.Clear();
        var prompt = Question.Trim();
        Question = string.Empty;
        // Plan mode stays in chat (clarify + PLAN: updates) - never route to code/shell workspaces.
        if (PlanModeEnabled)
        {
            await RunTurnAsync(prompt, attachCaptures: false);
            return;
        }
        var kind = TaskRouter.Classify(prompt);
        if (kind == TaskKind.Code)
        {
            var repository = WorkingDirectoryRequested is null ? WorkingDirectory : await WorkingDirectoryRequested();
            if (repository is null) { Question = prompt; return; }
            WorkingDirectory = repository;
            await RunCodeTurnAsync(prompt, repository);
            return;
        }
        if (kind == TaskKind.Shell && TaskLaunchRequested is not null &&
            await TaskLaunchRequested(new TaskLaunchRequest(kind, prompt, (AiProvider)SelectedProviderIndex, _regionPath, _contextPath))) return;
        if (SplitBrainEnabled)
        {
            await RunSplitBrainTurnAsync(prompt, attachCaptures: false);
            return;
        }
        // Straight chat starts text-only; the model may return NEED_SCREEN if a capture is required.
        await RunTurnAsync(prompt, attachCaptures: false);
    }

    /// <summary>Payload for a selected multiple-choice clarifying-question answer.</summary>
    public sealed record QuestionAnswerSelection(ChatMessage Message, string Answer);

    /// <summary>Sends the selected clarifying-question choice as the next turn.</summary>
    private Task AnswerQuestionAsync(object? parameter)
    {
        if (parameter is not QuestionAnswerSelection { Message: var source, Answer: var answer } || _busy)
            return Task.CompletedTask;
        var reply = answer.Trim();
        source.IsQuestion = false;
        return ContinueQuestionAsync(source, string.IsNullOrWhiteSpace(reply)
            ? "I don't have that information - please continue and do your best without it."
            : reply);
    }

    private Task SkipQuestionAsync(object? parameter)
    {
        if (parameter is not ChatMessage source || _busy) return Task.CompletedTask;
        source.IsQuestion = false;
        return ContinueQuestionAsync(source, "I don't have that information - please continue and do your best without it.");
    }

    /// <summary>Routes a clarifying-question answer either back into the owning CodeChatSession
    /// (continuing the same message/diff card) or, for plain chat, into a fresh RunTurnAsync turn -
    /// the ASK_USER convention is shared across all task kinds, so a code task's question must not
    /// be answered by spawning an unrelated plain-chat turn.</summary>
    private Task ContinueQuestionAsync(ChatMessage source, string answer) =>
        source.CodeSession is { } session
            ? RunCodeContinuationAsync(source, session, answer)
            : RunTurnAsync(answer, attachCaptures: false);

    /// <summary>OCR paths, reusing ambient full-screen OCR when the context file matches.</summary>
    private async Task<string?> ResolveOcrForPathsAsync(
        string? regionPath, string? contextPath, CancellationToken cancellationToken)
    {
        if (!AppSettings.Current.LocalOcrEnabled) return null;

        // Reuse ambient OCR when only full-screen context is needed and it matches.
        if (regionPath is null && contextPath is not null)
        {
            var ambient = AmbientOcrIfCurrent();
            if (ambient is not null &&
                string.Equals(contextPath, _ambientOcrSourcePath, StringComparison.OrdinalIgnoreCase))
                return ambient;
        }

        var ocr = await _localOcr.BuildContextAsync(regionPath, contextPath, cancellationToken);
        if (ocr is not null && regionPath is null && contextPath is not null)
        {
            _ambientOcrContext = ocr;
            _ambientOcrSourcePath = contextPath;
        }

        return ocr;
    }

    /// <param name="attachCaptures">
    /// When true (explain screen/selection, suggestion chips), use available screenshots for OCR (and vision fallback).
    /// When false (typed chat), start text-only so the model can answer without screen tokens and request
    /// NEED_SCREEN only when visual evidence is actually needed.
    /// </param>
    private async Task RunTurnAsync(string prompt, string? displayPrompt = null, bool attachCaptures = false)
    {
        var provider = (AiProvider)SelectedProviderIndex;
        var providerName = Providers[SelectedProviderIndex];
        var (region, context) = ChatCaptureAttachment.ForFirstRequest(attachCaptures, _regionPath, _contextPath);
        var sentVisual = ChatCaptureAttachment.HasVisual(region, context);
        var user = new ChatMessage("user", displayPrompt ?? prompt);
        AttachCaptureToMessage(user, region, context);
        Messages.Add(user);
        var answer = new ChatMessage("assistant", string.Empty, isPending: true)
        {
            Caption = sentVisual ? $"* {providerName} is reading your screen" : $"* {providerName} is thinking",
            TurnMeta = BuildTurnMeta(providerName, "Chat", sentVisual, WorkingDirectory),
            Text = sentVisual ? "Reading screen..." : "Thinking...",
        };
        Messages.Add(answer);
        SetBusy(true);
        SetActivity(sentVisual ? "Reading screen" : "Thinking",
            sentVisual ? "Screen context attached to this turn" : "Waiting for the provider to answer");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        var stopwatch = Stopwatch.StartNew();
        var ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => answer.Elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} s");
        ticker.Start();
        // Coalesce stream partials onto a short interval so multi-chunk output does not schedule
        // one UI apply per line; progressive text still appears and finalize applies full extract.
        using var streamBridge = new ChatStreamUiBridge(answer, providerName, md => TryApplyPlanMarkdown(md, answer));
        try
        {
            var history = Messages.Take(Messages.Count - 2).ToArray();
            var client = _clientFactory.Create(provider);
            var taskContext = BuildTurnContext(prompt);
            // Snapshot project files so we can audit agent writes after the turn.
            var writeSnapshot = WorkspaceWriteAuditor.Capture(WorkingDirectory);
            BeginLivePair(WorkingDirectory, writeSnapshot, answer);
            // On-device OCR first — prefer OCR text over vision tokens for the provider.
            string? localOcr = null;
            if (sentVisual)
            {
                SetOcrUi(OcrUiPhase.Running, "ON-DEVICE OCR RUNNING for this answer…");
                SetActivity("OCR RUNNING", "On-device text extraction (preferred over vision)");
                localOcr = await ResolveOcrForPathsAsync(region, context, cts.Token);
                if (localOcr is not null)
                {
                    var lines = CountOcrLines(localOcr);
                    var prefer = ScreenEvidence.PrefersOcrOverVision(localOcr);
                    SetOcrUi(OcrUiPhase.Ready,
                        prefer
                            ? $"OCR READY · ~{lines} lines · sending TEXT ONLY to {providerName} (no screenshot tokens)."
                            : $"OCR READY · ~{lines} lines · OCR + screenshot to {providerName}.");
                    answer.Caption = prefer
                        ? $"* {providerName} · local OCR only (no vision)"
                        : $"* {providerName} · local OCR + screen";
                    answer.TurnMeta = BuildTurnMeta(providerName, prefer ? "OCR-only" : "OCR+vision", hasVisual: true, WorkingDirectory);
                    SetActivity(prefer ? "OCR → model" : "OCR + vision",
                        prefer ? "Local text only — vision skipped" : "Local text with screenshot");
                }
                else
                {
                    SetOcrUi(OcrUiPhase.Failed,
                        string.IsNullOrWhiteSpace(_localOcr.LastError)
                            ? "OCR produced no text — falling back to screenshot vision."
                            : $"OCR failed: {_localOcr.LastError}");
                    SetActivity("Vision fallback", "OCR unavailable — using screenshot");
                }
            }

            var (provRegion, provContext) = ChatCaptureAttachment.ForProvider(region, context, localOcr);
            // Attach the chosen project folder so providers can read files (cwd + prompt root).
            var request = new AiRequest(prompt, provRegion, provContext, history)
            {
                WorkingDirectory = WorkingDirectory,
                TaskContext = taskContext,
                LocalOcrContext = localOcr,
            };
            var text = await client.AskAsync(request, streamBridge.OnPartial, cts.Token);
            streamBridge.SealPartials();
            SetActivity("Streaming", "Receiving the answer");
            // Text-first turn: if the model needs the screen, capture once; OCR preferred over vision.
            if (!sentVisual && ClarifyingQuestionParser.TryExtractScreenRereadReason(text, out var reason))
            {
                answer.Caption = $"* {providerName} is reading the screen";
                answer.TurnMeta = BuildTurnMeta(providerName, "Chat", hasVisual: true, WorkingDirectory);
                SetActivity("Reading screen", string.IsNullOrWhiteSpace(reason) ? "The provider requested visual context" : reason);
                answer.Text = string.IsNullOrWhiteSpace(reason) ? "Reading screen..." : $"Reading screen: {reason}";
                answer.IsStreaming = false;
                answer.IsPending = true;
                try
                {
                    _owner.Hide();
                    await Task.Delay(150, cts.Token);
                    var path = await _captureService.CaptureScreenAsync(_owner, cts.Token);
                    ReplaceCapture(ref _contextPath, path);
                    // Show the capture that was just taken in the user bubble.
                    AttachCaptureToMessage(user, _regionPath, _contextPath);
                }
                finally { _owner.Show(); _owner.Activate(); }

                SetOcrUi(OcrUiPhase.Running, "NEED_SCREEN → ON-DEVICE OCR RUNNING…");
                SetActivity("OCR RUNNING", "On-device OCR after NEED_SCREEN");
                localOcr = await ResolveOcrForPathsAsync(_regionPath, _contextPath, cts.Token);
                if (localOcr is not null)
                {
                    var prefer = ScreenEvidence.PrefersOcrOverVision(localOcr);
                    SetOcrUi(OcrUiPhase.Ready,
                        prefer
                            ? $"OCR READY after NEED_SCREEN · text only to {providerName}."
                            : $"OCR READY after NEED_SCREEN · OCR + screenshot.");
                    answer.Caption = prefer
                        ? $"* {providerName} · local OCR only (no vision)"
                        : $"* {providerName} · local OCR + screen";
                    answer.TurnMeta = BuildTurnMeta(providerName, prefer ? "OCR-only" : "OCR+vision", hasVisual: true, WorkingDirectory);
                }
                else
                {
                    SetOcrUi(OcrUiPhase.Failed, "OCR failed on NEED_SCREEN — using screenshot vision.");
                }

                var (needRegion, needContext) = ChatCaptureAttachment.ForProvider(_regionPath, _contextPath, localOcr);
                streamBridge.Reopen();
                writeSnapshot = WorkspaceWriteAuditor.Capture(WorkingDirectory);
                BeginLivePair(WorkingDirectory, writeSnapshot, answer);
                var screenRequest = new AiRequest(prompt, needRegion, needContext, history)
                {
                    WorkingDirectory = WorkingDirectory,
                    TaskContext = taskContext,
                    LocalOcrContext = localOcr,
                };
                text = await client.AskAsync(screenRequest, streamBridge.OnPartial, cts.Token);
                streamBridge.SealPartials();
                SetActivity("Streaming", "Receiving the screen-aware answer");
            }
            text = ClarifyingQuestionParser.RemoveScreenRereadDirective(text);
            if (string.IsNullOrWhiteSpace(text))
                text = sentVisual || HasCapture
                    ? "I still need a clearer screen capture."
                    : "I need a screenshot to answer that.";
            ApplyFinalAnswerText(answer, string.IsNullOrWhiteSpace(text) ? "The client returned no answer." : text.Trim());
            answer.Caption = $"✦ {providerName} - {stopwatch.Elapsed.TotalSeconds:0.0} s";
            AttachWriteAudit(answer, writeSnapshot);
            ConsumeEphemeralAttachments();
            RefreshOutcomeChips(prompt);
            if (answer.HasShowWhere)
                ShowWhereOnScreen(answer);
            // Follow-up chips must not delay marking the main answer turn complete.
            _ = GenerateFollowUpSuggestionsAsync();
        }
        catch (OperationCanceledException)
        {
            answer.Caption = $"✦ {providerName} - stopped";
            if (string.IsNullOrWhiteSpace(answer.Text)) answer.Text = "*Stopped.*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Caption = $"✦ {providerName} - error";
            answer.Text = ex.Message;
        }
        finally
        {
            EndLivePairWatch();
            ticker.Stop();
            stopwatch.Stop();
            answer.IsPending = false;
            answer.IsStreaming = false;
            answer.Elapsed = null;
            _requestCts = null;
            SetBusy(false);
        }
    }

    /// <summary>Final answer apply: full clean/extract and promote IsQuestion only when complete.</summary>
    private void ApplyFinalAnswerText(ChatMessage answer, string rawText)
    {
        var (withoutWhere, where) = ShowWhereParser.Extract(rawText);
        var applied = ChatStreamTextPolicy.ApplyFinal(withoutWhere);
        answer.Text = applied.Text;
        answer.ShowWhere = where;
        TryApplyPlanMarkdown(applied.PlanMarkdown, answer);
        if (!applied.IsQuestion) return;
        answer.Question = applied.Question;
        answer.QuestionChoices = applied.QuestionChoices;
        answer.IsQuestion = true;
    }

    /// <summary>
    /// Bridges provider partial callbacks (any thread) into coalesced UI-thread progressive text
    /// updates. Does not promote ASK_USER / IsQuestion - that waits for <see cref="ApplyFinalAnswerText"/>.
    /// </summary>
    private sealed class ChatStreamUiBridge : IDisposable
    {
        private readonly ChatMessage _answer;
        private readonly string _providerName;
        private readonly Action<string?>? _onPlanMarkdown;
        private readonly StreamPartialCoalescer _coalescer = new();
        private readonly DispatcherTimer _flushTimer;
        private int _epoch = 1; // 0 = sealed; reopen bumps so late posts from prior streams never match
        private int _reopenSeq = 1;

        public ChatStreamUiBridge(ChatMessage answer, string providerName, Action<string?>? onPlanMarkdown = null)
        {
            _answer = answer;
            _providerName = providerName;
            _onPlanMarkdown = onPlanMarkdown;
            _flushTimer = new DispatcherTimer(StreamPartialCoalescer.DefaultInterval, DispatcherPriority.Background,
                (_, _) => TryFlushHeld());
            _flushTimer.Start();
        }

        /// <summary>Provider stream callback - may run off the UI thread.</summary>
        public void OnPartial(string partial)
        {
            var epoch = Volatile.Read(ref _epoch);
            if (epoch == 0) return;
            if (_coalescer.TryPublishNow(partial, DateTime.UtcNow, out var publish))
                Dispatcher.UIThread.Post(() => ApplyPartial(publish, epoch));
        }

        /// <summary>Stops progressive applies so late posts cannot overwrite the final answer.</summary>
        public void SealPartials()
        {
            _flushTimer.Stop();
            Volatile.Write(ref _epoch, 0);
            // Discard held text - finalize uses the complete AskAsync return value.
            _ = _coalescer.TryFlush(DateTime.UtcNow, out _);
        }

        /// <summary>Allows progressive applies again (e.g. NEED_SCREEN retry stream).</summary>
        public void Reopen()
        {
            var next = Interlocked.Increment(ref _reopenSeq);
            Volatile.Write(ref _epoch, next);
            _flushTimer.Start();
        }

        private void TryFlushHeld()
        {
            var epoch = Volatile.Read(ref _epoch);
            if (epoch == 0) return;
            if (_coalescer.TryFlush(DateTime.UtcNow, out var publish))
                ApplyPartial(publish, epoch);
        }

        private void ApplyPartial(string raw, int epoch)
        {
            if (Volatile.Read(ref _epoch) != epoch) return;
            _answer.IsPending = false;
            _answer.IsStreaming = true;
            _answer.Caption = $"✦ {_providerName}";
            // Progressive text only - never flip IsQuestion from mid-stream fragments.
            // Plan body may update the plan window (check-offs) during plan/implement tracking.
            var applied = ChatStreamTextPolicy.ApplyPartial(raw);
            _answer.Text = applied.Text;
            _onPlanMarkdown?.Invoke(applied.PlanMarkdown);
        }

        public void Dispose()
        {
            _flushTimer.Stop();
            Volatile.Write(ref _epoch, 0);
        }
    }
}
