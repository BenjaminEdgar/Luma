using System.Diagnostics;
using System.Text;
using Luma.Ocr.Geometry;
using Luma.Ocr.Results;
using OpenCvSharp;
using RapidOCRSharpOnnx.Configurations;
using RapidOCRSharpOnnx.Providers;
using RapidOCRSharpOnnx.Utils;
using BackendOcrResult = RapidOCRSharpOnnx.OcrResult;
using BackendEngine = RapidOCRSharpOnnx.RapidOCRSharp;
using LumaOcrResult = Luma.Ocr.Results.OcrResult;

namespace Luma.Ocr.Engines;

/// <summary>
/// Local ONNX OCR engine (PaddleOCR / RapidOCR models via RapidOCRSharpOnnx).
/// Public types never leak the backend; all coordinates are in source-image space.
/// </summary>
public sealed class OnnxOcrEngine : IOcrEngine
{
    private readonly BackendEngine _engine;
    private readonly object _gate = new();
    private bool _disposed;

    public OnnxOcrEngine(OnnxOcrEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var config = options.ClassificationModelPath is { } clsPath
            ? new OcrConfig(
                options.DetectionModelPath,
                options.RecognitionModelPath,
                MapLanguage(options.Language),
                MapVersion(options.ModelVersion),
                clsPath)
            : new OcrConfig(
                options.DetectionModelPath,
                options.RecognitionModelPath,
                MapLanguage(options.Language),
                MapVersion(options.ModelVersion));

        if (options.MaxDetectionSideLength > 0)
            config.MaxSideLen = options.MaxDetectionSideLength;

        _engine = new BackendEngine(new ExecutionProviderCPU(config));
    }

    public Task<LumaOcrResult> RecognizeAsync(
        string imagePath,
        OcrOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Image not found.", imagePath);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (mat.Empty())
                throw new InvalidOperationException($"Failed to decode image: {imagePath}");
            return RecognizeMat(mat, options, cancellationToken);
        }, cancellationToken);
    }

    public Task<LumaOcrResult> RecognizeAsync(
        Stream imageStream,
        OcrOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var ms = new MemoryStream();
            imageStream.CopyTo(ms);
            return RecognizeBytesCore(ms.ToArray(), options, cancellationToken);
        }, cancellationToken);
    }

    public Task<LumaOcrResult> RecognizeAsync(
        ReadOnlyMemory<byte> imageBytes,
        OcrOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var copy = imageBytes.ToArray();
        return Task.Run(() => RecognizeBytesCore(copy, options, cancellationToken), cancellationToken);
    }

    private LumaOcrResult RecognizeBytesCore(byte[] bytes, OcrOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length == 0)
            throw new ArgumentException("Image bytes are empty.", nameof(bytes));

        using var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat.Empty())
            throw new InvalidOperationException("Failed to decode image bytes.");
        return RecognizeMat(mat, options, cancellationToken);
    }

    private LumaOcrResult RecognizeMat(Mat source, OcrOptions? options, CancellationToken cancellationToken)
    {
        options ??= new OcrOptions();
        var fullSize = new ImageSize(source.Width, source.Height);
        if (fullSize.IsEmpty)
            throw new InvalidOperationException("Image has empty dimensions.");

        var sw = Stopwatch.StartNew();
        var offsetX = 0;
        var offsetY = 0;
        Mat work = source;
        Mat? cropped = null;

        try
        {
            if (options.RegionOfInterest is { } roi)
            {
                var clamped = OcrGeometry.Clamp(roi, fullSize);
                if (clamped.IsEmpty)
                {
                    sw.Stop();
                    return new LumaOcrResult(fullSize, string.Empty, Array.Empty<OcrBlock>(), sw.Elapsed);
                }

                offsetX = clamped.X;
                offsetY = clamped.Y;
                cropped = new Mat(source, new Rect(clamped.X, clamped.Y, clamped.Width, clamped.Height));
                work = cropped;
            }

            cancellationToken.ThrowIfCancellationRequested();

            BackendOcrResult backend;
            lock (_gate)
            {
                ThrowIfDisposed();
                backend = _engine.RecognizeText(work);
            }

            sw.Stop();
            return MapResult(backend, fullSize, offsetX, offsetY, options.MinConfidence, sw.Elapsed);
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    private static LumaOcrResult MapResult(
        BackendOcrResult backend,
        ImageSize fullSize,
        int offsetX,
        int offsetY,
        float minConfidence,
        TimeSpan elapsed)
    {
        var detItems = backend.DetResult?.Data?.DetItems;
        var recItems = backend.RecResult?.Data;

        var blocks = new List<OcrBlock>();
        if (detItems is { Length: > 0 })
        {
            var count = detItems.Length;
            if (recItems is not null)
                count = Math.Min(count, recItems.Length);

            var unsorted = new List<(OcrBlock Block, float SortY, float SortX)>(count);

            for (var i = 0; i < count; i++)
            {
                var det = detItems[i];
                var text = recItems is not null
                    ? recItems[i].Label ?? string.Empty
                    : det.Word ?? string.Empty;
                var conf = recItems is not null ? recItems[i].Score : det.Score;

                if (conf < minConfidence)
                    continue;

                if (det.Box is null || det.Box.Length < 4)
                    continue;

                var quad = ToQuad(det.Box, offsetX, offsetY);
                var bounds = OcrGeometry.FromQuad(quad);
                var normalized = OcrGeometry.ToNormalized(bounds, fullSize);

                var block = new OcrBlock(text, conf, bounds, normalized, quad, Index: 0);
                unsorted.Add((block, bounds.Y + bounds.Height / 2f, bounds.X));
            }

            // Reading order: top-to-bottom, then left-to-right.
            unsorted.Sort((a, b) =>
            {
                var dy = a.SortY.CompareTo(b.SortY);
                return dy != 0 ? dy : a.SortX.CompareTo(b.SortX);
            });

            for (var i = 0; i < unsorted.Count; i++)
                blocks.Add(unsorted[i].Block with { Index = i });
        }

        var fullText = BuildFullText(blocks, backend.TextBlocks);
        return new LumaOcrResult(fullSize, fullText, blocks, elapsed);
    }

    private static string BuildFullText(IReadOnlyList<OcrBlock> blocks, string? backendText)
    {
        if (blocks.Count == 0)
            return backendText?.Trim() ?? string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
                sb.AppendLine();
            sb.Append(blocks[i].Text);
        }

        return sb.ToString();
    }

    private static PixelQuad ToQuad(OpenCvSharp.Point2f[] box, int offsetX, int offsetY)
    {
        // Engine order is typically TL, TR, BR, BL; fall back if fewer than 4 by repeating last.
        PixelPoint P(int i)
        {
            var p = box[Math.Min(i, box.Length - 1)];
            return new PixelPoint(
                (int)Math.Round(p.X) + offsetX,
                (int)Math.Round(p.Y) + offsetY);
        }

        return new PixelQuad(P(0), P(1), P(2), P(3));
    }

    private static LangRec MapLanguage(OcrLanguage language) => language switch
    {
        OcrLanguage.Chinese => LangRec.CH,
        OcrLanguage.ChineseDocument => LangRec.CH_DOC,
        OcrLanguage.English => LangRec.EN,
        OcrLanguage.Arabic => LangRec.ARABIC,
        OcrLanguage.ChineseTraditional => LangRec.CHINESE_CHT,
        OcrLanguage.Cyrillic => LangRec.CYRILLIC,
        OcrLanguage.Devanagari => LangRec.DEVANAGARI,
        OcrLanguage.Japanese => LangRec.JAPAN,
        OcrLanguage.Korean => LangRec.KOREAN,
        OcrLanguage.Kannada => LangRec.KA,
        OcrLanguage.Latin => LangRec.LATIN,
        OcrLanguage.Tamil => LangRec.TA,
        OcrLanguage.Telugu => LangRec.TE,
        OcrLanguage.EastSlavic => LangRec.ESLAV,
        OcrLanguage.Thai => LangRec.TH,
        OcrLanguage.Greek => LangRec.EL,
        _ => LangRec.EN,
    };

    private static OCRVersion MapVersion(OcrModelVersion version) => version switch
    {
        OcrModelVersion.PpOcrV4 => OCRVersion.PPOCRV4,
        OcrModelVersion.PpOcrV5 => OCRVersion.PPOCRV5,
        OcrModelVersion.PpOcrV6 => OCRVersion.PPOCRV6,
        OcrModelVersion.PpOcrV6Tiny => OCRVersion.PPOCRV6Tiny,
        _ => OCRVersion.PPOCRV5,
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;
        lock (_gate)
        {
            if (_disposed)
                return;
            _engine.Dispose();
            _disposed = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
