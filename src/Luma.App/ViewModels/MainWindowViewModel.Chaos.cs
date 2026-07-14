using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed partial class MainWindowViewModel
{
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
            { Caption = "Focus lock" });
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
            Caption = "Chaos debate",
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
                var localOcr = await ResolveOcrForPathsAsync(_regionPath, _contextPath, cts.Token);
                var (provR, provC) = ChatCaptureAttachment.ForProvider(_regionPath, _contextPath, localOcr);
                var reqA = new AiRequest(ChaosMode.DualDebatePrompt(topic, "Side A - ship the bold option"), provR, provC, [])
                { WorkingDirectory = WorkingDirectory, TaskContext = BuildTurnContext(topic), LocalOcrContext = localOcr };
                var reqB = new AiRequest(ChaosMode.DualDebatePrompt(topic, "Side B - ship the careful option"), provR, provC, [])
                { WorkingDirectory = WorkingDirectory, TaskContext = BuildTurnContext(topic), LocalOcrContext = localOcr };
                var taskA = clientA.AskAsync(reqA, null, cts.Token);
                var taskB = clientB.AskAsync(reqB, null, cts.Token);
                await Task.WhenAll(taskA, taskB);
                answer.Text = ChaosMode.FormatDualDebate(Providers[a], await taskA, Providers[b], await taskB);
                answer.Caption = $"{Providers[a]} vs {Providers[b]}";
            }
            else
            {
                var localOcr = await ResolveOcrForPathsAsync(_regionPath, _contextPath, cts.Token);
                var (provR, provC) = ChatCaptureAttachment.ForProvider(_regionPath, _contextPath, localOcr);
                var text = await _clientFactory.Create((AiProvider)providers[0])
                    .AskAsync(new AiRequest(ChaosMode.DebatePrompt(topic), provR, provC, [])
                    {
                        WorkingDirectory = WorkingDirectory,
                        TaskContext = BuildTurnContext(topic),
                        LocalOcrContext = localOcr,
                    }, partial => Dispatcher.UIThread.Post(() =>
                    {
                        answer.IsPending = false;
                        answer.IsStreaming = true;
                        answer.Text = ChatStreamTextPolicy.ApplyPartial(partial).Text;
                    }), cts.Token);
                ApplyFinalAnswerText(answer, text);
                answer.Caption = $"{Providers[providers[0]]} (solo debate)";
            }
            ConsumeEphemeralAttachments();
            _ = GenerateFollowUpSuggestionsAsync();
        }
        catch (OperationCanceledException)
        {
            answer.Caption = "debate stopped";
            if (string.IsNullOrWhiteSpace(answer.Text)) answer.Text = "*Stopped.*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Caption = "debate error";
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
}
