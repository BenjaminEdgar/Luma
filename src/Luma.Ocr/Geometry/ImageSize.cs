namespace Luma.Ocr.Geometry;

/// <summary>Pixel dimensions of a source image (width × height).</summary>
public readonly record struct ImageSize(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public void Deconstruct(out int width, out int height)
    {
        width = Width;
        height = Height;
    }

    public override string ToString() => $"{Width}×{Height}";
}
