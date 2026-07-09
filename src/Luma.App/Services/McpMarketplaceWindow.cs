using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Luma.App.Services;

/// <summary>Luma-styled MCP marketplace browser + install manager. Syncs installs into ~/.grok/config.toml.</summary>
public sealed class McpMarketplaceWindow : Window
{
    private readonly McpRegistryClient _registry = new();
    private readonly McpInstallManager _installs = new();
    private readonly TextBox _search = new()
    {
        PlaceholderText = "Search MCP servers…",
        FontSize = 13.5,
        MinHeight = 40,
        Padding = new Thickness(12, 10),
        CornerRadius = new CornerRadius(12),
        Background = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
        BorderBrush = new SolidColorBrush(Color.Parse("#4A8A63F5")),
        BorderThickness = new Thickness(1),
        Foreground = LumaTheme.TextBrightBrush,
        CaretBrush = LumaTheme.AccentSoftBrush,
    };
    private readonly StackPanel _marketList = new() { Spacing = 8 };
    private readonly StackPanel _installedList = new() { Spacing = 8 };
    private readonly TextBlock _status = new()
    {
        FontSize = 11.5,
        Foreground = LumaTheme.TextMutedBrush,
        Text = "Browse the official MCP Registry + curated install recipes.",
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly ScrollViewer _marketScroll;
    private readonly ScrollViewer _installedScroll;
    private readonly Button _tabMarket;
    private readonly Button _tabInstalled;
    private bool _showingMarket = true;
    private CancellationTokenSource? _searchCts;
    private string? _workspace;

    public McpMarketplaceWindow(string? workspacePath = null)
    {
        _workspace = workspacePath;
        Title = "MCP Marketplace";
        Width = 720;
        Height = 620;
        MinWidth = 560;
        MinHeight = 480;
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

        var drag = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Padding = new Thickness(0, 0, 0, 4),
            Child = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "MCP MARKETPLACE",
                                FontSize = 11,
                                FontWeight = FontWeight.SemiBold,
                                LetterSpacing = 1.3,
                                Foreground = LumaTheme.AccentSoftBrush,
                            },
                            new TextBlock
                            {
                                Text = "Discover · install · manage servers for Grok",
                                FontSize = 12,
                                Foreground = LumaTheme.TextMutedBrush,
                            },
                        },
                    },
                },
            },
        };
        var close = MakeButton("✕", "ghost", Close);
        close.Width = 34;
        close.Height = 34;
        close.Padding = new Thickness(0);
        Grid.SetColumn(close, 1);
        ((Grid)drag.Child!).Children.Add(close);
        drag.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
            e.Handled = true;
        };

        var searchRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = 8,
            Children = { _search },
        };
        var refresh = MakeButton("Refresh", "outline", () => _ = LoadMarketAsync(_search.Text));
        Grid.SetColumn(refresh, 1);
        searchRow.Children.Add(refresh);

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _tabMarket, _tabInstalled },
        };

        var body = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,*,Auto") };
        body.Children.Add(drag);
        Grid.SetRow(searchRow, 1);
        body.Children.Add(searchRow);
        Grid.SetRow(tabs, 2);
        tabs.Margin = new Thickness(0, 10, 0, 8);
        body.Children.Add(tabs);
        var lists = new Grid();
        lists.Children.Add(_marketScroll);
        lists.Children.Add(_installedScroll);
        Grid.SetRow(lists, 3);
        body.Children.Add(lists);
        Grid.SetRow(_status, 4);
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
        };
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
        StyleTab(_tabMarket, market);
        StyleTab(_tabInstalled, !market);
        if (!market) RefreshInstalled();
    }

    private async Task LoadMarketAsync(string? query, CancellationToken token = default)
    {
        _status.Text = "Loading servers…";
        _marketList.Children.Clear();
        try
        {
            var entries = await _registry.SearchAsync(query, 48, token);
            if (token.IsCancellationRequested) return;
            _marketList.Children.Clear();
            var featured = entries.Where(e => e.IsFeatured).ToList();
            var rest = entries.Where(e => !e.IsFeatured).ToList();
            if (featured.Count > 0 && string.IsNullOrWhiteSpace(query))
            {
                _marketList.Children.Add(SectionLabel("FEATURED"));
                foreach (var e in featured) _marketList.Children.Add(CatalogCard(e));
                _marketList.Children.Add(SectionLabel("REGISTRY"));
            }
            foreach (var e in rest) _marketList.Children.Add(CatalogCard(e));
            if (entries.Count == 0)
                _marketList.Children.Add(Muted("No servers matched. Try another search."));
            _status.Text = $"{entries.Count} servers · curated install recipes + official registry";
        }
        catch (Exception ex)
        {
            _status.Text = "Registry unavailable — showing curated list only.";
            _marketList.Children.Clear();
            foreach (var e in McpCuratedCatalog.All) _marketList.Children.Add(CatalogCard(e));
            if (!string.IsNullOrWhiteSpace(ex.Message))
                _status.Text += $" ({ex.Message})";
        }
    }

    private void RefreshInstalled()
    {
        _installedList.Children.Clear();
        var installed = _installs.LoadInstalled();
        if (installed.Count == 0)
        {
            _installedList.Children.Add(Muted("Nothing installed yet. Browse the Marketplace tab and hit Install."));
            _status.Text = $"Config sync → {McpInstallManager.GrokConfigPath()}";
            return;
        }
        foreach (var s in installed.OrderByDescending(s => s.InstalledAt))
            _installedList.Children.Add(InstalledCard(s));
        _status.Text = $"{installed.Count} installed · synced to Grok config.toml";
    }

    private Control CatalogCard(McpCatalogEntry entry)
    {
        var installed = _installs.IsInstalled(entry.Id);
        var badge = entry.IsFeatured ? "FEATURED" : entry.Source.ToUpperInvariant();
        var kind = entry.InstallKind switch
        {
            McpInstallKind.RemoteHttp => "Remote HTTP",
            McpInstallKind.NpxPackage => "npx",
            _ => "Custom",
        };

        var install = MakeButton(installed ? "Reinstall" : "Install", "accent", () => Install(entry));
        install.MinWidth = 96;
        var open = MakeButton("Repo", "ghost", () =>
        {
            var url = entry.RepositoryUrl ?? entry.WebsiteUrl;
            if (!string.IsNullOrWhiteSpace(url)) OpenUrl(url!);
        });
        open.IsVisible = !string.IsNullOrWhiteSpace(entry.RepositoryUrl ?? entry.WebsiteUrl);

        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = entry.Title,
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = LumaTheme.TextBrightBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"{badge} · {kind}" + (entry.Version is null ? "" : $" · {entry.Version}"),
                    FontSize = 11,
                    Foreground = LumaTheme.AccentSoftBrush,
                },
            },
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, install } };
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        return Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                header,
                new TextBlock
                {
                    Text = entry.Description,
                    FontSize = 12.5,
                    Foreground = LumaTheme.TextBodyBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.9,
                    MaxHeight = 64,
                },
                new TextBlock
                {
                    Text = entry.Id,
                    FontSize = 10.5,
                    Foreground = LumaTheme.TextMutedBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        });
    }

    private Control InstalledCard(McpInstalledServer server)
    {
        var toggle = new CheckBox
        {
            Content = server.Enabled ? "Enabled" : "Disabled",
            IsChecked = server.Enabled,
            FontSize = 12,
            Foreground = LumaTheme.TextBodyBrush,
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            var on = toggle.IsChecked == true;
            _installs.SetEnabled(server.Id, on);
            toggle.Content = on ? "Enabled" : "Disabled";
            _status.Text = on ? $"Enabled {server.Title}" : $"Disabled {server.Title}";
        };

        var remove = MakeButton("Remove", "ghost", () =>
        {
            _installs.Uninstall(server.Id);
            RefreshInstalled();
            _status.Text = $"Removed {server.Title}";
        });

        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = server.Title,
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = LumaTheme.TextBrightBrush,
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
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { toggle, remove } };
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

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
        });
    }

    private void Install(McpCatalogEntry entry)
    {
        try
        {
            var server = _installs.Install(entry, _workspace);
            _status.Text = $"Installed {server.Title} → synced to Grok ({server.ConfigSectionName})";
            if (_showingMarket) _ = LoadMarketAsync(_search.Text);
            else RefreshInstalled();
            ShowTab(false);
        }
        catch (Exception ex)
        {
            _status.Text = $"Install failed: {ex.Message}";
        }
    }

    private static Border Card(Control child) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
        BorderBrush = new SolidColorBrush(Color.Parse("#448A63F5")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(14, 12),
        Child = child,
    };

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
