using System.Diagnostics;

namespace Luma.App.Services;

/// <summary>
/// Reconstructs the PATH a Mac/Linux user actually has in their terminal.
///
/// GUI apps launched from Finder, Dock, or `open` inherit launchd's minimal PATH
/// (/usr/bin:/bin:/usr/sbin:/sbin) rather than the shell PATH set by ~/.zshrc / ~/.zprofile.
/// So CLIs installed under ~/.local/bin, Homebrew, mise, nvm, bun, etc. are invisible even
/// though they work in a terminal. We recover the real PATH once by asking the login shell,
/// and fall back to well-known install directories. Windows GUI apps already inherit the full
/// PATH from the registry, so this is a no-op there.
/// </summary>
public static class ShellEnvironment
{
    // Markers isolate the PATH from any banner/noise an interactive rc file prints.
    public const string SentinelStart = "__LUMA_PATH_START__";
    public const string SentinelEnd = "__LUMA_PATH_END__";

    private static readonly Lazy<string> Resolved = new(BuildResolvedPath);

    /// <summary>The process PATH augmented with the login shell's PATH and well-known CLI dirs.</summary>
    public static string ResolvedPath => Resolved.Value;

    /// <summary>Directories to search for a CLI, in priority order, de-duplicated.</summary>
    public static IEnumerable<string> SearchDirectories() =>
        ResolvedPath.Split(Path.PathSeparator)
            .Select(directory => directory.Trim().Trim('"'))
            .Where(directory => directory.Length > 0);

    private static string BuildResolvedPath()
    {
        var process = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (OperatingSystem.IsWindows()) return process;
        var login = TryReadLoginShellPath();
        var wellKnown = string.Join(Path.PathSeparator, WellKnownBinDirectories());
        return MergePathSegments([process, login, wellKnown]);
    }

    /// <summary>Joins PATH segments, splitting each on the path separator and dropping duplicates while keeping order.</summary>
    public static string MergePathSegments(IEnumerable<string?> segments)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var ordered = new List<string>();
        foreach (var segment in segments)
            foreach (var directory in (segment ?? string.Empty).Split(Path.PathSeparator))
            {
                var trimmed = directory.Trim().Trim('"');
                if (trimmed.Length > 0 && seen.Add(trimmed)) ordered.Add(trimmed);
            }
        return string.Join(Path.PathSeparator, ordered);
    }

    /// <summary>Standard CLI install locations that a login-shell probe may still miss (e.g. PATH set in a non-sourced file).</summary>
    public static IReadOnlyList<string> WellKnownBinDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string InHome(params string[] parts) => Path.Combine([home, .. parts]);
        return
        [
            InHome(".local", "bin"),
            InHome(".claude", "local"),   // claude's own installer target
            InHome(".grok", "bin"),       // Grok Build
            InHome(".bun", "bin"),
            InHome("Library", "pnpm"),
            InHome(".npm-global", "bin"),
            "/opt/homebrew/bin",          // Homebrew on Apple Silicon
            "/usr/local/bin",             // Homebrew on Intel / npm global default
        ];
    }

    /// <summary>Extracts the PATH the login shell printed between the sentinels.</summary>
    public static bool TryParseSentinel(string output, out string path)
    {
        path = string.Empty;
        var start = output.IndexOf(SentinelStart, StringComparison.Ordinal);
        if (start < 0) return false;
        start += SentinelStart.Length;
        var end = output.IndexOf(SentinelEnd, start, StringComparison.Ordinal);
        if (end < 0) return false;
        path = output[start..end];
        return true;
    }

    private static string TryReadLoginShellPath()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell) || !File.Exists(shell)) return string.Empty;

        // -l -i so both login (.zprofile) and interactive (.zshrc) config runs; fish needs its own PATH syntax.
        var isFish = Path.GetFileName(shell).Equals("fish", StringComparison.Ordinal);
        var pathExpression = isFish ? "(string join \":\" $PATH)" : "\"$PATH\"";
        var script = $"printf '{SentinelStart}%s{SentinelEnd}' {pathExpression}";

        try
        {
            var psi = new ProcessStartInfo(shell)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi);
            if (process is null) return string.Empty;
            process.StandardInput.Close(); // an interactive shell must not block waiting for input
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }
            return TryParseSentinel(stdout.GetAwaiter().GetResult(), out var path) ? path : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
