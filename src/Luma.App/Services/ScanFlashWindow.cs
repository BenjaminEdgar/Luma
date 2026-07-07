using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Luma.App.Services;

public sealed class ScanFlashWindow : Window
{
    private ScanFlashWindow(PixelRect bounds)
    {
        Position = bounds.Position; Width = bounds.Width; Height = bounds.Height;
        WindowDecorations = WindowDecorations.None; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        CanResize = false; Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var flash = new Border
        {
            Opacity = 0,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#008A63F5"), 0), new GradientStop(Color.Parse("#558A63F5"), .5), new GradientStop(Color.Parse("#004F7CFF"), 1) },
            },
        };
        flash.Styles.Add(new Style(x => x.OfType<Border>())
        {
            Animations =
            {
                new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(400),
                    Children = { new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } }, new KeyFrame { Cue = new Cue(.45), Setters = { new Setter(OpacityProperty, .75d) } }, new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } } },
                },
            },
        });
        Content = flash;
    }

    public static async void Play(Window owner, PixelRect bounds)
    {
        var window = new ScanFlashWindow(bounds); window.Show(owner);
        await Task.Delay(450); window.Close();
    }
}
