using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Luma.App.Models;

namespace Luma.App.Services;

public sealed class TaskConfirmationWindow : Window
{
    public TaskConfirmationWindow(TaskKind kind)
    {
        Width = 410;
        Height = 230;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;

        var label = kind switch
        {
            TaskKind.Email => "email reply",
            TaskKind.Code => "coding task",
            TaskKind.Shell => "terminal command",
            TaskKind.Browser => "web reply",
            _ => "complex task"
        };
        Content = new Border
        {
            Background = LumaTheme.GlassFillBrush,
            BorderBrush = LumaTheme.BorderAccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(LumaTheme.FloatingCornerRadius),
            Padding = new Thickness(22),
            BoxShadow = LumaTheme.SoftShadow,
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "✦ Dedicated workspace", Foreground = LumaTheme.AccentSoftBrush, FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.5 },
                    new TextBlock { Text = $"This looks like an {label}.", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = LumaTheme.TextBrightBrush },
                    new TextBlock { Text = "Open a focused window with questions, progress, and an approval-ready result?", TextWrapping = TextWrapping.Wrap, Opacity = .82, Foreground = LumaTheme.TextMutedBrush },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { Button("Keep in chat", "ghost", false), Button("Open workspace", "accent", true) }
                    },
                },
            },
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
    }

    private Button Button(string text, string style, bool result)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(9) };
        button.Classes.Add(style);
        button.Click += (_, _) => Close(result);
        return button;
    }
}

/// <summary>Shown instead of a full folder picker when a coding task already has a remembered
/// repository from earlier in the session - closes true to reuse it, false to pick a different one.</summary>
public sealed class RepositoryConfirmationWindow : Window
{
    public RepositoryConfirmationWindow(string repository)
    {
        Width = 460;
        Height = 210;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;

        Content = new Border
        {
            Background = LumaTheme.GlassFillBrush,
            BorderBrush = LumaTheme.BorderAccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(LumaTheme.FloatingCornerRadius),
            Padding = new Thickness(22),
            BoxShadow = LumaTheme.SoftShadow,
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "✦ Coding task", Foreground = LumaTheme.AccentSoftBrush, FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.5 },
                    new TextBlock { Text = "Use the same project folder as last time?", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = LumaTheme.TextBrightBrush },
                    new TextBlock { Text = repository, TextWrapping = TextWrapping.Wrap, Opacity = .85, FontFamily = FontFamily.Parse("Consolas"), FontSize = 12.5, Foreground = LumaTheme.TextBodyBrush },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { Button("Choose different...", "ghost", false), Button("Use this repo", "accent", true) }
                    },
                },
            },
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(true); };
    }

    private Button Button(string text, string style, bool result)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(9) };
        button.Classes.Add(style);
        button.Click += (_, _) => Close(result);
        return button;
    }
}

public abstract class TaskWorkspaceWindow : Window
{
    private readonly IAiClientFactory _clients;
    private readonly TaskLaunchRequest _request;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBlock _status = new() { FontSize = 12, Foreground = LumaTheme.AccentSoftBrush };
    private readonly TextBlock _question = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _questionChoices = new() { Spacing = 8 };
    private readonly Border _questionCard;
    protected readonly TextBox ArtifactBox = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        IsReadOnly = false,
        FontFamily = FontFamily.Parse("Consolas"),
        FontSize = 12
    };
    protected readonly Button PrimaryButton = new() { IsVisible = false, Padding = new Thickness(16, 9), CornerRadius = new CornerRadius(10) };
    protected readonly TextBlock Summary = new() { TextWrapping = TextWrapping.Wrap, Opacity = .76 };
    protected readonly TaskSession Session;
    protected string? WorkingDirectory;
    private string? _persistentContext;

    protected TaskWorkspaceWindow(TaskLaunchRequest request, IAiClientFactory clients, string title, string subtitle)
    {
        _request = request;
        _clients = clients;
        Session = new TaskSession(request.Kind, title, request.Provider);

        Title = title;
        Width = 900;
        Height = 680;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _questionCard = new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(0x2A, LumaTheme.AccentStart.R, LumaTheme.AccentStart.G, LumaTheme.AccentStart.B)),
            BorderBrush = LumaTheme.BorderAccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Child = new StackPanel { Spacing = 9, Children = { _question, _questionChoices } },
        };

        PrimaryButton.Classes.Add("accent");
        PrimaryButton.Click += async (_, _) => await OnPrimaryActionAsync();

        var stop = new Button { Content = "Stop", Padding = new Thickness(14, 8) };
        stop.Classes.Add("stop");
        stop.Click += (_, _) => _lifetime.Cancel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 9, Children = { stop, PrimaryButton } };

        Content = new Border
        {
            Margin = new Thickness(1),
            Padding = new Thickness(24),
            Background = new SolidColorBrush(LumaTheme.GlassFillStrong),
            BorderBrush = LumaTheme.BorderAccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(LumaTheme.FloatingCornerRadius),
            BoxShadow = LumaTheme.FloatingShadow,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
                RowSpacing = 14,
                Children =
                {
                    At(
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                            Children =
                            {
                                new StackPanel
                                {
                                    Spacing = 3,
                                    Children =
                                    {
                                        new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeight.Bold, Foreground = LumaTheme.TextBrightBrush },
                                        new TextBlock { Text = subtitle, Opacity = .75, Foreground = LumaTheme.TextMutedBrush }
                                    }
                                },
                                At(new TextBlock { Text = request.Provider.ToString(), Foreground = LumaTheme.AccentSoftBrush, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, 0, 1)
                            }
                        },
                        0),
                    At(_status, 1),
                    At(_questionCard, 2),
                    At(new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 10, Children = { Summary, At(ArtifactBox, 1) } }, 3),
                    At(actions, 4),
                },
            },
        };

        Opened += async (_, _) => await BeginAsync();
        Closed += (_, _) => _lifetime.Cancel();
    }

    protected abstract Task<(string Prompt, string? Context)> PrepareAsync(CancellationToken token);
    protected virtual Task OnPrimaryActionAsync() => Task.CompletedTask;
    protected virtual void OnArtifactReady(string artifact) { }
    protected CancellationToken Lifetime => _lifetime.Token;

    private void ShowQuestionChoices(IReadOnlyList<string> choices)
    {
        _questionChoices.Children.Clear();
        foreach (var choice in choices.Take(4))
        {
            var selected = choice;
            var button = new Button { Content = TextSanitizer.Clean(selected), Padding = new Thickness(12, 7), HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Classes.Add("outline");
            button.Click += async (_, _) => await ContinueAsync(selected);
            _questionChoices.Children.Add(button);
        }
        var skip = new Button { Content = "Continue without it", Padding = new Thickness(12, 7), HorizontalAlignment = HorizontalAlignment.Right };
        skip.Classes.Add("ghost");
        skip.Click += async (_, _) => await ContinueAsync("I don't have that information. Continue with your best judgement.");
        _questionChoices.Children.Add(skip);
    }

    private async Task BeginAsync()
    {
        RetryCount = 0;
        try
        {
            SetStatus("Preparing context");
            var prepared = await PrepareAsync(_lifetime.Token);
            _persistentContext = prepared.Context;
            await RunTurnAsync(prepared.Prompt, prepared.Context);
        }
        catch (OperationCanceledException) { SetStatus("Cancelled", TaskSessionState.Cancelled); }
        catch (Exception ex) { Fail(ex); }
    }

    private async Task ContinueAsync(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return;
        RetryCount = 0;
        _questionCard.IsVisible = false;
        await RunTurnAsync(answer, null);
    }

    protected async Task RunTurnAsync(string prompt, string? context)
    {
        SetStatus("Working", TaskSessionState.Working);
        Summary.Text = string.Empty;
        var priorHistory = Session.History.ToArray();
        var aiRequest = new AiRequest(prompt, _request.ImagePath, _request.ContextImagePath, priorHistory)
        {
            TaskKind = _request.Kind,
            TaskContext = context ?? _persistentContext,
            WorkingDirectory = WorkingDirectory
        };

        var raw = await _clients.Create(_request.Provider).AskAsync(aiRequest,
            partial => Dispatcher.UIThread.Post(() => Summary.Text = TextSanitizer.Clean(partial)),
            _lifetime.Token);

        var parsed = TaskResponseParser.Parse(raw, _request.Kind);
        Session.History.Add(new ChatMessage("user", TextSanitizer.Clean(prompt)));
        Session.History.Add(new ChatMessage("assistant", parsed.Question is null ? parsed.Text : $"{parsed.Text}\nQuestion: {parsed.Question}"));
        Summary.Text = TextSanitizer.Clean(parsed.Text);
        if (parsed.Question is not null)
        {
            Session.Question = parsed.Question;
            _question.Text = TextSanitizer.Clean(parsed.Question);
            ShowQuestionChoices(parsed.Choices);
            _questionCard.IsVisible = true;
            SetStatus("Waiting for your answer", TaskSessionState.Asking);
            return;
        }
        if (!string.IsNullOrWhiteSpace(parsed.Artifact))
        {
            Session.Artifact = parsed.Artifact;
            ArtifactBox.Text = TextSanitizer.Clean(parsed.Artifact);
            SetStatus("Ready for review", TaskSessionState.ReadyForApproval);
            OnArtifactReady(parsed.Artifact);
            return;
        }
        SetStatus("Complete", TaskSessionState.ReadyForApproval);
    }

    protected const int MaxAutoRetries = 2;
    protected int RetryCount { get; set; }

    /// <summary>Starts another AI turn asking for a corrected artifact, bounded to MaxAutoRetries.
    /// Never applies/executes anything itself - it only calls RunTurnAsync, whose normal completion
    /// path (OnArtifactReady) is what the subclass already uses to re-render the proposal and
    /// re-enable its own explicit action button.</summary>
    protected async Task<bool> RetryWithFeedbackAsync(string feedbackPrompt)
    {
        if (!RetryPolicy.ShouldRetry(RetryCount, MaxAutoRetries)) return false;
        RetryCount++;
        SetStatus($"Retrying ({RetryCount}/{MaxAutoRetries})...", TaskSessionState.Working);
        try { await RunTurnAsync(feedbackPrompt, null); return true; }
        catch (OperationCanceledException) { SetStatus("Cancelled", TaskSessionState.Cancelled); return false; }
        catch (Exception ex) { Fail(ex); return false; }
    }

    protected void SetStatus(string value, TaskSessionState state = TaskSessionState.Preparing)
    {
        var clean = TextSanitizer.Clean(value);
        Session.State = state;
        Session.AddStatus(clean);
        _status.Text = $"✦ {clean}";
    }

    protected void Fail(Exception ex)
    {
        Summary.Text = TextSanitizer.Clean(ex.Message);
        SetStatus("Needs attention", TaskSessionState.Failed);
    }

    private static T At<T>(T control, int row, int column = 0) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }
}

public sealed class EmailTaskWindow : TaskWorkspaceWindow
{
    private readonly IOutlookService _outlook;
    private readonly string _prompt;

    public EmailTaskWindow(TaskLaunchRequest request, IAiClientFactory clients, IOutlookService outlook)
        : base(request, clients, "Email reply", "Ask questions, refine the response, then open an unsent Outlook draft")
    {
        _outlook = outlook;
        _prompt = request.Prompt;
    }

    protected override Task<(string Prompt, string? Context)> PrepareAsync(CancellationToken token)
    {
        var mail = _outlook.ReadSelectedMessage();
        var context = $"Selected Outlook message\nFrom: {mail.Sender}\nSubject: {mail.Subject}\nBody:\n{mail.Body}";
        return Task.FromResult<(string Prompt, string? Context)>((_prompt, context));
    }

    protected override void OnArtifactReady(string artifact)
    {
        PrimaryButton.Content = "Open unsent draft in Outlook";
        PrimaryButton.IsVisible = true;
    }

    protected override Task OnPrimaryActionAsync()
    {
        try
        {
            _outlook.OpenReplyDraft(ArtifactBox.Text ?? string.Empty);
            SetStatus("Draft opened in Outlook", TaskSessionState.Applied);
            PrimaryButton.IsEnabled = false;
        }
        catch (Exception ex) { Fail(ex); }
        return Task.CompletedTask;
    }
}

public sealed class GenericTaskWindow : TaskWorkspaceWindow
{
    private readonly string _prompt;

    public GenericTaskWindow(TaskLaunchRequest request, IAiClientFactory clients)
        : base(request, clients, "Complex task", "A focused workspace for questions, progress, and the final deliverable")
        => _prompt = request.Prompt;

    protected override Task<(string Prompt, string? Context)> PrepareAsync(CancellationToken token) => Task.FromResult((_prompt, (string?)null));
    protected override void OnArtifactReady(string artifact) { PrimaryButton.Content = "Copy deliverable"; PrimaryButton.IsVisible = true; }
    protected override async Task OnPrimaryActionAsync()
    {
        if (Clipboard is not null)
            _ = ClipboardExtensions.SetTextAsync(Clipboard, ArtifactBox.Text ?? string.Empty);
        SetStatus("Copied to clipboard", TaskSessionState.Applied);
    }
}

public sealed class ShellTaskWindow : TaskWorkspaceWindow
{
    private readonly IShellService _shell;
    private readonly string _prompt;

    public ShellTaskWindow(TaskLaunchRequest request, IAiClientFactory clients, IShellService shell, string workingDirectory)
        : base(request, clients, "Terminal command", "Review a proposed command, then run it only after explicit approval")
    {
        _shell = shell;
        WorkingDirectory = workingDirectory;
        _prompt = request.Prompt;
    }

    protected override Task<(string Prompt, string? Context)> PrepareAsync(CancellationToken token) =>
        Task.FromResult((_prompt, (string?)$"Working directory: {WorkingDirectory}"));

    protected override void OnArtifactReady(string artifact) { PrimaryButton.Content = "Run command"; PrimaryButton.IsVisible = true; PrimaryButton.IsEnabled = true; RetryCount = 0; }

    protected override async Task OnPrimaryActionAsync()
    {
        try
        {
            PrimaryButton.IsEnabled = false;
            var command = ArtifactBox.Text ?? string.Empty;
            var result = await _shell.RunAsync(WorkingDirectory!, command, Lifetime);
            var output = string.Join('\n', new[] { result.Output, result.Error }.Where(s => !string.IsNullOrWhiteSpace(s)));
            Summary.Text = string.IsNullOrWhiteSpace(Summary.Text) ? output : $"{Summary.Text}\n\n{output}";
            if (result.ExitCode == 0) { SetStatus("Command completed", TaskSessionState.Applied); return; }

            SetStatus("Command failed", TaskSessionState.Failed);
            var retried = await RetryWithFeedbackAsync(
                $"The command `{command}` failed with exit code {result.ExitCode}.\n" +
                $"stdout:\n{result.Output}\nstderr:\n{result.Error}\n" +
                "Propose a corrected command for the same goal. Do not execute anything yourself.");
            if (!retried) PrimaryButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            PrimaryButton.IsEnabled = true;
            Fail(ex);
        }
    }
}

public sealed class BrowserTaskWindow : TaskWorkspaceWindow
{
    private readonly string _prompt;

    public BrowserTaskWindow(TaskLaunchRequest request, IAiClientFactory clients)
        : base(request, clients, "Web reply", "Draft a reply to paste into a browser")
        => _prompt = request.Prompt;

    protected override Task<(string Prompt, string? Context)> PrepareAsync(CancellationToken token) => Task.FromResult((_prompt, (string?)null));
    protected override void OnArtifactReady(string artifact) { PrimaryButton.Content = "Copy reply"; PrimaryButton.IsVisible = true; }
    protected override async Task OnPrimaryActionAsync()
    {
        if (Clipboard is not null)
            _ = ClipboardExtensions.SetTextAsync(Clipboard, ArtifactBox.Text ?? string.Empty);
        SetStatus("Copied to clipboard", TaskSessionState.Applied);
    }
}
