using Luma.App.Models;
using Luma.App.Services;

namespace Luma.Tests;

/// <summary>Guards that provider CLIs allow read + write file tools on agent turns.</summary>
[Collection(EnvironmentMutationCollection.Name)]
public sealed class AgentReadToolsTests
{
    [Fact]
    public void ClaudeEnablesReadAndWriteToolsOnAgentTurns()
    {
        var source = ReadShipped("src/Luma.App/Services/AiClients.cs");
        Assert.Contains("Read,Glob,Grep,Write,Edit", source);
        Assert.Contains("permission-mode", source);
        Assert.Contains("dontAsk", source);
        // Suggestion garnish should not pay for tools.
        Assert.Contains("TaskKind.Suggest or TaskKind.FollowUp or TaskKind.Route or TaskKind.ImprovePrompt", source);
    }

    [Fact]
    public void GrokEnablesReadAndWriteToolsOnAgentTurns()
    {
        var source = ReadShipped("src/Luma.App/Services/AiClients.cs");
        Assert.Contains("read_file,grep,list_dir,search_replace", source);
        Assert.Contains("--always-approve", source);
        // Invalid tool id "write" breaks Grok agent build on current CLIs.
        Assert.DoesNotContain("search_replace,write", source);
    }

    [Fact]
    public void CodexUsesWorkspaceWriteSandbox()
    {
        var source = ReadShipped("src/Luma.App/Services/AiClients.cs");
        Assert.Contains("workspace-write", source);
        // codex exec has no --ask-for-approval flag; use -c approval_policy instead.
        Assert.DoesNotContain("Add(\"--ask-for-approval\")", source);
        Assert.Contains("approval_policy=\\\"never\\\"", source);
        Assert.Contains("Add(\"workspace-write\")", source);
    }

    [Fact]
    public void ChatTurnPassesWorkingDirectorySoAgentsCanTouchProjectFiles()
    {
        var source = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.Chat.cs");
        Assert.Contains("WorkingDirectory = WorkingDirectory", source);
        Assert.Contains("new AiRequest(prompt, provRegion, provContext, history)", source);
    }

    [Fact]
    public void SystemPromptAllowsFileWritesUnderProjectRoot()
    {
        var previous = AppSettings.Current;
        try
        {
            AppSettings.Current = new AppSettings { LeanChatMode = false };
            var prompt = TestClient.Prompt(new AiRequest("Add a comment to Program.cs", null, null, [])
            {
                TaskKind = TaskKind.Chat,
                WorkingDirectory = @"C:\LMLB",
            });
            Assert.Contains("create, and edit files", prompt);
            Assert.Contains("Project directory (working root): C:\\LMLB", prompt);
            Assert.DoesNotContain("Do not write, edit, or execute", prompt);
        }
        finally
        {
            AppSettings.Current = previous;
        }
    }

    private sealed class TestClient : CliAiClient
    {
        protected override string Command => "unused";
        protected override void AddArguments(System.Diagnostics.ProcessStartInfo startInfo, Luma.App.Models.AiRequest request, string prompt, string sessionDirectory) { }
        public static string Prompt(Luma.App.Models.AiRequest request) => BuildPrompt(request);
    }

    private static string ReadShipped(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            if (File.Exists(Path.Combine(dir.FullName, "Luma.slnx")))
            {
                var fromRoot = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(fromRoot)) return File.ReadAllText(fromRoot);
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate shipped source {relativePath}");
    }
}
