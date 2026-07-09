namespace Luma.App.Services;

public enum FileChangeKind { Created, Modified, Deleted }

/// <summary>One project file the agent created, edited, or deleted during a turn.</summary>
public sealed class FileChangeRecord : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isUndone;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public required string RelativePath { get; init; }
    public required FileChangeKind Kind { get; init; }
    /// <summary>Content before the turn (null when the file was created). Too-large files omit content.</summary>
    public string? PreviousContent { get; init; }
    public bool ContentTracked { get; init; } = true;

    public bool IsUndone
    {
        get => _isUndone;
        private set
        {
            if (_isUndone == value) return;
            _isUndone = value;
            PropertyChanged?.Invoke(this, new(nameof(IsUndone)));
            PropertyChanged?.Invoke(this, new(nameof(CanUndo)));
            PropertyChanged?.Invoke(this, new(nameof(Summary)));
        }
    }

    public bool CanUndo => !IsUndone && ContentTracked &&
        (Kind is FileChangeKind.Modified or FileChangeKind.Deleted && PreviousContent is not null
         || Kind is FileChangeKind.Created);

    public string Summary => IsUndone
        ? $"Undone · {RelativePath}"
        : Kind switch
        {
            FileChangeKind.Created => $"Created {RelativePath}",
            FileChangeKind.Modified => $"Edited {RelativePath}",
            FileChangeKind.Deleted => $"Deleted {RelativePath}",
            _ => RelativePath,
        };

    public string KindLabel => Kind switch
    {
        FileChangeKind.Created => "+",
        FileChangeKind.Modified => "~",
        FileChangeKind.Deleted => "−",
        _ => "·",
    };

    public void MarkUndone() => IsUndone = true;
}

/// <summary>Point-in-time map of text files under a project root (for agent write auditing).</summary>
public sealed class WorkspaceSnapshot
{
    public Dictionary<string, string?> Contents { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Snapshots a project folder before an agent turn and diffs afterward so Luma can list
/// writes and offer one-click undo for tracked text files.
/// </summary>
public static class WorkspaceWriteAuditor
{
    public const int MaxFiles = 500;
    public const int MaxBytesPerFile = 256_000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".slnx", ".json", ".md", ".txt", ".xml", ".axaml", ".xaml",
        ".js", ".ts", ".tsx", ".jsx", ".css", ".scss", ".html", ".htm", ".py", ".rb", ".go",
        ".rs", ".java", ".kt", ".swift", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".config",
        ".sh", ".ps1", ".bat", ".cmd", ".sql", ".graphql", ".vue", ".svelte", ".php", ".r",
        ".dockerfile", ".gitignore", ".editorconfig", ".env", ".props", ".targets",
    };

    public static WorkspaceSnapshot Capture(string? root)
    {
        var snap = new WorkspaceSnapshot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return snap;

        foreach (var relative in WorkspaceFileListing.ListFiles(root, MaxFiles))
        {
            if (!IsTextLike(relative)) continue;
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            snap.Contents[Normalize(relative)] = ReadTracked(full);
        }
        return snap;
    }

    public static IReadOnlyList<FileChangeRecord> Diff(string? root, WorkspaceSnapshot before)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        var after = Capture(root);
        var changes = new List<FileChangeRecord>();
        var allKeys = new HashSet<string>(before.Contents.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in after.Contents.Keys) allKeys.Add(key);

        foreach (var key in allKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var had = before.Contents.TryGetValue(key, out var prev);
            var has = after.Contents.TryGetValue(key, out var next);
            if (had && has)
            {
                if (string.Equals(prev, next, StringComparison.Ordinal)) continue;
                // Both null content => size-skipped both sides; still detect via file existence mtime? skip.
                if (prev is null && next is null) continue;
                changes.Add(new FileChangeRecord
                {
                    RelativePath = key,
                    Kind = FileChangeKind.Modified,
                    PreviousContent = prev,
                    ContentTracked = prev is not null,
                });
            }
            else if (!had && has)
            {
                changes.Add(new FileChangeRecord
                {
                    RelativePath = key,
                    Kind = FileChangeKind.Created,
                    PreviousContent = null,
                    ContentTracked = true,
                });
            }
            else if (had && !has)
            {
                changes.Add(new FileChangeRecord
                {
                    RelativePath = key,
                    Kind = FileChangeKind.Deleted,
                    PreviousContent = prev,
                    ContentTracked = prev is not null,
                });
            }
        }
        return changes;
    }

    public static void Undo(string root, FileChangeRecord change)
    {
        if (!change.CanUndo) throw new InvalidOperationException("This change cannot be undone.");
        var full = Path.Combine(root, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        switch (change.Kind)
        {
            case FileChangeKind.Created:
                if (File.Exists(full)) File.Delete(full);
                break;
            case FileChangeKind.Modified:
            case FileChangeKind.Deleted:
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, change.PreviousContent ?? string.Empty);
                break;
        }
        change.MarkUndone();
    }

    private static string? ReadTracked(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length > MaxBytesPerFile) return null;
            // Skip obvious binary by NUL check on first chunk.
            using var stream = File.OpenRead(fullPath);
            Span<byte> buffer = stackalloc byte[512];
            var read = stream.Read(buffer);
            if (buffer[..read].IndexOf((byte)0) >= 0) return null;
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch { return null; }
    }

    private static bool IsTextLike(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Makefile", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
            return true;
        return TextExtensions.Contains(Path.GetExtension(relativePath));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
