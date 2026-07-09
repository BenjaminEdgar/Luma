using Luma.App.Services;

namespace Luma.Tests;

public sealed class RepoInspectionTests
{
    [Fact]
    public void EmptyListingReportsNoFiles()
    {
        var summary = RepoContextFormatter.BuildFileListSummary([]);
        Assert.Equal("(no files found)", summary);
    }

    [Fact]
    public void WorkspaceFileListingReadsAnyFolderWithoutGit()
    {
        var root = Path.Combine(Path.GetTempPath(), "LumaTests", "ws-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "src");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "pkg"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "readme.md"), "hi");
        File.WriteAllText(Path.Combine(nested, "app.cs"), "class App {}");
        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "index.js"), "module.exports = 1");
        File.WriteAllText(Path.Combine(root, ".git", "config"), "x");
        try
        {
            var files = WorkspaceFileListing.ListFiles(root);
            Assert.Contains("readme.md", files);
            Assert.Contains("src/app.cs", files);
            Assert.DoesNotContain(files, f => f.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(files, f => f.Contains(".git", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SmallListingIsNotTruncated()
    {
        var summary = RepoContextFormatter.BuildFileListSummary(["b.cs", "a.cs"]);
        Assert.Equal("a.cs\nb.cs", summary);
        Assert.DoesNotContain("more files", summary);
    }

    [Fact]
    public void ExcessEntriesAreTruncatedWithCount()
    {
        var files = Enumerable.Range(0, 10).Select(i => $"file{i}.cs").ToArray();
        var summary = RepoContextFormatter.BuildFileListSummary(files, maxEntries: 3);

        var lines = summary.Split('\n');
        Assert.Equal(4, lines.Length); // 3 files + the truncation note
        Assert.Contains("... and 7 more files", summary);
    }
}
