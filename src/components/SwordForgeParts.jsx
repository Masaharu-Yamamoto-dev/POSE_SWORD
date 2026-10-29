// 錬成完了と武器庫で使う部品。剣の見本・墨の円相・墨のしぶき・ステータス・柄・プライバシー・名前の書き換え。
// 大きさや並びは SwordForgeParts.css と、それぞれの画面の CSS で決める。
import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { HILT_DATABASE } from '../screens/hiltDatabase.js';
import { SPOT_MAX, SPOT_MIN, privacyOf } from '../swordPrivacy.js';
import { STAT_LABELS, STAT_MAX, bonusOf, hiltOf, signed, useImageShape } from './swordForge.js';
import './SwordForgeParts.css';

// 押すことを知らせる指（指先は左から 10.5、上から 3 の所）。HAND_PATH は線（指ごとに分かれている）、
// HAND_FILL は手全体を一筆で囲んだ輪郭で、線の下に和紙の色で塗る（線だけだと手のひらが塗られず透けるため）
const HAND_FILL = 'M9 15 V4.5 a1.5 1.5 0 0 1 3 0 V9.5 a1.5 1.5 0 0 1 3 0 V10.5 a1.5 1.5 0 0 1 3 0 V15 a6 6 0 0 1 -6 6 h-1 a6 6 0 0 1 -5 -3 L4 14.5 a1.5 1.5 0 0 1 2.5 -1.7 L9 15 Z';
const HAND_PATH = 'M9 11 V4.5 a1.5 1.5 0 0 1 3 0 V10 M12 9.5 a1.5 1.5 0 0 1 3 0 V11 M15 10.5 a1.5 1.5 0 0 1 3 0 V15 a6 6 0 0 1 -6 6 h-1 a6 6 0 0 1 -5 -3 L4 14.5 a1.5 1.5 0 0 1 2.5 -1.7 L9 15';

// 柄の画像の縦横比（高さ÷幅）。ゲームと同じく、柄の高さは幅 1.435 に対して 1.5666 × この比
const HILT_ASPECT = { '0': 553 / 552, '1': 522 / 420, '2': 470 / 362, '3': 470 / 420 };
// 柄の画像の、透明でない所の下端（上端 0・下端 1 の割合）。画像は決まっているので、先に測っておいた値
const HILT_BOTTOM = { '0': 0.901, '1': 0.931, '2': 0.955, '3': 0.955 };
const hiltHeightOf = id => 1.5666 * (HILT_ASPECT[id] ?? HILT_ASPECT['0']);
// 刀身の下端から、柄の見えている先までの長さ（P を 1 とした値。柄の中心は刀身の下端から 0.4 下）
const hiltReachOf = (id, bottom = HILT_BOTTOM[id] ?? 1) => 0.4 + hiltHeightOf(id) * (bottom - 0.5);
// 一番長く伸びる柄の分。どの柄にしても剣の大きさと刀身の位置が変わらないよう、いつもこれを見込む
const HILT_REACH_MAX = Math.max(...Object.keys(HILT_ASPECT).map(id => hiltReachOf(id)));

// 剣の見本の寸法（どれも P を 1 とした値）。並べた箱は幅 1.5・高さ（刀身＋柄）で、横は真ん中が 0、縦は上端が 0。
// ・cx, cy：刀身と柄の、透明でない所を合わせた見た目の真ん中が、箱の真ん中からどれだけずれているか（回す中心）
// ・ry：刀身の見えている上端から「一番長い柄の先」までの真ん中が、箱の真ん中からどれだけずれているか（置く基準）
// ・reach：その長さ（剣の大きさを決める）
// ・pad：柄の見えている先から、箱の下端までの透明な余白
const standGeometryOf = (aspect, bladeBox, hiltType, hiltBox) => {
  const bladeH = 1.5 * aspect;
  const hh = hiltHeightOf(hiltType);
  const hiltTop = bladeH + 0.4 - hh / 2;
  const boxH = Math.max(bladeH, hiltTop + hh);
  const x0 = Math.min(-0.75 + 1.5 * bladeBox.l, -0.7175 + 1.435 * hiltBox.l);
  const x1 = Math.max(-0.75 + 1.5 * bladeBox.r, -0.7175 + 1.435 * hiltBox.r);
  const y0 = Math.min(bladeH * bladeBox.t, hiltTop + hh * hiltBox.t);
  const y1 = Math.max(bladeH * bladeBox.b, hiltTop + hh * hiltBox.b);
  const top = bladeH * bladeBox.t;
  const bottom = Math.max(bladeH * bladeBox.b, bladeH + HILT_REACH_MAX);
  return {
    hh,
    cx: (x0 + x1) / 2,
    cy: (y0 + y1) / 2 - boxH / 2,
    ry: (top + bottom) / 2 - boxH / 2,
    reach: bottom - top,
    pad: boxH - y1,
  };
};

// ==========================================
// 剣の見本（刀身＋柄）
// ==========================================
// 組み方はゲームと同じ：刀身は幅 1.5P（下端の真ん中が原点）、柄は幅 1.435P で、中心が原点から 0.4P 下。
// P は CSS で決める（--P0 が基本の大きさ。刀身の上端から一番長い柄の先までが --standH に収まらないときは小さくする）。
// 柄を替えても P は変わらない。
// shadow のときは、刀身と柄の上に真っ黒な影をかぶせる（消すタイミングは画面の CSS で決める）。
// badge は剣の右上に添える札（装備中など）。回る部分の外に置くので、剣を回しても回らない。
// editable のときは刀身を押す・なぞるとワンポイントの丸が動き、柄は押せなくなる
// （柄の箱が刀身の足もとに重なって、そこへ丸を置けなくなるのを防ぐ）。それ以外は押すと onPress。
export function SwordStand({
  imageSrc, hiltType = '0', privacy, editable = false, onSpotStart, onSpotChange, onPress,
  spin = 0, float = false, shadow = false, showGuide = false, label = '剣を回す', className = '', children, badge,
}) {
  const blade = useImageShape(imageSrc);
  const hilt = hiltOf(hiltType);
  const hiltShape = useImageShape(hilt.imageSrc, HILT_ASPECT[hiltType]);
  const aspect = blade.aspect;
  const { hh, cx, cy, ry, reach, pad } = standGeometryOf(aspect, blade.box, hiltType, hiltShape.box);
  const { mode, spot } = privacyOf({ privacy });
  // なぞっている間は手元だけで丸を動かし、指を離したときに確定する
  const [dragSpot, setDragSpot] = useState(null);
  const draggingRef = useRef(false);
  const shownSpot = dragSpot ?? spot;

  const posOf = e => {
    const rect = e.currentTarget.getBoundingClientRect();
    return {
      ...spot,
      x: Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width)),
      y: Math.max(0, Math.min(1, (e.clientY - rect.top) / rect.height)),
    };
  };
  const onPointerDown = e => {
    if (!editable) return;
    draggingRef.current = true;
    try { e.currentTarget.setPointerCapture(e.pointerId); } catch { /* 取れなくても押した所には動く */ }
    setDragSpot(posOf(e));
    onSpotStart?.();
  };
  const onPointerMove = e => { if (editable && draggingRef.current) setDragSpot(posOf(e)); };
  const onPointerEnd = e => {
    if (!draggingRef.current) return;
    draggingRef.current = false;
    const next = e.type === 'pointercancel' ? dragSpot : posOf(e);
    setDragSpot(null);
    if (next) onSpotChange?.(next);
  };

  const spotStyle = {
    left: `${(shownSpot.x * 100).toFixed(2)}%`,
    top: `${(shownSpot.y * 100).toFixed(2)}%`,
    '--spot-d': `${(shownSpot.r * 200).toFixed(2)}%`,
  };
  const maskUrl = `url("${imageSrc}")`;

  return (
    <div className={`stand ${className}`} style={{
      '--aspect': aspect.toFixed(4), '--hh': hh.toFixed(4), '--cx': cx.toFixed(4), '--cy': cy.toFixed(4),
      '--ry': ry.toFixed(4), '--reach': reach.toFixed(4), '--pad': pad.toFixed(4),
    }}>
      <div className={`stand__float ${float ? 'is-floating' : ''}`}>
        <div className="stand__spin" style={{ transform: `rotate(${spin * 360}deg)` }}>
          <button
            type="button"
            className={`stand__blade ${editable ? 'is-editing' : ''}`}
            aria-label={editable ? '剣の隠したい所を押す' : label}
            onClick={editable ? undefined : onPress}
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerEnd}
            onPointerCancel={onPointerEnd}
          >
            <img src={imageSrc} alt="" draggable={false} className={mode === 'silhouette' ? 'is-silhouette' : ''} />
            {shadow && <img src={imageSrc} alt="" draggable={false} className="stand__shadow" />}
            {mode === 'onepoint' && (
              <span className="stand__mask" aria-hidden="true" style={{ WebkitMaskImage: maskUrl, maskImage: maskUrl }}>
                <span className="stand__spot" style={spotStyle} />
              </span>
            )}
            {mode === 'onepoint' && showGuide && <span className="stand__guide" aria-hidden="true" style={spotStyle} />}
            {mode === 'onepoint' && showGuide && (
              <span className="stand__hand" aria-hidden="true" style={{ left: spotStyle.left, top: spotStyle.top }}>
                <svg viewBox="0 0 24 24"><path className="stand__hand-fill" d={HAND_FILL} /><path d={HAND_PATH} /></svg>
              </span>
            )}
          </button>
          <button
            type="button"
            className="stand__hilt"
            aria-label={label}
            tabIndex={editable ? -1 : 0}
            onClick={editable ? undefined : onPress}
            style={editable ? { pointerEvents: 'none' } : undefined}
          >
            <img src={hilt.imageSrc} alt="" draggable={false} />
            {shadow && <img src={hilt.imageSrc} alt="" draggable={false} className="stand__shadow" />}
          </button>
          {children}
        </div>
        {badge}
      </div>
    </div>
  );
}

// ==========================================
// 墨の円相
// ==========================================
// 太さが変わる帯・縁から食い込むかすれ・描き終わりで割れる穂先・はみ出す毛・しぶきを、540×540 の座標で描く。
// 毎回同じ形になるよう、決まった種から乱数を作る。
function makeEnso({ width, noise, dryW, splitW, hairs, hairW, drops }) {
  const ecx = 270, ecy = 270, R = 232, N = 150, a0 = 150, sweep = 322;
  const at = (t, off) => {
    const a = (a0 + sweep * t) * Math.PI / 180;
    const rr = R + 7 * Math.sin(t * Math.PI * 1.3) - 6 * t + off;
    return [ecx + rr * Math.cos(a), ecy + rr * Math.sin(a)];
  };
  const bandWidth = t => (width[0] + width[1] * Math.pow(1 - t, 0.75)) * Math.min(1, 0.45 + t / 0.06);
  const rough = (t, k) => noise[0] * Math.sin(t * 61 + k) + noise[1] * Math.sin(t * 143 + k * 2.1) + noise[2] * Math.sin(t * 311 + k * 3.7);
  const fmt = p => `${p[0].toFixed(1)} ${p[1].toFixed(1)}`;

  const outer = [], inner = [];
  for (let i = 0; i <= N; i++) {
    const t = i / N, w = bandWidth(t);
    outer.push(fmt(at(t, w / 2 + rough(t, 1))));
    inner.unshift(fmt(at(t, -w / 2 + rough(t, 2))));
  }
  const arc = [];
  for (let i = 0; i <= 60; i++) arc.push(fmt(at(-0.02 + 1.08 * i / 60, 0)));

  let seed = 11;
  const rnd = () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
  // grow の筋は細く始まって先ほど太くなる（穂先が割れて開いていく形）。それ以外は両端が細く消える
  const streak = (center, t1, t2, w0, grow) => {
    const o = [], n2 = [], n = 28;
    for (let k = 0; k <= n; k++) {
      const u = k / n, t = t1 + (t2 - t1) * u;
      const c = center(t);
      const th = w0 * (grow ? Math.pow(u, 0.7) : Math.pow(Math.sin(Math.PI * u), 0.8)) / 2;
      o.push(fmt(at(t, c + th)));
      n2.unshift(fmt(at(t, c - th)));
    }
    return `M${o.join(' L')} L${n2.join(' L')} Z`;
  };
  const half = t => bandWidth(Math.min(t, 1)) / 2;
  const dry = [];
  // 縁から食い込むかすれ：墨が切れていく後半ほど多く、筆が外へ流れる外側の縁に多い
  for (let j = 0; j < 16; j++) {
    const side = rnd() < 0.7 ? 1 : -1;
    const f = side * (0.66 + rnd() * 0.3);
    const t1 = 0.32 + Math.sqrt(rnd()) * 0.56;
    const t2 = Math.min(0.99, t1 + 0.05 + rnd() * 0.17);
    dry.push(streak(t => f * half(t), t1, t2, dryW[0] + rnd() * dryW[1], false));
  }
  // 描き終わりで穂先が割れる
  [-0.4, 0.05, 0.45].forEach(g => {
    const t1 = 0.78 + rnd() * 0.07;
    dry.push(streak(t => g * half(t), t1, 1.0, splitW[0] + rnd() * splitW[1], true));
  });
  // 毛先：描き終わりから、長さの違う細い毛が数本だけはみ出す
  const hair = hairs.map(off => {
    const t1 = 0.9 + rnd() * 0.05;
    return streak(t => off * (1 - (t - 0.9) * 2), t1, 1.0 + rnd() * 0.05, hairW[0] + rnd() * hairW[1]);
  });
  const st = at(0, 0);
  const drop = ([dx, dy, r]) => {
    const x = st[0] + dx, y = st[1] + dy;
    return `M${(x - r).toFixed(1)} ${y.toFixed(1)} a${r} ${r} 0 1 0 ${2 * r} 0 a${r} ${r} 0 1 0 ${-2 * r} 0`;
  };
  return {
    main: `M${outer.join(' L')} L${inner.join(' L')} Z`,
    dry: dry.join(' '),
    hair: hair.join(' '),
    arc: `M${arc.join(' L')}`,
    drops: drops.map(drop).join(' '),
  };
}

// 錬成完了の墨の円相と、武器庫で選んだ剣の後ろに置く朱の円相
const ENSO = {
  craft: makeEnso({
    width: [2.5, 38], noise: [1.8, 1.2, 0.6], dryW: [0.9, 2.6], splitW: [1.6, 1.2],
    hairs: [-4.2, -1.6, 0.6, 2.8, 4.6], hairW: [0.8, 1.1],
    drops: [[-30, -8, 5], [-44, 6, 3], [-22, 22, 2.5], [-56, -18, 2], [-14, -30, 1.8]],
  }),
  rack: makeEnso({
    width: [4, 50], noise: [2.4, 1.6, 0.8], dryW: [1.2, 3.4], splitW: [2.2, 1.6],
    hairs: [-5.5, -2, 0.8, 3.6, 6], hairW: [1.2, 1.4],
    drops: [[-40, -10, 8], [-62, 10, 5], [-30, 32, 4], [-78, -26, 3]],
  }),
};

// draw のときは、筆で描いていくように円相を現す（描き終わりの線をなぞる型抜きを伸ばしていく）
export function EnsoInk({ variant = 'craft', draw = false, className = '', style }) {
  const maskId = `enso${useId().replace(/[^a-zA-Z0-9]/g, '')}`;
  const e = ENSO[variant];
  return (
    <svg viewBox="0 0 540 540" aria-hidden="true" className={`enso enso--${variant} ${draw ? 'is-drawing' : ''} ${className}`} style={style}>
      {variant === 'craft' && (
        <mask id={maskId} maskUnits="userSpaceOnUse" x="-60" y="-60" width="660" height="660">
          <path className="enso__reveal" d={e.arc} pathLength={1} />
        </mask>
      )}
      <g mask={variant === 'craft' ? `url(#${maskId})` : undefined}>
        <path className="enso__main" d={e.main} />
        <path className="enso__dry" d={e.dry} />
        <path className="enso__hair" d={e.hair} />
      </g>
      <path className="enso__drops" d={e.drops} />
    </svg>
  );
}

// ==========================================
// 墨のしぶき（刀身と柄が合わさった瞬間に、円相の真ん中から放射状に飛ぶ）
// ==========================================
// 落ちずにまっすぐ外へ進み、空気の抵抗で減速しながら消える。速いうちは進む向きに尾を引き、遅くなると丸い粒になる。
// 向き・飛ぶ距離・大きさ・減速の強さは粒ごとにばらつかせ、大きな粒の後ろには小さな粒がいくつか続く（整った放射にしない）。
// 大きさの基準は円相の幅（幅 520px のとき k = 1）。粒は不透明に描き、全体の薄れ方は canvas の opacity で付ける
// （粒の頭と尾が重なった所だけ濃くならないように）
const BURST = { count: 15, seconds: 1.0, tail: 0.045 };
export function InkBurst({ originRef, delay = 560 }) {
  const canvasRef = useRef(null);
  useEffect(() => {
    let raf = 0;
    const timer = setTimeout(() => {
      const canvas = canvasRef.current;
      const origin = originRef.current;
      if (!canvas || !origin) return;
      const box = canvas.getBoundingClientRect();
      const scale = box.width / canvas.offsetWidth || 1; // 16:9 の舞台で縮んでいる分
      const o = origin.getBoundingClientRect();
      const ox = (o.left + o.width / 2 - box.left) / scale;
      const oy = (o.top + o.height / 2 - box.top) / scale;
      const k = o.width / scale / 520;
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.round(canvas.offsetWidth * dpr);
      canvas.height = Math.round(canvas.offsetHeight * dpr);
      const ctx = canvas.getContext('2d');
      ctx.scale(dpr, dpr);
      ctx.fillStyle = '#000';

      let seed = 5;
      const rnd = () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
      const drops = [];
      for (let i = 0; i < BURST.count; i++) {
        // 向きは等間隔から大きくずらす。ところどころ隙間や重なりができる
        const ang = (i / BURST.count) * Math.PI * 2 + (rnd() - 0.5) * 1.1;
        // 距離は近い粒と遠い粒の差を大きく、大きさは小さい粒が多く大きい粒は少しだけ
        const dist = (50 + Math.pow(rnd(), 0.7) * 260) * k;
        const size = (3 + Math.pow(rnd(), 2.2) * 17) * k;
        drops.push({ ang, dist, size, drag: 3 + rnd() * 3, wait: rnd() * 0.05 });
        // 大きな粒の後ろに、同じ向きへ少し遅れて小さな粒が続く
        const trail = size > 9 * k ? 1 + Math.floor(rnd() * 3) : rnd() < 0.3 ? 1 : 0;
        for (let j = 0; j < trail; j++) {
          drops.push({
            ang: ang + (rnd() - 0.5) * 0.35,
            dist: dist * (0.45 + rnd() * 0.5),
            size: (1.5 + rnd() * 3.5) * k,
            drag: 3.5 + rnd() * 3,
            wait: 0.02 + rnd() * 0.08,
          });
        }
      }
      drops.forEach(d => { d.dx = Math.cos(d.ang); d.dy = Math.sin(d.ang); });

      let t0 = null;
      const tick = ts => {
        if (t0 === null) t0 = ts;
        const u = Math.min(1, (ts - t0) / 1000 / BURST.seconds);
        const t = u * BURST.seconds;
        ctx.clearRect(0, 0, canvas.offsetWidth, canvas.offsetHeight);
        canvas.style.opacity = u < 0.05 ? u / 0.05 : Math.pow(1 - (u - 0.05) / 0.95, 1.3);
        if (u >= 1) return;
        drops.forEach(d => {
          const td = t - d.wait;
          if (td <= 0) return;
          const left = Math.exp(-d.drag * td);
          const reach = d.dist * (1 - left);
          const hx = ox + d.dx * reach, hy = oy + d.dy * reach;
          const r = Math.max(0.8, d.size * (1 - 0.35 * u) / 2);
          const tail = Math.min(d.dist * d.drag * left * BURST.tail, reach * 0.6);
          ctx.beginPath();
          ctx.arc(hx, hy, r, 0, Math.PI * 2);
          ctx.fill();
          if (tail > r) {
            ctx.beginPath();
            ctx.moveTo(hx - d.dx * tail, hy - d.dy * tail);
            ctx.lineTo(hx - d.dy * r, hy + d.dx * r);
            ctx.lineTo(hx + d.dy * r, hy - d.dx * r);
            ctx.closePath();
            ctx.fill();
          }
        });
        raf = requestAnimationFrame(tick);
      };
      raf = requestAnimationFrame(tick);
    }, delay);
    return () => { clearTimeout(timer); cancelAnimationFrame(raf); };
  }, [originRef, delay]);
  return <canvas ref={canvasRef} className="ink-burst" aria-hidden="true" style={{ opacity: 0 }} />;
}

// ==========================================
// ステータス
// ==========================================
// 上：HP 右下：攻 左下：重。点線が剣そのものの値、朱の面が柄の補正を足した値
const AXES = [[0, -1], [0.866, 0.5], [-0.866, 0.5]];
const radarPoints = values => values.map((v, i) => {
  const k = Math.max(0.04, Math.min(1, v / STAT_MAX[i]));
  return `${(59 + AXES[i][0] * 34 * k).toFixed(1)},${(56 + AXES[i][1] * 34 * k).toFixed(1)}`;
}).join(' ');

export function RadarChart({ base, shown, totals, className = '' }) {
  return (
    <svg viewBox="0 0 118 100" role="img" className={`radar ${className}`}
      aria-label={`HP ${totals[0]}、攻撃 ${totals[1]}、重さ ${totals[2]}`}>
      <polygon className="radar__ring" points={radarPoints(STAT_MAX)} />
      <polygon className="radar__ring" points={radarPoints(STAT_MAX.map(v => v / 2))} />
      <polygon className="radar__base" points={radarPoints(base)} />
      <polygon className="radar__total" points={radarPoints(shown)} />
      <text x="59" y="13">HP</text>
      <text x="99" y="89">攻</text>
      <text x="19" y="89">重</text>
    </svg>
  );
}

export function StatsList({ base, bonus, shown, className = '' }) {
  return (
    <div className={`stats ${className}`}>
      {STAT_LABELS.map((label, i) => (
        <div key={label} className="stats__row">
          <span className="stats__label">{label}</span>
          <span className="stats__total">{Math.round(shown[i])}</span>
          <span className="stats__breakdown">（{base[i]}{signed(bonus[i])}）</span>
        </div>
      ))}
    </div>
  );
}

// ==========================================
// 柄・必殺技
// ==========================================
const HILT_IDS = Object.keys(HILT_DATABASE);

// ‹ 柄の画像 › と、その下の4つの点。右に柄の名前と補正
export function HiltPicker({ hiltType = '0', onChange, className = '' }) {
  const index = Math.max(0, HILT_IDS.indexOf(hiltType));
  const hilt = hiltOf(hiltType);
  const bonus = bonusOf(hiltType);
  const go = i => { const id = HILT_IDS[(i + HILT_IDS.length) % HILT_IDS.length]; if (id !== hiltType) onChange(id); };
  return (
    <div className={`hilt-pick ${className}`}>
      <div className="hilt-pick__chooser">
        <div className="hilt-pick__row">
          <button type="button" className="hilt-pick__arrow" aria-label="前の柄" onClick={() => go(index - 1)}>
            <svg viewBox="0 0 40 60" aria-hidden="true"><path d="M30 6 L10 30 L30 54" /></svg>
          </button>
          <span className="hilt-pick__img"><img src={hilt.imageSrc} alt={hilt.name} /></span>
          <button type="button" className="hilt-pick__arrow" aria-label="次の柄" onClick={() => go(index + 1)}>
            <svg viewBox="0 0 40 60" aria-hidden="true"><path d="M10 6 L30 30 L10 54" /></svg>
          </button>
        </div>
        <div className="hilt-pick__dots">
          {HILT_IDS.map((id, i) => (
            <button key={id} type="button" aria-pressed={i === index} aria-label={`${HILT_DATABASE[id].name}にする`} onClick={() => go(i)}>
              <span className={i === index ? 'is-on' : ''} />
            </button>
          ))}
        </div>
      </div>
      <div className="hilt-pick__info">
        <span className="hilt-pick__name">{hilt.name}</span>
        <span className="hilt-pick__mods">
          {STAT_LABELS.map((label, i) => (
            <span key={label} className="hilt-pick__mod">
              <span className="hilt-pick__mod-label">{label}</span>
              <span className={bonus[i] < 0 ? 'is-minus' : ''}>{signed(bonus[i])}</span>
            </span>
          ))}
        </span>
      </div>
    </div>
  );
}

// 必殺技。アイコンは丸の外側に余白がある絵なので、枠より大きく描いて丸を枠に合わせる
export function SkillInfo({ hiltType = '0', className = '' }) {
  const hilt = hiltOf(hiltType);
  return (
    <div className={`skill ${className}`}>
      <span className="skill__icon"><img src={hilt.skillIcon} alt="" /></span>
      <span className="skill__text">
        <span className="skill__head"><span className="skill__tag">必殺技</span><span className="skill__name">{hilt.skillName}</span></span>
        <span className="skill__desc">{hilt.skillDescription}</span>
      </span>
    </div>
  );
}

// ==========================================
// プライバシー
// ==========================================
const PRIVACY_MODES = [
  { id: 'none', label: 'ナチュラル' },
  { id: 'onepoint', label: 'ワンポイント' },
  { id: 'silhouette', label: 'シルエット' },
];
const PRIVACY_HINTS = {
  none: 'どこも隠さず、そのままの姿で戦います',
  onepoint: '自分の体を一箇所だけ隠して戦います',
  silhouette: '全身を隠して戦います',
};

const ModeIcon = ({ mode, thumbSrc }) => {
  if (mode === 'none') return (
    <svg viewBox="0 0 32 32" aria-hidden="true">
      <path d="M5 27 C4 15 12 5 28 4 C28 19 19 28 5 27 Z" style={{ fill: 'var(--sumi)' }} />
      <path d="M7.5 24.5 C12 19 17 13.5 23.5 8.5" style={{ fill: 'none', stroke: 'var(--washi)', strokeWidth: 1.8, strokeLinecap: 'round' }} />
    </svg>
  );
  if (mode === 'onepoint') return <svg viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="11" style={{ fill: 'var(--sumi)' }} /></svg>;
  return <img src={thumbSrc} alt="" className="privacy__thumb" />;
};

// 見え方の3つの札・丸の大きさ・説明。how はワンポイントの丸の動かし方の説明（武器庫で出す）
export function PrivacyPicker({ privacy, onChange, thumbSrc, how = false, className = '' }) {
  const { mode, spot } = privacyOf({ privacy });
  const onepoint = mode === 'onepoint';
  return (
    <div className={`privacy ${className}`}>
      <div className="privacy__modes">
        {PRIVACY_MODES.map(m => (
          <button key={m.id} type="button" aria-pressed={m.id === mode}
            className={`privacy__mode ${m.id === mode ? 'is-on' : ''}`}
            onClick={() => { if (m.id !== mode) onChange({ mode: m.id, spot }); }}>
            <span className="privacy__icon"><ModeIcon mode={m.id} thumbSrc={thumbSrc} /></span>
            <span className="privacy__label">{m.label}</span>
            <span className="inkline privacy__line" aria-hidden="true" />
          </button>
        ))}
      </div>
      <div className={`privacy__size ${onepoint ? '' : 'is-off'}`}>
        <span className="privacy__dot privacy__dot--sm" aria-hidden="true" />
        {/* つまみの通り道は墨の筆跡。つまみまでを濃く塗る（--fill） */}
        <span className="privacy__track" style={{ '--fill': `${((spot.r - SPOT_MIN) / (SPOT_MAX - SPOT_MIN) * 100).toFixed(1)}%` }}>
          <input type="range" min={SPOT_MIN * 100} max={SPOT_MAX * 100} step="1" value={Math.round(spot.r * 100)}
            disabled={!onepoint} aria-label="ワンポイントの丸の大きさ"
            onChange={e => onChange({ mode, spot: { ...spot, r: Number(e.target.value) / 100 } })} />
        </span>
        <span className="privacy__dot privacy__dot--lg" aria-hidden="true" />
      </div>
      <p className="privacy__hint">{PRIVACY_HINTS[mode]}</p>
      {how && <p className="privacy__how">{onepoint ? '上の剣を押すか、なぞって隠す場所を動かせます' : ''}</p>}
    </div>
  );
}

// ==========================================
// タブ
// ==========================================
export function Tabs({ tabs, value, onChange, className = '' }) {
  return (
    <div role="tablist" className={`tabs ${className}`}>
      {tabs.map(t => (
        <button key={t.id} type="button" role="tab" aria-selected={t.id === value}
          className={`tabs__tab ${t.id === value ? 'is-on' : ''}`} onClick={() => onChange(t.id)}>
          {t.label}
          <span className="inkline tabs__line" aria-hidden="true" />
        </button>
      ))}
    </div>
  );
}

// ==========================================
// 名前（表示と書き換え）
// ==========================================
// overlay のときは、書き換え欄を画面の上端に等倍で出す（16:9 の舞台がスマホの横持ちで小さく縮んでいると、
// 舞台の中の入力欄は文字が小さく、キーボードにも隠れてしまうため）
const PencilIcon = () => (
  <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M3 17 L4 13 L13.5 3.5 L16.5 6.5 L7 16 Z" /></svg>
);

const CrossIcon = () => (
  <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M5 5 L15 15 M15 5 L5 15" /></svg>
);

function NameFields({ draft, setDraft, maxLen, onSave, onCancel, autoFocus = false, compact = false }) {
  const count = Array.from(draft).length;
  const empty = draft.trim() === '';
  const onKeyDown = e => {
    if (e.nativeEvent.isComposing) return;
    if (e.key === 'Enter' && !empty) onSave();
    else if (e.key === 'Escape') onCancel();
  };
  return (
    <>
      <span className="name-edit__field">
        <input type="text" value={draft} aria-label="剣の名前" autoFocus={autoFocus}
          onChange={e => setDraft(Array.from(e.target.value).slice(0, maxLen).join(''))} onKeyDown={onKeyDown} />
        <span className={`name-edit__count ${count >= maxLen ? 'is-full' : ''}`} aria-live="polite">{count} / {maxLen}</span>
      </span>
      <button type="button" className={`name-edit__save ${compact ? 'is-compact' : ''}`} disabled={empty} onClick={onSave}>変更</button>
      {compact
        ? <button type="button" className="name-edit__cancel is-icon" aria-label="キャンセル" title="キャンセル" onClick={onCancel}><CrossIcon /></button>
        : <button type="button" className="name-edit__cancel" onClick={onCancel}>キャンセル</button>}
    </>
  );
}

// compact：書き換え欄のボタンを小さくし、キャンセルを × にする。iconOnly：「名前を変える」を鉛筆のアイコンだけにする。
// onEditingChange：書き換えを始めた・終えたことを知らせる（書き換え中だけ周りの札を隠すときに使う）
// 名前を2行に折り返すときの行の高さ（文字の大きさに対する割合）
const NAME_WRAP_LINE = 1.1;

export function NameEditor({
  name, onSave, maxLen = 15, fitMax, fitLines = 1, overlay = false, className = '', buttonClassName = '',
  compact = false, iconOnly = false, onEditingChange,
}) {
  const [editing, setEditingState] = useState(false);
  const [draft, setDraft] = useState(name);
  const setEditing = next => { setEditingState(next); onEditingChange?.(next); };
  const start = () => { setDraft(name); setEditing(true); };
  const cancel = () => setEditing(false);
  const save = () => {
    const value = draft.trim();
    if (!value) return;
    onSave(value);
    setEditing(false);
  };
  const inPlace = editing && !overlay;
  const textRef = useRef(null);

  // 名前は、行に入る範囲でできるだけ大きく（fitMax まで）。行の実際の幅と、文字の実際の幅を測って合わせる
  // （1文字あたりの幅の見積もりで決めると、長い名前で右が大きく余るため）。
  // fitLines が 2 のときは、1行だと小さくなる長い名前を2行に折り返し、行の高さいっぱいまで大きくする。
  // 大きさは React を通さずに直接付ける（測ってから決めるので、描く前にここで1回で済ませる）
  useLayoutEffect(() => {
    const text = textRef.current;
    if (!fitMax || !text) return;
    const row = text.closest('.name-edit__viewing');
    const button = row?.querySelector('.name-edit__start');
    let alive = true;
    const set = (size, wrap = false, width = '') => {
      text.style.fontSize = `${size}px`;
      text.style.whiteSpace = wrap ? 'normal' : '';
      // 2行に折り返すときは、2行の長さをそろえる
      text.style.textWrap = wrap ? 'balance' : '';
      text.style.lineHeight = wrap ? String(NAME_WRAP_LINE) : '';
      text.style.width = width;
    };
    const apply = () => {
      if (!alive || !row) return;
      const gap = parseFloat(getComputedStyle(row).columnGap) || 0;
      // 名前を変えるボタンと、下線が右へはみ出す分を除いた幅
      const room = row.clientWidth - (button ? button.offsetWidth + gap : 0) - 14;
      set(fitMax);
      const width = text.scrollWidth;
      const single = width > room ? Math.max(12, Math.floor(fitMax * room / width)) : fitMax;
      set(single);
      if (fitLines < 2 || single >= fitMax) return;
      // 2行：下線の分を除いた行の高さに2行が収まる大きさから、幅にも収まるまで下げる（1行より大きくなるときだけ）
      const height = row.clientHeight - (parseFloat(getComputedStyle(text.parentElement).paddingBottom) || 0);
      for (let size = Math.min(fitMax, Math.floor(height / (2 * NAME_WRAP_LINE))); size > single; size--) {
        set(size, true, `${room}px`);
        if (text.scrollHeight > height + 0.5 || text.scrollWidth > room) continue;
        // 下線と名前を変えるボタンを、長い方の行の右端にそろえる
        const range = document.createRange();
        range.selectNodeContents(text);
        const longest = Math.max(...Array.from(range.getClientRects(), r => r.width));
        set(size, true, `${Math.min(room, Math.ceil(longest) + 1)}px`);
        return;
      }
      set(single);
    };
    apply();
    // 文字の形が読み込み終わると幅が変わるので、そのときと、行の幅が変わったときに合わせ直す
    document.fonts?.ready.then(apply);
    const observer = new ResizeObserver(apply);
    observer.observe(row);
    return () => { alive = false; observer.disconnect(); };
  }, [name, fitMax, fitLines, inPlace]);

  return (
    <div className={`name-edit ${className}`}>
      {inPlace ? (
        <div className="name-edit__editing">
          <NameFields draft={draft} setDraft={setDraft} maxLen={maxLen} onSave={save} onCancel={cancel} autoFocus compact={compact} />
        </div>
      ) : (
        <div className="name-edit__viewing">
          <span className="name-edit__name">
            <span ref={textRef} className="name-edit__text">{name}</span>
            <span className="inkline name-edit__line" aria-hidden="true" />
          </span>
          {iconOnly
            ? <button type="button" className={`name-edit__start is-icon ${buttonClassName}`} aria-label="名前を変える" title="名前を変える" onClick={start}><PencilIcon /></button>
            : <button type="button" className={`name-edit__start ${buttonClassName}`} onClick={start}><PencilIcon />名前を変える</button>}
        </div>
      )}
      {editing && overlay && createPortal(
        <div className="forge-portal name-bar-veil" onPointerDown={e => { if (e.target === e.currentTarget) cancel(); }}>
          <div className="name-bar" role="dialog" aria-label="剣の名前を変える">
            <NameFields draft={draft} setDraft={setDraft} maxLen={maxLen} onSave={save} onCancel={cancel} autoFocus />
          </div>
        </div>,
        document.body,
      )}
    </div>
  );
}

// ==========================================
// 小さな情報の帯（縦長で下へスクロールしたときに、上端に重ねる：名前・ステータスの数字・必殺技のアイコン）
// ==========================================
// level（useCompactLevel）の段階に合わせて、元の表示が隠れた所から順に出す：1 名前、2 ステータス、3 必殺技
export function CompactBar({ name, shown, hiltType, level }) {
  const hilt = hiltOf(hiltType);
  const on = n => (level >= n ? 'is-on' : '');
  return (
    <div className={`forge-compact ${level > 0 ? 'is-shown' : ''}`} aria-hidden={level === 0}>
      <span className={`forge-compact__name ${on(1)}`}>{name}</span>
      <span className={`forge-compact__stats ${on(2)}`}>
        {STAT_LABELS.map((label, i) => (
          <span key={label}><span className="forge-compact__label">{label}</span><span className="forge-compact__total">{Math.round(shown[i])}</span></span>
        ))}
      </span>
      <span className={`skill__icon forge-compact__icon ${on(3)}`}><img src={hilt.skillIcon} alt={hilt.skillName} /></span>
    </div>
  );
}

// ==========================================
// 確認のシート（破棄など、取り消せない操作の前に出す）
// ==========================================
// onCancel を渡さないときは、お知らせとして出す（キャンセルがなく、外側を押しても閉じない。ボタンは confirmLabel の1つだけ）
export function ConfirmSheet({ title, children, confirmLabel, onConfirm, onCancel }) {
  return createPortal(
    <div className="forge-portal confirm-veil" onPointerDown={e => { if (onCancel && e.target === e.currentTarget) onCancel(); }}>
      <div className="confirm-sheet" role="alertdialog" aria-modal="true" aria-label={title}>
        <h3>{title}</h3>
        <div className="confirm-sheet__body">{children}</div>
        <div className="confirm-sheet__buttons">
          {onCancel && <button type="button" className="confirm-sheet__cancel" onClick={onCancel} autoFocus>キャンセル</button>}
          <button type="button" className="confirm-sheet__ok" onClick={onConfirm} autoFocus={!onCancel}>{confirmLabel}</button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

// 通信中など、待っているあいだのお知らせ（確認のシートと同じ形で、ボタンなし）。
// 周りは暗くせず、下の画面も押せるまま（待っているあいだに押してはいけないものは、呼び出し側で止める）
export function BusySheet({ title, children }) {
  return createPortal(
    <div className="forge-portal confirm-veil is-clear">
      <div className="confirm-sheet is-busy" role="status" aria-live="polite">
        <h3>{title}<span className="confirm-sheet__dots" aria-hidden="true"><i>.</i><i>.</i><i>.</i></span></h3>
        <div className="confirm-sheet__body">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
