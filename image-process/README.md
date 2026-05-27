---
title: POSE SWORD API
emoji: ⚔️
colorFrom: blue
colorTo: red
sdk: docker
app_port: 7860
pinned: false
---

# POSE_SWORD — 人物切り抜き + ステータス生成

JSON で受け取った base64 画像から人物を AI（rembg）で自動検出して背景透過 PNG に切り抜き、
さらに姿勢からゲーム用ステータス（attack / weight / hp）を算出して JSON で返す。

- **CLI** … `person_cutout.py`（切り抜きだけ）
- **API サーバ** … `server.py`（切り抜き + ステータス、React 等から呼ぶ用）

## ステータスの基準

| ステータス | 範囲 | 基準 | 計算元 |
| --- | --- | --- | --- |
| attack | 1〜100 | シルエットの**尖り具合**（手足を広げた尖ったポーズほど高い） | rembg のマスク（skimage solidity） |
| weight | 1〜100 | シルエットの**面積**（画像に占める割合が大きいほど高い） | rembg のマスク |
| hp | 100〜1000 | **背筋がまっすぐ** + **足が開いている**ほど高い | MediaPipe Pose の関節座標 |

しきい値は `stats.py` 冒頭の定数で調整できる。

## 入力 JSON

```json
{ "imageData": "iVBORw0KGgoAAAANSUhEUgAA..." }
```

`imageData` は base64 文字列。`data:image/png;base64,` の接頭辞は付いていても無くても良い。

## セットアップ

```bash
python3.14 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

初回実行時に rembg（u2net, 約 170MB）と MediaPipe のポーズモデル（数 MB, `~/.pose_sword/`）を自動ダウンロードする（要ネット接続）。

## API サーバ（React 連携）

```bash
source .venv/bin/activate
uvicorn server:app --reload --port 8000
# ドキュメント: http://localhost:8000/docs
```

リクエスト: `POST /cutout`
```json
{ "imageData": "<base64 画像>" }
```

レスポンス:
```json
{
  "imageData": "<透過PNG base64>",
  "width": 576, "height": 406,
  "bbox": { "x": 177, "y": 239, "w": 576, "h": 406 },
  "params": { "attack": 100, "weight": 9, "hp": 989 },
  "detail": { "...": "各値の内訳(デバッグ用)" }
}
```

React 側の呼び出し例:
```js
async function analyze(base64) {
  const res = await fetch("http://localhost:8000/cutout", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ imageData: base64 }),
  });
  if (!res.ok) throw new Error(await res.text());
  const data = await res.json();
  // data.params.attack / weight / hp を使う
  // data.imageData を <img src={`data:image/png;base64,${data.imageData}`}/> で表示
  return data;
}
```

> 本番では `server.py` の CORS `allow_origins=["*"]` をフロントの URL に絞ること。

## CLI（切り抜きだけ使う）

```bash
# ファイルから読んで PNG に保存
python person_cutout.py input.json -o out.png

# 標準入力から
cat input.json | python person_cutout.py - -o out.png

# 結果を base64 入り JSON で返す（標準出力）
python person_cutout.py input.json --json-out > result.json
```

## 主なオプション

| オプション | 説明 |
| --- | --- |
| `-o, --output` | 出力 PNG パス（既定 `output.png`） |
| `--json-key` | base64 が入っているキー名（既定 `imageData`） |
| `--json-out` | ファイルではなく base64 入り JSON を標準出力に返す |
| `--all-subjects` | 主被写体だけでなく検出した全領域を残す |
| `--no-crop` | 被写体で切り抜かず元の画像サイズを維持 |
| `--alpha-matting` | 輪郭をより滑らかにする（低速） |
