# cutout_person 関数の完全分析

## 1. この関数の位置と役割

対象ファイル:
- [image-process/person_cutout.py](person_cutout.py)

この関数は、人物画像を入力として受け取り、背景除去済みの人物画像を返す中核関数です。

実装シグネチャ:

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

役割の要点:
- `img`: PIL の画像オブジェクト
- `largest_only`: 主被写体の大きい領域だけ残すか
- `crop`: 背景を切り落として人物部分だけを取り出すか
- `alpha_matting`: 透明境界の滑らかさを高めるか
- `model`: rembg のモデル名
- `session`: rembg のセッションを再利用するための引数

この関数は、単なる「画像編集」ではなく、人物の背景を除去して、後段の処理で使いやすい形式に整える役割を担っています。

---

## 2. この関数が属するファイル全体の文脈

[image-process/person_cutout.py](person_cutout.py) には、次のような処理が揃っています。

- `read_input()`
  - JSON を読み込む
- `decode_image()`
  - base64 画像を PIL の RGBA 画像へ変換する
- `keep_largest_subject()`
  - 最大の人物領域だけ残す
- `crop_to_subject()`
  - 人物のアルファ領域に沿ってトリミングする
- `cutout_person()`
  - rembg で背景除去を実行する
- `parse_args()`
  - CLI 引数の設定
- `main()`
  - JSON を読んで、関数を呼び出し、保存または JSON 返却を行う

この中で `cutout_person()` は、もっとも中心的な処理です。

---

## 3. 関数の中身を逐次解説

### 3-1 rembg の import

```python
try:
    from rembg import new_session, remove
except ImportError:
    raise RuntimeError(
        "rembg が見つかりません。`pip install -r requirements.txt` を実行してください。"
    )
```

ここで最初に `rembg` を import しています。

- `new_session` は rembg 用のセッション作成
- `remove` は実際の背景除去処理
- rembg が未インストールの場合は明確な例外を投げる

この設計により、呼び出し元が `ImportError` の詳細を知らなくても、意味のあるエラーを受け取れます。

---

### 3-2 session の初期化

```python
if session is None:
    session = new_session(model)
```

ここは、高速化のための処理です。

- `session` が渡されない場合は新しいセッションを作成する
- すでにセッションがある場合はそれを再利用する
- `new_session(model)` は、モデル読み込みや内部前処理を一度行うので、リクエストごとに再生成すると遅い

`server.py` ではこのセッションを起動時に1回だけ生成して使い回しています。

---

### 3-3 背景除去の実行

```python
result = remove(
    img,
    session=session,
    post_process_mask=True,
    alpha_matting=alpha_matting,
).convert("RGBA")
```

ここが「背景除去の本体」です。

#### `remove()` の意味
`rembg.remove()` は、画像から人物や対象物を認識し、背景を透過にした新しい画像を返します。

#### パラメータの意図
- `img`: 入力画像
- `session`: あらかじめロードしたモデルセッション
- `post_process_mask=True`
  - マスク生成後に補正をかける
  - 余計なノイズやぼやけを減らす
- `alpha_matting=alpha_matting`
  - `alpha_matting=False` なら高速
  - `True` なら輪郭が自然になりやすいが重い

#### `.convert("RGBA")`
`remove()` の戻り値は、背景除去済みの画像ですが、PIL 画像としての形式が必ずしも `RGBA` であるとは限りません。
そのため、`convert("RGBA")` により、A (alpha) チャンネル付きの透明画像に揃えます。

結果的に、背景が透明で人物だけが残る画像になります。

---

### 3-4 主被写体の絞り込み

```python
if largest_only:
    result = keep_largest_subject(result)
```

`largest_only=True` がデフォルトです。

これは、人物が複数検出された場合に、一番大きな人物領域だけを残す処理です。

この関数は [image-process/person_cutout.py](person_cutout.py) の中で次のように実装されています。

```python
def keep_largest_subject(rgba: Image.Image) -> Image.Image:
    """アルファの連結成分のうち最大のものだけを残す(主被写体一人だけ)。"""
    from scipy import ndimage

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
```

#### 何をしているか
- 透明でないピクセルを「有効領域」とみなす
- `np.where` でマスクを作る
- `ndimage.label()` で連結成分ごとに分離する
- どのラベルが最も大きいかを計算する
- そのラベル以外の alpha 値を 0 にする

#### 結果
- 複数人物 or 複数領域が混ざっていても、最大の人物だけ残る
- 余計な人物や背景が残りにくくなる
- その後の `bbox` 計算やトリミングが安定する

---

### 3-5 人物領域でのトリミング

```python
if crop:
    result = crop_to_subject(result)
```

`crop=True` のとき、人物のアルファ領域の外接矩形で切り抜かれます。

実装:

```python
def crop_to_subject(rgba: Image.Image) -> Image.Image:
    """アルファ(被写体)の外接矩形でトリミングする。"""
    bbox = rgba.getchannel("A").getbbox()
    return rgba.crop(bbox) if bbox else rgba
```

#### `getbbox()` の意味
`A` チャンネルは透明度です。`getbbox()` は、

> 透明でないピクセルが含まれる最小の矩形

を返します。

その矩形だけを切り抜くので、
- 背景を取り除ける
- 画像サイズを小さくできる
- フロントエンドや Unity で扱いやすくなる
- 生成物の PNG が軽くなる

---

## 4. この関数の返り値

`cutout_person()` は最終的に `Image.Image` を返します。

戻り値の特徴:
- 形式は `RGBA`
- 背景は透過
- 人物領域だけが残る
- `crop=True` の場合は人物の_bbox に合わせて小さくなる
- `largest_only=True` の場合は主被写体だけ残る

つまり、最終的には「ゲームや UI にそのまま使える透過人物画像」が返るよう設計されています。

---

## 5. 関数が呼ばれる位置と用途

### 5-1 CLI からの利用
[image-process/person_cutout.py](person_cutout.py) の `main()` では、次のように呼ばれます。

```python
result = cutout_person(
    img,
    largest_only=not args.all_subjects,
    crop=not args.no_crop,
    alpha_matting=args.alpha_matting,
    model=args.model,
)
```

CLI では以下を実現できます。
- `input.json` を読む
- base64 を画像に変換する
- 背景除去を行う
- PNG で保存する
- あるいは base64 JSON で返す

---

### 5-2 API サーバーからの利用
[image-process/server.py](server.py) では次のように使われています。

```python
full = cutout_person(original, largest_only=True, crop=False, session=_REMBG_SESSION)
mask = silhouette_mask(full)
bbox = full.getchannel("A").getbbox()
```

#### ここでの意味
- `crop=False` なので、人物をそのまま元サイズで残す
- `largest_only=True` なので、主被写体だけ残す
- `session=_REMBG_SESSION` で rembg のモデルを再利用する
- `silhouette_mask(full)` に渡して、後続のステータス計算に使う

その後、

```python
out = crop_to_subject(full)
```

で人物だけをトリミングして、フロントに返す透過画像を生成しています。

この構成は非常に重要です。

- `cutout_person()` は「前処理の本体」
- `server.py` は「画像を API で返す準備」
- `stats.py` は「姿勢・比率からステータスを算出する」

というように役割が分かれています。

---

## 6. 重要な設計意図

### 6-1 rembg を抽象化している
`cutout_person()` は `rembg.remove()` の詳細を直接ユーザーに見せません。

そのため、呼び出し側は次のように簡単に扱えます。

- 背景除去したい
- 主被写体だけにしたい
- 切り抜きたい
- 高速化したい

この関数は、低レベル API を薄くラップしたアクセスポイントになっています。

---

### 6-2 session を受け取れる設計
`session` 引数があることで、
- サーバー起動時に1回だけ読み込む
- 毎回 `new_session()` を作らない
- 応答速度を改善できる

という利点があります。

これは本番 API では非常に合理的ですね。

---

### 6-3 crop と largest_only を分離している
この関数は、人物の抽出とトリミングを切り離しています。

- `largest_only` は人物の候補を絞る
- `crop` は最終画像のサイズを絞る

これにより、
- ステータス計算で元画像サイズが必要なとき
- 表示用に人物だけをクロップしたいとき

の使い分けがしやすくなっています。

---

## 7. 実行フローの全体像

```text
入力画像
  ↓
cutout_person()
  ↓
rembg.remove() で背景除去
  ↓
largest_only=True ? 最大人物だけ残す : 全領域残す
  ↓
crop=True ? 人物の bbox でトリミング : そのまま返す
  ↓
RGBA 画像を返す
```

最終的な処理の順序は次のようになります。

1. `remove()` により背景除去
2. `keep_largest_subject()` により主人物を選ぶ
3. `crop_to_subject()` により余計な背景を切り落とす
4. 最終的な `RGBA` 画像として返す

---

## 8. 典型的な利用例

### 8-1 そのまま保存したい

```python
img = decode_image(base64_string)
out = cutout_person(img)
out.save("person.png", format="PNG")
```

- 主被写体のみ
- 余分な背景を除去
- 人物画像を保存

### 8-2 全人物を残したい

```python
img = decode_image(base64_string)
out = cutout_person(img, largest_only=False, crop=True)
```

- 複数の人物候補を残したい場合に使う

### 8-3 どのサイズでも使いたい

```python
img = decode_image(base64_string)
out = cutout_person(img, crop=False)
```

- 元の画像サイズを保ちたい
- `bbox` 計算やシルエット処理で元画像サイズが必要な場合に有効

### 8-4 より自然な輪郭にしたい

```python
img = decode_image(base64_string)
out = cutout_person(img, alpha_matting=True)
```

- ノイズが減る代わりに遅くなる
- 生成された輪郭が自然になる

---

## 9. 失敗や注意点

### 9-1 rembg が未インストール
```python
RuntimeError: rembg が見つかりません。
```

これは `requirements.txt` の不足や未環境構築を意味します。

---

### 9-2 画像が壊れている場合
`decode_image()` 側で `ValueError` を出す設計です。

ただし、`cutout_person()` 自体は画像デコードまでは行わず、すでに PIL 画像が入っている前提です。

---

### 9-3 セッションの使い回しが重要
`session` を使わずに毎回 `new_session(model)` を持つと、
- 処理が重い
- 応答が遅い
- API の負荷が高くなる

そのため、サーバーでは起動時に1回生成して再利用しています。

---

### 9-4 `largest_only` で人物の選択が変わる
複数の主体が写っている画像では、一番大きい人物だけが残るので、
- 「別の人物も残したい」場合は `largest_only=False`
- 「主役だけ残したい」場合は `True`

と使い分けるとよいです。

---

## 10. この関数の位置づけの要約

この関数は、POSe_SWORD の画像処理において非常に中心的な関数です。

- [image-process/person_cutout.py](person_cutout.py) で、人物のみを抽出する中核機能
- [image-process/server.py](server.py) で API の入力画像から人物を切り抜く処理として利用
- [image-process/stats.py](stats.py) と組み合わせて、姿勢やシルエットからゲームステータスを計算するための前処理

要するに、

> 画像を受け取り、人物だけを切り抜き、背景透過画像として返す

という役割です。

そして、その返り値は後段のステータス解析やフロントで表示する際の基盤になります。

---

## 11. 一言で言うと

`cutout_person()` は、`rembg` を使って背景除去を行い、主被写体を絞り込み、必要なら人物だけをトリミングして返す、人物画像前処理の中核関数です。

この関数を理解できれば、
- 画像処理の入口
- サーバーとの接続
- フロントで渡す画像形式
- 人物抽出の設計思想

が見えてきます。

---

## 12. 重要なコードポインタ

- [image-process/person_cutout.py](person_cutout.py#L46-L56) — `keep_largest_subject()` と `crop_to_subject()`
- [image-process/person_cutout.py](person_cutout.py#L58-L87) — `cutout_person()` の本体
- [image-process/server.py](server.py#L91-L99) — API での実利用箇所
- [image-process/person_cutout.py](person_cutout.py#L107-L145) — CLI 経路と結果出力

---

## 13. 実装の全体像を図で見る

```text
base64 image
   ↓
decode_image()
   ↓
cutout_person()
   ├─ rembg.remove()で背景除去
   ├─ largest_only=True で主被写体だけ残す
   ├─ crop=True で人物の bbox でトリミング
   └─ RGBA PNGとして返す
   ↓
server.py
   ├─ mask を計算して stats.py に渡す
   └─ 表示用の透過画像を再度トリミングして返す
```

---

## 14. 結論

`cutout_person()` は、POSe_SWORD の中でも「画像前処理の核」を担う関数です。

- 背景除去
- 主被写体抽出
- 人物のトリミング
- 透明PNGとしての返却

を一括で行います。

この関数の設計が良い理由は、
- rembg の細部を抽象化している
- API と CLI で共通利用できる
- セッション再利用で高速化できる
- 後段の `stats.py` と自然に連携できる

ことです。

そのため、画像処理全体の流れを理解したいなら、この関数を最初に読むべきです。
