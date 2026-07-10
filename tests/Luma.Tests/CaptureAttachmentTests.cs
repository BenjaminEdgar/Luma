using Luma.App.Services;

namespace Luma.Tests;

/// <summary>
/// Straight chat starts text-only; the model may request NEED_SCREEN when visual evidence is required.
/// Explicit screen actions (explain screen/selection, suggestion chips) attach captures on the first request.
/// </summary>
public sealed class CaptureAttachmentTests
{
    [Fact]
    public void StraightChatFirstRequestStripsCapturesEvenWhenFilesExist()
    {
        var (region, context) = ChatCaptureAttachment.ForFirstRequest(
            attachCaptures: false, regionPath: @"C:\tmp\region.png", contextPath: @"C:\tmp\screen.png");

        Assert.Null(region);
        Assert.Null(context);
        Assert.False(ChatCaptureAttachment.HasVisual(region, context));
    }

    [Fact]
    public void ExplicitScreenTurnAttachesAvailableCaptures()
    {
        var (region, context) = ChatCaptureAttachment.ForFirstRequest(
            attachCaptures: true, regionPath: @"C:\tmp\region.png", contextPath: @"C:\tmp\screen.png");

        Assert.Equal(@"C:\tmp\region.png", region);
        Assert.Equal(@"C:\tmp\screen.png", context);
        Assert.True(ChatCaptureAttachment.HasVisual(region, context));
    }

    [Fact]
    public void ExplicitScreenTurnWithNoFilesStaysTextOnly()
    {
        var (region, context) = ChatCaptureAttachment.ForFirstRequest(
            attachCaptures: true, regionPath: null, contextPath: null);

        Assert.Null(region);
        Assert.Null(context);
    }

    [Fact]
    public void RunTurnWiresTextFirstChatAndScreenAttachCallSites()
    {
        var source = ReadShipped("src/Luma.App/ViewModels/MainWindowViewModel.Chat.cs");

        // Straight typed chat and clarifying-question continuations stay text-first.
        Assert.Contains("await RunTurnAsync(prompt, attachCaptures: false)", source);
        Assert.Contains("RunTurnAsync(answer, attachCaptures: false)", source);

        // Explicit screen intent attaches captures on the first request.
        Assert.Contains("attachCaptures: true", source);
        Assert.Contains("attachCaptures: true)", source);
        Assert.Contains("RunTurnAsync(prompt, displayPrompt: suggestion, attachCaptures: true)", source);

        // First request uses the resolved attachment paths, not raw fields unconditionally.
        Assert.Contains("ChatCaptureAttachment.ForFirstRequest", source);
        Assert.Contains("new AiRequest(prompt, region, context, history)", source);

        // NEED_SCREEN still retries with a capture when the first request was text-only.
        Assert.Contains("!sentVisual && ClarifyingQuestionParser.TryExtractScreenRereadReason", source);
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
