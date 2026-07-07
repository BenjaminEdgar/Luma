using Luma.App.Services;

namespace Luma.Tests;

public sealed class RepoInspectionTests
{
    [Fact]
    public void EmptyListingReportsNoFiles()
    {
        var summary = RepoContextFormatter.BuildFileListSummary([]);
        Assert.Equal("(no tracked files found)", summary);
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
