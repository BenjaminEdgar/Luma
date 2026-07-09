using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Luma.App.Services;

/// <summary>One line in the live unified-diff preview (add / del / context).</summary>
public sealed class LivePairPreviewLine
{
    public required DiffLineKind Kind { get; init; }
    public required string Text { get; init; }

    public bool IsAdded => Kind == DiffLineKind.Added;
    public bool IsRemoved => Kind == DiffLineKind.Removed;
    public bool IsContext => Kind == DiffLineKind.Context;

    /// <summary>Prefixed display text: +line / -line / ·line.</summary>
    public string Display => Kind switch
    {
        DiffLineKind.Added => "+" + Text,
        DiffLineKind.Removed => "-" + Text,
        _ => " " + Text,
    };
}

/// <summary>
/// One file in the live coding pair panel: path, heat stats, and a live unified-diff preview.
/// </summary>
public sealed class LivePairFile : INotifyPropertyChanged
{
    private int _additions;
    private int _deletions;
    private double _addBarWidth;
    private double _delBarWidth;
    private bool _isFresh;
    private bool _isActive;
    private FileChangeKind _kind;
    private string _previewSignature = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string RelativePath { get; init; }

    public FileChangeKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            Notify(nameof(Kind));
            Notify(nameof(KindLabel));
            Notify(nameof(Summary));
        }
    }

    public int Additions
    {
        get => _additions;
        private set { if (_additions != value) { _additions = value; Notify(); Notify(nameof(StatsLabel)); Notify(nameof(Summary)); } }
    }

    public int Deletions
    {
        get => _deletions;
        private set { if (_deletions != value) { _deletions = value; Notify(); Notify(nameof(StatsLabel)); Notify(nameof(Summary)); } }
    }

    /// <summary>Pixel width for the green add bar (0–72).</summary>
    public double AddBarWidth
    {
        get => _addBarWidth;
        private set { if (Math.Abs(_addBarWidth - value) > 0.1) { _addBarWidth = value; Notify(); } }
    }

    /// <summary>Pixel width for the red delete bar (0–72).</summary>
    public double DelBarWidth
    {
        get => _delBarWidth;
        private set { if (Math.Abs(_delBarWidth - value) > 0.1) { _delBarWidth = value; Notify(); } }
    }

    /// <summary>True when this file appeared or grew heat on the latest poll.</summary>
    public bool IsFresh
    {
        get => _isFresh;
        set { if (_isFresh != value) { _isFresh = value; Notify(); } }
    }

    /// <summary>True when this file is selected for the live line preview.</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; Notify(); } }
    }

    /// <summary>Live unified-diff lines for this file (capped).</summary>
    public ObservableCollection<LivePairPreviewLine> PreviewLines { get; } = [];

    public bool HasPreview => PreviewLines.Count > 0;

    public string KindLabel => Kind switch
    {
        FileChangeKind.Created => "+",
        FileChangeKind.Modified => "~",
        FileChangeKind.Deleted => "−",
        _ => "·",
    };

    public string DisplayName
    {
        get
        {
            var name = Path.GetFileName(RelativePath.Replace('\\', '/'));
            return string.IsNullOrEmpty(name) ? RelativePath : name;
        }
    }

    public string FolderHint
    {
        get
        {
            var dir = Path.GetDirectoryName(RelativePath.Replace('\\', '/'));
            return string.IsNullOrEmpty(dir) ? string.Empty : dir.Replace('\\', '/');
        }
    }

    public string StatsLabel => $"+{Additions} −{Deletions}";

    public string ChipLabel => $"{DisplayName}  {StatsLabel}";

    public string Summary => Kind switch
    {
        FileChangeKind.Created => $"Created {RelativePath}",
        FileChangeKind.Deleted => $"Deleted {RelativePath}",
        _ => $"Edited {RelativePath}",
    };

    public int HeatScore => Additions + Deletions;

    public void SetHeat(int additions, int deletions, int maxHeat)
    {
        Additions = Math.Max(0, additions);
        Deletions = Math.Max(0, deletions);
        var total = Math.Max(1, maxHeat);
        // Bars share 72px: proportional to this file's adds/dels vs max heat on the map.
        var scale = 72.0 / total;
        AddBarWidth = Math.Clamp(Additions * scale, Additions > 0 ? 4 : 0, 72);
        DelBarWidth = Math.Clamp(Deletions * scale, Deletions > 0 ? 4 : 0, 72);
    }

    /// <summary>Replaces preview lines only when the signature changes (avoids UI thrash).</summary>
    public void SetPreview(IReadOnlyList<LivePairPreviewLine> lines)
    {
        var signature = LivePairMap.PreviewSignature(lines);
        if (string.Equals(signature, _previewSignature, StringComparison.Ordinal)) return;
        _previewSignature = signature;
        PreviewLines.Clear();
        foreach (var line in lines) PreviewLines.Add(line);
        Notify(nameof(HasPreview));
        Notify(nameof(ChipLabel));
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Pure helpers for the live coding pair overlay: line heat, unified preview from
/// before/after text, and merging workspace diffs into a bindable collection.
/// </summary>
public static class LivePairMap
{
    public const double MaxBarPixels = 72;
    public const int MaxPreviewLines = 48;
    public const int ContextLines = 2;

    /// <summary>
    /// Bag-of-lines heat: common lines cancel; remainder are adds/deletes.
    /// Good enough for a live heatmap without a full Myers diff.
    /// </summary>
    public static (int Additions, int Deletions) MeasureHeat(string? before, string? after)
    {
        if (before is null && after is null) return (0, 0);
        if (before is null)
        {
            var lines = SplitLines(after!);
            return (lines.Length, 0);
        }
        if (after is null)
        {
            var lines = SplitLines(before);
            return (0, lines.Length);
        }

        var oldLines = SplitLines(before);
        var newLines = SplitLines(after);
        if (oldLines.Length == 0 && newLines.Length == 0) return (0, 0);

        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in oldLines)
        {
            remaining.TryGetValue(line, out var c);
            remaining[line] = c + 1;
        }

        var common = 0;
        foreach (var line in newLines)
        {
            if (!remaining.TryGetValue(line, out var c) || c <= 0) continue;
            remaining[line] = c - 1;
            common++;
        }

        return (newLines.Length - common, oldLines.Length - common);
    }

    /// <summary>
    /// Builds a compact unified-style preview: common prefix/suffix as context,
    /// middle block as removals then additions. Caps at <see cref="MaxPreviewLines"/>.
    /// </summary>
    public static IReadOnlyList<LivePairPreviewLine> BuildPreview(string? before, string? after, int maxLines = MaxPreviewLines)
    {
        maxLines = Math.Max(8, maxLines);
        var oldLines = before is null ? [] : SplitLines(before);
        var newLines = after is null ? [] : SplitLines(after);

        if (oldLines.Length == 0 && newLines.Length == 0) return [];

        if (oldLines.Length == 0)
            return Cap(newLines.Select(l => Line(DiffLineKind.Added, l)), maxLines);

        if (newLines.Length == 0)
            return Cap(oldLines.Select(l => Line(DiffLineKind.Removed, l)), maxLines);

        // Common prefix.
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length
               && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
            prefix++;

        // Common suffix (outside the prefix).
        var oldEnd = oldLines.Length - 1;
        var newEnd = newLines.Length - 1;
        while (oldEnd >= prefix && newEnd >= prefix
               && string.Equals(oldLines[oldEnd], newLines[newEnd], StringComparison.Ordinal))
        {
            oldEnd--;
            newEnd--;
        }

        var result = new List<LivePairPreviewLine>(maxLines + 4);

        // Leading context.
        var ctxStart = Math.Max(0, prefix - ContextLines);
        for (var i = ctxStart; i < prefix; i++)
            result.Add(Line(DiffLineKind.Context, oldLines[i]));

        // Removed block.
        for (var i = prefix; i <= oldEnd; i++)
            result.Add(Line(DiffLineKind.Removed, oldLines[i]));

        // Added block.
        for (var i = prefix; i <= newEnd; i++)
            result.Add(Line(DiffLineKind.Added, newLines[i]));

        // Trailing context.
        var suffixStart = oldEnd + 1;
        var suffixEnd = Math.Min(oldLines.Length - 1, suffixStart + ContextLines - 1);
        for (var i = suffixStart; i <= suffixEnd && i < oldLines.Length; i++)
            result.Add(Line(DiffLineKind.Context, oldLines[i]));

        if (result.Count == 0)
        {
            // Identical content (rare once auditor reports a change) — show a few context lines.
            var show = Math.Min(ContextLines * 2, oldLines.Length);
            for (var i = 0; i < show; i++)
                result.Add(Line(DiffLineKind.Context, oldLines[i]));
        }

        return Cap(result, maxLines);
    }

    public static string PreviewSignature(IReadOnlyList<LivePairPreviewLine> lines)
    {
        if (lines.Count == 0) return string.Empty;
        // Cheap content fingerprint so MergeInto can skip no-op updates.
        var hash = new System.Text.StringBuilder(lines.Count * 8);
        hash.Append(lines.Count).Append('|');
        foreach (var line in lines)
            hash.Append((int)line.Kind).Append(':').Append(line.Text.Length).Append(':')
                .Append(line.Text.Length > 0 ? line.Text[0] : '\0')
                .Append(line.Text.Length > 1 ? line.Text[^1] : '\0').Append(';');
        return hash.ToString();
    }

    /// <summary>
    /// Diffs the workspace against <paramref name="before"/> and computes heat + preview per file.
    /// </summary>
    public static IReadOnlyList<LivePairScanRow> Scan(string? root, WorkspaceSnapshot before)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        var changes = WorkspaceWriteAuditor.Diff(root, before);
        if (changes.Count == 0) return [];

        var rows = new List<LivePairScanRow>(changes.Count);
        foreach (var change in changes)
        {
            var after = change.Kind is FileChangeKind.Deleted
                ? null
                : ReadCurrent(root, change.RelativePath);
            var beforeText = change.Kind is FileChangeKind.Created
                ? null
                : change.PreviousContent;
            // Created with untracked previous, or modified with omitted content: still show kind.
            var (adds, dels) = MeasureHeat(beforeText, after);
            if (change.Kind is FileChangeKind.Created && adds == 0 && after is not null)
                adds = Math.Max(1, SplitLines(after).Length);
            if (change.Kind is FileChangeKind.Deleted && dels == 0)
                dels = Math.Max(1, beforeText is null ? 1 : SplitLines(beforeText).Length);
            var preview = BuildPreview(beforeText, after);
            rows.Add(new LivePairScanRow(change.RelativePath, change.Kind, adds, dels, preview));
        }
        return rows;
    }

    /// <summary>
    /// Updates <paramref name="target"/> in place so the UI does not thrash on every poll.
    /// Marks rows as fresh when heat increases or the path is new; refreshes previews.
    /// </summary>
    public static void MergeInto(ObservableCollection<LivePairFile> target, IReadOnlyList<LivePairScanRow> scan)
    {
        var maxHeat = Math.Max(1, scan.Count == 0 ? 1 : scan.Max(r => r.Additions + r.Deletions));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in scan)
        {
            seen.Add(row.RelativePath);
            var existing = target.FirstOrDefault(f =>
                string.Equals(f.RelativePath, row.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var entry = new LivePairFile { RelativePath = row.RelativePath, Kind = row.Kind };
                entry.SetHeat(row.Additions, row.Deletions, maxHeat);
                entry.SetPreview(row.Preview);
                entry.IsFresh = true;
                // Keep hotter files near the top for the file chip strip.
                var insertAt = 0;
                while (insertAt < target.Count && target[insertAt].HeatScore >= entry.HeatScore)
                    insertAt++;
                target.Insert(insertAt, entry);
            }
            else
            {
                var grew = row.Additions + row.Deletions > existing.HeatScore;
                existing.Kind = row.Kind;
                existing.SetHeat(row.Additions, row.Deletions, maxHeat);
                existing.SetPreview(row.Preview);
                existing.IsFresh = grew || existing.IsFresh;
            }
        }

        // Drop files that reverted to pre-turn content (agent rewrote then restored).
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(target[i].RelativePath))
                target.RemoveAt(i);
        }

        // Re-scale all bars against current max heat.
        maxHeat = Math.Max(1, target.Count == 0 ? 1 : target.Max(f => f.HeatScore));
        foreach (var file in target)
            file.SetHeat(file.Additions, file.Deletions, maxHeat);
    }

    /// <summary>Picks the best file to show in the live preview (freshest, then hottest).</summary>
    public static LivePairFile? PickActive(IReadOnlyList<LivePairFile> files, LivePairFile? current)
    {
        if (files.Count == 0) return null;
        if (current is not null && files.Any(f => ReferenceEquals(f, current) ||
                string.Equals(f.RelativePath, current.RelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            // Stick with the user's selection unless it vanished; auto-follow only when none set.
            var stillThere = files.FirstOrDefault(f =>
                string.Equals(f.RelativePath, current.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (stillThere is not null) return stillThere;
        }

        return files.FirstOrDefault(f => f.IsFresh && f.HasPreview)
            ?? files.FirstOrDefault(f => f.HasPreview)
            ?? files[0];
    }

    private static LivePairPreviewLine Line(DiffLineKind kind, string text) =>
        new() { Kind = kind, Text = Truncate(text) };

    private static string Truncate(string text) =>
        text.Length <= 200 ? text : text[..197] + "…";

    private static IReadOnlyList<LivePairPreviewLine> Cap(IEnumerable<LivePairPreviewLine> lines, int max)
    {
        var list = lines as IList<LivePairPreviewLine> ?? lines.ToList();
        if (list.Count <= max) return list is IReadOnlyList<LivePairPreviewLine> ro ? ro : list.ToList();

        // Prefer keeping both sides of the change: take first half + last half of the cap.
        var head = max * 2 / 3;
        var tail = max - head - 1;
        var result = new List<LivePairPreviewLine>(max);
        for (var i = 0; i < head; i++) result.Add(list[i]);
        result.Add(new LivePairPreviewLine { Kind = DiffLineKind.Context, Text = $"… {list.Count - max + 1} more lines …" });
        for (var i = list.Count - tail; i < list.Count; i++) result.Add(list[i]);
        return result;
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string? ReadCurrent(string root, string relativePath)
    {
        try
        {
            var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) return null;
            var info = new FileInfo(full);
            if (info.Length > WorkspaceWriteAuditor.MaxBytesPerFile) return null;
            return File.ReadAllText(full);
        }
        catch { return null; }
    }
}

public readonly record struct LivePairScanRow(
    string RelativePath,
    FileChangeKind Kind,
    int Additions,
    int Deletions,
    IReadOnlyList<LivePairPreviewLine> Preview);
