using Luma.App.Models;
using Luma.App.Services;

namespace Luma.Tests;

/// <summary>Guards the clarifying-question chat UX: model directive → message state → in-chat card wiring.</summary>
public sealed class ChatQuestionUiTests
{
    [Fact]
    public void FinalAskUserPromotesShowQuestionCardWithChoices()
    {
        var message = new ChatMessage("assistant", string.Empty);
        var applied = ChatStreamTextPolicy.ApplyFinal(
            "I can draft that.\nASK_USER: What tone? || Friendly || Formal || Brief");

        message.Text = applied.Text;
        if (applied.IsQuestion)
        {
            message.Question = applied.Question;
            message.QuestionChoices = applied.QuestionChoices;
            message.IsQuestion = true;
        }

        Assert.True(message.IsQuestion);
        Assert.True(message.ShowQuestionCard);
        Assert.True(message.HasQuestionChoices);
        Assert.Equal("What tone?", message.Question);
        Assert.Equal(["Friendly", "Formal", "Brief"], message.QuestionChoices);
        Assert.Equal("I can draft that.", message.Text);
    }

    [Fact]
    public void ClearingIsQuestionHidesQuestionCard()
    {
        var message = new ChatMessage("assistant", "Lead-in")
        {
            Question = "Which option?",
            QuestionChoices = ["A", "B"],
            IsQuestion = true,
        };
        Assert.True(message.ShowQuestionCard);

        message.IsQuestion = false;
        Assert.False(message.ShowQuestionCard);
    }

    [Fact]
    public void SystemPromptTeachesAskUserDirective()
    {
        var previous = AppSettings.Current;
        try
        {
            AppSettings.Current = new AppSettings { LeanChatMode = false };
            var prompt = BuildPrompt(new AiRequest("Help me write an email", null, null, []) { TaskKind = TaskKind.Chat });
            Assert.Contains("ASK_USER:", prompt);
            Assert.Contains("|| <choice1>", prompt);
            Assert.Contains("one clarifying question", prompt, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppSettings.Current = previous;
        }
    }

    [Fact]
    public void QuestionCardStylesExistInAppTheme()
    {
        var axaml = ReadShipped("src/Luma.App/App.axaml");
        Assert.Contains("Border.questioncard", axaml);
        Assert.Contains("Button.qchoice", axaml);
        Assert.Contains("TextBox.qanswer", axaml);
        Assert.Contains("Border.bubble.question", axaml);
        Assert.Contains("TextBlock.questionprompt", axaml);
    }

    [Fact]
    public void CollapsedQuestionsUsePopupWithoutForcingExpand()
    {
        var mainCs = ReadShipped("src/Luma.App/MainWindow.axaml.cs");
        Assert.Contains("When the dock is collapsed, keep it", mainCs, StringComparison.Ordinal);
        Assert.Contains("collapsed and use the floating prompt as the default answer surface", mainCs,
            StringComparison.Ordinal);
        var methodStart = mainCs.IndexOf("private void PresentClarifyingQuestion(", StringComparison.Ordinal);
        var methodEnd = mainCs.IndexOf("private void ClearPendingQuestion()", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = mainCs[methodStart..methodEnd];
        Assert.Contains("if (wasCollapsed)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SetExpanded(true);", method, StringComparison.Ordinal);
    }

    private static string BuildPrompt(AiRequest request)
    {
        // Same surface as CliParsingTests — exercises the shipped BuildPrompt path.
        return TestClient.Prompt(request);
    }

    private sealed class TestClient : CliAiClient
    {
        protected override string Command => "unused";
        protected override void AddArguments(System.Diagnostics.ProcessStartInfo startInfo, AiRequest request, string prompt, string sessionDirectory) { }
        public static string Prompt(AiRequest request) => BuildPrompt(request);
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
