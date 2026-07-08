using Avalonia;
using Luma.App.Services;

namespace Luma.Tests;

public sealed class SelectionRulesTests
{
    [Theory]
    [InlineData(24, 24, true)]
    [InlineData(100, 40, true)]
    [InlineData(23, 24, false)]
    [InlineData(24, 23, false)]
    [InlineData(0, 0, false)]
    public void ValidatesMinimumReadableRegion(int width, int height, bool expected)
    {
        Assert.Equal(expected, SelectionRules.IsUsable(new PixelRect(10, 20, width, height)));
    }

    [Fact]
    public void UnscaledSelectionMapsOneToOne()
    {
        var rect = SelectionRules.ToPhysicalRect(new Point(100, 50), new Point(300, 250), new PixelPoint(0, 0), 1.0);
        Assert.Equal(new PixelRect(100, 50, 200, 200), rect);
    }

    [Fact]
    public void ScaledDisplayMultipliesLogicalUnitsIntoPixels()
    {
        // 150% Windows scaling: logical (100,100)-(200,300) is physical (150,150) 150x300.
        var rect = SelectionRules.ToPhysicalRect(new Point(100, 100), new Point(200, 300), new PixelPoint(0, 0), 1.5);
        Assert.Equal(new PixelRect(150, 150, 150, 300), rect);
    }

    [Fact]
    public void ReversedDragNormalizes()
    {
        var rect = SelectionRules.ToPhysicalRect(new Point(200, 300), new Point(100, 100), new PixelPoint(0, 0), 1.0);
        Assert.Equal(new PixelRect(100, 100, 100, 200), rect);
    }

    [Fact]
    public void NegativeMultiMonitorOriginOffsetsAfterScaling()
    {
        // A monitor to the left of the primary starts at negative physical coordinates.
        var rect = SelectionRules.ToPhysicalRect(new Point(10, 20), new Point(110, 220), new PixelPoint(-1920, 0), 2.0);
        Assert.Equal(new PixelRect(-1920 + 20, 40, 200, 400), rect);
    }

    [Fact]
    public void HalfPixelBoundariesGrowTheSelection()
    {
        // 10 logical units at 125% is exactly 12.5 physical pixels - round up, never shave.
        var rect = SelectionRules.ToPhysicalRect(new Point(0, 0), new Point(10, 10), new PixelPoint(0, 0), 1.25);
        Assert.Equal(new PixelRect(0, 0, 13, 13), rect);
    }
}
