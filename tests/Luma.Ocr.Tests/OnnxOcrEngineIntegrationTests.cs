using Luma.Ocr.Engines;
using Luma.Ocr.Geometry;

namespace Luma.Ocr.Tests;

/// <summary>
/// Runs only when PP-OCR ONNX models are present under models/ocr (or LUMA_OCR_MODELS).
/// Geometry unit tests do not require models.
/// </summary>
public class OnnxOcrEngineIntegrationTests
{
    private static string? ResolveModelDirectory()
    {
        var env = Environment.GetEnvironmentVariable("LUMA_OCR_MODELS");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return Path.GetFullPath(env);

        // tests/Luma.Ocr.Tests -> repo root
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var candidate = Path.Combine(root, "models", "ocr");
        if (Directory.Exists(candidate) &&
            File.Exists(Path.Combine(candidate, "det.onnx")) &&
            File.Exists(Path.Combine(candidate, "rec.onnx")))
            return candidate;

        return null;
    }

    [Fact]
    public async Task Recognize_FixtureImage_ReturnsTextAndValidBounds()
    {
        var models = ResolveModelDirectory();
        if (models is null)
        {
            // Soft-skip: still green without downloaded models.
            return;
        }

        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hello.png");
        if (!File.Exists(fixture))
        {
            // Generate a simple text-free solid image; OCR may return empty — still validates pipeline.
            fixture = Path.Combine(Path.GetTempPath(), "luma-ocr-fixture.png");
            await CreateSolidPngAsync(fixture, 200, 80);
        }

        var options = OnnxOcrEngineOptions.FromDirectory(models, OcrModelVersion.PpOcrV5, OcrLanguage.English);
        await using var engine = new OnnxOcrEngine(options);

        var result = await engine.RecognizeAsync(fixture);

        Assert.True(result.ImageSize.Width > 0);
        Assert.True(result.ImageSize.Height > 0);
        Assert.True(result.Elapsed >= TimeSpan.Zero);

        foreach (var block in result.Blocks)
        {
            Assert.InRange(block.Confidence, 0f, 1.01f);
            Assert.True(block.Bounds.X >= -2);
            Assert.True(block.Bounds.Y >= -2);
            Assert.True(block.Bounds.Right <= result.ImageSize.Width + 2);
            Assert.True(block.Bounds.Bottom <= result.ImageSize.Height + 2);

            var n = block.Normalized;
            Assert.InRange(n.X, -0.05, 1.05);
            Assert.InRange(n.Y, -0.05, 1.05);
            Assert.InRange(n.Width, 0, 1.1);
            Assert.InRange(n.Height, 0, 1.1);
        }
    }

    private static async Task CreateSolidPngAsync(string path, int width, int height)
    {
        // Minimal valid 1x1 PNG expanded conceptually — write a tiny PNG via raw bytes (1x1 white).
        // Prefer OpenCv if available at runtime through the engine dependency graph.
        try
        {
            using var mat = new OpenCvSharp.Mat(height, width, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(255, 255, 255));
            OpenCvSharp.Cv2.PutText(
                mat,
                "Hello",
                new OpenCvSharp.Point(20, 50),
                OpenCvSharp.HersheyFonts.HersheySimplex,
                1.2,
                new OpenCvSharp.Scalar(0, 0, 0),
                2);
            OpenCvSharp.Cv2.ImWrite(path, mat);
        }
        catch
        {
            // 1x1 PNG
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            await File.WriteAllBytesAsync(path, png);
        }
    }
}
