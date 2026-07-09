using System.Text;
using System.Text.RegularExpressions;

namespace Luma.App.Services;

/// <summary>Parses @path mentions and builds extra prompt context from clipboard + attached files.</summary>
public static partial class ContextAttachments
{
    [GeneratedRegex(@"@(""(?<q>[^""]+)""|(?<p>[^\s@]+))", RegexOptions.CultureInvariant)]
    private static partial Regex MentionPattern();

    /// <summary>Extracts @file or @"path with spaces" mentions from the user prompt.</summary>
    public static IReadOnlyList<string> ExtractMentions(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return [];
        var list = new List<string>();
        foreach (Match match in MentionPattern().Matches(prompt))
        {
            var value = match.Groups["q"].Success ? match.Groups["q"].Value : match.Groups["p"].Value;
            value = value.Trim().TrimEnd(',', '.', ';', ':', ')', ']');
            if (value.Length == 0) continue;
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
                list.Add(value);
        }
        return list;
    }

    /// <summary>Resolves a mention to an absolute path under <paramref name="workingDirectory"/> when possible.</summary>
    public static string? ResolvePath(string? workingDirectory, string mention)
    {
        if (string.IsNullOrWhiteSpace(mention)) return null;
        var candidate = mention.Trim().Trim('"');
        if (Path.IsPathRooted(candidate) && File.Exists(candidate)) return Path.GetFullPath(candidate);
        if (string.IsNullOrWhiteSpace(workingDirectory)) return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        var combined = Path.GetFullPath(Path.Combine(workingDirectory, candidate.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(combined)) return combined;
        // Also try as-is from cwd-relative mention without working dir prefix.
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    public static string? BuildTaskContext(
        string? clipboard,
        IReadOnlyList<string> attachedFilePaths,
        string? workingDirectory,
        string prompt,
        int maxCharsPerFile = 6000,
        int maxTotalChars = 24000)
    {
        var builder = new StringBuilder();
        var used = 0;

        if (!string.IsNullOrWhiteSpace(clipboard))
        {
            var clip = clipboard.Trim();
            if (clip.Length > 4000) clip = clip[..4000] + "\n...[clipboard trimmed]";
            builder.AppendLine("Clipboard context (user attached):");
            builder.AppendLine(clip);
            builder.AppendLine();
            used += clip.Length;
        }

        var paths = new List<string>();
        foreach (var path in attachedFilePaths)
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);

        foreach (var mention in ExtractMentions(prompt))
        {
            var resolved = ResolvePath(workingDirectory, mention);
            if (resolved is not null && !paths.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                paths.Add(resolved);
        }

        foreach (var path in paths)
        {
            if (used >= maxTotalChars) break;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                var label = workingDirectory is not null
                    ? Path.GetRelativePath(workingDirectory, path).Replace('\\', '/')
                    : path;
                builder.AppendLine($"Attached file: {label}");
                if (info.Length > maxCharsPerFile)
                {
                    using var reader = new StreamReader(path);
                    var buffer = new char[maxCharsPerFile];
                    var n = reader.Read(buffer, 0, buffer.Length);
                    builder.Append(buffer, 0, n);
                    builder.AppendLine();
                    builder.AppendLine("...[file trimmed]");
                    used += n;
                }
                else
                {
                    var text = File.ReadAllText(path);
                    builder.AppendLine(text);
                    used += text.Length;
                }
                builder.AppendLine();
            }
            catch { /* skip unreadable */ }
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
