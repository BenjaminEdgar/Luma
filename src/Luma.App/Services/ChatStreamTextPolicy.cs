using System.Diagnostics.CodeAnalysis;

namespace Luma.App.Services;

/// <summary>
/// Pure stream-apply / finalization policy for assistant answer text.
/// Partials publish progressive display text only; full ASK_USER extract + IsQuestion
/// promotion happens only on finalize so incomplete fragments cannot open the question popup.
/// </summary>
public static class ChatStreamTextPolicy
{
    /// <summary>Progressive display during streaming. Never promotes IsQuestion.</summary>
    public static AppliedStreamText ApplyPartial(string rawText)
    {
        var cleaned = TextSanitizer.Clean(rawText);
        // Strip a complete ASK_USER / NEED_SCREEN line for nicer progressive display only —
        // do not report the question (that waits for finalize).
        cleaned = ClarifyingQuestionParser.RemoveScreenRereadDirective(cleaned);
        var extracted = ClarifyingQuestionParser.ExtractDetailed(cleaned);
        return new AppliedStreamText(
            Text: TextSanitizer.Clean(extracted.Text),
            IsQuestion: false,
            Question: null,
            QuestionChoices: []);
    }

    /// <summary>Final clean + extract. Promotes IsQuestion only when a complete directive is present.</summary>
    public static AppliedStreamText ApplyFinal(string rawText)
    {
        var cleaned = TextSanitizer.Clean(rawText);
        cleaned = ClarifyingQuestionParser.RemoveScreenRereadDirective(cleaned);
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = string.Empty;
        var extracted = ClarifyingQuestionParser.ExtractDetailed(cleaned);
        var text = TextSanitizer.Clean(extracted.Text);
        if (extracted.Question is null)
            return new AppliedStreamText(text, false, null, []);

        return new AppliedStreamText(
            Text: text,
            IsQuestion: true,
            Question: TextSanitizer.Clean(extracted.Question),
            QuestionChoices: extracted.Choices);
    }
}

/// <param name="IsQuestion">True only after finalization when a complete ASK_USER directive was found.</param>
public readonly record struct AppliedStreamText(
    string Text,
    bool IsQuestion,
    string? Question,
    IReadOnlyList<string> QuestionChoices);

/// <summary>
/// Bounds rapid stream partials to at most one publish per interval while always retaining
/// the latest text for the next flush. Clock is injected so unit tests can drive bursts without timers.
/// </summary>
public sealed class StreamPartialCoalescer
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(48);

    private readonly object _gate = new();
    private readonly TimeSpan _minInterval;
    private string? _pending;
    private DateTime _lastPublishUtc = DateTime.MinValue;

    public StreamPartialCoalescer(TimeSpan? minInterval = null) =>
        _minInterval = minInterval ?? DefaultInterval;

    /// <summary>Number of successful publishes (TryPublishNow or TryFlush that returned text).</summary>
    public int PublishCount { get; private set; }

    public bool HasPending
    {
        get { lock (_gate) return _pending is not null; }
    }

    /// <summary>
    /// Records the latest partial. When the interval has elapsed since the last publish,
    /// returns that text to apply immediately; otherwise holds it for a later <see cref="TryFlush"/>.
    /// </summary>
    public bool TryPublishNow(string partial, DateTime utcNow, [NotNullWhen(true)] out string? publish)
    {
        lock (_gate)
        {
            _pending = partial;
            if (utcNow - _lastPublishUtc < _minInterval)
            {
                publish = null;
                return false;
            }

            publish = Take(utcNow);
            return true;
        }
    }

    /// <summary>Publishes any held partial (timer tick or stream end). Returns false when nothing is held.</summary>
    public bool TryFlush(DateTime utcNow, [NotNullWhen(true)] out string? publish)
    {
        lock (_gate)
        {
            if (_pending is null)
            {
                publish = null;
                return false;
            }

            publish = Take(utcNow);
            return true;
        }
    }

    private string Take(DateTime utcNow)
    {
        var text = _pending!;
        _pending = null;
        _lastPublishUtc = utcNow;
        PublishCount++;
        return text;
    }
}
