using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Luma.App.Services;

/// <summary>Fullscreen transparent overlay that pulses a highlight ring at a physical pixel rect.</summary>
public sealed class GhostCursorWindow : Window
{
    private GhostCursorWindow(PixelRect screenBounds, Rect logicalHighlight)
    {
        Position = screenBounds.Position;
        Width = screenBounds.Width;
        Height = screenBounds.Height;
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        IsHitTestVisible = false;

        var ring = new Border
        {
            Width = Math.Max(24, logicalHighlight.Width),
            Height = Math.Max(24, logicalHighlight.Height),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(3),
            BorderBrush = LumaTheme.CreateAccentGradient(),
            BoxShadow = BoxShadows.Parse("0 0 28 0 #888A63F5, 0 0 8 0 #FFFFFFFF"),
            Background = new SolidColorBrush(Color.Parse("#228A63F5")),
            Opacity = 0,
        };
        ring.Styles.Add(new Style(x => x.OfType<Border>())
        {
            Animations =
            {
                new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(1600),
                    IterationCount = new IterationCount(2),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(RenderTransformProperty, new ScaleTransform(0.92, 0.92)) } },
                        new KeyFrame { Cue = new Cue(0.35), Setters = { new Setter(OpacityProperty, 1d), new Setter(RenderTransformProperty, new ScaleTransform(1.04, 1.04)) } },
                        new KeyFrame { Cue = new Cue(0.7), Setters = { new Setter(OpacityProperty, 0.85d), new Setter(RenderTransformProperty, new ScaleTransform(1, 1)) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d), new Setter(RenderTransformProperty, new ScaleTransform(1.08, 1.08)) } },
                    },
                },
            },
        });

        var label = new Border
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(logicalHighlight.X, Math.Max(8, logicalHighlight.Y - 28), 0, 0),
            Background = new SolidColorBrush(Color.Parse("#E0181A24")),
            BorderBrush = LumaTheme.BorderAccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3),
            Child = new TextBlock
            {
                Text = "✦ here",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = LumaTheme.TextBrightBrush,
            },
        };

        var canvas = new Canvas();
        Canvas.SetLeft(ring, logicalHighlight.X);
        Canvas.SetTop(ring, logicalHighlight.Y);
        canvas.Children.Add(ring);
        canvas.Children.Add(label);
        Content = canvas;
    }

    /// <summary>
    /// Shows a pulse ring for <paramref name="normalized"/> x,y,w,h (0–1 of the target screen).
    /// </summary>
    public static async void PointAt(Window owner, double x, double y, double w, double h, string? label = null)
    {
        try
        {
            var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
            if (screen is null) return;
            var bounds = screen.Bounds;
            var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;

            x = Math.Clamp(x, 0, 1);
            y = Math.Clamp(y, 0, 1);
            w = Math.Clamp(w, 0.02, 1 - x);
            h = Math.Clamp(h, 0.02, 1 - y);

            var physX = bounds.X + (int)(x * bounds.Width);
            var physY = bounds.Y + (int)(y * bounds.Height);
            var physW = Math.Max(40, (int)(w * bounds.Width));
            var physH = Math.Max(40, (int)(h * bounds.Height));

            // Window is placed on physical screen; children use logical coords.
            var logical = new Rect(
                (physX - bounds.X) / scaling,
                (physY - bounds.Y) / scaling,
                physW / scaling,
                physH / scaling);

            var window = new GhostCursorWindow(bounds, logical);
            if (!string.IsNullOrWhiteSpace(label) && window.Content is Canvas canvas && canvas.Children.Count > 1
                && canvas.Children[1] is Border { Child: TextBlock tb })
                tb.Text = "✦ " + label.Trim();

            window.Show(owner);
            await Task.Delay(3200);
            window.Close();
        }
        catch { /* overlay is best-effort */ }
    }
}
