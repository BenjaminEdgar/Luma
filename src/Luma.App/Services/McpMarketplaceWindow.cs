using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Luma.App.Controls;

namespace Luma.App.Services;

/// <summary>Luma-styled MCP marketplace browser + install manager. Syncs installs into ~/.grok/config.toml.</summary>
public sealed class McpMarketplaceWindow : Window
{
    private readonly McpRegistryClient _registry = new();
    private readonly McpInstallManager _installs = new();
    private readonly TextBox _search = new()
    {
        PlaceholderText = "Search servers, categories, packages…",
        FontSize = 13.5,
        MinHeight = 40,
        Padding = new Thickness(12, 10),
        CornerRadius = new CornerRadius(12),
        Background = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
        BorderBrush = LumaTheme.BorderAccentBrush,
        BorderThickness = new Thickness(1),
        Foreground = LumaTheme.TextBrightBrush,
        CaretBrush = LumaTheme.AccentSoftBrush,
    };
    private readonly StackPanel _marketList = new() { Spacing = 8 };
    private readonly StackPanel _installedList = new() { Spacing = 8 };
    private readonly StackPanel _categoryBar = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly StackPanel _starterBar = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private Control _starterRow = null!;
    private Control _categoryScroll = null!;
    private readonly TextBlock _status = new()
    {
        FontSize = 11.5,
        Foreground = LumaTheme.TextMutedBrush,
        Text = "Browse curated recipes + the official MCP Registry. Installs sync to Grok.",
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _liveCounts = new()
    {
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = LumaTheme.TextMutedBrush,
        Text = "—",
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ScrollViewer _marketScroll;
    private readonly ScrollViewer _installedScroll;
    private readonly Button _tabMarket;
    private readonly Button _tabInstalled;
    private bool _showingMarket = true;
    private CancellationTokenSource? _searchCts;
    private string? _workspace;
    private string _category = "All";
    private string? _starterPack; // null = all packs
    private IReadOnlyList<McpCatalogEntry> _lastEntries = [];

    public McpMarketplaceWindow(string? workspacePath = null)
    {
        _workspace = workspacePath;
        Title = "MCP Marketplace";
        Width = 780;
        Height = 700;
        MinWidth = 600;
        MinHeight = 520;
        CanResize = true;
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _tabMarket = TabButton("Marketplace", true, () => ShowTab(true));
        _tabInstalled = TabButton("Installed", false, () => ShowTab(false));

        _marketScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _marketList,
        };
        _installedScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _installedList,
            IsVisible = false,
        };

        var drag = BuildHeroHeader();
        var searchRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { _search },
        };
        var refresh = MakeButton("Refresh", "outline", () => _ = LoadMarketAsync(_search.Text));
        var openConfig = MakeButton("Config", "ghost", OpenConfigFile);
        ToolTip.SetTip(openConfig, "Open ~/.grok/config.toml");
        Grid.SetColumn(refresh, 1);
        Grid.SetColumn(openConfig, 2);
        searchRow.Children.Add(refresh);
        searchRow.Children.Add(openConfig);

        BuildStarterBar();
        BuildCategoryBar();

        _starterRow = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "STARTER PACKS",
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 1.0,
                    Foreground = LumaTheme.AccentSoftBrush,
                },
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _starterBar,
                },
            },
        };

        _categoryScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _categoryBar,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _tabMarket, _tabInstalled },
        };

        var body = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,Auto,Auto,*,Auto") };
        body.Children.Add(drag);
        Grid.SetRow(searchRow, 1);
        searchRow.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(searchRow);
        Grid.SetRow(_starterRow, 2);
        body.Children.Add(_starterRow);
        Grid.SetRow(_categoryScroll, 3);
        body.Children.Add(_categoryScroll);
        Grid.SetRow(tabs, 4);
        tabs.Margin = new Thickness(0, 10, 0, 8);
        body.Children.Add(tabs);
        var lists = new Grid();
        lists.Children.Add(_marketScroll);
        lists.Children.Add(_installedScroll);
        Grid.SetRow(lists, 5);
        body.Children.Add(lists);
        Grid.SetRow(_status, 6);
        _status.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(_status);

        Content = new Border
        {
            Background = LumaTheme.GlassFillBrush,
            BorderBrush = LumaTheme.CreatePanelBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(LumaTheme.FloatingCornerRadius),
            Padding = new Thickness(22, 18),
            BoxShadow = LumaTheme.FloatingShadow,
            Child = body,
        };

        _search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            _ = LoadMarketAsync(_search.Text);
            e.Handled = true;
        };
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                DebouncedSearch();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };

        Opened += async (_, _) =>
        {
            await LoadMarketAsync(null);
            RefreshInstalled();
            UpdateLiveCounts();
        };
    }

    private Border BuildHeroHeader()
    {
        var logoHalo = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(12),
            Background = LumaTheme.CreateAccentGradient(),
            BoxShadow = LumaTheme.SoftShadow,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new LumaLogo
            {
                Width = 22,
                Height = 22,
                ShowOrbit = false,
                StarBrush = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var titleBlock = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "MCP MARKETPLACE",
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 1.3,
                    Foreground = LumaTheme.AccentSoftBrush,
                },
                new TextBlock
                {
                    Text = McpMarketplaceUi.HeroTitle,
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = LumaTheme.TextBrightBrush,
                },
                new TextBlock
                {
                    Text = McpMarketplaceUi.HeroSubtitle,
                    FontSize = 12,
                    Foreground = LumaTheme.TextMutedBrush,
                },
            },
        };

        var countsPill = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#22FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#447C5CFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(12, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _liveCounts,
        };

        var close = MakeButton("✕", "ghost", Close);
        close.Width = 34;
        close.Height = 34;
        close.Padding = new Thickness(0);

        var top = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto"),
            ColumnSpacing = 12,
        };
        top.Children.Add(logoHalo);
        Grid.SetColumn(titleBlock, 1);
        top.Children.Add(titleBlock);
        Grid.SetColumn(countsPill, 2);
        top.Children.Add(countsPill);
        Grid.SetColumn(close, 3);
        top.Children.Add(close);

        var hero = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x28, LumaTheme.AccentStart.R, LumaTheme.AccentStart.G, LumaTheme.AccentStart.B), 0),
                    new GradientStop(Color.FromArgb(0x18, LumaTheme.AccentSoft.R, LumaTheme.AccentSoft.G, LumaTheme.AccentSoft.B), 0.45),
                    new GradientStop(Color.FromArgb(0x14, LumaTheme.AccentEnd.R, LumaTheme.AccentEnd.G, LumaTheme.AccentEnd.B), 1),
                },
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, LumaTheme.AccentSoft.R, LumaTheme.AccentSoft.G, LumaTheme.AccentSoft.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 12),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = top,
        };

        hero.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
            e.Handled = true;
        };
        return hero;
    }

    private void BuildStarterBar()
    {
        _starterBar.Children.Clear();
        var all = MakeButton("All packs", _starterPack is null ? "accent" : "outline", () =>
        {
            _starterPack = null;
            BuildStarterBar();
            RenderMarket(_lastEntries);
        });
        all.Padding = new Thickness(12, 6);
        all.FontSize = 12;
        all.CornerRadius = new CornerRadius(999);
        all.MinWidth = 0;
        _starterBar.Children.Add(all);

        foreach (var pack in McpMarketplaceUi.StarterPacks)
        {
            var isActive = string.Equals(_starterPack, pack.Id, StringComparison.OrdinalIgnoreCase);
            var chip = MakeButton(pack.Label, isActive ? "accent" : "outline", () =>
            {
                // Pack filter uses category→pack map (Dev/Testing/Cloud, Web, Data) — leave category on All.
                _starterPack = pack.Id;
                _category = "All";
                BuildStarterBar();
                BuildCategoryBar();
                RenderMarket(_lastEntries);
            });
            chip.Padding = new Thickness(14, 6);
            chip.FontSize = 12;
            chip.CornerRadius = new CornerRadius(999);
            chip.MinWidth = 0;
            ToolTip.SetTip(chip, pack.Hint);
            _starterBar.Children.Add(chip);
        }
    }

    private void BuildCategoryBar()
    {
        _categoryBar.Children.Clear();
        foreach (var cat in McpCuratedCatalog.Categories)
        {
            var isActive = string.Equals(cat, _category, StringComparison.OrdinalIgnoreCase);
            var chip = MakeButton(cat, isActive ? "accent" : "outline", () =>
            {
                _category = cat;
                // Category chips take over; clear pack so filters don't double-narrow.
                _starterPack = null;
                BuildStarterBar();
                BuildCategoryBar();
                RenderMarket(_lastEntries);
            });
            chip.Padding = new Thickness(12, 5);
            chip.FontSize = 11.5;
            chip.CornerRadius = new CornerRadius(999);
            chip.MinWidth = 0;
            _categoryBar.Children.Add(chip);
        }
    }

    private void DebouncedSearch()
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(320, cts.Token);
                await Dispatcher.UIThread.InvokeAsync(() => _ = LoadMarketAsync(_search.Text, cts.Token));
            }
            catch (OperationCanceledException) { }
        });
    }

    private void ShowTab(bool market)
    {
        _showingMarket = market;
        _marketScroll.IsVisible = market;
        _installedScroll.IsVisible = !market;
        _categoryScroll.IsVisible = market;
        _starterRow.IsVisible = market;
        StyleTab(_tabMarket, market);
        StyleTab(_tabInstalled, !market);
        UpdateTabLabels();
        if (!market) RefreshInstalled();
    }

    private void UpdateTabLabels()
    {
        var count = _installs.LoadInstalled().Count;
        _tabMarket.Content = "Marketplace";
        _tabInstalled.Content = count == 0 ? "Installed" : $"Installed ({count})";
    }

    private void UpdateLiveCounts()
    {
        var catalog = _lastEntries.Count > 0 ? _lastEntries.Count : McpCuratedCatalog.All.Count;
        var installed = _installs.LoadInstalled();
        var needKeys = McpMarketplaceUi.CountNeedKeys(installed);
        _liveCounts.Text = McpMarketplaceUi.FormatLiveCounts(catalog, installed.Count, needKeys);
        _liveCounts.Foreground = needKeys > 0
            ? new SolidColorBrush(LumaTheme.WarnText)
            : LumaTheme.TextMutedBrush;
    }

    private async Task LoadMarketAsync(string? query, CancellationToken token = default)
    {
        _status.Text = "Loading servers…";
        try
        {
            var entries = await _registry.SearchAsync(query, 60, token);
            if (token.IsCancellationRequested) return;
            _lastEntries = entries;
            RenderMarket(entries);
            var curated = entries.Count(e => e.Source == "curated");
            var registry = entries.Count - curated;
            _status.Text = $"{entries.Count} servers · {curated} curated · {registry} from registry";
            UpdateTabLabels();
            UpdateLiveCounts();
        }
        catch (Exception ex)
        {
            _lastEntries = McpCuratedCatalog.All.ToList();
            RenderMarket(_lastEntries);
            _status.Text = $"Registry offline — curated only. ({ex.Message})";
            UpdateLiveCounts();
        }
    }

    private void RenderMarket(IReadOnlyList<McpCatalogEntry> entries)
    {
        _marketList.Children.Clear();
        var filtered = FilterEntries(entries).ToList();
        var featured = filtered.Where(e => e.IsFeatured).ToList();
        var rest = filtered.Where(e => !e.IsFeatured).ToList();
        var showSpotlight = featured.Count > 0
                            && string.IsNullOrWhiteSpace(_search.Text)
                            && (_category is "All" or "Core" || _starterPack is not null);

        if (showSpotlight)
        {
            _marketList.Children.Add(SectionLabel(McpMarketplaceUi.FeaturedSection));
            foreach (var e in featured)
                _marketList.Children.Add(CatalogCard(e, spotlight: true));
            if (rest.Count > 0) _marketList.Children.Add(SectionLabel(McpMarketplaceUi.MoreSection));
        }
        else if (filtered.Count > 0)
        {
            var label = _starterPack is not null
                ? McpMarketplaceUi.StarterPacks.FirstOrDefault(p => p.Id == _starterPack)?.Label.ToUpperInvariant()
                  ?? _category.ToUpperInvariant()
                : _category == "All" ? "RESULTS" : _category.ToUpperInvariant();
            _marketList.Children.Add(SectionLabel(label ?? "RESULTS"));
        }

        foreach (var e in showSpotlight ? rest : filtered)
            _marketList.Children.Add(CatalogCard(e, spotlight: false));

        if (filtered.Count == 0)
            _marketList.Children.Add(Muted("No servers in this pack or category. Try All packs or another search."));
    }

    private IEnumerable<McpCatalogEntry> FilterEntries(IEnumerable<McpCatalogEntry> entries)
    {
        IEnumerable<McpCatalogEntry> q = entries;

        if (!string.IsNullOrWhiteSpace(_starterPack))
            q = q.Where(e => McpMarketplaceUi.IsInPack(e, _starterPack));

        if (string.Equals(_category, "All", StringComparison.OrdinalIgnoreCase))
            return q;
        if (string.Equals(_category, "Registry", StringComparison.OrdinalIgnoreCase))
            return q.Where(e => e.Source == "registry");

        // When a starter pack is active, category chip narrows further only if it matches pack family.
        return q.Where(e =>
            string.Equals(e.Category, _category, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshInstalled()
    {
        _installedList.Children.Clear();
        var installed = _installs.LoadInstalled();
        UpdateTabLabels();
        UpdateLiveCounts();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                MakeButton("Import from config", "outline", () =>
                {
                    var n = _installs.ImportFromGrokConfig();
                    RefreshInstalled();
                    _status.Text = n == 0
                        ? "No new servers found in config.toml"
                        : $"Imported {n} server{(n == 1 ? "" : "s")} from Grok config";
                }),
                MakeButton("Resync Grok", "outline", () =>
                {
                    _installs.SyncToGrokConfig();
                    _status.Text = $"Synced → {McpInstallManager.GrokConfigPath()}";
                }),
                MakeButton("Open config", "ghost", OpenConfigFile),
            },
        };
        _installedList.Children.Add(toolbar);

        if (installed.Count == 0)
        {
            RenderInstalledEmptyState();
            _status.Text = $"Config → {McpInstallManager.GrokConfigPath()}";
            return;
        }

        var missingAny = 0;
        foreach (var s in installed.OrderByDescending(s => s.Enabled).ThenByDescending(s => s.InstalledAt))
        {
            if (McpInstallManager.MissingEnvKeys(s).Count > 0) missingAny++;
            _installedList.Children.Add(InstalledCard(s));
        }

        _status.Text = missingAny > 0
            ? $"{installed.Count} installed · {missingAny} need API keys · synced to Grok"
            : $"{installed.Count} installed · synced to Grok config.toml";
    }

    private void RenderInstalledEmptyState()
    {
        _installedList.Children.Add(new TextBlock
        {
            Text = McpMarketplaceUi.InstalledEmptyTitle,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = LumaTheme.TextBrightBrush,
            Margin = new Thickness(4, 12, 4, 4),
        });
        _installedList.Children.Add(new TextBlock
        {
            Text = McpMarketplaceUi.InstalledEmptyHint,
            FontSize = 12.5,
            Foreground = LumaTheme.TextBodyBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 0, 4, 10),
        });

        var catalog = _lastEntries.Count > 0 ? _lastEntries : McpCuratedCatalog.All;
        var suggestions = McpMarketplaceUi.SuggestedFirstInstalls(catalog);
        foreach (var entry in suggestions)
        {
            var install = MakeButton("Install", "accent", () => _ = InstallAsync(entry));
            install.MinWidth = 96;
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(GlyphBadge(entry.Category, featured: entry.IsFeatured));
            var text = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = entry.Title,
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = LumaTheme.TextBrightBrush,
                    },
                    new TextBlock
                    {
                        Text = entry.Description,
                        FontSize = 12,
                        Foreground = LumaTheme.TextMutedBrush,
                        TextWrapping = TextWrapping.Wrap,
                        MaxHeight = 40,
                    },
                },
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Grid.SetColumn(install, 2);
            row.Children.Add(install);
            _installedList.Children.Add(Card(row, McpCardInstallState.Ready, spotlight: false));
        }
    }

    private Control CatalogCard(McpCatalogEntry entry, bool spotlight)
    {
        var installed = _installs.IsInstalled(entry.Id);
        var state = McpMarketplaceUi.ResolveInstallState(entry, installed);
        var kind = entry.InstallKind switch
        {
            McpInstallKind.RemoteHttp => "Remote HTTP",
            McpInstallKind.NpxPackage => "npx",
            _ => "Custom",
        };
        var category = string.IsNullOrWhiteSpace(entry.Category) ? "—" : entry.Category;

        var install = MakeButton(installed ? "Reinstall" : "Install", installed ? "outline" : "accent",
            () => _ = InstallAsync(entry));
        install.MinWidth = 96;
        var open = MakeButton("Repo", "ghost", () =>
        {
            var url = entry.RepositoryUrl ?? entry.WebsiteUrl;
            if (!string.IsNullOrWhiteSpace(url)) OpenUrl(url!);
        });
        open.IsVisible = !string.IsNullOrWhiteSpace(entry.RepositoryUrl ?? entry.WebsiteUrl);

        var titleRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"), ColumnSpacing = 10 };
        titleRow.Children.Add(GlyphBadge(entry.Category, featured: entry.IsFeatured || spotlight));
        var titles = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = entry.Title,
                    FontSize = spotlight ? 16 : 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = LumaTheme.TextBrightBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        entry.IsFeatured || spotlight ? Pill("FEATURED", accent: true) : new Control { IsVisible = false },
                        Pill(kind),
                        Pill(category),
                        StatePill(state),
                    },
                },
            },
        };
        Grid.SetColumn(titles, 1);
        titleRow.Children.Add(titles);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { open, install },
        };
        Grid.SetColumn(actions, 2);
        titleRow.Children.Add(actions);

        return Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                titleRow,
                new TextBlock
                {
                    Text = entry.Description,
                    FontSize = 12.5,
                    Foreground = LumaTheme.TextBodyBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.9,
                    MaxHeight = spotlight ? 88 : 72,
                },
                new TextBlock
                {
                    Text = entry.Id + (entry.Version is null ? "" : $" · {entry.Version}"),
                    FontSize = 10.5,
                    Foreground = LumaTheme.TextMutedBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        }, state, spotlight);
    }

    private Control InstalledCard(McpInstalledServer server)
    {
        var missing = McpInstallManager.MissingEnvKeys(server);
        var toggle = new CheckBox
        {
            Content = server.Enabled ? "On" : "Off",
            IsChecked = server.Enabled,
            FontSize = 12,
            Foreground = LumaTheme.TextBodyBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            var on = toggle.IsChecked == true;
            _installs.SetEnabled(server.Id, on);
            toggle.Content = on ? "On" : "Off";
            _status.Text = on ? $"Enabled {server.Title}" : $"Disabled {server.Title}";
        };

        var configure = MakeButton(missing.Count > 0 ? "Add keys" : "Env", "outline", () => _ = ConfigureEnvAsync(server));
        configure.IsVisible = server.Env.Count > 0 || missing.Count > 0;
        if (server.Kind != McpInstallKind.RemoteHttp)
            configure.IsVisible = true;

        var remove = MakeButton("Remove", "ghost", () =>
        {
            _installs.Uninstall(server.Id);
            RefreshInstalled();
            _status.Text = $"Removed {server.Title}";
            UpdateLiveCounts();
            if (_showingMarket) RenderMarket(_lastEntries);
        });

        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = server.Title,
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = LumaTheme.TextBrightBrush,
                },
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        Pill(server.Enabled ? "ENABLED" : "DISABLED", accent: server.Enabled),
                        missing.Count > 0 ? StatePill(McpCardInstallState.NeedsKey) : StatePill(McpCardInstallState.Installed),
                        missing.Count > 0
                            ? Pill($"MISSING: {string.Join(", ", missing)}")
                            : new Control { IsVisible = false },
                        Pill(server.ConfigSectionName),
                    },
                },
                new TextBlock
                {
                    Text = server.Kind == McpInstallKind.RemoteHttp
                        ? server.RemoteUrl ?? "remote"
                        : $"{server.Command} {string.Join(' ', server.Args)}",
                    FontSize = 11,
                    Foreground = LumaTheme.TextMutedBrush,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 40,
                },
            },
        });
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { toggle, configure, remove },
        };
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var state = missing.Count > 0 ? McpCardInstallState.NeedsKey : McpCardInstallState.Installed;
        return Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                header,
                new TextBlock
                {
                    Text = server.Description,
                    FontSize = 12.5,
                    Foreground = LumaTheme.TextBodyBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.88,
                },
            },
        }, state, spotlight: false);
    }

    private async Task InstallAsync(McpCatalogEntry entry)
    {
        try
        {
            IDictionary<string, string>? env = null;
            if (entry.DefaultEnv.Count > 0)
            {
                env = await PromptEnvAsync(
                    entry.Title,
                    entry.DefaultEnv.Keys.ToList(),
                    new Dictionary<string, string>(entry.DefaultEnv, StringComparer.OrdinalIgnoreCase),
                    optionalSkip: true);
                if (env is null)
                {
                    _status.Text = "Install cancelled.";
                    return;
                }
            }

            var server = _installs.Install(entry, _workspace, env);
            var missing = McpInstallManager.MissingEnvKeys(server);
            var delight = McpMarketplaceUi.InstallSuccessStatus(server.Title, missing);

            if (_showingMarket)
                RenderMarket(_lastEntries.Count > 0 ? _lastEntries : await _registry.SearchAsync(_search.Text));
            // Auto-flip to Installed so the new server is visible immediately.
            ShowTab(false);
            UpdateLiveCounts();

            // Set after RefreshInstalled (via ShowTab) so the unlock line is not overwritten.
            _status.Text = delight;
            _status.Foreground = LumaTheme.LiveGreenBrush;
            _ = Task.Run(async () =>
            {
                await Task.Delay(4200);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _status.Foreground = LumaTheme.TextMutedBrush;
                });
            });
        }
        catch (Exception ex)
        {
            _status.Text = $"Install failed: {ex.Message}";
            _status.Foreground = new SolidColorBrush(LumaTheme.Danger);
        }
    }

    private async Task ConfigureEnvAsync(McpInstalledServer server)
    {
        var keys = server.Env.Keys.ToList();
        if (keys.Count == 0)
            keys = ["API_KEY"];
        var result = await PromptEnvAsync(server.Title, keys, server.Env, optionalSkip: false);
        if (result is null) return;
        _installs.UpdateEnv(server.Id, result);
        RefreshInstalled();
        _status.Text = $"Updated env for {server.Title}";
        UpdateLiveCounts();
    }

    /// <summary>Simple modal for secret/env fields. Returns null if cancelled.</summary>
    private async Task<Dictionary<string, string>?> PromptEnvAsync(
        string title,
        IReadOnlyList<string> keys,
        IDictionary<string, string> existing,
        bool optionalSkip)
    {
        var fields = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Configure {title}",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = LumaTheme.TextBrightBrush,
        });
        panel.Children.Add(new TextBlock
        {
            Text = optionalSkip
                ? "Optional API keys — you can leave blank and edit later under Installed."
                : "Environment variables written into Grok config.toml (values stay local).",
            FontSize = 12,
            Foreground = LumaTheme.TextMutedBrush,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var key in keys)
        {
            existing.TryGetValue(key, out var val);
            var box = new TextBox
            {
                PlaceholderText = key,
                Text = val ?? "",
                PasswordChar = key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
                               || key.Contains("KEY", StringComparison.OrdinalIgnoreCase)
                               || key.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
                    ? '•'
                    : default,
                MinHeight = 38,
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
                BorderBrush = LumaTheme.BorderAccentBrush,
                BorderThickness = new Thickness(1),
                Foreground = LumaTheme.TextBrightBrush,
                CaretBrush = LumaTheme.AccentSoftBrush,
            };
            fields[key] = box;
            panel.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = key, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = LumaTheme.AccentSoftBrush },
                    box,
                },
            });
        }

        var dialog = new Window
        {
            Title = "Configure MCP",
            Width = 420,
            Height = Math.Clamp(220 + keys.Count * 72, 280, 520),
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var tcs = new TaskCompletionSource<Dictionary<string, string>?>();
        void Finish(Dictionary<string, string>? value)
        {
            tcs.TrySetResult(value);
            dialog.Close();
        }

        var save = MakeButton("Save", "accent", () =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in fields) map[kv.Key] = kv.Value.Text ?? "";
            Finish(map);
        });
        var cancel = MakeButton("Cancel", "ghost", () => Finish(null));
        var skip = MakeButton("Skip for now", "outline", () =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys) map[key] = existing.TryGetValue(key, out var v) ? v : "";
            Finish(map);
        });
        skip.IsVisible = optionalSkip;

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { skip, cancel, save },
        });

        dialog.Content = new Border
        {
            Background = LumaTheme.GlassFillBrush,
            BorderBrush = LumaTheme.CreatePanelBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20),
            BoxShadow = LumaTheme.FloatingShadow,
            Child = new ScrollViewer
            {
                Content = panel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(null); e.Handled = true; }
        };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private void OpenConfigFile()
    {
        try
        {
            var path = McpInstallManager.GrokConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllText(path, "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            _status.Text = $"Opened {path}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not open config: {ex.Message}";
        }
    }

    private static Border GlyphBadge(string? category, bool featured)
    {
        var glyph = McpMarketplaceUi.CategoryGlyph(category);
        return new Border
        {
            Width = featured ? 42 : 36,
            Height = featured ? 42 : 36,
            CornerRadius = new CornerRadius(12),
            Background = featured
                ? LumaTheme.CreateAccentGradient()
                : new SolidColorBrush(Color.Parse("#228A63F5")),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = featured ? 14 : 12,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private static Border StatePill(McpCardInstallState state)
    {
        var (bg, border, fg) = state switch
        {
            McpCardInstallState.Installed => ("#3322C55E", "#8822C55E", LumaTheme.LiveGreenBrush),
            McpCardInstallState.NeedsKey => (
                ColorToHex(Color.FromArgb(0x33, LumaTheme.WarnText.R, LumaTheme.WarnText.G, LumaTheme.WarnText.B)),
                ColorToHex(Color.FromArgb(0x88, LumaTheme.WarnText.R, LumaTheme.WarnText.G, LumaTheme.WarnText.B)),
                (IBrush)new SolidColorBrush(LumaTheme.WarnText)),
            _ => ("#332563EB", "#882563EB", LumaTheme.AccentSoftBrush),
        };
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(bg)),
            BorderBrush = new SolidColorBrush(Color.Parse(border)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 2),
            Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock
            {
                Text = McpMarketplaceUi.StateLabel(state),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = fg,
            },
        };
    }

    private static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Border Pill(string text, bool accent = false) => new()
    {
        Background = new SolidColorBrush(Color.Parse(accent ? "#338A63F5" : "#1AFFFFFF")),
        BorderBrush = new SolidColorBrush(Color.Parse(accent ? "#888A63F5" : "#44FFFFFF")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(8, 2),
        Margin = new Thickness(0, 0, 6, 4),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = accent ? LumaTheme.AccentSoftBrush : LumaTheme.TextMutedBrush,
        },
    };

    private static Border Card(Control child, McpCardInstallState state, bool spotlight)
    {
        var (borderHex, bgHex, thickness) = state switch
        {
            McpCardInstallState.Installed => ("#8822C55E", "#1422C55E", spotlight ? 2.0 : 1.5),
            McpCardInstallState.NeedsKey => (
                ColorToHex(Color.FromArgb(0x99, LumaTheme.WarnText.R, LumaTheme.WarnText.G, LumaTheme.WarnText.B)),
                ColorToHex(Color.FromArgb(0x18, LumaTheme.WarnText.R, LumaTheme.WarnText.G, LumaTheme.WarnText.B)),
                spotlight ? 2.0 : 1.5),
            _ when spotlight => (
                ColorToHex(Color.FromArgb(0xBB, LumaTheme.AccentSoft.R, LumaTheme.AccentSoft.G, LumaTheme.AccentSoft.B)),
                ColorToHex(Color.FromArgb(0x22, LumaTheme.AccentStart.R, LumaTheme.AccentStart.G, LumaTheme.AccentStart.B)),
                2.0),
            _ => ("#447C5CFF", "#1AFFFFFF", 1.0),
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(bgHex)),
            BorderBrush = spotlight && state == McpCardInstallState.Ready
                ? LumaTheme.CreateAccentGradient()
                : new SolidColorBrush(Color.Parse(borderHex)),
            BorderThickness = new Thickness(thickness),
            CornerRadius = new CornerRadius(spotlight ? 16 : 14),
            Padding = new Thickness(spotlight ? 16 : 14, spotlight ? 14 : 12),
            BoxShadow = spotlight ? LumaTheme.SoftShadow : default,
            Child = child,
        };
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 10.5,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 1.1,
        Foreground = LumaTheme.AccentSoftBrush,
        Margin = new Thickness(2, 8, 0, 2),
    };

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        Foreground = LumaTheme.TextMutedBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 20, 4, 0),
    };

    private static Button TabButton(string text, bool active, Action action)
    {
        var button = MakeButton(text, active ? "accent" : "outline", action);
        button.MinWidth = 110;
        return button;
    }

    private void StyleTab(Button button, bool active)
    {
        button.Classes.Clear();
        button.Classes.Add(active ? "accent" : "outline");
    }

    private static Button MakeButton(string text, string style, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8),
            CornerRadius = new CornerRadius(10),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        button.Classes.Add(style);
        button.Click += (_, _) => action();
        return button;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }
}
