using Luma.App.Models;
using Luma.App.Services;
using Luma.Ocr.Geometry;
using Luma.Ocr.Results;

namespace Luma.Tests;

public sealed class LocalOcrTests
{
    [Fact]
    public void Format_IncludesTextAndNormalizedCoords()
    {
        var size = new ImageSize(200, 100);
        var bounds = new PixelRect(20, 10, 40, 20);
        var result = new OcrResult(
            size,
            "Hello",
            [
                new OcrBlock(
                    "Hello",
                    0.99f,
                    bounds,
                    OcrGeometry.ToNormalized(bounds, size),
                    new PixelQuad(bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft),
                    0),
            ],
            TimeSpan.FromMilliseconds(5));

        var text = LocalOcrPrompt.Format(result, "Focus region");
        Assert.NotNull(text);
        Assert.Contains("Hello", text);
        Assert.Contains("LOCAL OCR", text);
        Assert.Contains("@0.1,0.1,0.2,0.2", text); // 20/200, 10/100, 40/200, 20/100
    }

    [Fact]
    public void Format_EmptyResult_ReturnsNull()
    {
        var empty = new OcrResult(new ImageSize(10, 10), "", [], TimeSpan.Zero);
        Assert.Null(LocalOcrPrompt.Format(empty));
    }

    [Fact]
    public void Combine_JoinsSections()
    {
        var combined = LocalOcrPrompt.Combine("FOCUS_PART", "SCREEN_PART");
        Assert.Contains("FOCUS_PART", combined);
        Assert.Contains("SCREEN_PART", combined);
    }

    [Fact]
    public void HasModels_FalseForMissingDir()
    {
        Assert.False(LocalOcrService.HasModels(Path.Combine(Path.GetTempPath(), "luma-ocr-missing-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void BuildPrompt_IncludesLocalOcrContext()
    {
        var request = new AiRequest("What does the button say?", null, @"C:\tmp\screen.png", [])
        {
            LocalOcrContext = "LOCAL OCR — Full screen:\nFull text:\nApply",
        };
        var prompt = PromptSurface.Prompt(request);
        Assert.Contains("LOCAL OCR", prompt);
        Assert.Contains("Apply", prompt);
        Assert.Contains("On-device LOCAL OCR", prompt);
    }

    [Fact]
    public void BuildPrompt_OcrOnly_SaysNoScreenshotAttached()
    {
        var request = new AiRequest("Explain", null, null, [])
        {
            LocalOcrContext = "LOCAL OCR — Full screen:\nFull text:\nSave",
        };
        var prompt = PromptSurface.Prompt(request);
        Assert.Contains("ONLY screen evidence", prompt);
        Assert.Contains("Save", prompt);
    }

    [Fact]
    public void BuildPrompt_WithoutOcr_KeepsScreenshotInstructions()
    {
        var request = new AiRequest("Explain", null, @"C:\tmp\screen.png", []);
        var prompt = PromptSurface.Prompt(request);
        Assert.Contains("screenshot", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("On-device LOCAL OCR", prompt);
    }

    [Fact]
    public void ForProvider_StripsImagesWhenOcrPreferred()
    {
        var prevEnabled = AppSettings.Current.LocalOcrEnabled;
        var prevPrefer = AppSettings.Current.LocalOcrPreferOverVision;
        try
        {
            AppSettings.Current.LocalOcrEnabled = true;
            AppSettings.Current.LocalOcrPreferOverVision = true;
            var (r, c) = ChatCaptureAttachment.ForProvider(@"C:\a.png", @"C:\b.png", "LOCAL OCR\nFull text:\nHi");
            Assert.Null(r);
            Assert.Null(c);
            Assert.True(ScreenEvidence.PrefersOcrOverVision("LOCAL OCR\nHi"));
        }
        finally
        {
            AppSettings.Current.LocalOcrEnabled = prevEnabled;
            AppSettings.Current.LocalOcrPreferOverVision = prevPrefer;
        }
    }

    [Fact]
    public void ForProvider_KeepsImagesWhenOcrMissing()
    {
        var prevEnabled = AppSettings.Current.LocalOcrEnabled;
        var prevPrefer = AppSettings.Current.LocalOcrPreferOverVision;
        try
        {
            AppSettings.Current.LocalOcrEnabled = true;
            AppSettings.Current.LocalOcrPreferOverVision = true;
            var (r, c) = ChatCaptureAttachment.ForProvider(@"C:\a.png", @"C:\b.png", null);
            Assert.Equal(@"C:\a.png", r);
            Assert.Equal(@"C:\b.png", c);
        }
        finally
        {
            AppSettings.Current.LocalOcrEnabled = prevEnabled;
            AppSettings.Current.LocalOcrPreferOverVision = prevPrefer;
        }
    }

    [Fact]
    public void LocalSuggestions_FromOcrContext()
    {
        var ocr = "LOCAL OCR — Full screen (100×50 px, on-device):\n\nFull text:\nApply changes\nCancel\n\nBlocks (reading order):\n";
        var chips = LocalOcrSuggestions.FromOcrContext(ocr, 3);
        Assert.NotEmpty(chips);
        Assert.True(chips.Count <= 3);
        Assert.Contains(chips, c => c.Contains("Apply", StringComparison.OrdinalIgnoreCase) || c.Contains("Explain", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Exposes protected BuildPrompt via the same pattern as other tests.</summary>
    private sealed class PromptSurface : CliAiClient
    {
        public PromptSurface() : base(null) { }
        protected override string Command => "test";
        protected override void AddArguments(System.Diagnostics.ProcessStartInfo startInfo, AiRequest request, string prompt, string sessionDirectory) { }
        public static string Prompt(AiRequest request) => BuildPrompt(request);
    }
}
