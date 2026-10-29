#!/usr/bin/env python3
"""黒バラフォントの「絞った版」を作る。

tools/fonts/kurobara-ui-chars.txt（ui-chars.mjs が書き出す）の文字だけを残して
public/kurobara-gothic-heavy-ui.woff2 に書き出す。字形は元の TTF のまま。

ふつうは npm run font:subset から呼ぶ（文字の一覧の書き出し → これ、の順に動く）。
必要なもの: pip install fonttools brotli
"""
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'public' / 'kurobara-gothic-heavy.ttf'
OUT = ROOT / 'public' / 'kurobara-gothic-heavy-ui.woff2'
CHARS = ROOT / 'tools' / 'fonts' / 'kurobara-ui-chars.txt'


def main():
    chars = CHARS.read_text(encoding='utf-8').rstrip('\n')
    options = subset.Options()
    options.flavor = 'woff2'
    font = TTFont(SRC)
    subsetter = subset.Subsetter(options)
    subsetter.populate(text=chars)
    subsetter.subset(font)
    font.save(OUT)
    print(f'{len(chars)} 文字 → {OUT.name}（{OUT.stat().st_size // 1024} KB）')


if __name__ == '__main__':
    main()
