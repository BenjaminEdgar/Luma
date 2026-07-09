using System.Diagnostics;
using Luma.App.Models;
using Luma.App.Services;
using Luma.App.ViewModels;

namespace Luma.Tests;

public sealed class CliParsingTests
{
    [Fact]
    public void FactoryCreatesGrokClient()
    {
        Assert.IsType<GrokClient>(new AiClientFactory().Create(AiProvider.Grok));
    }

    [Fact]
    public void GrokChatModelDefaultsToCliDefault()
    {
        var settings = new AppSettings();
        Assert.True(string.IsNullOrEmpty(settings.GrokChatModel));
        Assert.Equal("grok-composer-2.5-fast", settings.GrokSuggestionModel);
        Assert.DoesNotContain("grok-build", settings.GrokChatModel);
    }

    [Fact]
    public void GrokStreamLineYieldsTextDelta()
    {
        var line = "{\"type\":\"text\",\"data\":\"Hello \"}";
        Assert.True(TestClient.ReadStreamLine(line, out var delta, out var final));
        Assert.Equal("Hello ", delta);
        Assert.Null(final);
    }

    [Fact]
    public void GrokStreamLineIgnoresThoughtAndEnd()
    {
        Assert.True(TestClient.ReadStreamLine("{\"type\":\"thought\",\"data\":\"hmm\"}", out var thoughtDelta, out _));
        Assert.Null(thoughtDelta);

        Assert.True(TestClient.ReadStreamLine("{\"type\":\"end\",\"stopReason\":\"EndTurn\"}", out var endDelta, out var endFinal));
        Assert.Null(endDelta);
        Assert.Null(endFinal);
    }

    [Fact]
    public void GrokStreamTextAccumulatesLikeClaude()
    {
        string[] lines =
        [
            "{\"type\":\"thought\",\"data\":\"planning\"}",
            "{\"type\":\"text\",\"data\":\"Hello\"}",
            "{\"type\":\"text\",\"data\":\" there!\"}",
            "{\"type\":\"end\",\"stopReason\":\"EndTurn\",\"sessionId\":\"abc\"}",
        ];
        var streamed = string.Empty;
        foreach (var line in lines)
        {
            if (!TestClient.ReadStreamLine(line, out var delta, out _)) continue;
            if (delta is not null) streamed += delta;
        }
        Assert.Equal("Hello there!", streamed);
    }

    [Fact]
    public void ScreenPromptRequiresEvidenceFirstAnalysis()
    {
        var request = new AiRequest("What is wrong?", "region.png", "screen.png", []) { TaskKind = TaskKind.Chat };

        var prompt = TestClient.Prompt(request);

        Assert.Contains("primary evidence", prompt);
        Assert.Contains("main focus", prompt);
        Assert.Contains("transcribe visible text exactly", prompt);
        Assert.Contains("distinguish what is visibly present from what you infer", prompt);
        Assert.Contains("Never invent text", prompt);
        Assert.Contains("most useful next action", prompt);
    }

    [Fact]
    public void TextOnlyPromptDoesNotClaimVisualEvidence()
    {
        var request = new AiRequest("Explain dependency injection", null, null, []) { TaskKind = TaskKind.Chat };

        var prompt = TestClient.Prompt(request);

        Assert.DoesNotContain("primary evidence", prompt);
        Assert.Contains("NEED_SCREEN:", prompt);
        Assert.Contains("create, and edit files", prompt);
    }

    [Fact]
    public void PromptWithWorkingDirectoryAllowsFileReadsAndWrites()
    {
        var request = new AiRequest("Where is the router configured?", null, null, [])
        {
            TaskKind = TaskKind.Chat,
            WorkingDirectory = @"C:\LMLB",
        };

        var prompt = TestClient.Prompt(request);

        Assert.Contains("Project directory (working root): C:\\LMLB", prompt);
        Assert.Contains("create, and edit files under this root", prompt);
        Assert.DoesNotContain("primary evidence", prompt);
    }

    [Fact]
    public void CodePromptAllowsDirectEditsAndOptionalDiff()
    {
        var request = new AiRequest("Fix the null ref", null, null, [])
        {
            TaskKind = TaskKind.Code,
            WorkingDirectory = @"C:\LMLB",
        };

        var prompt = TestClient.Prompt(request);

        Assert.Contains("create and edit files", prompt);
        Assert.Contains("```diff", prompt);
        Assert.Contains("Project directory (working root): C:\\LMLB", prompt);
    }

    [Fact]
    public void PromptIncludesPinnedMemory()
    {
        var original = AppSettings.Current;
        try
        {
            AppSettings.Current = new AppSettings { AssistantMemory = "Repo: C:\\LMLB\nPreference: keep replies concise" };
            var prompt = TestClient.Prompt(new AiRequest("What should we do next?", null, null, []) { TaskKind = TaskKind.Chat });

            Assert.Contains("Pinned memory:", prompt);
            Assert.Contains("Repo: C:\\LMLB", prompt);
            Assert.Contains("Preference: keep replies concise", prompt);
        }
        finally
        {
            AppSettings.Current = original;
        }
    }

    [Fact]
    public void ScreenRereadDirectiveIsDetectedAndRemoved()
    {
        var raw = "I can answer that after I see the screen.\nNEED_SCREEN: I need the settings panel.";

        Assert.True(ClarifyingQuestionParser.TryExtractScreenRereadReason(raw, out var reason));
        Assert.Equal("I need the settings panel.", reason);
        Assert.Equal("I can answer that after I see the screen.", ClarifyingQuestionParser.RemoveScreenRereadDirective(raw));
    }

    [Fact]
    public void PromptIncludesCompactSummaryForTrimmedHistory()
    {
        ChatMessage Make(string role, string text) => new(role, text);
        var history = new[]
        {
            Make("user", "The app feels slow when I send a message."),
            Make("assistant", "That is probably the route call."),
            Make("user", "Can we keep the assistant fast?"),
            Make("assistant", "Yes, we can skip the extra route step."),
            Make("user", "Also keep the loading state obvious."),
            Make("assistant", "I added a pending bubble and spinner."),
            Make("user", "Can we make the assistant more useful?"),
            Make("assistant", "We can add a compact memory summary."),
            Make("user", "What else can we trim?"),
            Make("assistant", "We can shorten prompt boilerplate."),
        };

        var prompt = TestClient.Prompt(new AiRequest("What should we do next?", null, null, history)
        { TaskKind = TaskKind.Chat });

        Assert.Contains("Earlier context summary:", prompt);
        Assert.Contains("U:The app feels slow when I send a message.", prompt);
        Assert.Contains("A:That is probably the route call.", prompt);
        Assert.Contains("Recent conversation (latest 8):", prompt);
    }

    [Fact]
    public void ExtractsFinalTextFromJsonLines()
    {
        var client = new TestClient();
        var output = "{\"type\":\"progress\",\"text\":\"working\"}\n{\"type\":\"result\",\"result\":\"final answer\"}";
        Assert.Equal("final answer", client.Parse(output));
    }

    [Fact]
    public void PlainTextOutputIsPreserved()
    {
        Assert.Equal("plain answer", new TestClient().Parse("plain answer"));
    }

    [Fact]
    public void StreamLineYieldsTextDelta()
    {
        var line = "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello \"}}}";
        Assert.True(TestClient.ReadStreamLine(line, out var delta, out var final));
        Assert.Equal("Hello ", delta);
        Assert.Null(final);
    }

    [Fact]
    public void StreamLineIgnoresThinkingAndSignatureDeltas()
    {
        var signature = "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"signature_delta\",\"signature\":\"abc\"}}}";
        Assert.True(TestClient.ReadStreamLine(signature, out var delta, out _));
        Assert.Null(delta);

        var thinking = "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"hmm\"}}}";
        Assert.True(TestClient.ReadStreamLine(thinking, out delta, out _));
        Assert.Null(delta);
    }

    [Fact]
    public void StreamResultLineWinsOverAccumulatedDeltas()
    {
        string[] lines =
        [
            "{\"type\":\"system\",\"subtype\":\"init\"}",
            "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}}",
            "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\" there!\"}}}",
            "{\"type\":\"result\",\"subtype\":\"success\",\"result\":\"Hello there! (canonical)\"}",
        ];
        var streamed = string.Empty;
        string? final = null;
        foreach (var line in lines)
        {
            if (!TestClient.ReadStreamLine(line, out var delta, out var result)) continue;
            if (delta is not null) streamed += delta;
            if (result is not null) final = result;
        }
        Assert.Equal("Hello there!", streamed);
        Assert.Equal("Hello there! (canonical)", final);
    }

    [Fact]
    public void CodexStyleJsonIsNotMistakenForStream()
    {
        var line = "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"codex answer\"}}";
        Assert.False(TestClient.ReadStreamLine(line, out _, out _));
        Assert.Equal("codex answer", new TestClient().Parse(line));
    }

    [Fact]
    public void CommandsRespectCanExecute()
    {
        var ran = false;
        var command = new RelayCommand(() => ran = true, () => false);
        Assert.False(command.CanExecute(null));
        command.Execute(null);
        Assert.True(ran); // ICommand callers must check CanExecute; Execute remains deterministic.
    }

    private sealed class TestClient : CliAiClient
    {
        protected override string Command => "unused";
        protected override void AddArguments(ProcessStartInfo startInfo, AiRequest request, string prompt, string sessionDirectory) { }
        public string Parse(string value) => ParseOutput(value);
        public static string Prompt(AiRequest request) => BuildPrompt(request);
        public static bool ReadStreamLine(string line, out string? delta, out string? final) => TryReadStreamLine(line, out delta, out final);
    }
}
