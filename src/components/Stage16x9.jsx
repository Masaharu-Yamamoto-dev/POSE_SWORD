import { useEffect, useState } from 'react';
import './Stage16x9.css';

// 16:9 固定の舞台。中身は常に 1280×720 のつもりで組めばよく、
// ウィンドウに収まる最大の大きさへ丸ごと拡大・縮小して中央に置く（余白は和紙の地のまま）。
// 中では vw / vh の代わりに var(--stage-vw) / var(--stage-vh)（舞台の幅・高さの1%）を使う。
const STAGE_WIDTH = 1280;
const STAGE_HEIGHT = 720;

const fitScale = () => Math.min(window.innerWidth / STAGE_WIDTH, window.innerHeight / STAGE_HEIGHT);

export default function Stage16x9({ children }) {
  const [scale, setScale] = useState(fitScale);

  useEffect(() => {
    const onResize = () => setScale(fitScale());
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  return (
    <div className="stage169">
      <div className="stage169__canvas" style={{ transform: `scale(${scale})` }}>
        {children}
      </div>
    </div>
  );
}
