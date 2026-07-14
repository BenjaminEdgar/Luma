namespace Luma.Ocr.Geometry;

/// <summary>
/// Axis-aligned rectangle as fractions of the source image (0–1).
/// Same convention as screen-fraction overlays (x, y, width, height).
/// </summary>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() =>
        $"[{X:F4},{Y:F4} {Width:F4}×{Height:F4}]";
}
