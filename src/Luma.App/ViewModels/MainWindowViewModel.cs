using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Window _owner;
    private readonly IScreenCaptureService _captureService;
    private readonly IAiClientFactory _clientFactory;
    private readonly IRunningOperationCoordinator _operations;
    private readonly ProviderDiagnostics _diagnostics;
    private readonly IScreenDifferenceService _screenDifference;
    private readonly DispatcherTimer _operationTicker;
    private readonly CancellationTokenSource _lifetime = new();
    private string? _regionPath;
    private string? _contextPath;
    private Bitmap? _preview;
    private string _question = string.Empty;
    private bool _busy;
    private bool _refreshingContext;
    private bool _suggesting;
    private CancellationTokenSource? _suggestCts;
    private DateTime _suggestionsAt = DateTime.MinValue;
    private int _selectedProviderIndex;
    private string? _workingDirectory;
    private CancellationTokenSource? _requestCts;
    private ProviderDiagnostic? _claudeDiagnostic;
    private ProviderDiagnostic? _codexDiagnostic;
    private ProviderDiagnostic? _grokDiagnostic;
    private bool _diagnosticsReady;
    private bool _globalExplainShortcutAvailable;
    private string _runningStatus = string.Empty;

    public MainWindowViewModel(Window owner, IScreenCaptureService captureService, IAiClientFactory clientFactory,
        IRunningOperationCoordinator operations, ProviderDiagnostics diagnostics, IScreenDifferenceService screenDifference)
    {
        _owner = owner;
        _captureService = captureService;
        _clientFactory = clientFactory;
        _operations = operations;
        _diagnostics = diagnostics;
        _screenDifference = screenDifference;
        _operationTicker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => RefreshOperationStatus());
        CaptureCommand = new AsyncCommand(CaptureAsync, () => IsIdle);
        ExplainSelectionCommand = new AsyncCommand(ExplainSelectionAsync, () => IsIdle && SelectedDiagnostic?.IsAvailable != false);
        ExplainScreenCommand = new AsyncCommand(ExplainScreenAsync, () => IsIdle && HasContext && SelectedDiagnostic?.IsAvailable != false);
        SendCommand = new AsyncCommand(SendAsync, () => CanSend);
        ClearCaptureCommand = new RelayCommand(ClearCapture);
        StopCommand = new RelayCommand(_operations.CancelAll, () => HasRunningOperations);
        CopyMessageCommand = new ParameterCommand(CopyMessage);
        AnswerQuestionCommand = new AsyncParameterCommand(AnswerQuestionAsync);
        SkipQuestionCommand = new AsyncParameterCommand(SkipQuestionAsync);
        UseSuggestionCommand = new AsyncParameterCommand(UseSuggestionAsync);
        _operations.Changed += OnOperationsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public IReadOnlyList<string> Providers { get; } = ["Claude", "Codex", "Grok"];
    public AsyncCommand CaptureCommand { get; }
    public AsyncCommand ExplainSelectionCommand { get; }
    public AsyncCommand ExplainScreenCommand { get; }
    public AsyncCommand SendCommand { get; }
    public RelayCommand ClearCaptureCommand { get; }
    public RelayCommand StopCommand { get; }
    public ParameterCommand CopyMessageCommand { get; }
    public AsyncParameterCommand AnswerQuestionCommand { get; }
    public AsyncParameterCommand SkipQuestionCommand { get; }
    public AsyncParameterCommand UseSuggestionCommand { get; }
    /// <summary>Short prompt ideas derived from the ambient screen capture, shown as chips.</summary>
    public ObservableCollection<string> Suggestions { get; } = [];
    public bool IsSuggesting
    {
        get => _suggesting;
        private set
        {
            Set(ref _suggesting, value);
            NotifySurfaceStateChanged();
        }
    }
    public Func<TaskLaunchRequest, Task<bool>>? TaskLaunchRequested { get; set; }
    public Func<Task<string?>>? WorkingDirectoryRequested { get; set; }
    public Func<Task<bool>>? NewChatConfirmationRequested { get; set; }
    public Action? ScreenExplanationReadyToShow { get; set; }

    public int SelectedProviderIndex { get => _selectedProviderIndex; set { Set(ref _selectedProviderIndex, value); OnPropertyChanged(nameof(CanSend)); OnPropertyChanged(nameof(ProviderStatus)); OnPropertyChanged(nameof(HasProviderProblem)); SendCommand.RaiseCanExecuteChanged(); ExplainSelectionCommand.RaiseCanExecuteChanged(); ExplainScreenCommand.RaiseCanExecuteChanged(); } }
    public string? WorkingDirectory { get => _workingDirectory; set { Set(ref _workingDirectory, value); OnPropertyChanged(nameof(WorkingDirectoryLabel)); } }
    public string WorkingDirectoryLabel => WorkingDirectory is null ? "Choose project..." : Path.GetFileName(WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public string Question { get => _question; set { Set(ref _question, value); OnPropertyChanged(nameof(CanSend)); SendCommand.RaiseCanExecuteChanged(); } }
    public Bitmap? Preview { get => _preview; private set => Set(ref _preview, value); }
    public bool HasCapture => _regionPath is not null || _contextPath is not null;
    public bool HasContext => _contextPath is not null;
    public bool HasRegion => _regionPath is not null;
    /// <summary>True when only the ambient full-screen capture is held - shown as a slim pill
    /// rather than the image preview, which is reserved for regions the user snipped.</summary>
    public bool HasContextOnly => _contextPath is not null && _regionPath is null;
    public string PreviewLabel => _regionPath is not null ? "Selected region" : "Your screen";
    public bool HasAssistantMemory => !string.IsNullOrWhiteSpace(AppSettings.Current.AssistantMemory);
    public string AssistantMemoryPreview => MemoryPreview(AppSettings.Current.AssistantMemory);
    public string SurfaceStatus => _refreshingContext ? "Capturing" : _busy ? "Working" : IsSuggesting ? "Preparing shortcuts" : HasRegion ? "Region ready" : HasContext ? "Screen ready" : "Ready";
    public string SurfaceDetail => _refreshingContext ? "Refreshing screen context" : _busy ? (HasRunningOperations ? RunningStatus : "Waiting for the provider") : IsSuggesting ? "Generating quick actions" : HasRegion ? "Selected area stays in focus until you clear it" : HasContext ? "Screen context loaded" : "No screen context yet";
    public string LandingTitle => HasCapture ? "Ask about what is on your screen." : "Start with the screen.";
    public string LandingSubtitle => HasCapture ? "Use a shortcut chip, type a question, or snip a tighter region." : "Explain the screen, snip a region, or just ask.";
    public bool IsIdle => !_busy;
    public bool IsBusy => _busy;
    public bool CanSend => !_busy && !string.IsNullOrWhiteSpace(Question) && SelectedDiagnostic?.IsAvailable != false;
    public bool HasProviderProblem => SelectedDiagnostic?.IsAvailable == false;
    public string ProviderStatus => SelectedDiagnostic?.Message ?? string.Empty;
    public bool HasRunningOperations => _operations.Active.Count > 0;
    public bool GlobalExplainShortcutAvailable { get => _globalExplainShortcutAvailable; set => Set(ref _globalExplainShortcutAvailable, value); }
    public string RunningStatus { get => _runningStatus; private set => Set(ref _runningStatus, value); }
    private ProviderDiagnostic? SelectedDiagnostic => SelectedProviderIndex switch
    {
        0 => _claudeDiagnostic,
        1 => _codexDiagnostic,
        _ => _grokDiagnostic
    };

    public async Task InitializeDiagnosticsAsync()
    {
        var claude = _diagnostics.CheckAsync("claude", _lifetime.Token);
        var codex = _diagnostics.CheckAsync("codex", _lifetime.Token);
        var grok = _diagnostics.CheckAsync("grok", _lifetime.Token);
        await Task.WhenAll(claude, codex, grok);
        _claudeDiagnostic = await claude;
        _codexDiagnostic = await codex;
        _grokDiagnostic = await grok;
        _diagnosticsReady = true;
        SelectedProviderIndex = ProviderAvailability.Select(SelectedProviderIndex,
            [_claudeDiagnostic, _codexDiagnostic, _grokDiagnostic]);
        OnPropertyChanged(nameof(ProviderStatus)); OnPropertyChanged(nameof(HasProviderProblem)); OnPropertyChanged(nameof(CanSend));
        SendCommand.RaiseCanExecuteChanged(); ExplainSelectionCommand.RaiseCanExecuteChanged(); ExplainScreenCommand.RaiseCanExecuteChanged();
        NotifySurfaceStateChanged();
        if (_contextPath is not null && Messages.Count == 0) _ = GenerateSuggestionsAsync();
    }

    private void OnOperationsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RefreshOperationStatus();
        if (HasRunningOperations) _operationTicker.Start(); else _operationTicker.Stop();
        OnPropertyChanged(nameof(HasRunningOperations));
        StopCommand.RaiseCanExecuteChanged();
        NotifySurfaceStateChanged();
    });

    private void RefreshOperationStatus()
    {
        var active = _operations.Active;
        RunningStatus = active.Count == 0 ? string.Empty : active.Count == 1
            ? $"{active[0].Name} - {(DateTimeOffset.UtcNow - active[0].StartedAt).TotalSeconds:0}s"
            : $"{active.Count} processes running - {(DateTimeOffset.UtcNow - active[0].StartedAt).TotalSeconds:0}s";
        NotifySurfaceStateChanged();
    }

    /// <summary>Grabs the whole screen as background context. Called when the panel opens, while
    /// the window is still the small collapsed dock, so there's no need to hide it first.</summary>
    public async Task RefreshContextAsync()
    {
        if (_busy || _refreshingContext) return;
        _refreshingContext = true;
        try
        {
            var path = await _captureService.CaptureScreenAsync(_owner, _lifetime.Token);
            var difference = _contextPath is null ? 1d : _screenDifference.Measure(_contextPath, path);
            if (Messages.Count > 0 && difference >= .16 &&
                NewChatConfirmationRequested is not null && await NewChatConfirmationRequested())
            {
                Messages.Clear();
                Suggestions.Clear();
            }
            ReplaceCapture(ref _contextPath, path);
            _ = GenerateSuggestionsAsync(difference);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Messages.Add(new ChatMessage("assistant", $"Screen context capture failed: {ex.Message}") { IsError = true }); }
        finally
        {
            _refreshingContext = false;
            NotifySurfaceStateChanged();
        }
    }

    /// <summary>Asks the provider for a few short prompt ideas based on the ambient capture and
    /// shows them as chips. The chips are a bonus, so failures stay silent and a newer request,
    /// a send, or an existing conversation simply wins over the pending one.</summary>
    private async Task GenerateSuggestionsAsync(double screenDifference = 1d)
    {
        if (_contextPath is null || Messages.Count > 0 || _busy) return;
        if (!_diagnosticsReady) return;
        if (!AppSettings.Current.SuggestFromScreen) return;
        // Chips regenerate on every open by default; a nonzero reuse window (Settings) keeps
        // recent ones instead, saving a provider call.
        if (Suggestions.Count > 0 &&
            DateTime.UtcNow - _suggestionsAt < TimeSpan.FromSeconds(AppSettings.Current.SuggestionFreshSeconds)) return;
        // If the screen looks the same as when the current chips were made, they're still
        // accurate - skip the provider call (and its screenshot tokens) entirely.
        if (Suggestions.Count > 0 && AppSettings.Current.SkipSuggestionsWhenScreenUnchanged &&
            screenDifference < .05) return;
        _suggestCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _suggestCts = cts;
        // Existing chips (e.g. pre-warmed at launch) stay visible while the fresh batch loads.
        IsSuggesting = true;
        string? thumbnailPath = null;
        try
        {
            var contextPath = _contextPath;
            thumbnailPath = await Task.Run(() => CreateSuggestionThumbnail(contextPath), cts.Token);
            var request = new AiRequest("What might I want to ask about this screen?", null, thumbnailPath ?? contextPath, [])
            { TaskKind = TaskKind.Suggest };
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(request, null, cts.Token);
            if (cts.IsCancellationRequested || Messages.Count > 0 || _busy) return;
            var parsed = SuggestionParser.Parse(text, AppSettings.Current.SuggestionCount);
            if (parsed.Count == 0) return; // keep the old chips rather than blanking the panel
            Suggestions.Clear();
            foreach (var suggestion in parsed) Suggestions.Add(suggestion);
            _suggestionsAt = DateTime.UtcNow;
        }
        catch { }
        finally
        {
            if (thumbnailPath is not null) { try { File.Delete(thumbnailPath); } catch { } }
            if (_suggestCts == cts) { _suggestCts = null; IsSuggesting = false; }
            cts.Dispose();
        }
    }

    private async Task GenerateFollowUpSuggestionsAsync()
    {
        _suggestCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _suggestCts = cts;
        IsSuggesting = true;
        try
        {
            var history = Messages.ToArray();
            var request = new AiRequest("Suggest likely next replies.", null, null, history)
            { TaskKind = TaskKind.FollowUp };
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(request, null, cts.Token);
            if (cts.IsCancellationRequested) return;
            var parsed = SuggestionParser.Parse(text, AppSettings.Current.SuggestionCount);
            Suggestions.Clear();
            foreach (var suggestion in parsed) Suggestions.Add(suggestion);
        }
        catch (OperationCanceledException) { }
        catch { Suggestions.Clear(); }
        finally
        {
            if (_suggestCts == cts) { _suggestCts = null; IsSuggesting = false; }
            cts.Dispose();
        }
    }

    /// <summary>Chips only need coarse legibility, so the suggestion request ships a downscaled
    /// copy of the ambient capture instead of the full-resolution screen. Returns null (use the
    /// original) when the capture is already small or downscaling fails.</summary>
    private static string? CreateSuggestionThumbnail(string sourcePath)
    {
        var maxWidth = AppSettings.Current.SuggestionImageMaxWidth;
        try
        {
            using var source = new Bitmap(sourcePath);
            if (source.PixelSize.Width <= maxWidth) return null;
            var height = (int)Math.Round(source.PixelSize.Height * (double)maxWidth / source.PixelSize.Width);
            using var scaled = source.CreateScaledBitmap(new Avalonia.PixelSize(maxWidth, height));
            var path = Path.Combine(Path.GetTempPath(), "Luma", $"suggest-{Guid.NewGuid():N}.png");
            scaled.Save(path);
            return path;
        }
        catch { return null; }
    }

    /// <summary>Runs a clicked suggestion chip as a plain chat turn regardless of the selected
    /// mode - suggestions describe what is on screen, never code or shell work.</summary>
    private async Task UseSuggestionAsync(object? parameter)
    {
        if (parameter is not string suggestion || _busy) return;
        _suggestCts?.Cancel();
        Suggestions.Clear();
        await RunTurnAsync(suggestion);
    }

    private async Task CaptureAsync()
        => await CaptureRegionForContextAsync();

    private async Task ExplainSelectionAsync()
    {
        _suggestCts?.Cancel();
        Suggestions.Clear();
        if (!await CaptureRegionForContextAsync()) return;
        ScreenExplanationReadyToShow?.Invoke();
        await RunTurnAsync(
            "Explain the selected region clearly. Identify what is shown, what it means in context, and any important " +
            "error, warning, control, data, or next action visible there. Be specific and practical; do not merely describe the pixels.",
            "Explain this selection");
    }

    private async Task ExplainScreenAsync()
    {
        _suggestCts?.Cancel();
        Suggestions.Clear();
        if (!await CaptureFullScreenForContextAsync()) return;
        ScreenExplanationReadyToShow?.Invoke();
        await RunTurnAsync(
            "Explain what is important on this screen. Identify the application or content, summarize what the user is " +
            "looking at, call out any error, warning, unusual state, or likely point of confusion, and suggest the most useful next action. " +
            "Prioritize meaning over a generic visual description.",
            "Explain this screen");
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
        Suggestions.Clear();
        var prompt = Question.Trim();
        Question = string.Empty;
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
        await RunTurnAsync(prompt);
    }

    /// <summary>Sends the user's typed reply to a pending clarifying question as the next turn.</summary>
    private Task AnswerQuestionAsync(object? parameter)
    {
        if (parameter is not ChatMessage source || _busy) return Task.CompletedTask;
        var reply = source.QuestionAnswer.Trim();
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
        source.CodeSession is { } session ? RunCodeContinuationAsync(source, session, answer) : RunTurnAsync(answer);

    private async Task RunTurnAsync(string prompt, string? displayPrompt = null)
    {
        var provider = (AiProvider)SelectedProviderIndex;
        var providerName = Providers[SelectedProviderIndex];
        var hadCapture = HasCapture;
        Messages.Add(new ChatMessage("user", displayPrompt ?? prompt));
        var answer = new ChatMessage("assistant", string.Empty, isPending: true)
        {
            Caption = hadCapture ? $"* {providerName} is reading your screen" : $"* {providerName} is thinking",
            Text = hadCapture ? "Reading screen…" : "Thinking…",
        };
        Messages.Add(answer);
        SetBusy(true);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        var stopwatch = Stopwatch.StartNew();
        var ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => answer.Elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} s");
        ticker.Start();
        // Coalesce stream partials onto a short interval so multi-chunk output does not schedule
        // one UI apply per line; progressive text still appears and finalize applies full extract.
        using var streamBridge = new ChatStreamUiBridge(answer, providerName);
        try
        {
            var history = Messages.Take(Messages.Count - 2).ToArray();
            var client = _clientFactory.Create(provider);
            // Always attach available captures on the first request. An earlier regression set
            // probeWithoutScreen = HasCapture, which stripped images whenever a screenshot existed.
            var request = new AiRequest(prompt, _regionPath, _contextPath, history);
            var text = await client.AskAsync(request, streamBridge.OnPartial, cts.Token);
            streamBridge.SealPartials();
            // Text-only first turn: if the model asks for the screen, capture once and retry with it.
            if (!hadCapture && ClarifyingQuestionParser.TryExtractScreenRereadReason(text, out var reason))
            {
                answer.Caption = $"* {providerName} is reading the screen";
                answer.Text = string.IsNullOrWhiteSpace(reason) ? "Reading screen…" : $"Reading screen: {reason}";
                answer.IsStreaming = false;
                answer.IsPending = true;
                try
                {
                    _owner.Hide();
                    await Task.Delay(150, cts.Token);
                    var path = await _captureService.CaptureScreenAsync(_owner, cts.Token);
                    ReplaceCapture(ref _contextPath, path);
                }
                finally { _owner.Show(); _owner.Activate(); }

                streamBridge.Reopen();
                var screenRequest = new AiRequest(prompt, _regionPath, _contextPath, history);
                text = await client.AskAsync(screenRequest, streamBridge.OnPartial, cts.Token);
                streamBridge.SealPartials();
            }
            text = ClarifyingQuestionParser.RemoveScreenRereadDirective(text);
            if (string.IsNullOrWhiteSpace(text))
                text = hadCapture || HasCapture
                    ? "I still need a clearer screen capture."
                    : "I need a screenshot to answer that.";
            ApplyFinalAnswerText(answer, string.IsNullOrWhiteSpace(text) ? "The client returned no answer." : text.Trim());
            answer.Caption = $"✦ {providerName} - {stopwatch.Elapsed.TotalSeconds:0.0} s";
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
            ticker.Stop();
            stopwatch.Stop();
            answer.IsPending = false;
            answer.IsStreaming = false;
            answer.Elapsed = null;
            _requestCts = null;
            SetBusy(false);
        }
    }

    /// <summary>Runs a coding request inline: streams the assistant's explanation into a normal
    /// chat bubble like RunTurnAsync, and attaches a CodeChatSession that drives the diff review
    /// card (DiffCardControl) once a patch artifact arrives.</summary>
    public async Task RunCodeTurnAsync(string prompt, string repository)
    {
        var provider = (AiProvider)SelectedProviderIndex;
        var providerName = Providers[SelectedProviderIndex];
        Messages.Add(new ChatMessage("user", prompt));
        var answer = new ChatMessage("assistant", string.Empty, isPending: true)
        {
            Caption = $"✦ {providerName} is inspecting the repository",
            Text = "Inspecting repository…",
        };
        Messages.Add(answer);
        SetBusy(true);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        var stopwatch = Stopwatch.StartNew();
        var ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => answer.Elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} s");
        ticker.Start();
        try
        {
            var session = new CodeChatSession(answer, _clientFactory, provider, new GitService(), new ShellService(_operations), repository, _regionPath, _contextPath);
            answer.CodeSession = session;
            await session.RunAsync(prompt, cts.Token);
            answer.Caption = $"✦ {providerName} - {stopwatch.Elapsed.TotalSeconds:0.0} s";
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
            ticker.Stop();
            stopwatch.Stop();
            answer.IsPending = false;
            answer.IsStreaming = false;
            answer.Elapsed = null;
            _requestCts = null;
            SetBusy(false);
        }
    }

    /// <summary>Resumes a CodeChatSession after its clarifying question was answered - continues
    /// the same message/diff card rather than starting an unrelated plain-chat turn.</summary>
    private async Task RunCodeContinuationAsync(ChatMessage answer, CodeChatSession session, string reply)
    {
        SetBusy(true);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        answer.IsPending = true;
        var stopwatch = Stopwatch.StartNew();
        var ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => answer.Elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} s");
        ticker.Start();
        try { await session.ContinueAsync(reply, cts.Token); _ = GenerateFollowUpSuggestionsAsync(); }
        catch (OperationCanceledException) { answer.Caption = "✦ stopped"; }
        catch (Exception ex) { answer.IsError = true; answer.Text = ex.Message; }
        finally
        {
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
    private static void ApplyFinalAnswerText(ChatMessage answer, string rawText)
    {
        var applied = ChatStreamTextPolicy.ApplyFinal(rawText);
        answer.Text = applied.Text;
        if (!applied.IsQuestion) return;
        answer.Question = applied.Question;
        answer.QuestionChoices = applied.QuestionChoices;
        answer.IsQuestion = true;
    }

    /// <summary>
    /// Bridges provider partial callbacks (any thread) into coalesced UI-thread progressive text
    /// updates. Does not promote ASK_USER / IsQuestion — that waits for <see cref="ApplyFinalAnswerText"/>.
    /// </summary>
    private sealed class ChatStreamUiBridge : IDisposable
    {
        private readonly ChatMessage _answer;
        private readonly string _providerName;
        private readonly StreamPartialCoalescer _coalescer = new();
        private readonly DispatcherTimer _flushTimer;
        private int _epoch = 1; // 0 = sealed; reopen bumps so late posts from prior streams never match
        private int _reopenSeq = 1;

        public ChatStreamUiBridge(ChatMessage answer, string providerName)
        {
            _answer = answer;
            _providerName = providerName;
            _flushTimer = new DispatcherTimer(StreamPartialCoalescer.DefaultInterval, DispatcherPriority.Background,
                (_, _) => TryFlushHeld());
            _flushTimer.Start();
        }

        /// <summary>Provider stream callback — may run off the UI thread.</summary>
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
            // Discard held text — finalize uses the complete AskAsync return value.
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
            // Progressive text only — never flip IsQuestion from mid-stream fragments.
            _answer.Text = ChatStreamTextPolicy.ApplyPartial(raw).Text;
        }

        public void Dispose()
        {
            _flushTimer.Stop();
            Volatile.Write(ref _epoch, 0);
        }
    }

    private void CopyMessage(object? parameter)
    {
        if (parameter is not ChatMessage message) return;
        if (_owner.Clipboard is not null)
            _ = ClipboardExtensions.SetTextAsync(_owner.Clipboard, message.Text);
    }

    /// <summary>Removes the active capture: a selected region falls back to the full-screen
    /// context; clearing again removes the context too.</summary>
    private void ClearCapture()
    {
        if (_regionPath is not null) ReplaceCapture(ref _regionPath, null);
        else ReplaceCapture(ref _contextPath, null);
    }

    private void ReplaceCapture(ref string? slot, string? newPath)
    {
        if (slot is not null) { try { File.Delete(slot); } catch { } }
        slot = newPath;
        Preview?.Dispose();
        var active = _regionPath ?? _contextPath;
        Preview = active is null ? null : new Bitmap(active);
        OnPropertyChanged(nameof(HasCapture)); OnPropertyChanged(nameof(HasRegion)); OnPropertyChanged(nameof(HasContext));
        OnPropertyChanged(nameof(HasContextOnly));
        OnPropertyChanged(nameof(PreviewLabel)); OnPropertyChanged(nameof(CanSend));
        NotifySurfaceStateChanged();
        SendCommand.RaiseCanExecuteChanged();
        ExplainScreenCommand.RaiseCanExecuteChanged();
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanSend));
        NotifySurfaceStateChanged();
        CaptureCommand.RaiseCanExecuteChanged(); ExplainSelectionCommand.RaiseCanExecuteChanged(); ExplainScreenCommand.RaiseCanExecuteChanged(); SendCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void NotifySurfaceStateChanged()
    {
        OnPropertyChanged(nameof(SurfaceStatus));
        OnPropertyChanged(nameof(SurfaceDetail));
        OnPropertyChanged(nameof(LandingTitle));
        OnPropertyChanged(nameof(LandingSubtitle));
    }

    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(HasAssistantMemory));
        OnPropertyChanged(nameof(AssistantMemoryPreview));
    }

    private static string MemoryPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var firstLine = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        if (firstLine.Length > 140) firstLine = firstLine[..140].TrimEnd() + "…";
        return firstLine;
    }
    public void Dispose()
    {
        _operations.Changed -= OnOperationsChanged;
        _operationTicker.Stop();
        _lifetime.Cancel();
        ReplaceCapture(ref _regionPath, null);
        ReplaceCapture(ref _contextPath, null);
        _lifetime.Dispose();
    }
}
