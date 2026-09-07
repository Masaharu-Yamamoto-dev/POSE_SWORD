# cutout_person / rembg / bbox / mask / crop_to_subject の理解まとめ

## 0. 全体像を先に見る

この処理は、最終的に次の流れになります。

```text
入力画像(base64)
   │
   v
decode_image()   -> RGB/RGBA に変換
   │
   v
cutout_person()
   ├─ rembg.remove() で背景除去
   ├─ keep_largest_subject() で最大人物だけ残す
   ├─ crop_to_subject() で人物の周囲を切り抜く
   └─ RGBA 画像を返す
   │
   v
server.py
   ├─ mask = silhouette_mask(full)
   ├─ bbox = full.getchannel("A").getbbox()
   ├─ stats = compute_stats(original.convert("RGB"), mask)
   └─ JSON にして返す
```

### 画像としてのイメージ

```text
元画像
+--------------------------------------+
| 背景がいっぱいある                   |
|   [人物]                             |
+--------------------------------------+
          ↓ rembg.remove()
背景除去後
+--------------------------------------+
| 透明背景                             |
|   [人物だけ]                         |
+--------------------------------------+
          ↓ keep_largest_subject()
人物を1人だけ残す
+------------------+
| [人物]            |
+------------------+
          ↓ crop_to_subject()
人物の外接矩形で切り抜き
+-----------+
| [人物]     |
+-----------+
```

### 真偽マスクのイメージ

```text
alpha の値
0  0  0  0  0
0  255 255 0 0
0  255 255 255 0
0  0 255 0 0
0  0 0 0 0

      ↓ mask = arr[..., 3] > 0

True/False のマスク
F F F F F
F T T F F
F T T T F
F F T F F
F F F F F
```

### ラベル分けのイメージ

```text
mask
F F F F F
F T T F F
F T T T F
F F T F F
F F F F F

      ↓ ndimage.label(mask)

labeled
0 0 0 0 0
0 1 1 0 0
0 1 1 1 0
0 0 1 0 0
0 0 0 0 0
```

ここで、同じ番号のピクセルは「同じ領域」です。  
それを `counts` で面積を数え、最大の領域を主被写体として選びます。

---

## 1. どこから読むべきか（自分で書くなら）

もしこのコードを自分で書くなら、次の順で読むと理解しやすいです。

### 1) 最初に読むところ
- [image-process/person_cutout.py](image-process/person_cutout.py)
  - `decode_image()`
  - `keep_largest_subject()`
  - `crop_to_subject()`
  - `cutout_person()`

### 2) 次に読むところ
- [image-process/server.py](image-process/server.py)
  - `cutout()` 関数
  - `full = cutout_person(...)`
  - `mask = silhouette_mask(full)`
  - `stats = compute_stats(...)`
  - `out = crop_to_subject(full)`

### 3) 最後に読むところ
- [image-process/stats.py](image-process/stats.py)
  - `silhouette_mask()`
  - `compute_attack()`
  - `compute_weight()`
  - `compute_hp()`
  - `compute_stats()`

### 理由
- まず画像前処理のコアが [image-process/person_cutout.py](image-process/person_cutout.py) にある
- その結果を API が扱うのが [image-process/server.py](image-process/server.py)
- 形状と姿勢から数値を作るのが [image-process/stats.py](image-process/stats.py)

つまり、

```text
人物抽出
  → API で受け取り
  → シルエット計算
  → ステータス計算
  → JSON 返却
```

の順で理解するのが自然です。

---

## 1. 最小コード例（自分で書くならこの形）

この処理を最小限で書くと、次のような流れになります。

```python
import numpy as np
from PIL import Image
from scipy import ndimage
from rembg import new_session, remove


def keep_largest_subject(rgba: Image.Image) -> Image.Image:
    arr = np.array(rgba)
    mask = arr[..., 3] > 0
    if not mask.any():
        return rgba

    labeled, n = ndimage.label(mask)
    if n <= 1:
        return rgba

    counts = np.bincount(labeled.ravel())
    counts[0] = 0
    largest = int(counts.argmax())
    arr[..., 3] = np.where(labeled == largest, arr[..., 3], 0)
    return Image.fromarray(arr, "RGBA")


def crop_to_subject(rgba: Image.Image) -> Image.Image:
    bbox = rgba.getchannel("A").getbbox()
    return rgba.crop(bbox) if bbox else rgba


img = Image.open("input.png").convert("RGBA")
session = new_session("u2net")
result = remove(img, session=session, post_process_mask=True, alpha_matting=False).convert("RGBA")
result = keep_largest_subject(result)
result = crop_to_subject(result)
result.save("output.png")
```

### この最小コード例でやっていること

```text
入力画像
  ↓
rembg.remove()
  ↓
alphaマスクを作る
  ↓
領域ごとにラベル付け
  ↓
一番大きい領域だけ残す
  ↓
人物のbounding boxでcrop
  ↓
output.png
```

### 実行例（最小再現）

```python
# 1) 画像を作る（小さな白背景＋黒の人物っぽい塊）
from PIL import Image
import numpy as np

img = Image.new("RGBA", (20, 20), (255, 255, 255, 0))
# 例: 人物のような塊を描く
for x in range(5, 15):
    for y in range(3, 17):
        img.putpixel((x, y), (0, 0, 0, 255))

# 2) ここが本題の処理
arr = np.array(img)
mask = arr[..., 3] > 0
print("mask shape:", mask.shape)
print("non-transparent pixels:", mask.sum())

from scipy import ndimage
labeled, n = ndimage.label(mask)
print("num labels:", n)
counts = np.bincount(labeled.ravel())
counts[0] = 0
largest = int(counts.argmax())
print("largest label:", largest)

arr[..., 3] = np.where(labeled == largest, arr[..., 3], 0)
result = Image.fromarray(arr, "RGBA")
print("result size:", result.size)

bbox = result.getchannel("A").getbbox()
print("bbox:", bbox)
cut = result.crop(bbox)
print("cropped size:", cut.size)
cut.save("minimal_result.png")
print("saved to minimal_result.png")
```

### 実行結果のイメージ

```text
mask shape: (20, 20)
non-transparent pixels: 120
num labels: 1
largest label: 1
bbox: (5, 3, 15, 17)
cropped size: (10, 14)
saved to minimal_result.png
```

これが最小再現の考え方です。  
`mask` が人物の塊を作り、`label` がその塊を区別し、`crop` が人物だけを残します。

---

## 2. 実際の入力と出力の例

### 例1: 入力 JSON

```json
{
  "imageData": "iVBORw0KGgoAAAANSUhEUgAA..."
}
```

### 例2: その後の内部処理のイメージ

```text
元画像
+------------------------------------+
|  背景あり                          |
|  [人物]                            |
+------------------------------------+
            ↓ decode_image()
RGBA画像
+------------------------------------+
|  RGBA で人物と背景が入った画像      |
+------------------------------------+
            ↓ remove()
背景除去後
+------------------------------------+
| 透明背景                           |
|  [人物]                            |
+------------------------------------+
            ↓ keep_largest_subject()
主被写体だけ残す
+------------------------+
|     [人物]             |
+------------------------+
            ↓ crop_to_subject()
人物のbboxで切り抜き
+------------------+
| [人物だけ]       |
+------------------+
```

### 例3: API の返却イメージ

```json
{
  "imageData": "iVBORw0KGgoAAAANSUhEUgAA...",
  "width": 420,
  "height": 560,
  "bbox": {"x": 120, "y": 80, "w": 300, "h": 440},
  "params": {"attack": 68, "weight": 40, "hp": 720},
  "swordName": "炎Aliceソード"
}
```

この JSON の意味:
- `imageData`: 人物だけの透過PNG(base64)
- `bbox`: 人物の位置情報
- `params`: 攻撃力・体格・HP
- `swordName`: 武器名

---

## 3. 図だけで見るまとめ

### A. input → output

```text
[元画像]
   │
   ├─ decode_image()
   │    ↓
   │  RGBA画像
   │
   ├─ remove()
   │    ↓
   │  背景除去済み画像
   │
   ├─ keep_largest_subject()
   │    ↓
   │  最大人物だけ残す
   │
   ├─ crop_to_subject()
   │    ↓
   │  人物だけの小さい画像
   │
   └─ JSON / PNG 返却
```

### B. mask と bbox の関係

```text
alpha画像
+-------------------+
| 透明    [人物]     |
+-------------------+
     │
     ├─ getbbox() → 人物の最小四角
     │
     └─ crop() → その四角だけ切り出す
```

### C. label と counts の関係

```text
mask
F F F F F
F T T F F
F T T T F
F F T F F

   ↓ label()

labeled
0 0 0 0 0
0 1 1 0 0
0 1 1 1 0
0 0 1 0 0

   ↓ bincount()

counts = [背景, area1, area2, area3]

   ↓ argmax()

最大の area を選ぶ
```

---

## 4. rembg の session とは

rembg の `session` は、背景除去モデルを「読み込んだ状態」を再利用できるオブジェクトです。

```python
_REMBG_SESSION = new_session("u2net")
```

意味:
- `new_session("u2net")` は、u2net モデルを初期化して準備する処理
- その戻り値の `session` を使うと、毎回モデルを再ロードせずに済む
- これにより API の高速化ができる

### 例
```python
full = cutout_person(original, largest_only=True, crop=False, session=_REMBG_SESSION)
```

ここで `session` は、モデルの実行状態を再利用しているものです。

---

## 2. `*` の意味

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

`*` の後の引数は、キーワード引数で渡す必要があります。

### 例
```python
cutout_person(
    img,
    largest_only=True,
    crop=True,
    alpha_matting=False,
    model="u2net",
    session=_REMBG_SESSION,
)
```

これが正しい呼び出しです。

### なぜ必要か
- 引数の意味が読みやすい
- 引数順を間違えにくい
- 呼び出し側でミスが減る

---

## 3. `remove` は何か

```python
from rembg import new_session, remove
```

`remove` は rembg ライブラリの本体関数で、画像から背景を除去して人物だけを残す処理です。

```python
result = remove(
    img,
    session=session,
    post_process_mask=True,
    alpha_matting=alpha_matting,
).convert("RGBA")
```

### 引数の意味
- `img`: 入力画像
- `session`: モデルの再利用状態
- `post_process_mask=True`: マスク補正を行う
- `alpha_matting=...`: 輪郭を自然に整える

### `.convert("RGBA")` を付ける理由
`remove()` の出力を `RGBA` に変換することで、透明度 (`A`) を扱えるようになる。

これが必要な理由:
- 後で `mask = arr[..., 3] > 0` に使える
- `bbox = rgba.getchannel("A").getbbox()` で人物の範囲を計算できる
- 背景を透明にしたまま人物だけを切り抜ける

もし `.convert("RGBA")` を付けないと、透明度情報が失われて背景除去後の処理が崩れる可能性がある。

---

## 4. `arr[..., 3]` の意味

```python
arr = np.array(rgba)
mask = arr[..., 3] > 0
```

`arr` は画像データの NumPy 配列です。

画像は多くの場合、各ピクセルが `(R, G, B, A)` を持つので、形状は:

```python
(H, W, 4)
```

`arr[..., 3]` は、最後の次元の 3 番目、つまり alpha（透明度）だけを取り出す。

### 例
```python
arr[0,0] = [255, 0, 0, 128]
```

このとき `arr[..., 3]` は `128` になる。

### `...` の意味
NumPy で `...` は「残りの次元をすべてまとめて扱う」という意味です。

```python
arr[..., 3]
```

は「全てのピクセルに対して、最後のチャネルだけを見る」という意味です。

---

## 5. `mask` が True ということ

```python
mask = arr[..., 3] > 0
```

`mask` は「透明でないピクセルかどうか」を表す boolean 配列です。

### 意味
- `True`: このピクセルは描画されている
- `False`: このピクセルは透明

人物の背景除去後の画像では、人物の部分は不透明、背景は透明なので、
`True` は人物や対象領域に相当する。

### 例
```python
alpha = [
 [0, 0, 0],
 [0, 255, 0],
 [0, 255, 0],
]
```

このとき:

```python
mask = alpha > 0
```

は

```python
[
 [False, False, False],
 [False, True, False],
 [False, True, False],
]
```

になります。

---

## 6. `ndimage.label(mask)` は何をしているか

```python
labeled, n = ndimage.label(mask)
```

これは、`True` の連結した領域ごとに番号を振る処理です。

### 例
```python
mask = np.array([
    [0, 1, 1, 0],
    [0, 1, 0, 0],
    [0, 0, 0, 1],
    [0, 0, 1, 1],
])
```

ここでは透明でない領域が 2 つあるので、`label` は次のようになる。

```python
labeled = np.array([
    [0, 1, 1, 0],
    [0, 1, 0, 0],
    [0, 0, 0, 2],
    [0, 0, 2, 2],
])
```

`n` は領域数で、ここでは `2` です。

### 返り値
- `labeled`: 各ピクセルに「どの領域に属するか」の番号がついた配列
- `n`: 領域の個数

このコードでは、「人物候補の塊が何個あるか」を数えるために使っています。

---

## 7. `counts = np.bincount(labeled.ravel())` の意味

```python
counts = np.bincount(labeled.ravel())
```

`labeled.ravel()` は `labeled` を 1 次元に平らにします。

その後 `np.bincount()` は、各ラベルが何回出現したかを数えます。

### 例
```python
labeled = np.array([
    [0, 0, 1, 1],
    [0, 2, 2, 0],
    [0, 0, 0, 3],
])
```

`ravel()` すると:

```python
[0,0,1,1,0,2,2,0,0,0,0,3]
```

`bincount` の結果は:

```python
counts = [8, 2, 2, 1]
```

これは次の意味です。
- label 0: 背景のピクセル数
- label 1: 領域1の面積
- label 2: 領域2の面積
- label 3: 領域3の面積

---

## 8. `counts[0] = 0` はなぜか

```python
counts[0] = 0
```

背景のラベルは `0` です。

背景は人物ではないので、背景の面積を 0 にして、最大候補から除外します。

### 例
```python
counts = [8, 2, 2, 1]
```

ここで `counts[0] = 0` にすると:

```python
counts = [0, 2, 2, 1]
```

背景が一番大きくても選ばれないようになります。

---

## 9. `largest = int(counts.argmax())` はどういう処理か

```python
largest = int(counts.argmax())
```

`counts.argmax()` は最大値のインデックスを返します。

### 例
```python
counts = [0, 3, 2, 1]
```

このとき最大値は `3` で、その位置は index `1` です。

```python
counts.argmax() == 1
```

`largest` はその index を整数として保持するので:

```python
largest = 1
```

これは「一番大きい領域のラベル番号」を表します。

---

## 10. `arr[..., 3] = np.where(labeled == largest, arr[..., 3], 0)` の意味

```python
arr[..., 3] = np.where(labeled == largest, arr[..., 3], 0)
```

### `labeled == largest`
- 最大領域に属するピクセルは `True`
- それ以外は `False`

### `np.where(condition, arr[..., 3], 0)`
- condition が `True` の場所は元の alpha 値を残す
- condition が `False` の場所は `0`（透明）にする

### その結果
- 最大の人物領域だけ alpha が残る
- 他の人物候補やノイズは透明になる

これは「主被写体だけを残す」処理です。

### 具体例
```python
labeled = [
 [0, 0, 1, 1],
 [0, 2, 2, 0],
]
largest = 1
```

`labeled == largest` は:

```python
[
 [False, False, True, True],
 [False, False, False, False],
]
```

alpha 値がこのような配列なら:

```python
arr[..., 3] = [
 [0, 0, 200, 200],
 [0, 50, 50, 0],
]
```

実行後は:

```python
[
 [0, 0, 200, 200],
 [0, 0, 0, 0],
]
```

になる。

---

## 11. `return Image.fromarray(arr, "RGBA")` の意味

```python
return Image.fromarray(arr, "RGBA")
```

ここでは NumPy の配列を PIL の画像オブジェクトに戻しています。

- `arr` は `H x W x 4` の RGBA 配列
- `"RGBA"` は、その配列が RGBA 形式を表していると指定する
- 最終的に `Image.Image` が返る

### ここで返るもの
- 背景は透明
- 主被写体だけが残る
- 画像として再利用できる

---

## 12. `crop_to_subject` の意味

```python
def crop_to_subject(rgba: Image.Image) -> Image.Image:
    bbox = rgba.getchannel("A").getbbox()
    return rgba.crop(bbox) if bbox else rgba
```

`rgba.getchannel("A")` は alpha チャンネルのみを取り出す。

`getbbox()` は「非透明ピクセルがある最小の矩形」を返す。

```python
bbox = (left, upper, right, lower)
```

`rgba.crop(bbox)` は、その矩形の範囲だけを切り抜く。

### 具体例
元画像が大きく、人物が中央にいる場合:

```python
bbox = (100, 80, 500, 430)
```

このとき、画像の周囲の背景は切り落とされて、人物だけの小さい画像になる。

---

## 13. server.py でのこの一連の処理の意味

```python
full = cutout_person(original, largest_only=True, crop=False, session=_REMBG_SESSION)
mask = silhouette_mask(full)
bbox = full.getchannel("A").getbbox()
stats = compute_stats(original.convert("RGB"), mask)
out = crop_to_subject(full)
```

### この流れの意味
- `full`: 元サイズの人物画像
- `mask`: 人物の形状マスク
- `bbox`: 人物の位置情報
- `stats`: 攻撃力・体格・HP
- `out`: 表示用の切り抜き画像

### 処理の役割分担
- `mask` は AI 計算の入力
- `bbox` は位置情報のメタデータ
- `out` は返却用の画像
- `stats` はゲーム用の数値

---

## 14. `bbox` が本当に使われていないのか

`bbox` は内部計算には使われていないことが多いですが、返却 JSON に埋め込んでフロント側で使えるようにしています。

```python
"bbox": (
    {"x": bbox[0], "y": bbox[1], "w": bbox[2] - bbox[0], "h": bbox[3] - bbox[1]}
    if bbox else None
)
```

これは、人物の位置をクライアント側に伝えるためです。

つまり、
- 計算では使われない
- ただし API の結果としては有用

という状態です。

---

## 15. まとめ

この一連の処理は、最終的に次の3つを作るための流れです。

1. `mask`: 形状情報（攻撃力計算に使う）
2. `stats`: ゲーム用パラメータ（attack / weight / hp）
3. `out`: 人物だけ切り抜いた透過 PNG（返却用）

`bbox` は位置情報として返却されるが、計算の本体では直接使われていない。

`crop_to_subject()` は、人物が存在する部分だけを切り抜いて、背景を消した小さな画像を返す関数。

---

## 16. 一言で言うと

- `mask` = 形状を表すマスク
- `bbox` = 位置を表す矩形
- `stats` = 数値化した戦闘値
- `out` = 表示・返却用の小さな人物画像

この構成で、画像処理とゲーム情報生成が分離されています。
