using Luma.Ocr.Geometry;

namespace Luma.Ocr.Results;

/// <summary>Full-page OCR output with coordinate-rich text blocks.</summary>
public sealed record OcrResult(
    ImageSize ImageSize,
    string FullText,
    IReadOnlyList<OcrBlock> Blocks,
    TimeSpan Elapsed);
