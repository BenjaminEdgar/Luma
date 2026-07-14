#!/usr/bin/env python3
"""
Convert line-level OCR labels + full images into PaddleOCR recognition crops.

Input layout:
  data/ocr/images/001.png
  data/ocr/labels/001.txt
    each line: [[x1,y1],[x2,y2],[x3,y3],[x4,y4]]\\ttext

Output:
  out/train/*.png + train_list.txt
  out/val/*.png   + val_list.txt
"""

from __future__ import annotations

import argparse
import ast
import random
import sys
from pathlib import Path


def parse_box_line(line: str):
    line = line.strip()
    if not line or line.startswith("#"):
        return None
    if "\t" in line:
        box_s, text = line.split("\t", 1)
    elif " " in line:
        # last resort: first space after ]
        idx = line.rfind("]")
        if idx < 0:
            return None
        box_s, text = line[: idx + 1], line[idx + 1 :].strip()
    else:
        return None
    box = ast.literal_eval(box_s)
    pts = [(float(p[0]), float(p[1])) for p in box]
    return pts, text


def crop_quad(img, pts):
    """Axis-aligned crop from quad (simple; good enough for upright UI text)."""
    import numpy as np

    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    x0, x1 = int(max(0, min(xs))), int(min(img.shape[1], max(xs) + 1))
    y0, y1 = int(max(0, min(ys))), int(min(img.shape[0], max(ys) + 1))
    if x1 <= x0 or y1 <= y0:
        return None
    return img[y0:y1, x0:x1].copy()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", type=Path, required=True, help="Folder with images/ and labels/")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--val-ratio", type=float, default=0.1)
    ap.add_argument("--seed", type=int, default=42)
    args = ap.parse_args()

    try:
        import cv2
    except ImportError:
        print("pip install opencv-python-headless", file=sys.stderr)
        return 1

    images_dir = args.data / "images"
    labels_dir = args.data / "labels"
    if not images_dir.is_dir() or not labels_dir.is_dir():
        print("Expected data/images and data/labels", file=sys.stderr)
        return 1

    samples: list[tuple[object, str]] = []
    for label_path in sorted(labels_dir.glob("*.txt")):
        stem = label_path.stem
        img_path = None
        for ext in (".png", ".jpg", ".jpeg", ".webp", ".bmp"):
            cand = images_dir / f"{stem}{ext}"
            if cand.is_file():
                img_path = cand
                break
        if img_path is None:
            print(f"skip {stem}: no image")
            continue
        img = cv2.imread(str(img_path))
        if img is None:
            print(f"skip unreadable {img_path}")
            continue
        for line in label_path.read_text(encoding="utf-8").splitlines():
            parsed = parse_box_line(line)
            if not parsed:
                continue
            pts, text = parsed
            if not text.strip():
                continue
            crop = crop_quad(img, pts)
            if crop is None or crop.size == 0:
                continue
            samples.append((crop, text.strip()))

    if not samples:
        print("No samples produced", file=sys.stderr)
        return 1

    random.Random(args.seed).shuffle(samples)
    n_val = max(1, int(len(samples) * args.val_ratio)) if len(samples) > 10 else 0
    val = samples[:n_val]
    train = samples[n_val:] or samples

    def write_split(name: str, items: list[tuple[object, str]]) -> None:
        d = args.out / name
        d.mkdir(parents=True, exist_ok=True)
        list_path = args.out / f"{name}_list.txt"
        lines = []
        for i, (crop, text) in enumerate(items):
            fn = f"img_{i:05d}.png"
            cv2.imwrite(str(d / fn), crop)
            # Paddle list: relative path \t label
            lines.append(f"{name}/{fn}\t{text}")
        list_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"{name}: {len(items)} crops → {list_path}")

    args.out.mkdir(parents=True, exist_ok=True)
    write_split("train", train)
    if val:
        write_split("val", val)
    print(f"Total crops: {len(samples)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
