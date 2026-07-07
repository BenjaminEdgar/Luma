using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Luma.App.Models;

namespace Luma.App.Services;

public interface IAiClient
{
    /// <param name="onPartialText">Invoked with the accumulated answer text each time a streamed chunk arrives.</param>
    Task<string> AskAsync(AiRequest request, Action<string>? onPartialText, CancellationToken cancellationToken);
}
public interface IAiClientFactory { IAiClient Create(AiProvider provider); }

public sealed class AiClientFactory : IAiClientFactory
{
    public IAiClient Create(AiProvider provider) => provider == AiProvider.Claude ? new ClaudeClient() : new CodexClient();
}

public abstract class CliAiClient : IAiClient
{
    protected abstract string Command { get; }
    protected abstract void AddArguments(ProcessStartInfo startInfo, AiRequest request);

    public async Task<string> AskAsync(AiRequest request, Action<string>? onPartialText, CancellationToken cancellationToken)
    {
        var launch = ResolveCommand(Command) ?? throw new InvalidOperationException($"{Command} CLI was not found. Install it and sign in before using Luma.");
        var sessionDirectory = Path.Combine(Path.GetTempPath(), "Luma", $"session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionDirectory);
        try
        {
            var localRequest = request with
            {
                ImagePath = CopyIntoSession(request.ImagePath, sessionDirectory, "region.png"),
                ContextImagePath = CopyIntoSession(request.ContextImagePath, sessionDirectory, "fullscreen.png"),
            };
            var workingDirectory = localRequest.WorkingDirectory ?? sessionDirectory;
            var psi = BuildStartInfo(launch.Executable, workingDirectory);
            foreach (var argument in launch.PrefixArguments) psi.ArgumentList.Add(argument);
            AddArguments(psi, localRequest);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {Command}.");
            using var killRegistration = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            });
            try
            {
                await process.StandardInput.WriteAsync(BuildPrompt(localRequest));
                process.StandardInput.Close();
            }
            catch (IOException) { cancellationToken.ThrowIfCancellationRequested(); throw; }

            var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var streamed = new StringBuilder();
            var raw = new StringBuilder();
            string? final = null;
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                raw.Append(line).Append('\n');
                if (!TryReadStreamLine(line, out var delta, out var result)) continue;
                if (delta is not null)
                {
                    streamed.Append(delta);
                    onPartialText?.Invoke(streamed.ToString());
                }
                if (result is not null) final = result;
            }
            await process.WaitForExitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(FriendlyError(error, process.ExitCode));
            if (final is not null) return final;
            if (streamed.Length > 0) return streamed.ToString();
            return ParseOutput(raw.ToString());
        }
        finally { try { Directory.Delete(sessionDirectory, true); } catch { } }
    }

    /// <summary>Recognizes claude --output-format stream-json lines: text deltas inside
    /// stream_event/content_block_delta (ignoring thinking/signature deltas) and the final result event.</summary>
    protected static bool TryReadStreamLine(string line, out string? deltaText, out string? finalText)
    {
        deltaText = null;
        finalText = null;
        if (!line.StartsWith('{')) return false;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;
            switch (type.GetString())
            {
                case "stream_event":
                    if (root.TryGetProperty("event", out var evt) &&
                        evt.TryGetProperty("type", out var eventType) && eventType.ValueEquals("content_block_delta") &&
                        evt.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("type", out var deltaType) && deltaType.ValueEquals("text_delta") &&
                        delta.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        deltaText = text.GetString();
                    return true;
                case "result":
                    if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
                        finalText = result.GetString();
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException) { return false; }
    }

    protected virtual string ParseOutput(string output)
    {
        var results = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                CollectText(document.RootElement, results);
            }
            catch (JsonException) { if (!line.StartsWith('{')) results.Add(line.Trim()); }
        }
        return results.Count == 0 ? output.Trim() : results.Last();
    }

    private static void CollectText(JsonElement element, List<string> results)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.NameEquals("result") || property.NameEquals("text")) && property.Value.ValueKind == JsonValueKind.String)
                { var value = property.Value.GetString(); if (!string.IsNullOrWhiteSpace(value)) results.Add(value); }
                else CollectText(property.Value, results);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CollectText(child, results);
    }

    private static string? CopyIntoSession(string? source, string sessionDirectory, string name)
    {
        if (source is null) return null;
        var destination = Path.Combine(sessionDirectory, name);
        File.Copy(source, destination);
        return destination;
    }

    private static string BuildPrompt(AiRequest request)
    {
        var builder = new StringBuilder("You are helping inside Luma. Do not modify files or run commands.\n");
        builder.AppendLine(
            "If, and only if, giving a genuinely useful answer requires one specific piece of information you don't " +
            "already have (for example: the project directory or file path for a coding problem visible in the " +
            "screenshot, or the desired tone/recipient/key points for an email or message you're asked to draft), " +
            "ask for exactly that by ending your ENTIRE reply with a single line formatted exactly as: " +
            "ASK_USER: <your question>. Never use this for something already visible in the screenshot. Prefer " +
            "answering directly whenever you reasonably can, and ask at most one question.");
        switch (request.TaskKind)
        {
            case TaskKind.Email:
                builder.AppendLine("You are drafting an email reply. Ask one concise question at a time only when needed. When ready, briefly explain the draft, then put the complete reply after a line containing exactly DRAFT:. Never claim to send the email.");
                break;
            case TaskKind.Code:
                builder.AppendLine("You are preparing a code change for approval. Inspect the repository using read-only tools. Do not edit or execute anything. Ask one concise question at a time if needed. When ready, explain the change and include one complete unified patch in a ```diff fenced block. Paths must be repository-relative.");
                break;
            case TaskKind.Generic:
                builder.AppendLine("This is a complex task workspace. Work methodically, ask one concise question at a time when required, and finish with a useful deliverable.");
                break;
            case TaskKind.Shell:
                builder.AppendLine("You are proposing a shell command for approval. Do not execute anything yourself. Ask one concise question if needed. When ready, briefly explain what the command does, then include exactly one command in a fenced code block (```bash).");
                break;
            case TaskKind.Browser:
                builder.AppendLine("You are drafting a reply to paste into a web page (a form, comment box, or forum post). Ask one concise question only if needed. When ready, briefly explain the reply, then put the complete text after a line containing exactly DRAFT:.");
                break;
        }
        if (!string.IsNullOrWhiteSpace(request.TaskContext)) builder.AppendLine($"Task context:\n{request.TaskContext}");
        if (request.History.Count > 0)
        {
            builder.AppendLine("Conversation so far:");
            foreach (var item in request.History) builder.AppendLine($"{item.Role}: {TextSanitizer.Clean(item.Text)}");
        }
        if (request.ContextImagePath is not null) builder.AppendLine($"Full-screen screenshot for overall context: {request.ContextImagePath}");
        if (request.ImagePath is not null) builder.AppendLine($"Close-up of the specific region the user is asking about (their focus): {request.ImagePath}");
        builder.AppendLine($"User: {request.Question}");
        return builder.ToString();
    }

    private static ProcessStartInfo BuildStartInfo(string executable, string workingDirectory)
    {
        return new ProcessStartInfo(executable)
        { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    }

    private static (string Executable, string[] PrefixArguments)? ResolveCommand(string command)
    {
        var names = OperatingSystem.IsWindows() ? new[] { $"{command}.exe", $"{command}.cmd", command } : new[] { command };
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            foreach (var name in names) { var path = Path.Combine(folder.Trim('"'), name); if (File.Exists(path)) return ExpandWindowsShim(command, path); }
        var npm = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var npmShim = Path.Combine(npm, "npm", $"{command}.cmd");
        return File.Exists(npmShim) ? ExpandWindowsShim(command, npmShim) : null;
    }

    private static (string, string[]) ExpandWindowsShim(string command, string path)
    {
        if (!OperatingSystem.IsWindows() || !path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) return (path, []);
        var root = Path.GetDirectoryName(path)!;
        if (command == "codex")
        {
            var script = Path.Combine(root, "node_modules", "@openai", "codex", "bin", "codex.js");
            var node = Path.Combine(root, "node.exe");
            return (File.Exists(node) ? node : "node.exe", [script]);
        }
        var claude = Path.Combine(root, "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe");
        return (claude, []);
    }

    private static string FriendlyError(string error, int code)
    {
        if (error.Contains("auth", StringComparison.OrdinalIgnoreCase) || error.Contains("login", StringComparison.OrdinalIgnoreCase))
            return "The AI client is not authenticated. Open it in a terminal, sign in, then try again.";
        return string.IsNullOrWhiteSpace(error) ? $"The AI client exited with code {code}." : error.Trim();
    }
}

public sealed class CodexClient : CliAiClient
{
    protected override string Command => "codex";
    protected override void AddArguments(ProcessStartInfo psi, AiRequest request)
    {
        psi.ArgumentList.Add("exec"); psi.ArgumentList.Add("--ephemeral"); psi.ArgumentList.Add("--sandbox"); psi.ArgumentList.Add("read-only");
        psi.ArgumentList.Add("--skip-git-repo-check"); psi.ArgumentList.Add("--json");
        if (request.ContextImagePath is not null) { psi.ArgumentList.Add("--image"); psi.ArgumentList.Add(request.ContextImagePath); }
        if (request.ImagePath is not null) { psi.ArgumentList.Add("--image"); psi.ArgumentList.Add(request.ImagePath); }
        psi.ArgumentList.Add("-");
    }
}

public sealed class ClaudeClient : CliAiClient
{
    protected override string Command => "claude";
    protected override void AddArguments(ProcessStartInfo psi, AiRequest request)
    {
        psi.ArgumentList.Add("--print"); psi.ArgumentList.Add("--output-format"); psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--include-partial-messages"); psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--tools"); psi.ArgumentList.Add("Read"); psi.ArgumentList.Add("--permission-mode"); psi.ArgumentList.Add("dontAsk");
        psi.ArgumentList.Add("--no-session-persistence");
    }
}
