using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Luma.App.Services;

/// <summary>
/// Fullscreen transparent overlay that pulses a highlight ring at a screen region.
/// Screen bounds are physical pixels; window Width/Height and child layout use logical units.
/// </summary>
public sealed class GhostCursorWindow : Window
{
    private readonly PixelRect _bounds;
    private readonly double _scaling;

    private GhostCursorWindow(PixelRect screenBounds, double scaling, Rect logicalHighlight, string? labelText)
    {
        _bounds = screenBounds;
        _scaling = scaling <= 0 ? 1 : scaling;

        Position = screenBounds.Position;
        // Physical bounds → logical size (same pattern as SelectionWindow).
        Width = screenBounds.Width / _scaling;
        Height = screenBounds.Height / _scaling;
        WindowDecorations = WindowDecorations.None;
        WindowState = WindowState.Normal;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        IsHitTestVisible = false;

        var ringW = Math.Max(24, logicalHighlight.Width);
        var ringH = Math.Max(24, logicalHighlight.Height);
        var ring = new Border
        {
            Width = ringW,
            Height = ringH,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(3),
            BorderBrush = LumaTheme.CreateAccentGradient(),
            Background = new SolidColorBrush(Color.FromArgb(0x33, LumaTheme.AccentStart.R, LumaTheme.AccentStart.G, LumaTheme.AccentStart.B)),
            Opacity = 0,
            // Opacity-only keyframes: animating RenderTransform/ScaleTransform objects
            // via Style setters throws InvalidOperationException in Avalonia.
        };
        ring.Styles.Add(new Style(x => x.OfType<Border>())
        {
            Animations =
            {
                new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(900),
                    IterationCount = new IterationCount(3),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(0.4), Setters = { new Setter(OpacityProperty, 1d) } },
                        new KeyFrame { Cue = new Cue(0.75), Setters = { new Setter(OpacityProperty, 0.7d) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
                    },
                },
            },
        });

        var displayLabel = string.IsNullOrWhiteSpace(labelText) ? "✦ here" : "✦ " + labelText.Trim();
        var label = new Border
        {
            Background = LumaTheme.GlassFillBrush,
            BorderBrush = LumaTheme.BorderAccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3),
            Child = new TextBlock
            {
                Text = displayLabel,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = LumaTheme.TextBrightBrush,
            },
        };

        var canvas = new Canvas();
        Canvas.SetLeft(ring, logicalHighlight.X);
        Canvas.SetTop(ring, logicalHighlight.Y);

        // Position label centered above ring, with smart bounds checking
        label.Measure(Size.Infinity);
        var labelWidth = label.DesiredSize.Width > 0 ? label.DesiredSize.Width : 80;
        var labelHeight = label.DesiredSize.Height > 0 ? label.DesiredSize.Height : 24;

        // Center horizontally on ring center
        var ringCenterX = logicalHighlight.X + ringW / 2;
        var labelX = Math.Clamp(ringCenterX - labelWidth / 2, 4, Width - labelWidth - 4);

        // Place above ring with 8px gap, or below if too close to top
        var labelYAbove = logicalHighlight.Y - labelHeight - 8;
        var labelY = labelYAbove >= 4 ? labelYAbove : logicalHighlight.Y + ringH + 8;
        labelY = Math.Clamp(labelY, 4, Height - labelHeight - 4);

        Canvas.SetLeft(label, labelX);
        Canvas.SetTop(label, labelY);
        canvas.Children.Add(ring);
        canvas.Children.Add(label);
        Content = canvas;

        // Once on-screen, RenderScaling is authoritative — re-fit like SelectionWindow.
        Opened += (_, _) =>
        {
            var s = RenderScaling > 0 ? RenderScaling : _scaling;
            Width = _bounds.Width / s;
            Height = _bounds.Height / s;
        };
    }

    /// <summary>
    /// Shows a pulse ring for normalized x,y,w,h (0–1 of the target screen).
    /// Safe to call from any thread; work is marshaled to the UI thread.
    /// </summary>
    public static void PointAt(Window owner, double x, double y, double w, double h, string? label = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => PointAt(owner, x, y, w, h, label));
            return;
        }

        _ = PointAtAsync(owner, x, y, w, h, label);
    }

    private static async Task PointAtAsync(Window owner, double x, double y, double w, double h, string? label)
    {
        GhostCursorWindow? window = null;
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

            // Normalized fractions of the full physical screen → logical layout coords.
            var logical = new Rect(
                (x * bounds.Width) / scaling,
                (y * bounds.Height) / scaling,
                Math.Max(24, (w * bounds.Width) / scaling),
                Math.Max(24, (h * bounds.Height) / scaling));

            window = new GhostCursorWindow(bounds, scaling, logical, label);
            window.Show(owner);
            await Task.Delay(2800);
        }
        catch
        {
            // Overlay is best-effort; never break the chat flow.
        }
        finally
        {
            try { window?.Close(); } catch { /* already closed */ }
        }
    }
}
