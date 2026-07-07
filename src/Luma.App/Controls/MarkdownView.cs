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
    private static readonly IBrush InlineCodeBg = new SolidColorBrush(Color.Parse("#2BFFFFFF"));
    private static readonly IBrush InlineCodeFg = new SolidColorBrush(Color.Parse("#FFDCD4FF"));
    private static readonly IBrush CodeBlockBg = new SolidColorBrush(Color.Parse("#66090A10"));
    private static readonly IBrush CodeBlockBorder = new SolidColorBrush(Color.Parse("#22FFFFFF"));
    private static readonly IBrush CodeBlockFg = new SolidColorBrush(Color.Parse("#FFD9E2F2"));
    private static readonly IBrush MutedFg = new SolidColorBrush(Color.Parse("#99FFFFFF"));
    private static readonly IBrush LinkFg = new SolidColorBrush(Color.Parse("#FF9DB8FF"));
    private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#26FFFFFF"));
    private static readonly IBrush QuoteBar = new SolidColorBrush(Color.Parse("#668A7CFF"));

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
        var panel = new StackPanel { Spacing = 7 };
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

    private Control ListItem(int indent, string marker, string content)
    {
        var markerBlock = new TextBlock
        {
            Text = marker,
            FontSize = 13,
            Foreground = MutedFg,
            MinWidth = 16,
            Margin = new Thickness(0, 0, 4, 0),
        };
        var body = TextBlock(13);
        foreach (var inline in ParseInlines(content)) body.Inlines!.Add(inline);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(4 + Math.Min(indent, 8) * 7, 0, 0, 0),
        };
        Grid.SetColumn(body, 1);
        row.Children.Add(markerBlock);
        row.Children.Add(body);
        return row;
    }

    private Control Heading(string text, int level)
    {
        var block = TextBlock(level switch { 1 => 16.5, 2 => 15, _ => 13.5 });
        block.FontWeight = FontWeight.SemiBold;
        block.Margin = new Thickness(0, level <= 2 ? 4 : 2, 0, 0);
        foreach (var inline in ParseInlines(text)) block.Inlines!.Add(inline);
        return block;
    }

    private Control BlockQuote(string text)
    {
        var block = TextBlock(13);
        block.Foreground = MutedFg;
        foreach (var inline in ParseInlines(text)) block.Inlines!.Add(inline);
        return new Border
        {
            BorderBrush = QuoteBar,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
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
            TextWrapping = TextWrapping.NoWrap,
        };
        var scroll = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
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
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(language) ? "code" : language,
            FontSize = 10.5,
            Foreground = MutedFg,
            FontFamily = Mono,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        return new Border
        {
            Background = CodeBlockBg,
            BorderBrush = CodeBlockBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 6, 10, 9),
            Child = new StackPanel { Spacing = 4, Children = { header, scroll } },
        };
    }

    private static SelectableTextBlock TextBlock(double fontSize) => new()
    {
        FontSize = fontSize,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = fontSize * 1.45,
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
