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
    public bool IsSuggesting { get => _suggesting; private set => Set(ref _suggesting, value); }
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
        if (_contextPath is not null && Messages.Count == 0) _ = GenerateSuggestionsAsync();
    }

    private void OnOperationsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RefreshOperationStatus();
        if (HasRunningOperations) _operationTicker.Start(); else _operationTicker.Stop();
        OnPropertyChanged(nameof(HasRunningOperations));
        StopCommand.RaiseCanExecuteChanged();
    });

    private void RefreshOperationStatus()
    {
        var active = _operations.Active;
        RunningStatus = active.Count == 0 ? string.Empty : active.Count == 1
            ? $"{active[0].Name} - {(DateTimeOffset.UtcNow - active[0].StartedAt).TotalSeconds:0}s"
            : $"{active.Count} processes running - {(DateTimeOffset.UtcNow - active[0].StartedAt).TotalSeconds:0}s";
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
            if (_contextPath is not null && Messages.Count > 0 &&
                _screenDifference.Measure(_contextPath, path) >= .16 &&
                NewChatConfirmationRequested is not null && await NewChatConfirmationRequested())
            {
                Messages.Clear();
                Suggestions.Clear();
            }
            ReplaceCapture(ref _contextPath, path);
            _ = GenerateSuggestionsAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Messages.Add(new ChatMessage("assistant", $"Screen context capture failed: {ex.Message}") { IsError = true }); }
        finally { _refreshingContext = false; }
    }

    /// <summary>Asks the provider for a few short prompt ideas based on the ambient capture and
    /// shows them as chips. The chips are a bonus, so failures stay silent and a newer request,
    /// a send, or an existing conversation simply wins over the pending one.</summary>
    private async Task GenerateSuggestionsAsync()
    {
        if (_contextPath is null || Messages.Count > 0 || _busy) return;
        if (!_diagnosticsReady) return;
        if (!AppSettings.Current.SuggestFromScreen) return;
        // Chips regenerate on every open by default; a nonzero reuse window (Settings) keeps
        // recent ones instead, saving a provider call.
        if (Suggestions.Count > 0 &&
            DateTime.UtcNow - _suggestionsAt < TimeSpan.FromSeconds(AppSettings.Current.SuggestionFreshSeconds)) return;
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
        TaskKind kind;
        try { kind = await RoutePromptAsync(prompt); }
        catch (OperationCanceledException) { Question = prompt; return; }
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

    private async Task<TaskKind> RoutePromptAsync(string prompt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        SetBusy(true);
        try
        {
            var request = new AiRequest(prompt, null, null, []) { TaskKind = TaskKind.Route };
            var result = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(request, null, cts.Token);
            var route = result.Trim().ToUpperInvariant();
            if (route.Contains("CODE", StringComparison.Ordinal)) return TaskKind.Code;
            if (route.Contains("COMMAND", StringComparison.Ordinal)) return TaskKind.Shell;
            if (route.Contains("CHAT", StringComparison.Ordinal)) return TaskKind.Chat;
            var fallback = TaskRouter.Classify(prompt);
            return fallback is TaskKind.Code or TaskKind.Shell ? fallback : TaskKind.Chat;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            var fallback = TaskRouter.Classify(prompt);
            return fallback is TaskKind.Code or TaskKind.Shell ? fallback : TaskKind.Chat;
        }
        finally { _requestCts = null; SetBusy(false); }
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
        Messages.Add(new ChatMessage("user", displayPrompt ?? prompt));
        var answer = new ChatMessage("assistant", string.Empty, isPending: true)
        {
            Caption = HasCapture ? $"* {providerName} is reading your screen" : $"* {providerName} is thinking",
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
            var history = Messages.Take(Messages.Count - 2).ToArray();
            var request = new AiRequest(prompt, _regionPath, _contextPath, history);
            var text = await _clientFactory.Create(provider).AskAsync(request,
                partial => Dispatcher.UIThread.Post(() =>
                {
                    answer.IsPending = false;
                    answer.IsStreaming = true;
                    answer.Caption = $"* {providerName}";
                    ApplyAnswerText(answer, partial);
                }), cts.Token);
            ApplyAnswerText(answer, string.IsNullOrWhiteSpace(text) ? "The client returned no answer." : text.Trim());
            answer.Caption = $"* {providerName} - {stopwatch.Elapsed.TotalSeconds:0.0} s";
            _ = GenerateFollowUpSuggestionsAsync();
        }
        catch (OperationCanceledException)
        {
            answer.Caption = $"* {providerName} - stopped";
            if (string.IsNullOrWhiteSpace(answer.Text)) answer.Text = "*Stopped.*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Caption = $"* {providerName} - error";
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
            Caption = $"* {providerName} is inspecting the repository",
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
            answer.Caption = $"* {providerName} - {stopwatch.Elapsed.TotalSeconds:0.0} s";
            _ = GenerateFollowUpSuggestionsAsync();
        }
        catch (OperationCanceledException)
        {
            answer.Caption = $"* {providerName} - stopped";
            if (string.IsNullOrWhiteSpace(answer.Text)) answer.Text = "*Stopped.*";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Caption = $"* {providerName} - error";
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
        catch (OperationCanceledException) { answer.Caption = "* stopped"; }
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

    private static void ApplyAnswerText(ChatMessage answer, string rawText)
    {
        var clarification = ClarifyingQuestionParser.ExtractDetailed(TextSanitizer.Clean(rawText));
        answer.Text = TextSanitizer.Clean(clarification.Text);
        if (clarification.Question is not null)
        {
            answer.Question = TextSanitizer.Clean(clarification.Question);
            answer.QuestionChoices = clarification.Choices;
            answer.IsQuestion = true;
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
        SendCommand.RaiseCanExecuteChanged();
        ExplainScreenCommand.RaiseCanExecuteChanged();
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanSend));
        CaptureCommand.RaiseCanExecuteChanged(); ExplainSelectionCommand.RaiseCanExecuteChanged(); ExplainScreenCommand.RaiseCanExecuteChanged(); SendCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
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
