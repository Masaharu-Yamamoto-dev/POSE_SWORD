#!/usr/bin/env python3
"""手元の画像ファイル(jpg/png 等)を person_cutout.py 用の入力 JSON に変換する。

使い方:
    python make_input.py 写真.jpg -o input.json
"""

import argparse
import base64
import json
import sys


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description="画像ファイルを base64 入り JSON に変換する")
    p.add_argument("image", help="変換したい画像ファイル(jpg/png/webp 等)")
    p.add_argument("-o", "--output", default="input.json", help="出力 JSON パス(既定: input.json)")
    p.add_argument("--key", default="imageData", help="base64 を入れるキー名(既定: imageData)")
    args = p.parse_args(argv)

    try:
        with open(args.image, "rb") as f:
            data = f.read()
    except OSError as e:
        raise SystemExit(f"画像を読み込めませんでした: {e}")

    b64 = base64.b64encode(data).decode("ascii")
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump({args.key: b64}, f)
    print(f"作成: {args.output} ({len(data)} bytes -> {len(b64)} b64 chars)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
