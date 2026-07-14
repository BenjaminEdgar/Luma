namespace Luma.App.Services;

/// <summary>Install readiness for marketplace cards (pure — no Avalonia).</summary>
public enum McpCardInstallState
{
    Ready,
    NeedsKey,
    Installed,
}

/// <summary>
/// Pure helpers for MCP marketplace engagement UI: starter packs, glyphs, counts, install copy.
/// Keeps <see cref="McpMarketplaceWindow"/> thin and unit-testable without rewriting registry/install paths.
/// </summary>
public static class McpMarketplaceUi
{
    public const string HeroTitle = "Power up your agents with tools";
    public const string HeroSubtitle = "Discover · configure · sync servers for Claude, Codex & Grok";
    public const string FeaturedSection = "SPOTLIGHT";
    public const string MoreSection = "MORE SERVERS";
    public const string InstalledEmptyTitle = "Nothing installed yet";
    public const string InstalledEmptyHint = "Start with one of these — one tap installs and syncs to all your agents.";

    /// <summary>Starter pack chips (filter only — no bulk install).</summary>
    public static IReadOnlyList<McpStarterPack> StarterPacks { get; } =
    [
        new("dev", "Dev", "Repos, issues, Docker, local coding"),
        new("research", "Research", "Search, fetch, scrape, browse"),
        new("data", "Data", "SQL, caches, and databases"),
    ];

    /// <summary>Suggested first installs when Installed tab is empty.</summary>
    public static IReadOnlyList<string> SuggestedFirstInstallIds { get; } =
        ["curated/filesystem", "curated/github", "curated/memory"];

    /// <summary>Category → starter pack id (Dev / Research / Data). Core & others stay untagged.</summary>
    public static string? PackForCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        return category.Trim().ToLowerInvariant() switch
        {
            "dev" or "testing" or "cloud" => "dev",
            "web" => "research",
            "data" => "data",
            _ => null,
        };
    }

    public static bool IsInPack(McpCatalogEntry entry, string packId)
    {
        if (string.IsNullOrWhiteSpace(packId)) return true;
        var pack = PackForCategory(entry.Category);
        return string.Equals(pack, packId, StringComparison.OrdinalIgnoreCase);
    }

    public static McpCardInstallState ResolveInstallState(bool isInstalled, bool catalogNeedsKeys)
    {
        if (isInstalled) return McpCardInstallState.Installed;
        if (catalogNeedsKeys) return McpCardInstallState.NeedsKey;
        return McpCardInstallState.Ready;
    }

    public static McpCardInstallState ResolveInstallState(McpCatalogEntry entry, bool isInstalled) =>
        ResolveInstallState(isInstalled, entry.DefaultEnv.Count > 0);

    public static string CategoryGlyph(string? category) =>
        string.IsNullOrWhiteSpace(category)
            ? "◆"
            : category.Trim().ToLowerInvariant() switch
            {
                "core" => "✦",
                "dev" => "</>",
                "testing" => "◎",
                "web" => "◎",
                "data" => "▦",
                "cloud" => "☁",
                "registry" => "◌",
                _ => "◆",
            };

    public static string StateLabel(McpCardInstallState state) => state switch
    {
        McpCardInstallState.Installed => "INSTALLED",
        McpCardInstallState.NeedsKey => "NEEDS KEY",
        _ => "READY",
    };

    public static string FormatLiveCounts(int catalog, int installed, int needKeys) =>
        $"{catalog} catalog · {installed} installed · {needKeys} need keys";

    public static string YouUnlockedLine(string title) =>
        $"You unlocked {title} for your agents";

    public static string InstallSuccessStatus(string title, IReadOnlyList<string> missingKeys)
    {
        var unlocked = YouUnlockedLine(title);
        if (missingKeys.Count > 0)
            return $"✓ {unlocked} — set {string.Join(", ", missingKeys)} for full power";
        return $"✓ {unlocked}";
    }

    public static IReadOnlyList<McpCatalogEntry> SuggestedFirstInstalls(
        IEnumerable<McpCatalogEntry> catalog,
        IReadOnlyList<string>? preferredIds = null)
    {
        var ids = preferredIds ?? SuggestedFirstInstallIds;
        var byId = catalog
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var list = new List<McpCatalogEntry>();
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var e))
                list.Add(e);
        }
        if (list.Count >= 3) return list;
        // Fall back to featured curated so empty state never looks barren.
        foreach (var e in catalog.Where(c => c.IsFeatured && c.Source == "curated"))
        {
            if (list.Any(x => string.Equals(x.Id, e.Id, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(e);
            if (list.Count >= 3) break;
        }
        return list;
    }

    public static int CountNeedKeys(IEnumerable<McpInstalledServer> installed) =>
        installed.Count(s => McpInstallManager.MissingEnvKeys(s).Count > 0);
}

/// <summary>One-tap starter pack filter chip.</summary>
public sealed record McpStarterPack(string Id, string Label, string Hint);
