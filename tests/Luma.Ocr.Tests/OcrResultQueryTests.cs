using Luma.Ocr.Geometry;
using Luma.Ocr.Results;

namespace Luma.Ocr.Tests;

public class OcrResultQueryTests
{
    private static OcrResult Sample()
    {
        var size = new ImageSize(100, 100);
        var aBounds = new PixelRect(10, 10, 30, 10);
        var bBounds = new PixelRect(50, 40, 40, 15);
        var blocks = new[]
        {
            new OcrBlock(
                "Hello",
                0.95f,
                aBounds,
                OcrGeometry.ToNormalized(aBounds, size),
                new PixelQuad(
                    aBounds.TopLeft, aBounds.TopRight, aBounds.BottomRight, aBounds.BottomLeft),
                0),
            new OcrBlock(
                "World",
                0.90f,
                bBounds,
                OcrGeometry.ToNormalized(bBounds, size),
                new PixelQuad(
                    bBounds.TopLeft, bBounds.TopRight, bBounds.BottomRight, bBounds.BottomLeft),
                1),
        };
        return new OcrResult(size, "Hello\nWorld", blocks, TimeSpan.FromMilliseconds(12));
    }

    [Fact]
    public void FindContaining_PixelAndNormalized()
    {
        var result = Sample();
        var hit = result.FindContaining(new PixelPoint(15, 12));
        Assert.NotNull(hit);
        Assert.Equal("Hello", hit!.Text);

        var miss = result.FindContaining(new PixelPoint(0, 0));
        Assert.Null(miss);

        var nHit = result.FindContaining(0.55, 0.45);
        Assert.NotNull(nHit);
        Assert.Equal("World", nHit!.Text);
    }

    [Fact]
    public void InRegion_ReturnsOverlappingBlocks()
    {
        var result = Sample();
        var region = new PixelRect(40, 35, 30, 30);
        var hits = result.InRegion(region).ToList();
        Assert.Single(hits);
        Assert.Equal("World", hits[0].Text);
    }

    [Fact]
    public void BestMatch_PrefersExactThenNearby()
    {
        var result = Sample();
        var exact = result.BestMatch("Hello");
        Assert.Equal("Hello", exact?.Text);

        var partial = result.BestMatch("Wor");
        Assert.Equal("World", partial?.Text);

        var biased = result.BestMatch("o", near: new PixelPoint(60, 45));
        // both contain 'o' case-insensitively? Hello has o, World has o
        Assert.NotNull(biased);
    }

    [Fact]
    public void ToShowWhereStyle_ReturnsNormalizedTuple()
    {
        var result = Sample();
        var (x, y, w, h) = result.Blocks[0].ToShowWhereStyle();
        Assert.Equal(0.1, x, 5);
        Assert.Equal(0.1, y, 5);
        Assert.Equal(0.3, w, 5);
        Assert.Equal(0.1, h, 5);
    }

    [Fact]
    public void GroupIntoRows_MergesSameLineAndSortsLeftToRight()
    {
        var size = new ImageSize(400, 100);
        OcrBlock Block(string text, int x, int y, int index)
        {
            var b = new PixelRect(x, y, 40, 12);
            return new OcrBlock(text, 0.9f, b, OcrGeometry.ToNormalized(b, size),
                new PixelQuad(b.TopLeft, b.TopRight, b.BottomRight, b.BottomLeft), index);
        }

        // "Edit" center-Y is 1px above "File" — reading order puts it first; rows must fix X order.
        var result = new OcrResult(size, "", [
            Block("Edit", 60, 9, 0),
            Block("File", 10, 10, 1),
            Block("View", 110, 11, 2),
            Block("Save changes", 10, 50, 3),
        ], TimeSpan.Zero);

        var rows = result.GroupIntoRows();
        Assert.Equal(2, rows.Count);
        Assert.Equal(["File", "Edit", "View"], rows[0].Select(b => b.Text));
        Assert.Equal(["Save changes"], rows[1].Select(b => b.Text));
    }
}
