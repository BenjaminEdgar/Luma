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

public sealed class AiClientFactory(IRunningOperationCoordinator? operations = null) : IAiClientFactory
{
    public IAiClient Create(AiProvider provider) => provider switch
    {
        AiProvider.Claude => new ClaudeClient(operations),
        AiProvider.Grok => new GrokClient(operations),
        _ => new CodexClient(operations)
    };
}

public abstract class CliAiClient : IAiClient
{
    private readonly IRunningOperationCoordinator? _operations;
    protected CliAiClient(IRunningOperationCoordinator? operations = null) => _operations = operations;
    protected abstract string Command { get; }
    protected virtual bool PromptViaStandardInput => true;
    protected abstract void AddArguments(ProcessStartInfo startInfo, AiRequest request, string prompt);

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
            var prompt = BuildPrompt(localRequest);
            AddArguments(psi, localRequest, prompt);
            using var operation = _operations?.Begin($"{Command} request", cancellationToken);
            var processToken = operation?.Token ?? cancellationToken;
            using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {Command}.");
            using var killRegistration = processToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            });
            try
            {
                if (PromptViaStandardInput) await process.StandardInput.WriteAsync(prompt);
                process.StandardInput.Close();
            }
            catch (IOException) { processToken.ThrowIfCancellationRequested(); throw; }

            var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var streamed = new StringBuilder();
            var raw = new StringBuilder();
            string? final = null;
            while (await process.StandardOutput.ReadLineAsync(processToken) is { } line)
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
            await process.WaitForExitAsync(processToken);
            processToken.ThrowIfCancellationRequested();
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

    protected static string BuildPrompt(AiRequest request)
    {
        var builder = new StringBuilder("You are helping inside Luma. Be concise. Do not modify files or run commands. Ask one concise question at a time only when needed.\n");
        var hasVisualContext = request.ImagePath is not null || request.ContextImagePath is not null;
        if (hasVisualContext && request.TaskKind is not (TaskKind.Suggest or TaskKind.FollowUp or TaskKind.Route))
        {
            builder.AppendLine(
                "Treat the supplied screenshot as primary evidence and inspect it before answering. " +
                "If a selected-region image is present, it is the user's main focus; use the full-screen image only to understand surrounding context. " +
                "Quote or transcribe visible text exactly when it matters. Clearly distinguish what is visibly present from what you infer. " +
                "Never invent text, values, controls, or states that are cropped, blurred, or unreadable. " +
                "Answer the user's actual intent first, then explain the visible evidence and the most useful next action. " +
                "If an unreadable visual detail is essential, ask one multiple-choice question offering a tighter selection or a best-effort answer.");
        }
        switch (request.TaskKind)
        {
            case TaskKind.Email:
                builder.AppendLine("Draft the email reply. When ready, put the complete reply after a line containing exactly DRAFT:. Never claim to send the email.");
                break;
            case TaskKind.Code:
                builder.AppendLine("Inspect the repository using read-only tools. Do not edit or execute anything. When ready, explain the change and include one complete unified patch in a ```diff fenced block. Paths must be repository-relative.");
                break;
            case TaskKind.Generic:
                builder.AppendLine("Work methodically and finish with a useful deliverable.");
                break;
            case TaskKind.Shell:
                builder.AppendLine("Propose exactly one shell command in a fenced code block (```bash). Do not execute anything yourself.");
                break;
            case TaskKind.Browser:
                builder.AppendLine("Draft the reply to paste into the page. When ready, put the complete text after a line containing exactly DRAFT:.");
                break;
            case TaskKind.Suggest:
                builder.AppendLine(
                    $"Suggest up to {AppSettings.Current.SuggestionCount} short action prompts (under nine words each) from the full-screen screenshot. " +
                    "Prefer verb-led prompts. Reply with only the suggestions, one per line.");
                break;
            case TaskKind.FollowUp:
                builder.AppendLine(
                    $"Suggest up to {AppSettings.Current.SuggestionCount} short replies the user is likely to send next. " +
                    "Prefer verb-led replies under nine words. Reply with only the replies, one per line.");
                break;
            case TaskKind.Route:
                builder.AppendLine("Classify the request as exactly one of CHAT, CODE, or COMMAND. Reply with one word only.");
                break;
        }
        if (!string.IsNullOrWhiteSpace(AppSettings.Current.AssistantMemory))
        {
            var memory = AppSettings.Current.AssistantMemory.Trim();
            var max = AppSettings.Current.AssistantMemoryCharacterLimit;
            if (max > 0 && memory.Length > max) memory = memory[..max] + " ...[trimmed]";
            builder.AppendLine($"Pinned memory:\n{memory}");
        }
        if (!string.IsNullOrWhiteSpace(request.TaskContext)) builder.AppendLine($"Context:\n{request.TaskContext}");
        if (request.History.Count > 0)
        {
            // Every call re-sends the conversation, so trim it to the configured token budget.
            var messageLimit = AppSettings.Current.HistoryMessageLimit;
            var characterLimit = AppSettings.Current.HistoryCharacterLimit;
            var omitted = messageLimit > 0 && request.History.Count > messageLimit;
            builder.AppendLine(omitted ? $"Recent conversation (latest {messageLimit}):" : "Conversation:");
            if (omitted)
            {
                var omittedItems = request.History.Take(request.History.Count - messageLimit).ToArray();
                builder.AppendLine($"Earlier context summary: {SummarizeHistory(omittedItems)}");
            }
            var items = omitted ? request.History.Skip(request.History.Count - messageLimit) : request.History;
            foreach (var item in items)
            {
                var text = TextSanitizer.Clean(item.Text);
                if (characterLimit > 0 && text.Length > characterLimit)
                    text = text[..characterLimit] + " ...[trimmed]";
                builder.AppendLine($"{ShortRole(item.Role)}: {text}");
            }
        }
        if (request.ContextImagePath is not null) builder.AppendLine($"Screen: {request.ContextImagePath}");
        if (request.ImagePath is not null) builder.AppendLine($"Focus: {request.ImagePath}");
        builder.AppendLine($"User: {request.Question}");
        return builder.ToString();
    }

    private static string ShortRole(string role)
    {
        if (role.StartsWith("user", StringComparison.OrdinalIgnoreCase)) return "U";
        if (role.StartsWith("assistant", StringComparison.OrdinalIgnoreCase)) return "A";
        return string.IsNullOrWhiteSpace(role) ? "?" : role[..1].ToUpperInvariant();
    }

    private static string SummarizeHistory(IReadOnlyList<ChatMessage> history)
    {
        if (history.Count == 0) return "none";
        var parts = new List<string>();
        foreach (var item in history.Take(4))
        {
            var text = TextSanitizer.Clean(item.Text).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Length > 90) text = text[..90].TrimEnd() + "…";
            parts.Add($"{ShortRole(item.Role)}:{text}");
        }
        if (history.Count > 4) parts.Add("...");
        return parts.Count == 0 ? "none" : string.Join(" | ", parts);
    }

    private static ProcessStartInfo BuildStartInfo(string executable, string workingDirectory)
    {
        return new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CreateNoWindow = true
        };
    }

    public static (string Executable, string[] PrefixArguments)? ResolveCommand(string command)
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

public sealed class CodexClient(IRunningOperationCoordinator? operations = null) : CliAiClient(operations)
{
    protected override string Command => "codex";
    protected override void AddArguments(ProcessStartInfo psi, AiRequest request, string prompt)
    {
        psi.ArgumentList.Add("exec"); psi.ArgumentList.Add("--ephemeral"); psi.ArgumentList.Add("--sandbox"); psi.ArgumentList.Add("read-only");
        psi.ArgumentList.Add("--skip-git-repo-check"); psi.ArgumentList.Add("--json");
        var hasImage = request.ContextImagePath is not null || request.ImagePath is not null;
        var model = request.TaskKind is TaskKind.Suggest or TaskKind.FollowUp or TaskKind.Route
            ? FirstNonBlank(AppSettings.Current.CodexSuggestionModel, hasImage ? AppSettings.Current.CodexImageModel : null)
            : hasImage
                ? FirstNonBlank(AppSettings.Current.CodexImageModel, AppSettings.Current.CodexChatModel)
                : AppSettings.Current.CodexChatModel;
        if (!string.IsNullOrWhiteSpace(model)) { psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(model.Trim()); }
        // Cheapest reasoning for the latency-sensitive suggestion garnish (user-overridable).
        var effort = AppSettings.Current.CodexSuggestionReasoningEffort;
        if (request.TaskKind is TaskKind.Suggest or TaskKind.FollowUp or TaskKind.Route && !string.IsNullOrWhiteSpace(effort))
        { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add($"model_reasoning_effort=\"{effort.Trim()}\""); }
        if (request.ContextImagePath is not null) { psi.ArgumentList.Add("--image"); psi.ArgumentList.Add(request.ContextImagePath); }
        if (request.ImagePath is not null) { psi.ArgumentList.Add("--image"); psi.ArgumentList.Add(request.ImagePath); }
        psi.ArgumentList.Add("-");
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "gpt-5.4-mini";
}

public sealed class ClaudeClient(IRunningOperationCoordinator? operations = null) : CliAiClient(operations)
{
    protected override string Command => "claude";
    protected override void AddArguments(ProcessStartInfo psi, AiRequest request, string prompt)
    {
        psi.ArgumentList.Add("--print"); psi.ArgumentList.Add("--output-format"); psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--include-partial-messages"); psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--tools"); psi.ArgumentList.Add("Read"); psi.ArgumentList.Add("--permission-mode"); psi.ArgumentList.Add("dontAsk");
        psi.ArgumentList.Add("--no-session-persistence");
        // Suggestion chips are a latency-sensitive garnish, so they default to Haiku (the fast,
        // cheap tier); chat uses the CLI's own default. Both are user-overridable in Settings.
        var model = request.TaskKind is TaskKind.Suggest or TaskKind.FollowUp or TaskKind.Route
            ? AppSettings.Current.ClaudeSuggestionModel
            : AppSettings.Current.ClaudeChatModel;
        if (!string.IsNullOrWhiteSpace(model)) { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model.Trim()); }
    }
}

public sealed class GrokClient(IRunningOperationCoordinator? operations = null) : CliAiClient(operations)
{
    protected override string Command => "grok";
    protected override bool PromptViaStandardInput => false;

    protected override void AddArguments(ProcessStartInfo psi, AiRequest request, string prompt)
    {
        psi.ArgumentList.Add("--single");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("plain");
        psi.ArgumentList.Add("--permission-mode");
        psi.ArgumentList.Add("plan");
        psi.ArgumentList.Add("--tools");
        psi.ArgumentList.Add("Read");
        psi.ArgumentList.Add("--disable-web-search");
        psi.ArgumentList.Add("--no-memory");
        psi.ArgumentList.Add("--no-subagents");
        var model = request.TaskKind is TaskKind.Suggest or TaskKind.FollowUp or TaskKind.Route
            ? AppSettings.Current.GrokSuggestionModel
            : AppSettings.Current.GrokChatModel;
        if (!string.IsNullOrWhiteSpace(model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(model.Trim());
        }
    }

    protected override string ParseOutput(string output) => output.Trim();
}
