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

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable
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
    private bool _planModeEnabled;
    private bool _planProgressTracking;
    private bool _planWindowCollapsed;
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
        TogglePlanModeCommand = new RelayCommand(TogglePlanMode);
        TogglePlanWindowCommand = new RelayCommand(TogglePlanWindow, () => PlanChipVisible);
        ImplementPlanCommand = new AsyncCommand(ImplementApprovedPlanAsync,
            () => IsIdle && PlanModeEnabled && Plan.CanImplement && SelectedDiagnostic?.IsAvailable != false);
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
    public RelayCommand TogglePlanModeCommand { get; }
    /// <summary>Collapses/expands the plan window (does not toggle plan mode).</summary>
    public RelayCommand TogglePlanWindowCommand { get; }
    public AsyncCommand ImplementPlanCommand { get; }
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
    /// <summary>Live plan document for Plan Mode (separate window + Implement handoff).</summary>
    public PlanDocument Plan { get; } = new();
    public bool PlanModeEnabled
    {
        get => _planModeEnabled;
        private set
        {
            if (_planModeEnabled == value) return;
            Set(ref _planModeEnabled, value);
            PlanMode.Active = value;
            OnPropertyChanged(nameof(PlanModeMenuLabel));
            OnPropertyChanged(nameof(PlanModeChipLabel));
            OnPropertyChanged(nameof(PlanChipVisible));
            OnPropertyChanged(nameof(ComposePlaceholder));
            ImplementPlanCommand.RaiseCanExecuteChanged();
            TogglePlanWindowCommand.RaiseCanExecuteChanged();
            NotifySurfaceStateChanged();
            if (value) IsPlanWindowCollapsed = false; // entering plan shows expanded checklist
            PlanModeChanged?.Invoke(value);
        }
    }
    public string PlanModeMenuLabel => PlanModeEnabled ? "Plan mode: ON" : "Plan mode: OFF";
    public string PlanModeChipLabel => "Plan";
    /// <summary>Tracks last known collapsed state of the side plan window for chevron affordance on chip.</summary>
    public bool IsPlanWindowCollapsed
    {
        get => _planWindowCollapsed;
        internal set
        {
            if (_planWindowCollapsed == value) return;
            Set(ref _planWindowCollapsed, value);
        }
    }
    /// <summary>Plan chip stays while mode is on or implement is tracking progress (collapse/expand only).</summary>
    public bool PlanChipVisible => PlanDockExperience.ChipVisible(PlanModeEnabled, PlanProgressTracking);
    /// <summary>True while Implement is running — plan window stays open and accepts step check-offs.</summary>
    public bool PlanProgressTracking
    {
        get => _planProgressTracking;
        private set
        {
            if (_planProgressTracking == value) return;
            _planProgressTracking = value;
            PlanMode.TrackingProgress = value;
            OnPropertyChanged(nameof(PlanProgressTracking));
            OnPropertyChanged(nameof(PlanChipVisible));
            TogglePlanWindowCommand.RaiseCanExecuteChanged();
            PlanProgressTrackingChanged?.Invoke(value);
        }
    }
    /// <summary>Raised when plan mode is toggled so the UI can show/hide the plan window.</summary>
    public Action<bool>? PlanModeChanged { get; set; }
    /// <summary>Raised when implement progress tracking starts/stops (keep plan window + dock tint).</summary>
    public Action<bool>? PlanProgressTrackingChanged { get; set; }
    /// <summary>Raised when the plan chip asks to collapse/expand the plan window (not mode).</summary>
    public Action? PlanWindowToggleRequested { get; set; }
    /// <summary>Raised when the plan document is updated from a PLAN: directive.</summary>
    public Action? PlanUpdated { get; set; }
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
    public string ComposePlaceholder => PlanModeEnabled
        ? "Describe what you want to plan…  Enter to send"
        : HasWorkingDirectory
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
        : PlanModeEnabled ? "Plan"
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
        : PlanModeEnabled ? "Chat clarifies · plan window updates."
        : ChaosModeEnabled ? "Roast · debate · ELI5/staff tone · focus lock"
        : LeanChatEnabled ? "Short prompts · tighter history · fewer tokens"
        : HasRegion ? "Selected area stays in focus until you clear it"
        : HasContext ? "Screen context loaded"
        : "No screen context yet";
    public string LandingTitle => PlanModeEnabled
        ? "Draft the plan."
        : ChaosModeEnabled
        ? "Chaos Mode is on."
        : HasWorkingDirectory ? "What should change?"
        : HasCapture ? "Ask about your screen." : "Start with the screen.";
    public string LandingSubtitle => PlanModeEnabled
        ? "Clarify in chat · approve → Implement."
        : ChaosModeEnabled
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

    /// <summary>Runs a clicked suggestion chip as a plain chat turn regardless of the selected
    /// mode - suggestions describe what is on screen, never code or shell work.</summary>
    // Chat turn flow moved to MainWindowViewModel.Chat.cs.
    // GenerateScreenDigestAsync
    // BuildTurnContext
    // ContextAttachments
    // await RunTurnAsync(prompt, attachCaptures: false)
    // RunTurnAsync(answer, attachCaptures: false)
    // RunTurnAsync(prompt, displayPrompt: suggestion, attachCaptures: true)
    // ChatCaptureAttachment.ForFirstRequest
    // ChatStreamUiBridge
    // streamBridge.OnPartial
    // ChatStreamTextPolicy.ApplyPartial
    // ApplyFinalAnswerText
    // ChatStreamTextPolicy.ApplyFinal
    // _ = GenerateFollowUpSuggestionsAsync()
    // TryExtractScreenRereadReason
    // Messages.Take(Messages.Count - 2)
    // new AiRequest(prompt, region, context, history)
    // WorkingDirectory = WorkingDirectory
    // WorkspaceWriteAuditor
    // !sentVisual && ClarifyingQuestionParser.TryExtractScreenRereadReason
    // ImprovePromptAsync
    // TaskKind.ImprovePrompt
    // ArgueWithYourselfAsync
    // TogglePomodoro
    // IsFocusLocked
    // GhostCursorWindow.PointAt
    // RunSplitBrainTurnAsync
    // never turns plan mode off
    // OutcomeMemory
    // ShowWhereParser
    // BeginLivePair
    // LivePairMap
    // PlanWindowToggleRequested

    private void ShowWhereOnScreen(object? parameter)
    {
        if (parameter is not ChatMessage { ShowWhere: { } target }) return;
        GhostCursorWindow.PointAt(_owner, target.X, target.Y, target.Width, target.Height, target.Label);
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
        ImplementPlanCommand.RaiseCanExecuteChanged();
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
        PlanMode.Active = false;
        PlanMode.TrackingProgress = false;
        DisposeMessages();
        ReplaceCapture(ref _regionPath, null);
        ReplaceCapture(ref _contextPath, null);
        _lifetime.Dispose();
    }
}





