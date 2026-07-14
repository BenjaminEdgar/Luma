using Luma.App.Services;

namespace Luma.Tests;

public sealed class McpMarketplaceUiTests
{
    [Fact]
    public void HeroAndEmptyStateStringsAreSet()
    {
        Assert.Equal("Power up your agents with tools", McpMarketplaceUi.HeroTitle);
        Assert.Contains("Grok", McpMarketplaceUi.HeroSubtitle, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SPOTLIGHT", McpMarketplaceUi.FeaturedSection);
        Assert.Equal("Nothing installed yet", McpMarketplaceUi.InstalledEmptyTitle);
        Assert.Contains("one tap", McpMarketplaceUi.InstalledEmptyHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StarterPacksAreDevResearchData()
    {
        Assert.Equal(3, McpMarketplaceUi.StarterPacks.Count);
        Assert.Equal(new[] { "dev", "research", "data" }, McpMarketplaceUi.StarterPacks.Select(p => p.Id).ToArray());
        Assert.Equal(new[] { "Dev", "Research", "Data" }, McpMarketplaceUi.StarterPacks.Select(p => p.Label).ToArray());
    }

    [Fact]
    public void CategoryMapsToStarterPacks()
    {
        Assert.Equal("dev", McpMarketplaceUi.PackForCategory("Dev"));
        Assert.Equal("dev", McpMarketplaceUi.PackForCategory("Testing"));
        Assert.Equal("dev", McpMarketplaceUi.PackForCategory("Cloud"));
        Assert.Equal("research", McpMarketplaceUi.PackForCategory("Web"));
        Assert.Equal("data", McpMarketplaceUi.PackForCategory("Data"));
        Assert.Null(McpMarketplaceUi.PackForCategory("Core"));
        Assert.Null(McpMarketplaceUi.PackForCategory(null));
    }

    [Fact]
    public void IsInPackFiltersCuratedCatalog()
    {
        var github = McpCuratedCatalog.All.First(e => e.Id == "curated/github");
        var fetch = McpCuratedCatalog.All.First(e => e.Id == "curated/fetch");
        var sqlite = McpCuratedCatalog.All.First(e => e.Id == "curated/sqlite");
        var memory = McpCuratedCatalog.All.First(e => e.Id == "curated/memory");

        Assert.True(McpMarketplaceUi.IsInPack(github, "dev"));
        Assert.False(McpMarketplaceUi.IsInPack(github, "data"));
        Assert.True(McpMarketplaceUi.IsInPack(fetch, "research"));
        Assert.True(McpMarketplaceUi.IsInPack(sqlite, "data"));
        Assert.False(McpMarketplaceUi.IsInPack(memory, "dev")); // Core stays out of packs
    }

    [Fact]
    public void InstallStateAndLabels()
    {
        Assert.Equal(McpCardInstallState.Installed, McpMarketplaceUi.ResolveInstallState(true, true));
        Assert.Equal(McpCardInstallState.NeedsKey, McpMarketplaceUi.ResolveInstallState(false, true));
        Assert.Equal(McpCardInstallState.Ready, McpMarketplaceUi.ResolveInstallState(false, false));
        Assert.Equal("INSTALLED", McpMarketplaceUi.StateLabel(McpCardInstallState.Installed));
        Assert.Equal("NEEDS KEY", McpMarketplaceUi.StateLabel(McpCardInstallState.NeedsKey));
        Assert.Equal("READY", McpMarketplaceUi.StateLabel(McpCardInstallState.Ready));

        var github = McpCuratedCatalog.All.First(e => e.Id == "curated/github");
        var fs = McpCuratedCatalog.All.First(e => e.Id == "curated/filesystem");
        Assert.Equal(McpCardInstallState.NeedsKey, McpMarketplaceUi.ResolveInstallState(github, false));
        Assert.Equal(McpCardInstallState.Ready, McpMarketplaceUi.ResolveInstallState(fs, false));
        Assert.Equal(McpCardInstallState.Installed, McpMarketplaceUi.ResolveInstallState(github, true));
    }

    [Fact]
    public void CategoryGlyphsAndLiveCounts()
    {
        Assert.Equal("✦", McpMarketplaceUi.CategoryGlyph("Core"));
        Assert.Equal("</>", McpMarketplaceUi.CategoryGlyph("Dev"));
        Assert.Equal("▦", McpMarketplaceUi.CategoryGlyph("Data"));
        Assert.Equal("◆", McpMarketplaceUi.CategoryGlyph(null));
        Assert.Equal("12 catalog · 3 installed · 1 need keys",
            McpMarketplaceUi.FormatLiveCounts(12, 3, 1));
    }

    [Fact]
    public void InstallSuccessCopyUnlocksServer()
    {
        Assert.Equal("You unlocked Memory for your agents", McpMarketplaceUi.YouUnlockedLine("Memory"));
        Assert.Equal("✓ You unlocked Memory for your agents",
            McpMarketplaceUi.InstallSuccessStatus("Memory", []));
        Assert.Contains("GITHUB_TOKEN",
            McpMarketplaceUi.InstallSuccessStatus("GitHub", ["GITHUB_TOKEN"]));
        Assert.StartsWith("✓ ", McpMarketplaceUi.InstallSuccessStatus("GitHub", ["GITHUB_TOKEN"]));
    }

    [Fact]
    public void SuggestedFirstInstallsPrefersFilesystemGithubMemory()
    {
        var picks = McpMarketplaceUi.SuggestedFirstInstalls(McpCuratedCatalog.All);
        Assert.Equal(3, picks.Count);
        Assert.Equal("curated/filesystem", picks[0].Id);
        Assert.Equal("curated/github", picks[1].Id);
        Assert.Equal("curated/memory", picks[2].Id);
    }

    [Fact]
    public void SuggestedFirstInstallsFallsBackToFeatured()
    {
        var thin = McpCuratedCatalog.All.Where(e => e.Id == "curated/time").ToList();
        // Only time available — fill from featured curated when preferred ids missing.
        var fullCatalog = thin.Concat(McpCuratedCatalog.All.Where(e => e.IsFeatured)).ToList();
        var picks = McpMarketplaceUi.SuggestedFirstInstalls(fullCatalog, preferredIds: ["curated/missing"]);
        Assert.True(picks.Count >= 1);
        Assert.All(picks, p => Assert.True(p.IsFeatured || p.Id == "curated/time"));
    }
}
