namespace Luma.App.Services;

public enum ChaosTone
{
    Normal = 0,
    Eli5 = 1,
    StaffEng = 2,
}

/// <summary>Prompts and labels for Chaos Mode — pure helpers, no UI.</summary>
public static class ChaosMode
{
    public const int DefaultPomodoroMinutes = 25;

    public static string ToneLabel(ChaosTone tone) => tone switch
    {
        ChaosTone.Eli5 => "ELI5",
        ChaosTone.StaffEng => "Staff eng",
        _ => "Normal",
    };

    public static string ToneChipLabel(ChaosTone tone) => $"Tone: {ToneLabel(tone)}";

    public static ChaosTone NextTone(ChaosTone tone) => tone switch
    {
        ChaosTone.Normal => ChaosTone.Eli5,
        ChaosTone.Eli5 => ChaosTone.StaffEng,
        _ => ChaosTone.Normal,
    };

    public static string? ToneDirective(ChaosTone tone) => tone switch
    {
        ChaosTone.Eli5 =>
            "CHAOS TONE — Explain like I'm 5: short words, vivid analogies, zero jargon. Still be accurate.",
        ChaosTone.StaffEng =>
            "CHAOS TONE — Staff engineer: precise trade-offs, failure modes, edge cases, and what you'd ship Monday. No fluff.",
        _ => null,
    };

    public static string RoastUiPrompt() =>
        "CHAOS MODE — Roast my UI. Be witty and specific about layout, hierarchy, contrast, copy, density, and questionable choices. " +
        "Then give 3 concrete fixes. Stay kind enough that I can still ship today.";

    public static string DebatePrompt(string topic) =>
        "CHAOS MODE — Argue with yourself about this:\n" +
        topic.Trim() + "\n\n" +
        "Structure your reply exactly as:\n" +
        "## Side A\n" +
        "(strongest case for one approach)\n" +
        "## Side B\n" +
        "(strongest case for a different approach)\n" +
        "## Verdict\n" +
        "(who wins for a pragmatic ship-today decision, 2–4 sentences)";

    public static string DualDebatePrompt(string topic, string stance) =>
        $"CHAOS MODE — You are arguing only this stance: {stance}. " +
        "Be persuasive, concrete, and a little dramatic. No both-sides hedging.\n\nTopic:\n" + topic.Trim();

    public static string FormatDualDebate(string providerA, string answerA, string providerB, string answerB) =>
        $"## {providerA} — Side A\n{answerA.Trim()}\n\n## {providerB} — Side B\n{answerB.Trim()}\n\n" +
        "## You decide\nBoth models argued hard. Pick the bits that survive contact with production.";

    public static string PomodoroBlockedMessage(TimeSpan remaining) =>
        remaining.TotalSeconds <= 0
            ? "Focus lock cleared — go explain the world."
            : $"🍅 Chaos focus lock — explain unlocks in {FormatRemaining(remaining)}. Touch grass (or your code).";

    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
            : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }
}
