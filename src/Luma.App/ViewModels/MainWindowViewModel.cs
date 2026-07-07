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
    private readonly CancellationTokenSource _lifetime = new();
    private string? _regionPath;
    private string? _contextPath;
    private Bitmap? _preview;
    private string _question = string.Empty;
    private bool _busy;
    private bool _refreshingContext;
    private int _selectedProviderIndex;
    private int _selectedModeIndex;
    private string? _workingDirectory;
    private CancellationTokenSource? _requestCts;

    public MainWindowViewModel(Window owner, IScreenCaptureService captureService, IAiClientFactory clientFactory)
    {
        _owner = owner;
        _captureService = captureService;
        _clientFactory = clientFactory;
        CaptureCommand = new AsyncCommand(CaptureAsync, () => IsIdle);
        SendCommand = new AsyncCommand(SendAsync, () => CanSend);
        ClearCaptureCommand = new RelayCommand(ClearCapture);
        StopCommand = new RelayCommand(() => _requestCts?.Cancel(), () => IsBusy);
        CopyMessageCommand = new ParameterCommand(CopyMessage);
        AnswerQuestionCommand = new AsyncParameterCommand(AnswerQuestionAsync);
        SkipQuestionCommand = new AsyncParameterCommand(SkipQuestionAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public IReadOnlyList<string> Providers { get; } = ["Claude", "Codex"];
    public IReadOnlyList<string> Modes { get; } = ["Ask", "Code", "Command"];
    public AsyncCommand CaptureCommand { get; }
    public AsyncCommand SendCommand { get; }
    public RelayCommand ClearCaptureCommand { get; }
    public RelayCommand StopCommand { get; }
    public ParameterCommand CopyMessageCommand { get; }
    public AsyncParameterCommand AnswerQuestionCommand { get; }
    public AsyncParameterCommand SkipQuestionCommand { get; }
    public Func<TaskLaunchRequest, Task<bool>>? TaskLaunchRequested { get; set; }
    public Func<Task<string?>>? WorkingDirectoryRequested { get; set; }

    public int SelectedProviderIndex { get => _selectedProviderIndex; set { Set(ref _selectedProviderIndex, value); OnPropertyChanged(nameof(CanSend)); } }
    public int SelectedModeIndex { get => _selectedModeIndex; set { Set(ref _selectedModeIndex, value); OnPropertyChanged(nameof(IsCodeMode)); OnPropertyChanged(nameof(WorkingDirectoryLabel)); } }
    public string? WorkingDirectory { get => _workingDirectory; set { Set(ref _workingDirectory, value); OnPropertyChanged(nameof(WorkingDirectoryLabel)); } }
    public bool IsCodeMode => SelectedModeIndex == 1;
    public string WorkingDirectoryLabel => WorkingDirectory is null ? "Choose project..." : Path.GetFileName(WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public string Question { get => _question; set { Set(ref _question, value); OnPropertyChanged(nameof(CanSend)); SendCommand.RaiseCanExecuteChanged(); } }
    public Bitmap? Preview { get => _preview; private set => Set(ref _preview, value); }
    public bool HasCapture => _regionPath is not null || _contextPath is not null;
    public bool HasContext => _contextPath is not null;
    public bool HasRegion => _regionPath is not null;
    public string PreviewLabel => _regionPath is not null ? "Selected region" : "Full screen";
    public bool IsIdle => !_busy;
    public bool IsBusy => _busy;
    public bool CanSend => !_busy && !string.IsNullOrWhiteSpace(Question);

    /// <summary>Grabs the whole screen as background context. Called when the panel opens, while
    /// the window is still the small collapsed dock, so there's no need to hide it first.</summary>
    public async Task RefreshContextAsync()
    {
        if (_busy || _refreshingContext) return;
        _refreshingContext = true;
        try
        {
            var path = await _captureService.CaptureScreenAsync(_owner, _lifetime.Token);
            ReplaceCapture(ref _contextPath, path);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Messages.Add(new ChatMessage("assistant", $"Screen context capture failed: {ex.Message}") { IsError = true }); }
        finally { _refreshingContext = false; }
    }

    private async Task CaptureAsync()
    {
        SetBusy(true);
        try
        {
            _owner.Hide();
            await Task.Delay(150, _lifetime.Token);
            var path = await _captureService.CaptureRegionAsync(_owner, _lifetime.Token);
            if (path is null) return;
            ReplaceCapture(ref _regionPath, path);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Messages.Add(new ChatMessage("assistant", $"Capture failed: {ex.Message}") { IsError = true }); }
        finally { _owner.Show(); _owner.Activate(); SetBusy(false); }
    }

    private async Task SendAsync()
    {
        var prompt = Question.Trim();
        Question = string.Empty;
        var kind = SelectedModeIndex switch { 1 => TaskKind.Code, 2 => TaskKind.Shell, _ => TaskKind.Chat };
        if (kind == TaskKind.Code)
        {
            WorkingDirectory ??= WorkingDirectoryRequested is null ? null : await WorkingDirectoryRequested();
            if (WorkingDirectory is null) { Question = prompt; return; }
            await RunCodeTurnAsync(prompt, WorkingDirectory);
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

    private async Task RunTurnAsync(string prompt)
    {
        var provider = (AiProvider)SelectedProviderIndex;
        var providerName = Providers[SelectedProviderIndex];
        Messages.Add(new ChatMessage("user", prompt));
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
            var session = new CodeChatSession(answer, _clientFactory, provider, new GitService(), new ShellService(), repository, _regionPath, _contextPath);
            answer.CodeSession = session;
            await session.RunAsync(prompt, cts.Token);
            answer.Caption = $"* {providerName} - {stopwatch.Elapsed.TotalSeconds:0.0} s";
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
        try { await session.ContinueAsync(reply, cts.Token); }
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
        var (text, question) = ClarifyingQuestionParser.Extract(TextSanitizer.Clean(rawText));
        answer.Text = TextSanitizer.Clean(text);
        if (question is not null)
        {
            answer.Question = TextSanitizer.Clean(question);
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
        OnPropertyChanged(nameof(PreviewLabel)); OnPropertyChanged(nameof(CanSend));
        SendCommand.RaiseCanExecuteChanged();
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanSend));
        CaptureCommand.RaiseCanExecuteChanged(); SendCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        _lifetime.Cancel();
        ReplaceCapture(ref _regionPath, null);
        ReplaceCapture(ref _contextPath, null);
        _lifetime.Dispose();
    }
}
