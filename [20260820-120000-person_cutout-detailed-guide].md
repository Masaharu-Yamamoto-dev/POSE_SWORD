# 2026-08-20 12:00:00 — person_cutout.py 詳細解説

## 1. このファイルの役割

[image-process/person_cutout.py](image-process/person_cutout.py) は、

- JSON で受け取った base64 画像を読み込む
- 背景を除去して人物だけを抽出する
- 主被写体だけを残す
- 必要なら被写体部分だけで切り抜く
- PNG を保存したり、base64 JSON として返したりする

ことを担当する処理ファイルです。

このファイルは、AI 画像処理の「前処理と主体抽出」の本体です。

---

## 2. 全体像

このファイルの流れは大きく分けて次の4段階です。

1. 入力を読む
   - JSON ファイルまたは標準入力から画像データを読む
2. 画像を decode する
   - base64 文字列を PIL の画像へ変換する
3. 人物を切り抜く
   - rembg を利用して背景除去を行う
4. 出力する
   - PNG 保存または base64 JSON を返す

処理の流れは次のようになります。

```text
JSON input
  ↓
read_input()
  ↓
decode_image()
  ↓
cutout_person()
  ↓
keep_largest_subject()
  ↓
crop_to_subject()
  ↓
output PNG / JSON
```

---

## 3. 依存ライブラリ

このファイルは以下のライブラリを使っています。

- argparse
  - コマンドライン引数を処理する
- base64
  - base64 をエンコード/デコードする
- binascii
  - base64 の不正形式を検知する
- io
  - バイト列と画像を扱う
- json
  - JSON の読み書き
- sys
  - 標準入力/標準出力
- numpy
  - 画像のマスク処理に使う
- PIL.Image
  - 画像の読み込み、変換、保存
- rembg
  - 背景除去の本体
- scipy.ndimage
  - 最大連結成分の抽出に使う

重要なのは、背景除去自体の実体は rembg にあり、このファイルではそれをラップして使っている点です。

---

## 4. 最初の関数: read_input()

```python
def read_input(source: str) -> dict:
    """JSON をファイルパスまたは標準入力 ('-') から読み込む。"""
```

### 役割

- JSON ファイルを開く
- 標準入力から JSON を読む
- `json.loads()` で Python の dict に変換する
- 変換失敗時は `ValueError` を投げる

### 実装の意味

```python
if source == "-":
    raw = sys.stdin.read()
else:
    with open(source, "r", encoding="utf-8") as f:
        raw = f.read()
```

- `-` の場合は標準入力を読む
- それ以外はファイルとして開く

そのあと、

```python
return json.loads(raw)
```

で JSON を Python の辞書に変換します。

### 失敗時の挙動

```python
except json.JSONDecodeError as e:
    raise ValueError(f"入力 JSON の解析に失敗しました: {e}")
```

JSON の形式が壊れていると、ちゃんとした例外として上に伝えます。

### なぜ大事か

この段階で入力が JSON でなければ、後続の処理が成立しないので、ここで止めるのが正しい設計です。

---

## 5. 画像 decode の核: decode_image()

```python
def decode_image(b64: str) -> Image.Image:
    """base64 文字列(data URI 接頭辞は任意)を RGBA の PIL Image に変換する。"""
```

### 役割

- base64 文字列を画像バイナリに戻す
- 画像として PIL で読み込む
- `RGBA` 形式に変換する

### 典型的な入力

```json
{"imageData": "iVBORw0KGgoAAAANSUhEUgAA..."}
```

または、ブラウザからよく来る次の形式も対応します。

```text
data:image/png;base64,....
```

### まず行うこと

```python
if not isinstance(b64, str) or not b64.strip():
    raise ValueError("imageData が空、または文字列ではありません。")
```

- `imageData` がない・空・文字列じゃない→不正
- 早期に止める

### data URI への対応

```python
if b64.startswith("data:"):
    b64 = b64.split(",", 1)[-1]
```

ブラウザの base64 画像データには `data:image/png;base64,...` が含まれることがあるので、

- 先頭の `data:...;base64,`
- その後ろだけを使う

といった処理が必要です。

### base64 デコード

```python
try:
    data = base64.b64decode(b64, validate=True)
except (binascii.Error, ValueError) as e:
    raise ValueError(f"base64 のデコードに失敗しました: {e}")
```

この部分は、

- 正しい base64 であるか
- 文字列が壊れていないか

を確認しています。

### PIL で画像として開く

```python
try:
    img = Image.open(io.BytesIO(data))
    img.load()
except Exception as e:
    raise ValueError(f"画像として読み込めませんでした: {e}")
```

- バイト列を `BytesIO` に入れる
- `Image.open()` で image object を作る
- `img.load()` で実際にメモリへ読み込む

画像として解釈できない場合や壊れている場合は `ValueError` に変換します。

### 最終的な形式

```python
return img.convert("RGBA")
```

RGBA に変換して返します。理由は背景除去の結果が透明画像になるからです。

---

## 6. 主被写体だけ残す: keep_largest_subject()

```python
def keep_largest_subject(rgba: Image.Image) -> Image.Image:
    """アルファの連結成分のうち最大のものだけを残す(主被写体一人だけ)。"""
```

### 役割

リムーブされた画像には、人物以外のノイズや小さな領域が残ることがあります。

この関数は、

- アルファ値が 0 でないピクセルを「有効領域」として扱う
- 連結成分ごとに分割する
- 最大の領域だけ残す
- それ以外の領域を透明にする

という処理を行います。

### 実装の解説

```python
from scipy import ndimage
```

これは遅延 import です。必要なときだけ import する設計になっています。

```python
arr = np.array(rgba)
mask = arr[..., 3] > 0
```

- `arr` は画像のNumPy配列
- `arr[..., 3]` はアルファチャンネル
- アルファが 0 より大きい場所が「何かがある領域」

```python
if not mask.any():
    return rgba
```

有効領域が一つもない時は何もしません。

### ラベル付け

```python
labeled, n = ndimage.label(mask)
```

- 連結成分ごとにラベルを付ける
- `n` は領域数

```python
if n <= 1:
    return rgba
```

領域が1個しかない場合は、そのまま返します。

### 最大面積の領域を選ぶ

```python
counts = np.bincount(labeled.ravel())
counts[0] = 0
largest = int(counts.argmax())
```

- `bincount` で各ラベルのピクセル数を数える
- 背景のラベル 0 は除外
- 最大の領域を探す

```python
arr[..., 3] = np.where(labeled == largest, arr[..., 3], 0)
return Image.fromarray(arr, "RGBA")
```

- 最大のラベルの領域だけ透明度を保つ
- それ以外の領域を透明にする

つまり「最大の人物だけを残す」という処理です。

### この関数が必要な理由

rebg の結果は、

- 人物だけでなく小さなノイズ
- 背景に混ざった塊
- 複数人の候補

を含むことがあり、ゲームのステータス計算では主要人物に絞ったほうが良いからです。

---

## 7. 被写体の外接矩形で切り抜く: crop_to_subject()

```python
def crop_to_subject(rgba: Image.Image) -> Image.Image:
    """アルファ(被写体)の外接矩形でトリミングする。"""
```

### 役割

人物の透明マスクから、実際に被写体が存在する範囲だけを切り抜きます。

```python
bbox = rgba.getchannel("A").getbbox()
return rgba.crop(bbox) if bbox else rgba
```

### `getbbox()` の意味

`A` チャンネルはアルファ値です。ここで `getbbox()` は、

> 透明でないピクセルがある最小の矩形

を返します。

その矩形だけを crop するので、余計な背景が消えます。

### 効果

- 画像のサイズが小さくなる
- 背景がない人物だけを返せる
- 生成した PNG が軽くなる
- Unity やフロントで扱いやすくなる

---

## 8. 人物切り抜きの本体: cutout_person()

```python
def cutout_person(
    img: Image.Image,
    *,
    largest_only: bool = True,
    crop: bool = True,
    alpha_matting: bool = False,
    model: str = "u2net",
    session=None,
) -> Image.Image:
```

### 役割

これは rembg を使って人物を背景透過された PNG に変換する核心関数です。

### 重要なポイント

- `img` は PIL の画像
- `largest_only=True` にすると最大の主被写体だけ残す
- `crop=True` にすると被写体部分だけで切り抜く
- `alpha_matting` は輪郭の自然さの向上
- `session` を使い回すことで高速化できる

### まずやること

```python
try:
    from rembg import new_session, remove
except ImportError:
    raise RuntimeError(
        "rembg が見つかりません。`pip install -r requirements.txt` を実行してください。"
    )
```

この処理で rembg がないときに明確な失敗を知らせます。

### session の扱い

```python
if session is None:
    session = new_session(model)
```

- `session` が渡されていなければ新しく作る
- サーバー側では最初に1回だけ生成して使い回す

### remove() の実行

```python
result = remove(
    img,
    session=session,
    post_process_mask=True,
    alpha_matting=alpha_matting,
).convert("RGBA")
```

ここが本当に人物抽出をしている部分です。

- `remove` は背景除去の本体
- `post_process_mask=True` はマスク改善
- `alpha_matting` は境界の滑らかさを上げるが、遅くなる
- `.convert("RGBA")` で透明画像へ変換

### 最大人物への絞り込み

```python
if largest_only:
    result = keep_largest_subject(result)
```

### トリミング

```python
if crop:
    result = crop_to_subject(result)
```

これで最終的に背景除去済みの人物画像が戻ります。

### この関数の設計意図

この関数の良いところは、

- rembg の詳細を隠蔽している
- 高速化のため session を受け取れる
- CLI / API / サーバーの両方で使える

という点です。

---

## 9. コマンドライン引数の定義: parse_args()

```python
def parse_args(argv=None) -> argparse.Namespace:
```

### 役割

このスクリプトをコマンドから使うときの引数を定義します。

### 主要な引数

- input
  - 入力 JSON ファイルのパス
- -o / --output
  - 出力 PNG ファイル
- --json-key
  - base64 が入っているキー名
- --model
  - rembg のモデル名（例: u2net）
- --all-subjects
  - すべての領域を残す
- --no-crop
  - トリミングしない
- --alpha-matting
  - より自然だが重い処理
- --json-out
  - PNG 保存ではなく base64 JSON を出力

### 例

```bash
python person_cutout.py input.json -o out.png
```

```bash
cat input.json | python person_cutout.py - -o out.png
```

```bash
python person_cutout.py input.json --json-out > result.json
```

---

## 10. 実行本体: main()

```python
def main(argv=None) -> int:
```

### 役割

コマンドとして呼ばれたときに、入力読み込み→切り抜き→出力をまとめて実行します。

### 実装の流れ

```python
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
```

ここでは、

- JSON を読む
- `imageData` があるか確認する
- decode する
- cutout する
- 失敗したら stderr に説明を出す

という順番です。

### 出力の分岐

```python
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
```

### これは重要

- `--json-out` が true なら PNG を base64 に変換して JSON を返す
- それ以外ならファイル出力する

つまり、このスクリプトは「CLI でも API でも使える設計」になっています。

---

## 11. ファイル末尾の起動処理

```python
if __name__ == "__main__":
    raise SystemExit(main())
```

このコードにより、

- スクリプトとして直接実行されたときだけ main を呼ぶ
- 終了コードをそのまま OS に返す

という動作になります。

---

## 12. まとめ：このファイルがやっていること

[image-process/person_cutout.py](image-process/person_cutout.py) の本質は次のとおりです。

1. base64 画像を受け取る
2. 画像として読み込む
3. rembg で人物だけを抽出する
4. 最大人物だけを残す
5. 被写体切り抜きで背景を削る
6. PNG または base64 JSON を返す

これは、他のコードと組み合わせると次のように使われます。

- [image-process/server.py](image-process/server.py)
  - サーバーから受け取った画像をこの関数に渡す
- [image-process/stats.py](image-process/stats.py)
  - 切り抜いた後の人物から姿勢や比率を計算する

### つまり

このファイルは、「人物を切り抜いて、ゲームに使える形に整える」ための中核処理です。

---

## 13. 実際の処理順を要約すると

```text
1. read_input() で JSON を読む
2. decode_image() で base64 → RGBA に変換
3. cutout_person() で rembg により背景除去
4. keep_largest_subject() で人物候補の中から最大のものを選ぶ
5. crop_to_subject() で余計な背景を切り落とす
6. PNG または JSON として出力する
```

これがこのファイルの全体像です。

---

## 14. ひとことで言うと

このファイルは、

> 画像を受け取って、人物だけを抽出し、見やすく整えた後、ゲームで使える形に変換する

ための処理の集まりです。

そして、

- [image-process/server.py](image-process/server.py) は API 層
- [image-process/person_cutout.py](image-process/person_cutout.py) は 画像前処理の本体
- [image-process/stats.py](image-process/stats.py) は ステータス計算の本体

という役割分担になっています。
