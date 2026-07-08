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
}
