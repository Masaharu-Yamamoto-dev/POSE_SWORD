import { useLayoutEffect, useRef } from 'react';

// 中身がウィンドウの高さに収まらないとき、収まるまで全体を縮める（縦スクロールをできるだけ出さない）。
// 縮めすぎると文字が読めず、ボタンも押しにくいので下限を設ける。
// 下限まで縮めても収まらないときは、縮めずに元の大きさのままスクロールさせる（小さいうえにスクロール、を避ける）。
// 16:9固定の画面（Stage16x9）には使わない。あちらは自分で拡大・縮小する。
const MIN_ZOOM = 0.6;

export default function FitToHeight({ children }) {
  const ref = useRef(null);

  useLayoutEffect(() => {
    const el = ref.current;
    const fit = () => {
      // いったん等倍に戻して、本来の高さを測る
      // （scrollHeight は使わない：ボタンの筆跡など、枠の外へはみ出す飾りまで数えてしまう）
      el.style.zoom = 1;
      const natural = el.offsetHeight;
      // 倍率は切り捨てる（端数の丸めで1pxはみ出すのを避ける）
      const zoom = Math.floor((window.innerHeight / natural) * 1000) / 1000;
      el.style.zoom = zoom < 1 && zoom >= MIN_ZOOM ? zoom : 1;
    };
    fit();
    // 中身の高さが変わったとき（メッセージが出た・画像が読み込まれた等）と、ウィンドウの大きさが変わったとき
    const observer = new ResizeObserver(fit);
    observer.observe(el);
    window.addEventListener('resize', fit);
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', fit);
    };
  }, []);

  return <div ref={ref}>{children}</div>;
}
