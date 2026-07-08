using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace Luma.App.Services;

public sealed class KillTargetWindow : Window
{
    private const int TargetSize = 82;
    private readonly Border _circle;

    public KillTargetWindow()
    {
        Width = TargetSize;
        Height = TargetSize;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        _circle = new Border
        {
            Width = 70,
            Height = 70,
            CornerRadius = new CornerRadius(35),
            Background = new SolidColorBrush(Color.Parse("#CC361D29")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FFFF6B79")),
            BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = "STOP",
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        Content = _circle;
    }

    public void ShowFor(Screen screen)
    {
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.Center.X - TargetSize / 2, area.Bottom - TargetSize - 24);
        Show();
    }

    public bool Contains(PixelPoint dockPosition, PixelSize dockSize)
    {
        var dockCenter = new PixelPoint(dockPosition.X + dockSize.Width / 2, dockPosition.Y + dockSize.Height / 2);
        return dockCenter.X >= Position.X && dockCenter.X <= Position.X + TargetSize &&
               dockCenter.Y >= Position.Y && dockCenter.Y <= Position.Y + TargetSize;
    }

    public void SetHot(bool hot)
    {
        _circle.Background = new SolidColorBrush(Color.Parse(hot ? "#F2E33B4E" : "#CC361D29"));
        _circle.RenderTransform = hot ? new ScaleTransform(1.08, 1.08) : new ScaleTransform(1, 1);
    }
}
