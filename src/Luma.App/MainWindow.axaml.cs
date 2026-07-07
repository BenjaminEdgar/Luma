using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Text.Json;
using Luma.App.Models;
using Luma.App.Services;
using Luma.App.ViewModels;

namespace Luma.App;

public partial class MainWindow : Window
{
    private const double DragThreshold = 5;

    private readonly MainWindowViewModel _viewModel;
    private readonly QuestionPromptWindow _questionWindow = new();
    private ChatMessage? _pendingQuestion;
    private bool _expanded;
    private PointerPressedEventArgs? _dockPress;
    private Point _dockPressPoint;
    private int _snapAnimationId;
    private string? _lastCodeRepository;
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(this, new ScreenCaptureService(), new AiClientFactory());
        DataContext = _viewModel;
        _viewModel.TaskLaunchRequested = LaunchTaskAsync;
        LoadSettings();
        _viewModel.WorkingDirectoryRequested = ChooseWorkingDirectoryAsync;
        ProjectButton.Click += async (_, _) => await ChooseWorkingDirectoryAsync();
        _questionWindow.Answered += answer =>
        {
            var message = _pendingQuestion; _pendingQuestion = null;
            if (message is null) return;
            if (answer is null) _viewModel.SkipQuestionCommand.Execute(message);
            else { message.QuestionAnswer = answer; _viewModel.AnswerQuestionCommand.Execute(message); }
        };
        DockButton.Click += (_, _) => SetExpanded(true);
        // Button handles (and swallows) PointerPressed, so drag detection must tunnel in first.
        DockButton.AddHandler(PointerPressedEvent, OnDockPointerPressed, RoutingStrategies.Tunnel);
        DockButton.AddHandler(PointerMovedEvent, OnDockPointerMoved, RoutingStrategies.Tunnel);
        DockButton.AddHandler(PointerReleasedEvent, (_, _) => _dockPress = null, RoutingStrategies.Tunnel);
        DragHandle.PointerPressed += OnHeaderPressed;
        LogoButton.Click += (_, _) => SetExpanded(false);
        ClosePanelButton.Click += (_, _) => SetExpanded(false);
        Closing += (_, _) => { SaveSettings(); _questionWindow.Close(); _viewModel.Dispose(); };
        Opened += (_, _) => SnapToNearestEdge();
        QuestionBox.AddHandler(KeyDownEvent, OnQuestionKeyDown, RoutingStrategies.Tunnel);
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _expanded) SetExpanded(false); };
        _viewModel.Messages.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (ChatMessage message in e.NewItems)
                    message.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(ChatMessage.Text)) ScrollChatToEnd();
                        if (args.PropertyName == nameof(ChatMessage.IsQuestion) && message.IsQuestion && message.Question is not null)
                        { _pendingQuestion = message; _questionWindow.Show(this, TextSanitizer.Clean(message.Question)); }
                    };
            ScrollChatToEnd();
        };
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
            taskWindow = new EmailTaskWindow(request, new AiClientFactory(), new OutlookService());
        else if (request.Kind == TaskKind.Shell)
        {
            var workingDirectory = _viewModel.WorkingDirectory ?? await ChooseWorkingDirectoryAsync();
            if (workingDirectory is null) return false;
            taskWindow = new ShellTaskWindow(request, new AiClientFactory(), new ShellService(), workingDirectory);
        }
        else if (request.Kind == TaskKind.Browser)
            taskWindow = new BrowserTaskWindow(request, new AiClientFactory());
        else taskWindow = new GenericTaskWindow(request, new AiClientFactory());
        taskWindow.Show(this);
        return true;
    }

    /// <summary>Reuses the repository from the last coding task this session (via a small confirm/change
    /// popup) instead of showing the OS folder picker every time; still lets you switch repos on demand.</summary>
    private async Task<string?> ResolveCodeRepositoryAsync()
    {
        if (_lastCodeRepository is not null)
        {
            var keepSame = await new RepositoryConfirmationWindow(_lastCodeRepository).ShowDialog<bool>(this);
            if (keepSame) return _lastCodeRepository;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose the Git repository", AllowMultiple = false });
        if (folders.Count == 0) return null;
        _lastCodeRepository = folders[0].Path.LocalPath;
        return _lastCodeRepository;
    }

    private async Task<string?> ChooseWorkingDirectoryAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose a project or working folder", AllowMultiple = false });
        if (folders.Count == 0) return _viewModel.WorkingDirectory;
        var path = folders[0].Path.LocalPath;
        _lastCodeRepository = path;
        _viewModel.WorkingDirectory = path;
        SaveSettings();
        return path;
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath));
            if (settings is null) return;
            _viewModel.SelectedProviderIndex = Math.Clamp(settings.Provider, 0, _viewModel.Providers.Count - 1);
            _viewModel.SelectedModeIndex = Math.Clamp(settings.Mode, 0, _viewModel.Modes.Count - 1);
            if (settings.WorkingDirectory is not null && Directory.Exists(settings.WorkingDirectory))
                _viewModel.WorkingDirectory = _lastCodeRepository = settings.WorkingDirectory;
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new UserSettings(
                _viewModel.SelectedProviderIndex, _viewModel.SelectedModeIndex, _viewModel.WorkingDirectory)));
        }
        catch { }
    }

    private sealed record UserSettings(int Provider, int Mode, string? WorkingDirectory);

    private void ScrollChatToEnd() =>
        Dispatcher.UIThread.Post(ChatScroll.ScrollToEnd, DispatcherPriority.Loaded);

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
        BeginMoveDrag(press); // on Windows this blocks until the drag ends
        SnapToNearestEdge(animate: true);
    }

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
        PositionPanelOnScreen(); // keep the dropped panel fully visible
    }

    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        DockButton.IsVisible = !expanded;
        DockGlow.IsVisible = !expanded;
        PanelBackground.IsVisible = expanded;
        Panel.IsVisible = expanded;
        Width = expanded ? 540 : 52;
        Height = expanded ? 660 : 52;
        if (expanded)
        {
            PositionPanelOnScreen();
            Dispatcher.UIThread.Post(() =>
            {
                Panel.Opacity = 1;
                Panel.RenderTransform = TransformOperations.Parse("translateY(0px)");
                QuestionBox.Focus();
            }, DispatcherPriority.Loaded);
        }
        else
        {
            Panel.Opacity = 0;
            Panel.RenderTransform = TransformOperations.Parse("translateY(10px)");
            SnapToNearestEdge();
        }
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
