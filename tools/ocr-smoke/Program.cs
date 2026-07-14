using Luma.Ocr.Engines;
using OpenCvSharp;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var models = Path.Combine(root, "models", "ocr");
Console.WriteLine("models: " + models);
Console.WriteLine("det exists: " + File.Exists(Path.Combine(models, "det.onnx")));

var imgPath = Path.Combine(Path.GetTempPath(), "luma-ocr-smoke.png");
using (var mat = new Mat(120, 400, MatType.CV_8UC3, new Scalar(255, 255, 255)))
{
    Cv2.PutText(mat, "Hello OCR 123", new Point(20, 70), HersheyFonts.HersheySimplex, 1.4, new Scalar(0, 0, 0), 2);
    Cv2.ImWrite(imgPath, mat);
}
Console.WriteLine("image: " + imgPath);

var opts = OnnxOcrEngineOptions.FromDirectory(models, OcrModelVersion.PpOcrV5, OcrLanguage.English);
await using var engine = new OnnxOcrEngine(opts);
var result = await engine.RecognizeAsync(imgPath);
Console.WriteLine("size: " + result.ImageSize);
Console.WriteLine("elapsed: " + result.Elapsed);
Console.WriteLine("full: " + result.FullText);
foreach (var b in result.Blocks)
    Console.WriteLine($"  [{b.Index}] '{b.Text}' conf={b.Confidence:F3} bounds={b.Bounds} norm={b.Normalized}");
