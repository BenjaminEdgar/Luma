using Luma.App.Services;

namespace Luma.Tests;

public sealed class ChaosModeTests
{
    [Fact]
    public void ToneCyclesAndLabels()
    {
        Assert.Equal(ChaosTone.Eli5, ChaosMode.NextTone(ChaosTone.Normal));
        Assert.Equal(ChaosTone.StaffEng, ChaosMode.NextTone(ChaosTone.Eli5));
        Assert.Equal(ChaosTone.Normal, ChaosMode.NextTone(ChaosTone.StaffEng));
        Assert.Equal("Tone: ELI5", ChaosMode.ToneChipLabel(ChaosTone.Eli5));
        Assert.Contains("Explain like I'm 5", ChaosMode.ToneDirective(ChaosTone.Eli5)!);
        Assert.Contains("Staff engineer", ChaosMode.ToneDirective(ChaosTone.StaffEng)!);
        Assert.Null(ChaosMode.ToneDirective(ChaosTone.Normal));
    }

    [Fact]
    public void RoastAndDebatePromptsAreStructured()
    {
        Assert.Contains("Roast my UI", ChaosMode.RoastUiPrompt());
        var debate = ChaosMode.DebatePrompt("Should we rewrite the dock?");
        Assert.Contains("## Side A", debate);
        Assert.Contains("## Side B", debate);
        Assert.Contains("## Verdict", debate);
        Assert.Contains("rewrite the dock", debate);

        var dual = ChaosMode.FormatDualDebate("Claude", "Ship it.", "Grok", "Wait.");
        Assert.Contains("Claude", dual);
        Assert.Contains("Grok", dual);
        Assert.Contains("Ship it.", dual);
    }

    [Fact]
    public void PomodoroMessageFormatsRemaining()
    {
        Assert.Equal("12:05", ChaosMode.FormatRemaining(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(5)));
        Assert.Contains("focus lock", ChaosMode.PomodoroBlockedMessage(TimeSpan.FromMinutes(3)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cleared", ChaosMode.PomodoroBlockedMessage(TimeSpan.Zero), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PromptIncludesChaosToneWhenEnabled()
    {
        var original = AppSettings.Current;
        try
        {
            AppSettings.Current = new AppSettings { ChaosMode = true, ChaosTone = (int)ChaosTone.Eli5 };
            var prompt = TestClient.Prompt(new Luma.App.Models.AiRequest("hi", null, null, [])
            { TaskKind = Luma.App.Models.TaskKind.Chat });
            Assert.Contains("CHAOS TONE", prompt);
            Assert.Contains("Explain like I'm 5", prompt);
        }
        finally { AppSettings.Current = original; }
    }

    [Fact]
    public void ShippedUiExposesChaosControls()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("ChaosModeEnabled", xaml);
        Assert.Contains("RoastUiCommand", xaml);
        Assert.Contains("ArgueWithYourselfCommand", xaml);
        Assert.Contains("ToggleChaosModeCommand", xaml);
        Assert.Contains("chaoschip", xaml);

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("ArgueWithYourselfAsync", vm);
        Assert.Contains("TogglePomodoro", vm);
        Assert.Contains("IsFocusLocked", vm);
    }

    private sealed class TestClient : CliAiClient
    {
        protected override string Command => "unused";
        protected override void AddArguments(System.Diagnostics.ProcessStartInfo startInfo, Luma.App.Models.AiRequest request, string prompt, string sessionDirectory) { }
        public static string Prompt(Luma.App.Models.AiRequest request) => BuildPrompt(request);
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
