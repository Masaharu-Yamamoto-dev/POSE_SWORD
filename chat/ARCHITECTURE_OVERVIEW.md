# image-process ディレクトリ分析メモ

## 1. 目的

このディレクトリは、ユーザーが撮影した人物画像を入力として受け取り、
人物を背景透過で切り抜き、姿勢とシルエットを解析して、ゲーム用のステータスを算出するための処理群です。

最終的には、React 側から呼ばれる API が以下を返します。

- 透過PNGの画像データ
- 画像の幅・高さ
- bbox
- status: attack / weight / hp
- swordName

---

## 2. ディレクトリ構造

- [README.md](README.md)
  - このディレクトリの使い方と API の仕様がまとまったドキュメント
- [person_cutout.py](person_cutout.py)
  - 画像から人物を切り抜く本体
  - CLI と API で共通利用される処理
- [server.py](server.py)
  - FastAPI による HTTP API の入口
  - Web アプリから受けた画像を処理して JSON を返す
- [stats.py](stats.py)
  - シルエットと姿勢からゲームステータスを計算する
- [make_input.py](make_input.py)
  - 画像ファイルを JSON に変換する補助スクリプト
- [input.json](input.json)
  - サンプル入力
- [Dockerfile](Dockerfile)
  - コンテナ起動用

---

## 3. 主要なエントリーポイント

### 3-1 CLI 入口

- [person_cutout.py](person_cutout.py#L58-L145)

このファイルの `main()` が CLI の本体です。

処理順:
1. JSON から `imageData` を読み取る
2. `decode_image()` で base64 を PIL 画像に変換する
3. `cutout_person()` で人物のみを切り抜く
4. 画像を PNG 保存 or JSON で返す

### 3-2 API 入口

- [server.py](server.py#L69-L117)

FastAPI の `@app.post("/cutout")` がメイン入口です。

処理順:
1. `imageData` を decode
2. `cutout_person` で人物を切り抜く
3. `silhouette_mask` で切り抜きマスクを作る
4. `compute_stats(original.convert("RGB"), mask)` でステータス計算
5. `crop_to_subject` で人物部分だけをトリミング
6. base64 エンコードして返す

---

## 4. 処理フローの詳細

### 4-1 入力

- [person_cutout.py](person_cutout.py#L18-L44)

`decode_image()` は次を担います。

- base64 文字列の取得
- `data:image/png;base64,...` の接頭辞除去
- `PIL.Image` の RGBA 形式への変換
- 不正データ時に `ValueError` を投げる

### 4-2 人物切り抜き

- [person_cutout.py](person_cutout.py#L46-L87)

`cutout_person()` は rembg を使って背景除去を行います。

重要なポイント:
- `remove(...)` により背景を除去
- `post_process_mask=True` でマスクを補正
- `largest_only=True` では主被写体のみに絞る
- `crop=True` で人物だけをトリミングした画像を返す

`keep_largest_subject()` は、人物が複数検出された場合に一番大きい領域のみを残す処理です。

### 4-3 切り抜き後の最終画像

- [person_cutout.py](person_cutout.py#L46-L56)

`crop_to_subject()` は、人物のアルファマスクがある領域の bbox を使って、人物を切り出します。

これにより最終的に「背景が透過された人物画像」が出来上がります。

---

## 5. ステータス設計

### 5-1 attack

- [stats.py](stats.py#L78-L119)

`compute_attack()` は、人物のシルエットの「尖り具合」を計算します。

計算の考え方:
- convex hull で包囲領域を求める
- area / hull_area を solidity として計算
- erin? もっとわかりやすく言うと「角ばっているほど攻撃力が高くなる」
- 左右非対称度も加算して最終値を決める

### 5-2 weight

- [stats.py](stats.py#L121-L128)

`compute_weight()` は、人物のシルエット面積比率を使って体格を算出します。

- 画像全体に対する人物の占有率が大きいほど重い
- 1〜100 に正規化される

### 5-3 hp

- [stats.py](stats.py#L130-L170)

`compute_hp()` は姿勢から HP を決めます。

- 肩の中点と腰の中点で背筋がまっすぐかを見る
- 足首の距離で足が開いているかを見る
- これを組み合わせて HP を決定

姿勢の推定には MediaPipe の Pose Landmarker を使っています。

---

## 6. MediaPipe の使い方

- [stats.py](stats.py#L170-L200)

`_get_landmarker()` と `_run_pose()` が姿勢検出の入口です。

流れ:
1. `_ensure_pose_model()` でモデルが無ければダウンロード
2. `PoseLandmarker.create_from_options()` でモデルを生成
3. `_run_pose()` で画像から landmarks を抽出
4. `compute_hp()` に渡す

ここで `landmarks` が取得できない場合は `hp` を安全のため最小値にする設計です。

---

## 7. 最終的な返却 JSON

- [server.py](server.py#L76-L117)

API の返却例は次のような構造です。

```json
{
  "imageData": "<透過PNGのbase64>",
  "width": 576,
  "height": 406,
  "bbox": { "x": 177, "y": 239, "w": 576, "h": 406 },
  "params": { "attack": 100, "weight": 9, "hp": 989 },
  "detail": { "attack": {...}, "weight": {...}, "hp": {...} },
  "swordName": "炎の剣"
}
```

意味:
- `imageData` → 画面表示用の透過PNG
- `params.*` → ゲームに使う数値
- `detail.*` → デバッグ用の内訳
- `swordName` → 剣の名前

---

## 8. 一連の処理を図でまとめる

```text
入力: base64 画像
  ↓
decode_image()
  ↓
cutout_person()
  ├─ rembg で背景除去
  ├─ 主被写体に限定
  └─ 人物だけをトリミング
  ↓
stats.py
  ├─ attack = compute_attack()
  ├─ weight = compute_weight()
  └─ hp = compute_hp() with MediaPipe Pose
  ↓
server.py /cutout
  ├─ 画像を base64 化
  ├─ params に埋め込む
  ├─ swordName を生成
  └─ JSON を返却
```

---

## 9. 重要なコードポインター

最初に読むべき順番:

1. [image-process/README.md](README.md)
2. [image-process/server.py](server.py#L69-L117)
3. [image-process/person_cutout.py](person_cutout.py#L58-L145)
4. [image-process/stats.py](stats.py#L78-L200)

特に重要な関数:

- `decode_image()` — [person_cutout.py](person_cutout.py#L18-L44)
- `cutout_person()` — [person_cutout.py](person_cutout.py#L61-L87)
- `compute_attack()` — [stats.py](stats.py#L78-L119)
- `compute_weight()` — [stats.py](stats.py#L121-L128)
- `compute_hp()` — [stats.py](stats.py#L130-L170)
- `cutout()` — [server.py](server.py#L69-L117)

---

## 10. 結論

image-process は、単なる「画像を切り抜く処理」ではなく、
「人物画像から剣の能力値を生成する前処理エンジン」になっています。

- 画像の背景除去
- 人物の姿勢解析
- その見た目から攻撃力・重さ・HP を作る
- JSON でフロントに渡す

という流れが、全体の設計の中心です。
