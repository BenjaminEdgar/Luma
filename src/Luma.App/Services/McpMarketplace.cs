using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Luma.App.Services;

public enum McpInstallKind { RemoteHttp, NpxPackage, CustomCommand }

/// <summary>A server available in the marketplace (registry or curated).</summary>
public sealed class McpCatalogEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? Version { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? WebsiteUrl { get; init; }
    public string Source { get; init; } = "registry"; // registry | curated
    public McpInstallKind InstallKind { get; init; }
    public string? RemoteUrl { get; init; }
    public string? Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public IReadOnlyDictionary<string, string> DefaultEnv { get; init; } = new Dictionary<string, string>();
    public string? Category { get; init; }
    public bool IsFeatured { get; init; }
}

/// <summary>A server the user has installed / enabled for Grok.</summary>
public sealed class McpInstalledServer
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public McpInstallKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public string? RemoteUrl { get; set; }
    public string? Command { get; set; }
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ConfigKey { get; set; }

    public string ConfigSectionName =>
        string.IsNullOrWhiteSpace(ConfigKey) ? SanitizeKey(Id) : ConfigKey!;

    public static string SanitizeKey(string id)
    {
        var key = Regex.Replace(id, @"[^a-zA-Z0-9_-]+", "-").Trim('-').ToLowerInvariant();
        if (key.Length == 0) key = "mcp-server";
        if (key.Length > 48) key = key[..48];
        return key;
    }
}

/// <summary>Fetches servers from the official MCP Registry + merges curated install recipes.</summary>
public sealed class McpRegistryClient
{
    public const string RegistryBase = "https://registry.modelcontextprotocol.io";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", "Luma-MCP-Marketplace/1.0" } },
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<McpCatalogEntry>> SearchAsync(string? query, int limit = 40, CancellationToken token = default)
    {
        var curated = McpCuratedCatalog.All;
        var results = new List<McpCatalogEntry>();

        // Curated first (reliable install recipes).
        IEnumerable<McpCatalogEntry> curatedHits = curated;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            curatedHits = curated.Where(c =>
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (c.Category?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        results.AddRange(curatedHits);

        try
        {
            var url = string.IsNullOrWhiteSpace(query)
                ? $"{RegistryBase}/v0/servers?limit={Math.Clamp(limit, 5, 100)}"
                : $"{RegistryBase}/v0/servers?search={Uri.EscapeDataString(query.Trim())}&limit={Math.Clamp(limit, 5, 100)}";
            using var response = await Http.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var page = await JsonSerializer.DeserializeAsync<RegistryPage>(stream, JsonOptions, token);
            if (page?.Servers is null) return Dedup(results);

            foreach (var item in page.Servers)
            {
                var s = item.Server;
                if (s is null) continue;
                if (item.Meta?.Official is { IsLatest: false }) continue;

                var remotes = s.Remotes ?? [];
                var remote = remotes.FirstOrDefault(r =>
                    r.Type is "streamable-http" or "sse" or "http") ?? remotes.FirstOrDefault();
                var packages = s.Packages ?? [];
                var npm = packages.FirstOrDefault(p =>
                    string.Equals(p.RegistryType, "npm", StringComparison.OrdinalIgnoreCase) ||
                    (p.Identifier?.Contains('@') ?? false));

                McpInstallKind kind;
                string? command = null;
                IReadOnlyList<string> args = [];
                string? remoteUrl = remote?.Url;

                if (npm is not null && !string.IsNullOrWhiteSpace(npm.Identifier))
                {
                    kind = McpInstallKind.NpxPackage;
                    command = "npx";
                    args = ["-y", npm.Identifier!];
                }
                else if (!string.IsNullOrWhiteSpace(remoteUrl))
                {
                    kind = McpInstallKind.RemoteHttp;
                }
                else
                {
                    kind = McpInstallKind.CustomCommand;
                }

                var id = s.Name ?? s.Title ?? Guid.NewGuid().ToString("N");
                if (results.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))) continue;

                results.Add(new McpCatalogEntry
                {
                    Id = id,
                    Title = s.Title ?? s.Name ?? id,
                    Description = s.Description ?? "",
                    Version = s.Version,
                    RepositoryUrl = s.Repository?.Url,
                    WebsiteUrl = s.WebsiteUrl,
                    Source = "registry",
                    InstallKind = kind,
                    RemoteUrl = remoteUrl,
                    Command = command,
                    Args = args,
                    Category = "Registry",
                });
            }
        }
        catch
        {
            // Offline / registry down — curated list still works.
        }

        return Dedup(results).Take(limit).ToList();
    }

    private static List<McpCatalogEntry> Dedup(List<McpCatalogEntry> list)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<McpCatalogEntry>();
        foreach (var e in list)
        {
            if (!seen.Add(e.Id)) continue;
            result.Add(e);
        }
        return result;
    }

    private sealed class RegistryPage
    {
        public List<RegistryItem>? Servers { get; set; }
    }

    private sealed class RegistryItem
    {
        public RegistryServer? Server { get; set; }
        [JsonPropertyName("_meta")]
        public RegistryMeta? Meta { get; set; }
    }

    private sealed class RegistryMeta
    {
        [JsonPropertyName("io.modelcontextprotocol.registry/official")]
        public OfficialMeta? Official { get; set; }
    }

    private sealed class OfficialMeta
    {
        public bool IsLatest { get; set; } = true;
        public string? Status { get; set; }
    }

    private sealed class RegistryServer
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? WebsiteUrl { get; set; }
        public RegistryRepo? Repository { get; set; }
        public List<RegistryRemote>? Remotes { get; set; }
        public List<RegistryPackage>? Packages { get; set; }
    }

    private sealed class RegistryRepo
    {
        public string? Url { get; set; }
    }

    private sealed class RegistryRemote
    {
        public string? Type { get; set; }
        public string? Url { get; set; }
    }

    private sealed class RegistryPackage
    {
        public string? RegistryType { get; set; }
        public string? Identifier { get; set; }
    }
}

/// <summary>Hand-picked servers with known-good install recipes (stdio / npx).</summary>
public static class McpCuratedCatalog
{
    public static IReadOnlyList<McpCatalogEntry> All { get; } =
    [
        new()
        {
            Id = "curated/filesystem",
            Title = "Filesystem",
            Description = "Read and write files under a directory you choose. Great for project-local tools.",
            Source = "curated",
            InstallKind = McpInstallKind.NpxPackage,
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-filesystem", "{{workspace}}"],
            Category = "Core",
            IsFeatured = true,
            Version = "latest",
        },
        new()
        {
            Id = "curated/github",
            Title = "GitHub",
            Description = "Repos, PRs, issues, and code search via the GitHub API. Set GITHUB_PERSONAL_ACCESS_TOKEN.",
            Source = "curated",
            InstallKind = McpInstallKind.NpxPackage,
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-github"],
            DefaultEnv = new Dictionary<string, string> { ["GITHUB_PERSONAL_ACCESS_TOKEN"] = "" },
            Category = "Dev",
            IsFeatured = true,
            Version = "latest",
        },
        new()
        {
            Id = "curated/memory",
            Title = "Memory",
            Description = "Persistent knowledge graph memory for agents across sessions.",
            Source = "curated",
            InstallKind = McpInstallKind.NpxPackage,
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-memory"],
            Category = "Core",
            IsFeatured = true,
            Version = "latest",
        },
        new()
        {
            Id = "curated/fetch",
            Title = "Fetch",
            Description = "Fetch and extract content from web URLs for research.",
            Source = "curated",
            InstallKind = McpInstallKind.NpxPackage,
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-fetch"],
            Category = "Web",
            IsFeatured = true,
            Version = "latest",
        },
        new()
        {
            Id = "curated/puppeteer",
            Title = "Puppeteer",
            Description = "Browser automation — navigate pages, take screenshots, click elements.",
            Source = "curated",
            InstallKind = McpInstallKind.NpxPackage,
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-puppeteer"],
            Category = "Web",
            IsFeatured = true,
            Version = "latest",
        },
        new()
        {
            Id = "curated/sqlite",
            Title = "SQLite",
            Description = "Query a local SQLite database. Pass the DB path as an arg after install if needed.",
            Source = "curated",
            InstallKind = McpInstallKind.NpxPackage,
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-sqlite", "{{workspace}}/data.db"],
            Category = "Data",
            Version = "latest",
        },
        new()
        {
            Id = "curated/git",
            Title = "Git",
            Description = "Git status, log, and repository tools against a local folder.",
            Source = "curated",
            InstallKind = McpInstallKind.NpxPackage,
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-git", "--repository", "{{workspace}}"],
            Category = "Dev",
            Version = "latest",
        },
        new()
        {
            Id = "curated/brave-search",
            Title = "Brave Search",
            Description = "Web search via Brave. Set BRAVE_API_KEY in env after install.",
            Source = "curated",
            InstallKind = McpInstallKind.NpxPackage,
            Command = "npx",
            Args = ["-y", "@modelcontextprotocol/server-brave-search"],
            DefaultEnv = new Dictionary<string, string> { ["BRAVE_API_KEY"] = "" },
            Category = "Web",
            Version = "latest",
        },
    ];
}

/// <summary>Persists installs and syncs them into Grok Build's config.toml.</summary>
public sealed class McpInstallManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<McpInstalledServer> LoadInstalled()
    {
        try
        {
            var path = StorePath();
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<McpInstalledServer>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch { return []; }
    }

    public void SaveInstalled(IReadOnlyList<McpInstalledServer> servers)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(servers, JsonOptions));
        SyncToGrokConfig(servers);
    }

    public McpInstalledServer Install(McpCatalogEntry entry, string? workspacePath = null, IDictionary<string, string>? envOverrides = null)
    {
        var list = LoadInstalled().ToList();
        var existing = list.FirstOrDefault(s => string.Equals(s.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Enabled = true;
            existing.Title = entry.Title;
            existing.Description = entry.Description;
            ApplyEntry(existing, entry, workspacePath, envOverrides);
            SaveInstalled(list);
            return existing;
        }

        var installed = new McpInstalledServer
        {
            Id = entry.Id,
            Title = entry.Title,
            Description = entry.Description,
            Kind = entry.InstallKind,
            Enabled = true,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        ApplyEntry(installed, entry, workspacePath, envOverrides);
        list.Add(installed);
        SaveInstalled(list);
        return installed;
    }

    public void Uninstall(string id)
    {
        var list = LoadInstalled().Where(s => !string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
        SaveInstalled(list);
    }

    public void SetEnabled(string id, bool enabled)
    {
        var list = LoadInstalled().ToList();
        var item = list.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        item.Enabled = enabled;
        SaveInstalled(list);
    }

    public bool IsInstalled(string id) =>
        LoadInstalled().Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string GrokConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "config.toml");

    public static string StorePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "mcp-installs.json");

    /// <summary>Writes [mcp_servers.*] blocks for Luma-managed servers into ~/.grok/config.toml.</summary>
    public void SyncToGrokConfig(IReadOnlyList<McpInstalledServer>? servers = null)
    {
        servers ??= LoadInstalled();
        var path = GrokConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        var without = StripManagedBlocks(existing);
        var builder = new StringBuilder(without.TrimEnd());
        if (builder.Length > 0) builder.AppendLine().AppendLine();

        builder.AppendLine("# --- Luma MCP Marketplace (managed) ---");
        foreach (var server in servers.Where(s => s.Enabled))
        {
            var key = server.ConfigSectionName;
            builder.AppendLine($"[mcp_servers.{key}]");
            builder.AppendLine("enabled = true");
            if (server.Kind == McpInstallKind.RemoteHttp && !string.IsNullOrWhiteSpace(server.RemoteUrl))
            {
                builder.AppendLine($"url = \"{EscapeToml(server.RemoteUrl)}\"");
            }
            else
            {
                builder.AppendLine($"command = \"{EscapeToml(server.Command ?? "npx")}\"");
                if (server.Args.Count > 0)
                {
                    builder.Append("args = [");
                    builder.Append(string.Join(", ", server.Args.Select(a => $"\"{EscapeToml(a)}\"")));
                    builder.AppendLine("]");
                }
            }
            if (server.Env.Count > 0)
            {
                builder.Append("env = { ");
                builder.Append(string.Join(", ", server.Env
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                    .Select(kv => $"{kv.Key} = \"{EscapeToml(kv.Value)}\"")));
                builder.AppendLine(" }");
            }
            builder.AppendLine();
        }
        builder.AppendLine("# --- end Luma MCP Marketplace ---");

        File.WriteAllText(path, builder.ToString());
    }

    private static void ApplyEntry(McpInstalledServer target, McpCatalogEntry entry, string? workspace, IDictionary<string, string>? envOverrides)
    {
        target.Kind = entry.InstallKind;
        target.RemoteUrl = entry.RemoteUrl;
        target.Command = entry.Command;
        var args = entry.Args.Select(a => Expand(a, workspace)).ToList();
        target.Args = args;
        target.Env = new Dictionary<string, string>(entry.DefaultEnv, StringComparer.OrdinalIgnoreCase);
        if (envOverrides is not null)
            foreach (var kv in envOverrides)
                target.Env[kv.Key] = kv.Value;
    }

    private static string Expand(string value, string? workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace)) workspace = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return value.Replace("{{workspace}}", workspace, StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeToml(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string StripManagedBlocks(string toml)
    {
        const string start = "# --- Luma MCP Marketplace (managed) ---";
        const string end = "# --- end Luma MCP Marketplace ---";
        while (true)
        {
            var i = toml.IndexOf(start, StringComparison.Ordinal);
            if (i < 0) break;
            var j = toml.IndexOf(end, i, StringComparison.Ordinal);
            if (j < 0)
            {
                toml = toml[..i];
                break;
            }
            j += end.Length;
            while (j < toml.Length && (toml[j] == '\r' || toml[j] == '\n')) j++;
            toml = toml[..i] + toml[j..];
        }
        return toml;
    }
}
