using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Luma.App.Services;

public sealed class SelectionWindow : Window
{
    private static readonly Color Accent = Color.Parse("#8A63F5");

    private readonly TaskCompletionSource<PixelRect?> _completion = new();
    private readonly Canvas _canvas = new();
    private readonly Rectangle _selection = new()
    {
        Stroke = new SolidColorBrush(Accent),
        StrokeThickness = 2,
        RadiusX = 3,
        RadiusY = 3,
        Fill = new SolidColorBrush(Color.FromArgb(36, Accent.R, Accent.G, Accent.B)),
        IsVisible = false,
    };
    private readonly Border _sizeBadge;
    private readonly TextBlock _sizeText;
    private readonly Border _hint;
    private readonly PixelRect _bounds;
    private Point _start;

    private SelectionWindow(PixelRect bounds, double scaling)
    {
        // Screen bounds are physical pixels but Width/Height are logical units, so divide by
        // the scale factor or the overlay overshoots the screen on scaled displays.
        _bounds = bounds;
        Position = bounds.Position;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        WindowDecorations = WindowDecorations.None;
        WindowState = WindowState.Normal;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = new SolidColorBrush(Color.FromArgb(75, 0, 0, 0));
        Cursor = new Cursor(StandardCursorType.Cross);

        _sizeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeight.Medium };
        _sizeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, Accent.R, Accent.G, Accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 3),
            Child = _sizeText,
            IsVisible = false,
        };
        _hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(16, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 32, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "*", FontSize = 13, Foreground = new SolidColorBrush(Accent), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Drag to select a region - Esc to cancel", Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };

        _canvas.Children.Add(_selection);
        _canvas.Children.Add(_sizeBadge);
        Content = new Grid { Children = { _canvas, _hint } };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Complete(null); };
        Closed += (_, _) => _completion.TrySetResult(null);
        // The constructor's scaling is an estimate from screen metadata; once the window is
        // actually on screen, RenderScaling is authoritative - re-fit the overlay with it.
        Opened += (_, _) => { Width = _bounds.Width / RenderScaling; Height = _bounds.Height / RenderScaling; };
    }

    public static async Task<PixelRect?> SelectAsync(Window owner, CancellationToken token)
    {
        var screens = owner.Screens.All;
        var left = screens.Min(s => s.Bounds.X);
        var top = screens.Min(s => s.Bounds.Y);
        var right = screens.Max(s => s.Bounds.Right);
        var bottom = screens.Max(s => s.Bounds.Bottom);
        var origin = new PixelPoint(left, top);
        var scaling = screens.FirstOrDefault(s => s.Bounds.Contains(origin))?.Scaling
            ?? owner.Screens.Primary?.Scaling ?? 1.0;
        var window = new SelectionWindow(new PixelRect(left, top, right - left, bottom - top), scaling);
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => window.Complete(null)));
        window.Show();
        window.Activate();
        window.Focus();
        return await window._completion.Task;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
        _start = e.GetPosition(_canvas);
        _selection.IsVisible = true;
        _sizeBadge.IsVisible = true;
        _hint.IsVisible = false;
        e.Pointer.Capture(_canvas);
        Update(_start);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured == _canvas) Update(e.GetPosition(_canvas));
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured != _canvas) return;
        var end = e.GetPosition(_canvas);
        e.Pointer.Capture(null);
        // Pointer positions are logical units; the capture pipeline (BitBlt & friends) works in
        // physical pixels, so the selection must be scaled back up before leaving this window.
        var result = SelectionRules.ToPhysicalRect(_start, end, Position, RenderScaling);
        if (!SelectionRules.IsUsable(result))
        {
            _selection.IsVisible = false;
            _sizeBadge.IsVisible = false;
            _hint.IsVisible = true;
            if (_hint.Child is StackPanel panel && panel.Children.LastOrDefault() is TextBlock text)
                text.Text = $"Select at least {SelectionRules.MinimumWidth} x {SelectionRules.MinimumHeight} pixels - Esc to cancel";
            return;
        }
        Complete(result);
    }

    private void Update(Point point)
    {
        var x = Math.Min(_start.X, point.X);
        var y = Math.Min(_start.Y, point.Y);
        var w = Math.Abs(point.X - _start.X);
        var h = Math.Abs(point.Y - _start.Y);
        Canvas.SetLeft(_selection, x);
        Canvas.SetTop(_selection, y);
        _selection.Width = w;
        _selection.Height = h;
        _sizeText.Text = $"{(int)Math.Round(w * RenderScaling)} x {(int)Math.Round(h * RenderScaling)}";
        Canvas.SetLeft(_sizeBadge, Math.Clamp(point.X + 14, 8, Math.Max(8, _canvas.Bounds.Width - 96)));
        Canvas.SetTop(_sizeBadge, Math.Clamp(point.Y + 14, 8, Math.Max(8, _canvas.Bounds.Height - 34)));
    }

    private void Complete(PixelRect? result)
    {
        _completion.TrySetResult(result);
        Close();
    }
}

public static class SelectionRules
{
    public const int MinimumWidth = 24;
    public const int MinimumHeight = 24;
    public static bool IsUsable(PixelRect selection) =>
        selection.Width >= MinimumWidth && selection.Height >= MinimumHeight;

    /// <summary>Converts a drag in window-logical units into the physical-pixel rect the
    /// capture pipeline expects. origin is the overlay window's physical position.</summary>
    public static PixelRect ToPhysicalRect(Point start, Point end, PixelPoint origin, double scaling)
    {
        var x = Math.Min(start.X, end.X) * scaling;
        var y = Math.Min(start.Y, end.Y) * scaling;
        var w = Math.Abs(end.X - start.X) * scaling;
        var h = Math.Abs(end.Y - start.Y) * scaling;
        // Away-from-zero so a half-pixel boundary grows the selection instead of shaving it.
        return new PixelRect(
            origin.X + (int)Math.Round(x, MidpointRounding.AwayFromZero),
            origin.Y + (int)Math.Round(y, MidpointRounding.AwayFromZero),
            (int)Math.Round(w, MidpointRounding.AwayFromZero),
            (int)Math.Round(h, MidpointRounding.AwayFromZero));
    }
}
