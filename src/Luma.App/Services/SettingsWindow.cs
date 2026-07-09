using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Luma.App.Services;

/// <summary>Edits every AppSettings option. Values are written back and persisted only when
/// the user clicks Save; Escape or Cancel discards.</summary>
public sealed class SettingsWindow : Window
{
    private readonly CheckBox _capture;
    private readonly CheckBox _suggest;
    private readonly CheckBox _prewarm;
    private readonly CheckBox _globalShortcut;
    private readonly NumericUpDown _count;
    private readonly NumericUpDown _fresh;
    private readonly NumericUpDown _imageWidth;
    private readonly CheckBox _skipUnchanged;
    private readonly NumericUpDown _historyMessages;
    private readonly NumericUpDown _historyChars;
    private readonly NumericUpDown _memoryChars;
    private readonly TextBox _assistantMemory;
    private readonly TextBox _claudeChat;
    private readonly TextBox _claudeSuggest;
    private readonly TextBox _codexChat;
    private readonly TextBox _codexImage;
    private readonly TextBox _codexSuggest;
    private readonly TextBox _codexEffort;
    private readonly TextBox _grokChat;
    private readonly TextBox _grokSuggest;

    public SettingsWindow()
    {
        var settings = AppSettings.Current;
        _capture = Toggle("Capture the screen when the panel opens", settings.CaptureScreenOnOpen);
        _suggest = Toggle("Suggest prompts from what's on screen", settings.SuggestFromScreen);
        _prewarm = Toggle("Prepare suggestions when Luma starts", settings.PrewarmOnLaunch);
        _globalShortcut = Toggle("Global Ctrl+Shift+E explain shortcut", settings.EnableGlobalExplainShortcut);
        _count = Number(1, 5, settings.SuggestionCount);
        _fresh = Number(0, 3600, settings.SuggestionFreshSeconds);
        _imageWidth = Number(480, 7680, settings.SuggestionImageMaxWidth);
        _skipUnchanged = Toggle("Skip suggestions when the screen is unchanged", settings.SkipSuggestionsWhenScreenUnchanged);
        _historyMessages = Number(0, 500, settings.HistoryMessageLimit);
        _historyChars = Number(0, 200_000, settings.HistoryCharacterLimit);
        _memoryChars = Number(0, 20_000, settings.AssistantMemoryCharacterLimit);
        _assistantMemory = Multiline(settings.AssistantMemory, "Keep it short: repo path, preferences, goals, constraints");
        _claudeChat = Text(settings.ClaudeChatModel, "CLI default");
        _claudeSuggest = Text(settings.ClaudeSuggestionModel, "CLI default (claude-haiku-4-5 recommended)");
        _codexChat = Text(settings.CodexChatModel, "gpt-5.4-mini");
        _codexImage = Text(settings.CodexImageModel, "gpt-5.4-mini");
        _codexSuggest = Text(settings.CodexSuggestionModel, "gpt-5.4-mini");
        _codexEffort = Text(settings.CodexSuggestionReasoningEffort, "CLI default (low recommended)");
        _grokChat = Text(settings.GrokChatModel, "CLI default (grok-4.5)");
        _grokSuggest = Text(settings.GrokSuggestionModel, "grok-composer-2.5-fast");

        Width = 470; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowDecorations = WindowDecorations.None; Topmost = true; ShowInTaskbar = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var dragHandle = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll),
            Padding = new Thickness(0, 0, 0, 2),
            Child = new TextBlock
            {
                Text = "LUMA SETTINGS", Foreground = new SolidColorBrush(Color.Parse("#B3A6FF")),
                FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 1,
            },
        };
        dragHandle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e); e.Handled = true;
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F514161E")), BorderBrush = new SolidColorBrush(Color.Parse("#408A63F5")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 18),
            BoxShadow = BoxShadows.Parse("0 16 48 0 #99000000"),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    dragHandle,
                    new ScrollViewer
                    {
                        MaxHeight = 560,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new StackPanel
                        {
                            Spacing = 9, Margin = new Thickness(0, 0, 6, 0),
                            Children =
                            {
                                Section("Screen context"),
                                _globalShortcut,
                                Hint("From any application, press Ctrl+Shift+E to select a region and explain it. Restart Luma after changing this."),
                                _capture,
                                Hint("Luma grabs a screenshot as ambient context so answers can see what you see."),
                                _suggest,
                                Hint("Sends the capture to the provider to offer one-click prompt chips."),
                                _prewarm,
                                Hint("Runs one suggestion request at launch so the first open shows chips instantly."),
                                Labeled("Suggestions to offer (1-5)", _count),
                                Labeled("Reuse suggestions for (seconds)", _fresh),
                                Hint("0 regenerates suggestions every time the panel opens; higher values reuse recent chips to save calls."),
                                Labeled("Suggestion screenshot max width (px)", _imageWidth),
                                Hint("The suggestion request ships a downscaled copy; smaller is faster and cheaper."),
                                Section("Token budget"),
                                _skipUnchanged,
                                Hint("If nothing on screen changed, existing chips are reused for free instead of asking again."),
                                Labeled("History messages per request (0 = all)", _historyMessages),
                                Labeled("Max characters per history message (0 = full)", _historyChars),
                                Hint("Every request re-sends the conversation; trimming old or long messages cuts cost on long chats."),
                                Section("Assistant memory"),
                                Labeled("Pinned memory max characters", _memoryChars),
                                _assistantMemory,
                                Hint("Use this for stable facts you want Luma to remember across launches, like a repo path, preferences, or a long-running goal."),
                                Section("Claude models"),
                                Labeled("Questions and coding", _claudeChat),
                                Labeled("Routing and suggestions", _claudeSuggest),
                                Hint("Used for automatic routing, screen suggestions, and quick replies. Choose a cheap model such as Haiku."),
                                Section("Codex models"),
                                Labeled("Questions and coding", _codexChat),
                                Labeled("Image/screenshot context", _codexImage),
                                Labeled("Routing and suggestions", _codexSuggest),
                                Labeled("Routing/suggestion reasoning effort", _codexEffort),
                                Hint("Screenshot requests fall back to the explicit configured image model instead of the Codex CLI default; effort is minimal, low, medium, or high."),
                                Section("Grok models"),
                                Labeled("Questions and coding", _grokChat),
                                Labeled("Routing and suggestions", _grokSuggest),
                                Hint("IDs from `grok models`. Leave chat blank for the CLI default. The fast composer model is used for routing and suggestions."),
                            },
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
                        Children = { MakeButton("Cancel", "ghost", Close), MakeButton("Save", "accent", SaveAndClose) },
                    },
                },
            },
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    private void SaveAndClose()
    {
        var settings = AppSettings.Current;
        settings.CaptureScreenOnOpen = _capture.IsChecked ?? true;
        settings.SuggestFromScreen = _suggest.IsChecked ?? true;
        settings.PrewarmOnLaunch = _prewarm.IsChecked ?? true;
        settings.EnableGlobalExplainShortcut = _globalShortcut.IsChecked ?? true;
        settings.SuggestionCount = (int)(_count.Value ?? 3);
        settings.SuggestionFreshSeconds = (int)(_fresh.Value ?? 90);
        settings.SuggestionImageMaxWidth = (int)(_imageWidth.Value ?? 1280);
        settings.SkipSuggestionsWhenScreenUnchanged = _skipUnchanged.IsChecked ?? true;
        settings.HistoryMessageLimit = (int)(_historyMessages.Value ?? 12);
        settings.HistoryCharacterLimit = (int)(_historyChars.Value ?? 4000);
        settings.AssistantMemoryCharacterLimit = (int)(_memoryChars.Value ?? 2000);
        settings.AssistantMemory = _assistantMemory.Text?.Trim() ?? "";
        settings.ClaudeChatModel = _claudeChat.Text?.Trim() ?? "";
        settings.ClaudeSuggestionModel = _claudeSuggest.Text?.Trim() ?? "";
        settings.CodexChatModel = _codexChat.Text?.Trim() ?? "";
        settings.CodexImageModel = _codexImage.Text?.Trim() ?? "";
        settings.CodexSuggestionModel = _codexSuggest.Text?.Trim() ?? "";
        settings.CodexSuggestionReasoningEffort = _codexEffort.Text?.Trim() ?? "";
        settings.GrokChatModel = _grokChat.Text?.Trim() ?? "";
        settings.GrokSuggestionModel = _grokSuggest.Text?.Trim() ?? "";
        settings.Save();
        Close();
    }

    private static TextBlock Section(string title) => new()
    {
        Text = title, FontSize = 12.5, FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.Parse("#C9BFFF")), Margin = new Thickness(0, 8, 0, 0),
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.5,
        Margin = new Thickness(0, -4, 0, 2),
    };

    private static CheckBox Toggle(string label, bool value) => new()
    { Content = label, IsChecked = value, FontSize = 12.5 };

    private static NumericUpDown Number(int minimum, int maximum, int value) => new()
    {
        Minimum = minimum, Maximum = maximum, Value = value, Increment = 1, FormatString = "0",
        Width = 130, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right,
    };

    private static TextBox Text(string value, string placeholder) => new()
    {
        Text = value, PlaceholderText = placeholder, FontSize = 12.5, Width = 240,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    private static TextBox Multiline(string value, string placeholder) => new()
    {
        Text = value,
        PlaceholderText = placeholder,
        FontSize = 12.5,
        Width = 240,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinLines = 4,
        MaxLines = 8,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    private static Grid Labeled(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new TextBlock
        { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private static Button MakeButton(string text, string style, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(9) };
        button.Classes.Add(style); button.Click += (_, _) => action(); return button;
    }
}
