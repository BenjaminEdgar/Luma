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
    private readonly NumericUpDown _count;
    private readonly NumericUpDown _fresh;
    private readonly NumericUpDown _imageWidth;
    private readonly TextBox _claudeChat;
    private readonly TextBox _claudeSuggest;
    private readonly TextBox _codexChat;
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
        _count = Number(1, 5, settings.SuggestionCount);
        _fresh = Number(0, 3600, settings.SuggestionFreshSeconds);
        _imageWidth = Number(480, 7680, settings.SuggestionImageMaxWidth);
        _claudeChat = Text(settings.ClaudeChatModel, "CLI default");
        _claudeSuggest = Text(settings.ClaudeSuggestionModel, "CLI default (claude-haiku-4-5 recommended)");
        _codexChat = Text(settings.CodexChatModel, "CLI default");
        _codexSuggest = Text(settings.CodexSuggestionModel, "CLI default");
        _codexEffort = Text(settings.CodexSuggestionReasoningEffort, "CLI default (low recommended)");
        _grokChat = Text(settings.GrokChatModel, "grok-build");
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
                                Section("Claude models"),
                                Labeled("Questions and coding", _claudeChat),
                                Labeled("Routing and suggestions", _claudeSuggest),
                                Hint("Used for automatic routing, screen suggestions, and quick replies. Choose a cheap model such as Haiku."),
                                Section("Codex models"),
                                Labeled("Questions and coding", _codexChat),
                                Labeled("Routing and suggestions", _codexSuggest),
                                Labeled("Routing/suggestion reasoning effort", _codexEffort),
                                Hint("Model names the codex CLI accepts; effort is minimal, low, medium, or high."),
                                Section("Grok models"),
                                Labeled("Questions and coding", _grokChat),
                                Labeled("Routing and suggestions", _grokSuggest),
                                Hint("Model names the Grok Code CLI accepts. The fast composer model is used for low-cost background requests."),
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
        settings.SuggestionCount = (int)(_count.Value ?? 3);
        settings.SuggestionFreshSeconds = (int)(_fresh.Value ?? 90);
        settings.SuggestionImageMaxWidth = (int)(_imageWidth.Value ?? 1280);
        settings.ClaudeChatModel = _claudeChat.Text?.Trim() ?? "";
        settings.ClaudeSuggestionModel = _claudeSuggest.Text?.Trim() ?? "";
        settings.CodexChatModel = _codexChat.Text?.Trim() ?? "";
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
