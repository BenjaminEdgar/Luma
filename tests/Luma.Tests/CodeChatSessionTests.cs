using System.Diagnostics;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.Tests;

public sealed class CodeChatSessionTests
{
    private const string ValidDiffResponse =
        "I changed line2.\n```diff\n" +
        "diff --git a/greeter.txt b/greeter.txt\n--- a/greeter.txt\n+++ b/greeter.txt\n" +
        "@@ -1,3 +1,3 @@\n line1\n-line2\n+line2-changed\n line3\n```";

    private const string InvalidDiffResponse =
        "I changed line2.\n```diff\n" +
        "diff --git a/greeter.txt b/greeter.txt\n--- a/greeter.txt\n+++ b/greeter.txt\n" +
        "@@ -1,3 +1,3 @@\n line1\n-wrongcontext\n+line2-changed\n line3\n```";

    private static async Task<string> CreateTempRepoAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LumaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await RunGitAsync(dir, "init");
        await RunGitAsync(dir, "config user.email test@example.com");
        await RunGitAsync(dir, "config user.name Test");
        await File.WriteAllTextAsync(Path.Combine(dir, "greeter.txt"), "line1\nline2\nline3\n");
        await RunGitAsync(dir, "add greeter.txt");
        await RunGitAsync(dir, "commit -m init");
        return dir;
    }

    private static async Task RunGitAsync(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
    }

    private static CodeChatSession CreateSession(string repository, FakeAiClient client, IShellService? shell = null) =>
        new(new ChatMessage("assistant", string.Empty), new FakeAiClientFactory(client), AiProvider.Claude,
            new GitService(), shell ?? new ShellService(), repository, null, null);

    [Fact]
    public async Task ValidDiffOnFirstTryEnablesApply()
    {
        var repo = await CreateTempRepoAsync();
        try
        {
            var client = new FakeAiClient(_ => ValidDiffResponse);
            var session = CreateSession(repo, client);

            await session.RunAsync("fix line2", CancellationToken.None);

            Assert.True(session.CanApply);
            Assert.NotNull(session.Document);
            Assert.Single(session.Document!.Files);
            Assert.Equal(1, client.CallCount);
        }
        finally { try { Directory.Delete(repo, true); } catch { } }
    }

    [Fact]
    public async Task NonGitFolderStillRunsAndLetsAgentReadContext()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LumaTests", "nongit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "notes.txt"), "hello from plain folder\n");
        try
        {
            string? seenContext = null;
            var client = new FakeAiClient(request =>
            {
                seenContext = request.TaskContext;
                return "Looks fine — no code change needed.";
            });
            var session = CreateSession(dir, client);

            await session.RunAsync("What is in this folder?", CancellationToken.None);

            Assert.Equal(1, client.CallCount);
            Assert.NotNull(seenContext);
            Assert.Contains("Project root:", seenContext);
            Assert.Contains("notes.txt", seenContext);
            Assert.Contains("non-git folder", seenContext, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Choose a valid Git repository", session.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task InvalidDiffRetriesUpToBoundThenBlocksWithoutApplying()
    {
        var repo = await CreateTempRepoAsync();
        try
        {
            var client = new FakeAiClient(_ => InvalidDiffResponse);
            var session = CreateSession(repo, client);

            await session.RunAsync("fix line2", CancellationToken.None);

            Assert.False(session.CanApply);
            Assert.Equal(1 + CodeChatSession.MaxAutoRetries, client.CallCount);
            var content = await File.ReadAllTextAsync(Path.Combine(repo, "greeter.txt"));
            Assert.Equal("line1\nline2\nline3\n", content.Replace("\r\n", "\n"));
        }
        finally { try { Directory.Delete(repo, true); } catch { } }
    }

    [Fact]
    public async Task ApplyOnlyRunsOnExplicitCall()
    {
        var repo = await CreateTempRepoAsync();
        try
        {
            var client = new FakeAiClient(_ => ValidDiffResponse);
            var session = CreateSession(repo, client);
            await session.RunAsync("fix line2", CancellationToken.None);

            var beforeApply = await File.ReadAllTextAsync(Path.Combine(repo, "greeter.txt"));
            Assert.DoesNotContain("line2-changed", beforeApply);

            await session.ApplyAsync(CancellationToken.None);

            var afterApply = await File.ReadAllTextAsync(Path.Combine(repo, "greeter.txt"));
            Assert.Contains("line2-changed", afterApply);
            Assert.True(session.CanVerify);
            Assert.False(session.CanApply);
        }
        finally { try { Directory.Delete(repo, true); } catch { } }
    }

    [Fact]
    public async Task DeselectingAllHunksDisablesApply()
    {
        var repo = await CreateTempRepoAsync();
        try
        {
            var client = new FakeAiClient(_ => ValidDiffResponse);
            var session = CreateSession(repo, client);
            await session.RunAsync("fix line2", CancellationToken.None);

            session.Document!.Files[0].Hunks[0].IsSelected = false;
            await session.OnSelectionChangedAsync(CancellationToken.None);

            Assert.False(session.CanApply);
            Assert.Contains("least one", session.StatusMessage);
        }
        finally { try { Directory.Delete(repo, true); } catch { } }
    }

    [Fact]
    public async Task VerifyFailureRevealsRevertAndRevertRestoresContent()
    {
        var repo = await CreateTempRepoAsync();
        try
        {
            var client = new FakeAiClient(_ => ValidDiffResponse);
            var session = CreateSession(repo, client);
            await session.RunAsync("fix line2", CancellationToken.None);
            await session.ApplyAsync(CancellationToken.None);

            session.VerifyCommand = OperatingSystem.IsWindows() ? "exit 1" : "false";
            await session.VerifyAsync(CancellationToken.None);
            Assert.True(session.CanRevert);

            await session.RevertAsync(CancellationToken.None);

            var restored = (await File.ReadAllTextAsync(Path.Combine(repo, "greeter.txt"))).Replace("\r\n", "\n");
            Assert.Equal("line1\nline2\nline3\n", restored);
            Assert.False(session.CanRevert);
            Assert.False(session.CanVerify);
            Assert.True(session.CanApply);
        }
        finally { try { Directory.Delete(repo, true); } catch { } }
    }

    [Fact]
    public async Task VerifySuccessDoesNotRevealRevert()
    {
        var repo = await CreateTempRepoAsync();
        try
        {
            var client = new FakeAiClient(_ => ValidDiffResponse);
            var session = CreateSession(repo, client);
            await session.RunAsync("fix line2", CancellationToken.None);
            await session.ApplyAsync(CancellationToken.None);

            session.VerifyCommand = OperatingSystem.IsWindows() ? "exit 0" : "true";
            await session.VerifyAsync(CancellationToken.None);

            Assert.False(session.CanRevert);
        }
        finally { try { Directory.Delete(repo, true); } catch { } }
    }

    private sealed class FakeAiClient(Func<AiRequest, string> respond) : IAiClient
    {
        public int CallCount { get; private set; }

        public Task<string> AskAsync(AiRequest request, Action<string>? onPartialText, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeAiClientFactory(IAiClient client) : IAiClientFactory
    {
        public IAiClient Create(AiProvider provider) => client;
    }
}
