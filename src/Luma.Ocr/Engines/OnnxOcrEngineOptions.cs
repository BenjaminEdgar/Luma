namespace Luma.Ocr.Engines;

/// <summary>
/// Model paths and defaults for <see cref="OnnxOcrEngine"/>.
/// Models are portable ONNX files (same on every OS).
/// </summary>
public sealed class OnnxOcrEngineOptions
{
    /// <summary>Text detection model (.onnx).</summary>
    public required string DetectionModelPath { get; init; }

    /// <summary>Text recognition model (.onnx).</summary>
    public required string RecognitionModelPath { get; init; }

    /// <summary>Optional text-line orientation classifier (.onnx).</summary>
    public string? ClassificationModelPath { get; init; }

    /// <summary>PP-OCR model family (default v5).</summary>
    public OcrModelVersion ModelVersion { get; init; } = OcrModelVersion.PpOcrV5;

    /// <summary>Recognition language family used by the underlying engine.</summary>
    public OcrLanguage Language { get; init; } = OcrLanguage.English;

    /// <summary>
    /// Longest image side (px) fed to text detection; larger inputs are downscaled first.
    /// Coordinates are always mapped back to full source pixels. 0 keeps the backend default
    /// (2000, document-tuned). Screen captures stay accurate well below that — smaller is faster.
    /// </summary>
    public int MaxDetectionSideLength { get; init; }

    /// <summary>
    /// Resolve standard filenames under a model directory:
    /// <c>det.onnx</c>, <c>rec.onnx</c>, optional <c>cls.onnx</c>.
    /// </summary>
    public static OnnxOcrEngineOptions FromDirectory(
        string modelDirectory,
        OcrModelVersion version = OcrModelVersion.PpOcrV5,
        OcrLanguage language = OcrLanguage.English)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        var dir = Path.GetFullPath(modelDirectory);
        var det = Path.Combine(dir, "det.onnx");
        var rec = Path.Combine(dir, "rec.onnx");
        var cls = Path.Combine(dir, "cls.onnx");

        return new OnnxOcrEngineOptions
        {
            DetectionModelPath = det,
            RecognitionModelPath = rec,
            ClassificationModelPath = File.Exists(cls) ? cls : null,
            ModelVersion = version,
            Language = language,
        };
    }

    public void Validate()
    {
        if (!File.Exists(DetectionModelPath))
            throw new FileNotFoundException("Detection model not found.", DetectionModelPath);
        if (!File.Exists(RecognitionModelPath))
            throw new FileNotFoundException("Recognition model not found.", RecognitionModelPath);
        if (ClassificationModelPath is not null && !File.Exists(ClassificationModelPath))
            throw new FileNotFoundException("Classification model not found.", ClassificationModelPath);
    }
}

public enum OcrModelVersion
{
    PpOcrV4,
    PpOcrV5,
    PpOcrV6,
    PpOcrV6Tiny,
}

public enum OcrLanguage
{
    Chinese,
    ChineseDocument,
    English,
    Arabic,
    ChineseTraditional,
    Cyrillic,
    Devanagari,
    Japanese,
    Korean,
    Kannada,
    Latin,
    Tamil,
    Telugu,
    EastSlavic,
    Thai,
    Greek,
}
