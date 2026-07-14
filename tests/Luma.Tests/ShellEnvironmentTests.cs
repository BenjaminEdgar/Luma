using Luma.App.Services;

namespace Luma.Tests;

public sealed class ShellEnvironmentTests
{
    [Fact]
    public void MergeDropsDuplicatesAndPreservesFirstSeenOrder()
    {
        var merged = ShellEnvironment.MergePathSegments(
        [
            "/usr/bin:/bin",
            "/opt/homebrew/bin:/usr/bin", // /usr/bin is a duplicate
            "/opt/homebrew/bin",          // whole segment duplicate
        ]);

        Assert.Equal("/usr/bin:/bin:/opt/homebrew/bin", merged);
    }

    [Fact]
    public void MergeIgnoresEmptyAndWhitespaceSegments()
    {
        var merged = ShellEnvironment.MergePathSegments(["", null, "  ", "/usr/bin::/bin"]);
        Assert.Equal("/usr/bin:/bin", merged);
    }

    [Fact]
    public void ParseSentinelExtractsPathBetweenMarkers()
    {
        var noisy = "Welcome to your shell!\n"
            + ShellEnvironment.SentinelStart + "/opt/homebrew/bin:/usr/bin" + ShellEnvironment.SentinelEnd
            + "\nsome trailing banner";

        Assert.True(ShellEnvironment.TryParseSentinel(noisy, out var path));
        Assert.Equal("/opt/homebrew/bin:/usr/bin", path);
    }

    [Fact]
    public void ParseSentinelFailsWhenMarkersMissing()
    {
        Assert.False(ShellEnvironment.TryParseSentinel("no markers here", out var path));
        Assert.Equal(string.Empty, path);
    }

    [Fact]
    public void WellKnownDirectoriesCoverCommonCliInstallSpots()
    {
        var dirs = ShellEnvironment.WellKnownBinDirectories();

        Assert.Contains(dirs, d => d.EndsWith(Path.Combine(".local", "bin")));
        Assert.Contains(dirs, d => d.EndsWith(Path.Combine(".claude", "local")));
        Assert.Contains(dirs, d => d.EndsWith(Path.Combine(".grok", "bin")));
        Assert.Contains("/opt/homebrew/bin", dirs);
        Assert.Contains("/usr/local/bin", dirs);
    }
}
