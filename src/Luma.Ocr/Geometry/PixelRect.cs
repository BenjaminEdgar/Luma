namespace Luma.Ocr.Geometry;

/// <summary>
/// Axis-aligned rectangle in image pixel space (origin top-left, Y down).
/// <see cref="X"/>/<see cref="Y"/> is the top-left corner; size is non-negative.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelPoint TopLeft => new(X, Y);
    public PixelPoint TopRight => new(Right, Y);
    public PixelPoint BottomLeft => new(X, Bottom);
    public PixelPoint BottomRight => new(Right, Bottom);

    public override string ToString() => $"[{X},{Y} {Width}×{Height}]";
}
