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
import os
import random
import string

from fastapi import FastAPI, HTTPException, Header
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from rembg import new_session
import socketio

# ランダムに選ばれる剣の銘
SWORD_NAMES = [
    "炎",
    "氷",
    "雷",
    "風",
    "光",
    "闇",
    "星",
    "月",
    "紅蓮",
    "蒼穹",
    "天空",
    "大地",
    "鉄",
    "黄金",
    "白銀",
    "翠嵐",
    "烈火",
    "極光",
    "疾風",
    "破魔",
]

from person_cutout import crop_to_subject, cutout_person, decode_image
from stats import compute_stats, silhouette_mask

app = FastAPI(title="POSE_SWORD API", version="0.1.0")

# HuggingFace Spaces の「Secrets」で API_KEY を設定する
# 未設定の場合は認証なし（ローカル開発用）
_API_KEY = os.environ.get("API_KEY", "")

# ブラウザから直接呼ばれるため、Vercel の URL のみ許可する
# CORS_ORIGINS 環境変数で追加できる（カンマ区切り）
_cors_env = os.environ.get("CORS_ORIGINS", "")
_allow_origins = [o.strip() for o in _cors_env.split(",") if o.strip()] or ["*"]

app.add_middleware(
    CORSMiddleware,
    allow_origins=_allow_origins,
    allow_methods=["POST", "GET"],
    allow_headers=["*"],
)

# rembg のモデルは起動時に1回だけロードして使い回す(リクエストごとに読むと遅い)
_REMBG_SESSION = new_session("u2net")


class CutoutRequest(BaseModel):
    imageData: str
    userName: str = ""


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/cutout")
def cutout(req: CutoutRequest, x_api_key: str = Header(None)):
    # APIキーが設定されている場合は認証チェック
    if _API_KEY and x_api_key != _API_KEY:
        raise HTTPException(status_code=401, detail="Unauthorized")

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

    # 剣の名前を生成: "ユーザ名の???ソード/カリバー/刀/ブレード"
    sword_adj = random.choice(SWORD_NAMES)
    sword_suffix = random.choice(["ソード", "カリバー", "刀", "ブレード"])
    if req.userName:
        sword_name = f"{req.userName}の{sword_adj}{sword_suffix}"
    else:
        sword_name = f"{sword_adj}{sword_suffix}"

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
        "swordName": sword_name,
    }


# ---------------------------------------------------------------------------
# Socket.IO サーバー（リアルタイム通信）
# ---------------------------------------------------------------------------

sio = socketio.AsyncServer(
    async_mode="asgi",
    cors_allowed_origins=_allow_origins,
    max_http_buffer_size=5 * 1024 * 1024,  # 5 MB
)

# ルーム管理: room_id → {"host_sid": str, "client_sid": str | None}
rooms: dict[str, dict] = {}


def _generate_room_id() -> str:
    """6桁のランダムな数字IDを生成する。"""
    return "".join(random.choices(string.digits, k=6))


def _find_room_by_sid(sid: str) -> tuple[str | None, str | None]:
    """sid が所属するルームを探し (room_id, role) を返す。"""
    for room_id, info in rooms.items():
        if info["host_sid"] == sid:
            return room_id, "host"
        if info.get("client_sid") == sid:
            return room_id, "client"
    return None, None


@sio.event
async def create_room(sid: str):
    """ホストがルームを作成する。"""
    for _ in range(10):
        room_id = _generate_room_id()
        if room_id not in rooms:
            break
    else:
        await sio.emit("error", {"message": "ルーム作成に失敗しました"}, to=sid)
        return

    rooms[room_id] = {"host_sid": sid, "client_sid": None}
    await sio.enter_room(sid, room_id)
    await sio.emit("room_created", {"roomId": room_id}, to=sid)


@sio.event
async def join_room(sid: str, data: dict):
    """クライアントがルームに参加する。"""
    room_id = data.get("roomId", "")
    if room_id not in rooms:
        await sio.emit("error", {"message": "ルームが見つかりません"}, to=sid)
        return
    if rooms[room_id].get("client_sid") is not None:
        await sio.emit("error", {"message": "ルームは満員です"}, to=sid)
        return

    rooms[room_id]["client_sid"] = sid
    await sio.enter_room(sid, room_id)
    # 双方に通知
    await sio.emit("peer_joined", {"roomId": room_id}, to=rooms[room_id]["host_sid"])
    await sio.emit("room_joined", {"roomId": room_id}, to=sid)


@sio.event
async def relay(sid: str, data: dict):
    """メッセージを相手に中継する。"""
    room_id, role = _find_room_by_sid(sid)
    if room_id is None:
        return
    info = rooms[room_id]
    target = info["client_sid"] if role == "host" else info["host_sid"]
    if target:
        await sio.emit("relay", data, to=target)


@sio.event
async def leave_room(sid: str):
    """明示的にルームを退出する。"""
    await _cleanup(sid)


@sio.event
async def disconnect(sid: str):
    """切断時のクリーンアップ。"""
    await _cleanup(sid)


async def _cleanup(sid: str):
    room_id, role = _find_room_by_sid(sid)
    if room_id is None:
        return
    info = rooms[room_id]
    # 相手に LEAVE を通知
    other = info["client_sid"] if role == "host" else info["host_sid"]
    if other:
        await sio.emit("relay", {"type": "LEAVE"}, to=other)
    # ルームを削除
    del rooms[room_id]


# FastAPI + Socket.IO を統合した ASGI アプリケーション
application = socketio.ASGIApp(sio, other_asgi_app=app)
