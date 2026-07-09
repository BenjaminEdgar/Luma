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
    // Expanded panel size; user-resizable via the corner grip and remembered across runs.
    public int PanelWidth { get; set; } = 640;
    public int PanelHeight { get; set; } = 800;
    /// <summary>Short persistent notes the assistant should always remember.</summary>
    public string AssistantMemory { get; set; } = "";

    // Ambient screen context and suggestion chips.
    public bool CaptureScreenOnOpen { get; set; } = true;
    public bool SuggestFromScreen { get; set; } = true;
    public bool PrewarmOnLaunch { get; set; } = true;
    public int SuggestionCount { get; set; } = 3;
    /// <summary>
    /// Reuse recent chips for this many seconds after generation (0 = always regenerate on open).
    /// Default 45 keeps reopening the dock snappy without stale forever.
    /// </summary>
    public int SuggestionFreshSeconds { get; set; } = 45;
    /// <summary>Downscale ambient capture before suggestion calls (smaller = faster tokens).</summary>
    public int SuggestionImageMaxWidth { get; set; } = 720;
    /// <summary>Skips regenerating chips when the screen is visually unchanged - a free reuse.</summary>
    public bool SkipSuggestionsWhenScreenUnchanged { get; set; } = true;

    // Token budget: how much conversation history each provider call re-sends.
    /// <summary>Only the most recent N messages are sent per request; 0 sends everything.</summary>
    public int HistoryMessageLimit { get; set; } = 8;
    /// <summary>Each history message is trimmed to this many characters; 0 disables trimming.</summary>
    public int HistoryCharacterLimit { get; set; } = 2200;
    /// <summary>Caps the pinned memory text so the assistant stays concise.</summary>
    public int AssistantMemoryCharacterLimit { get; set; } = 2000;

    // Per-provider model overrides.
    public string ClaudeChatModel { get; set; } = "";
    public string ClaudeSuggestionModel { get; set; } = "claude-haiku-4-5";
    public string CodexChatModel { get; set; } = "gpt-5.4-mini";
    public string CodexImageModel { get; set; } = "gpt-5.4-mini";
    public string CodexSuggestionModel { get; set; } = "gpt-5.4-mini";
    public string CodexSuggestionReasoningEffort { get; set; } = "low";
    /// <summary>Chat/code reasoning effort for Codex: low, medium, or high (compose picker).</summary>
    public string ChatReasoningEffort { get; set; } = "medium";
    /// <summary>Blank uses the Grok Build CLI default (currently grok-4.5). Run `grok models` for valid IDs.</summary>
    public string GrokChatModel { get; set; } = "";
    public string GrokSuggestionModel { get; set; } = "grok-composer-2.5-fast";

    // Chaos Mode — optional silliness with real utility.
    /// <summary>Enables roast / tone / debate chips and optional focus-lock on explain.</summary>
    public bool ChaosMode { get; set; }
    /// <summary>0 = normal, 1 = ELI5, 2 = staff eng.</summary>
    public int ChaosTone { get; set; }
    /// <summary>When Chaos Mode is on, Start focus lock blocks explain for this many minutes.</summary>
    public int ChaosPomodoroMinutes { get; set; } = 25;

    public static void Load()
    {
        try
        {
            var path = SettingsPath();
            if (File.Exists(path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { Current = new AppSettings(); }
        // grok-build is no longer a valid model id on current Grok Build CLIs.
        if (string.Equals(Current.GrokChatModel, "grok-build", StringComparison.OrdinalIgnoreCase))
            Current.GrokChatModel = "";
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
        PanelWidth = Math.Clamp(PanelWidth, 480, 3000);
        PanelHeight = Math.Clamp(PanelHeight, 520, 3000);
        HistoryMessageLimit = Math.Clamp(HistoryMessageLimit, 0, 500);
        HistoryCharacterLimit = Math.Clamp(HistoryCharacterLimit, 0, 200_000);
        AssistantMemoryCharacterLimit = Math.Clamp(AssistantMemoryCharacterLimit, 0, 20_000);
        ChaosTone = Math.Clamp(ChaosTone, 0, 2);
        ChaosPomodoroMinutes = Math.Clamp(ChaosPomodoroMinutes, 1, 120);
        ChatReasoningEffort = NormalizeEffort(ChatReasoningEffort, "medium");
        // Blank keeps the Codex CLI default for suggestion garnish; non-blank is normalized.
        if (!string.IsNullOrWhiteSpace(CodexSuggestionReasoningEffort))
            CodexSuggestionReasoningEffort = NormalizeEffort(CodexSuggestionReasoningEffort, "low");
        AssistantMemory = AssistantMemory.Length > AssistantMemoryCharacterLimit && AssistantMemoryCharacterLimit > 0
            ? AssistantMemory[..AssistantMemoryCharacterLimit]
            : AssistantMemory;
    }

    /// <summary>Maps free text to low/medium/high; blank keeps <paramref name="fallback"/>.</summary>
    public static string NormalizeEffort(string? value, string fallback = "medium")
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim().ToLowerInvariant();
        return v switch
        {
            "low" or "minimal" or "min" => "low",
            "med" or "medium" or "mid" => "medium",
            "high" or "max" or "xhigh" or "x-high" => "high",
            _ => fallback
        };
    }

    public static int EffortToIndex(string? effort) => NormalizeEffort(effort) switch
    {
        "low" => 0,
        "high" => 2,
        _ => 1
    };

    public static string EffortFromIndex(int index) => index switch
    {
        0 => "low",
        2 => "high",
        _ => "medium"
    };

    private static string SettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma", "settings.json");
}
