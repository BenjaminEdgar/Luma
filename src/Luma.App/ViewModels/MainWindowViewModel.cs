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
    private bool _improvingPrompt;
    private CancellationTokenSource? _suggestCts;
    private CancellationTokenSource? _improveCts;
    private DateTime _suggestionsAt = DateTime.MinValue;
    private int _selectedProviderIndex;
    private int _selectedEffortIndex = 1;
    private string? _workingDirectory;
    private CancellationTokenSource? _requestCts;
    private ProviderDiagnostic? _claudeDiagnostic;
    private ProviderDiagnostic? _codexDiagnostic;
    private ProviderDiagnostic? _grokDiagnostic;
    private bool _diagnosticsReady;
    private bool _globalExplainShortcutAvailable;
    private string _runningStatus = string.Empty;
    private string _activityStatus = "Working";
    private string _activityDetail = "Waiting for the provider";
    private string? _clipboardSnippet;
    private readonly List<string> _attachedFilePaths = [];
    private DateTimeOffset? _focusUntilUtc;
    private readonly DispatcherTimer _chaosTicker;
    private readonly DispatcherTimer _livePairTicker;
    private bool _splitBrainEnabled;
    private WorkspaceSnapshot? _livePairSnapshot;
    private string? _livePairRoot;
    private ChatMessage? _livePairAnswer;
    private bool _livePairActive;
    private LivePairFile? _activeLivePairFile;
    private bool _livePairUserPicked;

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
        _chaosTicker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => TickChaosFocus());
        _livePairTicker = new DispatcherTimer(TimeSpan.FromMilliseconds(1100), DispatcherPriority.Background,
            (_, _) => PollLivePair());
        CaptureCommand = new AsyncCommand(CaptureAsync, () => IsIdle);
        ExplainSelectionCommand = new AsyncCommand(ExplainSelectionAsync, () => IsIdle && !IsFocusLocked && SelectedDiagnostic?.IsAvailable != false);
        ExplainScreenCommand = new AsyncCommand(ExplainScreenAsync, () => IsIdle && !IsFocusLocked && SelectedDiagnostic?.IsAvailable != false);
        SendCommand = new AsyncCommand(SendAsync, () => CanSend);
        ClearCaptureCommand = new RelayCommand(ClearCapture);
        NewChatCommand = new RelayCommand(StartNewChat, () => CanStartNewChat);
        StopCommand = new RelayCommand(_operations.CancelAll, () => HasRunningOperations);
        CopyMessageCommand = new ParameterCommand(CopyMessage);
        AnswerQuestionCommand = new AsyncParameterCommand(AnswerQuestionAsync);
        SkipQuestionCommand = new AsyncParameterCommand(SkipQuestionAsync);
        UseSuggestionCommand = new AsyncParameterCommand(UseSuggestionAsync);
        UseCodeActionCommand = new AsyncParameterCommand(UseCodeActionAsync);
        UseClipboardCommand = new AsyncCommand(UseClipboardAsync, () => IsIdle);
        ClearClipboardCommand = new RelayCommand(ClearClipboardSnippet, () => HasClipboardSnippet);
        ClearAttachedFilesCommand = new RelayCommand(ClearAttachedFiles, () => HasAttachedFiles);
        UndoFileChangeCommand = new ParameterCommand(UndoFileChange);
        ToggleChaosModeCommand = new RelayCommand(ToggleChaosMode);
        ToggleLeanChatCommand = new RelayCommand(ToggleLeanChat);
        CycleChaosToneCommand = new RelayCommand(CycleChaosTone, () => ChaosModeEnabled);
        RoastUiCommand = new AsyncCommand(RoastUiAsync, () => IsIdle && ChaosModeEnabled && SelectedDiagnostic?.IsAvailable != false);
        ArgueWithYourselfCommand = new AsyncCommand(ArgueWithYourselfAsync, () => IsIdle && ChaosModeEnabled && SelectedDiagnostic?.IsAvailable != false);
        TogglePomodoroCommand = new RelayCommand(TogglePomodoro, () => ChaosModeEnabled);
        ToggleSplitBrainCommand = new RelayCommand(ToggleSplitBrain);
        ShowWhereCommand = new ParameterCommand(ShowWhereOnScreen);
        ChooseSplitBrainCommand = new ParameterCommand(ChooseSplitBrainSide);
        JumpLivePairCommand = new ParameterCommand(JumpLivePairFile);
        DismissLivePairCommand = new RelayCommand(DismissLivePair);
        SelectProviderCommand = new ParameterCommand(SelectProvider);
        SelectEffortCommand = new ParameterCommand(SelectEffort);
        ImprovePromptCommand = new AsyncCommand(ImprovePromptAsync,
            () => IsIdle && !IsImprovingPrompt && !string.IsNullOrWhiteSpace(Question) && SelectedDiagnostic?.IsAvailable != false);
        _selectedEffortIndex = AppSettings.EffortToIndex(AppSettings.Current.ChatReasoningEffort);
        _operations.Changed += OnOperationsChanged;
        Messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanStartNewChat));
            OnPropertyChanged(nameof(ShowScreenLandingActions));
            OnPropertyChanged(nameof(ShowRepoLandingActions));
            NewChatCommand.RaiseCanExecuteChanged();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public IReadOnlyList<string> Providers { get; } = ["Claude", "Codex", "Grok"];
    public IReadOnlyList<string> EffortLevels { get; } = ["Low", "Medium", "High"];
    public AsyncCommand CaptureCommand { get; }
    public AsyncCommand ExplainSelectionCommand { get; }
    public AsyncCommand ExplainScreenCommand { get; }
    public AsyncCommand SendCommand { get; }
    public RelayCommand ClearCaptureCommand { get; }
    public RelayCommand NewChatCommand { get; }
    public RelayCommand StopCommand { get; }
    public ParameterCommand CopyMessageCommand { get; }
    public AsyncParameterCommand AnswerQuestionCommand { get; }
    public AsyncParameterCommand SkipQuestionCommand { get; }
    public AsyncParameterCommand UseSuggestionCommand { get; }
    public AsyncParameterCommand UseCodeActionCommand { get; }
    public AsyncCommand UseClipboardCommand { get; }
    public RelayCommand ClearClipboardCommand { get; }
    public RelayCommand ClearAttachedFilesCommand { get; }
    public ParameterCommand UndoFileChangeCommand { get; }
    public RelayCommand ToggleChaosModeCommand { get; }
    public RelayCommand ToggleLeanChatCommand { get; }
    public RelayCommand CycleChaosToneCommand { get; }
    public AsyncCommand RoastUiCommand { get; }
    public AsyncCommand ArgueWithYourselfCommand { get; }
    public RelayCommand TogglePomodoroCommand { get; }
    public RelayCommand ToggleSplitBrainCommand { get; }
    public ParameterCommand ShowWhereCommand { get; }
    public ParameterCommand ChooseSplitBrainCommand { get; }
    public ParameterCommand JumpLivePairCommand { get; }
    public RelayCommand DismissLivePairCommand { get; }
    public ParameterCommand SelectProviderCommand { get; }
    public ParameterCommand SelectEffortCommand { get; }
    public AsyncCommand ImprovePromptCommand { get; }
    /// <summary>Opens a file picker (wired from MainWindow) and returns absolute paths.</summary>
    public Func<Task<IReadOnlyList<string>>>? AttachFilesRequested { get; set; }
    /// <summary>Raised when the user picks a file in the live pair map so the UI can scroll to the audit row.</summary>
    public Action<ChatMessage, string>? LivePairJumpRequested { get; set; }
    /// <summary>Files the agent has touched so far this turn (live diff panel).</summary>
    public ObservableCollection<LivePairFile> LivePairFiles { get; } = [];
    public bool HasLivePair => LivePairFiles.Count > 0;
    public bool IsLivePairWatching => _livePairActive;
    /// <summary>Show the strip while watching (even empty) or after files land.</summary>
    public bool ShowLivePair => _livePairActive || LivePairFiles.Count > 0;
    public string LivePairTitle => _livePairActive
        ? (LivePairFiles.Count == 0 ? "LIVE DIFF · watching writes…" : $"LIVE DIFF · {LivePairFiles.Count} file{(LivePairFiles.Count == 1 ? "" : "s")}")
        : (LivePairFiles.Count == 0 ? "LIVE DIFF" : $"LIVE DIFF · {LivePairFiles.Count} file{(LivePairFiles.Count == 1 ? "" : "s")}");
    public string LivePairSubtitle => _livePairActive
        ? (ActiveLivePairFile is { } active
            ? $"Streaming · {active.RelativePath}"
            : "Live line output while the agent writes")
        : (ActiveLivePairFile is { } done
            ? done.RelativePath
            : "Click a file to preview · jump to audit when ready");

    /// <summary>File currently shown in the live unified-diff preview.</summary>
    public LivePairFile? ActiveLivePairFile
    {
        get => _activeLivePairFile;
        private set
        {
            if (ReferenceEquals(_activeLivePairFile, value)) return;
            if (_activeLivePairFile is not null) _activeLivePairFile.IsActive = false;
            _activeLivePairFile = value;
            if (_activeLivePairFile is not null) _activeLivePairFile.IsActive = true;
            OnPropertyChanged(nameof(ActiveLivePairFile));
            OnPropertyChanged(nameof(HasLivePairPreview));
            OnPropertyChanged(nameof(LivePairPreviewLines));
            OnPropertyChanged(nameof(LivePairSubtitle));
        }
    }

    public bool HasLivePairPreview => ActiveLivePairFile is { HasPreview: true };
    public ObservableCollection<LivePairPreviewLine>? LivePairPreviewLines => ActiveLivePairFile?.PreviewLines;
    /// <summary>Short prompt ideas derived from the ambient screen capture, shown as chips.</summary>
    public ObservableCollection<string> Suggestions { get; } = [];
    /// <summary>Display names of files attached for the next send (@file or picker).</summary>
    public ObservableCollection<string> AttachedFileLabels { get; } = [];
    public bool IsSuggesting
    {
        get => _suggesting;
        private set
        {
            Set(ref _suggesting, value);
            NotifySurfaceStateChanged();
        }
    }
    public bool IsImprovingPrompt
    {
        get => _improvingPrompt;
        private set
        {
            Set(ref _improvingPrompt, value);
            NotifySurfaceStateChanged();
            ImprovePromptCommand.RaiseCanExecuteChanged();
        }
    }
    public string? ClipboardSnippet
    {
        get => _clipboardSnippet;
        private set
        {
            Set(ref _clipboardSnippet, value);
            OnPropertyChanged(nameof(HasClipboardSnippet));
            OnPropertyChanged(nameof(ClipboardSnippetPreview));
            ClearClipboardCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HasClipboardSnippet => !string.IsNullOrWhiteSpace(ClipboardSnippet);
    public string ClipboardSnippetPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ClipboardSnippet)) return string.Empty;
            var one = ClipboardSnippet.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return one.Length <= 64 ? one : one[..64].TrimEnd() + "…";
        }
    }
    public bool HasAttachedFiles => _attachedFilePaths.Count > 0;
    public bool ChaosModeEnabled => AppSettings.Current.ChaosMode;
    public ChaosTone ChaosTone => (ChaosTone)AppSettings.Current.ChaosTone;
    public string ChaosToneChipLabel => ChaosMode.ToneChipLabel(ChaosTone);
    public string ChaosModeMenuLabel => ChaosModeEnabled ? "Chaos Mode: ON" : "Chaos Mode: OFF";
    public bool LeanChatEnabled => AppSettings.Current.LeanChatMode;
    public string LeanChatMenuLabel => LeanChatEnabled ? "Lean chat: ON" : "Lean chat: OFF";
    public string LeanChatChipLabel => LeanChatEnabled ? "Lean ON" : "Lean";
    public bool IsFocusLocked => ChaosModeEnabled && _focusUntilUtc is { } until && until > DateTimeOffset.UtcNow;
    public string PomodoroLabel
    {
        get
        {
            if (!IsFocusLocked || _focusUntilUtc is null) return "Start focus lock";
            var left = _focusUntilUtc.Value - DateTimeOffset.UtcNow;
            return $"🍅 {ChaosMode.FormatRemaining(left)}";
        }
    }
    public string PomodoroMenuLabel => IsFocusLocked ? "Cancel focus lock" : $"Start {AppSettings.Current.ChaosPomodoroMinutes}m focus lock";
    public bool SplitBrainEnabled
    {
        get => _splitBrainEnabled;
        private set
        {
            if (_splitBrainEnabled == value) return;
            Set(ref _splitBrainEnabled, value);
            OnPropertyChanged(nameof(SplitBrainMenuLabel));
            OnPropertyChanged(nameof(SplitBrainChipLabel));
        }
    }
    public string SplitBrainMenuLabel => SplitBrainEnabled ? "Split-brain: ON" : "Split-brain: OFF";
    public string SplitBrainChipLabel => SplitBrainEnabled ? "Split-brain ON" : "Split-brain";
    public Func<TaskLaunchRequest, Task<bool>>? TaskLaunchRequested { get; set; }
    public Func<Task<string?>>? WorkingDirectoryRequested { get; set; }
    public Func<Task<bool>>? NewChatConfirmationRequested { get; set; }
    public Action? ScreenExplanationReadyToShow { get; set; }

    public int SelectedProviderIndex
    {
        get => _selectedProviderIndex;
        set
        {
            if (_selectedProviderIndex == value) return;
            Set(ref _selectedProviderIndex, value);
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(ProviderStatus));
            OnPropertyChanged(nameof(HasProviderProblem));
            NotifyModelPickerChanged();
            SendCommand.RaiseCanExecuteChanged();
            ImprovePromptCommand.RaiseCanExecuteChanged();
            ExplainSelectionCommand.RaiseCanExecuteChanged();
            ExplainScreenCommand.RaiseCanExecuteChanged();
            RoastUiCommand.RaiseCanExecuteChanged();
            ArgueWithYourselfCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>0 = low, 1 = medium, 2 = high — used by Codex chat/code turns.</summary>
    public int SelectedEffortIndex
    {
        get => _selectedEffortIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, EffortLevels.Count - 1);
            if (_selectedEffortIndex == clamped) return;
            Set(ref _selectedEffortIndex, clamped);
            AppSettings.Current.ChatReasoningEffort = AppSettings.EffortFromIndex(clamped);
            AppSettings.Current.Save();
            NotifyModelPickerChanged();
        }
    }

    /// <summary>Compact compose-bar label, e.g. "Claude · Med".</summary>
    public string ModelPickerLabel
    {
        get
        {
            var provider = Providers[Math.Clamp(SelectedProviderIndex, 0, Providers.Count - 1)];
            var effort = SelectedEffortIndex switch
            {
                0 => "Low",
                2 => "High",
                _ => "Med"
            };
            return $"{provider} · {effort}";
        }
    }

    public string ProviderMenuClaude => MenuCheck(SelectedProviderIndex == 0, "Claude");
    public string ProviderMenuCodex => MenuCheck(SelectedProviderIndex == 1, "Codex");
    public string ProviderMenuGrok => MenuCheck(SelectedProviderIndex == 2, "Grok");
    public string EffortMenuLow => MenuCheck(SelectedEffortIndex == 0, "Low");
    public string EffortMenuMedium => MenuCheck(SelectedEffortIndex == 1, "Medium");
    public string EffortMenuHigh => MenuCheck(SelectedEffortIndex == 2, "High");
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set
        {
            Set(ref _workingDirectory, value);
            OnPropertyChanged(nameof(WorkingDirectoryLabel));
            OnPropertyChanged(nameof(HasWorkingDirectory));
            OnPropertyChanged(nameof(WorkingDirectoryTip));
            OnPropertyChanged(nameof(ComposePlaceholder));
            OnPropertyChanged(nameof(ShowScreenLandingActions));
            OnPropertyChanged(nameof(ShowRepoLandingActions));
            OnPropertyChanged(nameof(ShowGlobalExplainHint));
            NotifySurfaceStateChanged();
        }
    }
    public bool HasWorkingDirectory => !string.IsNullOrWhiteSpace(WorkingDirectory);
    /// <summary>Folder name for the compose chip; full path is in <see cref="WorkingDirectoryTip"/>.</summary>
    public string WorkingDirectoryLabel => WorkingDirectory is null
        ? "No project folder"
        : Path.GetFileName(WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public string WorkingDirectoryTip => WorkingDirectory is null
        ? "Set a project folder so Luma can read local files"
        : WorkingDirectory;
    public string ComposePlaceholder => HasWorkingDirectory
        ? "Describe a code change…  @file to pin  ·  Enter to send"
        : "Ask anything…  @file to pin  ·  Enter to send";
    public bool ShowScreenLandingActions => Messages.Count == 0 && !HasWorkingDirectory;
    public bool ShowRepoLandingActions => Messages.Count == 0 && HasWorkingDirectory;
    /// <summary>Compact busy strip when there is detail beyond the header status pill.</summary>
    public bool ShowBusyDetail => IsBusy && !string.IsNullOrWhiteSpace(SurfaceDetail);
    /// <summary>Shortcut tip only on screen landing — hide in repo mode to cut empty-state noise.</summary>
    public bool ShowGlobalExplainHint => GlobalExplainShortcutAvailable && ShowScreenLandingActions;
    public string Question
    {
        get => _question;
        set
        {
            Set(ref _question, value);
            OnPropertyChanged(nameof(CanSend));
            SendCommand.RaiseCanExecuteChanged();
            ImprovePromptCommand.RaiseCanExecuteChanged();
        }
    }
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
    public string SurfaceStatus =>
        IsFocusLocked ? PomodoroLabel
        : _refreshingContext ? "Capturing"
        : _busy ? _activityStatus
        : IsImprovingPrompt ? "Improving prompt"
        : IsSuggesting ? "Preparing shortcuts"
        : ChaosModeEnabled ? "Chaos"
        : LeanChatEnabled ? "Lean"
        : HasRegion ? "Region ready"
        : HasContext ? "Screen ready"
        : "Ready";
    public string SurfaceDetail =>
        IsFocusLocked ? ChaosMode.PomodoroBlockedMessage(_focusUntilUtc!.Value - DateTimeOffset.UtcNow)
        : _refreshingContext ? "Refreshing screen context"
        : _busy ? (HasRunningOperations ? RunningStatus : _activityDetail)
        : IsImprovingPrompt ? "Rewriting your draft — not sent yet"
        : IsSuggesting ? "Generating quick actions"
        : ChaosModeEnabled ? "Roast · debate · ELI5/staff tone · focus lock"
        : LeanChatEnabled ? "Short prompts · tighter history · fewer tokens"
        : HasRegion ? "Selected area stays in focus until you clear it"
        : HasContext ? "Screen context loaded"
        : "No screen context yet";
    public string LandingTitle => ChaosModeEnabled
        ? "Chaos Mode is on."
        : HasWorkingDirectory ? "What should change?"
        : HasCapture ? "Ask about your screen." : "Start with the screen.";
    public string LandingSubtitle => ChaosModeEnabled
        ? "Roast, debate, flip tone, or focus-lock Explain."
        : HasWorkingDirectory
            ? "Type a change, or pick a chip below."
        : HasCapture
            ? "Use a chip, type a question, or explain a region."
            : "Explain the screen, pick a region, or just ask.";
    public bool IsIdle => !_busy;
    public bool IsBusy => _busy;
    public bool CanStartNewChat => !_busy && Messages.Count > 0;
    public bool CanSend => !_busy && !string.IsNullOrWhiteSpace(Question) && SelectedDiagnostic?.IsAvailable != false;
    public bool HasProviderProblem => SelectedDiagnostic?.IsAvailable == false;
    public string ProviderStatus => SelectedDiagnostic?.Message ?? string.Empty;
    public bool HasRunningOperations => _operations.Active.Count > 0;
    public bool GlobalExplainShortcutAvailable
    {
        get => _globalExplainShortcutAvailable;
        set
        {
            Set(ref _globalExplainShortcutAvailable, value);
            OnPropertyChanged(nameof(ShowGlobalExplainHint));
        }
    }
    public string RunningStatus { get => _runningStatus; private set => Set(ref _runningStatus, value); }
    public string ActivityStatus { get => _activityStatus; private set => Set(ref _activityStatus, value); }
    public string ActivityDetail { get => _activityDetail; private set => Set(ref _activityDetail, value); }
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
            var hadMessages = Messages.Count > 0;
            var bigChange = hadMessages && difference >= .16;
            if (bigChange && NewChatConfirmationRequested is not null && await NewChatConfirmationRequested())
            {
                DisposeMessages();
                Suggestions.Clear();
                hadMessages = false;
            }
            ReplaceCapture(ref _contextPath, path);
            if (bigChange && hadMessages)
                _ = GenerateScreenDigestAsync();
            else
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

    /// <summary>When the screen changes mid-conversation, summarize what's important + offer action chips.</summary>
    private async Task GenerateScreenDigestAsync()
    {
        if (_contextPath is null || !_diagnosticsReady) return;
        var digest = new ChatMessage("assistant", "Scanning what’s on screen…")
        {
            Caption = "✦ Screen changed",
            IsPending = true,
        };
        Messages.Add(digest);
        string? thumbnailPath = null;
        try
        {
            thumbnailPath = await Task.Run(() => CreateSuggestionThumbnail(_contextPath), _lifetime.Token);
            var request = new AiRequest(
                "The user's screen just changed. Reply with:\n" +
                "1) Up to 3 short bullets of what looks important right now (each line starts with - ).\n" +
                "2) A blank line, then up to 3 verb-led next actions (under 8 words each, one per line).\n" +
                "No preamble.",
                null, thumbnailPath ?? _contextPath, [])
            { TaskKind = TaskKind.Suggest };
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(request, null, _lifetime.Token);
            var (summary, actions) = ScreenDigestParser.Parse(text);
            digest.Text = summary;
            digest.SetActionChips(actions.Concat(["Start new chat"]));
            digest.Caption = "✦ Screen changed";
        }
        catch (Exception ex)
        {
            digest.Text = "Your screen looks different than last time.";
            digest.SetActionChips(["Explain this screen", "Start new chat"]);
            if (!string.IsNullOrWhiteSpace(ex.Message))
                digest.Text += $"\n\n*(Digest unavailable: {ex.Message})*";
        }
        finally
        {
            digest.IsPending = false;
            if (thumbnailPath is not null) { try { File.Delete(thumbnailPath); } catch { } }
        }
    }

    /// <summary>Asks the provider for a few short prompt ideas based on the ambient capture and
    /// shows them as chips. Seeds appear instantly; AI refines them. Failures stay silent.</summary>
    private async Task GenerateSuggestionsAsync(double screenDifference = 1d)
    {
        if (_contextPath is null || Messages.Count > 0 || _busy) return;
        if (!_diagnosticsReady) return;
        if (!AppSettings.Current.SuggestFromScreen) return;
        // Fresh window: reuse recent AI chips (not mere seeds) to keep reopening snappy.
        if (Suggestions.Count > 0 && !SuggestionParser.IsOnlySeeds(Suggestions) &&
            DateTime.UtcNow - _suggestionsAt < TimeSpan.FromSeconds(AppSettings.Current.SuggestionFreshSeconds)) return;
        // If the screen looks the same as when the current chips were made, they're still
        // accurate - skip the provider call (and its screenshot tokens) entirely.
        if (Suggestions.Count > 0 && !SuggestionParser.IsOnlySeeds(Suggestions) &&
            AppSettings.Current.SkipSuggestionsWhenScreenUnchanged &&
            screenDifference < .05) return;

        _suggestCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _suggestCts = cts;
        // Instant seeds so the empty landing never waits on the model.
        if (Suggestions.Count == 0 || SuggestionParser.IsOnlySeeds(Suggestions))
            ApplySuggestionChips(SuggestionPrompts.InstantSeeds, markFresh: false);
        IsSuggesting = true;
        string? thumbnailPath = null;
        try
        {
            var contextPath = _contextPath;
            thumbnailPath = await Task.Run(() => CreateSuggestionThumbnail(contextPath), cts.Token);
            var count = AppSettings.Current.SuggestionCount;
            var request = new AiRequest(SuggestionPrompts.FromScreen(count), null, thumbnailPath ?? contextPath, [])
            { TaskKind = TaskKind.Suggest };
            // Progressive: as stream lines arrive, promote the first usable chips early.
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(
                request,
                partial =>
                {
                    if (cts.IsCancellationRequested || Messages.Count > 0 || _busy) return;
                    var early = SuggestionParser.Parse(partial, count);
                    if (early.Count > 0)
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (cts.IsCancellationRequested || Messages.Count > 0 || _busy) return;
                            ApplySuggestionChips(early, markFresh: false);
                        });
                },
                cts.Token);
            if (cts.IsCancellationRequested || Messages.Count > 0 || _busy) return;
            var parsed = SuggestionParser.Parse(text, count);
            if (parsed.Count == 0) return; // keep seeds / early chips rather than blanking
            ApplySuggestionChips(parsed, markFresh: true);
        }
        catch { /* keep seeds */ }
        finally
        {
            if (thumbnailPath is not null) { try { File.Delete(thumbnailPath); } catch { } }
            if (_suggestCts == cts) { _suggestCts = null; IsSuggesting = false; }
            cts.Dispose();
        }
    }

    /// <summary>Rewrites the compose draft in place (does not send). Uses the cheap suggestion path.</summary>
    private async Task ImprovePromptAsync()
    {
        var draft = Question.Trim();
        if (_busy || string.IsNullOrWhiteSpace(draft) || SelectedDiagnostic?.IsAvailable == false) return;

        _improveCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _improveCts = cts;
        IsImprovingPrompt = true;
        try
        {
            // Light history so rewrites stay grounded in the current chat when present.
            var history = Messages.Count == 0
                ? Array.Empty<ChatMessage>()
                : Messages.Skip(Math.Max(0, Messages.Count - 6)).ToArray();
            var request = new AiRequest(PromptImprove.BuildRequest(draft), null, null, history)
            {
                TaskKind = TaskKind.ImprovePrompt,
            };
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex)
                .AskAsync(request, null, cts.Token);
            if (cts.IsCancellationRequested) return;
            var improved = PromptImprove.Parse(text);
            // Only replace if the user has not edited/sent/cleared the draft while we worked.
            if (!string.IsNullOrWhiteSpace(improved) &&
                string.Equals(Question.Trim(), draft, StringComparison.Ordinal))
                Question = improved;
        }
        catch (OperationCanceledException) { }
        catch { /* leave draft unchanged */ }
        finally
        {
            if (_improveCts == cts) { _improveCts = null; IsImprovingPrompt = false; }
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
            var count = AppSettings.Current.SuggestionCount;
            var request = new AiRequest(SuggestionPrompts.FollowUp(count), null, null, history)
            { TaskKind = TaskKind.FollowUp };
            var text = await _clientFactory.Create((AiProvider)SelectedProviderIndex).AskAsync(
                request,
                partial =>
                {
                    if (cts.IsCancellationRequested) return;
                    var early = SuggestionParser.Parse(partial, count);
                    if (early.Count > 0)
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (cts.IsCancellationRequested) return;
                            ApplySuggestionChips(early, markFresh: false);
                        });
                },
                cts.Token);
            if (cts.IsCancellationRequested) return;
            var parsed = SuggestionParser.Parse(text, count);
            if (parsed.Count == 0) return;
            ApplySuggestionChips(parsed, markFresh: true);
        }
        catch (OperationCanceledException) { }
        catch { /* leave whatever chips we had */ }
        finally
        {
            if (_suggestCts == cts) { _suggestCts = null; IsSuggesting = false; }
            cts.Dispose();
        }
    }

    private void ApplySuggestionChips(IReadOnlyList<string> chips, bool markFresh)
    {
        if (chips.Count == 0) return;
        Suggestions.Clear();
        foreach (var chip in chips) Suggestions.Add(chip);
        if (markFresh) _suggestionsAt = DateTime.UtcNow;
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
        if (string.Equals(suggestion, "Start new chat", StringComparison.OrdinalIgnoreCase))
        {
            // Allow force-clear even when CanStartNewChat would normally require messages+idle.
            ForceNewChat();
            return;
        }
        // Outcome chips are phrased as Avoid:/Retry: — strip prefix for the actual prompt.
        var prompt = suggestion;
        if (prompt.StartsWith("Avoid: ", StringComparison.OrdinalIgnoreCase))
            prompt = "Don't do this again; try another approach instead of: " + prompt["Avoid: ".Length..];
        else if (prompt.StartsWith("Retry: ", StringComparison.OrdinalIgnoreCase))
            prompt = "Do this again carefully: " + prompt["Retry: ".Length..];
        // Chips are derived from the ambient capture — attach screen on the first request.
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
        if (_regionPath is not null) ReplaceCapture(ref _regionPath, null);
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
        source.CodeSession is { } session
            ? RunCodeContinuationAsync(source, session, answer)
            : RunTurnAsync(answer, attachCaptures: false);

    /// <param name="attachCaptures">
    /// When true (explain screen/selection, suggestion chips), attach available screenshots on the first request.
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
            Text = sentVisual ? "Reading screen…" : "Thinking…",
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
        using var streamBridge = new ChatStreamUiBridge(answer, providerName);
        try
        {
            var history = Messages.Take(Messages.Count - 2).ToArray();
            var client = _clientFactory.Create(provider);
            var taskContext = BuildTurnContext(prompt);
            // Snapshot project files so we can audit agent writes after the turn.
            var writeSnapshot = WorkspaceWriteAuditor.Capture(WorkingDirectory);
            BeginLivePair(WorkingDirectory, writeSnapshot, answer);
            // Attach the chosen project folder so providers can read files (cwd + prompt root).
            var request = new AiRequest(prompt, region, context, history)
            {
                WorkingDirectory = WorkingDirectory,
                TaskContext = taskContext,
            };
            var text = await client.AskAsync(request, streamBridge.OnPartial, cts.Token);
            streamBridge.SealPartials();
            SetActivity("Streaming", "Receiving the answer");
            // Text-first turn: if the model needs the screen, capture once and retry with it.
            if (!sentVisual && ClarifyingQuestionParser.TryExtractScreenRereadReason(text, out var reason))
            {
                answer.Caption = $"* {providerName} is reading the screen";
                answer.TurnMeta = BuildTurnMeta(providerName, "Chat", hasVisual: true, WorkingDirectory);
                SetActivity("Reading screen", string.IsNullOrWhiteSpace(reason) ? "The provider requested visual context" : reason);
                answer.Text = string.IsNullOrWhiteSpace(reason) ? "Reading screen…" : $"Reading screen: {reason}";
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

                streamBridge.Reopen();
                writeSnapshot = WorkspaceWriteAuditor.Capture(WorkingDirectory);
                BeginLivePair(WorkingDirectory, writeSnapshot, answer);
                var screenRequest = new AiRequest(prompt, _regionPath, _contextPath, history)
                {
                    WorkingDirectory = WorkingDirectory,
                    TaskContext = taskContext,
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

    /// <summary>Runs a coding request inline: streams the assistant's explanation into a normal
    /// chat bubble like RunTurnAsync, and attaches a CodeChatSession that drives the diff review
    /// card (DiffCardControl) once a patch artifact arrives.</summary>
    public async Task RunCodeTurnAsync(string prompt, string repository)
    {
        var provider = (AiProvider)SelectedProviderIndex;
        var providerName = Providers[SelectedProviderIndex];
        var user = new ChatMessage("user", prompt);
        AttachCaptureToMessage(user, _regionPath, _contextPath);
        Messages.Add(user);
        var answer = new ChatMessage("assistant", string.Empty, isPending: true)
        {
            Caption = $"✦ {providerName} is inspecting the repository",
            Text = "Inspecting repository…",
        };
        answer.TurnMeta = BuildTurnMeta(providerName, "Code", ChatCaptureAttachment.HasVisual(_regionPath, _contextPath), repository);
        Messages.Add(answer);
        SetBusy(true);
        SetActivity("Inspecting repo", "Preparing a coding turn");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _requestCts = cts;
        var stopwatch = Stopwatch.StartNew();
        var ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => answer.Elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} s");
        ticker.Start();
        try
        {
            var writeSnapshot = WorkspaceWriteAuditor.Capture(repository);
            BeginLivePair(repository, writeSnapshot, answer);
            var session = new CodeChatSession(answer, _clientFactory, provider, new GitService(), new ShellService(_operations), repository, _regionPath, _contextPath);
            answer.CodeSession = session;
            await session.RunAsync(prompt, cts.Token);
            SetActivity("Writing files", "Auditing workspace changes");
            answer.Caption = $"✦ {providerName} - {stopwatch.Elapsed.TotalSeconds:0.0} s";
            AttachWriteAudit(answer, writeSnapshot);
            ConsumeEphemeralAttachments();
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
        var (withoutWhere, where) = ShowWhereParser.Extract(rawText);
        var applied = ChatStreamTextPolicy.ApplyFinal(withoutWhere);
        answer.Text = applied.Text;
        answer.ShowWhere = where;
        if (!applied.IsQuestion) return;
        answer.Question = applied.Question;
        answer.QuestionChoices = applied.QuestionChoices;
        answer.IsQuestion = true;
    }

    private void ShowWhereOnScreen(object? parameter)
    {
        if (parameter is not ChatMessage { ShowWhere: { } target }) return;
        GhostCursorWindow.PointAt(_owner, target.X, target.Y, target.Width, target.Height, target.Label);
    }

    private void ToggleSplitBrain() => SplitBrainEnabled = !SplitBrainEnabled;

    private void ChooseSplitBrainSide(object? parameter)
    {
        // Parameter format: message via Tag isn't easy — use SplitBrainChoice record
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
        var answer = new ChatMessage("assistant", "Running split-brain…", isPending: true)
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

    /// <summary>Clears the conversation so the user can start fresh without restarting Luma.</summary>
    private void StartNewChat()
    {
        if (!CanStartNewChat) return;
        ForceNewChat();
    }

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
            // No project folder — clear any stale map from a previous turn.
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
            // Turn still running or audit not attached yet — keep preview, pulse the chip.
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
        // Merge into suggestions without wiping AI chips if present — prefer outcome chips first.
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
        var answer = new ChatMessage("assistant", "Warming up the debate stage…", isPending: true)
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
                var reqA = new AiRequest(ChaosMode.DualDebatePrompt(topic, "Side A — ship the bold option"), _regionPath, _contextPath, [])
                { WorkingDirectory = WorkingDirectory, TaskContext = BuildTurnContext(topic) };
                var reqB = new AiRequest(ChaosMode.DualDebatePrompt(topic, "Side B — ship the careful option"), _regionPath, _contextPath, [])
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

    /// <summary>Shows the region (preferred) or full-screen capture inside a chat bubble.</summary>
    private static void AttachCaptureToMessage(ChatMessage message, string? regionPath, string? contextPath)
    {
        if (regionPath is not null)
            message.AttachImage(regionPath, "Selected area");
        else if (contextPath is not null)
            message.AttachImage(contextPath, "Screen");
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
        OnPropertyChanged(nameof(CanStartNewChat));
        NotifySurfaceStateChanged();
        CaptureCommand.RaiseCanExecuteChanged(); ExplainSelectionCommand.RaiseCanExecuteChanged(); ExplainScreenCommand.RaiseCanExecuteChanged();
        SendCommand.RaiseCanExecuteChanged(); ImprovePromptCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged(); NewChatCommand.RaiseCanExecuteChanged();
        RoastUiCommand.RaiseCanExecuteChanged(); ArgueWithYourselfCommand.RaiseCanExecuteChanged();
    }

    private void SetActivity(string status, string detail)
    {
        ActivityStatus = status;
        ActivityDetail = detail;
        NotifySurfaceStateChanged();
    }

    private static string BuildTurnMeta(string provider, string mode, bool hasVisual, string? workingDirectory)
    {
        var context = new List<string>();
        if (!string.IsNullOrWhiteSpace(workingDirectory)) context.Add("project");
        if (hasVisual) context.Add("screen");
        return $"Provider: {provider} | Mode: {mode} | Context: {(context.Count == 0 ? "text" : string.Join(" + ", context))}";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void NotifySurfaceStateChanged()
    {
        OnPropertyChanged(nameof(SurfaceStatus));
        OnPropertyChanged(nameof(SurfaceDetail));
        OnPropertyChanged(nameof(ShowBusyDetail));
        OnPropertyChanged(nameof(LandingTitle));
        OnPropertyChanged(nameof(LandingSubtitle));
        OnPropertyChanged(nameof(ComposePlaceholder));
        OnPropertyChanged(nameof(ShowScreenLandingActions));
        OnPropertyChanged(nameof(ShowRepoLandingActions));
        OnPropertyChanged(nameof(ShowGlobalExplainHint));
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
        _livePairTicker.Stop();
        _chaosTicker.Stop();
        _lifetime.Cancel();
        DisposeMessages();
        ReplaceCapture(ref _regionPath, null);
        ReplaceCapture(ref _contextPath, null);
        _lifetime.Dispose();
    }
}
