# Local OCR models (Luma.Ocr)

Portable **PP-OCRv5 ONNX** weights for on-device OCR — no LLM, no cloud.

| File | Role | Typical size |
|------|------|--------------|
| `det.onnx` | Text detection | ~84 MB |
| `rec.onnx` | Text recognition (**must** embed `character` metadata) | ~8 MB (EN) |
| `dict.txt` | Charset for rec | ~1–2 KB |
| `cls.onnx` | Optional line orientation | ~6 MB |
| `manifest.json` | Download provenance | small |

`.onnx` files are **gitignored**. Generate them with:

```powershell
# from repo root
pip install huggingface_hub onnx
python tools/ocr/download_models.py --lang english
```

Other packs: `--lang latin` | `chinese` | `korean` | `eslav` | `thai` | `greek`.

## Verify

```powershell
dotnet run --project tools/ocr-smoke -c Release
```

## Use from C#

```csharp
var opts = OnnxOcrEngineOptions.FromDirectory("models/ocr");
await using var engine = new OnnxOcrEngine(opts);
var result = await engine.RecognizeAsync("ui.png");
// result.FullText, result.Blocks[*].Bounds / Normalized
```

## Fine-tune for your UI

See **[tools/ocr/train/README.md](../../tools/ocr/train/README.md)** — collect screenshots, fine-tune with PaddleOCR, export ONNX, re-embed dict, drop into this folder.

Source pack: [monkt/paddleocr-onnx](https://huggingface.co/monkt/paddleocr-onnx) (Apache-2.0, from PaddleOCR).
