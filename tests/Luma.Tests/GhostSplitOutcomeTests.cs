using Luma.App.Services;

namespace Luma.Tests;

public sealed class GhostSplitOutcomeTests
{
    [Fact]
    public void ShowWhereParserExtractsNormalizedRectAndStripsDirective()
    {
        var raw = "Click Apply in the dialog.\nSHOW_WHERE: Apply button | 0.72,0.81,0.14,0.06";
        var (text, target) = ShowWhereParser.Extract(raw);
        Assert.Equal("Click Apply in the dialog.", text);
        Assert.NotNull(target);
        Assert.Equal("Apply button", target!.Label);
        Assert.Equal(0.72, target.X, 3);
        Assert.Equal(0.81, target.Y, 3);
        Assert.Equal(0.14, target.Width, 3);
        Assert.Equal(0.06, target.Height, 3);
    }

    [Fact]
    public void ShowWhereParserWithoutLabelWorks()
    {
        var (text, target) = ShowWhereParser.Extract("Look here\nSHOW_WHERE: 0.1,0.2,0.3,0.4");
        Assert.Equal("Look here", text);
        Assert.NotNull(target);
        Assert.Null(target!.Label);
        Assert.Equal(0.1, target.X, 3);
    }

    [Fact]
    public void OutcomeMemoryRecordsAndSuggestsChips()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma");
        var path = Path.Combine(dir, "outcome-memory.json");
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            OutcomeMemory.Record(OutcomeKind.Undo, "Undid edit AuthService.cs", tags: ["AuthService.cs"]);
            OutcomeMemory.Record(OutcomeKind.Write, "Edited Program.cs", tags: ["Program.cs"]);
            var chips = OutcomeMemory.SuggestChips("auth null reference", max: 4);
            Assert.NotEmpty(chips);
            Assert.Contains(chips, c => c.Contains("AuthService", StringComparison.OrdinalIgnoreCase)
                || c.Contains("Avoid", StringComparison.OrdinalIgnoreCase)
                || c.Contains("Program", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (backup is null) { try { File.Delete(path); } catch { } }
            else File.WriteAllText(path, backup);
        }
    }

    [Fact]
    public void SplitBrainChooseUpdatesMergedText()
    {
        var split = new SplitBrainResult
        {
            ProviderA = "Claude",
            ProviderB = "Grok",
            TextA = "Explain carefully.",
            TextB = "Ship the patch.",
        };
        Assert.Contains("Claude", split.MergedText);
        Assert.Contains("Grok", split.MergedText);
        split.Choose("A");
        Assert.Equal("Explain carefully.", split.MergedText);
        Assert.True(split.HasChosen);
        Assert.False(split.CanChoose);
    }

    [Fact]
    public void ShippedUiWiresGhostSplitOutcome()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("ShowWhereCommand", xaml);
        Assert.Contains("HasShowWhere", xaml);
        Assert.Contains("HasSplitBrain", xaml);
        Assert.Contains("OnSplitBrainKeepClick", xaml);
        Assert.Contains("ToggleSplitBrainCommand", xaml);

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("GhostCursorWindow.PointAt", vm);
        Assert.Contains("RunSplitBrainTurnAsync", vm);
        Assert.Contains("OutcomeMemory", vm);
        Assert.Contains("ShowWhereParser", vm);
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
