using Luma.Ocr.Geometry;

namespace Luma.Ocr.Tests;

public class OcrGeometryTests
{
    private static readonly ImageSize Size = new(200, 100);

    [Fact]
    public void ToNormalized_AndBack_RoundTripsApproximately()
    {
        var rect = new PixelRect(20, 10, 40, 30);
        var n = OcrGeometry.ToNormalized(rect, Size);
        Assert.Equal(0.1, n.X, 5);
        Assert.Equal(0.1, n.Y, 5);
        Assert.Equal(0.2, n.Width, 5);
        Assert.Equal(0.3, n.Height, 5);

        var back = OcrGeometry.ToPixel(n, Size);
        Assert.Equal(rect, back);
    }

    [Fact]
    public void FromQuad_BuildsAxisAlignedBounds()
    {
        var quad = new PixelQuad(
            new PixelPoint(10, 20),
            new PixelPoint(50, 20),
            new PixelPoint(50, 40),
            new PixelPoint(10, 40));

        var bounds = OcrGeometry.FromQuad(quad);
        Assert.Equal(new PixelRect(10, 20, 40, 20), bounds);
    }

    [Fact]
    public void FromQuad_HandlesRotatedCorners()
    {
        var quad = new PixelQuad(
            new PixelPoint(20, 10),
            new PixelPoint(40, 20),
            new PixelPoint(30, 40),
            new PixelPoint(10, 30));

        var bounds = OcrGeometry.FromQuad(quad);
        Assert.Equal(new PixelRect(10, 10, 30, 30), bounds);
    }

    [Fact]
    public void Center_And_Contains()
    {
        var rect = new PixelRect(10, 20, 40, 20);
        Assert.Equal(new PixelPoint(30, 30), OcrGeometry.Center(rect));
        Assert.True(OcrGeometry.Contains(rect, new PixelPoint(10, 20)));
        Assert.True(OcrGeometry.Contains(rect, new PixelPoint(49, 39)));
        Assert.False(OcrGeometry.Contains(rect, new PixelPoint(50, 30)));
        Assert.False(OcrGeometry.Contains(rect, new PixelPoint(30, 40)));
    }

    [Fact]
    public void Intersect_And_IoU()
    {
        var a = new PixelRect(0, 0, 50, 50);
        var b = new PixelRect(25, 25, 50, 50);
        var inter = OcrGeometry.Intersect(a, b);
        Assert.NotNull(inter);
        Assert.Equal(new PixelRect(25, 25, 25, 25), inter);

        var iou = OcrGeometry.IoU(a, b);
        // intersection 625, union 2500+2500-625 = 4375
        Assert.Equal(625.0 / 4375.0, iou, 5);
    }

    [Fact]
    public void Clamp_KeepsRectInsideImage()
    {
        var rect = new PixelRect(-10, -5, 50, 40);
        var clamped = OcrGeometry.Clamp(rect, Size);
        Assert.Equal(new PixelRect(0, 0, 40, 35), clamped);
    }

    [Fact]
    public void Offset_MovesQuadAndRect()
    {
        var rect = new PixelRect(5, 5, 10, 10);
        Assert.Equal(new PixelRect(15, 25, 10, 10), OcrGeometry.Offset(rect, 10, 20));

        var quad = new PixelQuad(
            new PixelPoint(0, 0),
            new PixelPoint(2, 0),
            new PixelPoint(2, 2),
            new PixelPoint(0, 2));
        var moved = OcrGeometry.Offset(quad, 3, 4);
        Assert.Equal(new PixelPoint(3, 4), moved.TopLeft);
        Assert.Equal(new PixelPoint(5, 6), moved.BottomRight);
    }
}
