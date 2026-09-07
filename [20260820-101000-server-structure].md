# 2026-08-20 10:10:00 — server.py の全体像

## 1. 役割

[image-process/server.py](image-process/server.py) は FastAPI の API サーバーです。

主な役割は次のとおりです。

- JSON で画像を受け取る
- 人物を切り抜く
- 姿勢とシルエットを解析する
- ゲーム用ステータスを計算する
- 結果を JSON で返す

## 2. 主要な定義

### CutoutRequest

```python
class CutoutRequest(BaseModel):
    imageData: str
    userName: str = ""
```

意味:
- `imageData` は必須
- `userName` は任意
- JSON のボディを Python オブジェクトに変換する

### app = FastAPI(...)

FastAPI アプリ本体。ルート定義の起点です。

## 3. ルート

### GET /health

```python
@app.get("/health")
def health():
    return {"status": "ok"}
```

- ヘルスチェック用エンドポイント
- 監視やデプロイ確認で使う

### POST /cutout

```python
def cutout(req: CutoutRequest, x_api_key: str = Header(None)):
```

- JSON body を `req` に受け取る
- ヘッダーの `X-API-Key` を `x_api_key` に受け取る
- 認証判定と画像処理を行う

## 4. 処理順序

1. API キー認証
2. `decode_image(req.imageData)`
3. `cutout_person(...)`
4. `silhouette_mask(...)`
5. `compute_stats(...)`
6. `crop_to_subject(...)`
7. PNG を base64 に変換
8. `swordName` を生成
9. JSON を返却

## 5. 返却例

```json
{
  "imageData": "<base64>",
  "width": 576,
  "height": 406,
  "bbox": { "x": 177, "y": 239, "w": 576, "h": 406 },
  "params": { "attack": 100, "weight": 9, "hp": 989 },
  "detail": { "attack": {...}, "weight": {...}, "hp": {...} },
  "swordName": "炎Yamadaソード"
}
```

## 6. まとめ

server.py は、画像処理のロジックを FastAPI の HTTP 層へ接続する役目を担っており、処理の本体は [image-process/person_cutout.py](image-process/person_cutout.py) と [image-process/stats.py](image-process/stats.py) に分離されています。
