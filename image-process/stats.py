"""切り抜き結果と姿勢から、ゲーム用ステータス(attack / weight / hp)を算出する。

基準:
  - attack(1〜100)  : シルエットの「尖り具合」。skimage の solidity(充実度)から計算。
                       手足を広げた尖ったポーズほど solidity が低く、攻撃力が高くなる。
  - weight(1〜100)  : シルエットの「面積」(画像に占める割合)。大きいほど高い。
  - hp(100〜1000)   : 「背筋がまっすぐ」+「足が開いている」。MediaPipe Pose の関節座標で判定。

正規化のしきい値は下の定数で調整できる(被写体の写り方に合わせてチューニングする)。
"""

from __future__ import annotations

import math
import urllib.request
from pathlib import Path

import numpy as np
from PIL import Image

# MediaPipe Tasks 用のポーズモデル(lite, 数MB)。初回に自動ダウンロードする。
POSE_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/pose_landmarker/"
    "pose_landmarker_lite/float16/latest/pose_landmarker_lite.task"
)
POSE_MODEL_PATH = Path.home() / ".pose_sword" / "pose_landmarker_lite.task"

# ----- 正規化の基準(チューニング用) -----
ATTACK_SPIKINESS_MAX = 0.45   # 尖り具合(=1-solidity)がこれ以上で攻撃力 100
WEIGHT_AREA_MIN = 0.04        # 面積比がこれ以下で体格 1
WEIGHT_AREA_MAX = 0.45        # 面積比がこれ以上で体格 100
HP_SPINE_WEIGHT = 0.5         # HP に占める「背筋まっすぐ」の比率
HP_SPREAD_WEIGHT = 0.5        # HP に占める「足開き」の比率
HP_SPREAD_MAX = 1.4           # 足首間距離 ÷ 肩幅 がこれ以上で開き満点

# MediaPipe Pose の関節インデックス(33点)
L_SHOULDER, R_SHOULDER = 11, 12
L_HIP, R_HIP = 23, 24
L_ANKLE, R_ANKLE = 27, 28


def _clamp01(x: float) -> float:
    return max(0.0, min(1.0, x))


def silhouette_mask(rgba: Image.Image) -> np.ndarray:
    """RGBA 画像のアルファから、人物シルエットの bool マスクを得る。"""
    return np.array(rgba.getchannel("A")) > 0


def compute_attack(mask: np.ndarray) -> dict:
    """尖り具合(1 - solidity)から攻撃力を計算する。"""
    from skimage.morphology import convex_hull_image

    area = int(mask.sum())
    if area == 0:
        return {"value": 1, "spikiness": 0.0, "solidity": 1.0}
    hull_area = int(convex_hull_image(mask).sum())
    solidity = area / hull_area if hull_area else 1.0
    spikiness = 1.0 - solidity
    norm = _clamp01(spikiness / ATTACK_SPIKINESS_MAX)
    return {
        "value": int(round(1 + norm * 99)),
        "spikiness": round(spikiness, 4),
        "solidity": round(solidity, 4),
    }


def compute_weight(mask: np.ndarray) -> dict:
    """シルエット面積(画像に占める割合)から体格を計算する。"""
    h, w = mask.shape
    area_ratio = float(mask.sum()) / float(w * h)
    norm = _clamp01((area_ratio - WEIGHT_AREA_MIN) / (WEIGHT_AREA_MAX - WEIGHT_AREA_MIN))
    return {"value": int(round(1 + norm * 99)), "areaRatio": round(area_ratio, 4)}


def compute_hp(landmarks, width: int, height: int) -> dict:
    """背筋のまっすぐさ + 足の開きから HP を計算する。

    landmarks は MediaPipe の正規化座標(x,y が 0〜1)。ピクセルに直して角度・距離を測る。
    """
    def px(i):
        lm = landmarks[i]
        return np.array([lm.x * width, lm.y * height])

    shoulder_mid = (px(L_SHOULDER) + px(R_SHOULDER)) / 2
    hip_mid = (px(L_HIP) + px(R_HIP)) / 2
    shoulder_w = abs(px(L_SHOULDER)[0] - px(R_SHOULDER)[0]) + 1e-6

    # 背筋: 肩の中点→腰の中点ベクトルが垂直に近いほどまっすぐ
    v = hip_mid - shoulder_mid
    angle_from_vertical = math.atan2(abs(v[0]), abs(v[1]) + 1e-6)  # 0 = 完全に垂直
    uprightness = _clamp01(1 - angle_from_vertical / (math.pi / 2))

    # 足開き: 足首どうしの距離 ÷ 肩幅
    ankle_dist = abs(px(L_ANKLE)[0] - px(R_ANKLE)[0])
    spread = _clamp01((ankle_dist / shoulder_w) / HP_SPREAD_MAX)

    score = HP_SPINE_WEIGHT * uprightness + HP_SPREAD_WEIGHT * spread
    return {
        "value": int(round(100 + score * 900)),
        "uprightness": round(uprightness, 3),
        "legSpread": round(spread, 3),
        "poseDetected": True,
    }


_LANDMARKER = None  # 起動後に1回だけ生成して使い回す


def _ensure_pose_model() -> str:
    """ポーズモデルが無ければダウンロードし、ローカルパスを返す。"""
    if not POSE_MODEL_PATH.exists():
        POSE_MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve(POSE_MODEL_URL, POSE_MODEL_PATH)
    return str(POSE_MODEL_PATH)


def _get_landmarker():
    """MediaPipe Tasks の PoseLandmarker を生成(初回のみ)して返す。"""
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


def _run_pose(rgb: Image.Image):
    """MediaPipe Pose を実行し、関節リスト(33点)を返す。検出できなければ None。"""
    import mediapipe as mp

    arr = np.ascontiguousarray(np.array(rgb.convert("RGB")))
    mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=arr)
    result = _get_landmarker().detect(mp_image)
    if not result.pose_landmarks:
        return None
    return result.pose_landmarks[0]  # 先頭の人物の 33 関節


def compute_stats(original_rgb: Image.Image, mask: np.ndarray) -> dict:
    """全ステータスをまとめて計算して返す。

    original_rgb : 元画像(姿勢推定はこちらで行う)
    mask         : 切り抜きシルエットの bool マスク(元画像と同サイズ)
    """
    attack = compute_attack(mask)
    weight = compute_weight(mask)

    landmarks = _run_pose(original_rgb)
    if landmarks is None:
        # 姿勢が取れなければ HP は最小(100)。フロントで poseDetected を見て扱える
        hp = {"value": 100, "uprightness": None, "legSpread": None, "poseDetected": False}
    else:
        h, w = mask.shape
        hp = compute_hp(landmarks, w, h)

    return {
        "params": {"attack": attack["value"], "weight": weight["value"], "hp": hp["value"]},
        "detail": {"attack": attack, "weight": weight, "hp": hp},
    }
