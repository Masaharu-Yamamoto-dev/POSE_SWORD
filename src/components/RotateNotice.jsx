import { useEffect, useState } from 'react';
import './RotateNotice.css';

// 16:9固定の画面（ロビー・対戦・結果）を、スマホやタブレットを縦に持って見ているときの案内。
// 上にかぶせて見せるだけで、下の画面は止めも外しもしない。
// （ホストの端末が対戦の進行を担っているので、向きが変わっても試合は動き続ける必要がある）
// PCは対象外：マウスで操作する端末では、ウィンドウが縦長でも出さない。
const PORTRAIT_TOUCH = '(orientation: portrait) and (pointer: coarse)';

export default function RotateNotice() {
  const [isPortrait, setIsPortrait] = useState(() => window.matchMedia(PORTRAIT_TOUCH).matches);
  // 「このまま続ける」を押したら、次に横向きへ戻すまで出さない
  const [dismissed, setDismissed] = useState(false);

  useEffect(() => {
    const query = window.matchMedia(PORTRAIT_TOUCH);
    const onChange = () => {
      setIsPortrait(query.matches);
      if (!query.matches) setDismissed(false);
    };
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, []);

  if (!isPortrait || dismissed) return null;

  return (
    <div className="rotate-notice" role="alert">
      <div className="rotate-notice__phone" aria-hidden="true" />
      <p className="rotate-notice__text">端末を横向きにしてください</p>
      <p className="rotate-notice__sub">対戦は横向きの画面で遊びます</p>
      <button type="button" className="brush-link" onClick={() => setDismissed(true)}>このまま続ける</button>
    </div>
  );
}
