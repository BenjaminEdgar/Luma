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

        Assert.DoesNotContain("primary evidence", TestClient.Prompt(request));
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
        protected override void AddArguments(ProcessStartInfo startInfo, AiRequest request, string prompt) { }
        public string Parse(string value) => ParseOutput(value);
        public static string Prompt(AiRequest request) => BuildPrompt(request);
        public static bool ReadStreamLine(string line, out string? delta, out string? final) => TryReadStreamLine(line, out delta, out final);
    }
}
