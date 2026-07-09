using System.Text.RegularExpressions;
using Luma.App.Services;

namespace Luma.Tests;

public sealed class McpMarketplaceTests
{
    [Fact]
    public void CuratedCatalogHasInstallableFeaturedServers()
    {
        Assert.NotEmpty(McpCuratedCatalog.All);
        Assert.Contains(McpCuratedCatalog.All, e => e.IsFeatured && e.InstallKind == McpInstallKind.NpxPackage);
        Assert.All(McpCuratedCatalog.All.Where(e => e.InstallKind == McpInstallKind.NpxPackage),
            e =>
            {
                Assert.Equal("npx", e.Command);
                Assert.NotEmpty(e.Args);
            });
    }

    [Fact]
    public void InstallManagerPersistsAndSanitizesKeys()
    {
        var path = McpInstallManager.StorePath();
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        var grok = McpInstallManager.GrokConfigPath();
        var grokBackup = File.Exists(grok) ? File.ReadAllText(grok) : null;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var manager = new McpInstallManager();
            var entry = McpCuratedCatalog.All.First(e => e.Id == "curated/memory");
            var installed = manager.Install(entry, workspacePath: Path.GetTempPath());
            Assert.True(manager.IsInstalled(entry.Id));
            Assert.Equal("npx", installed.Command);
            Assert.Contains("-y", installed.Args);
            Assert.True(installed.Enabled);

            manager.SetEnabled(entry.Id, false);
            Assert.False(manager.LoadInstalled().First(s => s.Id == entry.Id).Enabled);

            var key = McpInstalledServer.SanitizeKey("ai.smithery/Hello World!!");
            Assert.DoesNotContain(" ", key);
            Assert.DoesNotContain("!", key);
            Assert.False(string.IsNullOrWhiteSpace(key));

            // Managed block should exist in Grok config after install.
            manager.SetEnabled(entry.Id, true);
            var toml = File.ReadAllText(grok);
            Assert.Contains("Luma MCP Marketplace", toml);
            Assert.Contains("[mcp_servers.", toml);

            manager.Uninstall(entry.Id);
            Assert.False(manager.IsInstalled(entry.Id));
        }
        finally
        {
            if (backup is null) { try { File.Delete(path); } catch { } }
            else File.WriteAllText(path, backup);
            if (grokBackup is null)
            {
                // Leave file if manager created it; strip only if we had no prior file and want clean — restore empty managed.
                try
                {
                    if (File.Exists(grok) && grokBackup is null)
                    {
                        var text = File.ReadAllText(grok);
                        if (text.Contains("Luma MCP Marketplace") && text.Trim().StartsWith("# --- Luma"))
                            File.Delete(grok);
                    }
                }
                catch { }
            }
            else File.WriteAllText(grok, grokBackup);
        }
    }

    [Fact]
    public void WorkspacePlaceholderExpandsOnInstall()
    {
        var path = McpInstallManager.StorePath();
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        var grok = McpInstallManager.GrokConfigPath();
        var grokBackup = File.Exists(grok) ? File.ReadAllText(grok) : null;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var manager = new McpInstallManager();
            var entry = McpCuratedCatalog.All.First(e => e.Id == "curated/filesystem");
            var ws = Path.Combine(Path.GetTempPath(), "LumaWs");
            var installed = manager.Install(entry, ws);
            Assert.Contains(ws, string.Join(' ', installed.Args));
            Assert.DoesNotContain("{{workspace}}", string.Join(' ', installed.Args));
            manager.Uninstall(entry.Id);
        }
        finally
        {
            if (backup is null) { try { File.Delete(path); } catch { } }
            else File.WriteAllText(path, backup);
            if (grokBackup is not null) File.WriteAllText(grok, grokBackup);
        }
    }

    [Fact]
    public void MissingEnvKeysDetectsEmptySecrets()
    {
        var entry = McpCuratedCatalog.All.First(e => e.Id == "curated/github");
        var missing = McpInstallManager.MissingEnvKeys(entry, new Dictionary<string, string>());
        Assert.Contains("GITHUB_PERSONAL_ACCESS_TOKEN", missing);

        var filled = McpInstallManager.MissingEnvKeys(entry,
            new Dictionary<string, string> { ["GITHUB_PERSONAL_ACCESS_TOKEN"] = "ghp_x" });
        Assert.Empty(filled);
    }

    [Fact]
    public void ParseMcpServerTablesImportsHandEditedConfig()
    {
        const string toml =
            """
            [mcp_servers.my-custom]
            enabled = true
            command = "npx"
            args = ["-y", "foo"]
            env = { API_KEY = "secret" }

            # --- Luma MCP Marketplace (managed) ---
            [mcp_servers.curated-memory]
            enabled = true
            command = "npx"
            # --- end Luma MCP Marketplace ---
            """;

        var parsed = McpInstallManager.ParseMcpServerTables(toml);
        Assert.Contains(parsed, s => s.ConfigSectionName == "my-custom" && s.Args.Contains("foo"));
        Assert.DoesNotContain(parsed, s => s.ConfigSectionName == "curated-memory");
        var custom = parsed.First(s => s.ConfigSectionName == "my-custom");
        Assert.Equal("secret", custom.Env["API_KEY"]);
    }

    [Fact]
    public void CuratedCatalogHasCategoriesAndMoreRecipes()
    {
        Assert.Contains("Core", McpCuratedCatalog.Categories);
        Assert.Contains("Dev", McpCuratedCatalog.Categories);
        Assert.Contains("Testing", McpCuratedCatalog.Categories);
        Assert.Contains("Cloud", McpCuratedCatalog.Categories);
        Assert.True(McpCuratedCatalog.All.Count >= 25);
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/sequential-thinking");
        Assert.Contains(McpCuratedCatalog.All, e => e.DefaultEnv.ContainsKey("DATABASE_URL"));
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/gitlab");
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/linear");
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/playwright" && e.IsFeatured);
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/docker" && e.IsFeatured);
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/mongodb");
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/supabase");
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/sentry");
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/exa");
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/firecrawl");
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/context7");
        Assert.Contains(McpCuratedCatalog.All, e => e.Id == "curated/chrome-devtools");
        // Featured stays lean: Dev-biased additions without flooding the strip.
        Assert.InRange(McpCuratedCatalog.All.Count(e => e.IsFeatured), 6, 12);
        Assert.All(McpCuratedCatalog.All.Where(e => e.InstallKind == McpInstallKind.RemoteHttp),
            e => Assert.False(string.IsNullOrWhiteSpace(e.RemoteUrl)));
    }

    [Fact]
    public void StripMcpServerTablesRemovesDuplicateKeysBeforeManagedSync()
    {
        const string messy =
            """
            [cli]
            installer = "internal"

            [mcp_servers.curated-memory]
            enabled = true
            command = "npx"

            [mcp_servers.other]
            enabled = true
            command = "echo"

            # --- Luma MCP Marketplace (managed) ---
            [mcp_servers.curated-memory]
            enabled = true
            command = "npx"
            # --- end Luma MCP Marketplace ---
            """;

        var withoutManaged = messy; // StripMcpServerTables is only for tables; full sync strips managed first.
        // Simulate the two-step cleanup SyncToGrokConfig performs.
        var start = "# --- Luma MCP Marketplace (managed) ---";
        var end = "# --- end Luma MCP Marketplace ---";
        var i = withoutManaged.IndexOf(start, StringComparison.Ordinal);
        var j = withoutManaged.IndexOf(end, StringComparison.Ordinal) + end.Length;
        withoutManaged = withoutManaged[..i] + withoutManaged[j..];
        var cleaned = McpInstallManager.StripMcpServerTables(withoutManaged, ["curated-memory"]);

        Assert.DoesNotContain("[mcp_servers.curated-memory]", cleaned);
        Assert.Contains("[mcp_servers.other]", cleaned);
        Assert.Contains("[cli]", cleaned);
    }

    [Fact]
    public void SyncToGrokConfigDoesNotDuplicateExistingMcpTables()
    {
        var path = McpInstallManager.StorePath();
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        var grok = McpInstallManager.GrokConfigPath();
        var grokBackup = File.Exists(grok) ? File.ReadAllText(grok) : null;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            // Pre-seed the same key Grok already had outside Luma's managed block.
            File.WriteAllText(grok,
                """
                [cli]
                installer = "internal"

                [mcp_servers.curated-memory]
                enabled = true
                command = "npx"
                args = ["-y", "@modelcontextprotocol/server-memory"]

                """);

            var manager = new McpInstallManager();
            var entry = McpCuratedCatalog.All.First(e => e.Id == "curated/memory");
            manager.Install(entry, workspacePath: Path.GetTempPath());

            var toml = File.ReadAllText(grok);
            var matches = Regex.Matches(toml, @"\[mcp_servers\.curated-memory\]");
            Assert.Single(matches);
            Assert.Contains("Luma MCP Marketplace", toml);
            Assert.DoesNotContain("duplicate", toml); // sanity

            manager.Uninstall(entry.Id);
        }
        finally
        {
            if (backup is null) { try { File.Delete(path); } catch { } }
            else File.WriteAllText(path, backup);
            if (grokBackup is null) { try { File.Delete(grok); } catch { } }
            else File.WriteAllText(grok, grokBackup);
        }
    }

    [Fact]
    public void ShippedUiOpensMarketplace()
    {
        var settings = ReadShipped("src/Luma.App/Services/SettingsWindow.cs");
        Assert.Contains("McpMarketplaceWindow", settings);
        Assert.Contains("MCP Marketplace", settings);

        var main = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("MCP Marketplace", main);
        Assert.Contains("OnMcpMarketplaceClick", main);

        var code = ReadShipped("src/Luma.App/MainWindow.axaml.cs");
        Assert.Contains("OnMcpMarketplaceClick", code);
        Assert.Contains("McpMarketplaceWindow", code);
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
