#!/usr/bin/env python3
"""JSON で受け取った base64 PNG から人物を切り抜き、背景透過 PNG を出力する。

入力 JSON 例:
    {"imageData": "iVBORw0KGgoAAAANSUhEUgAA..."}

使い方:
    python person_cutout.py input.json -o out.png      # ファイルから
    cat input.json | python person_cutout.py - -o out.png  # 標準入力から
    python person_cutout.py input.json --json-out > result.json  # base64 で返す
"""

import argparse
import base64
import binascii
import io
import json
import sys

import numpy as np
from PIL import Image


def read_input(source: str) -> dict:
    """JSON をファイルパスまたは標準入力 ('-') から読み込む。"""
    if source == "-":
        raw = sys.stdin.read()
    else:
        with open(source, "r", encoding="utf-8") as f:
            raw = f.read()
    try:
        return json.loads(raw)
    except json.JSONDecodeError as e:
        raise ValueError(f"入力 JSON の解析に失敗しました: {e}")


def decode_image(b64: str) -> Image.Image:
    """base64 文字列(data URI 接頭辞は任意)を RGBA の PIL Image に変換する。

    入力が不正な場合は ValueError を送出する(サーバ側で 400 に変換しやすいように)。
    """
    if not isinstance(b64, str) or not b64.strip():
        raise ValueError("imageData が空、または文字列ではありません。")
    # "data:image/png;base64,...." 形式の接頭辞を除去
    if b64.startswith("data:"):
        b64 = b64.split(",", 1)[-1]
    b64 = b64.strip()
    try:
        data = base64.b64decode(b64, validate=True)
    except (binascii.Error, ValueError) as e:
        raise ValueError(f"base64 のデコードに失敗しました: {e}")
    try:
        img = Image.open(io.BytesIO(data))
        img.load()
    except Exception as e:  # PIL は様々な例外を投げるため広めに捕捉
        raise ValueError(f"画像として読み込めませんでした: {e}")
    return img.convert("RGBA")


def keep_largest_subject(rgba: Image.Image) -> Image.Image:
    """アルファの連結成分のうち最大のものだけを残す(主被写体一人だけ)。"""
    from scipy import ndimage  # rembg の依存に含まれるため遅延 import

    arr = np.array(rgba)
    mask = arr[..., 3] > 0
    if not mask.any():
        return rgba
    labeled, n = ndimage.label(mask)
    if n <= 1:
        return rgba
    # ラベルごとの面積を数え、最大の成分を主被写体とみなす
    counts = np.bincount(labeled.ravel())
    counts[0] = 0  # 背景ラベルは除外
    largest = int(counts.argmax())
    arr[..., 3] = np.where(labeled == largest, arr[..., 3], 0)
    return Image.fromarray(arr, "RGBA")


def crop_to_subject(rgba: Image.Image) -> Image.Image:
    """アルファ(被写体)の外接矩形でトリミングする。"""
    bbox = rgba.getchannel("A").getbbox()
    return rgba.crop(bbox) if bbox else rgba


def cutout_person(
    img: Image.Image,
    *,
    largest_only: bool = True,
    crop: bool = True,
    alpha_matting: bool = False,
    model: str = "u2net",
    session=None,
) -> Image.Image:
    """rembg で人物を切り抜き、背景透過の RGBA Image を返す。

    session に rembg の session を渡すと使い回せる(サーバで起動時に1回ロードする用)。
    """
    try:
        from rembg import new_session, remove
    except ImportError:
        raise RuntimeError(
            "rembg が見つかりません。`pip install -r requirements.txt` を実行してください。"
        )

    if session is None:
        session = new_session(model)
    result = remove(
        img,
        session=session,
        post_process_mask=True,
        alpha_matting=alpha_matting,
    ).convert("RGBA")

    if largest_only:
        result = keep_largest_subject(result)
    if crop:
        result = crop_to_subject(result)
    return result


def parse_args(argv=None) -> argparse.Namespace:
    p = argparse.ArgumentParser(description="JSON の base64 画像から人物を背景透過で切り抜く")
    p.add_argument("input", help="入力 JSON ファイルのパス('-' で標準入力)")
    p.add_argument("-o", "--output", default="output.png", help="出力 PNG パス(既定: output.png)")
    p.add_argument("--json-key", default="imageData", help="base64 が入っているキー名(既定: imageData)")
    p.add_argument("--model", default="u2net", help="rembg モデル名(既定: u2net)")
    p.add_argument("--all-subjects", action="store_true", help="主被写体だけでなく検出した全領域を残す")
    p.add_argument("--no-crop", action="store_true", help="被写体で切り抜かず元の画像サイズを維持する")
    p.add_argument("--alpha-matting", action="store_true", help="輪郭をより滑らかにする(低速)")
    p.add_argument("--json-out", action="store_true", help="ファイルではなく結果を base64 入り JSON で標準出力に返す")
    return p.parse_args(argv)


def main(argv=None) -> int:
    args = parse_args(argv)
    try:
        payload = read_input(args.input)
        if args.json_key not in payload:
            raise ValueError(f"JSON にキー '{args.json_key}' がありません。")
        img = decode_image(payload[args.json_key])
        result = cutout_person(
            img,
            largest_only=not args.all_subjects,
            crop=not args.no_crop,
            alpha_matting=args.alpha_matting,
            model=args.model,
        )
    except (ValueError, RuntimeError, OSError) as e:
        print(f"エラー: {e}", file=sys.stderr)
        return 1

    if args.json_out:
        buf = io.BytesIO()
        result.save(buf, format="PNG")
        out_b64 = base64.b64encode(buf.getvalue()).decode("ascii")
        json.dump({"imageData": out_b64, "width": result.width, "height": result.height}, sys.stdout)
        sys.stdout.write("\n")
    else:
        result.save(args.output, format="PNG")
        print(f"切り抜き完了: {args.output} ({result.width}x{result.height})", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
