using Luma.Ocr.Geometry;

namespace Luma.Ocr.Results;

/// <summary>Spatial and text queries over an <see cref="OcrResult"/>.</summary>
public static class OcrResultQueries
{
    public static OcrBlock? FindContaining(this OcrResult result, PixelPoint point)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var block in result.Blocks)
        {
            if (OcrGeometry.Contains(block.Bounds, point))
                return block;
        }

        return null;
    }

    public static OcrBlock? FindContaining(this OcrResult result, double normalizedX, double normalizedY)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var block in result.Blocks)
        {
            if (OcrGeometry.Contains(block.Normalized, normalizedX, normalizedY))
                return block;
        }

        return null;
    }

    public static IEnumerable<OcrBlock> InRegion(this OcrResult result, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var block in result.Blocks)
        {
            if (OcrGeometry.Intersects(block.Bounds, region))
                yield return block;
        }
    }

    public static IEnumerable<OcrBlock> InRegion(this OcrResult result, NormalizedRect region)
    {
        ArgumentNullException.ThrowIfNull(result);
        var pixel = OcrGeometry.ToPixel(region, result.ImageSize);
        return result.InRegion(pixel);
    }

    /// <summary>
    /// Best text match; optional location bias prefers blocks nearer <paramref name="near"/>.
    /// </summary>
    public static OcrBlock? BestMatch(
        this OcrResult result,
        string text,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase,
        PixelPoint? near = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        OcrBlock? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var block in result.Blocks)
        {
            if (string.IsNullOrEmpty(block.Text))
                continue;

            double textScore;
            if (block.Text.Equals(text, comparison))
                textScore = 2.0;
            else if (block.Text.Contains(text, comparison))
                textScore = 1.0;
            else
                continue;

            var locationScore = 0.0;
            if (near is { } p)
            {
                var c = OcrGeometry.Center(block.Bounds);
                var dx = c.X - p.X;
                var dy = c.Y - p.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                // Prefer closer blocks without overpowering exact text matches.
                locationScore = 1.0 / (1.0 + dist / 100.0);
            }

            var score = textScore + locationScore + block.Confidence * 0.1;
            if (score > bestScore)
            {
                bestScore = score;
                best = block;
            }
        }

        return best;
    }

    /// <summary>
    /// Normalized (x, y, w, h) suitable for Luma-style SHOW_WHERE directives.
    /// </summary>
    public static (double X, double Y, double Width, double Height) ToShowWhereStyle(this OcrBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var n = block.Normalized;
        return (n.X, n.Y, n.Width, n.Height);
    }

    /// <summary>
    /// Groups blocks (already in reading order) into visual rows: a block joins the current row
    /// when its vertical center sits within the row's height band. Left-to-right order inside a
    /// row is preserved, so joined rows read like the screen ("File  Edit  View" as one line).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<OcrBlock>> GroupIntoRows(this OcrResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var rows = new List<List<OcrBlock>>();
        List<OcrBlock>? row = null;
        double rowCenterY = 0, rowHeight = 0;

        foreach (var block in result.Blocks)
        {
            var centerY = block.Bounds.Y + block.Bounds.Height / 2.0;
            var tolerance = Math.Max(rowHeight, block.Bounds.Height) * 0.6;
            if (row is null || Math.Abs(centerY - rowCenterY) > tolerance)
            {
                row = [block];
                rows.Add(row);
                rowCenterY = centerY;
                rowHeight = block.Bounds.Height;
            }
            else
            {
                row.Add(block);
                // Track the running band so slightly staggered blocks still merge.
                rowCenterY = (rowCenterY * (row.Count - 1) + centerY) / row.Count;
                rowHeight = Math.Max(rowHeight, block.Bounds.Height);
            }
        }

        // Reading order sorts by center-Y first, which can shuffle near-tied blocks in a row.
        foreach (var r in rows)
            r.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        return rows;
    }
}
