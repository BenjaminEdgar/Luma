# Train / fine-tune your local OCR (then run in Luma.Ocr)

You already have a strong **pretrained** PP-OCRv5 stack under `models/ocr/`.  
“Best at OCR” on *your* UI (fonts, themes, DPI, app chrome) comes from **fine-tuning** on labeled screenshots, then **exporting ONNX** back into `models/ocr/`.

Training is done in **Python + PaddleOCR** (industry standard). Inference stays in **C#** (`Luma.Ocr`) on the user’s machine — no LLM required.

```
Your screenshots + labels
        │
        ▼
  PaddleOCR fine-tune (GPU recommended)
        │
        ▼
  Export rec/det → ONNX + dict
        │
        ▼
  python tools/ocr/download_models.py   # or copy your exports
  embed character metadata (script does this)
        │
        ▼
  Luma.Ocr OnnxOcrEngine  (CPU local, cross-platform)
```

## 0. Baseline (already set up)

```powershell
# From repo root
pip install huggingface_hub onnx
python tools/ocr/download_models.py --lang english
dotnet run --project tools/ocr-smoke -c Release
```

If smoke prints `Hello` / `OCR 123` with boxes, local inference works.

## 1. Collect training data (highest leverage)

Create a folder of **real screenshots** from the apps you care about:

```
data/ocr/
  images/
    001.png
    002.png
  labels/
    001.txt      # one line per text box (see below)
    002.txt
```

### Label format (line-level, PaddleOCR style)

Each line in `labels/001.txt`:

```text
[[x1,y1],[x2,y2],[x3,y3],[x4,y4]]	the text content
```

- Four corners of the text box (pixels, top-left origin)
- Tab-separated text
- Use [PPOCRLabel](https://github.com/PFCCLab/PPOCRLabel) or Label Studio for speed

**Aim for:** 200–2000 diverse UI crops first (menus, dialogs, code, buttons, dark/light themes). Quality beats quantity.

### Convert to Paddle rec crops (optional helper)

```powershell
python tools/ocr/train/prepare_rec_dataset.py --data data/ocr --out data/ocr_rec
```

Produces:

```
data/ocr_rec/
  train/
    img_0001.png
    ...
  train_list.txt   # path\tlabel
  val/
  val_list.txt
```

## 2. Fine-tune recognition (most impact)

Use the official PaddleOCR rec fine-tune path on top of PP-OCRv5 English (or Chinese multi-script).

```powershell
# Example environment (GPU strongly recommended)
pip install paddlepaddle-gpu paddleocr   # or CPU paddlepaddle

# Follow PaddleOCR docs for "finetune recognition":
# https://paddlepaddle.github.io/PaddleOCR/latest/en/ppocr/model_train/recognition.html
```

Practical recipe:

1. Start from **PP-OCRv5 mobile/server rec** pretrained weights (English or Chinese).
2. Point `Train.dataset.data_dir` / `label_file_list` at `data/ocr_rec`.
3. Lower learning rate (e.g. 1e-4 → 1e-5), few epochs (5–20).
4. Keep the **same character dict** as production (`models/ocr/dict.txt`) unless you expand the alphabet deliberately.

Detection fine-tune is optional; only do it if boxes are systematically wrong. Most UI gains come from **rec**.

## 3. Export to ONNX for Luma.Ocr

```powershell
# After fine-tune, export with paddle2onnx (see PaddleOCR deployment docs)
# Then place files:
#   models/ocr/det.onnx
#   models/ocr/rec.onnx   # your fine-tuned rec
#   models/ocr/dict.txt
#   models/ocr/cls.onnx   # optional

python tools/ocr/embed_dict.py --rec models/ocr/rec.onnx --dict models/ocr/dict.txt
```

**Critical:** `rec.onnx` must have ONNX metadata key `character` = newline-separated charset  
(same as `dict.txt`). RapidOCRSharpOnnx / Luma.Ocr read this at load time.  
`embed_dict.py` (and `download_models.py`) do this for you.

Match `OcrModelVersion` in C# to the family you fine-tuned (`PpOcrV5` by default).

## 4. Verify

```powershell
dotnet run --project tools/ocr-smoke -c Release
dotnet test tests/Luma.Ocr.Tests -c Release
```

Or in C#:

```csharp
var opts = OnnxOcrEngineOptions.FromDirectory("models/ocr");
await using var engine = new OnnxOcrEngine(opts);
var result = await engine.RecognizeAsync("my-ui.png");
// result.Blocks[*].Bounds / Normalized — local, no LLM
```

## 5. What “best” means in practice

| Goal | Action |
|------|--------|
| Works offline today | Use downloaded PP-OCRv5 English pack |
| Best on *your* product UI | Fine-tune rec on 500+ labeled screenshots |
| Multi-language UI | `download_models.py --lang chinese` or `latin` |
| Better tiny/blurry text | Prefer server det/rec weights; still export ONNX |
| Continuous improvement | Log OCR mistakes → re-label → re-finetune → drop new ONNX |

## 6. Shipping to users

- Ship **ONNX + dict** next to the app (or download on first run via `download_models.py` logic).
- Host app references OS-specific `OpenCvSharp4.runtime.*` + `Microsoft.ML.OnnxRuntime`.
- Models are CPU-friendly (~90 MB det + ~8 MB EN rec); no cloud, no LLM for text extraction.

LLM remains useful for *understanding* UI intent; **reading pixels → text + coordinates** should stay on-device via `Luma.Ocr`.
