using Luma.Ocr.Geometry;

namespace Luma.Ocr;

/// <summary>Per-call OCR options (coordinate space is always the full source image).</summary>
public sealed class OcrOptions
{
    /// <summary>Drop blocks whose confidence is below this value (0–1).</summary>
    public float MinConfidence { get; init; }

    /// <summary>
    /// Optional crop in source-image pixels. Recognition runs on the crop;
    /// returned coordinates are offset back into full-image space.
    /// </summary>
    public PixelRect? RegionOfInterest { get; init; }
}
