using Luma.App.Services;
using System.Collections.ObjectModel;

namespace Luma.Tests;

public sealed class LivePairMapTests
{
    [Fact]
    public void MeasureHeat_CountsCreateModifyAndDelete()
    {
        var create = LivePairMap.MeasureHeat(null, "a\nb\nc");
        Assert.Equal(3, create.Additions);
        Assert.Equal(0, create.Deletions);

        var modify = LivePairMap.MeasureHeat("hello\nworld\n", "hello\nthere\nfriend\n");
        Assert.True(modify.Additions >= 1);
        Assert.True(modify.Deletions >= 1);

        var delete = LivePairMap.MeasureHeat("gone\n", null);
        Assert.Equal(0, delete.Additions);
        Assert.True(delete.Deletions >= 1);
    }

    [Fact]
    public void BuildPreview_ShowsRemovedAndAddedLinesWithContext()
    {
        var before = "keep\nold-a\nold-b\ntail\n";
        var after = "keep\nnew-a\nnew-b\ntail\n";
        var preview = LivePairMap.BuildPreview(before, after);

        Assert.Contains(preview, l => l.IsContext && l.Text == "keep");
        Assert.Contains(preview, l => l.IsRemoved && l.Text == "old-a");
        Assert.Contains(preview, l => l.IsRemoved && l.Text == "old-b");
        Assert.Contains(preview, l => l.IsAdded && l.Text == "new-a");
        Assert.Contains(preview, l => l.IsAdded && l.Text == "new-b");
        Assert.Contains(preview, l => l.IsContext && l.Text == "tail");
        Assert.Contains(preview, l => l.Display.StartsWith('+'));
        Assert.Contains(preview, l => l.Display.StartsWith('-'));
    }

    [Fact]
    public void BuildPreview_CreateIsAllAdds_DeleteIsAllRemoves()
    {
        var created = LivePairMap.BuildPreview(null, "one\ntwo");
        Assert.All(created, l => Assert.True(l.IsAdded));
        Assert.Equal(2, created.Count);

        var deleted = LivePairMap.BuildPreview("gone\nbye", null);
        Assert.All(deleted, l => Assert.True(l.IsRemoved));
        Assert.Equal(2, deleted.Count);
    }

    [Fact]
    public void Scan_DetectsFilesAndHeat()
    {
        var root = Path.Combine(Path.GetTempPath(), "LumaTests", "pair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var a = Path.Combine(root, "a.txt");
        File.WriteAllText(a, "line1\nline2\n");
        try
        {
            var snap = WorkspaceWriteAuditor.Capture(root);
            File.WriteAllText(a, "line1\nchanged\nextra\n");
            File.WriteAllText(Path.Combine(root, "b.txt"), "new file\n");

            var scan = LivePairMap.Scan(root, snap);
            Assert.Contains(scan, r => r.RelativePath == "a.txt" && r.Kind == FileChangeKind.Modified);
            Assert.Contains(scan, r => r.RelativePath == "b.txt" && r.Kind == FileChangeKind.Created && r.Additions > 0);
            Assert.Contains(scan, r => r.RelativePath == "a.txt" && r.Preview.Count > 0
                && r.Preview.Any(p => p.IsAdded || p.IsRemoved));

            var map = new ObservableCollection<LivePairFile>();
            LivePairMap.MergeInto(map, scan);
            Assert.True(map.Count >= 2);
            Assert.Contains(map, f => f.RelativePath == "a.txt" && f.AddBarWidth + f.DelBarWidth > 0);
            Assert.Contains(map, f => f.IsFresh);
            Assert.Contains(map, f => f.HasPreview);

            var active = LivePairMap.PickActive(map, null);
            Assert.NotNull(active);
            Assert.True(active!.HasPreview);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ShippedUiWiresLivePair()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("livepair", xaml);
        Assert.Contains("LivePairFiles", xaml);
        Assert.Contains("JumpLivePairCommand", xaml);
        Assert.Contains("ShowLivePair", xaml);
        Assert.Contains("LivePairPreviewLines", xaml);
        Assert.Contains("livepairpreview", xaml);
        Assert.Contains("livepairchip", xaml);

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("BeginLivePair", vm);
        Assert.Contains("LivePairMap", vm);
        Assert.Contains("JumpLivePairCommand", vm);
        Assert.Contains("ActiveLivePairFile", vm);
        Assert.Contains("BuildPreview", ReadShipped("src/Luma.App/Services/LivePairMap.cs"));

        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "src/Luma.App/Services/LivePairMap.cs")));
    }

    private static string ReadShipped(string relative) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Luma.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repo root.");
    }
}
