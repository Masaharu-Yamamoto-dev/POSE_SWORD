"""POSE_SWORD API サーバ。

React 等から JSON で画像(base64)を受け取り、人物を切り抜いて
ゲーム用ステータス(attack / weight / hp)を計算し、JSON で返す。

起動:
    uvicorn server:app --reload --port 8000
ドキュメント(自動生成):
    http://localhost:8000/docs
"""

import base64
import io

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from rembg import new_session

from person_cutout import crop_to_subject, cutout_person, decode_image
from stats import compute_stats, silhouette_mask

app = FastAPI(title="POSE_SWORD API", version="0.1.0")

# ブラウザ(React)から別オリジンで叩けるように CORS を許可
# 本番では allow_origins をフロントの URL に絞ること
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["POST", "GET"],
    allow_headers=["*"],
)

# rembg のモデルは起動時に1回だけロードして使い回す(リクエストごとに読むと遅い)
_REMBG_SESSION = new_session("u2net")


class CutoutRequest(BaseModel):
    imageData: str


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/cutout")
def cutout(req: CutoutRequest):
    # 1. base64 をデコード(不正なら 400)
    try:
        original = decode_image(req.imageData)  # RGBA(元画像)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    # 2. 切り抜き(マスク計算用に元サイズのまま取得)
    full = cutout_person(original, largest_only=True, crop=False, session=_REMBG_SESSION)
    mask = silhouette_mask(full)
    bbox = full.getchannel("A").getbbox()

    # 3. ステータス算出(姿勢推定は元画像で実施)
    stats = compute_stats(original.convert("RGB"), mask)

    # 4. 出力用に被写体で切り抜いた透過 PNG を base64 化
    out = crop_to_subject(full)
    buf = io.BytesIO()
    out.save(buf, format="PNG")
    out_b64 = base64.b64encode(buf.getvalue()).decode("ascii")

    return {
        "imageData": out_b64,
        "width": out.width,
        "height": out.height,
        "bbox": (
            {"x": bbox[0], "y": bbox[1], "w": bbox[2] - bbox[0], "h": bbox[3] - bbox[1]}
            if bbox
            else None
        ),
        "params": stats["params"],   # {"attack":.., "weight":.., "hp":..}
        "detail": stats["detail"],   # 各値の内訳(デバッグ用)
    }
