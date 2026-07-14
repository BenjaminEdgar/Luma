using Luma.Ocr;
using Luma.Ocr.Engines;
using Luma.Ocr.Results;

namespace Luma.App.Services;

/// <summary>
/// Lazy on-device OCR for screen captures. Soft-fails when models or native runtimes are missing
/// so chat can still use screenshots alone.
/// </summary>
public sealed class LocalOcrService : IAsyncDisposable, IDisposable
{
    private readonly object _gate = new();
    private OnnxOcrEngine? _engine;
    private string? _loadedModelsPath;
    private bool _initAttempted;
    private string? _lastError;
    private bool _disposed;

    public string? LastError => _lastError;
    public bool IsReady
    {
        get
        {
            lock (_gate) return _engine is not null;
        }
    }

    /// <summary>True when models are on disk even if the engine is not yet constructed.</summary>
    public static bool ModelsAvailable(string? overridePath = null) =>
        TryResolveModelsDirectory(overridePath) is not null;

    public static string? TryResolveModelsDirectory(string? overridePath = null)
    {
        overridePath ??= AppSettings.Current.LocalOcrModelsPath;
        foreach (var candidate in CandidateModelDirs(overridePath))
        {
            if (HasModels(candidate))
                return candidate;
        }

        return null;
    }

    public static IEnumerable<string> CandidateModelDirs(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            yield return Path.GetFullPath(overridePath.Trim());

        var env = Environment.GetEnvironmentVariable("LUMA_OCR_MODELS");
        if (!string.IsNullOrWhiteSpace(env))
            yield return Path.GetFullPath(env.Trim());

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Luma", "ocr-models");

        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "models", "ocr");
        yield return Path.Combine(baseDir, "ocr-models");

        // Dev: walk up from bin/... looking for repo models/ocr
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "models", "ocr");
        }
    }

    public static bool HasModels(string directory) =>
        Directory.Exists(directory) &&
        File.Exists(Path.Combine(directory, "det.onnx")) &&
        File.Exists(Path.Combine(directory, "rec.onnx"));

    /// <summary>
    /// OCR region and/or full-screen captures and return a combined prompt block, or null.
    /// </summary>
    public async Task<string?> BuildContextAsync(
        string? regionPath,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        if (!AppSettings.Current.LocalOcrEnabled)
            return null;
        if (regionPath is null && contextPath is null)
            return null;

        if (!EnsureEngine())
            return null;

        string? focus = null;
        string? screen = null;

        if (regionPath is not null && File.Exists(regionPath))
        {
            var r = await RecognizeFileAsync(regionPath, cancellationToken).ConfigureAwait(false);
            focus = LocalOcrPrompt.Format(r, "Focus region");
        }

        if (contextPath is not null && File.Exists(contextPath) &&
            !string.Equals(contextPath, regionPath, StringComparison.OrdinalIgnoreCase))
        {
            var r = await RecognizeFileAsync(contextPath, cancellationToken).ConfigureAwait(false);
            screen = LocalOcrPrompt.Format(r, "Full screen");
        }

        return LocalOcrPrompt.Combine(focus, screen);
    }

    /// <summary>Rendered screen text below this confidence is nearly always garbage.</summary>
    private static readonly OcrOptions ScreenOptions = new() { MinConfidence = 0.35f };

    public async Task<OcrResult?> RecognizeFileAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (!EnsureEngine())
            return null;

        OnnxOcrEngine engine;
        lock (_gate)
        {
            if (_engine is null) return null;
            engine = _engine;
        }

        try
        {
            return await engine.RecognizeAsync(imagePath, ScreenOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return null;
        }
    }

    private bool EnsureEngine()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_engine is not null) return true;
            if (_initAttempted) return false;
            _initAttempted = true;

            var models = TryResolveModelsDirectory();
            if (models is null)
            {
                _lastError = "OCR models not found (expected det.onnx + rec.onnx). Run: python tools/ocr/download_models.py";
                return false;
            }

            try
            {
                // Screen-tuned: text is upright (no orientation classifier) and rendered crisply,
                // so detection stays accurate on a downscaled input — much faster on CPU.
                var opts = new OnnxOcrEngineOptions
                {
                    DetectionModelPath = Path.Combine(models, "det.onnx"),
                    RecognitionModelPath = Path.Combine(models, "rec.onnx"),
                    ClassificationModelPath = null,
                    ModelVersion = OcrModelVersion.PpOcrV5,
                    Language = OcrLanguage.English,
                    // Benchmarked sweet spot for screen captures: ~3× faster than 2000 and *more*
                    // accurate (PP-OCR det/rec are trained near this scale; mild downscale helps).
                    MaxDetectionSideLength = 1280,
                };
                _engine = new OnnxOcrEngine(opts);
                _loadedModelsPath = models;
                _lastError = null;
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _engine = null;
                return false;
            }
        }
    }

    /// <summary>Drop the engine so a new model path can be loaded after settings change.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _engine?.Dispose();
            _engine = null;
            _loadedModelsPath = null;
            _initAttempted = false;
            _lastError = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
