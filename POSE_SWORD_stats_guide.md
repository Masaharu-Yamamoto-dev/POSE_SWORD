# POSE_SWORD / stats.py 解説メモ

## 1. 概要

このファイルは、人物画像からゲーム用ステータスを計算するための処理をまとめたものです。  
主な目的は次の3つです。

- `attack`: 形の鋭さ、攻撃力
- `weight`: シルエットの大きさ、体格
- `hp`: 姿勢の良さと足の開きから決まる耐久力

実際の入口は [image-process/server.py](image-process/server.py) から呼ばれており、そこでは人物切り抜きの後に `compute_stats(original.convert("RGB"), mask)` を実行します。

---

## 2. このファイルが担う責務

[image-process/stats.py](image-process/stats.py) は大きく分けて次の責務を持っています。

1. シルエットのマスク生成
   - `silhouette_mask()`
   - RGBA のアルファ値から人物部分だけを抽出

2. 攻撃力計算
   - `compute_attack()`
   - 形が尖っているか
   - 左右の非対称があるか

3. 体格計算
   - `compute_weight()`
   - 画像中に人物がどれだけ占めているか

4. HP 計算
   - `compute_hp()`
   - 背筋がまっすぐか
   - 足が開いているか

5. 全体統合
   - `compute_stats()`
   - 3つの値をまとめて返す

---

## 3. 全体の処理フロー

```text
画像入力
  ↓
背景除去
  ↓
人物マスク生成
  ↓
stats.py
  ├─ attack = compute_attack(mask, landmarks)
  ├─ weight = compute_weight(mask)
  └─ hp = compute_hp(landmarks, width, height)
  ↓
JSON返却
  ├─ params
  └─ detail
```

---

## 4. import しているライブラリ一覧と説明

### 標準ライブラリ
- `math`
  - 角度計算に使用
  - `atan2`, `pi` を使う
- `urllib.request`
  - MediaPipe のモデルをネットワークからダウンロードするために使用
- `pathlib.Path`
  - ダウンロード先のローカルパスを管理

### 外部ライブラリ
- `numpy`
  - 画像マスクを配列として扱う
  - `np.array`, `np.logical_xor`, `np.logical_or` を多用
- `PIL.Image`
  - 画像の読み込み、アルファチャンネル取得、RGB変換に使用
- `skimage`
  - `convex_hull_image` を使って convex hull を計算
- `mediapipe`
  - PoseLandmarker で人物の関節位置を検出

---

## 5. 主要な関数の解説

### 5-1 `_clamp01()`

```python
def _clamp01(x: float) -> float:
    return max(0.0, min(1.0, x))
```

これは値を 0〜1 に丸める関数です。  
正規化したいときに使う安全装置です。

---

### 5-2 `silhouette_mask()`

```python
def silhouette_mask(rgba: Image.Image) -> np.ndarray:
    return np.array(rgba.getchannel("A")) > 0
```

- RGBA 画像の `A` はアルファチャンネル
- 透過されていないところが人物の存在範囲
- そのマスクを `True/False` の配列として返す

これは attack / weight / hp の入力の中心です。

---

### 5-3 `_compute_asymmetry()`

左右非対称度を計算します。

- 肩の中心と腰の中心を結ぶ軸を基準に左右を反転比較する
- 左右のシルエットがどう違うかを測る
- `attack` の一部として使われる

```python
shoulder_cx = (landmarks[L_SHOULDER].x + landmarks[R_SHOULDER].x) / 2 * width
hip_cx = (landmarks[L_HIP].x + landmarks[R_HIP].x) / 2 * width
center_x = (shoulder_cx + hip_cx) / 2
```

ここで「体の重心軸」を見つけて、そこを基準に左右を比較します。

---

### 5-4 `compute_attack()`

```python
def compute_attack(mask: np.ndarray, landmarks=None) -> dict:
```

トップの考え方:

- `solidity = area / hull_area`
- 形が尖っているほど `solidity` は小さくなる
- 尖り具合を `spikiness` として計算
- 左右非対称度も加算して最終攻撃力を決める

```python
hull_area = int(convex_hull_image(mask).sum())
solidity = area / hull_area if hull_area else 1.0
spikiness = 1.0 - solidity
```

`skimage` の convex hull を使って「人物の輪郭を包む凸包」を作り、その中でどれだけ詰まっているかを見る設計です。

---

### 5-5 `compute_weight()`

```python
def compute_weight(mask: np.ndarray) -> dict:
```

人物が画像全体の中で占める面積比率を計算します。

```python
area_ratio = float(mask.sum()) / float(w * h)
```

- 面積が大きいほど体格が高くなる
- 小さいほど軽い

---

### 5-6 `compute_hp()`

```python
def compute_hp(landmarks, width: int, height: int) -> dict:
```

ここが一番重要な姿勢計算です。

#### 1. 座標をピクセル座標に変換

```python
def px(i):
    lm = landmarks[i]
    return np.array([lm.x * width, lm.y * height])
```

MediaPipe の座標は `x`, `y` が `0〜1` なので、画像サイズに合わせて変換しています。

#### 2. 背筋のまっすぐさを計算

```python
shoulder_mid = (px(L_SHOULDER) + px(R_SHOULDER)) / 2
hip_mid = (px(L_HIP) + px(R_HIP)) / 2
v = hip_mid - shoulder_mid
angle_from_vertical = math.atan2(abs(v[0]), abs(v[1]) + 1e-6)
uprightness = _clamp01(1 - angle_from_vertical / (math.pi / 2))
```

- 肩の中心と腰の中心をつなぐ線がほぼ縦方向なら、背筋がまっすぐ
- 横に大きくずれているほど、傾いているとみなす

#### 3. 足の開きを計算

```python
ankle_dist = abs(px(L_ANKLE)[0] - px(R_ANKLE)[0])
spread = _clamp01((ankle_dist / shoulder_w) / HP_SPREAD_MAX)
```

- 足首どうしの横距離が大きいほど、足が開いている
- その割合が大きいほど HP が高くなる

#### 4. 最終スコア

```python
score = HP_SPINE_WEIGHT * uprightness + HP_SPREAD_WEIGHT * spread
return {
    "value": int(round(100 + score * 900)),
    "uprightness": round(uprightness, 3),
    "legSpread": round(spread, 3),
    "poseDetected": True,
}
```

HP は次の式で決まります。

$$
HP = 100 + (0.5 \times uprightness + 0.5 \times spread) \times 900
$$

---

## 6. `_get_landmarker()` と `_run_pose()` の解説

### `_get_landmarker()`

```python
def _get_landmarker():
    global _LANDMARKER
    if _LANDMARKER is None:
        from mediapipe.tasks import python as mp_python
        from mediapipe.tasks.python import vision

        options = vision.PoseLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=_ensure_pose_model()),
            running_mode=vision.RunningMode.IMAGE,
            num_poses=1,
        )
        _LANDMARKER = vision.PoseLandmarker.create_from_options(options)
    return _LANDMARKER
```

- MediaPipe の PoseLandmarker を生成する
- 初回だけ生成し、後は使い回す
- `model_asset_path` に姿勢推定モデルを指定

### `_run_pose()`

```python
def _run_pose(rgb: Image.Image):
    import mediapipe as mp

    arr = np.ascontiguousarray(np.array(rgb.convert("RGB")))
    mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=arr)
    result = _get_landmarker().detect(mp_image)
    if not result.pose_landmarks:
        return None
    return result.pose_landmarks[0]
```

- 画像を MediaPipe が読める形式へ変換
- `detect()` で姿勢検出
- `result.pose_landmarks[0]` が最初の人物の landmark 群
- 検出できなければ `None`

---

## 7. MediaPipe Pose の 33 個の landmark 一覧

このコードでは使用しているのは一部ですが、MediaPipe の全体は 33 個です。

```text
0   nose
1   left_eye_inner
2   left_eye
3   left_eye_outer
4   right_eye_inner
5   right_eye
6   right_eye_outer
7   left_ear
8   right_ear
9   mouth_left
10  mouth_right
11  left_shoulder
12  right_shoulder
13  left_elbow
14  right_elbow
15  left_wrist
16  right_wrist
17  left_pinky
18  right_pinky
19  left_index
20  right_index
21  left_thumb
22  right_thumb
23  left_hip
24  right_hip
25  left_knee
26  right_knee
27  left_ankle
28  right_ankle
29  left_heel
30  right_heel
31  left_foot_index
32  right_foot_index
```

---

## 8. 33 個を人型に並べた図

```text
                           0  nose
                           |
                         1   2   3
                         \   |   /
                          \  |  /
                           7  8
                         9     10

11 left_shoulder ---------------- 12 right_shoulder
        |                               |
      13 elbow                       14 elbow
        |                               |
      15 wrist                       16 wrist
        |                               |
        |                               |
23 left_hip --------------------- 24 right_hip
    |                               |
25 left_knee                   26 right_knee
    |                               |
27 left_ankle                 28 right_ankle
    |                               |
29 left_heel                  30 right_heel
    |                               |
31 left_foot_index         32 right_foot_index
```

このコードで使っているのは主に:

- `11`, `12` = 肩
- `23`, `24` = 腰
- `27`, `28` = 足首

です。

---

## 9. HP 計算の図解

```text
                   shoulder_mid = (11 + 12) / 2
                           *
                             |
                             |
                             |
                           *
                   hip_mid = (23 + 24) / 2

             背筋がまっすぐなら、肩と腰の中心がほぼ縦に並ぶ
```

```text
left_ankle(27)      right_ankle(28)
      *--------------------*
   足首間距離 = |27.x - 28.x|
```

```text
肩幅 = |11.x - 12.x|
足の開き = ankle_dist / shoulder_w
```

そして最終的に、

```text
score = 0.5 * uprightness + 0.5 * legSpread
hp    = 100 + score * 900
```

---

## 10. landmark と attack / weight / hp の対応表

| 種別 | 使用する landmark | 理由 |
|---|---|---|
| attack | `mask` を中心に使用、必要に応じて `11,12,23,24` | 形の尖り、左右非対称度 |
| weight | なし（`mask` の面積比率だけ） | シルエットの大きさ |
| hp | `11,12,23,24,27,28` | 背筋の真直度、足の開き |

### 実際の対応
- `11,12` → 肩の左右
- `23,24` → 腰の左右
- `27,28` → 足首の左右
- これらで「まっすぐさ」と「足の開き」を判断

---

## 11. 最終まとめ

このコードは、単なる画像処理ではなく、

> 「人物の姿勢と形をゲームステータスへ変換するエンジン」

です。

- `attack` は「形の鋭さ」
- `weight` は「大きさ」
- `hp` は「姿勢が良いか」「足が開いているか」

という観点で設計されています。

特に HP は、MediaPipe の landmark を利用して「背筋がまっすぐか」「足が開いているか」を判定し、それを数値化して 100〜1000 に変換しています。

---

必要なら次に、
- 「この解説を README 形式に整える」
- 「この内容をそのまま Notion / Markdown 文書として書き直す」
- 「図だけを PNG 風に整えて説明用にする」

という形でも整えられます。
