using System.Diagnostics;
using System.Text;
using Luma.App.Models;
using Luma.App.Services;

namespace Luma.Tests;

/// <summary>Drives the shipped stream-apply / finalization units with multi-chunk sequences.</summary>
public sealed class ChatStreamTextPolicyTests
{
    [Fact]
    public void PartialUpdatesShowProgressiveTextWithoutPromotingQuestion()
    {
        var chunks = new[]
        {
            "This button",
            "This button opens settings",
            "This button opens settings.\nASK_USER: Which",
            "This button opens settings.\nASK_USER: Which panel?",
        };

        string? lastText = null;
        foreach (var chunk in chunks)
        {
            var applied = ChatStreamTextPolicy.ApplyPartial(chunk);
            Assert.False(applied.IsQuestion);
            Assert.Null(applied.Question);
            Assert.Empty(applied.QuestionChoices);
            lastText = applied.Text;
            Assert.False(string.IsNullOrWhiteSpace(lastText));
        }

        // Incomplete/mid-stream ASK_USER must not surface as IsQuestion; display text still grows.
        Assert.Contains("opens settings", lastText!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ASK_USER", lastText!, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalizeExtractsCompleteAskUserAndPromotesQuestion()
    {
        var raw = "I can help with that.\nASK_USER: Which approach? || Minimal || Full";
        var applied = ChatStreamTextPolicy.ApplyFinal(raw);

        Assert.True(applied.IsQuestion);
        Assert.Equal("Which approach?", applied.Question);
        Assert.Equal(["Minimal", "Full"], applied.QuestionChoices);
        Assert.Equal("I can help with that.", applied.Text);
        Assert.DoesNotContain("ASK_USER", applied.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalizeWithoutDirectiveDoesNotPromoteQuestion()
    {
        var applied = ChatStreamTextPolicy.ApplyFinal("Plain answer with no clarifying question.");
        Assert.False(applied.IsQuestion);
        Assert.Null(applied.Question);
        Assert.Equal("Plain answer with no clarifying question.", applied.Text);
    }

    [Fact]
    public void MultiChunkSequenceEndsWithFullFinalTextAndOnlyFinalPromotesQuestion()
    {
        var partials = new[]
        {
            "Sure",
            "Sure, I can draft that.",
            "Sure, I can draft that.\nASK_USER: Who is",
        };

        foreach (var p in partials)
        {
            var step = ChatStreamTextPolicy.ApplyPartial(p);
            Assert.False(step.IsQuestion);
        }

        var final = ChatStreamTextPolicy.ApplyFinal(
            "Sure, I can draft that.\nASK_USER: Who is the recipient?");
        Assert.True(final.IsQuestion);
        Assert.Equal("Who is the recipient?", final.Question);
        Assert.Equal("Sure, I can draft that.", final.Text);
    }

    [Fact]
    public void PartialStripsNeedScreenForDisplayWithoutChangingFinalization()
    {
        var partial = ChatStreamTextPolicy.ApplyPartial("Need a look.\nNEED_SCREEN: blurry capture");
        Assert.False(partial.IsQuestion);
        Assert.DoesNotContain("NEED_SCREEN", partial.Text, StringComparison.Ordinal);

        var final = ChatStreamTextPolicy.ApplyFinal("Need a look.\nNEED_SCREEN: blurry capture");
        Assert.False(final.IsQuestion);
        Assert.DoesNotContain("NEED_SCREEN", final.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CoalescerBoundsPublishCountUnderRapidPartials()
    {
        var coalescer = new StreamPartialCoalescer(TimeSpan.FromMilliseconds(50));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var published = new List<string>();

        // Burst of 20 partials within one interval — only the first should publish immediately.
        for (var i = 1; i <= 20; i++)
        {
            var text = new string('a', i);
            if (coalescer.TryPublishNow(text, t0.AddMilliseconds(i), out var pub))
                published.Add(pub);
        }

        Assert.True(coalescer.PublishCount <= 2,
            $"Expected bounded publishes under burst, got {coalescer.PublishCount}");
        Assert.True(coalescer.HasPending || published.Count >= 1);

        // Flush yields the latest held text once.
        if (coalescer.TryFlush(t0.AddMilliseconds(100), out var flushed))
            published.Add(flushed);

        Assert.Equal(new string('a', 20), published[^1]);
        Assert.True(coalescer.PublishCount < 20,
            "Coalescer must not publish one-per-partial under a rapid burst");
        Assert.True(published.Count < 20);
    }

    [Fact]
    public void CoalescerPublishesAgainAfterIntervalAndAlwaysEndsWithLatest()
    {
        var coalescer = new StreamPartialCoalescer(TimeSpan.FromMilliseconds(40));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var published = new List<string>();

        void Feed(string text, double ms)
        {
            if (coalescer.TryPublishNow(text, t0.AddMilliseconds(ms), out var pub))
                published.Add(pub);
        }

        Feed("a", 0);
        Feed("ab", 5);
        Feed("abc", 10);
        Feed("abcd", 50); // past interval
        Feed("abcde", 55);
        if (coalescer.TryFlush(t0.AddMilliseconds(100), out var last))
            published.Add(last);

        Assert.Contains("a", published);
        Assert.Equal("abcde", published[^1]);
        Assert.True(coalescer.PublishCount >= 2);
        Assert.True(coalescer.PublishCount < 5);
    }

    [Fact]
    public void PartialThenFinalPipelineMatchesExpectedAnswerAndQuestion()
    {
        // Simulates the chat turn path: coalesced progressive applies, then finalize.
        var coalescer = new StreamPartialCoalescer(TimeSpan.FromMilliseconds(30));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string display = "";
        bool wasQuestionDuringStream = false;

        var streamChunks = new[]
        {
            (0d, "Here is the fix."),
            (5d, "Here is the fix.\nASK_USER: Prefer"),
            (12d, "Here is the fix.\nASK_USER: Prefer patch or rewrite? || Patch || Rewrite"),
        };

        foreach (var (ms, chunk) in streamChunks)
        {
            if (coalescer.TryPublishNow(chunk, t0.AddMilliseconds(ms), out var pub))
            {
                var applied = ChatStreamTextPolicy.ApplyPartial(pub);
                display = applied.Text;
                wasQuestionDuringStream |= applied.IsQuestion;
            }
        }

        if (coalescer.TryFlush(t0.AddMilliseconds(80), out var held))
        {
            var applied = ChatStreamTextPolicy.ApplyPartial(held);
            display = applied.Text;
            wasQuestionDuringStream |= applied.IsQuestion;
        }

        Assert.False(wasQuestionDuringStream);
        Assert.False(string.IsNullOrWhiteSpace(display));

        var finalRaw = "Here is the fix.\nASK_USER: Prefer patch or rewrite? || Patch || Rewrite";
        var final = ChatStreamTextPolicy.ApplyFinal(finalRaw);
        Assert.True(final.IsQuestion);
        Assert.Equal("Prefer patch or rewrite?", final.Question);
        Assert.Equal(["Patch", "Rewrite"], final.QuestionChoices);
        Assert.Equal("Here is the fix.", final.Text);
    }

    [Fact]
    public void ProviderStreamDeltasAccumulateAndFeedUiApplyPath()
    {
        // Same accumulation shape as CliAiClient: each text delta appends, partial callback gets full so far.
        string[] grokLines =
        [
            "{\"type\":\"text\",\"data\":\"Here is the fix.\"}",
            "{\"type\":\"text\",\"data\":\"\\nASK_USER: Prefer patch or rewrite? || Patch || Rewrite\"}",
            "{\"type\":\"end\",\"stopReason\":\"EndTurn\"}",
        ];
        string[] claudeLines =
        [
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"Here is the fix."}}}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"\nASK_USER: Prefer patch or rewrite? || Patch || Rewrite"}}}""",
            """{"type":"result","result":"Here is the fix.\nASK_USER: Prefer patch or rewrite? || Patch || Rewrite"}""",
        ];

        foreach (var lines in new[] { grokLines, claudeLines })
        {
            var streamed = new StringBuilder();
            string? final = null;
            var progressive = new List<string>();
            foreach (var line in lines)
            {
                if (!TestStreamClient.ReadStreamLine(line, out var delta, out var result)) continue;
                if (delta is not null)
                {
                    streamed.Append(delta);
                    var partial = ChatStreamTextPolicy.ApplyPartial(streamed.ToString());
                    progressive.Add(partial.Text);
                    Assert.False(partial.IsQuestion);
                }
                if (result is not null) final = result;
            }

            var full = final ?? streamed.ToString();
            Assert.Equal("Here is the fix.\nASK_USER: Prefer patch or rewrite? || Patch || Rewrite", full);
            Assert.NotEmpty(progressive);
            Assert.Contains("Here is the fix.", progressive[^1], StringComparison.Ordinal);

            var applied = ChatStreamTextPolicy.ApplyFinal(full);
            Assert.True(applied.IsQuestion);
            Assert.Equal("Prefer patch or rewrite?", applied.Question);
            Assert.Equal("Here is the fix.", applied.Text);
        }
    }

    [Fact]
    public void ChatTurnPathWiresCoalescedPartialsFinalExtractHistoryAndNonBlockingFollowUps()
    {
        var viewModel = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.cs");
        Assert.Contains("ChatStreamUiBridge", viewModel);
        Assert.Contains("streamBridge.OnPartial", viewModel);
        Assert.Contains("ChatStreamTextPolicy.ApplyPartial", viewModel);
        Assert.Contains("ApplyFinalAnswerText", viewModel);
        Assert.Contains("ChatStreamTextPolicy.ApplyFinal", viewModel);
        Assert.Contains("_ = GenerateFollowUpSuggestionsAsync()", viewModel);
        Assert.Contains("TryExtractScreenRereadReason", viewModel);
        Assert.Contains("Messages.Take(Messages.Count - 2)", viewModel);

        var mainWindow = ReadShipped("src/Luma.App/MainWindow.axaml.cs");
        Assert.Contains("nameof(ChatMessage.Text)", mainWindow);
        Assert.Contains("ScrollChatToEnd()", mainWindow);
        Assert.Contains("nameof(ChatMessage.IsQuestion)", mainWindow);
        Assert.Contains("message.IsQuestion && message.Question is not null", mainWindow);
    }

    private sealed class TestStreamClient : CliAiClient
    {
        protected override string Command => "unused";
        protected override void AddArguments(ProcessStartInfo startInfo, AiRequest request, string prompt, string sessionDirectory) { }
        public static bool ReadStreamLine(string line, out string? delta, out string? final) =>
            TryReadStreamLine(line, out delta, out final);
    }

    private static string ReadShipped(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            if (File.Exists(Path.Combine(dir.FullName, "Luma.slnx")))
            {
                var fromRoot = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(fromRoot)) return File.ReadAllText(fromRoot);
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate shipped source {relativePath}");
    }
}
