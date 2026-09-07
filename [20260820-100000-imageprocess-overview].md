# 2026-08-20 10:00:00 — image-process 概要

## 1. ディレクトリの役割

image-process は、人物画像を入力として受け取り、人物を背景透過で切り抜き、姿勢とシルエットからゲーム用ステータスを計算する処理群です。

- [image-process/server.py](image-process/server.py)
  - FastAPI ベースの API エントリーポイント
- [image-process/person_cutout.py](image-process/person_cutout.py)
  - 画像から人物を切り抜く処理
- [image-process/stats.py](image-process/stats.py)
  - attack / weight / hp を算出するロジック
- [image-process/make_input.py](image-process/make_input.py)
  - ローカル画像を JSON 形式に変換する補助スクリプト
- [image-process/README.md](image-process/README.md)
  - API の仕様と使い方の説明

## 2. 処理の流れ

1. React から `imageData` を受け取る
2. base64 をデコードして画像に戻す
3. `rembg` で背景除去
4. 人物のアルファマスクから silhouette を取り出す
5. MediaPipe で姿勢座標を推定
6. 攻撃力 / 重さ / HP を計算
7. 人物切り抜き済み PNG を返却
8. JSON で `params` / `detail` / `swordName` を返す

## 3. 重要な関数

- `decode_image()`
  - base64 画像を PIL の RGBA 画像に変換
- `cutout_person()`
  - rembg による人物分離
- `compute_attack()`
  - シルエットの尖り具合から attack を算出
- `compute_weight()`
  - 面積比率から weight を算出
- `compute_hp()`
  - 姿勢と足開きから hp を算出

## 4. 返却する値のイメージ

```json
{
  "imageData": "<PNG base64>",
  "params": { "attack": 78, "weight": 59, "hp": 820 },
  "detail": { "attack": {...}, "weight": {...}, "hp": {...} },
  "swordName": "炎Yamadaソード"
}
```

## 5. 結論

image-process は「人物画像から剣のステータスを作る AI レイヤー」であり、Web フロントと Unity の中間に位置する重要な処理基盤です。
