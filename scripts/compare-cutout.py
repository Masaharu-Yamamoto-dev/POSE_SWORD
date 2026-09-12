#!/usr/bin/env python
"""切り抜きモデルを同じ写真で比べる。

  image-process/.venv/bin/python scripts/compare-cutout.py --input image-process/input.json
  image-process/.venv/bin/python scripts/compare-cutout.py --image 写真.jpg --models u2net u2net_human_seg

各モデルで切り抜きとステータス算出を行い、数値を並べて透過PNGを保存する。
HP は姿勢推定（切り抜きマスクを使わない）なので、モデルを変えても動かない。
"""
import argparse
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "image-process"))

import numpy as np  # noqa: E402
from PIL import Image  # noqa: E402

from person_cutout import crop_to_subject, cutout_person, decode_image  # noqa: E402
from stats import compute_attack, compute_stats, compute_weight, silhouette_mask  # noqa: E402


def load_image(args) -> Image.Image:
    if args.image:
        return Image.open(args.image).convert("RGBA")
    import json
    data = json.loads(Path(args.input).read_text())
    return decode_image(data[args.json_key])


def main() -> int:
    parser = argparse.ArgumentParser(description="切り抜きモデルの比較")
    parser.add_argument("--input", default="image-process/input.json", help="base64 を含む JSON")
    parser.add_argument("--json-key", default="imageData")
    parser.add_argument("--image", help="画像ファイル（指定すると JSON より優先）")
    parser.add_argument("--models", nargs="+", default=["u2net", "u2net_human_seg"])
    parser.add_argument("--out-dir", default="/tmp/cutout-compare")
    parser.add_argument("--alpha-matting", action="store_true", help="髪の境界を丁寧に（重い）")
    # 姿勢推定は切り抜きマスクを使わないので、比較には不要。
    # macOS では MediaPipe が Metal の初期化で異常終了することがあるため既定では通さない。
    parser.add_argument("--with-pose", action="store_true", help="姿勢推定も通してHPまで出す")
    args = parser.parse_args()

    from rembg import new_session

    original = load_image(args)
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    print(f"\n入力: {original.width}×{original.height}\n")

    rows = []
    masks = {}
    for model in args.models:
        session = new_session(model)
        started = time.perf_counter()
        full = cutout_person(original, largest_only=True, crop=False,
                             session=session, alpha_matting=args.alpha_matting)
        elapsed = time.perf_counter() - started
        mask = silhouette_mask(full)

        path = out_dir / f"{model}.png"
        crop_to_subject(full).save(path)
        masks[model] = mask

        if args.with_pose:
            stats = compute_stats(original.convert("RGB"), mask)
            attack, weight = stats["detail"]["attack"], stats["detail"]["weight"]
            hp = stats["params"]["hp"]
        else:
            attack, weight = compute_attack(mask, None), compute_weight(mask)
            hp = "—"
        rows.append({
            "model": model,
            "秒": f"{elapsed:.1f}",
            "面積比": f"{weight['areaRatio']:.3f}",
            "solidity": f"{attack['solidity']:.3f}",
            "非対称": f"{attack['asymmetry']:.3f}",
            "攻撃": attack["value"],
            "重さ": weight["value"],
            "HP": hp,
        })
        print(f"  {model}: {path}")

    headers = list(rows[0].keys())
    widths = [max(len(str(h)), *(len(str(r[h])) for r in rows)) for h in headers]
    print("\n" + "  ".join(str(h).ljust(w) for h, w in zip(headers, widths)))
    print("  ".join("-" * w for w in widths))
    for row in rows:
        print("  ".join(str(row[h]).ljust(w) for h, w in zip(headers, widths)))

    # 1つ目のモデルとの重なり具合。1.0 なら同じ、値が小さいほど別物。
    if len(args.models) > 1:
        base = masks[args.models[0]]
        print()
        for model in args.models[1:]:
            other = masks[model]
            union = np.logical_or(base, other).sum()
            iou = float(np.logical_and(base, other).sum()) / float(union) if union else 1.0
            print(f"  {args.models[0]} との重なり（IoU）: {model} = {iou:.3f}")
    print(f"\n画像は {out_dir} に保存しました。並べて見比べてください。\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
