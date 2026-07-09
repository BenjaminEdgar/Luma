using System.Text;
using System.Text.Json;

namespace Luma.App.Services;

public static class RepoContextFormatter
{
    /// <summary>Formats a sorted, capped file listing for inclusion in an AI prompt.
    /// Caps both entry count and total character length so a huge repo doesn't blow up the prompt.</summary>
    public static string BuildFileListSummary(IReadOnlyList<string> paths, int maxEntries = 400, int maxChars = 8000)
    {
        if (paths.Count == 0) return "(no files found)";

        var sorted = paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var builder = new StringBuilder();
        var shown = 0;
        foreach (var path in sorted)
        {
            var candidate = path + "\n";
            if (shown >= maxEntries || builder.Length + candidate.Length > maxChars) break;
            builder.Append(candidate);
            shown++;
        }
        if (shown < sorted.Count) builder.Append($"... and {sorted.Count - shown} more files\n");
        return builder.ToString().TrimEnd('\n');
    }
}

/// <summary>
/// Lists files under any folder for AI context — no Git repository required.
/// Skips common bulky/hidden trees so plain project folders stay usable.
/// </summary>
public static class WorkspaceFileListing
{
    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", ".vscode",
        "node_modules", "bin", "obj", "dist", "build", "out", "target",
        "__pycache__", ".venv", "venv", "packages", ".next", ".nuxt",
        "coverage", ".turbo", ".cache",
    };

    /// <summary>Returns repository-relative (forward-slash) paths under <paramref name="root"/>.</summary>
    public static IReadOnlyList<string> ListFiles(string root, int maxFiles = 2000)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];

        var results = new List<string>(Math.Min(maxFiles, 256));
        var rootFull = Path.GetFullPath(root);
        try
        {
            Walk(rootFull, rootFull, results, maxFiles);
        }
        catch
        {
            // Partial listing is still useful when permissions block a subtree.
        }
        return results;
    }

    private static void Walk(string rootFull, string current, List<string> results, int maxFiles)
    {
        if (results.Count >= maxFiles) return;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(current); }
        catch { return; }

        foreach (var file in files)
        {
            if (results.Count >= maxFiles) return;
            results.Add(ToRelative(rootFull, file));
        }

        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(current); }
        catch { return; }

        foreach (var dir in dirs)
        {
            if (results.Count >= maxFiles) return;
            var name = Path.GetFileName(dir);
            if (SkipDirectoryNames.Contains(name)) continue;
            Walk(rootFull, dir, results, maxFiles);
        }
    }

    private static string ToRelative(string rootFull, string fullPath)
    {
        var relative = Path.GetRelativePath(rootFull, fullPath);
        return relative.Replace('\\', '/');
    }
}

public static class TestCommandDetector
{
    /// <summary>Guesses a build/test command from repository markers, first match wins. Returns
    /// null when nothing recognizable is found; the caller shows an empty, user-editable textbox.</summary>
    public static string? Detect(string repositoryRoot)
    {
        if (File.Exists(Path.Combine(repositoryRoot, "Cargo.toml"))) return "cargo test";
        if (File.Exists(Path.Combine(repositoryRoot, "go.mod"))) return "go test ./...";

        var packageJson = Path.Combine(repositoryRoot, "package.json");
        if (File.Exists(packageJson))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
                if (document.RootElement.TryGetProperty("scripts", out var scripts))
                {
                    if (scripts.TryGetProperty("test", out _)) return "npm test";
                    if (scripts.TryGetProperty("build", out _)) return "npm run build";
                }
            }
            catch (JsonException) { }
            return null;
        }

        var hasPython = File.Exists(Path.Combine(repositoryRoot, "pyproject.toml")) || File.Exists(Path.Combine(repositoryRoot, "requirements.txt"));
        if (hasPython && (Directory.Exists(Path.Combine(repositoryRoot, "tests")) || Directory.Exists(Path.Combine(repositoryRoot, "test"))))
            return "pytest";

        var projectFiles = FindProjectFiles(repositoryRoot);
        if (projectFiles.Count > 0)
        {
            var hasTestProject = projectFiles.Any(p => Path.GetFileNameWithoutExtension(p).Contains("test", StringComparison.OrdinalIgnoreCase));
            return hasTestProject ? "dotnet test" : "dotnet build";
        }

        return null;
    }

    /// <summary>Searches the repository root and its immediate subdirectories only (top 2 levels),
    /// so detection stays cheap even in large repos.</summary>
    private static List<string> FindProjectFiles(string root)
    {
        var results = new List<string>();
        string[] patterns = ["*.sln", "*.slnx", "*.csproj"];
        foreach (var pattern in patterns) results.AddRange(SafeSearch(root, pattern));
        foreach (var directory in SafeDirectories(root))
            foreach (var pattern in patterns) results.AddRange(SafeSearch(directory, pattern));
        return results;
    }

    private static IEnumerable<string> SafeSearch(string directory, string pattern)
    {
        try { return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try { return Directory.GetDirectories(root); }
        catch { return []; }
    }
}
