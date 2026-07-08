using System.Text.Json;

namespace Luma.App.Services;

/// <summary>Every user-tunable option, persisted to LocalApplicationData/Luma/settings.json.
/// Loaded once at startup into the static Current instance; the settings window mutates and
/// saves it. Blank chat model strings mean "let the provider CLI use its own default"; Codex
/// image-context requests still fall back to the explicit configured image model.</summary>
public sealed class AppSettings
{
    public static AppSettings Current { get; set; } = new();

    public int Provider { get; set; }
    public int Mode { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool EnableGlobalExplainShortcut { get; set; } = true;

    // Ambient screen context and suggestion chips.
    public bool CaptureScreenOnOpen { get; set; } = true;
    public bool SuggestFromScreen { get; set; } = true;
    public bool PrewarmOnLaunch { get; set; } = true;
    public int SuggestionCount { get; set; } = 3;
    /// <summary>0 regenerates suggestions on every panel open; higher values reuse recent chips.</summary>
    public int SuggestionFreshSeconds { get; set; } = 0;
    public int SuggestionImageMaxWidth { get; set; } = 1280;
    /// <summary>Skips regenerating chips when the screen is visually unchanged - a free reuse.</summary>
    public bool SkipSuggestionsWhenScreenUnchanged { get; set; } = true;

    // Token budget: how much conversation history each provider call re-sends.
    /// <summary>Only the most recent N messages are sent per request; 0 sends everything.</summary>
    public int HistoryMessageLimit { get; set; } = 12;
    /// <summary>Each history message is trimmed to this many characters; 0 disables trimming.</summary>
    public int HistoryCharacterLimit { get; set; } = 4000;

    // Per-provider model overrides.
    public string ClaudeChatModel { get; set; } = "";
    public string ClaudeSuggestionModel { get; set; } = "claude-haiku-4-5";
    public string CodexChatModel { get; set; } = "gpt-5.4-mini";
    public string CodexImageModel { get; set; } = "gpt-5.4-mini";
    public string CodexSuggestionModel { get; set; } = "gpt-5.4-mini";
    public string CodexSuggestionReasoningEffort { get; set; } = "low";
    public string GrokChatModel { get; set; } = "grok-build";
    public string GrokSuggestionModel { get; set; } = "grok-composer-2.5-fast";

    public static void Load()
    {
        try
        {
            var path = SettingsPath();
            if (File.Exists(path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { Current = new AppSettings(); }
        Current.Clamp();
    }

    public void Save()
    {
        Clamp();
        try
        {
            var path = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void Clamp()
    {
        SuggestionCount = Math.Clamp(SuggestionCount, 1, 5);
        SuggestionFreshSeconds = Math.Clamp(SuggestionFreshSeconds, 0, 3600);
        SuggestionImageMaxWidth = Math.Clamp(SuggestionImageMaxWidth, 480, 7680);
        HistoryMessageLimit = Math.Clamp(HistoryMessageLimit, 0, 500);
        HistoryCharacterLimit = Math.Clamp(HistoryCharacterLimit, 0, 200_000);
    }

    private static string SettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "settings.json");
}
