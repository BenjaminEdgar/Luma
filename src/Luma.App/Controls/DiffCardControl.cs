using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Luma.App.Services;

namespace Luma.App.Controls;

/// <summary>Renders a <see cref="CodeChatSession"/> inline in a chat bubble: the checkbox-driven
/// structured diff (with a read-only raw fallback when parsing fails), a status line, and
/// Apply/Run tests/Revert buttons. Rebuilds when the session raises PropertyChanged, but keeps the
/// DiffView instance alive so per-file collapse state is preserved.</summary>
public sealed class DiffCardControl : ContentControl
{
    public static readonly StyledProperty<CodeChatSession?> SessionProperty =
        AvaloniaProperty.Register<DiffCardControl, CodeChatSession?>(nameof(Session));

    private static readonly IBrush MutedFg = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush CardBg = new SolidColorBrush(Color.Parse("#F2FFFFFF"));
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#887C5CFF"));

    private readonly TextBlock _status = new() { FontSize = 12, Foreground = MutedFg, TextWrapping = TextWrapping.WrapWithOverflow };
    private readonly DiffView _diffView = new() { MaxHeight = 360 };
    private readonly TextBlock _rawFallback = new()
    {
        FontFamily = FontFamily.Parse("Cascadia Mono,Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,monospace"),
        FontSize = 12,
        Foreground = MutedFg,
        TextWrapping = TextWrapping.WrapWithOverflow,
    };
    private readonly ScrollViewer _rawScroll = new()
    {
        MaxHeight = 360,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
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
        _rawScroll.Content = _rawFallback;

        _diffView.SelectionChanged += async () =>
        {
            if (Session is { } session) await session.OnSelectionChangedAsync(CancellationToken.None);
        };

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

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            ItemSpacing = 9,
            Children = { _verifyCommandBox, _verifyButton, _revertButton, _apply },
        };

        Content = new Border
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children = { _status, _artifactHost, actions },
            },
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

        var hasStructured = session.Document is { Files.Count: > 0 };
        if (hasStructured)
        {
            if (!ReferenceEquals(_diffView.Document, session.Document))
                _diffView.Document = session.Document;
        }
        else
            _rawFallback.Text = session.RawPatch;

        _artifactHost.Children.Clear();
        if (hasStructured)
            _artifactHost.Children.Add(_diffView);
        else if (!string.IsNullOrWhiteSpace(session.RawPatch))
            _artifactHost.Children.Add(_rawScroll);

        _apply.IsEnabled = session.CanApply;
        _verifyCommandBox.IsVisible = session.CanVerify;
        _verifyButton.IsVisible = session.CanVerify;
        _revertButton.IsVisible = session.CanRevert;
    }
}
