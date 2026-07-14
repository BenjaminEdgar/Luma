using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Luma.App.Services;

namespace Luma.App.Controls;

/// <summary>Renders a useful subset of markdown (headings, lists, quotes, fenced code,
/// bold/italic/inline code/links) as native Avalonia controls with selectable text.</summary>
public sealed class MarkdownView : ContentControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private static readonly FontFamily Mono = new("Cascadia Mono,Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,monospace");
    // Theme-aware tokens (read LumaTheme live so Blue / Colorful stay coherent).
    private static IBrush InlineCodeBg => AccentA(0x28);
    private static IBrush InlineCodeFg => LumaTheme.TextBodyBrush;
    private static IBrush CodeBlockBg => LumaTheme.GlassFillBrush;
    private static IBrush CodeBlockHeaderBg => LumaTheme.GlassFillStrongBrush;
    private static IBrush CodeBlockBorder => LumaTheme.BorderAccentBrush;
    private static IBrush CodeBlockFg => LumaTheme.TextBrightBrush;
    private static IBrush HeadingFg => LumaTheme.TextBrightBrush;
    private static IBrush HeadingAccentFg => LumaTheme.AccentSoftBrush;
    private static IBrush MutedFg => LumaTheme.TextMutedBrush;
    private static IBrush LinkFg => LumaTheme.AccentSoftBrush;
    private static IBrush RuleBrush => AccentA(0x66);
    private static IBrush QuoteBar => AccentA(0xCC);
    private static IBrush QuoteBg => AccentA(0x18);
    private static IBrush TableHeaderBg => LumaTheme.GlassFillBrush;
    private static IBrush TableStripeBg => AccentA(0x12);
    private static IBrush TableLine => LumaTheme.BorderAccentBrush;
    private static IBrush TableBorder => LumaTheme.BorderAccentBrush;
    private static IBrush CheckFill => LumaTheme.AccentBrush;
    private static IBrush CheckRim => LumaTheme.BorderAccentBrush;
    private static IBrush BulletFg => LumaTheme.AccentBrush;

    private static IBrush AccentA(byte a)
    {
        var c = LumaTheme.AccentStart;
        return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
    }

    private static readonly TimeSpan RebuildThrottle = TimeSpan.FromMilliseconds(75);
    private DateTime _lastRebuild = DateTime.MinValue;
    private bool _rebuildQueued;

    static MarkdownView()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.ScheduleRebuild());
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Coalesces rapid updates (streamed deltas) into at most one rebuild per throttle window.</summary>
    private void ScheduleRebuild()
    {
        if (_rebuildQueued) return;
        var wait = RebuildThrottle - (DateTime.UtcNow - _lastRebuild);
        if (wait <= TimeSpan.Zero)
        {
            Rebuild();
            return;
        }
        _rebuildQueued = true;
        DispatcherTimer.RunOnce(() => { _rebuildQueued = false; Rebuild(); }, wait);
    }

    private void Rebuild()
    {
        _lastRebuild = DateTime.UtcNow;
        // Stretch so SelectableTextBlock receives a finite width and wraps inside chat bubbles.
        var panel = new StackPanel { Spacing = 7, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var text = TextSanitizer.Clean(Markdown);
        if (!string.IsNullOrWhiteSpace(text))
            foreach (var block in BuildBlocks(text.Replace("\r\n", "\n").Trim('\n')))
                panel.Children.Add(block);
        Content = panel;
    }

    private IEnumerable<Control> BuildBlocks(string text)
    {
        var lines = text.Split('\n');
        var paragraph = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```"))
            {
                foreach (var block in FlushParagraph(paragraph)) yield return block;
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                while (++i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    code.Add(lines[i]);
                yield return CodeBlock(string.Join('\n', code), language);
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                foreach (var block in FlushParagraph(paragraph)) yield return block;
                continue;
            }
            if (trimmed.StartsWith('#'))
            {
                var level = trimmed.TakeWhile(c => c == '#').Count();
                if (level <= 6 && trimmed.Length > level && trimmed[level] == ' ')
                {
                    foreach (var block in FlushParagraph(paragraph)) yield return block;
                    yield return Heading(trimmed[(level + 1)..].Trim(), level);
                    continue;
                }
            }
            if (trimmed.Length >= 3 && (trimmed.All(c => c == '-') || trimmed.All(c => c == '*') || trimmed.All(c => c == '_')))
            {
                foreach (var block in FlushParagraph(paragraph)) yield return block;
                yield return new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 3) };
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                foreach (var block in FlushParagraph(paragraph)) yield return block;
                var quote = new List<string> { trimmed.TrimStart('>').TrimStart() };
                while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('>'))
                    quote.Add(lines[++i].TrimStart().TrimStart('>').TrimStart());
                yield return BlockQuote(string.Join(' ', quote));
                continue;
            }
            if (trimmed.StartsWith('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                foreach (var block in FlushParagraph(paragraph)) yield return block;
                var headerCells = SplitRow(trimmed);
                var aligns = ParseAlignments(lines[i + 1], headerCells.Count);
                i++; // consume separator row
                var rows = new List<List<string>>();
                while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('|') && !IsTableSeparator(lines[i + 1]))
                    rows.Add(SplitRow(lines[++i]));
                yield return Table(headerCells, aligns, rows);
                continue;
            }
            if (TryParseListItem(line, out var indent, out var marker, out var content))
            {
                foreach (var block in FlushParagraph(paragraph)) yield return block;
                yield return ListItem(indent, marker, content);
                continue;
            }
            paragraph.Add(trimmed);
        }
        foreach (var block in FlushParagraph(paragraph)) yield return block;
    }

    private IEnumerable<Control> FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0) yield break;
        var block = TextBlock(13);
        for (var i = 0; i < paragraph.Count; i++)
        {
            if (i > 0) block.Inlines!.Add(new LineBreak());
            foreach (var inline in ParseInlines(paragraph[i])) block.Inlines!.Add(inline);
        }
        paragraph.Clear();
        yield return block;
    }

    private static bool TryParseListItem(string line, out int indent, out string marker, out string content)
    {
        indent = line.Length - line.TrimStart().Length;
        var trimmed = line.TrimStart();
        marker = "-";
        content = string.Empty;
        if (trimmed.Length > 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
        {
            content = trimmed[2..];
            return true;
        }
        var digits = trimmed.TakeWhile(char.IsDigit).Count();
        if (digits is > 0 and <= 3 && trimmed.Length > digits + 1 &&
            trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' ')
        {
            marker = trimmed[..digits] + ".";
            content = trimmed[(digits + 2)..];
            return true;
        }
        return false;
    }

    private static bool IsTableSeparator(string line)
    {
        var t = line.Trim();
        return t.Length >= 3 && t.Contains('-') && t.All(c => c is '|' or ':' or '-' or ' ');
    }

    /// <summary>Stand-in for escaped pipes while splitting table rows (never appears in chat text).</summary>
    private const char EscapedPipe = (char)1;

    /// <summary>Splits a pipe-table row into trimmed cells; honors escaped \| inside cells.</summary>
    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return t.Replace("\\|", EscapedPipe.ToString()).Split('|')
            .Select(cell => cell.Replace(EscapedPipe, '|').Trim()).ToList();
    }

    private static TextAlignment[] ParseAlignments(string separator, int columns)
    {
        var cells = SplitRow(separator);
        var aligns = new TextAlignment[Math.Max(1, Math.Max(columns, cells.Count))];
        for (var c = 0; c < aligns.Length; c++)
        {
            var spec = c < cells.Count ? cells[c] : string.Empty;
            var left = spec.StartsWith(':');
            var right = spec.EndsWith(':');
            aligns[c] = left && right ? TextAlignment.Center : right ? TextAlignment.Right : TextAlignment.Left;
        }
        return aligns;
    }

    private Control Table(List<string> header, TextAlignment[] aligns, List<List<string>> rows)
    {
        var cols = Math.Max(header.Count, rows.Count == 0 ? 1 : rows.Max(r => r.Count));
        var grid = new Grid();
        for (var c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r <= rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        void Cell(int r, int c, string content, bool isHeader)
        {
            var block = TextBlock(12.5);
            // Auto columns keep alignment honest; wide tables scroll sideways instead of squeezing.
            block.TextWrapping = TextWrapping.NoWrap;
            block.TextAlignment = aligns[Math.Min(c, aligns.Length - 1)];
            if (isHeader)
            {
                block.FontSize = 12;
                block.FontWeight = FontWeight.SemiBold;
                block.Foreground = HeadingFg;
            }
            foreach (var inline in ParseInlines(content)) block.Inlines!.Add(inline);
            var cell = new Border
            {
                Padding = new Thickness(11, 6, 11, 6),
                Background = isHeader ? TableHeaderBg : r % 2 == 0 ? TableStripeBg : Brushes.Transparent,
                BorderBrush = TableLine,
                BorderThickness = new Thickness(0, r == 0 ? 0 : 1, c == cols - 1 ? 0 : 1, 0),
                Child = block,
            };
            Grid.SetRow(cell, r);
            Grid.SetColumn(cell, c);
            grid.Children.Add(cell);
        }

        for (var c = 0; c < cols; c++) Cell(0, c, c < header.Count ? header[c] : string.Empty, true);
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < cols; c++)
                Cell(r + 1, c, c < rows[r].Count ? rows[r][c] : string.Empty, false);

        return new Border
        {
            BorderBrush = TableBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            BoxShadow = BoxShadows.Parse("0 2 8 0 #0C080F23"),
            Child = new ScrollViewer
            {
                Content = grid,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
    }

    private Control ListItem(int indent, string marker, string content)
    {
        Control markerControl;
        var isTask = marker == "-" && content.Length >= 4 && content[0] == '[' && content[2] == ']' &&
                     content[3] == ' ' && content[1] is ' ' or 'x' or 'X';
        if (isTask)
        {
            var done = content[1] is 'x' or 'X';
            content = content[4..];
            markerControl = TaskCheck(done);
        }
        else
        {
            markerControl = new TextBlock
            {
                Text = marker == "-" ? "•" : marker,
                FontSize = 13,
                Foreground = marker == "-" ? BulletFg : MutedFg,
                MinWidth = 16,
                Margin = new Thickness(0, 0, 4, 0),
            };
        }
        var body = TextBlock(13);
        foreach (var inline in ParseInlines(content)) body.Inlines!.Add(inline);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(4 + Math.Min(indent, 8) * 7, 0, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(body, 1);
        row.Children.Add(markerControl);
        row.Children.Add(body);
        return row;
    }

    /// <summary>GitHub task-list checkbox: violet coin when done, quiet rim when open.</summary>
    private static Control TaskCheck(bool done) => new Border
    {
        Width = 15,
        Height = 15,
        CornerRadius = new CornerRadius(4.5),
        Margin = new Thickness(0, 2.5, 7, 0),
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        Background = done ? CheckFill : Brushes.Transparent,
        BorderBrush = done ? CheckFill : CheckRim,
        BorderThickness = new Thickness(1.4),
        Child = done
            ? new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M2.5,7.5 L6,11 L12,3.5"),
                Stroke = Brushes.White,
                StrokeThickness = 1.8,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Width = 9,
                Height = 9,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            }
            : null,
    };

    private Control Heading(string text, int level)
    {
        var block = TextBlock(level switch { 1 => 17, 2 => 15.5, _ => 13.5 });
        block.FontWeight = level <= 2 ? FontWeight.Bold : FontWeight.SemiBold;
        block.Foreground = level <= 2 ? HeadingAccentFg : HeadingFg;
        block.Margin = new Thickness(0, level <= 2 ? 6 : 3, 0, 2);
        foreach (var inline in ParseInlines(text)) block.Inlines!.Add(inline);
        if (level > 1) return block;
        // H1: accent underline so headings read as signal chrome, not plain bold.
        var rule = new Border
        {
            Height = 1.5,
            Background = RuleBrush,
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 0, 2),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        return new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Children = { block, rule },
        };
    }

    private Control BlockQuote(string text)
    {
        var block = TextBlock(13);
        block.Foreground = MutedFg;
        foreach (var inline in ParseInlines(text)) block.Inlines!.Add(inline);
        return new Border
        {
            Background = QuoteBg,
            BorderBrush = QuoteBar,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Padding = new Thickness(12, 6, 10, 6),
            Child = block,
        };
    }

    private Control CodeBlock(string code, string language)
    {
        var body = new SelectableTextBlock
        {
            Text = code,
            FontFamily = Mono,
            FontSize = 12,
            LineHeight = 18,
            Foreground = CodeBlockFg,
            // Wrap long lines so code never forces the chat bubble wider than the panel.
            TextWrapping = TextWrapping.WrapWithOverflow,
        };
        var scroll = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 280,
        };
        var copy = new Button { Content = "copy", Classes = { "codecopy" } };
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await ClipboardExtensions.SetTextAsync(clipboard, code);
            copy.Content = "copied";
            DispatcherTimer.RunOnce(() => copy.Content = "copy", TimeSpan.FromSeconds(1.5));
        };
        var langLabel = new TextBlock
        {
            Text = string.IsNullOrEmpty(language) ? "code" : language,
            FontSize = 10.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeadingAccentFg,
            FontFamily = Mono,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.9,
        };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(copy, 1);
        headerGrid.Children.Add(langLabel);
        headerGrid.Children.Add(copy);
        var header = new Border
        {
            Background = CodeBlockHeaderBg,
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(10, 5, 8, 5),
            Margin = new Thickness(-10, -6, -10, 0),
            Child = headerGrid,
        };
        return new Border
        {
            Background = CodeBlockBg,
            BorderBrush = CodeBlockBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 6, 10, 10),
            ClipToBounds = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            BoxShadow = BoxShadows.Parse("0 3 10 0 #0E080F23, inset 0 1 0 0 #AAFFFFFF"),
            Child = new StackPanel { Spacing = 6, Children = { header, scroll } },
        };
    }

    private static SelectableTextBlock TextBlock(double fontSize) => new()
    {
        FontSize = fontSize,
        Foreground = LumaTheme.TextBodyBrush,
        // Break long URLs / tokens so bubbles stay within the panel.
        TextWrapping = TextWrapping.WrapWithOverflow,
        LineHeight = fontSize * 1.48,
        Inlines = [],
    };

    private static IEnumerable<Inline> ParseInlines(string text)
    {
        var result = new List<Inline>();
        var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length == 0) return;
            result.Add(new Run(plain.ToString()));
            plain.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    Flush();
                    result.Add(new Run(text[(i + 1)..end])
                    {
                        FontFamily = Mono,
                        FontSize = 12,
                        Background = InlineCodeBg,
                        Foreground = InlineCodeFg,
                    });
                    i = end + 1;
                    continue;
                }
            }
            else if (c is '*' or '_')
            {
                var doubled = i + 1 < text.Length && text[i + 1] == c;
                var delimiter = doubled ? new string(c, 2) : c.ToString();
                var end = text.IndexOf(delimiter, i + delimiter.Length, StringComparison.Ordinal);
                if (end > i + delimiter.Length - 1)
                {
                    var inner = text[(i + delimiter.Length)..end];
                    if (inner.Length > 0 && !char.IsWhiteSpace(inner[0]) && !char.IsWhiteSpace(inner[^1]))
                    {
                        Flush();
                        var span = new Span
                        {
                            FontWeight = doubled ? FontWeight.SemiBold : FontWeight.Normal,
                            FontStyle = doubled ? FontStyle.Normal : FontStyle.Italic,
                        };
                        foreach (var inline in ParseInlines(inner)) span.Inlines.Add(inline);
                        result.Add(span);
                        i = end + delimiter.Length;
                        continue;
                    }
                }
            }
            else if (c == '~' && i + 1 < text.Length && text[i + 1] == '~')
            {
                var end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end > i + 1)
                {
                    Flush();
                    var span = new Span { TextDecorations = TextDecorations.Strikethrough, Foreground = MutedFg };
                    foreach (var inline in ParseInlines(text[(i + 2)..end])) span.Inlines.Add(inline);
                    result.Add(span);
                    i = end + 2;
                    continue;
                }
            }
            else if (c == '[')
            {
                var close = text.IndexOf(']', i + 1);
                if (close > i && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var paren = text.IndexOf(')', close + 2);
                    if (paren > close + 1)
                    {
                        Flush();
                        var span = new Span { Foreground = LinkFg, TextDecorations = TextDecorations.Underline };
                        foreach (var inline in ParseInlines(text[(i + 1)..close])) span.Inlines.Add(inline);
                        result.Add(span);
                        i = paren + 1;
                        continue;
                    }
                }
            }
            plain.Append(c);
            i++;
        }
        Flush();
        return result;
    }
}
