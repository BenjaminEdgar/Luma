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
    private Point _start;

    private SelectionWindow(PixelRect bounds)
    {
        Position = bounds.Position;
        Width = bounds.Width;
        Height = bounds.Height;
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
    }

    public static async Task<PixelRect?> SelectAsync(Window owner, CancellationToken token)
    {
        var screens = owner.Screens.All;
        var left = screens.Min(s => s.Bounds.X);
        var top = screens.Min(s => s.Bounds.Y);
        var right = screens.Max(s => s.Bounds.Right);
        var bottom = screens.Max(s => s.Bounds.Bottom);
        var window = new SelectionWindow(new PixelRect(left, top, right - left, bottom - top));
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => window.Complete(null)));
        window.Show();
        window.Activate();
        window.Focus();
        return await window._completion.Task;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
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
        var x = (int)Math.Min(_start.X, end.X);
        var y = (int)Math.Min(_start.Y, end.Y);
        var w = (int)Math.Abs(end.X - _start.X);
        var h = (int)Math.Abs(end.Y - _start.Y);
        Complete(new PixelRect(Position.X + x, Position.Y + y, w, h));
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
        _sizeText.Text = $"{(int)w} x {(int)h}";
        Canvas.SetLeft(_sizeBadge, point.X + 14);
        Canvas.SetTop(_sizeBadge, point.Y + 14);
    }

    private void Complete(PixelRect? result)
    {
        _completion.TrySetResult(result);
        Close();
    }
}
