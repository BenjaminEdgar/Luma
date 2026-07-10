using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Luma.App.Services;

/// <summary>
/// Floating clarifying-question prompt shown when the panel is collapsed so the user
/// can still answer without hunting for the in-chat card.
/// </summary>
public sealed class QuestionPromptWindow : Window
{
    private readonly TextBlock _eyebrow = new()
    {
        Text = "QUICK QUESTION",
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 1.4,
        Foreground = LumaTheme.AccentSoftBrush,
    };
    private readonly TextBlock _question = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 17,
        FontWeight = FontWeight.SemiBold,
        Foreground = LumaTheme.TextBrightBrush,
        LineHeight = 24,
    };
    private readonly StackPanel _choices = new() { Spacing = 8 };

    public event Action<string?>? Answered;

    public QuestionPromptWindow()
    {
        Width = 420;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 560;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;

        var dragHandle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Padding = new Thickness(0, 0, 0, 2),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Controls.LumaLogo
                    {
                        Width = 15,
                        Height = 15,
                        StarBrush = LumaTheme.AccentSoftBrush,
                        ShowOrbit = false,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    _eyebrow,
                },
            },
        };
        dragHandle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
            e.Handled = true;
        };

        Content = new Border
        {
            Background = LumaTheme.GlassFillBrush,
            BorderBrush = LumaTheme.CreatePanelBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(LumaTheme.FloatingCornerRadius),
            Padding = new Thickness(22, 18),
            BoxShadow = LumaTheme.FloatingShadow,
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    dragHandle,
                    new Border
                    {
                        Height = 1.5,
                        Background = new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                            GradientStops =
                            {
                                new GradientStop(Color.FromArgb(0, LumaTheme.AccentStart.R, LumaTheme.AccentStart.G, LumaTheme.AccentStart.B), 0),
                                new GradientStop(Color.FromArgb(0xCC, LumaTheme.AccentStart.R, LumaTheme.AccentStart.G, LumaTheme.AccentStart.B), 0.4),
                                new GradientStop(Color.FromArgb(0x99, LumaTheme.AccentEnd.R, LumaTheme.AccentEnd.G, LumaTheme.AccentEnd.B), 0.75),
                                new GradientStop(Color.FromArgb(0, LumaTheme.AccentEnd.R, LumaTheme.AccentEnd.G, LumaTheme.AccentEnd.B), 1),
                            },
                        },
                    },
                    new ScrollViewer
                    {
                        MaxHeight = 160,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = _question,
                    },
                    _choices,
                    MakeButton("Continue without this", "ghost", () => Complete(null), wide: true),
                },
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Complete(null); e.Handled = true; }
        };
    }

    public void Show(Window owner, string question, IReadOnlyList<string> choices)
    {
        _question.Text = TextSanitizer.Clean(question);
        _choices.Children.Clear();
        foreach (var choice in choices.Take(4))
            _choices.Children.Add(MakeButton(TextSanitizer.Clean(choice), "qchoice", () => Complete(choice), wide: true));

        var area = owner.Screens.ScreenFromWindow(owner)?.WorkingArea ?? owner.Screens.Primary?.WorkingArea;
        if (area is { } bounds)
            Position = new PixelPoint(
                bounds.X + (bounds.Width - (int)Width) / 2,
                bounds.Y + Math.Max(48, (bounds.Height - 300) / 2));

        if (!IsVisible) base.Show(owner);
        Activate();
    }

    private void Complete(string? answer)
    {
        if (!IsVisible) return;
        Hide();
        Answered?.Invoke(string.IsNullOrWhiteSpace(answer) ? null : answer.Trim());
    }

    private static Button MakeButton(string text, string style, Action action, bool wide)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(16, 10),
            CornerRadius = new CornerRadius(style == "qchoice" ? 12 : 11),
            HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Classes.Add(style);
        button.Click += (_, _) => action();
        return button;
    }
}
