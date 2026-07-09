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

            var map = new ObservableCollection<LivePairFile>();
            LivePairMap.MergeInto(map, scan);
            Assert.True(map.Count >= 2);
            Assert.Contains(map, f => f.RelativePath == "a.txt" && f.AddBarWidth + f.DelBarWidth > 0);
            Assert.Contains(map, f => f.IsFresh);
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

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("BeginLivePair", vm);
        Assert.Contains("LivePairMap", vm);
        Assert.Contains("JumpLivePairCommand", vm);

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
