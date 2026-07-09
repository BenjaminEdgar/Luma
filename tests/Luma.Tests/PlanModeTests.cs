using Luma.App.Services;

namespace Luma.Tests;

public sealed class PlanModeTests
{
    [Fact]
    public void PlanParserExtractsDirectiveAndStripsFromText()
    {
        var raw =
            "Here is the approach.\n" +
            "PLAN:\n" +
            "# Auth fix\n" +
            "- [ ] Add null check\n" +
            "- [x] Write test\n" +
            "ASK_USER: Prefer unit or integration? || Unit || Integration";

        var (text, plan) = PlanParser.Extract(raw);
        Assert.Contains("Here is the approach.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PLAN:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Add null check", text, StringComparison.Ordinal);
        Assert.Contains("ASK_USER:", text, StringComparison.Ordinal); // left for ClarifyingQuestionParser
        Assert.NotNull(plan);
        Assert.Contains("# Auth fix", plan!, StringComparison.Ordinal);
        Assert.Contains("Add null check", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("ASK_USER", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanParserExtractsFencedPlanBlock()
    {
        var raw = "Notes\n```plan\n# Ship UI\n- [ ] Wire button\n```\nDone.";
        var (text, plan) = PlanParser.Extract(raw);
        Assert.Contains("Notes", text, StringComparison.Ordinal);
        Assert.Contains("Done.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("```", text, StringComparison.Ordinal);
        Assert.Equal("# Ship UI\n- [ ] Wire button", plan);
    }

    [Fact]
    public void PlanDocumentParsesChecklistSteps()
    {
        var doc = PlanParser.Parse("# Login\n- [ ] Validate\n- [x] Persist\n- plain bullet");
        Assert.Equal("Login", doc.Title);
        Assert.True(doc.CanImplement);
        Assert.Equal(2, doc.Steps.Count); // checklist takes priority over plain bullets
        Assert.False(doc.Steps[0].Done);
        Assert.True(doc.Steps[1].Done);
        Assert.Equal("Validate", doc.Steps[0].Text);
    }

    [Fact]
    public void PlanDocumentFallsBackToPlainBullets()
    {
        var doc = PlanParser.Parse("# Work\n- first\n- second");
        Assert.Equal(2, doc.Steps.Count);
        Assert.All(doc.Steps, s => Assert.False(s.Done));
    }

    [Fact]
    public void ChatStreamFinalSurfacesPlanMarkdown()
    {
        var raw = "I need one detail.\nPLAN:\n# Feature\n- [ ] Step\nASK_USER: Scope? || Small || Large";
        var applied = ChatStreamTextPolicy.ApplyFinal(raw);
        Assert.True(applied.IsQuestion);
        Assert.Equal("Scope?", applied.Question);
        Assert.NotNull(applied.PlanMarkdown);
        Assert.Contains("# Feature", applied.PlanMarkdown!, StringComparison.Ordinal);
        Assert.DoesNotContain("PLAN:", applied.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ASK_USER", applied.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatStreamPartialStripsPlanWithoutPromotingQuestion()
    {
        var applied = ChatStreamTextPolicy.ApplyPartial(
            "Drafting…\nPLAN:\n# WIP\n- [ ] a\nASK_USER: incomplete");
        Assert.False(applied.IsQuestion);
        // Plan body is surfaced mid-stream so implement progress can check steps off live.
        Assert.NotNull(applied.PlanMarkdown);
        Assert.Contains("# WIP", applied.PlanMarkdown!, StringComparison.Ordinal);
        Assert.DoesNotContain("PLAN:", applied.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementPromptIncludesPlanBody()
    {
        var prompt = PlanMode.BuildImplementPrompt("# Title\n- [ ] Do it");
        Assert.Contains("Implement this approved plan", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("# Title", prompt, StringComparison.Ordinal);
        Assert.Contains("Do it", prompt, StringComparison.Ordinal);
        Assert.Contains("PLAN:", prompt, StringComparison.Ordinal);
        Assert.Contains("[x]", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SetStepDoneRewritesChecklistMarkdown()
    {
        var doc = PlanParser.Parse("# Ship\n- [ ] One\n- [ ] Two");
        Assert.Equal(0, doc.Steps.Count(s => s.Done));
        doc.SetStepDone(0, true);
        Assert.True(doc.Steps[0].Done);
        Assert.False(doc.Steps[1].Done);
        Assert.Contains("- [x] One", doc.Markdown, StringComparison.Ordinal);
        Assert.Contains("- [ ] Two", doc.Markdown, StringComparison.Ordinal);
        Assert.Equal("1/2 steps checked", doc.StepSummary);
    }

    [Fact]
    public void ProgressDirectiveMentionsCheckOffs()
    {
        Assert.Contains("PLAN:", PlanMode.ProgressDirective, StringComparison.Ordinal);
        Assert.Contains("[x]", PlanMode.ProgressDirective, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedUiWiresPlanMode()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        // Mode toggle lives on the + menu only; chip collapses/expands the plan window.
        Assert.Contains("TogglePlanModeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("PlanModeMenuLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("TogglePlanWindowCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("PlanChipVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("ImplementPlanCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("PlanModeChipLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("IsPlanWindowCollapsed", xaml, StringComparison.Ordinal);
        Assert.Contains("planchip", xaml, StringComparison.Ordinal);
        Assert.Contains("planpulse", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"StatusPill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("leanchip", xaml, StringComparison.Ordinal); // Lean uses dedicated style (not cwdchip)

        var appXaml = ReadShipped("src/Luma.App/App.axaml");
        Assert.Contains("Button.planchip", appXaml, StringComparison.Ordinal);
        Assert.Contains("Button.leanchip", appXaml, StringComparison.Ordinal);
        Assert.Contains("panelshell.plan", appXaml, StringComparison.Ordinal);
        Assert.Contains("statuspill.plan", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanWindowPolishesLiveUpdatesAndChecklist()
    {
        var planWin = ReadShipped("src/Luma.App/Services/PlanDocumentWindow.cs");
        Assert.Contains("ScrollViewer", planWin, StringComparison.Ordinal);
        Assert.Contains("RefreshContent", planWin, StringComparison.Ordinal);
        Assert.Contains("_hasAnchored", planWin, StringComparison.Ordinal);
        Assert.Contains("UpdateStepsList", planWin, StringComparison.Ordinal);
        Assert.Contains("StepRow", planWin, StringComparison.Ordinal);
        // Tick-box default; raw markdown only via Edit toggle.
        Assert.Contains("ToggleEdit", planWin, StringComparison.Ordinal);
        Assert.Contains("IsVisible = false", planWin, StringComparison.Ordinal);
        // Collapse ↔ mini dock; progress tracking hides Clear/Edit/Implement.
        Assert.Contains("SetCollapsed", planWin, StringComparison.Ordinal);
        Assert.Contains("_collapsedDock", planWin, StringComparison.Ordinal);
        Assert.Contains("SetProgressTracking", planWin, StringComparison.Ordinal);

        var mainCs = ReadShipped("src/Luma.App/MainWindow.axaml.cs");
        Assert.Contains("OnPlanUpdated", mainCs, StringComparison.Ordinal);
        Assert.Contains("RefreshContent", mainCs, StringComparison.Ordinal);
        Assert.Contains("PlanUpdated = OnPlanUpdated", mainCs, StringComparison.Ordinal);
        Assert.Contains("PlanWindowToggleRequested", mainCs, StringComparison.Ordinal);
        Assert.Contains("SetProgressTracking", mainCs, StringComparison.Ordinal);
    }

    [Fact]
    public void DiffCardDropsApplyPatchAndHidesEmptyArtifact()
    {
        var diffCard = ReadShipped("src/Luma.App/Controls/DiffCardControl.cs");
        Assert.DoesNotContain("Apply patch", diffCard, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAsync", diffCard, StringComparison.Ordinal);
        Assert.Contains("hasArtifact", diffCard, StringComparison.Ordinal);
        Assert.Contains("_card.IsVisible = hasArtifact", diffCard, StringComparison.Ordinal);
        Assert.Contains("write-audit", diffCard, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadShipped(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Luma.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repo root with Luma.slnx");
    }
}
