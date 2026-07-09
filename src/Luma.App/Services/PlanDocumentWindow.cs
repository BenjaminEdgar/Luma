using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Luma.App.Services;

/// <summary>
/// Always-on-top plan document: live checklist/markdown from PLAN: directives,
/// editable by the user, with Implement handing the approved plan back to main chat.
/// Supports expanded checklist panel vs mini dock (glyph + title + progress).
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
    private readonly TextBlock _dockTitle = new()
    {
        FontSize = 12.5,
        FontWeight = FontWeight.SemiBold,
        Foreground = LumaTheme.TextBrightBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _dockSummary = new()
    {
        FontSize = 11,
        Foreground = LumaTheme.TextMutedBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
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
    private readonly ScrollViewer _stepsScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        // Long plans scroll inside a fixed band instead of growing the window.
        MaxHeight = 280,
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
        MinHeight = 160,
        IsVisible = false,
    };
    private readonly Button _editToggle;
    private readonly Button _implement;
    private readonly Button _clear;
    private readonly Grid _actions;
    private readonly Grid _expandedBody;
    private readonly Border _collapsedDock;
    private readonly List<StepRow> _stepRows = [];
    private bool _syncing;
    private bool _editing;
    private bool _hasAnchored;
    private bool _collapsed;
    private bool _progressTracking;

    /// <summary>Raised when the user clicks Implement with the current editor markdown.</summary>
    public event Action<string>? ImplementRequested;

    /// <summary>Raised on collapse/expand so main window can reflect chevron affordance on the plan chip.</summary>
    public event Action<bool>? CollapsedChanged;

    public bool IsCollapsed => _collapsed;

    public PlanDocumentWindow(PlanDocument document)
    {
        _document = document;
        Title = "Plan";
        ApplyWindowSize(collapsed: false);
        CanResize = true;
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _stepsScroll.Content = _stepsList;
        _progressTrack.Child = _progressFill;
        _progressTrack.SizeChanged += (_, _) => RefreshProgress();

        _implement = MakeButton("Implement", "accent", OnImplementClick, wide: true);
        _implement.IsEnabled = false;
        _implement.FontWeight = FontWeight.SemiBold;
        _implement.FontSize = 13;
        _implement.Padding = new Thickness(16, 10);
        _implement.CornerRadius = new CornerRadius(11);
        _editToggle = MakeButton("Edit", "ghost", ToggleEdit, wide: false);
        // Close fully hides (mode stays on if active); chip re-expands. Collapse is via plan chip.
        var close = MakeButton("Close", "ghost", () =>
        {
            _hasAnchored = false;
            Hide();
        }, wide: false);
        _clear = MakeButton("Clear", "ghost", () =>
        {
            _document.Clear();
            SyncFromDocument();
        }, wide: false);

        // Soft gradient strip with plan glyph badge + PLAN wordmark (drag-to-move).
        var dragHandle = BuildDragHandle();

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

        // Clear + Edit left; full-width accent Implement.
        var leftActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _clear, _editToggle },
        };
        _actions = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = 8,
            Children = { leftActions, _implement },
        };
        Grid.SetColumn(_implement, 1);

        _expandedBody = new Grid
        {
            // Checklist gets the flexible * row; editor is optional Auto below it.
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto,Auto"),
            RowSpacing = 12,
            Children =
            {
                dragHandle,
                header,
                _stepsScroll,
                _editor,
                _actions,
            },
        };
        Grid.SetRow(header, 1);
        Grid.SetRow(_stepsScroll, 2);
        Grid.SetRow(_editor, 3);
        Grid.SetRow(_actions, 4);

        _collapsedDock = BuildCollapsedDock();

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
                Children = { _expandedBody, _collapsedDock },
            },
        };

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
        ApplyCollapsedVisuals();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                // Escape collapses when expanded; second Escape (or Close) hides fully.
                if (!_collapsed) SetCollapsed(true);
                else
                {
                    _hasAnchored = false;
                    Hide();
                }
                e.Handled = true;
            }
        };
    }

    /// <summary>Position beside the owner and show. Anchors only on first show (or after hide).</summary>
    public void ShowBeside(Window owner)
    {
        if (!_hasAnchored && owner.IsVisible)
        {
            var gap = 12;
            Position = new PixelPoint(
                owner.Position.X + (int)owner.Bounds.Width + gap,
                owner.Position.Y);
            _hasAnchored = true;
        }
        if (!IsVisible) Show(owner);
        else Activate();
        SyncFromDocument();
    }

    /// <summary>Refresh checklist/markdown without re-anchoring or activating the window.</summary>
    public void RefreshContent() => SyncFromDocument();

    /// <summary>Expand or collapse to the mini always-on-top dock.</summary>
    public void SetCollapsed(bool collapsed)
    {
        if (_collapsed == collapsed) return;
        // Leaving edit mode when collapsing so live PLAN: streams resume cleanly.
        if (collapsed && _editing)
        {
            _editing = false;
            _document.ReplaceFromMarkdown(_editor.Text ?? string.Empty);
        }
        _collapsed = collapsed;
        ApplyCollapsedVisuals();
        CollapsedChanged?.Invoke(_collapsed);
        if (!_collapsed) Activate();
    }

    public void ToggleCollapsed() => SetCollapsed(!_collapsed);

    /// <summary>While implement is running, hide Clear / Edit / Implement (progress is live-only).</summary>
    public void SetProgressTracking(bool tracking)
    {
        if (_progressTracking == tracking) return;
        _progressTracking = tracking;
        ApplyActionVisibility();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hasAnchored = false;
        base.OnClosed(e);
    }

    public void SyncFromDocument()
    {
        _syncing = true;
        try
        {
            // While the user is hand-editing, do not stomp the TextBox from live PLAN: streams.
            if (!_editing && _editor.Text != _document.Markdown)
                _editor.Text = _document.Markdown;
            RefreshChrome();
        }
        finally { _syncing = false; }
    }

    private void ApplyCollapsedVisuals()
    {
        _expandedBody.IsVisible = !_collapsed;
        _collapsedDock.IsVisible = _collapsed;
        CanResize = !_collapsed;
        ApplyWindowSize(_collapsed);
        // Compact padding on mini dock.
        if (Content is Border shell)
            shell.Padding = _collapsed ? new Thickness(10, 8) : new Thickness(18, 16);
        // Re-apply nested visibility (actions/editor/steps depend on collapsed + tracking).
        ApplyEditVisibility();
        ApplyActionVisibility();
    }

    private void ApplyWindowSize(bool collapsed)
    {
        var layout = PlanDockExperience.Layout(collapsed);
        Width = layout.Width;
        Height = layout.Height;
        MinWidth = layout.MinWidth;
        MinHeight = layout.MinHeight;
        if (collapsed)
        {
            // Snap to dock height so the pill stays tight.
            MaxHeight = layout.Height + 8;
            MaxWidth = 480;
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;
        }
    }

    private void ApplyActionVisibility()
    {
        var show = PlanDockExperience.ShowEditActions(_progressTracking);
        _actions.IsVisible = show && !_collapsed;
        // If tracking, force out of edit mode so steps list stays live.
        if (!show && _editing)
        {
            _editing = false;
            ApplyEditVisibility();
        }
    }

    private Border BuildCollapsedDock()
    {
        var planGlyph = new Avalonia.Controls.Shapes.Path
        {
            Width = 11,
            Height = 11,
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
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = LumaTheme.CreateAccentGradient(),
            Child = planGlyph,
            BoxShadow = BoxShadows.Parse("0 4 12 0 #337C4DFF"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var labels = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _dockTitle, _dockSummary },
        };
        var planWordmark = new TextBlock
        {
            Text = "PLAN",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.4,
            Foreground = new SolidColorBrush(Color.Parse("#5B21B6")),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75,
        };
        var row = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = 10,
            Children = { glyphBadge, labels, planWordmark },
        };
        Grid.SetColumn(labels, 1);
        Grid.SetColumn(planWordmark, 2);

        var dock = new Border
        {
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = Brushes.Transparent,
            IsVisible = false,
            Child = row,
        };
        // Click pill → expand; drag via press+move on empty chrome uses BeginMoveDrag on right area.
        dock.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            // Expand on click; Shift+drag moves without expanding.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                BeginMoveDrag(e);
                e.Handled = true;
                return;
            }
            SetCollapsed(false);
            e.Handled = true;
        };
        return dock;
    }

    private Border BuildDragHandle()
    {
        var planGlyph = new Avalonia.Controls.Shapes.Path
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
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        // Icon click collapses (banner behind it moves via dragHandle).
        glyphBadge.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            ToggleCollapsed();
            e.Handled = true;
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
        return dragHandle;
    }

    private void ToggleEdit()
    {
        _editing = !_editing;
        if (_editing)
        {
            _syncing = true;
            try { _editor.Text = _document.Markdown; }
            finally { _syncing = false; }
        }
        else
        {
            // Leaving edit: commit any hand-edits.
            _document.ReplaceFromMarkdown(_editor.Text ?? string.Empty);
        }
        ApplyEditVisibility();
        RefreshChrome();
    }

    private void ApplyEditVisibility()
    {
        var hasSteps = _document.Steps.Count > 0;
        // Tick-box UI is default when steps exist; raw markdown only in edit mode
        // (or when there is freeform plan text with no checklist).
        _editor.IsVisible = !_collapsed && (_editing || (!hasSteps && _document.HasContent));
        _editToggle.Content = _editing ? "Done" : "Edit";
        _stepsScroll.IsVisible = !_collapsed && hasSteps && !_editing;
    }

    private void RefreshChrome()
    {
        _title.Text = _document.Title;
        _summary.Text = _document.StepSummary;
        _dockTitle.Text = string.IsNullOrWhiteSpace(_document.Title) ? "Plan" : _document.Title;
        _dockSummary.Text = _document.StepSummary;
        _implement.IsEnabled = _document.CanImplement;
        ApplyEditVisibility();
        ApplyActionVisibility();
        UpdateStepsList();
        RefreshProgress();
    }

    /// <summary>Reuse existing step rows when possible to avoid layout thrash on live check-offs.</summary>
    private void UpdateStepsList()
    {
        var steps = _document.Steps;
        if (steps.Count == 0)
        {
            if (_stepRows.Count > 0)
            {
                _stepsList.Children.Clear();
                _stepRows.Clear();
            }
            return;
        }

        // Shrink surplus rows.
        while (_stepRows.Count > steps.Count)
        {
            var last = _stepRows[^1];
            _stepsList.Children.Remove(last.Root);
            _stepRows.RemoveAt(_stepRows.Count - 1);
        }

        // Grow missing rows.
        while (_stepRows.Count < steps.Count)
        {
            var row = StepRow.Create();
            _stepRows.Add(row);
            _stepsList.Children.Add(row.Root);
        }

        for (var i = 0; i < steps.Count; i++)
            _stepRows[i].Apply(steps[i]);
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
        if (_editing || _editor.IsVisible)
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

    /// <summary>Reusable checklist row visuals updated in place.</summary>
    private sealed class StepRow
    {
        private static readonly IBrush DoneBg = new SolidColorBrush(Color.Parse("#F0FBFF"));
        private static readonly IBrush OpenBg = new SolidColorBrush(Color.Parse("#FAF9FF"));
        private static readonly IBrush DoneBorder = new SolidColorBrush(Color.Parse("#CFFAFE"));
        private static readonly IBrush OpenBorder = new SolidColorBrush(Color.Parse("#EDE9FE"));
        private static readonly IBrush MarkOpenBorder = new SolidColorBrush(Color.Parse("#C4B5FD"));
        private static readonly IBrush MarkOpenBg = new SolidColorBrush(Color.Parse("#F8F6FF"));
        private static readonly BoxShadows DoneMarkShadow = BoxShadows.Parse("0 2 8 0 #337C4DFF");

        public Border Root { get; }
        private readonly Border _mark;
        private readonly TextBlock _markGlyph;
        private readonly TextBlock _label;
        private bool? _lastDone;
        private string? _lastText;

        private StepRow(Border root, Border mark, TextBlock markGlyph, TextBlock label)
        {
            Root = root;
            _mark = mark;
            _markGlyph = markGlyph;
            _label = label;
        }

        public static StepRow Create()
        {
            var markGlyph = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var mark = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(6),
                Child = markGlyph,
            };
            var label = new TextBlock
            {
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var root = new Border
            {
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
            return new StepRow(root, mark, markGlyph, label);
        }

        public void Apply(PlanStep step)
        {
            if (_lastDone == step.Done && _lastText == step.Text) return;
            _lastDone = step.Done;
            _lastText = step.Text;

            var done = step.Done;
            _mark.BorderThickness = new Thickness(done ? 0 : 1.5);
            _mark.BorderBrush = done ? null : MarkOpenBorder;
            _mark.Background = done ? LumaTheme.CreateAccentGradient() : MarkOpenBg;
            _mark.BoxShadow = done ? DoneMarkShadow : default;
            _markGlyph.Text = done ? "✓" : "";

            _label.Text = step.Text;
            _label.FontWeight = done ? FontWeight.Normal : FontWeight.Medium;
            _label.Foreground = done ? LumaTheme.TextMutedBrush : LumaTheme.TextBodyBrush;
            _label.TextDecorations = done ? TextDecorations.Strikethrough : null;
            _label.Opacity = done ? 0.78 : 1;

            Root.Background = done ? DoneBg : OpenBg;
            Root.BorderBrush = done ? DoneBorder : OpenBorder;
        }
    }
}
