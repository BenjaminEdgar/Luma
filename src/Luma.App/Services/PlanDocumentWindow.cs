using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Luma.App.Services;

/// <summary>
/// Always-on-top plan document: live checklist/markdown from PLAN: directives,
/// editable by the user, with Implement handing the approved plan back to main chat.
/// </summary>
public sealed class PlanDocumentWindow : Window
{
    private readonly PlanDocument _document;
    private readonly TextBlock _title = new()
    {
        FontSize = 18,
        FontWeight = FontWeight.SemiBold,
        Foreground = LumaTheme.TextBrightBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _summary = new()
    {
        FontSize = 12,
        Foreground = LumaTheme.TextMutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _progressFill = new()
    {
        Height = 5,
        CornerRadius = new CornerRadius(999),
        Background = LumaTheme.CreateAccentGradient(),
        HorizontalAlignment = HorizontalAlignment.Left,
        Width = 0,
        MinWidth = 0,
    };
    private readonly Border _progressTrack = new()
    {
        Height = 5,
        CornerRadius = new CornerRadius(999),
        Background = new SolidColorBrush(Color.Parse("#E8E4F8")),
        ClipToBounds = true,
        Opacity = 0.55,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _stepsList = new()
    {
        Spacing = 6,
        Margin = new Thickness(0, 2, 0, 0),
    };
    private readonly TextBox _editor = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = FontFamily.Parse("Consolas,Cascadia Mono,monospace"),
        FontSize = 12.5,
        Foreground = LumaTheme.TextBodyBrush,
        CaretBrush = LumaTheme.AccentSoftBrush,
        Padding = new Thickness(14, 12),
        CornerRadius = new CornerRadius(14),
        Background = new SolidColorBrush(Color.Parse("#F7F6FF")),
        BorderBrush = new SolidColorBrush(Color.Parse("#E0DCF5")),
        BorderThickness = new Thickness(1),
        MinHeight = 220,
    };
    private readonly Button _implement;
    private bool _syncing;

    /// <summary>Raised when the user clicks Implement with the current editor markdown.</summary>
    public event Action<string>? ImplementRequested;

    public PlanDocumentWindow(PlanDocument document)
    {
        _document = document;
        Title = "Plan";
        Width = 440;
        Height = 560;
        MinWidth = 360;
        MinHeight = 360;
        CanResize = true;
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _progressTrack.Child = _progressFill;
        _progressTrack.SizeChanged += (_, _) => RefreshProgress();

        _implement = MakeButton("Implement", "accent", OnImplementClick, wide: true);
        _implement.IsEnabled = false;
        _implement.FontWeight = FontWeight.SemiBold;
        _implement.FontSize = 13;
        _implement.Padding = new Thickness(16, 10);
        _implement.CornerRadius = new CornerRadius(11);
        var close = MakeButton("Close", "ghost", () => Hide(), wide: false);
        var clear = MakeButton("Clear", "ghost", () =>
        {
            _document.Clear();
            SyncFromDocument();
        }, wide: false);

        // Soft gradient strip with plan glyph badge + PLAN wordmark (drag-to-move).
        var planGlyph = new Path
        {
            Width = 12,
            Height = 12,
            Stretch = Stretch.Uniform,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Data = Geometry.Parse("M2,3 H10 M2,6.5 H10 M2,10 H7"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var glyphBadge = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(8),
            Background = LumaTheme.CreateAccentGradient(),
            Child = planGlyph,
            BoxShadow = BoxShadows.Parse("0 4 12 0 #337C4DFF"),
        };
        var dragHandle = new Border
        {
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#D4CCF5")),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#F5F0FF"), 0),
                    new GradientStop(Color.Parse("#EEF6FF"), 0.55),
                    new GradientStop(Color.Parse("#F0FBFF"), 1),
                },
            },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    glyphBadge,
                    new TextBlock
                    {
                        Text = "PLAN",
                        FontSize = 12,
                        FontWeight = FontWeight.Bold,
                        LetterSpacing = 1.8,
                        Foreground = new SolidColorBrush(Color.Parse("#5B21B6")),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        dragHandle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
            e.Handled = true;
        };

        var header = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        _title,
                        new Grid
                        {
                            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                            ColumnSpacing = 10,
                            Children =
                            {
                                _progressTrack,
                                _summary,
                            },
                        },
                    },
                },
                close,
            },
        };
        Grid.SetColumn(close, 1);
        Grid.SetColumn(_summary, 1);

        // Ghost Clear left; full-width accent Implement.
        var actions = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = 8,
            Children = { clear, _implement },
        };
        Grid.SetColumn(_implement, 1);

        Content = new Border
        {
            Background = LumaTheme.GlassFillBrush,
            BorderBrush = LumaTheme.CreatePanelBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(LumaTheme.FloatingCornerRadius),
            Padding = new Thickness(18, 16),
            BoxShadow = LumaTheme.FloatingShadow,
            Child = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,*,Auto"),
                RowSpacing = 12,
                Children =
                {
                    dragHandle,
                    header,
                    _stepsList,
                    _editor,
                    actions,
                },
            },
        };
        Grid.SetRow(header, 1);
        Grid.SetRow(_stepsList, 2);
        Grid.SetRow(_editor, 3);
        Grid.SetRow(actions, 4);

        _editor.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            _document.ReplaceFromMarkdown(_editor.Text ?? string.Empty);
            RefreshChrome();
        };

        _document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlanDocument.Markdown) or nameof(PlanDocument.Title)
                or nameof(PlanDocument.StepSummary) or nameof(PlanDocument.CanImplement)
                or nameof(PlanDocument.Steps))
                Dispatcher.UIThread.Post(SyncFromDocument);
        };

        SyncFromDocument();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        };
    }

    public void ShowBeside(Window owner)
    {
        if (owner.IsVisible)
        {
            var gap = 12;
            Position = new PixelPoint(
                owner.Position.X + (int)owner.Bounds.Width + gap,
                owner.Position.Y);
        }
        if (!IsVisible) Show(owner);
        else Activate();
        SyncFromDocument();
    }

    public void SyncFromDocument()
    {
        _syncing = true;
        try
        {
            if (_editor.Text != _document.Markdown)
                _editor.Text = _document.Markdown;
            RefreshChrome();
        }
        finally { _syncing = false; }
    }

    private void RefreshChrome()
    {
        _title.Text = _document.Title;
        _summary.Text = _document.StepSummary;
        _implement.IsEnabled = _document.CanImplement;
        RebuildStepsList();
        RefreshProgress();
    }

    private void RebuildStepsList()
    {
        _stepsList.Children.Clear();
        var steps = _document.Steps;
        _stepsList.IsVisible = steps.Count > 0;
        if (steps.Count == 0) return;

        foreach (var step in steps)
        {
            var done = step.Done;
            var mark = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(done ? 0 : 1.5),
                BorderBrush = new SolidColorBrush(Color.Parse(done ? "#00E5FF" : "#C4B5FD")),
                Background = done
                    ? LumaTheme.CreateAccentGradient()
                    : new SolidColorBrush(Color.Parse("#F8F6FF")),
                Child = new TextBlock
                {
                    Text = done ? "✓" : "",
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            if (done) mark.BoxShadow = BoxShadows.Parse("0 2 8 0 #337C4DFF");
            var label = new TextBlock
            {
                Text = step.Text,
                FontSize = 12.5,
                FontWeight = done ? FontWeight.Normal : FontWeight.Medium,
                Foreground = done
                    ? LumaTheme.TextMutedBrush
                    : LumaTheme.TextBodyBrush,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = done ? TextDecorations.Strikethrough : null,
                Opacity = done ? 0.78 : 1,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new Border
            {
                Background = done
                    ? new SolidColorBrush(Color.Parse("#F0FBFF"))
                    : new SolidColorBrush(Color.Parse("#FAF9FF")),
                BorderBrush = new SolidColorBrush(Color.Parse(done ? "#CFFAFE" : "#EDE9FE")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8),
                Child = new Grid
                {
                    ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
                    ColumnSpacing = 10,
                    Children = { mark, label },
                },
            };
            Grid.SetColumn(label, 1);
            _stepsList.Children.Add(row);
        }
    }

    private void RefreshProgress()
    {
        var total = _document.Steps.Count;
        var trackW = _progressTrack.Bounds.Width;
        if (trackW <= 0) trackW = 120;

        if (total == 0)
        {
            // Empty / waiting: dim track, no fill.
            _progressTrack.Opacity = 0.4;
            _progressFill.Width = 0;
            return;
        }

        _progressTrack.Opacity = 1;
        var done = _document.Steps.Count(s => s.Done);
        var ratio = Math.Clamp(done / (double)total, 0, 1);
        _progressFill.Width = trackW * ratio;
    }

    private void OnImplementClick()
    {
        // Push editor text first so hand-edits are what get implemented.
        _document.ReplaceFromMarkdown(_editor.Text ?? string.Empty);
        if (!_document.CanImplement) return;
        ImplementRequested?.Invoke(_document.Markdown);
    }

    private static Button MakeButton(string text, string style, Action onClick, bool wide)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(wide ? 16 : 14, 8),
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
        };
        button.Classes.Add(style);
        button.Click += (_, _) => onClick();
        return button;
    }
}
