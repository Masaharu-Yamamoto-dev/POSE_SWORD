#!/usr/bin/env python3
"""必殺技アイコン（デザイン第6案）を、Unity でそのまま使える層ごとの PNG に書き出す。

どの画像も同じ大きさで、丸の中心が画像の真ん中にある。Unity では同じ大きさの Image を
重ねるだけで位置が合う。

書き出すもの
  POSE_SWORD_Unity/Assets/UI/SkillIcons/
    ring_track.png          SPゲージの溝（全技共通）
    {技}_ring.png           SPゲージの色。Image を Filled / Radial 360 / Fill Origin: Top / Clockwise にし、
                            fillAmount に SP の割合を入れて溜める
    {技}_body.png           丸の中身と枠。キャラが枠の上へ飛び出す部分も含む（発動できる時）
    {技}_body_charge.png    溜めている途中（暗く落とした版）
    {技}_glow.png           発動できる時に後ろで光らせる
  public/skill-icons/{技}.png   全部を重ねた完成形（React の画面などで使う）

重ね順（下から）: glow → ring_track → ring → body

技の名前: spin=大回転斬 giant=巨大回転斬 attack=オレ達アタック shield=オレ達シールド
          tornado=竜巻猛突 grasp=オレ掌握斬

使い方: python3 tools/skill-icons/render_skill_icons.py [--person 人物の切り抜きPNG] [--size 512]
"""
import argparse
import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SWORD_DIR = ROOT / 'POSE_SWORD_Unity' / 'Assets' / 'Sword'
OUT_UNITY = ROOT / 'POSE_SWORD_Unity' / 'Assets' / 'UI' / 'SkillIcons'
OUT_WEB = ROOT / 'public' / 'skill-icons'

SS = 2  # 縁をなめらかにするため、2倍の大きさで描いてから縮める
WASHI = '#f5f0e6'
HILT_FILES = {'0': 'sword_handle.png', '1': 'sword_handle_1.png', '2': 'sword_handle_2.png', '3': 'sword_handle_3.png'}
# Unity 上の剣の組み方：刀身は幅1.5（下端中央が原点）、柄は幅1.435・中心が原点の0.4下
HW, HK = 1.43503, 1.5666

SKILLS = {
    'spin': dict(hilt='0', ring='#4f9d97', glow=(79, 157, 151, .7), focus=True, fan=(30, 79, 80),
                 bg=((.5, .46), [(0, '#eef0e6'), (.26, '#a9cfc9'), (.62, '#4f9894'), (1, '#1e3f40')])),
    'giant': dict(hilt='1', ring='#c8513a', glow=(200, 81, 58, .7), focus=False,
                  bg=((.5, .8), [(0, '#f3e2d2'), (.22, '#e8a27c'), (.55, '#c4442b'), (1, '#4a120c')])),
    'attack': dict(hilt='2', ring='#5f7dbb', glow=(95, 125, 187, .7), focus=False, clone=('#1f3566', '#b9c7e6'),
                   bg=((.34, .66), [(0, '#e6e8ec'), (.28, '#9fb1d6'), (.60, '#46639f'), (1, '#172648')])),
    'shield': dict(hilt='3', ring='#68a47b', glow=(104, 164, 123, .7), focus=True, clone=('#1f4a30', '#bcd9c2'),
                   bg=((.5, .48), [(0, '#e9ede0'), (.28, '#b2d1b4'), (.62, '#4f8f63'), (1, '#1a3b26')])),
    'tornado': dict(hilt=None, ring='#d6a23e', glow=(214, 162, 62, .7), focus=True, fan=(107, 71, 16),
                    bg=((.5, .5), [(0, '#f4ecd6'), (.30, '#e9c97a'), (.64, '#c8902e'), (1, '#5a3a10')])),
    'grasp': dict(hilt=None, ring='#8a72b8', glow=(138, 114, 184, .7), focus=True,
                  bg=((.5, .42), [(0, '#a58cc4'), (.32, '#6d5191'), (.68, '#33224d'), (1, '#140c1f')])),
}


def hexc(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


# ---------------------------------------------------------------- 線の部品（SVG の 0〜100 の座標。角度は真上から時計回り）

def taper_arc(c, r, a1, a2, w, n=24):
    """先細りの弧。尾（a1）で細く、頭（a2）で太さ w。頭は丸く閉じる。"""
    outer, inner = [], []
    for i in range(n + 1):
        t = i / n
        ph = math.radians(a1 + (a2 - a1) * t)
        ww = w * t ** 0.9 / 2
        outer.append((c[0] + (r + ww) * math.sin(ph), c[1] - (r + ww) * math.cos(ph)))
        inner.append((c[0] + (r - ww) * math.sin(ph), c[1] - (r - ww) * math.cos(ph)))
    hp = math.radians(a2)
    hc = (c[0] + r * math.sin(hp), c[1] - r * math.cos(hp))
    tang = (math.cos(hp), math.sin(hp)) if a2 > a1 else (-math.cos(hp), -math.sin(hp))
    o = outer[-1]
    cap = []
    for i in range(1, 8):
        a = math.pi * i / 8
        cap.append((hc[0] + (o[0] - hc[0]) * math.cos(a) + tang[0] * (w / 2) * math.sin(a),
                    hc[1] + (o[1] - hc[1]) * math.cos(a) + tang[1] * (w / 2) * math.sin(a)))
    return outer + cap + inner[::-1]


def needle(x1, y1, x2, y2, w):
    """先細りの直線。尾（x1,y1）は点、頭（x2,y2）は太さ w で丸く閉じる。"""
    dx, dy = x2 - x1, y2 - y1
    L = math.hypot(dx, dy) or 1
    ux, uy = dx / L, dy / L
    nx, ny = -uy * w / 2, ux * w / 2
    pts = [(x1, y1)]
    for i in range(9):
        a = math.pi * i / 8
        pts.append((x2 + nx * math.cos(a) + ux * (w / 2) * math.sin(a), y2 + ny * math.cos(a) + uy * (w / 2) * math.sin(a)))
    return pts


def sparkle(x, y, s, n=8):
    """辺がへこんだ4つ角の光。"""
    k = s * 0.18
    seq = [(x, y - s), (x + k, y - k), (x + s, y), (x + k, y + k), (x, y + s), (x - k, y + k), (x - s, y), (x - k, y - k), (x, y - s)]
    pts = []
    for i in range(0, 8, 2):
        p0, c, p1 = seq[i], seq[i + 1], seq[i + 2]
        for j in range(n):
            t = j / n
            pts.append(((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * c[0] + t * t * p1[0],
                        (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * c[1] + t * t * p1[1]))
    return pts


def curl(x, y, r0, turns, w, sgn, a0, n=40):
    """先細りの渦（風）。"""
    outer, inner = [], []
    for i in range(n + 1):
        t = i / n
        a = a0 + sgn * t * turns * 2 * math.pi
        r = r0 * (1 - 0.72 * t)
        ww = (w / 2) * min(1, t * 5) * (1 - t) + 0.12
        outer.append((x + (r + ww) * math.cos(a), y + (r + ww) * math.sin(a)))
        inner.append((x + (r - ww) * math.cos(a), y + (r - ww) * math.sin(a)))
    return outer + inner[::-1]


def taper_ell(ecx, ecy, rx, ry, a1, a2, w, n=24):
    """楕円に沿った先細りの帯（尾 a1 → 頭 a2）。"""
    outer, inner = [], []
    for i in range(n + 1):
        t = i / n
        a = math.radians(a1 + (a2 - a1) * t)
        ww = w * t ** 0.9 / 2
        px, py = ecx + rx * math.cos(a), ecy + ry * math.sin(a)
        nx, ny = math.cos(a) / rx, math.sin(a) / ry
        nl = math.hypot(nx, ny)
        nx, ny = nx / nl, ny / nl
        outer.append((px + nx * ww, py + ny * ww))
        inner.append((px - nx * ww, py - ny * ww))
    return outer + inner[::-1]


def crescent(c, rmid, a1, a2, w, n=16):
    """斬撃の三日月。中央が太く両端が細い。"""
    outer, inner = [], []
    for i in range(n + 1):
        t = i / n
        ph = math.radians(a1 + (a2 - a1) * t)
        ww = w * math.sin(math.pi * t)
        outer.append((c[0] + (rmid + ww) * math.sin(ph), c[1] - (rmid + ww) * math.cos(ph)))
        inner.append((c[0] + (rmid - ww * 0.35) * math.sin(ph), c[1] - (rmid - ww * 0.35) * math.cos(ph)))
    return outer + inner[::-1]


def focus_lines():
    """背景の集中線（太さ・長さ・間隔をばらした三角）。濃さ3段階に分けて返す。"""
    rnd = random.Random(20261019)
    groups = {0: [], 1: [], 2: []}
    n = 84
    for i in range(n):
        th = i * 360 / n + rnd.uniform(-1.6, 1.6)
        if rnd.random() < 0.12:
            continue
        w = rnd.choice([0.35, 0.5, 0.7, 0.9, 1.2, 1.6])
        rin = rnd.choice([22, 26, 30, 33, 36, 39, 42, 44]) + rnd.uniform(-2, 2)
        g = rnd.choices([0, 1, 2], weights=[3, 4, 2])[0]

        def P(r, a):
            a = math.radians(a)
            return (50 + r * math.sin(a), 50 - r * math.cos(a))
        groups[g].append([P(75, th - w), P(75, th + w), P(rin, th)])
    return groups


def washi_texture(size=512):
    """和紙の質感（ほぼ白。乗算で薄く重ねる）。"""
    rng = np.random.default_rng(7)

    def blurred(scale, amp):
        n = rng.normal(0, 1, (size // scale + 2, size // scale + 2))
        im = Image.fromarray(((n - n.min()) / (n.max() - n.min()) * 255).astype('uint8')).resize((size, size), Image.BICUBIC)
        return (np.asarray(im).astype(float) / 255 - 0.5) * amp
    base = 1.0 - (0.5 + blurred(64, 1.0)) * 0.05 - (0.5 + blurred(16, 1.0)) * 0.04 - (0.5 + blurred(2, 1.0)) * 0.05
    rgb = np.stack([base * 1.0, base * 0.985, base * 0.955], -1)
    img = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype('uint8'), 'RGB')
    d = ImageDraw.Draw(img, 'RGBA')
    r = random.Random(3)
    for _ in range(170):
        x, y = r.uniform(0, size), r.uniform(0, size)
        ang = r.uniform(0, math.pi)
        L = r.uniform(18, 70)
        pts = []
        for _ in range(12):
            ang += r.uniform(-0.18, 0.18)
            x += math.cos(ang) * L / 12
            y += math.sin(ang) * L / 12
            pts.append((x, y))
        d.line(pts, fill=(196, 182, 156, r.randint(40, 95)), width=1)
    return img.filter(ImageFilter.GaussianBlur(0.4))


# ---------------------------------------------------------------- 描画のしくみ

class Icon:
    def __init__(self, size):
        self.s = size * SS
        s = self.s
        self.inset = s * 0.085
        self.ew = max(2 * SS, s * 0.02)
        self.R = s / 2 - self.inset
        self.Rc = self.R - self.ew
        self.K = 50 * self.R / self.Rc
        self.mx = round(s * 0.156)          # 左右の余白（光がはみ出す分）
        self.my = round(s * 0.266)          # 上下の余白（キャラが上へ飛び出す分。丸の中心を画像の中心にそろえる）
        self.W = s + 2 * self.mx
        self.H = s + 2 * self.my
        self.cx = self.W / 2
        self.cy = self.H / 2

    def blank(self):
        return Image.new('RGBA', (self.W, self.H), (0, 0, 0, 0))

    def vb(self, x, y):
        """SVG の 0〜100 座標 → 画像のピクセル（内側の円が 0〜100）。"""
        return (self.cx + (x - 50) / 50 * self.Rc, self.cy + (y - 50) / 50 * self.Rc)

    def vbs(self, v):
        return v / 50 * self.Rc

    def circle_mask(self, r):
        m = Image.new('L', (self.W, self.H), 0)
        ImageDraw.Draw(m).ellipse((self.cx - r, self.cy - r, self.cx + r, self.cy + r), fill=255)
        return m

    def fill(self, layer, polys, color, op=1.0):
        """SVG の path の塗りと同じく、重なっても濃くならないよう一つの型にまとめて塗る。"""
        m = Image.new('L', (self.W, self.H), 0)
        d = ImageDraw.Draw(m)
        for poly in polys:
            d.polygon([self.vb(*p) for p in poly], fill=255)
        self.paint(layer, m, color, op)

    def paint(self, layer, mask, color, op=1.0):
        rgb = hexc(color) if isinstance(color, str) else color
        top = Image.new('RGBA', (self.W, self.H), rgb + (0,))
        top.putalpha(mask.point(lambda v: int(v * op)))
        layer.alpha_composite(top)

    def stroke_circle(self, layer, x, y, r, width, color, op=1.0, fill_op=None):
        m = Image.new('L', (self.W, self.H), 0)
        px, py = self.vb(x, y)
        rp, wp = self.vbs(r), self.vbs(width)
        if fill_op is not None:
            ImageDraw.Draw(m).ellipse((px - rp, py - rp, px + rp, py + rp), fill=255)
            self.paint(layer, m, color, fill_op)
            return
        ImageDraw.Draw(m).ellipse((px - rp - wp / 2, py - rp - wp / 2, px + rp + wp / 2, py + rp + wp / 2), fill=255)
        ImageDraw.Draw(m).ellipse((px - rp + wp / 2, py - rp + wp / 2, px + rp - wp / 2, py + rp - wp / 2), fill=0)
        self.paint(layer, m, color, op)

    def stroke_poly(self, layer, pts, width, color, op=1.0):
        m = Image.new('L', (self.W, self.H), 0)
        ImageDraw.Draw(m).line([self.vb(*p) for p in pts], fill=255, width=max(1, round(self.vbs(width))), joint='curve')
        self.paint(layer, m, color, op)


def radial_bg(ic, center, stops, rings=False):
    """CSS の radial-gradient(circle at …) と同じ塗り（大きさは丸の箱の遠い角まで）。"""
    box = 2 * ic.R
    x0, y0 = ic.cx - ic.R, ic.cy - ic.R
    yy, xx = np.mgrid[0:ic.H, 0:ic.W].astype(float)
    px, py = x0 + center[0] * box, y0 + center[1] * box
    rmax = max(math.hypot(px - a, py - b) for a in (x0, x0 + box) for b in (y0, y0 + box))
    t = np.sqrt((xx - px) ** 2 + (yy - py) ** 2) / rmax
    out = np.zeros((ic.H, ic.W, 3))
    st = [(p, np.array(hexc(h), float)) for p, h in stops]
    for (p0, c0), (p1, c1) in zip(st, st[1:]):
        m = (t >= p0) & (t <= p1)
        f = ((t - p0) / (p1 - p0))[..., None]
        out[m] = (c0 * (1 - f) + c1 * f)[m]
    out[t > 1] = st[-1][1]
    if rings:  # 巨大回転斬：足元から広がる細い輪（repeating-radial-gradient）
        qx, qy = x0 + 0.5 * box, y0 + 0.9 * box
        qmax = max(math.hypot(qx - a, qy - b) for a in (x0, x0 + box) for b in (y0, y0 + box))
        frac = (np.sqrt((xx - qx) ** 2 + (yy - qy) ** 2) / qmax) % 0.082
        ring = (frac >= 0.07)[..., None]
        out = np.where(ring, out * (1 - .18) + np.array([243, 226, 210]) * .18, out)
    return out


def dilate(alpha, r):
    """丸く太らせる（フチ用）。ぼかしてからしきい値で切り、縁はなめらかに残す。"""
    if r <= 0:
        return alpha
    b = np.asarray(alpha.filter(ImageFilter.GaussianBlur(r / 2)), float)
    out = np.clip((b - 3) / 9, 0, 1) * 255
    return Image.fromarray(np.maximum(out, np.asarray(alpha, float)).astype('uint8'))


def flat(alpha, color, op=1.0):
    img = Image.new('RGBA', alpha.size, hexc(color) + (0,) if isinstance(color, str) else color + (0,))
    img.putalpha(alpha.point(lambda v: int(v * op)))
    return img


def shadow(alpha, sigma, rgba):
    b = alpha.filter(ImageFilter.GaussianBlur(sigma))
    img = Image.new('RGBA', alpha.size, rgba[:3] + (0,))
    img.putalpha(b.point(lambda v: int(v * rgba[3])))
    return img


def dim(img):
    """溜めている途中：grayscale(.9) brightness(.62)。透明度はそのまま。"""
    a = np.asarray(img).astype(float)
    rgb = a[..., :3] / 255
    g = (rgb @ np.array([0.2126, 0.7152, 0.0722]))[..., None]
    rgb = rgb * 0.1 + g * 0.9
    rgb = np.clip(rgb * 0.62, 0, 1)
    a[..., :3] = rgb * 255
    return Image.fromarray(a.astype('uint8'), 'RGBA')


# ---------------------------------------------------------------- 剣（刀身＝人物、柄）の置き方

class Sword:
    def __init__(self, person_path, hilt_key):
        self.person = Image.open(person_path).convert('RGBA')
        a = self.person.split()[3].point(lambda v: 255 if v > 24 else 0)
        bb = a.getbbox() or (0, 0, self.person.width, self.person.height)
        w, h = self.person.size
        self.bh = 1.5 * h / w
        self.pb = (-0.75 + 1.5 * bb[0] / w, -self.bh + self.bh * bb[1] / h, -0.75 + 1.5 * bb[2] / w, -self.bh + self.bh * bb[3] / h)
        self.hilt = None
        self.hh = 0
        self.htop = 0
        self.guard = 0
        if hilt_key is not None:
            self.hilt = Image.open(SWORD_DIR / HILT_FILES[hilt_key]).convert('RGBA')
            hw_, hh_ = self.hilt.size
            hb = self.hilt.split()[3].point(lambda v: 255 if v > 24 else 0).getbbox()
            self.hh = hh_ / hw_ * HK
            self.htop = 0.4 - self.hh / 2
            self.guard = self.htop + self.hh * hb[1] / hh_

    def focus(self, with_hilt):
        pb = self.pb
        if with_hilt and self.hilt is not None:
            return (min(pb[0], -0.2), min(pb[1], self.guard), max(pb[2], 0.2), max(pb[3], self.guard + 0.16))
        return pb

    def local(self, with_hilt, P=420):
        X0, Y0 = -1.0, -self.bh - 0.05
        can = Image.new('RGBA', (int(2 * P), int((1.7 - Y0) * P)), (0, 0, 0, 0))
        can.alpha_composite(self.person.resize((int(1.5 * P), int(self.bh * P)), Image.LANCZOS), (int((-0.75 - X0) * P), int((-self.bh - Y0) * P)))
        if with_hilt and self.hilt is not None:
            can.alpha_composite(self.hilt.resize((int(HW * P), int(self.hh * P)), Image.LANCZOS), (int((-HW / 2 - X0) * P), int((self.htop - Y0) * P)))
        return can, (X0, Y0), P


def rotpt(x, y, th):
    c, s = math.cos(math.radians(th)), math.sin(math.radians(th))
    return (x * c - y * s, x * s + y * c)


def rotbox(b, th):
    p = [rotpt(x, y, th) for x in (b[0], b[2]) for y in (b[1], b[3])]
    return (min(q[0] for q in p), min(q[1] for q in p), max(q[0] for q in p), max(q[1] for q in p))


def place(ic, src, org, P, ox, oy, u, th):
    X0, Y0 = org
    c, s = math.cos(math.radians(th)), math.sin(math.radians(th))
    return src.transform((ic.W, ic.H), Image.AFFINE,
                         (c / u * P, s / u * P, ((-ox * c - oy * s) / u - X0) * P, -s / u * P, c / u * P, ((ox * s - oy * c) / u - Y0) * P),
                         resample=Image.BICUBIC)


# ---------------------------------------------------------------- 1つの技を描く

def render(skill, person_path, size, tex):
    sk = SKILLS[skill]
    ic = Icon(size)
    s, R = ic.s, ic.R
    sword = Sword(person_path, sk['hilt'])
    pb = sword.pb

    # 1) 丸の中身（背景＋和紙の質感）
    bg = radial_bg(ic, sk['bg'][0], sk['bg'][1], rings=(skill == 'giant'))
    t = np.asarray(tex.resize((int(2 * R), int(2 * R)), Image.BICUBIC)).astype(float) / 255
    tt = np.ones((ic.H, ic.W, 3))
    ox_, oy_ = int(ic.cx - R), int(ic.cy - R)
    tt[oy_:oy_ + t.shape[0], ox_:ox_ + t.shape[1]] = t
    bg = bg * (1 - .55 + .55 * tt)
    disc = Image.fromarray(np.clip(bg, 0, 255).astype('uint8'), 'RGB').convert('RGBA')
    disc.putalpha(ic.circle_mask(R))

    # 2) 後ろのエフェクト
    back = ic.blank()
    grasp = skill == 'grasp'
    if sk['focus']:
        col = '#cdb9e6' if grasp else WASHI
        ops = (.12, .18, .26) if grasp else (.28, .42, .6)
        for g, polys in focus_lines().items():
            ic.fill(back, polys, col, ops[g])

    specs = []
    K = ic.K
    if skill in ('spin', 'tornado'):
        sg = 1 if skill == 'spin' else -1
        with_h = sword.hilt is not None
        reach = max(math.hypot(x, y) for x in (pb[0], pb[2]) for y in (pb[1], pb[3]))
        u0 = 1.3 * R / reach
        T = (-0.25 * sg, 0.48 if skill == 'spin' else 0.54)
        for a, op in [(-24, .06), (-12, .12), (0, .2), (12, .3), (24, .42)]:
            specs.append(dict(th=a * sg, u=u0, piv=(0, 0), t=T, wh=with_h, op=op, role='ghost', color='#000000'))
        specs.append(dict(th=36 * sg, u=u0, piv=(0, 0), t=T, wh=with_h, role='main'))
        c0 = (50 + T[0] * K, 50 + T[1] * K)
        rr = 1.3 * K
        # 回った跡の扇（なめらかに薄れる）
        yy, xx = np.mgrid[0:ic.H, 0:ic.W].astype(float)
        pxc, pyc = ic.vb(*c0)
        phi = (np.degrees(np.arctan2(xx - pxc, -(yy - pyc))) + 360) % 360
        if sg > 0:
            rel = (phi - (-114)) % 360
            al = np.where(rel <= 149, rel / 149 * .36, np.where(rel < 150, (150 - rel) * .36, 0))
        else:
            rel = (phi - (-36)) % 360
            al = np.where(rel <= 150, (1 - rel / 150) * .36, 0)
        fan = np.zeros((ic.H, ic.W, 4))
        fan[..., :3] = sk['fan']
        fan[..., 3] = al * 255
        back.alpha_composite(Image.fromarray(fan.astype('uint8'), 'RGBA'))
        ic.fill(back, [taper_arc(c0, rr * .62, -60 * sg, 30 * sg, 2.6)], WASHI, .7)
        ic.fill(back, [taper_arc(c0, rr * .80, -40 * sg, 33 * sg, 3.2)], WASHI, .8)
        ic.fill(back, [taper_arc(c0, rr * 1.0, -20 * sg, 34 * sg, 3.4)], WASHI, .65)
        if skill == 'spin':
            ic.fill(back, [taper_arc(c0, rr * .45, 140, 210, 2.2)], WASHI, .55)
        ic.fill(back, [taper_arc(c0, rr * .93, -14 * sg, 35 * sg, 4.4)], sk['ring'], .94)
        ic.fill(back, [taper_arc(c0, rr * .93, 0, 35 * sg, 1.6)], WASHI, 1)
    else:
        if skill == 'giant':
            comp = [dict(th=6, fit=(2.4, 2.5), t=(0, -0.38))]
        elif skill == 'attack':
            comp = [dict(th=52, k=.55, t=(.40, -.55), role='clone', noh=True), dict(th=52, k=.55, t=(.66, .02), role='clone', noh=True),
                    dict(th=52, k=.55, t=(-.12, -.78), role='clone', noh=True), dict(th=36, fit=(1.25, 1.65), t=(-.30, .16))]
        elif skill == 'shield':
            comp = [dict(th=-14, k=.42, t=(-.66, -.34), role='clone', noh=True, ghost=(-.46, -.42)),
                    dict(th=14, k=.42, t=(.66, -.34), role='clone', noh=True, ghost=(.80, -.16)),
                    dict(th=0, fit=(1.05, 1.75), t=(0, 0)),
                    dict(th=-24, k=.44, t=(-.64, .28), role='clone', noh=True, ghost=(-.66, .16)),
                    dict(th=24, k=.44, t=(.64, .28), role='clone', noh=True, ghost=(.52, .38))]
        else:  # grasp：柄なし、紫の輪の中心で少し透かす
            comp = [dict(th=0, fit=(.72, 1.1), t=(0, .12), aura=True, op=.9)]
        lead = [c for c in comp if 'fit' in c][0]
        lb = rotbox(sword.focus(True), lead['th'])
        u0 = min(lead['fit'][0] * R / (lb[2] - lb[0]), lead['fit'][1] * R / (lb[3] - lb[1]))
        for c in comp:
            wh = not c.get('noh') and sword.hilt is not None
            fb = sword.focus(wh)
            piv = ((fb[0] + fb[2]) / 2, (fb[1] + fb[3]) / 2)
            if c.get('ghost'):
                specs.append(dict(th=c['th'], u=u0 * c['k'], piv=piv, t=c['ghost'], wh=False, op=.4, role='ghost', color='#7fae8a'))
            specs.append(dict(th=c['th'], u=u0 if 'fit' in c else u0 * c['k'], piv=piv, t=c['t'], wh=wh, op=c.get('op', 1),
                              role=c.get('role', 'main'), aura=c.get('aura')))
        if skill == 'giant':
            shards = [[(10, 57), (17, 53), (15, 61)], [(85, 47), (91, 45), (88, 51)], [(21, 33), (25, 30), (24, 35)],
                      [(78, 25), (82, 23), (80, 27)], [(88, 77), (82, 75), (84, 81)]]
            ic.fill(back, shards, '#3b1209', .84)
        elif skill == 'attack':
            fd = (math.sin(math.radians(52)), -math.cos(math.radians(52)))
            pd = (-fd[1], fd[0])
            # ごく薄い流線（列は等間隔、長さと隙間はばらす）
            seed = [7]

            def rand():
                seed[0] = (seed[0] * 16807) % 2147483647
                return (seed[0] - 1) / 2147483646
            fl1, fl2 = [], []
            lane = -48.4
            while lane <= 48.41:
                half = math.sqrt(max(0, 2500 - lane * lane))
                pos = -half + rand() * 6
                while pos < half:
                    ln, gap, wl, faint = 8 + rand() * 14, 5 + rand() * 9, 0.5 + rand() * 0.6, rand() < 0.5
                    ax, ay = 50 + pd[0] * lane + fd[0] * pos, 50 + pd[1] * lane + fd[1] * pos
                    (fl1 if faint else fl2).append(needle(ax, ay, ax + fd[0] * ln, ay + fd[1] * ln, wl))
                    pos += ln + gap
                lane += 4.4
            ic.fill(back, fl1, WASHI, .12)
            ic.fill(back, fl2, WASHI, .18)
            # 少し濃い流線15本 [横のずれ, 長さ, 太さ, 前後のずれ, 濃さ]
            SL = [[-46.2, 28, 0.9, 16.7, 0.46], [-39.6, 28, 2.0, -20.5, 0.3], [-33.0, 22, 2.0, 14.5, 0.38], [-26.4, 34, 0.9, -20.5, 0.46],
                  [-19.8, 34, 0.9, -11.6, 0.3], [-13.2, 22, 2.0, -11.9, 0.38], [-6.6, 34, 2.0, -3.7, 0.22], [0.0, 40, 2.0, 19.7, 0.46],
                  [6.6, 46, 1.6, 1.9, 0.3], [13.2, 34, 1.2, -9.7, 0.38], [19.8, 22, 2.0, 13.7, 0.46], [26.4, 40, 0.9, -13.1, 0.3],
                  [33.0, 46, 2.0, -1.9, 0.3], [39.6, 46, 0.9, 11.5, 0.22], [46.2, 22, 1.2, -8.5, 0.46]]
            op_map = {.22: .34, .3: .44, .38: .54, .46: .66}
            by_op = {}
            for q in SL:
                mx_, my_ = 50 + pd[0] * q[0] + fd[0] * q[3], 50 + pd[1] * q[0] + fd[1] * q[3]
                by_op.setdefault(op_map[q[4]], []).append(needle(mx_ - fd[0] * q[1] / 2, my_ - fd[1] * q[1] / 2, mx_ + fd[0] * q[1] / 2, my_ + fd[1] * q[1] / 2, q[2]))
            for op, polys in by_op.items():
                ic.fill(back, polys, WASHI, op)
            tails, side = [], []
            for c in comp:
                if c.get('role') != 'clone':
                    continue
                C = (50 + c['t'][0] * K, 50 + c['t'][1] * K)
                tails.append(needle(C[0] - fd[0] * .62 * K, C[1] - fd[1] * .62 * K, C[0] - fd[0] * .10 * K, C[1] - fd[1] * .10 * K, 2.6))
                for sd in (-1, 1):
                    ox2, oy2 = pd[0] * .10 * K * sd, pd[1] * .10 * K * sd
                    side.append(needle(C[0] - fd[0] * .46 * K + ox2, C[1] - fd[1] * .46 * K + oy2, C[0] - fd[0] * .14 * K + ox2, C[1] - fd[1] * .14 * K + oy2, 1.6))
            ic.fill(back, tails, WASHI, .7)
            ic.fill(back, side, WASHI, .5)
        elif skill == 'shield':
            ic.stroke_poly(back, [(50 + 44 * math.cos(math.radians(a)), 54 + 18 * math.sin(math.radians(a))) for a in np.linspace(180, 360, 60)], 1, WASHI, .28)
            ic.fill(back, [taper_ell(50, 54, 44, 18, 250, 218, 2.6), taper_ell(50, 54, 44, 18, 354, 322, 2.6)], WASHI, .63)
        elif grasp:
            ic.stroke_circle(back, 50, 50, 18.91, 1, '#c3a8e8', .25)
            ic.stroke_circle(back, 50, 50, 27.32, 1, '#c3a8e8', .4)
            ic.stroke_circle(back, 50, 50, 42.02, 0, '#c3a8e8', fill_op=.06)
            ic.stroke_circle(back, 50, 50, 42.02, 7, '#c3a8e8', .3)
            ic.stroke_circle(back, 50, 50, 42.02, 2.2, '#d9c6f2', .95)
    back.putalpha(ImageChopsMultiply(back.split()[3], ic.circle_mask(ic.Rc)))

    # 3) キャラ（自分・分身・残像）
    ch = ic.blank()
    full_local = sword.local(True)
    bare_local = sword.local(False)
    ol = max(1, s * 0.011)
    glow_px = max(2 * SS, s * 0.028)
    for sp in specs:
        src, org, P = full_local if sp['wh'] else bare_local
        prx, pry = rotpt(sp['piv'][0], sp['piv'][1], sp['th'])
        ox = ic.cx + sp['t'][0] * R - sp['u'] * prx
        oy = ic.cy + sp['t'][1] * R - sp['u'] * pry
        placed = place(ic, src, org, P, ox, oy, sp['u'], sp['th'])
        a = placed.split()[3]
        if sp['role'] == 'ghost':
            inst = flat(a, sp['color'])
        elif sp['role'] == 'clone':
            body_c, line_c = sk['clone']
            inst = flat(dilate(a, ol), line_c)
            inst.alpha_composite(flat(a, body_c))
        else:
            outl = dilate(a, ol)
            inst = shadow(outl, glow_px / 2, sk['glow'])
            inst.alpha_composite(flat(outl, '#f3eee3'))
            inst.alpha_composite(flat(a, '#000000'))
            if sp.get('aura'):
                aura = shadow(inst.split()[3], s * 0.03 / 2, (138, 114, 184, .8))
                aura.alpha_composite(inst)
                inst = aura
        if sp.get('op', 1) < 1:
            inst.putalpha(inst.split()[3].point(lambda v, o=sp['op']: int(v * o)))
        ch.alpha_composite(inst)
    clip = ic.circle_mask(ic.Rc)
    if skill not in ('tornado', 'grasp'):   # 上半分は枠を突き破って外へ出せる
        ImageDraw.Draw(clip).rectangle((ic.cx - s * 0.62, ic.cy - s / 2 - R * 0.62, ic.cx + s * 0.62, ic.cy), fill=255)
    ch.putalpha(ImageChopsMultiply(ch.split()[3], clip))

    # 4) 前のエフェクト
    front = ic.blank()
    if skill == 'spin':
        c0 = (50 - 0.25 * K, 50 + 0.48 * K)
        rr = 1.3 * K
        ic.fill(front, [crescent(c0, rr * .82, 50, 78, 2.4)], WASHI, .5)
        ic.fill(front, [crescent(c0, rr * .93, 42, 80, 5)], WASHI, .95)
        ic.fill(front, [sparkle(50 + 41 * math.sin(math.radians(a)), 50 - 41 * math.cos(math.radians(a)), sz) for a, sz in [(122, 3.4), (142, 5.2), (162, 3.4)]], WASHI)
    elif skill == 'tornado':
        A = lambda ang: (50 + 40 * math.sin(math.radians(ang)), 50 - 40 * math.cos(math.radians(ang)))
        ic.fill(front, [curl(*A(242), 5.6, 1.15, 2.4, 1, math.radians(200))], WASHI, .92)
        ic.fill(front, [curl(*A(202), 4.4, 1.1, 2.0, -1, math.radians(-20))], WASHI, .86)
        ic.fill(front, [sparkle(*A(182), 3.4)], WASHI)
        ic.fill(front, [sparkle(*A(222), 4.0)], '#cfdcf2')
        ic.fill(front, [sparkle(*A(262), 2.8)], '#e2efc8')
    elif skill == 'attack':
        ic.fill(front, [sparkle(71, 85, 5), sparkle(85, 75, 3.2), sparkle(17, 28, 4)], WASHI)
    elif skill == 'shield':
        ic.stroke_poly(front, [(50 + 44 * math.cos(math.radians(a)), 54 + 18 * math.sin(math.radians(a))) for a in np.linspace(0, 180, 60)], 1, WASHI, .28)
        ic.fill(front, [taper_ell(50, 54, 44, 18, 172, 140, 2.6), taper_ell(50, 54, 44, 18, 72, 40, 2.6)], WASHI, .63)
    elif grasp:
        chev = [(-6, -5), (0, 1), (6, -5), (6, -0.6), (0, 5.6), (-6, -0.6)]
        polys = []
        for (px_, py_, ang) in [(85.51, 29.5, 60), (50, 91, 180), (14.49, 29.5, 300)]:
            polys.append([(px_ + rotpt(x, y, ang)[0], py_ + rotpt(x, y, ang)[1]) for x, y in chev])
        ic.fill(front, polys, '#efe6f7')
        enemy = [(-3.6, -11.5), (3.6, -11.5), (3, 5), (5.2, 5), (5.2, 6.8), (1, 6.8), (1, 10.4), (-1, 10.4), (-1, 6.8), (-5.2, 6.8), (-5.2, 5), (-3, 5)]
        for (ex, ey, rot_) in [(50, 15.33, 0), (80.02, 67.34, 25), (19.98, 67.34, -25)]:
            ic.stroke_circle(front, ex, ey, 10.5, 1, '#c3a8e8', .7)
            body = [(ex + rotpt(x * .6, y * .6, rot_)[0], ey + rotpt(x * .6, y * .6, rot_)[1]) for x, y in enemy]
            hx_, hy_ = ex + rotpt(0, -15 * .6, rot_)[0], ey + rotpt(0, -15 * .6, rot_)[1]
            head = [(hx_ + 3.3 * .6 * math.cos(a), hy_ + 3.3 * .6 * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 28)]
            ic.fill(front, [body, head], '#2a2238')
            ic.stroke_poly(front, body + [body[0]], 1.08, '#d9c6f2')
            ic.stroke_poly(front, head + [head[0]], 1.08, '#d9c6f2')
    front.putalpha(ImageChopsMultiply(front.split()[3], ic.circle_mask(ic.Rc)))

    # 5) 枠（墨の輪と内側の細い光）
    frame = ic.blank()
    m = ic.circle_mask(R)
    ImageDraw.Draw(m).ellipse((ic.cx - ic.Rc, ic.cy - ic.Rc, ic.cx + ic.Rc, ic.cy + ic.Rc), fill=0)
    ic.paint(frame, m, '#171412')
    hl = max(1 * SS, s * 0.008)
    m = ic.circle_mask(ic.Rc)
    ImageDraw.Draw(m).ellipse((ic.cx - ic.Rc + hl, ic.cy - ic.Rc + hl, ic.cx + ic.Rc - hl, ic.cy + ic.Rc - hl), fill=0)
    ic.paint(frame, m, (239, 233, 220), .35)

    def body_layers(charge):
        dimmed = ic.blank()
        dimmed.alpha_composite(disc)
        dimmed.alpha_composite(back)
        tmp = ic.blank()
        tmp.alpha_composite(ch)
        if charge:
            dimmed = dim(dimmed)
            tmp = dim(tmp)
            fr = dim(front)
        else:
            fr = front
        out = ic.blank()
        out.alpha_composite(dimmed)
        out.alpha_composite(frame)
        out.alpha_composite(tmp)
        out.alpha_composite(fr)
        return out

    body = body_layers(False)
    body_charge = body_layers(True)

    # 6) SPゲージ（色）と溝、光
    ring = ic.blank()
    m = ic.circle_mask(s / 2)
    ImageDraw.Draw(m).ellipse((ic.cx - R + 2 * SS, ic.cy - R + 2 * SS, ic.cx + R - 2 * SS, ic.cy + R - 2 * SS), fill=0)
    ic.paint(ring, m, sk['ring'])
    # 溝は墨のまま、外と内の縁に和紙色の細い線を引いて彫った溝に見せる。
    # 墨の縁だけだと対戦画面の暗い背景に溶けて、溜めている途中のゲージの輪郭が見えなくなる
    track = ic.blank()
    ic.paint(track, m, (23, 20, 18), .55)
    ow = max(1.5 * SS, s * 0.01)
    m2 = ic.circle_mask(s / 2 + ow)
    ImageDraw.Draw(m2).ellipse((ic.cx - s / 2, ic.cy - s / 2, ic.cx + s / 2, ic.cy + s / 2), fill=0)
    ic.paint(track, m2, (239, 233, 220), .8)
    ri = R - 2 * SS
    m3 = ic.circle_mask(ri)
    ImageDraw.Draw(m3).ellipse((ic.cx - ri + hl, ic.cy - ri + hl, ic.cx + ri - hl, ic.cy + ri - hl), fill=0)
    ic.paint(track, m3, (239, 233, 220), .5)
    glow = shadow(ic.circle_mask(s / 2 + s * 0.02), s * 0.12 / 2, sk['glow'])
    outside = ic.circle_mask(s / 2).point(lambda v: 255 - v)
    glow.putalpha(ImageChopsMultiply(glow.split()[3], outside))

    def shrink(img):
        return img.resize((img.width // SS, img.height // SS), Image.LANCZOS)
    return {k: shrink(v) for k, v in dict(body=body, body_charge=body_charge, ring=ring, track=track, glow=glow).items()}


def ImageChopsMultiply(a, b):
    return Image.fromarray((np.asarray(a, float) * np.asarray(b, float) / 255).astype('uint8'))


def compose(layers, pct=1.0, ready=True):
    """重ねた見た目（確認用・React 用）。pct は SP の割合。"""
    out = Image.new('RGBA', layers['body'].size, (0, 0, 0, 0))
    if ready:
        out.alpha_composite(layers['glow'])
    out.alpha_composite(layers['track'])
    ring = layers['ring'].copy()
    if pct < 1:
        w, h = ring.size
        yy, xx = np.mgrid[0:h, 0:w].astype(float)
        phi = (np.degrees(np.arctan2(xx - w / 2, -(yy - h / 2))) + 360) % 360
        keep = (phi <= pct * 360).astype(float)
        a = np.asarray(ring.split()[3], float) * keep
        ring.putalpha(Image.fromarray(a.astype('uint8')))
    out.alpha_composite(ring)
    out.alpha_composite(layers['body'] if ready else layers['body_charge'])
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--person', default=str(SWORD_DIR / 'sampleA.png'), help='刀身にする人物の切り抜きPNG（既定はサンプルA）')
    ap.add_argument('--size', type=int, default=512, help='丸（SPゲージの外側）の直径。画像はこれに余白を足した大きさになる')
    ap.add_argument('--skills', default=','.join(SKILLS), help='書き出す技（カンマ区切り）')
    args = ap.parse_args()
    OUT_UNITY.mkdir(parents=True, exist_ok=True)
    OUT_WEB.mkdir(parents=True, exist_ok=True)
    tex = washi_texture()
    track_saved = False
    for skill in args.skills.split(','):
        layers = render(skill, args.person, args.size, tex)
        layers['body'].save(OUT_UNITY / f'{skill}_body.png')
        layers['body_charge'].save(OUT_UNITY / f'{skill}_body_charge.png')
        layers['ring'].save(OUT_UNITY / f'{skill}_ring.png')
        layers['glow'].save(OUT_UNITY / f'{skill}_glow.png')
        if not track_saved:
            layers['track'].save(OUT_UNITY / 'ring_track.png')
            track_saved = True
        compose(layers).save(OUT_WEB / f'{skill}.png')
        print(f'{skill}: {layers["body"].size[0]}x{layers["body"].size[1]}')


if __name__ == '__main__':
    main()
