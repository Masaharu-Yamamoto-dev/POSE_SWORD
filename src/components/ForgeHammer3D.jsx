import { useEffect, useRef } from 'react';
import { prefersReducedMotion } from './swordForge.js';
import './ForgeHammer3D.css';

// ==========================================
// 錬成中の槌（3D）：左手前の地面に撮った写真を寝かせ、右奥から槌で叩き続ける
// ==========================================
// CSS の3D（perspective と preserve-3d）で組む。槌は板6枚の箱を2つ（柄と頭）、写真と影と輪は地面の板の上に置く。
// 毎フレームの動きは requestAnimationFrame で要素の transform を直接書き換える（React の再描画を挟まない）。
// 寸法はすべて 540×520 の舞台の上の px。表示する大きさは CSS の --k で丸ごと縮める

// 動き：0.5秒に1回。地面と平行（手前向き）を 0° として 120° まで振りかぶり、少し溜めてから一気に振り下ろす
const SWING = { len: 0.5, up: 120, rise: 0.18, hang: 0.1, fall: 0.04 };
// 見る向き：少し上から見下ろし、写真が左手前・槌が右奥に来るよう場面を回す
const VIEW = { tilt: 10, yaw: -40, scale: 0.8 };
const STARS = 10;

// 槌の寸法。支点（柄の端）は写真の奥・上。頭は、打った時に写真の真ん中より少し手前へ、平らな面で当たるようにする
const H = 150, D = 190, HEAD_H = 82, HIT_Z = 10;
const L = Math.hypot(H - HEAD_H / 2, D + HIT_Z);
const HIT_ANGLE = -Math.atan2(H - HEAD_H / 2, D + HIT_Z) * 180 / Math.PI;

// 星（朱、濃い朱の縁）と、小さなきらめき（4つ角）
const STAR_SVG = '<svg viewBox="-12 -12 24 24"><path d="M0 -11 L3.2 -3.6 L11 -3.4 L4.9 1.6 L7 9.4 L0 4.9 L-7 9.4 L-4.9 1.6 L-11 -3.4 L-3.2 -3.6 Z" fill="#c83a2c" stroke="#7a1912" stroke-width="1.6" stroke-linejoin="round"/></svg>';
const SPARK_SVG = '<svg viewBox="-10 -10 20 20"><path d="M0 -10 Q1.4 -1.4 10 0 Q1.4 1.4 0 10 Q-1.4 1.4 -10 0 Q-1.4 -1.4 0 -10 Z" fill="#e0644f" stroke="#8f2219" stroke-width="1"/></svg>';
// 打った所に一瞬出す、放射状の短い線（朱）
const POP_SVG = '<svg viewBox="-60 -60 120 120"><g stroke="#c82a1c" stroke-width="4" stroke-linecap="round">'
  + Array.from({ length: 10 }, (_, i) => {
    const a = i / 10 * Math.PI * 2 + 0.2;
    const r0 = 30 + (i % 2) * 6, r1 = 52 - (i % 3) * 4;
    return `<line x1="${(Math.cos(a) * r0).toFixed(1)}" y1="${(Math.sin(a) * r0).toFixed(1)}" x2="${(Math.cos(a) * r1).toFixed(1)}" y2="${(Math.sin(a) * r1).toFixed(1)}"/>`;
  }).join('')
  + '</g></svg>';

const div = (cls, style, parent) => {
  const e = document.createElement('div');
  if (cls) e.className = cls;
  if (style) Object.assign(e.style, style);
  if (parent) parent.appendChild(e);
  return e;
};

// 箱（6面）。面ごとに明るさを変えて立体に見せる
function box(parent, w, h, d, base, z) {
  const g = div('', { transform: `translateZ(${z}px)` }, parent);
  const shade = k => `color-mix(in srgb, ${base} ${Math.round(k * 100)}%, #000)`;
  [
    [w, h, `translateZ(${d / 2}px)`, 1],
    [w, h, `rotateY(180deg) translateZ(${d / 2}px)`, 0.7],
    [d, h, `rotateY(90deg) translateZ(${w / 2}px)`, 0.82],
    [d, h, `rotateY(-90deg) translateZ(${w / 2}px)`, 0.62],
    [w, d, `rotateX(90deg) translateZ(${h / 2}px)`, 0.92],
    [w, d, `rotateX(-90deg) translateZ(${h / 2}px)`, 0.55],
  ].forEach(([fw, fh, tf, k]) => div('forge3d__face', {
    width: `${fw}px`, height: `${fh}px`, marginLeft: `${-fw / 2}px`, marginTop: `${-fh / 2}px`, transform: tf, background: k === 1 ? base : shade(k),
  }, g));
  return g;
}

// 振り上げた角度（打った所から）。振り上げは減速しながら、振り下ろしは加速しながら
function raiseAt(u) {
  const { rise, hang, fall, up } = SWING;
  const top = up - HIT_ANGLE;
  if (u < rise) return top * (1 - Math.pow(1 - u / rise, 3));
  if (u < rise + hang) return top;
  if (u < rise + hang + fall) return top * (1 - Math.pow((u - rise - hang) / fall, 3));
  return 0;
}

export default function ForgeHammer3D({ imageSrc, aspect = 4 / 3 }) {
  const worldRef = useRef(null);

  useEffect(() => {
    const world = worldRef.current;
    if (!world) return;
    world.textContent = '';
    // 地面・写真・影・輪
    const floor = div('forge3d__floor', null, world);
    const wide = aspect >= 1;
    const pw = wide ? 168 : 112, ph = Math.round(pw / aspect);
    const photo = div('forge3d__photo', { width: `${pw}px`, height: `${ph}px`, marginLeft: `${-pw / 2}px`, marginTop: `${-ph / 2}px` }, floor);
    if (imageSrc) {
      const img = document.createElement('img');
      img.src = imageSrc;
      img.alt = '';
      photo.appendChild(img);
    }
    const shadow = div('forge3d__shadow', null, floor);
    const ring = div('forge3d__ring', null, floor);
    const pop = div('forge3d__pop', null, world);
    pop.innerHTML = POP_SVG;
    // 槌（支点で回す）：柄と頭
    const arm = div('', null, world);
    box(arm, 11, 11, L - 20, '#6a5341', (L - 20) / 2);
    const head = div('', { transform: `translateZ(${L}px) rotateX(${-HIT_ANGLE}deg)` }, arm);
    box(head, 50, HEAD_H, 46, '#2b2622', 0);

    const stars = [];
    let seed = 7;
    const rnd = () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
    let clock = 0, prev = null, lastHit = -1, shakeT = 9, ringT = 9, popT = 9, raf = 0;
    const face = `rotateY(${-VIEW.yaw}deg) rotateX(${VIEW.tilt}deg)`; // こちらを向かせる（場面の回転の逆）
    const center = Math.sin(VIEW.yaw * Math.PI / 180) * D / 2 * VIEW.scale;

    const frame = dt => {
      const n = Math.floor(clock / SWING.len), u = clock - n * SWING.len;
      const a = HIT_ANGLE + raiseAt(u);
      const sinceHit = u - (SWING.rise + SWING.hang + SWING.fall);
      // CSS の rotateX は、正の向きで柄の先（手前向き）が上へ起きる
      arm.style.transform = `translate3d(0, ${-H}px, ${-D}px) rotateX(${a.toFixed(2)}deg)`;
      // 槌の頭の真下に影。近づくほど濃く小さく
      const r = a * Math.PI / 180;
      const hy = -H - L * Math.sin(r), hz = -D + L * Math.cos(r);
      const height = Math.max(0, -hy - HEAD_H / 2);
      shadow.style.transform = `translate(0, ${hz.toFixed(1)}px) translateZ(2px) scale(${(0.7 + height / 180).toFixed(3)})`;
      shadow.style.opacity = Math.max(0.12, 0.9 - height / 260).toFixed(2);
      // 打った瞬間：朱の星とこまかいきらめきが、写真から少し離れた所から上と横へ弾け、「ドン」の線と地面の輪が一瞬出る
      if (dt > 0 && sinceHit >= 0 && lastHit !== n) {
        lastHit = n; shakeT = 0; ringT = 0; popT = 0;
        for (let i = 0; i < STARS; i++) {
          const ang = rnd() * Math.PI * 2;
          const big = rnd() < 0.4;
          const size = big ? 28 + rnd() * 14 : 14 + rnd() * 8;
          const v = 150 + rnd() * 160;
          const el = div('forge3d__star', { width: `${size}px`, height: `${size}px`, marginLeft: `${-size / 2}px`, marginTop: `${-size / 2}px` }, world);
          el.innerHTML = big ? STAR_SVG : SPARK_SVG;
          stars.push({ el, x: Math.cos(ang) * 46, y: -26, z: Math.sin(ang) * 40, vx: Math.cos(ang) * v, vz: Math.sin(ang) * v * 0.8, vy: -(220 + rnd() * 200), spin: (rnd() - 0.5) * 720, rot: rnd() * 72, t: 0, life: 0.55 + rnd() * 0.3 });
        }
      }
      for (let i = stars.length - 1; i >= 0; i--) {
        const s = stars[i];
        s.t += dt;
        if (s.t >= s.life) { s.el.remove(); stars.splice(i, 1); continue; }
        s.vy += 700 * dt;
        s.x += s.vx * dt; s.y += s.vy * dt; s.z += s.vz * dt;
        s.rot += s.spin * dt;
        // 出た瞬間にぽんと大きくなり、終わりに縮んで消える
        const k = s.t / s.life;
        const sc = k < 0.15 ? 0.4 + 0.9 * (k / 0.15) : k < 0.3 ? 1.3 - 0.3 * ((k - 0.15) / 0.15) : 1 - Math.max(0, (k - 0.7) / 0.3);
        s.el.style.transform = `translate3d(${s.x.toFixed(1)}px, ${Math.min(-4, s.y).toFixed(1)}px, ${s.z.toFixed(1)}px) ${face} rotate(${s.rot.toFixed(1)}deg) scale(${sc.toFixed(3)})`;
      }
      popT += dt;
      const pk = Math.min(1, popT / 0.16);
      pop.style.opacity = popT < 0.16 ? (1 - pk).toFixed(2) : '0';
      pop.style.transform = `translate3d(0, -26px, 0) ${face} scale(${(0.8 + 0.6 * pk).toFixed(3)})`;
      ringT += dt;
      const rk = Math.min(1, ringT / 0.3), re = 1 - Math.pow(1 - rk, 3);
      ring.style.transform = `translateZ(3px) scale(${(1 + 9 * re).toFixed(2)})`;
      ring.style.borderWidth = `${(6 / (1 + 9 * re)).toFixed(2)}px`;
      ring.style.opacity = ringT < 0.3 ? ((1 - rk) * 0.85).toFixed(2) : '0';
      // 打った直後に、場面全体を小さく揺らす
      shakeT += dt;
      const shk = Math.max(0, 1 - shakeT / 0.18);
      world.style.transform = `translate(${(center + Math.sin(shakeT * 70) * 1.5 * shk).toFixed(2)}px, ${(Math.sin(shakeT * 95) * 3 * shk).toFixed(2)}px) scale3d(${VIEW.scale}, ${VIEW.scale}, ${VIEW.scale}) rotateX(${-VIEW.tilt}deg) rotateY(${VIEW.yaw}deg)`;
    };

    // 動きを減らす設定では、振りかぶったところで止めておく
    if (prefersReducedMotion()) {
      clock = SWING.rise;
      frame(0);
      return () => { world.textContent = ''; };
    }
    const tick = ts => {
      if (prev === null) prev = ts;
      const dt = Math.min(0.05, (ts - prev) / 1000);
      prev = ts;
      clock += dt;
      frame(dt);
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => { cancelAnimationFrame(raf); world.textContent = ''; };
  }, [imageSrc, aspect]);

  return (
    <div className="forge3d" aria-hidden="true">
      <div className="forge3d__stage">
        <div className="forge3d__world" ref={worldRef} />
      </div>
    </div>
  );
}
