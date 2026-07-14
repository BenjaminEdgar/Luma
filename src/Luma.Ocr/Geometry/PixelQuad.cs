namespace Luma.Ocr.Geometry;

/// <summary>
/// Four corner points of a (possibly rotated) text region in image pixels.
/// Order: top-left, top-right, bottom-right, bottom-left when upright.
/// </summary>
public readonly record struct PixelQuad(
    PixelPoint TopLeft,
    PixelPoint TopRight,
    PixelPoint BottomRight,
    PixelPoint BottomLeft)
{
    public IEnumerable<PixelPoint> Corners
    {
        get
        {
            yield return TopLeft;
            yield return TopRight;
            yield return BottomRight;
            yield return BottomLeft;
        }
    }

    public override string ToString() =>
        $"TL{TopLeft} TR{TopRight} BR{BottomRight} BL{BottomLeft}";
}
