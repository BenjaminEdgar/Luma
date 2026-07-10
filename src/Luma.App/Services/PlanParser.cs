using System.Text.RegularExpressions;

namespace Luma.App.Services;

/// <summary>Extracts optional PLAN: blocks (or ```plan fences) from assistant replies.</summary>
public static partial class PlanParser
{
    // PLAN: then body until ASK_USER / NEED_SCREEN / SHOW_WHERE at line start, or end of text.
    [GeneratedRegex(
        @"(?:\r?\n|^)[ \t]*PLAN:[ \t]*(?:\r?\n)?(?<body>[\s\S]*?)(?=(?:\r?\n)[ \t]*(?:ASK_USER|NEED_SCREEN|SHOW_WHERE):|\z)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PlanDirective();

    [GeneratedRegex(
        @"```(?:plan|PLAN)[ \t]*\r?\n(?<body>[\s\S]*?)```",
        RegexOptions.Multiline)]
    private static partial Regex PlanFence();

    [GeneratedRegex(@"^\s*#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex TitleLine();

    [GeneratedRegex(@"^\s*[-*]\s+\[([ xX])\]\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex ChecklistItem();

    [GeneratedRegex(@"^\s*[-*]\s+(?!\[[ xX]\])(.+)$", RegexOptions.Multiline)]
    private static partial Regex BulletItem();

    /// <summary>Strips the plan directive/fence from display text and returns the plan markdown body.</summary>
    public static (string Text, string? PlanMarkdown) Extract(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (string.Empty, null);

        var fence = PlanFence().Match(raw);
        if (fence.Success)
        {
            var body = fence.Groups["body"].Value.Trim();
            var cleaned = (raw[..fence.Index] + raw[(fence.Index + fence.Length)..]).Trim();
            return (cleaned, string.IsNullOrWhiteSpace(body) ? null : body);
        }

        var match = PlanDirective().Match(raw);
        if (!match.Success) return (raw, null);

        var planBody = match.Groups["body"].Value.Trim();
        var text = (raw[..match.Index] + raw[(match.Index + match.Length)..]).Trim();
        return (text, string.IsNullOrWhiteSpace(planBody) ? null : planBody);
    }

    /// <summary>Parses plan markdown into a structured document (title + checklist/bullets).</summary>
    public static PlanDocument Parse(string planMarkdown)
    {
        var doc = new PlanDocument();
        doc.ReplaceFromMarkdown(planMarkdown);
        return doc;
    }

    public static string InferTitle(string planMarkdown)
    {
        if (string.IsNullOrWhiteSpace(planMarkdown)) return "Plan";
        var m = TitleLine().Match(planMarkdown);
        if (m.Success) return m.Groups[1].Value.Trim();
        var first = planMarkdown.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('-') && !l.StartsWith('*'));
        return string.IsNullOrWhiteSpace(first) ? "Plan" : first.TrimStart('#').Trim();
    }

    public static IReadOnlyList<PlanStep> ParseSteps(string planMarkdown)
    {
        if (string.IsNullOrWhiteSpace(planMarkdown)) return [];
        var steps = new List<PlanStep>();
        foreach (Match m in ChecklistItem().Matches(planMarkdown))
        {
            var done = m.Groups[1].Value is "x" or "X";
            var text = m.Groups[2].Value.Trim();
            if (text.Length > 0) steps.Add(new PlanStep(text, done));
        }
        if (steps.Count > 0) return steps;

        foreach (Match m in BulletItem().Matches(planMarkdown))
        {
            var text = m.Groups[1].Value.Trim();
            if (text.Length > 0) steps.Add(new PlanStep(text, Done: false));
        }
        return steps;
    }

    /// <summary>Rewrites checklist lines in plan markdown to match <paramref name="steps"/> order/state.</summary>
    public static string RewriteChecklistMarkdown(string markdown, IReadOnlyList<PlanStep> steps)
    {
        if (string.IsNullOrWhiteSpace(markdown) || steps.Count == 0) return markdown ?? string.Empty;
        var stepIndex = 0;
        return ChecklistRewrite().Replace(markdown, m =>
        {
            if (stepIndex >= steps.Count) return m.Value;
            var mark = steps[stepIndex].Done ? "x" : " ";
            stepIndex++;
            return $"{m.Groups["prefix"].Value}[{mark}]{m.Groups["suffix"].Value}";
        });
    }

    [GeneratedRegex(@"(?m)^(?<prefix>\s*[-*]\s+)\[(?:[ xX])\](?<suffix>\s+.*)$")]
    private static partial Regex ChecklistRewrite();
}

/// <summary>One checklist row in a live plan document.</summary>
public sealed record PlanStep(string Text, bool Done);

/// <summary>Editable plan shown in the floating plan window; updated as the agent streams PLAN: blocks.</summary>
public sealed class PlanDocument : System.ComponentModel.INotifyPropertyChanged
{
    private string _title = "Plan";
    private string _markdown = string.Empty;
    private IReadOnlyList<PlanStep> _steps = [];
    private string _lastActivity = "Waiting for plan...";
    private DateTimeOffset _lastActivityAt = DateTimeOffset.MinValue;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "Plan" : value.Trim();
            if (_title == next) return;
            _title = next;
            PropertyChanged?.Invoke(this, new(nameof(Title)));
        }
    }

    public string Markdown
    {
        get => _markdown;
        set
        {
            var next = value ?? string.Empty;
            if (_markdown == next) return;
            _markdown = next;
            PropertyChanged?.Invoke(this, new(nameof(Markdown)));
            PropertyChanged?.Invoke(this, new(nameof(HasContent)));
            PropertyChanged?.Invoke(this, new(nameof(CanImplement)));
            PropertyChanged?.Invoke(this, new(nameof(StepSummary)));
            PropertyChanged?.Invoke(this, new(nameof(ProgressSummary)));
        }
    }

    public IReadOnlyList<PlanStep> Steps
    {
        get => _steps;
        private set
        {
            _steps = value ?? [];
            PropertyChanged?.Invoke(this, new(nameof(Steps)));
            PropertyChanged?.Invoke(this, new(nameof(StepSummary)));
            PropertyChanged?.Invoke(this, new(nameof(ProgressSummary)));
        }
    }

    public bool HasContent => !string.IsNullOrWhiteSpace(Markdown);
    public bool CanImplement => HasContent;

    public string ProgressSummary
    {
        get
        {
            if (Steps.Count == 0)
                return HasContent ? "Editable plan" : "Waiting for plan...";
            var done = Steps.Count(s => s.Done);
            var percent = Math.Clamp((int)Math.Round(done * 100.0 / Steps.Count), 0, 100);
            return $"{done}/{Steps.Count} steps checked · {percent}% complete";
        }
    }

    public string LastActivity
    {
        get => _lastActivity;
        private set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "Waiting for plan..." : value.Trim();
            if (_lastActivity == next) return;
            _lastActivity = next;
            PropertyChanged?.Invoke(this, new(nameof(LastActivity)));
        }
    }

    public DateTimeOffset LastActivityAt
    {
        get => _lastActivityAt;
        private set
        {
            if (_lastActivityAt == value) return;
            _lastActivityAt = value;
            PropertyChanged?.Invoke(this, new(nameof(LastActivityAt)));
        }
    }

    public string StepSummary
    {
        get
        {
            if (Steps.Count == 0)
                return HasContent ? "Editable plan" : "Waiting for plan…";
            var done = Steps.Count(s => s.Done);
            return $"{done}/{Steps.Count} steps checked";
        }
    }

    public void ReplaceFromMarkdown(string? planMarkdown)
    {
        var md = (planMarkdown ?? string.Empty).Trim();
        Title = PlanParser.InferTitle(md);
        Steps = PlanParser.ParseSteps(md);
        Markdown = md;
        RecordActivity(HasContent ? $"Plan refreshed: {ProgressSummary}" : "Plan cleared");
    }

    public void Clear()
    {
        Title = "Plan";
        Steps = [];
        Markdown = string.Empty;
        RecordActivity("Plan cleared");
    }

    /// <summary>Marks a checklist step done/undone and rewrites the matching markdown checkbox.</summary>
    public void SetStepDone(int index, bool done)
    {
        if (index < 0 || index >= Steps.Count) return;
        var current = Steps[index];
        if (current.Done == done) return;

        var next = Steps.ToList();
        next[index] = current with { Done = done };
        Steps = next;
        Markdown = PlanParser.RewriteChecklistMarkdown(Markdown, next);
        RecordActivity($"{current.Text} {(done ? "checked off" : "reopened")}");
    }

    private void RecordActivity(string activity)
    {
        LastActivity = activity;
        LastActivityAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Prompts and session flag for Plan Mode (planning chat + separate plan window).</summary>
public static class PlanMode
{
    /// <summary>When true, system prompts forbid file edits and require PLAN: updates.</summary>
    public static bool Active { get; set; }

    /// <summary>When true, implement turn is live — accept PLAN: progress (checked-off steps).</summary>
    public static bool TrackingProgress { get; set; }

    public static string SystemDirective =>
        "PLAN MODE is active. Do not edit files, do not apply patches, and do not run destructive shell. " +
        "You may read the project to ask better questions. " +
        "Clarify with at most one ASK_USER question per turn when needed. " +
        "After every reply, emit the full current plan (replace, do not append) as:\n" +
        "PLAN:\n# Short title\n- [ ] concrete step\n- [ ] next step\n" +
        "Keep the plan complete and up to date from the conversation so far. " +
        "Do not start implementing until the user leaves plan mode / clicks Implement.";

    /// <summary>Injected while implementing so the plan window can check steps off mid-work.</summary>
    public static string ProgressDirective =>
        "You are implementing an approved plan. After you finish each checklist step, re-emit the full plan " +
        "(replace, do not append) so the plan window can check items off:\n" +
        "PLAN:\n# Short title\n- [x] completed step\n- [ ] remaining step\n" +
        "Mark only finished steps [x]; leave unfinished steps [ ]. " +
        "Emit a PLAN update between steps when practical, and again when fully done.";

    public static string BuildImplementPrompt(string planMarkdown)
    {
        var body = string.IsNullOrWhiteSpace(planMarkdown) ? "(empty plan)" : planMarkdown.Trim();
        return
            "Implement this approved plan now. Make the code changes with your tools. " +
            "Prefer small, focused edits; summarize what you changed. " +
            "When a structured patch for review helps, include one complete unified diff.\n\n" +
            "As you complete each checklist step, re-emit the full plan as:\n" +
            "PLAN:\n# title\n- [x] done steps\n- [ ] remaining steps\n" +
            "so the plan window can check them off. Mark only finished steps [x].\n\n" +
            "Approved plan:\n" + body;
    }

    public static string FormatChatNote(string planTitle) =>
        string.IsNullOrWhiteSpace(planTitle)
            ? "Updated the plan window."
            : $"Updated the plan: {planTitle.Trim()}";
}
