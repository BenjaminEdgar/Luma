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
