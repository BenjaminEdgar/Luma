using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Luma.App.Models;

namespace Luma.App.Services;

public static partial class TaskRouter
{
    public static TaskKind Classify(string prompt)
    {
        if (BrowserWords().IsMatch(prompt)) return TaskKind.Browser;
        if (EmailWords().IsMatch(prompt)) return TaskKind.Email;
        if (ShellWords().IsMatch(prompt)) return TaskKind.Shell;
        if (CodeWords().IsMatch(prompt)) return TaskKind.Code;
        if (GenericWords().IsMatch(prompt) || prompt.Length > 220) return TaskKind.Generic;
        return TaskKind.Chat;
    }

    [GeneratedRegex(@"\b(email|e-mail|outlook|reply|draft a message|respond to (him|her|them|this))\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailWords();
    [GeneratedRegex(@"\b(browser|webpage|web page|website|web form|comment box|forum post)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BrowserWords();
    [GeneratedRegex(@"\b(terminal|shell|command line|bash|powershell|run this command|execute this|npm install|pip install|cli command)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShellWords();
    [GeneratedRegex(@"\b(code|coding|bug|exception|stack trace|repository|repo|git|diff|patch|compile|test failure|refactor|implement)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CodeWords();
    [GeneratedRegex(@"\b(complex|research|analyse|analyze|investigate|project|work through|step by step|detailed plan)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GenericWords();
}

public sealed record TaskResponse(string Text, string? Question, string? Artifact, IReadOnlyList<string> Choices);

public static partial class TaskResponseParser
{
    public static TaskResponse Parse(string raw, TaskKind kind)
    {
        var clarification = ClarifyingQuestionParser.ExtractDetailed(TextSanitizer.Clean(raw));
        var text = clarification.Text;
        var question = clarification.Question;
        string? artifact = null;
        if (kind == TaskKind.Code)
        {
            var match = DiffFence().Match(text);
            if (match.Success)
            {
                artifact = NormalizeDiffTrailingNewline(TextSanitizer.Clean(match.Groups[1].Value.Trim()));
                text = text.Remove(match.Index, match.Length).Trim();
            }
            else
            {
                var start = text.IndexOf("diff --git ", StringComparison.Ordinal);
                if (start >= 0)
                {
                    artifact = NormalizeDiffTrailingNewline(TextSanitizer.Clean(text[start..].Trim()));
                    text = text[..start].Trim();
                }
            }
        }
        else if (kind is TaskKind.Email or TaskKind.Browser)
        {
            var match = DraftBlock().Match(text);
            if (match.Success)
            {
                artifact = TextSanitizer.Clean(match.Groups[1].Value.Trim());
                text = text.Remove(match.Index, match.Length).Trim();
            }
            else if (question is null)
            {
                artifact = TextSanitizer.Clean(text.Trim());
            }
        }
        else if (kind == TaskKind.Shell)
        {
            var match = ShellFence().Match(text);
            if (match.Success)
            {
                artifact = TextSanitizer.Clean(match.Groups[1].Value.Trim());
                text = text.Remove(match.Index, match.Length).Trim();
            }
        }
        else if (question is null)
        {
            artifact = TextSanitizer.Clean(text.Trim());
        }
        return new TaskResponse(TextSanitizer.Clean(text), question is null ? null : TextSanitizer.Clean(question), artifact, clarification.Choices);
    }

    /// <summary>A unified diff's last content line must be newline-terminated or git apply rejects
    /// it as "corrupt patch" - Trim() above strips that trailing newline, so restore it here.</summary>
    private static string NormalizeDiffTrailingNewline(string diff) => diff.Length == 0 || diff.EndsWith('\n') ? diff : diff + "\n";

    [GeneratedRegex(@"```diff\s*(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DiffFence();
    [GeneratedRegex(@"(?:^|\n)DRAFT:\s*(.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DraftBlock();
    [GeneratedRegex(@"```(?:bash|sh|shell|powershell)?\s*(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ShellFence();
}

public interface IOutlookService
{
    OutlookMessage ReadSelectedMessage();
    void OpenReplyDraft(string body);
}

public sealed record OutlookMessage(string Sender, string Subject, string Body);

public sealed class OutlookService : IOutlookService
{
    private dynamic? _source;

    public OutlookMessage ReadSelectedMessage()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Outlook automation is available on Windows only.");
        var type = Type.GetTypeFromProgID("Outlook.Application") ?? throw new InvalidOperationException("Classic Outlook is not installed.");
        dynamic app = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Outlook could not be started.");
        dynamic explorer = app.ActiveExplorer() ?? throw new InvalidOperationException("Open Outlook and select an email first.");
        dynamic selection = explorer.Selection;
        if ((int)selection.Count < 1) throw new InvalidOperationException("Select one email in Outlook, then try again.");
        _source = selection.Item(1);
        return new OutlookMessage(
            TextSanitizer.Clean((string)(_source.SenderName ?? "Unknown sender")),
            TextSanitizer.Clean((string)(_source.Subject ?? "(no subject)")),
            TextSanitizer.Clean((string)(_source.Body ?? string.Empty)));
    }

    public void OpenReplyDraft(string body)
    {
        if (_source is null) throw new InvalidOperationException("The original Outlook message is no longer available.");
        dynamic reply = _source.Reply();
        reply.Body = body.Trim() + "\r\n\r\n" + (string)(reply.Body ?? string.Empty);
        reply.Display(false);
    }
}

public sealed record GitRepositoryStatus(IReadOnlySet<string> ChangedPaths);
public sealed record DiffValidation(bool IsValid, string Message, IReadOnlyList<string> Paths, int Additions, int Deletions);

public interface IGitService
{
    Task<bool> IsRepositoryAsync(string path, CancellationToken token);
    Task<GitRepositoryStatus> GetStatusAsync(string path, CancellationToken token);
    Task<DiffValidation> ValidateDiffAsync(string repository, string diff, IReadOnlySet<string> protectedPaths, CancellationToken token);
    Task ApplyDiffAsync(string repository, string diff, CancellationToken token);
    Task<IReadOnlyList<string>> ListFilesAsync(string repository, CancellationToken token);
    Task RevertDiffAsync(string repository, string diff, CancellationToken token);
}

public sealed class GitService : IGitService
{
    public async Task<bool> IsRepositoryAsync(string path, CancellationToken token) =>
        Directory.Exists(path) && (await RunGitAsync(path, ["rev-parse", "--is-inside-work-tree"], null, token)).ExitCode == 0;

    public async Task<GitRepositoryStatus> GetStatusAsync(string path, CancellationToken token)
    {
        var result = await RunGitAsync(path, ["status", "--porcelain", "-z"], null, token);
        EnsureSuccess(result, "Could not read repository status");
        var paths = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length > 3 ? line[3..] : line)
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new GitRepositoryStatus(paths);
    }

    public async Task<DiffValidation> ValidateDiffAsync(string repository, string diff, IReadOnlySet<string> protectedPaths, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(diff) || !diff.Contains("diff --git ", StringComparison.Ordinal))
            return new(false, "The provider did not return a unified Git diff.", [], 0, 0);
        if (diff.Contains("GIT binary patch", StringComparison.OrdinalIgnoreCase))
            return new(false, "Binary patches are not supported.", [], 0, 0);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(diff, @"^diff --git a/(.+?) b/(.+?)$", RegexOptions.Multiline))
        {
            var path = NormalizePath(match.Groups[2].Value.Trim());
            if (Path.IsPathRooted(path) || path.Split('/').Contains(".."))
                return new(false, "The patch contains a path outside the repository.", [], 0, 0);
            paths.Add(path);
        }
        if (paths.Count == 0) return new(false, "No changed files were found in the diff.", [], 0, 0);

        var overlap = paths.FirstOrDefault(protectedPaths.Contains);
        if (overlap is not null)
            return new(false, $"The patch touches '{overlap}', which already has uncommitted changes.", paths.ToArray(), 0, 0);

        var check = await RunGitAsync(repository, ["apply", "--check", "-"], diff, token);
        if (check.ExitCode != 0)
            return new(false, string.IsNullOrWhiteSpace(check.Error) ? "git apply --check failed." : check.Error.Trim(), paths.ToArray(), 0, 0);

        var additions = diff.Split('\n').Count(l => l.StartsWith('+') && !l.StartsWith("+++"));
        var deletions = diff.Split('\n').Count(l => l.StartsWith('-') && !l.StartsWith("---"));
        return new(true, $"{paths.Count} files +{additions} -{deletions}", paths.ToArray(), additions, deletions);
    }

    public async Task ApplyDiffAsync(string repository, string diff, CancellationToken token)
    {
        var result = await RunGitAsync(repository, ["apply", "-"], diff, token);
        EnsureSuccess(result, "The patch could not be applied");
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string repository, CancellationToken token)
    {
        var result = await RunGitAsync(repository, ["ls-files", "--cached", "--others", "--exclude-standard", "-z"], null, token);
        EnsureSuccess(result, "Could not list repository files");
        return result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(NormalizePath).ToArray();
    }

    public async Task RevertDiffAsync(string repository, string diff, CancellationToken token)
    {
        var result = await RunGitAsync(repository, ["apply", "-R", "-"], diff, token);
        EnsureSuccess(result, "The patch could not be reverted");
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('"');
    private static void EnsureSuccess(ProcessResult result, string prefix)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException($"{prefix}: {result.Error.Trim()}");
    }

    private static async Task<ProcessResult> RunGitAsync(string directory, string[] arguments, string? input, CancellationToken token)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Git is not installed.");
        if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), token);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}

public sealed record ShellResult(int ExitCode, string Output, string Error);

public interface IShellService
{
    Task<ShellResult> RunAsync(string workingDirectory, string command, CancellationToken token);
}

public sealed class ShellService(IRunningOperationCoordinator? operations = null) : IShellService
{
    public async Task<ShellResult> RunAsync(string workingDirectory, string command, CancellationToken token)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        psi.WorkingDirectory = workingDirectory;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        using var operation = operations?.Begin("Shell command", token);
        var processToken = operation?.Token ?? token;
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start a shell.");
        using var killRegistration = processToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });
        var output = process.StandardOutput.ReadToEndAsync(processToken);
        var error = process.StandardError.ReadToEndAsync(processToken);
        await process.WaitForExitAsync(processToken);
        processToken.ThrowIfCancellationRequested();
        return new ShellResult(process.ExitCode, await output, await error);
    }
}
