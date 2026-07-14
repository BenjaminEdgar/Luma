namespace Luma.Ocr.Geometry;

/// <summary>Pure, platform-free coordinate helpers for OCR results.</summary>
public static class OcrGeometry
{
    public static NormalizedRect ToNormalized(PixelRect rect, ImageSize imageSize)
    {
        if (imageSize.IsEmpty)
            throw new ArgumentException("Image size must be positive.", nameof(imageSize));

        return new NormalizedRect(
            (double)rect.X / imageSize.Width,
            (double)rect.Y / imageSize.Height,
            (double)rect.Width / imageSize.Width,
            (double)rect.Height / imageSize.Height);
    }

    public static PixelRect ToPixel(NormalizedRect rect, ImageSize imageSize)
    {
        if (imageSize.IsEmpty)
            throw new ArgumentException("Image size must be positive.", nameof(imageSize));

        var x = (int)Math.Round(rect.X * imageSize.Width);
        var y = (int)Math.Round(rect.Y * imageSize.Height);
        var w = (int)Math.Round(rect.Width * imageSize.Width);
        var h = (int)Math.Round(rect.Height * imageSize.Height);
        return new PixelRect(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    public static PixelRect FromQuad(PixelQuad quad)
    {
        var minX = Min4(quad.TopLeft.X, quad.TopRight.X, quad.BottomRight.X, quad.BottomLeft.X);
        var minY = Min4(quad.TopLeft.Y, quad.TopRight.Y, quad.BottomRight.Y, quad.BottomLeft.Y);
        var maxX = Max4(quad.TopLeft.X, quad.TopRight.X, quad.BottomRight.X, quad.BottomLeft.X);
        var maxY = Max4(quad.TopLeft.Y, quad.TopRight.Y, quad.BottomRight.Y, quad.BottomLeft.Y);
        return new PixelRect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    public static PixelQuad Offset(PixelQuad quad, int dx, int dy) =>
        new(
            new PixelPoint(quad.TopLeft.X + dx, quad.TopLeft.Y + dy),
            new PixelPoint(quad.TopRight.X + dx, quad.TopRight.Y + dy),
            new PixelPoint(quad.BottomRight.X + dx, quad.BottomRight.Y + dy),
            new PixelPoint(quad.BottomLeft.X + dx, quad.BottomLeft.Y + dy));

    public static PixelRect Offset(PixelRect rect, int dx, int dy) =>
        new(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);

    public static PixelPoint Center(PixelRect rect) =>
        new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    public static bool Contains(PixelRect rect, PixelPoint point) =>
        point.X >= rect.Left && point.X < rect.Right &&
        point.Y >= rect.Top && point.Y < rect.Bottom;

    public static bool Contains(NormalizedRect rect, double x, double y) =>
        x >= rect.Left && x <= rect.Right &&
        y >= rect.Top && y <= rect.Bottom;

    public static bool Intersects(PixelRect a, PixelRect b) =>
        a.Left < b.Right && a.Right > b.Left &&
        a.Top < b.Bottom && a.Bottom > b.Top;

    public static PixelRect? Intersect(PixelRect a, PixelRect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top)
            return null;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelRect Clamp(PixelRect rect, ImageSize imageSize)
    {
        if (imageSize.IsEmpty)
            return default;

        var left = Math.Clamp(rect.Left, 0, imageSize.Width);
        var top = Math.Clamp(rect.Top, 0, imageSize.Height);
        var right = Math.Clamp(rect.Right, 0, imageSize.Width);
        var bottom = Math.Clamp(rect.Bottom, 0, imageSize.Height);
        if (right <= left || bottom <= top)
            return default;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>Intersection-over-union of two axis-aligned rects (0–1).</summary>
    public static double IoU(PixelRect a, PixelRect b)
    {
        var inter = Intersect(a, b);
        if (inter is null || inter.Value.IsEmpty)
            return 0;

        var interArea = (double)inter.Value.Width * inter.Value.Height;
        var union = (double)a.Width * a.Height + (double)b.Width * b.Height - interArea;
        return union <= 0 ? 0 : interArea / union;
    }

    private static int Min4(int a, int b, int c, int d) => Math.Min(Math.Min(a, b), Math.Min(c, d));
    private static int Max4(int a, int b, int c, int d) => Math.Max(Math.Max(a, b), Math.Max(c, d));
}
