using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Luma.App.Services;

public sealed class QuestionPromptWindow : Window
{
    private readonly TextBlock _question = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16, FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _choices = new() { Spacing = 8 };

    public event Action<string?>? Answered;

    public QuestionPromptWindow()
    {
        Width = 390; SizeToContent = SizeToContent.Height; MaxHeight = 520; CanResize = false;
        WindowDecorations = WindowDecorations.None; Topmost = true; ShowInTaskbar = false; Background = Brushes.Transparent;
        var dragHandle = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll),
            Padding = new Thickness(0, 0, 0, 4),
            Child = new TextBlock
            {
                Text = "ONE DETAIL BEFORE I CONTINUE", Foreground = new SolidColorBrush(Color.Parse("#B3A6FF")),
                FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 1,
            },
        };
        dragHandle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e); e.Handled = true;
        };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F514161E")), BorderBrush = new SolidColorBrush(Color.Parse("#408A63F5")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(20),
            BoxShadow = BoxShadows.Parse("0 16 48 0 #99000000"),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    dragHandle,
                    new ScrollViewer
                    {
                        MaxHeight = 220,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = _question,
                    },
                    _choices,
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
            _choices.Children.Add(MakeButton(TextSanitizer.Clean(choice), "outline", () => Complete(choice)));
        _choices.Children.Add(MakeButton("Continue without it", "ghost", () => Complete(null)));
        var area = owner.Screens.ScreenFromWindow(owner)?.WorkingArea ?? owner.Screens.Primary?.WorkingArea;
        if (area is { } bounds) Position = new PixelPoint(bounds.X + (bounds.Width - (int)Width) / 2, bounds.Y + Math.Max(40, (bounds.Height - 260) / 2));
        if (!IsVisible) base.Show(owner);
        Activate();
    }

    private void Complete(string? answer)
    {
        if (!IsVisible) return;
        Hide(); Answered?.Invoke(string.IsNullOrWhiteSpace(answer) ? null : answer);
    }

    private static Button MakeButton(string text, string style, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(9) };
        button.Classes.Add(style); button.Click += (_, _) => action(); return button;
    }
}
