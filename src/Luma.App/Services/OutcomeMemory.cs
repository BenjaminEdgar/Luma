using System.Text.Json;

namespace Luma.App.Services;

public enum OutcomeKind
{
    Undo,
    Write,
    Note,
}

public sealed record OutcomeEntry(
    DateTimeOffset At,
    OutcomeKind Kind,
    string Summary,
    string? Detail,
    IReadOnlyList<string> Tags);

/// <summary>Tiny local log of what worked / got undone, used for “last time you…” chips.</summary>
public static class OutcomeMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const int MaxEntries = 80;
    private const int MaxChips = 4;

    public static void Record(OutcomeKind kind, string summary, string? detail = null, IEnumerable<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(summary)) return;
        try
        {
            var list = Load().ToList();
            list.Insert(0, new OutcomeEntry(
                DateTimeOffset.UtcNow,
                kind,
                summary.Trim(),
                string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
                (tags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()));
            if (list.Count > MaxEntries) list = list.Take(MaxEntries).ToList();
            Save(list);
        }
        catch { /* memory is best-effort */ }
    }

    public static IReadOnlyList<OutcomeEntry> Load()
    {
        try
        {
            var path = StorePath();
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<OutcomeEntry>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch { return []; }
    }

    /// <summary>Builds short suggestion chips from recent outcomes, optionally biased by prompt text.</summary>
    public static IReadOnlyList<string> SuggestChips(string? promptHint = null, int max = MaxChips)
    {
        var entries = Load();
        if (entries.Count == 0) return [];

        var tokens = Tokenize(promptHint);
        var ranked = entries
            .Select(e => (Entry: e, Score: Score(e, tokens)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.At)
            .Select(x => ChipFor(x.Entry))
            .Where(c => c.Length is >= 4 and <= 56)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
        return ranked;
    }

    public static string ChipFor(OutcomeEntry entry) => entry.Kind switch
    {
        OutcomeKind.Undo => "Avoid: " + Truncate(entry.Summary, 40),
        OutcomeKind.Write => "Retry: " + Truncate(entry.Summary, 40),
        _ => Truncate(entry.Summary, 48),
    };

    private static int Score(OutcomeEntry entry, HashSet<string> tokens)
    {
        var score = entry.Kind switch
        {
            OutcomeKind.Undo => 3,
            OutcomeKind.Write => 2,
            _ => 1,
        };
        // Recency boost (last 7 days).
        var ageDays = (DateTimeOffset.UtcNow - entry.At).TotalDays;
        if (ageDays < 1) score += 4;
        else if (ageDays < 7) score += 2;

        if (tokens.Count == 0) return score;
        var hay = (entry.Summary + " " + entry.Detail + " " + string.Join(' ', entry.Tags)).ToLowerInvariant();
        foreach (var t in tokens)
            if (hay.Contains(t, StringComparison.Ordinal)) score += 2;
        return score;
    }

    private static HashSet<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.ToLowerInvariant()
            .Split([' ', '\n', '\r', '\t', ',', '.', '/', '\\', ':', ';', '(', ')', '[', ']', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3)
            .Take(24)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";

    private static void Save(IReadOnlyList<OutcomeEntry> entries)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private static string StorePath() =>
        Path.Combine(GetLocalAppDataRoot(), "Luma", "outcome-memory.json");

    private static string GetLocalAppDataRoot()
    {
        var env = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        return string.IsNullOrWhiteSpace(env)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : env;
    }
}
