using Luma.Ocr.Geometry;

namespace Luma.Ocr.Results;

/// <summary>One recognized text region with geometry in source-image space.</summary>
public sealed record OcrBlock(
    string Text,
    float Confidence,
    PixelRect Bounds,
    NormalizedRect Normalized,
    PixelQuad Quad,
    int Index);
