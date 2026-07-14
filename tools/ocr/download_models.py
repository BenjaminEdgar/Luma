#!/usr/bin/env python3
"""
Download PP-OCRv5 ONNX models for Luma.Ocr and embed the recognition
character dictionary into rec.onnx (required by RapidOCRSharpOnnx).

Usage (from repo root):
  python tools/ocr/download_models.py
  python tools/ocr/download_models.py --lang english
  python tools/ocr/download_models.py --lang latin --out models/ocr

Models are portable CPU ONNX files (Windows / Linux / macOS).
"""

from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path


REPO = "monkt/paddleocr-onnx"

# language folder name on Hugging Face -> Luma.Ocr OcrLanguage hint
LANGS = {
    "english": "languages/english",
    "latin": "languages/latin",
    "chinese": "languages/chinese",
    "korean": "languages/korean",
    "eslav": "languages/eslav",
    "thai": "languages/thai",
    "greek": "languages/greek",
}


def embed_character_metadata(rec_path: Path, dict_path: Path) -> None:
    import onnx

    with dict_path.open(encoding="utf-8") as f:
        lines = [ln.rstrip("\n\r") for ln in f.read().splitlines()]

    model = onnx.load(str(rec_path))
    # Engine prepends "blank" and appends " " → classes should be len(lines)+2
    del model.metadata_props[:]
    entry = model.metadata_props.add()
    entry.key = "character"
    entry.value = "\n".join(lines)
    onnx.save(model, str(rec_path))
    print(f"  embedded {len(lines)} characters into {rec_path.name} (classes≈{len(lines)+2})")


def main() -> int:
    parser = argparse.ArgumentParser(description="Download Luma.Ocr ONNX models")
    parser.add_argument(
        "--out",
        type=Path,
        default=Path("models/ocr"),
        help="Output directory (default: models/ocr)",
    )
    parser.add_argument(
        "--lang",
        choices=sorted(LANGS.keys()),
        default="english",
        help="Recognition language pack (default: english)",
    )
    args = parser.parse_args()

    try:
        from huggingface_hub import hf_hub_download
    except ImportError:
        print("Install: pip install huggingface_hub onnx", file=sys.stderr)
        return 1

    try:
        import onnx  # noqa: F401
    except ImportError:
        print("Install: pip install onnx", file=sys.stderr)
        return 1

    out: Path = args.out
    out.mkdir(parents=True, exist_ok=True)
    lang_prefix = LANGS[args.lang]

    files = {
        "det.onnx": "detection/v5/det.onnx",
        "rec.onnx": f"{lang_prefix}/rec.onnx",
        "dict.txt": f"{lang_prefix}/dict.txt",
        "cls.onnx": "preprocessing/textline-orientation/PP-LCNet_x1_0_textline_ori.onnx",
    }

    print(f"Downloading PP-OCRv5 ({args.lang}) → {out.resolve()}")
    for name, remote in files.items():
        print(f"  {remote} …")
        cached = hf_hub_download(REPO, remote)
        dest = out / name
        shutil.copy2(cached, dest)
        print(f"    → {dest} ({dest.stat().st_size:,} bytes)")

    embed_character_metadata(out / "rec.onnx", out / "dict.txt")

    # Write a small manifest for the C# host
    manifest = out / "manifest.json"
    manifest.write_text(
        "{\n"
        f'  "source": "{REPO}",\n'
        f'  "detection": "detection/v5/det.onnx",\n'
        f'  "recognition": "{lang_prefix}/rec.onnx",\n'
        f'  "language": "{args.lang}",\n'
        f'  "ocrVersion": "PpOcrV5",\n'
        f'  "note": "rec.onnx has character metadata embedded for Luma.Ocr"\n'
        "}\n",
        encoding="utf-8",
    )
    print(f"Wrote {manifest}")
    print("Done. Use with OnnxOcrEngineOptions.FromDirectory(\"{0}\")".format(out.as_posix()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
