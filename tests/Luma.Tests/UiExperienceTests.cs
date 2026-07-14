using Luma.App.Services;

namespace Luma.Tests;

/// <summary>Gates the experiential shell redesign: shared theme, expand/collapse sequencing,
/// and structural presence of overflow-safe chat chrome in shipped sources.</summary>
public sealed class UiExperienceTests
{
    [Fact]
    public void ExpandStateHidesDockShowsPanelAndFocusesCompose()
    {
        var state = ShellExperience.Expanded(panelWidth: 640, panelHeight: 800);

        Assert.True(state.Expanded);
        Assert.False(state.DockVisible);
        Assert.True(state.PanelVisible);
        Assert.True(state.FocusCompose);
        Assert.Equal(1, state.PanelOpacity);
        Assert.Equal(ShellExperience.ExpandedTransform, state.PanelTransform);
        Assert.Equal(640, state.Width);
        Assert.Equal(800, state.Height);
        Assert.Equal(ShellExperience.ExpandedMinWidth, state.MinWidth);
        Assert.Equal(ShellExperience.ExpandedMinHeight, state.MinHeight);
    }

    [Fact]
    public void CollapseAnimatingKeepsPanelVisibleAtExpandedSizeSoTransitionsCanRun()
    {
        var state = ShellExperience.CollapseAnimating(panelWidth: 640, panelHeight: 800);

        Assert.False(state.Expanded);
        Assert.False(state.DockVisible);
        Assert.True(state.PanelVisible);
        Assert.Equal(0, state.PanelOpacity);
        Assert.Equal(ShellExperience.CollapsedTransform, state.PanelTransform);
        Assert.Equal(640, state.Width);
        Assert.Equal(800, state.Height);
        Assert.Equal(ShellExperience.ExpandedMinWidth, state.MinWidth);
        Assert.False(state.FocusCompose);
    }

    [Fact]
    public void CollapsedDockShowsDockHidesPanelAndResetsDockSize()
    {
        var state = ShellExperience.CollapsedDock();

        Assert.False(state.Expanded);
        Assert.True(state.DockVisible);
        Assert.False(state.PanelVisible);
        Assert.False(state.FocusCompose);
        Assert.Equal(0, state.PanelOpacity);
        Assert.Equal(ShellExperience.CollapsedTransform, state.PanelTransform);
        Assert.Equal(ShellExperience.DockSize, state.Width);
        Assert.Equal(ShellExperience.DockSize, state.Height);
    }

    [Fact]
    public void ExpandSeedIsTransparentScaledButAlreadyExpandedSize()
    {
        var seed = ShellExperience.ExpandSeed(700, 900);
        Assert.True(seed.PanelVisible);
        Assert.False(seed.DockVisible);
        Assert.Equal(0, seed.PanelOpacity);
        Assert.Equal(ShellExperience.CollapsedTransform, seed.PanelTransform);
        Assert.Equal(700, seed.Width);
        Assert.Equal(900, seed.Height);
        Assert.False(seed.FocusCompose);
    }

    [Fact]
    public void ExpandClampsUndersizedPanelToMinimums()
    {
        var state = ShellExperience.Expanded(panelWidth: 100, panelHeight: 50);

        Assert.Equal(ShellExperience.ExpandedMinWidth, state.Width);
        Assert.Equal(ShellExperience.ExpandedMinHeight, state.Height);
    }

    [Fact]
    public void ForExpandedLegacyHelperMatchesTerminalStates()
    {
        Assert.Equal(ShellExperience.Expanded(640, 800), ShellExperience.ForExpanded(true, 640, 800));
        Assert.Equal(ShellExperience.CollapsedDock(), ShellExperience.ForExpanded(false, 640, 800));
    }

    [Fact]
    public void ThemePaletteDefaultsToBlueAndSupportsColorfulAndEmerald()
    {
        // Default Blue: #2563EB → #38BDF8, soft #3B82F6, mist #F0F7FF.
        LumaTheme.Apply(UiThemeId.Blue, app: null);
        Assert.Equal(UiThemeId.Blue, LumaTheme.CurrentId);
        Assert.Equal(0x25, LumaTheme.AccentStart.R);
        Assert.Equal(0x63, LumaTheme.AccentStart.G);
        Assert.Equal(0xEB, LumaTheme.AccentStart.B);
        Assert.Equal(0x38, LumaTheme.AccentEnd.R);
        Assert.Equal(0xBD, LumaTheme.AccentEnd.G);
        Assert.Equal(0xF8, LumaTheme.AccentEnd.B);
        Assert.Equal(0xF0, LumaTheme.InkPanel.R);
        Assert.True(LumaTheme.FloatingCornerRadius >= 16);
        Assert.NotNull(LumaTheme.CreatePanelBorderBrush());
        Assert.NotNull(LumaTheme.CreateAccentGradient());
        Assert.NotNull(LumaTheme.GlassFillBrush);
        Assert.NotNull(LumaTheme.AccentSoftBrush);

        LumaTheme.Apply(UiThemeId.Colorful, app: null);
        Assert.Equal(UiThemeId.Colorful, LumaTheme.CurrentId);
        Assert.Equal(0x7C, LumaTheme.AccentStart.R);
        Assert.Equal(0x4D, LumaTheme.AccentStart.G);
        Assert.Equal(0xFF, LumaTheme.AccentStart.B);
        Assert.Equal(0x00, LumaTheme.AccentEnd.R);
        Assert.Equal(0xE5, LumaTheme.AccentEnd.G);

        // Emerald: #059669 → #34D399, mint mist #F0FDF8.
        LumaTheme.Apply(UiThemeId.Emerald, app: null);
        Assert.Equal(UiThemeId.Emerald, LumaTheme.CurrentId);
        Assert.Equal(0x05, LumaTheme.AccentStart.R);
        Assert.Equal(0x96, LumaTheme.AccentStart.G);
        Assert.Equal(0x69, LumaTheme.AccentStart.B);
        Assert.Equal(0x34, LumaTheme.AccentEnd.R);
        Assert.Equal(0xD3, LumaTheme.AccentEnd.G);
        Assert.Equal(0x99, LumaTheme.AccentEnd.B);
        Assert.Equal(0xF0, LumaTheme.InkPanel.R);
        Assert.Equal(0xFD, LumaTheme.InkPanel.G);
        Assert.NotNull(LumaTheme.CreatePanelFillBrush());

        Assert.Equal(3, LumaTheme.ThemeChoices.Count);
        Assert.Equal(UiThemeId.Emerald, LumaTheme.ThemeChoices[2].Id);

        // Restore default for other tests.
        LumaTheme.Apply(UiThemeId.Blue, app: null);
    }

    [Fact]
    public void ThemeSettingParsesBlueColorfulAndEmerald()
    {
        Assert.Equal(UiThemeId.Blue, LumaTheme.ParseThemeId(null));
        Assert.Equal(UiThemeId.Blue, LumaTheme.ParseThemeId("Blue"));
        Assert.Equal(UiThemeId.Colorful, LumaTheme.ParseThemeId("Colorful"));
        Assert.Equal(UiThemeId.Colorful, LumaTheme.ParseThemeId("aurora"));
        Assert.Equal(UiThemeId.Emerald, LumaTheme.ParseThemeId("Emerald"));
        Assert.Equal(UiThemeId.Emerald, LumaTheme.ParseThemeId("mint"));
        Assert.Equal(UiThemeId.Emerald, LumaTheme.ParseThemeId("2"));
        Assert.Contains("UiTheme", ReadShipped("src/Luma.App/Services/AppSettings.cs"));
        Assert.Contains("LumaTheme.Apply", ReadShipped("src/Luma.App/Services/SettingsWindow.cs"));
        Assert.Contains("ThemeChoiceIndex", ReadShipped("src/Luma.App/Services/SettingsWindow.cs"));
        Assert.Contains("ApplyFromSettings", ReadShipped("src/Luma.App/App.axaml.cs"));
    }

    [Fact]
    public void MainWindowCollapseAnimatesBeforeHidingAndResizingToDock()
    {
        var code = ReadShipped("src/Luma.App/MainWindow.axaml.cs");
        Assert.Contains("ShellExperience.CollapseAnimating", code);
        Assert.Contains("FinalizeCollapseAfterTransitionAsync", code);
        Assert.Contains("ShellExperience.PanelTransitionDuration", code);
        Assert.Contains("ShellExperience.CollapsedDock()", code);
        Assert.Contains("ShellExperience.ExpandSeed", code);
        Assert.Contains("ShellExperience.Expanded", code);
        Assert.Contains("QuestionBox.Focus()", code);
        // Collapse must not apply CollapsedDock (hide/resize) until after the transition delay.
        var finalize = code.IndexOf("FinalizeCollapseAfterTransitionAsync", StringComparison.Ordinal);
        var delay = code.IndexOf("PanelTransitionDuration", finalize, StringComparison.Ordinal);
        var dock = code.IndexOf("CollapsedDock()", finalize, StringComparison.Ordinal);
        Assert.True(delay > finalize && dock > delay,
            "CollapsedDock must be applied after waiting PanelTransitionDuration");
    }

    [Fact]
    public void MainWindowXamlAnimatesPanelAndDisablesHorizontalChatScroll()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("DoubleTransition Property=\"Opacity\"", xaml);
        Assert.Contains("TransformOperationsTransition Property=\"RenderTransform\"", xaml);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains("Classes=\"dockglow\"", xaml);
        Assert.Contains("Classes=\"chip\"", xaml);
        Assert.Contains("Classes=\"composeshell\"", xaml);
        Assert.Contains("WrapWithOverflow", xaml);
        Assert.Contains("Name=\"PanelBackground\"", xaml);
        Assert.Contains("Duration=\"0:0:0.26\"", xaml);
    }

    [Fact]
    public void AppAxamlDefinesSharedControlStylesAndPalette()
    {
        var xaml = ReadShipped("src/Luma.App/App.axaml");
        Assert.Contains("x:Key=\"AccentGradient\"", xaml);
        Assert.Contains("x:Key=\"PanelFillBrush\"", xaml);
        Assert.Contains("Selector=\"Button.accent\"", xaml);
        Assert.Contains("Selector=\"Button.ghost\"", xaml);
        Assert.Contains("Selector=\"Button.outline\"", xaml);
        Assert.Contains("Selector=\"Button.chip\"", xaml);
        Assert.Contains("Selector=\"Button.stop\"", xaml);
        Assert.Contains("Selector=\"Border.bubble\"", xaml);
        Assert.Contains("Selector=\"Border.composeshell\"", xaml);
        Assert.Contains("Selector=\"Border.dockglow\"", xaml);
        Assert.Contains("Selector=\"Ellipse.thinkingring\"", xaml);
        // Default seed palette is Blue (white → blue).
        Assert.Contains("#2563EB", xaml);
        Assert.Contains("#38BDF8", xaml);
        // Colorful + Emerald palettes live in LumaTheme for switching.
        var theme = ReadShipped("src/Luma.App/Services/LumaTheme.cs");
        Assert.Contains("#7C4DFF", theme);
        Assert.Contains("#00E5FF", theme);
        Assert.Contains("UiThemeId.Colorful", theme);
        Assert.Contains("#059669", theme);
        Assert.Contains("#34D399", theme);
        Assert.Contains("UiThemeId.Emerald", theme);
    }

    [Fact]
    public void SecondaryWindowsConsumeLumaTheme()
    {
        foreach (var relative in new[]
                 {
                     "src/Luma.App/Services/SettingsWindow.cs",
                     "src/Luma.App/Services/QuestionPromptWindow.cs",
                     "src/Luma.App/Services/ScreenChangeWindow.cs",
                     "src/Luma.App/Services/TaskWindows.cs",
                     "src/Luma.App/Services/KillTargetWindow.cs",
                     "src/Luma.App/Services/SelectionWindow.cs",
                     "src/Luma.App/Services/ScanFlashWindow.cs",
                     "src/Luma.App/Services/McpMarketplaceWindow.cs",
                     "src/Luma.App/Services/GhostCursorWindow.cs",
                 })
        {
            var source = ReadShipped(relative);
            Assert.True(source.Contains("LumaTheme."),
                $"{relative} should use LumaTheme shared design system");
        }
    }

    [Fact]
    public void ComposeBarHasPlusMenuForWorkingDirectory()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("ComposePlusButton", xaml);
        Assert.Contains("Classes=\"composemore\"", xaml);
        Assert.Contains("Set working directory", xaml);
        Assert.Contains("Clear working directory", xaml);
        Assert.Contains("OnSetWorkingDirectoryClick", xaml);
        Assert.Contains("MenuFlyout", xaml);
        Assert.Contains("HasWorkingDirectory", xaml);
        Assert.Contains("WorkingDirectoryLabel", xaml);

        var code = ReadShipped("src/Luma.App/MainWindow.axaml.cs");
        Assert.Contains("OnSetWorkingDirectoryClick", code);
        Assert.Contains("OnClearWorkingDirectoryClick", code);
        Assert.Contains("ChooseWorkingDirectoryAsync", code);
    }

    [Fact]
    public void ComposeBarHasModelAndEffortPickerOnTheRight()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("ComposeModelButton", xaml);
        Assert.Contains("Classes=\"composemodel\"", xaml);
        Assert.Contains("ModelPickerLabel", xaml);
        Assert.Contains("SelectProviderCommand", xaml);
        Assert.Contains("SelectEffortCommand", xaml);
        Assert.Contains("EffortMenuLow", xaml);
        Assert.Contains("EffortMenuHigh", xaml);
        // Provider lives in compose, not the header ComboBox.
        Assert.DoesNotContain("ItemsSource=\"{Binding Providers}\"", xaml);

        var styles = ReadShipped("src/Luma.App/App.axaml");
        Assert.Contains("Selector=\"Button.composemodel\"", styles);

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("ModelPickerLabel", vm);
        Assert.Contains("SelectedEffortIndex", vm);
        Assert.Contains("ChatReasoningEffort", vm);
    }

    [Fact]
    public void QuickActionsOfferNewChatAndBiggerExplainPartWithoutSnip()
    {
        var xaml = ReadShipped("src/Luma.App/MainWindow.axaml");
        Assert.Contains("NewChatCommand", xaml);
        Assert.Contains("New chat", xaml);
        Assert.Contains("Explain this part", xaml);
        Assert.Contains("ExplainSelectionCommand", xaml);
        Assert.DoesNotContain("Snip a region", xaml);
        Assert.DoesNotContain("Command=\"{Binding CaptureCommand}\"", xaml);

        var vm = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("NewChatCommand", vm);
        Assert.Contains("StartNewChat", vm);
        Assert.Contains("CanStartNewChat", vm);
    }

    [Fact]
    public void MarkdownAndDiffEnforceOverflowSafeWrapping()
    {
        var markdown = ReadShipped("src/Luma.App/Controls/MarkdownView.cs");
        Assert.Contains("TextWrapping.WrapWithOverflow", markdown);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", markdown);
        Assert.Contains("MaxHeight = 280", markdown);

        var diffView = ReadShipped("src/Luma.App/Controls/DiffView.cs");
        Assert.Contains("TextWrapping.WrapWithOverflow", diffView);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled", diffView);
        Assert.Contains("MaxHeight = 340", diffView);

        var diffCard = ReadShipped("src/Luma.App/Controls/DiffCardControl.cs");
        Assert.Contains("WrapWithOverflow", diffCard);
        Assert.Contains("ClipToBounds = true", diffCard);
        Assert.DoesNotContain("Apply patch", diffCard);
    }

    private static string ReadShipped(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            var alt = Path.Combine(dir.FullName, "Luma.slnx");
            if (File.Exists(alt))
            {
                var fromRoot = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(fromRoot)) return File.ReadAllText(fromRoot);
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate shipped source {relativePath}");
    }
}
