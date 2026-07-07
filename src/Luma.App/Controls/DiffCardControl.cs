using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Luma.App.Services;

namespace Luma.App.Controls;

/// <summary>Renders a <see cref="CodeChatSession"/> inline in a chat bubble: the checkbox diff
/// view (or a raw editable fallback), a status line, and Apply/Run tests/Revert buttons - the same
/// chrome the former CodeTaskWindow popup provided, minus the window frame. Rebuilds its visible
/// state whenever the session raises PropertyChanged, but keeps the DiffView/raw-textbox instances
/// alive across rebuilds so per-file collapse state and in-progress typing aren't lost.</summary>
public sealed class DiffCardControl : ContentControl
{
    public static readonly StyledProperty<CodeChatSession?> SessionProperty =
        AvaloniaProperty.Register<DiffCardControl, CodeChatSession?>(nameof(Session));

    private static readonly IBrush MutedFg = new SolidColorBrush(Color.Parse("#99FFFFFF"));
    private static readonly IBrush CardBg = new SolidColorBrush(Color.Parse("#66090A10"));
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#22FFFFFF"));

    private readonly TextBlock _status = new() { FontSize = 12, Foreground = MutedFg, TextWrapping = TextWrapping.Wrap };
    private readonly Button _toggle = new() { Padding = new Thickness(10, 5), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly DiffView _diffView = new();
    private readonly TextBox _rawBox = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = FontFamily.Parse("Consolas"),
        FontSize = 12,
        MinHeight = 160,
    };
    private readonly Grid _artifactHost = new();
    private readonly Button _apply = new() { Content = "Apply patch", Padding = new Thickness(16, 9), CornerRadius = new CornerRadius(10) };
    private readonly TextBox _verifyCommandBox = new() { Width = 200, PlaceholderText = "e.g. dotnet test", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _verifyButton = new() { Content = "Run tests", Padding = new Thickness(14, 8) };
    private readonly Button _revertButton = new() { Content = "Revert", Padding = new Thickness(14, 8) };

    private CodeChatSession? _subscribed;

    static DiffCardControl() => SessionProperty.Changed.AddClassHandler<DiffCardControl>((view, _) => view.Rebind());

    public CodeChatSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public DiffCardControl()
    {
        _toggle.Classes.Add("ghost");
        _toggle.Click += (_, _) => Session?.ToggleRawView();

        _diffView.SelectionChanged += async () =>
        {
            if (Session is { } session) await session.OnSelectionChangedAsync(CancellationToken.None);
        };

        _rawBox.TextChanged += (_, _) => Session?.SetRawPatch(_rawBox.Text ?? string.Empty);

        _apply.Classes.Add("accent");
        _apply.Click += async (_, _) =>
        {
            if (Session is { } session) await session.ApplyAsync(CancellationToken.None);
        };

        _verifyCommandBox.TextChanged += (_, _) =>
        {
            if (Session is { } session) session.VerifyCommand = _verifyCommandBox.Text ?? string.Empty;
        };
        _verifyButton.Classes.Add("outline");
        _verifyButton.Click += async (_, _) =>
        {
            if (Session is { } session) await session.VerifyAsync(CancellationToken.None);
        };

        _revertButton.Classes.Add("stop");
        _revertButton.Click += async (_, _) =>
        {
            if (Session is { } session) await session.RevertAsync(CancellationToken.None);
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 9,
            Children = { _verifyCommandBox, _verifyButton, _revertButton, _apply },
        };

        Content = new Border
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = new StackPanel { Spacing = 8, Children = { _status, _toggle, _artifactHost, actions } },
        };
    }

    private void Rebind()
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnSessionPropertyChanged;
        _subscribed = Session;
        if (_subscribed is not null) _subscribed.PropertyChanged += OnSessionPropertyChanged;
        Rebuild();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.UIThread.Post(Rebuild);

    private void Rebuild()
    {
        var session = Session;
        if (session is null) return;

        _status.Text = session.StatusMessage;
        _toggle.Content = session.ShowStructured ? "Edit raw patch" : "Structured view";

        if (!ReferenceEquals(_diffView.Document, session.Document)) _diffView.Document = session.Document;
        if (!_rawBox.IsFocused && _rawBox.Text != session.RawPatch) _rawBox.Text = session.RawPatch;

        _artifactHost.Children.Clear();
        _artifactHost.Children.Add(session.ShowStructured ? _diffView : _rawBox);

        _apply.IsEnabled = session.CanApply;
        _verifyCommandBox.IsVisible = session.CanVerify;
        _verifyButton.IsVisible = session.CanVerify;
        _revertButton.IsVisible = session.CanRevert;
    }
}
