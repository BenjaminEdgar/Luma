using System.Text;
using System.Text.RegularExpressions;

namespace Luma.App.Services;

public enum DiffLineKind { Context, Added, Removed, NoNewlineMarker }

public sealed class DiffLine
{
    public DiffLineKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
}

public sealed class DiffHunk
{
    /// <summary>The raw original "@@ -a,b +c,d @@ trailing" line, kept for display.</summary>
    public string Header { get; set; } = string.Empty;
    public int OldStart { get; set; }
    public int OldCount { get; set; }
    public int NewStart { get; set; }
    public int NewCount { get; set; }
    public List<DiffLine> Lines { get; } = [];
    public bool IsSelected { get; set; } = true;

    public int Additions => Lines.Count(l => l.Kind == DiffLineKind.Added);
    public int Deletions => Lines.Count(l => l.Kind == DiffLineKind.Removed);
}

public sealed class DiffFile
{
    public string OldPath { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsRename { get; set; }
    public bool IsBinary { get; set; }

    /// <summary>Raw lines from "diff --git" through the last line before the first hunk (or end of block for binary files), verbatim.</summary>
    public List<string> HeaderLines { get; } = [];
    public List<DiffHunk> Hunks { get; } = [];
    public bool IsSelected { get; set; } = true;

    public int Additions => Hunks.Sum(h => h.Additions);
    public int Deletions => Hunks.Sum(h => h.Deletions);
}

public sealed class DiffDocument
{
    public List<DiffFile> Files { get; } = [];

    public int TotalAdditions => Files.Sum(f => f.Additions);
    public int TotalDeletions => Files.Sum(f => f.Deletions);

    /// <summary>Reconstitutes a unified diff containing only currently-selected files/hunks.
    /// A file is included only when it is itself selected AND has at least one selected hunk
    /// (binary files, which have no hunks, are included whenever selected).</summary>
    public string BuildPatch()
    {
        var builder = new StringBuilder();
        foreach (var file in Files)
        {
            if (!file.IsSelected) continue;
            var selectedHunks = file.Hunks.Where(h => h.IsSelected).ToList();
            if (file.Hunks.Count > 0 && selectedHunks.Count == 0) continue;

            foreach (var line in file.HeaderLines) builder.Append(line).Append('\n');

            var offset = 0;
            foreach (var hunk in file.Hunks)
            {
                if (!hunk.IsSelected) { continue; }
                var newStart = hunk.OldStart + offset;
                builder.Append($"@@ -{hunk.OldStart},{hunk.OldCount} +{newStart},{hunk.NewCount} @@\n");
                foreach (var line in hunk.Lines) builder.Append(line.Text).Append('\n');
                offset += hunk.NewCount - hunk.OldCount;
            }
        }
        return builder.ToString();
    }
}

public static class DiffParser
{
    private static readonly Regex FileHeaderRegex = new(@"^diff --git a/(.+) b/(.+)$", RegexOptions.Multiline);
    private static readonly Regex HunkHeaderRegex = new(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");

    public static DiffDocument Parse(string unifiedDiff)
    {
        var document = new DiffDocument();
        var normalized = unifiedDiff.Replace("\r\n", "\n").Replace("\r", "\n");
        var matches = FileHeaderRegex.Matches(normalized);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : normalized.Length;
            document.Files.Add(ParseFile(normalized[start..end]));
        }
        return document;
    }

    private static DiffFile ParseFile(string block)
    {
        var lines = block.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];

        var headerMatch = FileHeaderRegex.Match(lines[0]);
        var file = new DiffFile
        {
            OldPath = NormalizePath(headerMatch.Groups[1].Value.Trim()),
            NewPath = NormalizePath(headerMatch.Groups[2].Value.Trim()),
        };

        var index = 0;
        while (index < lines.Length && !HunkHeaderRegex.IsMatch(lines[index]))
        {
            var line = lines[index];
            file.HeaderLines.Add(line);
            if (line.StartsWith("new file mode", StringComparison.Ordinal)) file.IsNew = true;
            else if (line.StartsWith("deleted file mode", StringComparison.Ordinal)) file.IsDeleted = true;
            else if (line.StartsWith("rename from ", StringComparison.Ordinal) || line.StartsWith("rename to ", StringComparison.Ordinal)) file.IsRename = true;
            else if (line.StartsWith("GIT binary patch", StringComparison.Ordinal) || line.Contains("Binary files ", StringComparison.Ordinal)) file.IsBinary = true;
            index++;
        }

        while (index < lines.Length)
        {
            var hunkMatch = HunkHeaderRegex.Match(lines[index]);
            if (!hunkMatch.Success) break;
            var hunk = new DiffHunk
            {
                Header = lines[index],
                OldStart = int.Parse(hunkMatch.Groups[1].Value),
                OldCount = hunkMatch.Groups[2].Success ? int.Parse(hunkMatch.Groups[2].Value) : 1,
                NewStart = int.Parse(hunkMatch.Groups[3].Value),
                NewCount = hunkMatch.Groups[4].Success ? int.Parse(hunkMatch.Groups[4].Value) : 1,
            };
            index++;
            while (index < lines.Length && !HunkHeaderRegex.IsMatch(lines[index]))
            {
                hunk.Lines.Add(new DiffLine { Kind = ClassifyLine(lines[index]), Text = lines[index] });
                index++;
            }
            file.Hunks.Add(hunk);
        }

        return file;
    }

    private static DiffLineKind ClassifyLine(string line)
    {
        if (line.StartsWith('\\')) return DiffLineKind.NoNewlineMarker;
        if (line.StartsWith('+')) return DiffLineKind.Added;
        if (line.StartsWith('-')) return DiffLineKind.Removed;
        return DiffLineKind.Context;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('"');
}
