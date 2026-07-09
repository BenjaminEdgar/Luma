using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Luma.App.Services;

namespace Luma.App.Controls;

/// <summary>Renders a <see cref="DiffDocument"/> as per-file, per-hunk cards with checkboxes for
/// selective apply. Mutates the document's IsSelected flags directly and fires SelectionChanged
/// so the host can reconstitute a filtered patch via DiffDocument.BuildPatch().</summary>
public sealed class DiffView : ContentControl
{
    public static readonly StyledProperty<DiffDocument?> DocumentProperty =
        AvaloniaProperty.Register<DiffView, DiffDocument?>(nameof(Document));

    public event Action? SelectionChanged;

    private static readonly FontFamily Mono = new("Cascadia Mono,Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,monospace");
    private static readonly IBrush AddedBg = new SolidColorBrush(Color.Parse("#1E3B2A"));
    private static readonly IBrush AddedFg = new SolidColorBrush(Color.Parse("#FF8FE8AE"));
    private static readonly IBrush RemovedBg = new SolidColorBrush(Color.Parse("#3B1E22"));
    private static readonly IBrush RemovedFg = new SolidColorBrush(Color.Parse("#FFFF9AA6"));
    private static readonly IBrush ContextFg = new SolidColorBrush(Color.Parse("#99FFFFFF"));
    private static readonly IBrush MutedFg = new SolidColorBrush(Color.Parse("#99FFFFFF"));
    private static readonly IBrush CardBg = new SolidColorBrush(Color.Parse("#66090A10"));
    private static readonly IBrush CardBorder = new SolidColorBrush(Color.Parse("#22FFFFFF"));

    private readonly HashSet<DiffFile> _collapsed = [];

    static DiffView()
    {
        DocumentProperty.Changed.AddClassHandler<DiffView>((view, _) => view.Rebuild());
    }

    public DiffDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    private void Rebuild()
    {
        var document = Document;
        if (document is null || document.Files.Count == 0) { Content = null; return; }

        var root = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(Toolbar(document));
        foreach (var file in document.Files) root.Children.Add(FileCard(file));
        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 340,
            ClipToBounds = true,
        };
    }

    private Control Toolbar(DiffDocument document)
    {
        var selectAll = new Button { Content = "Select all", Padding = new Thickness(10, 5) };
        selectAll.Classes.Add("ghost");
        selectAll.Click += (_, _) => SetAll(document, true);

        var selectNone = new Button { Content = "Select none", Padding = new Thickness(10, 5) };
        selectNone.Classes.Add("ghost");
        selectNone.Click += (_, _) => SetAll(document, false);

        var summary = new TextBlock
        {
            Text = $"{document.Files.Count} files  +{document.TotalAdditions} -{document.TotalDeletions}",
            Foreground = MutedFg,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        grid.Children.Add(summary);
        grid.Children.Add(At(selectAll, 0, 1));
        grid.Children.Add(At(selectNone, 0, 2));
        return grid;
    }

    private void SetAll(DiffDocument document, bool selected)
    {
        foreach (var file in document.Files)
        {
            file.IsSelected = selected;
            foreach (var hunk in file.Hunks) hunk.IsSelected = selected;
        }
        Rebuild();
        SelectionChanged?.Invoke();
    }

    private Control FileCard(DiffFile file)
    {
        var hunkCount = file.Hunks.Count;
        var allSelected = hunkCount == 0 ? file.IsSelected : file.Hunks.All(h => h.IsSelected);
        var noneSelected = hunkCount != 0 && file.Hunks.All(h => !h.IsSelected);

        var fileCheck = new CheckBox
        {
            IsThreeState = true,
            IsChecked = hunkCount == 0 ? file.IsSelected : allSelected ? true : noneSelected ? false : null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        fileCheck.Click += (_, _) =>
        {
            var makeSelected = !(hunkCount == 0 ? file.IsSelected : allSelected);
            file.IsSelected = makeSelected;
            foreach (var hunk in file.Hunks) hunk.IsSelected = makeSelected;
            Rebuild();
            SelectionChanged?.Invoke();
        };

        var pathText = new TextBlock
        {
            Text = file.IsRename && file.OldPath != file.NewPath ? $"{file.OldPath} -> {file.NewPath}" : file.NewPath,
            FontFamily = Mono,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var badge = new TextBlock
        {
            Text = file.IsBinary ? "binary" : $"+{file.Additions} -{file.Deletions}",
            FontSize = 11,
            Foreground = MutedFg,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var expanded = !file.IsBinary && !_collapsed.Contains(file);
        var toggle = new Button
        {
            Content = expanded ? "▾" : "▸",
            Padding = new Thickness(6, 2),
            IsVisible = !file.IsBinary,
        };
        toggle.Classes.Add("ghost");
        toggle.Click += (_, _) =>
        {
            if (!_collapsed.Remove(file)) _collapsed.Add(file);
            Rebuild();
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        header.Children.Add(fileCheck);
        header.Children.Add(At(pathText, 0, 1));
        header.Children.Add(At(badge, 0, 2));
        header.Children.Add(At(toggle, 0, 3));

        var body = new StackPanel { Spacing = 6, Children = { header } };
        if (file.IsBinary)
            body.Children.Add(new TextBlock { Text = "Binary file changed", FontSize = 12, Foreground = MutedFg, Margin = new Thickness(28, 0, 0, 0) });
        else if (expanded)
            foreach (var hunk in file.Hunks) body.Children.Add(HunkCard(hunk));

        return new Border
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = body,
        };
    }

    private Control HunkCard(DiffHunk hunk)
    {
        var check = new CheckBox { IsChecked = hunk.IsSelected, VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) =>
        {
            hunk.IsSelected = !hunk.IsSelected;
            Rebuild();
            SelectionChanged?.Invoke();
        };
        var headerText = new TextBlock
        {
            Text = hunk.Header,
            FontFamily = Mono,
            FontSize = 11.5,
            Foreground = MutedFg,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.WrapWithOverflow,
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(28, 0, 0, 0),
            Children = { check, At(headerText, 0, 1) },
        };

        return new StackPanel
        {
            Spacing = 4,
            Opacity = hunk.IsSelected ? 1.0 : 0.45,
            Children = { header, HunkLines(hunk) },
        };
    }

    private Control HunkLines(DiffHunk hunk)
    {
        var panel = new StackPanel { Margin = new Thickness(28, 0, 0, 0) };
        foreach (var line in hunk.Lines)
        {
            if (line.Kind == DiffLineKind.NoNewlineMarker) continue;
            var (background, foreground) = line.Kind switch
            {
                DiffLineKind.Added => (AddedBg, AddedFg),
                DiffLineKind.Removed => (RemovedBg, RemovedFg),
                _ => ((IBrush)Brushes.Transparent, ContextFg),
            };
            panel.Children.Add(new Border
            {
                Background = background,
                Padding = new Thickness(8, 0),
                ClipToBounds = true,
                Child = new TextBlock
                {
                    Text = line.Text,
                    FontFamily = Mono,
                    FontSize = 12,
                    Foreground = foreground,
                    TextWrapping = TextWrapping.WrapWithOverflow,
                },
            });
        }
        return new Border
        {
            ClipToBounds = true,
            Child = panel,
        };
    }

    private static T At<T>(T control, int row, int column) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }
}
