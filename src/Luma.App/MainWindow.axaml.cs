using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Luma.App.Models;
using Luma.App.Services;
using Luma.App.ViewModels;

namespace Luma.App;

public partial class MainWindow : Window
{
    private const double DragThreshold = 5;

    private readonly MainWindowViewModel _viewModel;
    private readonly RunningOperationCoordinator _operations = new();
    private readonly KillTargetWindow _killTarget = new();
    private readonly DispatcherTimer _killTargetTimer;
    private readonly QuestionPromptWindow _questionWindow = new();
    private PlanDocumentWindow? _planWindow;
    private readonly GlobalShortcutService _globalShortcut = new();
    private ChatMessage? _pendingQuestion;
    private bool _expanded;
    private PointerPressedEventArgs? _dockPress;
    private Point _dockPressPoint;
    private int _snapAnimationId;
    private int _shellAnimationId;
    private int _shellPulseId;
    private int _messageCount;
    private string? _lastCodeRepository;

    public MainWindow()
    {
        InitializeComponent();
        var clients = new AiClientFactory(_operations);
        _viewModel = new MainWindowViewModel(this, new ScreenCaptureService(), clients, _operations,
            new ProviderDiagnostics(), new ScreenDifferenceService());
        _killTargetTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input,
            (_, _) => _killTarget.SetHot(_killTarget.Contains(Position, new PixelSize((int)Width, (int)Height))));
        DataContext = _viewModel;
        _viewModel.TaskLaunchRequested = LaunchTaskAsync;
        _viewModel.NewChatConfirmationRequested = () => new ScreenChangeWindow().ShowDialog<bool>(this);
        _viewModel.ScreenExplanationReadyToShow = () => SetExpanded(true);
        _viewModel.OcrUiChanged = ApplyOcrChrome;
        LoadSettings();
        ApplyOcrChrome();
        _viewModel.WorkingDirectoryRequested = ResolveWorkingDirectoryAsync;
        _viewModel.AttachFilesRequested = PickFilesToAttachAsync;
        // Micro-delights: provider accent flash (skip initial settings load via Opened flag timing).
        // Also keep the live-diff preview scrolled to the latest lines as writes land.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedProviderIndex) && _expanded)
                _ = PulseShellFlashAsync();
            if (e.PropertyName is nameof(MainWindowViewModel.LivePairPreviewLines)
                or nameof(MainWindowViewModel.ActiveLivePairFile)
                or nameof(MainWindowViewModel.HasLivePairPreview))
                ScrollLivePairPreviewToEnd();
        };
        _questionWindow.Answered += answer =>
        {
            var message = _pendingQuestion;
            ClearPendingQuestion();
            if (message is null) return;
            if (answer is null) _viewModel.SkipQuestionCommand.Execute(message);
            else _viewModel.AnswerQuestionCommand.Execute(new MainWindowViewModel.QuestionAnswerSelection(message, answer));
        };
        _viewModel.PlanModeChanged = on =>
        {
            ApplyPlanTint(on || _viewModel.PlanProgressTracking);
            if (on)
            {
                // Entering plan mode: show expanded checklist (chip only collapses, never mode-off).
                EnsurePlanWindow().SetCollapsed(false);
                ShowPlanWindow();
            }
            // User left plan mode (not mid-implement) — hide. Implement-end keeps the window
            // so checked-off steps stay visible.
            else if (!_viewModel.PlanProgressTracking) _planWindow?.Hide();
        };
        _viewModel.PlanProgressTrackingChanged = tracking =>
        {
            ApplyPlanTint(tracking || _viewModel.PlanModeEnabled);
            EnsurePlanWindow().SetProgressTracking(tracking);
            if (tracking) ShowPlanWindow();
            // Do not hide when tracking ends — user should see final check-offs.
        };
        // Plan chip: collapse ↔ expand mini dock; does not toggle plan mode.
        _viewModel.PlanWindowToggleRequested = TogglePlanWindowCollapsed;
        _viewModel.PlanWindowCollapsedChanged = collapsed =>
        {
            if (_planWindow is not null && _planWindow.IsCollapsed != collapsed)
                _planWindow.SetCollapsed(collapsed);
        };
        // Live PLAN: updates only refresh content — do not re-anchor/activate every tick.
        _viewModel.PlanUpdated = OnPlanUpdated;
        // Capture ambient context while the window is still the tiny dock, so the panel
        // itself never covers what the user was looking at; suggestions stream in after.
        DockButton.Click += async (_, _) =>
        {
            if (AppSettings.Current.CaptureScreenOnOpen) await _viewModel.RefreshContextAsync();
            SetExpanded(true);
        };
        SettingsButton.Click += async (_, _) =>
        {
            await new SettingsWindow().ShowDialog(this);
            _viewModel.NotifySettingsChanged();
        };
        ResizeGrip.PointerPressed += (_, e) =>
        {
            if (!_expanded || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginResizeDrag(WindowEdge.SouthEast, e); // blocks until the drag ends on Windows
            AppSettings.Current.PanelWidth = (int)Width;
            AppSettings.Current.PanelHeight = (int)Height;
            e.Handled = true;
        };
        // Button handles (and swallows) PointerPressed, so drag detection must tunnel in first.
        DockButton.AddHandler(PointerPressedEvent, OnDockPointerPressed, RoutingStrategies.Tunnel);
        DockButton.AddHandler(PointerMovedEvent, OnDockPointerMoved, RoutingStrategies.Tunnel);
        DockButton.AddHandler(PointerReleasedEvent, (_, _) => _dockPress = null, RoutingStrategies.Tunnel);
        DragHandle.PointerPressed += OnHeaderPressed;
        LogoButton.Click += (_, _) => SetExpanded(false);
        ClosePanelButton.Click += (_, _) => SetExpanded(false);
        Closing += (_, _) =>
        {
            SaveSettings();
            _globalShortcut.Dispose();
            _operations.CancelAll();
            _killTarget.Close();
            _questionWindow.Close();
            try { _planWindow?.Close(); } catch { /* ignore */ }
            _viewModel.Dispose();
        };
        Opened += (_, _) =>
        {
            SnapToNearestEdge();
            if (AppSettings.Current.EnableGlobalExplainShortcut)
                _viewModel.GlobalExplainShortcutAvailable = _globalShortcut.Start(() =>
                {
                    if (_viewModel.ExplainSelectionCommand.CanExecute(null))
                        _viewModel.ExplainSelectionCommand.Execute(null);
                });
            _ = _viewModel.InitializeDiagnosticsAsync();
            // Pre-warm the suggestion chips so the first dock click shows them instantly.
            if (AppSettings.Current.CaptureScreenOnOpen && AppSettings.Current.SuggestFromScreen &&
                AppSettings.Current.PrewarmOnLaunch)
                _ = _viewModel.RefreshContextAsync();
        };
        QuestionBox.AddHandler(KeyDownEvent, OnQuestionKeyDown, RoutingStrategies.Tunnel);
        // Compose "listening" beacon: pure focus glue (no VM change).
        QuestionBox.GotFocus += (_, _) => ComposeShell.Classes.Set("listening", true);
        QuestionBox.LostFocus += (_, _) => ComposeShell.Classes.Set("listening", false);
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _expanded) SetExpanded(false); };
        _viewModel.Messages.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (ChatMessage message in e.NewItems)
                    message.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(ChatMessage.Text)) ScrollChatToEnd();
                        if (args.PropertyName != nameof(ChatMessage.IsQuestion)) return;
                        if (message.IsQuestion && message.Question is not null)
                            PresentClarifyingQuestion(message);
                        else if (ReferenceEquals(_pendingQuestion, message))
                            ClearPendingQuestion();
                    };
            // New chat / clear: brief shell dim→bright reset breath.
            var prev = _messageCount;
            _messageCount = _viewModel.Messages.Count;
            if (prev > 0 && _messageCount == 0 && _expanded)
                _ = PulseShellBreathAsync();
            ScrollChatToEnd();
        };
        _viewModel.LivePairJumpRequested = (_, _) => ScrollChatToEnd();
        _viewModel.LivePairFiles.CollectionChanged += (_, _) =>
        {
            // Keep the live diff panel in view when the first write lands.
            if (_viewModel.LivePairFiles.Count == 1) ScrollChatToEnd();
            ScrollLivePairPreviewToEnd();
        };
    }

    /// <summary>
    /// Surfaces a clarifying question via the in-chat card. When the dock is collapsed, keep it
    /// collapsed and use the floating prompt as the default answer surface.
    /// </summary>
    private void PresentClarifyingQuestion(ChatMessage message)
    {
        _pendingQuestion = message;
        var wasCollapsed = !_expanded;
        if (wasCollapsed)
        {
            _questionWindow.Show(this, TextSanitizer.Clean(message.Question!), message.QuestionChoices);
            return;
        }

        ScrollChatToEnd();
        _questionWindow.Hide();
    }

    private void ClearPendingQuestion()
    {
        _pendingQuestion = null;
        _questionWindow.Hide();
    }

    private void ApplyPlanTint(bool on)
    {
        // Idle aurora tint — busy/writing styles override panelshell.plan when active.
        PanelBackground.Classes.Set("plan", on);
        StatusPill.Classes.Set("plan", on);
    }

    /// <summary>Make OCR running / ready / offline scream in the header pill + banner chrome.</summary>
    private void ApplyOcrChrome()
    {
        var phase = _viewModel.OcrPhase;
        var running = OcrUiStatus.IsBusyPhase(phase);
        var ready = phase == OcrUiPhase.Ready;
        var alert = OcrUiStatus.IsAlertPhase(phase);

        StatusPill.Classes.Set("ocr-run", running);
        StatusPill.Classes.Set("ocr-ready", ready && !running);
        StatusPill.Classes.Set("ocr-alert", alert && !running);

        OcrBanner.Classes.Set("running", running);
        OcrBanner.Classes.Set("ready", ready && !running);
        OcrBanner.Classes.Set("alert", alert && !running);
    }

    private void ShowPlanWindow()
    {
        EnsurePlanWindow().ShowBeside(this);
    }

    private void TogglePlanWindowCollapsed()
    {
        var window = EnsurePlanWindow();
        if (!window.IsVisible)
        {
            window.SetCollapsed(false);
            window.ShowBeside(this);
            return;
        }
        window.ToggleCollapsed();
    }

    private void OnPlanUpdated()
    {
        var window = EnsurePlanWindow();
        // Live check-offs update both expanded checklist and dock caption via SyncFromDocument.
        if (window.IsVisible)
            window.RefreshContent();
        else
            window.ShowBeside(this);
    }

    private PlanDocumentWindow EnsurePlanWindow()
    {
        _planWindow ??= CreatePlanWindow();
        return _planWindow;
    }

    private PlanDocumentWindow CreatePlanWindow()
    {
        var window = new PlanDocumentWindow(_viewModel.Plan);
        window.CollapsedChanged += collapsed => { _viewModel.IsPlanWindowCollapsed = collapsed; };
        window.SetProgressTracking(_viewModel.PlanProgressTracking);
        window.ImplementRequested += markdown =>
        {
            if (string.IsNullOrWhiteSpace(markdown)) return;
            _viewModel.ReplacePlanMarkdownFromWindow(markdown);
            if (_viewModel.ImplementPlanCommand.CanExecute(null))
                _viewModel.ImplementPlanCommand.Execute(null);
        };
        return window;
    }

    private void OnQuestionChoiceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string choice, Tag: ChatMessage message }) return;
        if (string.IsNullOrWhiteSpace(choice) || !message.IsQuestion) return;
        var selection = new MainWindowViewModel.QuestionAnswerSelection(message, choice.Trim());
        if (_viewModel.AnswerQuestionCommand.CanExecute(selection))
            _viewModel.AnswerQuestionCommand.Execute(selection);
        ClearPendingQuestion();
        e.Handled = true;
    }

    private async Task<bool> LaunchTaskAsync(TaskLaunchRequest request)
    {
        if (request.Kind == TaskKind.Code)
        {
            var repository = await ResolveCodeRepositoryAsync();
            if (repository is null) return false;
            await _viewModel.RunCodeTurnAsync(request.Prompt, repository);
            return true;
        }

        var confirmation = new TaskConfirmationWindow(request.Kind);
        if (!await confirmation.ShowDialog<bool>(this)) return false;

        Window taskWindow;
        if (request.Kind == TaskKind.Email)
            taskWindow = new EmailTaskWindow(request, new AiClientFactory(_operations), new OutlookService());
        else if (request.Kind == TaskKind.Shell)
        {
            var workingDirectory = _viewModel.WorkingDirectory ?? await ChooseWorkingDirectoryAsync();
            if (workingDirectory is null) return false;
            taskWindow = new ShellTaskWindow(request, new AiClientFactory(_operations), new ShellService(_operations), workingDirectory);
        }
        else if (request.Kind == TaskKind.Browser)
            taskWindow = new BrowserTaskWindow(request, new AiClientFactory(_operations));
        else taskWindow = new GenericTaskWindow(request, new AiClientFactory(_operations));
        taskWindow.Show(this);
        return true;
    }

    private void OnSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string suggestion }) return;
        if (_viewModel.UseSuggestionCommand.CanExecute(suggestion))
            _viewModel.UseSuggestionCommand.Execute(suggestion);
        e.Handled = true;
    }

    /// <summary>Reuses the last project folder this session (via a small confirm/change popup)
    /// instead of a folder picker every time; still lets you switch folders on demand. Not Git-only.</summary>
    private async Task<string?> ResolveCodeRepositoryAsync()
    {
        if (_lastCodeRepository is not null)
        {
            var keepSame = await new RepositoryConfirmationWindow(_lastCodeRepository).ShowDialog<bool>(this);
            if (keepSame) return _lastCodeRepository;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose a project folder", AllowMultiple = false });
        if (folders.Count == 0) return null;
        _lastCodeRepository = folders[0].Path.LocalPath;
        return _lastCodeRepository;
    }

    private async Task<string?> ChooseWorkingDirectoryAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose a project folder (any folder — Git not required)", AllowMultiple = false });
        if (folders.Count == 0) return _viewModel.WorkingDirectory;
        var path = folders[0].Path.LocalPath;
        _lastCodeRepository = path;
        _viewModel.WorkingDirectory = path;
        SaveSettings();
        return path;
    }

    private async void OnSetWorkingDirectoryClick(object? sender, RoutedEventArgs e)
    {
        ComposePlusButton.Flyout?.Hide();
        await ChooseWorkingDirectoryAsync();
        e.Handled = true;
    }

    private void OnClearWorkingDirectoryClick(object? sender, RoutedEventArgs e)
    {
        ComposePlusButton.Flyout?.Hide();
        _viewModel.WorkingDirectory = null;
        SaveSettings();
        e.Handled = true;
    }

    private async void OnAttachFileClick(object? sender, RoutedEventArgs e)
    {
        ComposePlusButton.Flyout?.Hide();
        await _viewModel.AttachFilesFromPickerAsync();
        e.Handled = true;
    }

    private void OnMcpMarketplaceClick(object? sender, RoutedEventArgs e)
    {
        ComposePlusButton.Flyout?.Hide();
        var market = new McpMarketplaceWindow(_viewModel.WorkingDirectory);
        market.Show(this);
        e.Handled = true;
    }

    private void OnSplitBrainKeepClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string side, DataContext: ChatMessage message }) return;
        _viewModel.ChooseSplitBrainCommand.Execute(new MainWindowViewModel.SplitBrainChoice(message, side));
        e.Handled = true;
    }

    private async Task<IReadOnlyList<string>> PickFilesToAttachAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach files to the next message",
            AllowMultiple = true,
        });
        return files.Select(f => f.Path.LocalPath).Where(File.Exists).ToArray();
    }

    private async Task<string?> ResolveWorkingDirectoryAsync()
    {
        var path = await ResolveCodeRepositoryAsync();
        if (path is null) return null;
        _viewModel.WorkingDirectory = path;
        SaveSettings();
        return path;
    }

    private void LoadSettings()
    {
        AppSettings.Load();
        var settings = AppSettings.Current;
        _viewModel.SelectedProviderIndex = Math.Clamp(settings.Provider, 0, _viewModel.Providers.Count - 1);
        _viewModel.SelectedEffortIndex = AppSettings.EffortToIndex(settings.ChatReasoningEffort);
        if (settings.WorkingDirectory is not null && Directory.Exists(settings.WorkingDirectory))
            _viewModel.WorkingDirectory = _lastCodeRepository = settings.WorkingDirectory;
    }

    private void SaveSettings()
    {
        var settings = AppSettings.Current;
        settings.Provider = _viewModel.SelectedProviderIndex;
        settings.ChatReasoningEffort = AppSettings.EffortFromIndex(_viewModel.SelectedEffortIndex);
        settings.WorkingDirectory = _viewModel.WorkingDirectory;
        settings.Save();
    }

    private void ScrollChatToEnd() =>
        Dispatcher.UIThread.Post(ChatScroll.ScrollToEnd, DispatcherPriority.Loaded);

    private void ScrollLivePairPreviewToEnd() =>
        Dispatcher.UIThread.Post(() => LivePairPreviewScroll?.ScrollToEnd(), DispatcherPriority.Loaded);

    private void OnQuestionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (_viewModel.SendCommand.CanExecute(null)) _viewModel.SendCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dockPress = e;
        _dockPressPoint = e.GetPosition(this);
    }

    private void OnDockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dockPress is null) return;
        var delta = e.GetPosition(this) - _dockPressPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;
        var press = _dockPress;
        _dockPress = null;
        var screen = Screens.ScreenFromWindow(this);
        var canKill = screen is not null && _operations.Active.Count > 0;
        if (canKill)
        {
            _killTarget.ShowFor(screen!);
            _killTargetTimer.Start();
        }
        BeginMoveDrag(press); // on Windows this blocks until the drag ends
        _killTargetTimer.Stop();
        var droppedOnTarget = canKill && _killTarget.Contains(Position, new PixelSize((int)Width, (int)Height));
        _killTarget.Hide();
        if (droppedOnTarget) _operations.CancelAll();
        SnapToNearestEdge(animate: !droppedOnTarget);
    }

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
        PositionPanelOnScreen(); // keep the dropped panel fully visible
    }

    /// <summary>Provider switch: brief violet flash on the panelshell border/glow.</summary>
    private async Task PulseShellFlashAsync()
    {
        if (!PanelBackground.IsVisible) return;
        var id = ++_shellPulseId;
        PanelBackground.Classes.Set("flash", true);
        try
        {
            await Task.Delay(280);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (id != _shellPulseId) return;
        PanelBackground.Classes.Set("flash", false);
    }

    /// <summary>New chat: shell dim→bright reset (opacity only — no transform thrash).</summary>
    private async Task PulseShellBreathAsync()
    {
        if (!PanelBackground.IsVisible || !_expanded) return;
        var id = ++_shellPulseId;
        var prior = PanelBackground.Opacity;
        if (prior < 0.5) prior = 1;
        PanelBackground.Opacity = 0.42;
        try
        {
            await Task.Delay(140);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (id != _shellPulseId || !_expanded) return;
        PanelBackground.Opacity = 1;
    }

    private void SetExpanded(bool expanded)
    {
        var settings = AppSettings.Current;
        var animationId = ++_shellAnimationId;

        if (expanded)
        {
            // Cancel any in-flight collapse finalization.
            _expanded = true;
            var seed = ShellExperience.ExpandSeed(settings.PanelWidth, settings.PanelHeight);
            var open = ShellExperience.Expanded(settings.PanelWidth, settings.PanelHeight);
            ApplyShellLayout(seed);
            // Seed pose first (transparent/scaled) while the expanded window is already sized.
            Panel.Opacity = seed.PanelOpacity;
            Panel.RenderTransform = TransformOperations.Parse(seed.PanelTransform);
            PanelBackground.Opacity = 0;
            PositionPanelOnScreen();
            Dispatcher.UIThread.Post(() =>
            {
                if (animationId != _shellAnimationId) return;
                Panel.Opacity = open.PanelOpacity;
                Panel.RenderTransform = TransformOperations.Parse(open.PanelTransform);
                PanelBackground.Opacity = 1;
                if (open.FocusCompose) QuestionBox.Focus();
            }, DispatcherPriority.Loaded);
            return;
        }

        // Collapse: animate panel out while still visible at expanded size, then swap to dock.
        _expanded = false;
        var closing = ShellExperience.CollapseAnimating(settings.PanelWidth, settings.PanelHeight);
        ApplyShellLayout(closing);
        Panel.Opacity = closing.PanelOpacity;
        Panel.RenderTransform = TransformOperations.Parse(closing.PanelTransform);
        PanelBackground.Opacity = 0;
        _ = FinalizeCollapseAfterTransitionAsync(animationId);
    }

    /// <summary>Applies dock/panel visibility and window size from a shell state without starting transitions.</summary>
    private void ApplyShellLayout(ShellVisualState state)
    {
        DockButton.IsVisible = state.DockVisible;
        DockGlow.IsVisible = state.DockVisible;
        DockRing.IsVisible = state.DockVisible;
        PanelBackground.IsVisible = state.PanelVisible;
        Panel.IsVisible = state.PanelVisible;
        ResizeGrip.IsVisible = state.PanelVisible;
        MinWidth = state.MinWidth;
        MinHeight = state.MinHeight;
        Width = state.Width;
        Height = state.Height;
    }

    private async Task FinalizeCollapseAfterTransitionAsync(int animationId)
    {
        try
        {
            await Task.Delay(ShellExperience.PanelTransitionDuration);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (animationId != _shellAnimationId || _expanded) return;

        var dock = ShellExperience.CollapsedDock();
        ApplyShellLayout(dock);
        Panel.Opacity = dock.PanelOpacity;
        Panel.RenderTransform = TransformOperations.Parse(dock.PanelTransform);
        PanelBackground.Opacity = 1;
        SnapToNearestEdge(animate: true);
    }

    private void PositionPanelOnScreen()
    {
        var area = Screens.ScreenFromWindow(this)?.WorkingArea;
        if (area is null) return;
        Position = new PixelPoint(
            Math.Clamp(Position.X, area.Value.X, area.Value.Right - (int)Width),
            Math.Clamp(Position.Y, area.Value.Y, area.Value.Bottom - (int)Height));
    }

    private void SnapToNearestEdge(bool animate = false)
    {
        if (_expanded) return;
        var area = Screens.ScreenFromWindow(this)?.WorkingArea;
        if (area is null) return;
        var right = area.Value.Right - (int)Width - 8;
        var left = area.Value.X + 8;
        var target = new PixelPoint(Position.X + (int)Width / 2 < area.Value.Center.X ? left : right,
            Math.Clamp(Position.Y, area.Value.Y + 8, area.Value.Bottom - (int)Height - 8));
        if (animate) _ = AnimatePositionAsync(target);
        else Position = target;
    }

    private async Task AnimatePositionAsync(PixelPoint target)
    {
        var id = ++_snapAnimationId; // a newer drag/snap cancels this one
        var start = Position;
        const int steps = 14;
        for (var i = 1; i <= steps; i++)
        {
            if (id != _snapAnimationId) return;
            var t = 1 - Math.Pow(1 - i / (double)steps, 3);
            Position = new PixelPoint(
                (int)Math.Round(start.X + (target.X - start.X) * t),
                (int)Math.Round(start.Y + (target.Y - start.Y) * t));
            await Task.Delay(12);
        }
    }
}
