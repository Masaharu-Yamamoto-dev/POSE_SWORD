// 錬成完了と武器庫で使う計算とフック（画面の向き・数字の動き・剣の数値）。部品は SwordForgeParts.jsx
import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { HILT_DATABASE } from '../screens/hiltDatabase.js';

// ==========================================
// 画面の向き・動きを減らす設定・16:9の舞台の倍率
// ==========================================
const subscribeQuery = query => onChange => {
  const list = window.matchMedia(query);
  list.addEventListener('change', onChange);
  return () => list.removeEventListener('change', onChange);
};
const PORTRAIT = '(orientation: portrait)';
const REDUCED = '(prefers-reduced-motion: reduce)';
const subscribePortrait = subscribeQuery(PORTRAIT);
const subscribeReduced = subscribeQuery(REDUCED);

// 縦長か横長か（ウィンドウの縦横比で決まるので、PCのウィンドウでも同じように切り替わる）
export const usePortrait = () => useSyncExternalStore(subscribePortrait, () => window.matchMedia(PORTRAIT).matches);
export const prefersReducedMotion = () => window.matchMedia(REDUCED).matches;
export const useReducedMotion = () => useSyncExternalStore(subscribeReduced, prefersReducedMotion);

// Stage16x9 と同じ計算。舞台がどれだけ縮んでいるか
const subscribeResize = onChange => {
  window.addEventListener('resize', onChange);
  return () => window.removeEventListener('resize', onChange);
};
const stageScale = () => Math.min(window.innerWidth / 1280, window.innerHeight / 720);
export const useStageScale = () => useSyncExternalStore(subscribeResize, stageScale);

// 画像の形：縦横比（高さ÷幅）と、透明でない所が収まる枠（左上 0・右下 1 の割合）。
// 切り抜いた写真は人の周りに透明な余白が残ることがあるので、見た目の真ん中を出すのに使う
const FULL_BOX = Object.freeze({ l: 0, t: 0, r: 1, b: 1 });
const SHAPE_CACHE_SIZE = 8;
const shapeCache = new Map();

const measureShape = src => new Promise(resolve => {
  const img = new Image();
  img.onload = () => {
    const nw = img.naturalWidth, nh = img.naturalHeight;
    if (!nw || !nh) return resolve(null);
    // 縮めて数える（端の1〜2pxのずれは見た目に分からない）
    const s = Math.min(1, 160 / Math.max(nw, nh));
    const w = Math.max(1, Math.round(nw * s)), h = Math.max(1, Math.round(nh * s));
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(img, 0, 0, w, h);
    const data = ctx.getImageData(0, 0, w, h).data;
    let l = w, t = h, r = -1, b = -1;
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        if (data[(y * w + x) * 4 + 3] < 24) continue;
        if (x < l) l = x;
        if (x > r) r = x;
        if (y < t) t = y;
        if (y > b) b = y;
      }
    }
    resolve({ aspect: nh / nw, box: r < 0 ? FULL_BOX : { l: l / w, t: t / h, r: (r + 1) / w, b: (b + 1) / h } });
  };
  img.onerror = () => resolve(null);
  img.src = src;
});

// 読み込むまでは前の値のまま
export function useImageShape(src, fallbackAspect = 1.5) {
  const [shape, setShape] = useState({ aspect: fallbackAspect, box: FULL_BOX });
  useEffect(() => {
    if (!src) return;
    let alive = true;
    let job = shapeCache.get(src);
    if (!job) {
      job = measureShape(src);
      shapeCache.set(src, job);
      // 写真は大きいので、覚えておくのは最近の数枚だけ
      if (shapeCache.size > SHAPE_CACHE_SIZE) shapeCache.delete(shapeCache.keys().next().value);
    }
    job.then(next => { if (alive && next) setShape(next); });
    return () => { alive = false; };
  }, [src]);
  return shape;
}

// 数字をなめらかに目標の値へ動かす（ease-out）。delay だけ待ってから動き出す
export function useAnimatedValues(target, { from = null, delay = 0, duration = 450 } = {}) {
  const reduced = useReducedMotion();
  const [shown, setShown] = useState(() => from ?? target);
  const shownRef = useRef(shown);
  const key = target.join(',');
  useEffect(() => {
    const to = key.split(',').map(Number);
    const start = shownRef.current;
    const length = reduced ? 0 : duration;
    let raf = 0;
    let t0 = null;
    const tick = ts => {
      if (t0 === null) t0 = ts;
      const k = length <= 0 ? 1 : Math.min(1, (ts - t0) / length);
      const e = 1 - Math.pow(1 - k, 3);
      const next = start.map((v, i) => v + (to[i] - v) * e);
      shownRef.current = next;
      setShown(next);
      if (k < 1) raf = requestAnimationFrame(tick);
    };
    const timer = setTimeout(() => { raf = requestAnimationFrame(tick); }, reduced ? 0 : delay);
    return () => { clearTimeout(timer); cancelAnimationFrame(raf); };
  }, [key, delay, duration, reduced]);
  return shown;
}

// ==========================================
// 剣の数値
// ==========================================
export const STAT_LABELS = ['HP', '攻撃', '重さ'];
export const STAT_MAX = [1000, 100, 100];
export const hiltOf = id => HILT_DATABASE[id || '0'] || HILT_DATABASE['0'];
export const baseStatsOf = sword => [sword?.hp || 0, sword?.attack || 0, sword?.weight || 0];
export const bonusOf = id => { const h = hiltOf(id); return [h.hpBonus || 0, h.attackBonus || 0, h.weightBonus || 0]; };
export const totalsOf = (sword, hiltId = sword?.hiltType) => {
  const bonus = bonusOf(hiltId);
  return baseStatsOf(sword).map((v, i) => Math.max(1, v + bonus[i]));
};
// 全角の記号で書く：＋20 / －50 / ±0
export const signed = n => n > 0 ? `＋${n}` : n < 0 ? `－${Math.abs(n)}` : '±0';

// ==========================================
// 縦長の小さな情報の帯
// ==========================================
// 元の表示の名前・ステータス・必殺技が、上から順に帯の後ろへ隠れるたびに、帯へ1つずつ小さく出す（段階 0〜3）。
// それぞれ、真ん中が「その時点の帯の下端」より上へ来たら隠れたとみなす。帯の高さは CSS（.forge-scroll-wrap.is-compact-N）と同じ
export const COMPACT_BAR_H = [0, 36, 68, 68];

export function useCompactLevel(selectors) {
  const [level, setLevel] = useState(0);
  const onScroll = e => {
    const box = e.currentTarget;
    const top = box.getBoundingClientRect().top;
    let next = 0;
    for (const selector of selectors) {
      const el = box.querySelector(selector);
      if (!el) break;
      const r = el.getBoundingClientRect();
      if ((r.top + r.bottom) / 2 - top >= COMPACT_BAR_H[next]) break;
      next += 1;
    }
    setLevel(next);
  };
  return [level, onScroll];
}
