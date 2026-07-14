using Luma.App.Services;

namespace Luma.Tests;

public sealed class OcrUiStatusTests
{
    [Theory]
    [InlineData(OcrUiPhase.Running, "OCR RUNNING")]
    [InlineData(OcrUiPhase.Ready, "OCR ready")]
    [InlineData(OcrUiPhase.Offline, "OCR offline")]
    [InlineData(OcrUiPhase.Capturing, "Capturing")]
    public void PillLabel_IsLoudAndShort(OcrUiPhase phase, string expected)
    {
        Assert.Equal(expected, OcrUiStatus.PillLabel(phase));
    }

    [Fact]
    public void RunningBanner_IsObvious()
    {
        Assert.Contains("ON-DEVICE OCR RUNNING", OcrUiStatus.BannerTitle(OcrUiPhase.Running));
        Assert.True(OcrUiStatus.IsBusyPhase(OcrUiPhase.Running));
        Assert.True(OcrUiStatus.IsBusyPhase(OcrUiPhase.Capturing));
        Assert.True(OcrUiStatus.IsAlertPhase(OcrUiPhase.Offline));
    }
}
