namespace Luma.Ocr.Geometry;

/// <summary>Integer pixel point in image space (origin top-left, Y down).</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public override string ToString() => $"({X}, {Y})";
}
