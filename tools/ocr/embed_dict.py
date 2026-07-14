#!/usr/bin/env python3
"""Embed dict.txt into rec.onnx metadata key 'character' for Luma.Ocr."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--rec", type=Path, required=True, help="Path to rec.onnx")
    p.add_argument("--dict", type=Path, required=True, help="Path to dict.txt")
    args = p.parse_args()

    try:
        import onnx
    except ImportError:
        print("pip install onnx", file=sys.stderr)
        return 1

    if not args.rec.is_file():
        print(f"Missing {args.rec}", file=sys.stderr)
        return 1
    if not args.dict.is_file():
        print(f"Missing {args.dict}", file=sys.stderr)
        return 1

    lines = [ln.rstrip("\n\r") for ln in args.dict.read_text(encoding="utf-8").splitlines()]
    model = onnx.load(str(args.rec))
    del model.metadata_props[:]
    entry = model.metadata_props.add()
    entry.key = "character"
    entry.value = "\n".join(lines)
    onnx.save(model, str(args.rec))
    print(f"Embedded {len(lines)} chars into {args.rec} (engine classes ≈ {len(lines) + 2})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
