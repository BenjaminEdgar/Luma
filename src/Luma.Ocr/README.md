# Luma.Ocr

Cross-platform, local OCR library with a **coordinate-rich** public API.

- **TFM:** `net10.0` (not Windows-only)
- **Engine:** ONNX Runtime + PP-OCR models via [RapidOCRSharpOnnx](https://www.nuget.org/packages/RapidOCRSharpOnnx)
- **Coordinates:** pixel rects, normalized 0–1 rects, rotated quads, hit-test helpers
- **No cloud** — models run on-device

## Install

```xml
<PackageReference Include="Luma.Ocr" /> <!-- or ProjectReference -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.26.0" />
```

Add the **OpenCvSharp native runtime for your OS** on the **host** project (app or tests):

| OS | Package |
|----|---------|
| Windows | `OpenCvSharp4.runtime.win` |
| Linux x64 | `OpenCvSharp4.runtime.linux-x64` |
| macOS x64 | `OpenCvSharp4.runtime.osx.10.15-x64` |
| macOS arm64 | Use the OpenCvSharp runtime package matching your version (or x64 under Rosetta if arm64 package is unavailable) |

Example (MSBuild OS conditions):

```xml
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.13.0.20260627" Condition="$([MSBuild]::IsOSPlatform('Windows'))" />
<PackageReference Include="OpenCvSharp4.runtime.linux-x64" Version="4.13.0.20260627" Condition="$([MSBuild]::IsOSPlatform('Linux'))" />
<PackageReference Include="OpenCvSharp4.runtime.osx.10.15-x64" Version="4.13.0.20260627" Condition="$([MSBuild]::IsOSPlatform('OSX'))" />
```

## Models (local, no LLM)

```powershell
# from repo root — downloads PP-OCRv5 + embeds charset into rec.onnx
pip install huggingface_hub onnx
python tools/ocr/download_models.py --lang english
```

```
models/ocr/
  det.onnx
  rec.onnx      # character metadata required
  dict.txt
  cls.onnx      # optional
  manifest.json
```

- Docs: [`../../models/ocr/README.md`](../../models/ocr/README.md)
- **Fine-tune on your screenshots:** [`../../tools/ocr/train/README.md`](../../tools/ocr/train/README.md)
- Smoke test: `dotnet run --project tools/ocr-smoke -c Release`

## Usage

```csharp
using Luma.Ocr;
using Luma.Ocr.Engines;
using Luma.Ocr.Geometry;

var options = OnnxOcrEngineOptions.FromDirectory(
    modelDirectory: @"path/to/models/ocr",
    version: OcrModelVersion.PpOcrV5,
    language: OcrLanguage.English);

await using IOcrEngine engine = new OnnxOcrEngine(options);

var result = await engine.RecognizeAsync("screenshot.png");

Console.WriteLine(result.FullText);
foreach (var block in result.Blocks)
{
    // Pixel space (top-left origin, Y down)
    Console.WriteLine($"{block.Text} @ {block.Bounds} conf={block.Confidence:F2}");

    // Normalized 0–1 (compatible with Luma SHOW_WHERE style)
    var (x, y, w, h) = block.ToShowWhereStyle();
    Console.WriteLine($"  norm={x:F3},{y:F3},{w:F3},{h:F3}");
}

// Hit-test
var hit = result.FindContaining(new PixelPoint(120, 80));

// Region filter
foreach (var b in result.InRegion(new PixelRect(0, 0, 200, 100)))
    Console.WriteLine(b.Text);
```

### ROI

```csharp
var result = await engine.RecognizeAsync(
    "full.png",
    new OcrOptions
    {
        RegionOfInterest = new PixelRect(100, 50, 400, 300),
        MinConfidence = 0.5f,
    });
// block.Bounds are still in full-image coordinates
```

## Coordinate system

All geometry is in **source image pixel space** (not screen/DPI space).

| Type | Meaning |
|------|---------|
| `PixelRect` | Axis-aligned box: `X,Y,Width,Height` |
| `NormalizedRect` | Same box as fractions of image size (0–1) |
| `PixelQuad` | Four corners for rotated text |

Map to screen yourself using your capture DPI/scale (Luma already has selection/capture helpers).

## Cross-platform notes

- No `net*-windows` TFM, no Win32 / `System.Drawing` / Windows OCR APIs
- Same ONNX models on Windows, Linux, and macOS
- Host supplies OpenCv + ONNX native runtimes via NuGet RID assets
