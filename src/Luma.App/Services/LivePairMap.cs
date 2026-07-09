using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Luma.App.Services;

/// <summary>
/// One file in the live coding pair mini-map: path + change kind + add/delete heat bars.
/// </summary>
public sealed class LivePairFile : INotifyPropertyChanged
{
    private int _additions;
    private int _deletions;
    private double _addBarWidth;
    private double _delBarWidth;
    private bool _isFresh;
    private FileChangeKind _kind;

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

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Pure helpers for the live coding pair overlay: line heat from before/after text,
/// and merging workspace diffs into a bindable mini-map collection.
/// </summary>
public static class LivePairMap
{
    public const double MaxBarPixels = 72;

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
    /// Diffs the workspace against <paramref name="before"/> and computes heat per changed file.
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
            rows.Add(new LivePairScanRow(change.RelativePath, change.Kind, adds, dels));
        }
        return rows;
    }

    /// <summary>
    /// Updates <paramref name="target"/> in place so the UI does not thrash on every poll.
    /// Marks rows as fresh when heat increases or the path is new.
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
                entry.IsFresh = true;
                // Keep hotter files near the top for the mini-map.
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
    int Deletions);
