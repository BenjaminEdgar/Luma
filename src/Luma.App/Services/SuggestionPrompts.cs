namespace Luma.App.Services;

/// <summary>Short, latency-friendly prompts for suggestion chips (and instant seeds while AI loads).</summary>
public static class SuggestionPrompts
{
    /// <summary>Always-safe chips shown immediately so the panel never feels empty.</summary>
    public static IReadOnlyList<string> InstantSeeds { get; } =
    [
        "Explain this screen",
        "What's important here?",
        "What should I do next?",
    ];

    /// <summary>
    /// Screen-aware chip prompt: forces a tight, parseable shape so fast models return usable lines.
    /// </summary>
    public static string FromScreen(int count)
    {
        count = Math.Clamp(count, 1, 6);
        return
            $"Look at the screenshot. Reply with exactly {count} short action chips the user might tap next.\n" +
            "Rules:\n" +
            $"- Exactly {count} lines, nothing else (no intro, no bullets, no numbering, no quotes)\n" +
            "- Each line is a verb-led request under 8 words (max ~50 characters)\n" +
            "- Specific to what is visible (error, app, dialog, code, email, etc.)\n" +
            "- Do not repeat the same idea\n" +
            "Example shape:\n" +
            "Explain this error\n" +
            "Fix the failing test\n" +
            "Summarize this dialog";
    }

    /// <summary>Follow-up chips after a chat turn — text-only, no tools.</summary>
    public static string FollowUp(int count)
    {
        count = Math.Clamp(count, 1, 6);
        return
            $"Suggest exactly {count} likely next things the user might say or ask.\n" +
            "Rules:\n" +
            $"- Exactly {count} lines only (no intro, bullets, or numbering)\n" +
            "- Each line under 8 words, verb-led when possible\n" +
            "- Grounded in the conversation so far\n" +
            "- Distinct ideas, no duplicates";
    }
}
