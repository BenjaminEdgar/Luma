using System.Text;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.Tests;

public sealed class TaskServicesTests
{
    [Theory]
    [InlineData("Reply to this Outlook email", TaskKind.Email)]
    [InlineData("Fix this bug and show me the git diff", TaskKind.Code)]
    [InlineData("Investigate this complex project step by step", TaskKind.Generic)]
    [InlineData("What does this button do?", TaskKind.Chat)]
    [InlineData("Run this command in the terminal", TaskKind.Shell)]
    [InlineData("Draft a reply for this web form", TaskKind.Browser)]
    public void RouterClassifiesExpectedWorkflow(string prompt, TaskKind expected) =>
        Assert.Equal(expected, TaskRouter.Classify(prompt));

    [Fact]
    public void CodeResponseExtractsDiffArtifact()
    {
        var raw = "I changed the null handling.\n```diff\ndiff --git a/a.cs b/a.cs\n--- a/a.cs\n+++ b/a.cs\n@@ -1 +1 @@\n-old\n+new\n```";
        var parsed = TaskResponseParser.Parse(raw, TaskKind.Code);
        Assert.Equal("I changed the null handling.", parsed.Text);
        Assert.Contains("diff --git a/a.cs b/a.cs", parsed.Artifact);
        Assert.Null(parsed.Question);
    }

    [Fact]
    public void EmailResponseExtractsDraft()
    {
        var parsed = TaskResponseParser.Parse("Ready for review.\nDRAFT:\nThanks for the update.", TaskKind.Email);
        Assert.Equal("Ready for review.", parsed.Text);
        Assert.Equal("Thanks for the update.", parsed.Artifact);
    }

    [Fact]
    public void BrowserResponseExtractsDraft()
    {
        var parsed = TaskResponseParser.Parse("Ready for review.\nDRAFT:\nThanks for your comment!", TaskKind.Browser);
        Assert.Equal("Ready for review.", parsed.Text);
        Assert.Equal("Thanks for your comment!", parsed.Artifact);
    }

    [Fact]
    public void ShellResponseExtractsCommandArtifact()
    {
        var raw = "This lists the files.\n```bash\nls -la\n```";
        var parsed = TaskResponseParser.Parse(raw, TaskKind.Shell);
        Assert.Equal("This lists the files.", parsed.Text);
        Assert.Equal("ls -la", parsed.Artifact);
        Assert.Null(parsed.Question);
    }

    [Fact]
    public void TaskResponsePreservesClarifyingQuestion()
    {
        var parsed = TaskResponseParser.Parse("I can prepare this.\nASK_USER: What tone should I use?", TaskKind.Email);
        Assert.Equal("What tone should I use?", parsed.Question);
        Assert.Null(parsed.Artifact);
    }

    [Fact]
    public async Task DiffValidationRejectsTraversalBeforeInvokingGit()
    {
        const string diff = "diff --git a/../secret.txt b/../secret.txt\n--- a/../secret.txt\n+++ b/../secret.txt\n@@ -1 +1 @@\n-old\n+new";
        var result = await new GitService().ValidateDiffAsync("C:\\does-not-need-to-exist", diff,
            new HashSet<string>(), CancellationToken.None);
        Assert.False(result.IsValid);
        Assert.Contains("outside", result.Message);
    }

    [Fact]
    public void TextSanitizerCleansCommonMojibake()
    {
        var broken = "caf\u00C3\u00A9";
        Assert.Equal("café", TextSanitizer.Clean(broken));
    }

    [Fact]
    public async Task ListFilesExcludesGitignoredPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LumaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await RunGitAsync(tempDir, "init");
            await RunGitAsync(tempDir, "config user.email test@example.com");
            await RunGitAsync(tempDir, "config user.name Test");
            await File.WriteAllTextAsync(Path.Combine(tempDir, ".gitignore"), "ignored.txt\n");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "tracked.txt"), "hello");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ignored.txt"), "secret");

            var files = await new GitService().ListFilesAsync(tempDir, CancellationToken.None);

            Assert.Contains("tracked.txt", files);
            Assert.DoesNotContain("ignored.txt", files);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RevertDiffRestoresOriginalContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LumaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            const string original = "line1\nline2\nline3\n";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file.txt"), original);
            const string diff = "diff --git a/file.txt b/file.txt\n--- a/file.txt\n+++ b/file.txt\n@@ -1,3 +1,3 @@\n line1\n-line2\n+line2-changed\n line3\n";

            var git = new GitService();
            await git.ApplyDiffAsync(tempDir, diff, CancellationToken.None);
            Assert.Contains("line2-changed", await File.ReadAllTextAsync(Path.Combine(tempDir, "file.txt")));

            await git.RevertDiffAsync(tempDir, diff, CancellationToken.None);
            var restored = (await File.ReadAllTextAsync(Path.Combine(tempDir, "file.txt"))).Replace("\r\n", "\n");
            Assert.Equal(original, restored);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static async Task RunGitAsync(string workingDirectory, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        await process.WaitForExitAsync();
    }
}
