using Luma.App.Models;
using Luma.App.Services;

namespace Luma.Tests;

public sealed class PromptImproveTests
{
    [Fact]
    public void BuildRequestIncludesDraftAndRewriteRules()
    {
        var request = PromptImprove.BuildRequest("fix the button");
        Assert.Contains("Draft:\nfix the button", request);
        Assert.Contains("Do not answer the prompt", request);
        Assert.Contains("only the improved prompt text", request);
    }

    [Theory]
    [InlineData("Make the login form validate emails", "Make the login form validate emails")]
    [InlineData("\"Make the login form validate emails\"", "Make the login form validate emails")]
    [InlineData("Improved prompt: fix the flaky test", "fix the flaky test")]
    [InlineData("```\nRefactor DiffModel.cs for clarity\n```", "Refactor DiffModel.cs for clarity")]
    public void ParseStripsCommonGarnish(string raw, string expected)
    {
        Assert.Equal(expected, PromptImprove.Parse(raw));
    }

    [Fact]
    public void ParseReturnsNullForBlank()
    {
        Assert.Null(PromptImprove.Parse("   "));
        Assert.Null(PromptImprove.Parse(null));
    }

    [Fact]
    public void SystemPromptForImproveIsRewriteOnlyAndLightweight()
    {
        var prompt = TestClient.Prompt(new AiRequest(PromptImprove.BuildRequest("help with this"), null, null, [])
        {
            TaskKind = TaskKind.ImprovePrompt,
            WorkingDirectory = @"C:\LMLB",
        });
        Assert.Contains("Rewrite the draft prompt only", prompt);
        Assert.DoesNotContain("Project directory (working root)", prompt);
        Assert.DoesNotContain("NEED_SCREEN", prompt);
    }

    [Fact]
    public void ComposePlusMenuWiresImprovePrompt()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("Improve prompt", xaml);
        Assert.Contains("ImprovePromptCommand", xaml);

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("ImprovePromptAsync", vm);
        Assert.Contains("TaskKind.ImprovePrompt", vm);
    }

    private sealed class TestClient : CliAiClient
    {
        protected override string Command => "unused";
        protected override void AddArguments(System.Diagnostics.ProcessStartInfo startInfo, AiRequest request, string prompt, string sessionDirectory) { }
        public static string Prompt(AiRequest request) => BuildPrompt(request);
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
        throw new FileNotFoundException(relativePath);
    }
}
