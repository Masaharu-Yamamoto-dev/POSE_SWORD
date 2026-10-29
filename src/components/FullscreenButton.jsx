import { useEffect, useState } from 'react';
import './FullscreenButton.css';

// スマホ・タブレットで、アドレスバーなどを消して画面いっぱいで遊ぶためのボタン。
// 全画面はブラウザの決まりでタップした時にしか始められないので、自動では切り替えずボタンにしている。
// 出すのは全画面にできる端末だけ（Androidなど）。iPhoneのSafariはページからの全画面に
// 対応していない（document.fullscreenEnabled が false）ので、ボタン自体を出さない。
// PCは対象外。全画面の間はボタンを隠す（戻すのは端末の戻る操作で行う）。
const TOUCH = '(pointer: coarse)';

const canFullscreen = () => Boolean(document.fullscreenEnabled && document.documentElement.requestFullscreen);

export default function FullscreenButton() {
  const [isTouch, setIsTouch] = useState(() => window.matchMedia(TOUCH).matches);
  const [isFull, setIsFull] = useState(() => Boolean(document.fullscreenElement));

  useEffect(() => {
    const query = window.matchMedia(TOUCH);
    const onTouch = () => setIsTouch(query.matches);
    const onFull = () => setIsFull(Boolean(document.fullscreenElement));
    query.addEventListener('change', onTouch);
    document.addEventListener('fullscreenchange', onFull);
    return () => {
      query.removeEventListener('change', onTouch);
      document.removeEventListener('fullscreenchange', onFull);
    };
  }, []);

  if (!isTouch || isFull || !canFullscreen()) return null;

  const enter = async () => {
    try {
      await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
      // 全画面の間だけ横向きに固定できる（Androidなど）。できない端末では何もしない
      await screen.orientation?.lock?.('landscape');
    } catch {
      // 断られた・固定に未対応の時は、今の表示のまま続ける
    }
  };

  return (
    <button type="button" className="fullscreen-btn" onClick={enter} aria-label="全画面で遊ぶ" title="全画面で遊ぶ">
      <span className="fullscreen-btn__mark" aria-hidden="true" />
    </button>
  );
}
