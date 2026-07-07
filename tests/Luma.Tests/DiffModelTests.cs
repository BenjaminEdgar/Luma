using Luma.App.Services;

namespace Luma.Tests;

public sealed class DiffModelTests
{
    private const string TwoHunkDiff =
        "diff --git a/file.txt b/file.txt\n" +
        "--- a/file.txt\n" +
        "+++ b/file.txt\n" +
        "@@ -1,3 +1,4 @@\n" +
        " line1\n" +
        "+inserted\n" +
        " line2\n" +
        " line3\n" +
        "@@ -18,3 +19,3 @@\n" +
        " line18\n" +
        "-line19\n" +
        "+line19-changed\n" +
        " line20\n";

    [Fact]
    public void ParsesMultipleFilesAndHunks()
    {
        const string diff =
            "diff --git a/a.cs b/a.cs\n--- a/a.cs\n+++ b/a.cs\n@@ -1,1 +1,1 @@\n-old\n+new\n" +
            "diff --git a/b.cs b/b.cs\n--- a/b.cs\n+++ b/b.cs\n@@ -2,1 +2,1 @@\n-foo\n+bar\n";

        var document = DiffParser.Parse(diff);

        Assert.Equal(2, document.Files.Count);
        Assert.Equal("a.cs", document.Files[0].NewPath);
        Assert.Single(document.Files[0].Hunks);
        Assert.Equal(DiffLineKind.Removed, document.Files[0].Hunks[0].Lines[0].Kind);
        Assert.Equal(DiffLineKind.Added, document.Files[0].Hunks[0].Lines[1].Kind);
        Assert.Equal("b.cs", document.Files[1].NewPath);
    }

    [Fact]
    public void HunkHeaderWithoutCountDefaultsToOne()
    {
        const string diff = "diff --git a/a.cs b/a.cs\n--- a/a.cs\n+++ b/a.cs\n@@ -12 +12 @@\n-old\n+new\n";

        var document = DiffParser.Parse(diff);

        var hunk = document.Files[0].Hunks[0];
        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(1, hunk.NewCount);
        Assert.Equal(12, hunk.OldStart);
        Assert.Equal(12, hunk.NewStart);
    }

    [Fact]
    public void RoundTripsIdenticalWhenEverythingSelected()
    {
        var document = DiffParser.Parse(TwoHunkDiff);

        Assert.Equal(TwoHunkDiff, document.BuildPatch());
    }

    [Fact]
    public void DeselectingEarlierHunkLeavesLaterHunkPositionUnshifted()
    {
        var document = DiffParser.Parse(TwoHunkDiff);
        document.Files[0].Hunks[0].IsSelected = false;

        var patch = document.BuildPatch();

        Assert.DoesNotContain("+inserted", patch);
        Assert.Contains("@@ -18,3 +18,3 @@", patch);
        Assert.DoesNotContain("+19,3", patch);
    }

    [Fact]
    public void DeselectingWholeFileOmitsItEntirely()
    {
        const string diff =
            "diff --git a/a.cs b/a.cs\n--- a/a.cs\n+++ b/a.cs\n@@ -1,1 +1,1 @@\n-old\n+new\n" +
            "diff --git a/b.cs b/b.cs\n--- a/b.cs\n+++ b/b.cs\n@@ -2,1 +2,1 @@\n-foo\n+bar\n";
        var document = DiffParser.Parse(diff);
        document.Files[0].IsSelected = false;

        var patch = document.BuildPatch();

        Assert.DoesNotContain("a.cs", patch);
        Assert.Contains("b.cs", patch);
    }

    [Fact]
    public void BinaryFileParsesWithoutHunks()
    {
        const string diff = "diff --git a/img.png b/img.png\nindex 111..222 100644\nGIT binary patch\nliteral 10\nabcdefghij\n";

        var document = DiffParser.Parse(diff);

        Assert.True(document.Files[0].IsBinary);
        Assert.Empty(document.Files[0].Hunks);
    }

    [Fact]
    public async Task SelectiveHunkPatchAppliesOnlySelectedChangeToRealFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LumaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var content = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line{i}")) + "\n";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file.txt"), content);

            var document = DiffParser.Parse(TwoHunkDiff);
            document.Files[0].Hunks[0].IsSelected = false; // keep only the "line19 -> line19-changed" hunk
            var patch = document.BuildPatch();

            var git = new GitService();
            await git.ApplyDiffAsync(tempDir, patch, CancellationToken.None);

            var result = await File.ReadAllTextAsync(Path.Combine(tempDir, "file.txt"));
            Assert.DoesNotContain("inserted", result);
            Assert.Contains("line19-changed", result);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
