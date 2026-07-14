using Luma.Ocr.Results;

namespace Luma.Ocr;

/// <summary>Cross-platform local OCR engine contract.</summary>
public interface IOcrEngine : IAsyncDisposable, IDisposable
{
    Task<OcrResult> RecognizeAsync(
        string imagePath,
        OcrOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<OcrResult> RecognizeAsync(
        Stream imageStream,
        OcrOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<OcrResult> RecognizeAsync(
        ReadOnlyMemory<byte> imageBytes,
        OcrOptions? options = null,
        CancellationToken cancellationToken = default);
}
