// 剣の写真の加工（シルエット・ワンポイント・左右反転）と、相手に見せる画像の決め方。
//
// プライバシーは剣ごとに { mode, spot } で持つ。
//   mode: 'none'（ナチュラル） / 'onepoint'（ワンポイント） / 'silhouette'（シルエット）
//   spot: ワンポイントの黒丸。画像に対する割合で持つ（x・y は左上が 0・右下が 1、r は画像の幅に対する半径）
// 元の写真（imageSrc / imageStr）は自分の画面でだけ使う。相手と Unity には publicImageOf の画像だけを渡す。

export const SPOT_MIN = 0.05;
export const SPOT_MAX = 0.3;
// 黒丸は写真の真ん中から、大きさは調整のつまみの真ん中あたりから始める
export const DEFAULT_SPOT = Object.freeze({ x: 0.5, y: 0.5, r: 0.17 });

export const newPrivacy = (mode = 'none') => ({ mode, spot: { ...DEFAULT_SPOT } });
export const privacyOf = sword => sword?.privacy ?? newPrivacy('none');

const loadImage = src => new Promise((resolve, reject) => {
  const img = new Image();
  img.onload = () => resolve(img);
  img.onerror = reject;
  img.src = src;
});

const canvasFor = img => {
  const canvas = document.createElement('canvas');
  canvas.width = img.width;
  canvas.height = img.height;
  return [canvas, canvas.getContext('2d')];
};

// 人が写っている所を真っ黒に塗りつぶす。透明な部分はそのまま
export async function makeSilhouetteImage(src) {
  if (!src) return '';
  const img = await loadImage(src);
  const [canvas, ctx] = canvasFor(img);
  ctx.drawImage(img, 0, 0);
  ctx.globalCompositeOperation = 'source-in';
  ctx.fillStyle = '#000';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  return canvas.toDataURL('image/png');
}

// 黒丸を焼き込む。人が写っている所にだけ塗る（source-atop）ので、透明な部分の形は元の写真と同じ。
// Unity は届いた画像の透明な部分から当たり判定を作り直すので、丸がはみ出すと剣の形が変わってしまう
export async function makeOnepointImage(src, spot) {
  if (!src) return '';
  const img = await loadImage(src);
  const [canvas, ctx] = canvasFor(img);
  ctx.drawImage(img, 0, 0);
  ctx.globalCompositeOperation = 'source-atop';
  ctx.fillStyle = '#000';
  ctx.beginPath();
  ctx.arc(spot.x * canvas.width, spot.y * canvas.height, spot.r * canvas.width, 0, Math.PI * 2);
  ctx.fill();
  return canvas.toDataURL('image/png');
}

export async function flipImage(src) {
  const img = await loadImage(src);
  const [canvas, ctx] = canvasFor(img);
  ctx.translate(canvas.width, 0);
  ctx.scale(-1, 1);
  ctx.drawImage(img, 0, 0);
  return canvas.toDataURL('image/png');
}

// 写真から作る加工済みの画像をまとめて作り直す（錬成・撮り直し・左右反転・丸を動かしたとき）
export async function derivedImages(imageSrc, privacy) {
  const silhouetteSrc = await makeSilhouetteImage(imageSrc);
  const onepointSrc = privacy?.mode === 'onepoint' ? await makeOnepointImage(imageSrc, privacy.spot) : '';
  return { silhouetteSrc, onepointSrc };
}

// 相手の画面と Unity に渡す画像。ナチュラル以外では、加工前の写真を決して返さない
// （ワンポイントの焼き込みがまだ終わっていない間は、シルエットで代わりにする）
export function publicImageOf(sword, isRandomMatch = false) {
  if (!sword) return '';
  const mode = isRandomMatch ? 'silhouette' : privacyOf(sword).mode;
  if (mode === 'none') return sword.imageSrc;
  if (mode === 'onepoint' && sword.onepointSrc) return sword.onepointSrc;
  return sword.silhouetteSrc || '';
}

export const base64Of = dataUrl => (dataUrl || '').split(',')[1] || '';
