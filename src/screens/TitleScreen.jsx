// src/screens/TitleScreen.jsx
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import InkButton from '../components/InkButton.jsx';
import { BusySheet } from '../components/SwordForgeParts.jsx';
import '../components/SwordForgeParts.css';
import Stage16x9 from '../components/Stage16x9.jsx';
import { usePortrait } from '../components/swordForge.js';
import './TitleScreen.css';

const ID_LENGTH = 6;
const SWORD_MAX = 3;
// メニューの中身を切り替えるクロスフェードの長さ（TitleScreen.css の .title-swap__layer と同じ）
const SWAP_MS = 280;
// 縦長でメニューを縮めるときの下限。これより小さいと文字が読みにくく、ボタンも押しにくい
const MIN_FIT_ZOOM = 0.55;

// 縦長：メニューの中身（.title-fit）が、ロゴの下の残りの高さ（.title-side）に収まるまで縮める。
// スクロールバーを出さないため。下限まで縮めても収まらないときだけ、バーを見せずにスクロールさせる。
// 測り方は FitToHeight と同じ（いったん等倍に戻して本来の高さを測る）。
// パネルを切り替えると、消えていく前のパネル（.is-out）は浮かせて重ねるので高さに数えず、
// 新しいパネルだけで倍率を決める。前のパネルは消え終わるまで元の大きさで見せたいので、
// 全体の倍率の変化を打ち消す倍率を前のパネルにだけ掛ける（zoom は入れ子で掛け算になる）。
// panelKey が変わるたびに測り直す（画面に出す前に済ませるので、大きさが途中で変わって見えない）
function useFitMenu(enabled, panelKey) {
  const sideRef = useRef(null);
  const fitRef = useRef(null);
  const outRef = useRef(null);
  // いま全体に掛けている倍率（＝切り替える前のパネルが見えていた倍率）
  const appliedRef = useRef(1);
  useLayoutEffect(() => {
    const side = sideRef.current, fit = fitRef.current;
    if (!side || !fit) return;
    if (!enabled) { fit.style.zoom = ''; side.style.overflowY = ''; appliedRef.current = 1; return; }
    const before = appliedRef.current;
    const apply = () => {
      fit.style.zoom = 1;
      const style = getComputedStyle(side);
      const room = side.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom);
      const natural = fit.offsetHeight;
      // 倍率は切り捨てる（端数の丸めで1pxはみ出すのを避ける）
      const fitZoom = natural > 0 ? Math.floor((room / natural) * 1000) / 1000 : 1;
      const zoom = fitZoom < 1 ? Math.max(fitZoom, MIN_FIT_ZOOM) : 1;
      fit.style.zoom = zoom;
      appliedRef.current = zoom;
      if (outRef.current) outRef.current.style.zoom = before / zoom;
      // 収まったときはスクロールさせない。ボタンの筆跡の飾り（ふだんは見えない大きな枠）が
      // はみ出し分として数えられ、バーの無いままわずかに動いてしまうため
      side.style.overflowY = fitZoom >= MIN_FIT_ZOOM ? 'hidden' : '';
    };
    apply();
    // 中身が変わったとき（メッセージが出た等）と、画面の大きさが変わったとき
    const observer = new ResizeObserver(apply);
    observer.observe(side);
    observer.observe(fit);
    return () => observer.disconnect();
  }, [enabled, panelKey]);
  return { sideRef, fitRef, outRef };
}

// 縦長のタイトルは画面に固定して敷くので、ページ自体はスクロールさせない。
// （外側の枠は 100vh の高さがあり、スマホでアドレスバーが出ている間は、見えている高さより少し長くなる）
function useLockPageScroll(enabled) {
  useEffect(() => {
    if (!enabled) return;
    const html = document.documentElement;
    const before = html.style.overflow;
    html.style.overflow = 'hidden';
    return () => { html.style.overflow = before; };
  }, [enabled]);
}

export default function TitleScreen({
  direction = "forward", // 🌟 追加：遷移方向を受け取る
  mySwordData,
  backgroundSrc = '', // 背景に薄く出す剣。プライバシーの設定を反映した画像を受け取る
  titleMode,          
  targetId,           
  setTargetId,        
  systemMessage,
  goToCrafting,
  startCrafting,      // 剣を錬成する（名前の入力から）
  swordCount = 0,     // 持っている剣の数（0本なら武器庫は開けない、3本なら新しく作れない）
  handleCreateRoom,
  handleJoinRoom,
  handleCancelJoin,   
  connectToHost,
  connecting,
  openRandomMatch,
  startRandomMatch,
  matchMode,
  setMatchMode,
  onOpenHowToPlay,
  matchmaking = null  // ランダムマッチの待ち表示。あるときはメニューの代わりに出す
}) {
  // 🌟 追加：アニメーションクラスの判定
  const animClass = direction === "back" ? "page-enter-back" : "page-enter-forward";
  const portrait = usePortrait();
  useLockPageScroll(portrait);
  // ランダムマッチの希望人数（マッチング開始を押すまで選び直せる）
  const [matchSize, setMatchSize] = useState(2);

  // ロビーID：6桁そろったら入室する（同じIDで何度も試さないよう、試したIDを覚えておく）
  const triedIdRef = useRef('');
  useEffect(() => {
    if (titleMode !== 'JOIN_INPUT' || connecting) return;
    if (targetId.length < ID_LENGTH) { triedIdRef.current = ''; return; }
    if (triedIdRef.current === targetId) return;
    triedIdRef.current = targetId;
    connectToHost();
  }, [titleMode, targetId, connecting, connectToHost]);

  // メニューの中身の切り替え（クロスフェード）。切り替わったら、前の中身を SWAP_MS のあいだ重ねて残す
  const panelKey = matchmaking ? 'MATCHING' : titleMode;
  const { sideRef, fitRef, outRef } = useFitMenu(portrait, panelKey);
  const [currentKey, setCurrentKey] = useState(panelKey);
  const [exiting, setExiting] = useState(null);
  if (currentKey !== panelKey) {
    setCurrentKey(panelKey);
    setExiting(currentKey);
  }
  useEffect(() => {
    if (!exiting) return;
    const timer = setTimeout(() => setExiting(null), SWAP_MS);
    return () => clearTimeout(timer);
  }, [exiting]);

  // メニューの場所の中身。mode は titleMode（ランダムマッチの待ちは 'MATCHING'）。live は今出ている側か（消えていく側は false）
  const renderPanel = (mode, live) => {
    if (mode === 'MATCHING') return matchmaking;
    if (mode === 'DEFAULT') return (
      <div className="title-menu">
        <InkButton variant="shu" onClick={startCrafting} disabled={swordCount >= SWORD_MAX}>剣を錬成する</InkButton>
        <InkButton onClick={() => goToCrafting("TITLE")} disabled={swordCount === 0}>武器庫を開く</InkButton>
        {/* 所持数。0本のときも出し、押せない「武器庫を開く」と同じ薄い色にする。3本そろうと新しく作れないので、そのことも書く */}
        <p className={`title-count ${swordCount === 0 ? 'is-empty' : ''}`}>
          所持 <b>{swordCount}</b> / {SWORD_MAX}{swordCount >= SWORD_MAX && '（これ以上は作れません）'}
        </p>

        <div className="brush-rule" style={{ margin: '10px 0' }}></div>

        <p style={{ color: 'var(--usuzumi)', fontSize: '14px', margin: '0 0 -10px 0' }}>
          {mySwordData ? "2〜4人で対戦できます" : "対戦するには、先に剣を錬成してください"}
        </p>

        <InkButton onClick={openRandomMatch} disabled={!mySwordData || connecting}>ランダムマッチ</InkButton>

        <InkButton onClick={handleCreateRoom} disabled={!mySwordData || connecting}>ロビーを作成</InkButton>

        <InkButton onClick={handleJoinRoom} disabled={!mySwordData || connecting}>ロビーに入る</InkButton>
      </div>
    );
    if (mode === 'MATCH_SIZE') return (
      // ランダムマッチの人数選び。ルールと人数はロビーのルール設定と同じ墨札で選び、マッチング開始で探し始める
      <div className="title-panel title-match">
        <p className="title-panel__title">ランダムマッチ</p>
        <p className="title-panel__sub">見知らぬ相手と対戦します</p>
        <div className="title-choices">
          <div className="title-choices__group">
            <b>希望ルール</b>
            <div className="title-choices__row">
              {[["0", "剣"], ["1", "独楽"]].map(([value, label]) => (
                <button key={value} type="button" aria-pressed={matchMode === value}
                  className={`fuda-btn ${matchMode === value ? 'fuda-btn--on' : 'fuda-btn--off'}`}
                  onClick={() => setMatchMode(value)}>{label}</button>
              ))}
            </div>
          </div>
          <div className="title-choices__group">
            <b>希望人数</b>
            <div className="title-choices__row">
              {[2, 4].map(size => (
                <button key={size} type="button" aria-pressed={matchSize === size}
                  className={`fuda-btn ${matchSize === size ? 'fuda-btn--on' : 'fuda-btn--off'}`}
                  onClick={() => setMatchSize(size)}>{size}人</button>
              ))}
            </div>
          </div>
        </div>
        <p className="title-panel__note">対戦相手の希望ルールが優先される場合があります</p>
        <div className="title-panel__go forge-primary is-plain">
          <InkButton variant="shu" lit onClick={() => startRandomMatch(matchSize)}>マッチング開始</InkButton>
        </div>
        <div className="title-panel__back">
          <InkButton variant="usuzumi" onClick={handleCancelJoin}>戻る</InkButton>
        </div>
      </div>
    );
    return (
      // ロビーに入る：6桁のIDを1桁ずつの枠に入れる（実際の入力欄は枠の上に透明に重ねた1つ）。6桁そろったら入室する。
      // 入室ボタンは置かない（失敗したときは、1桁消して入れ直すか、Enter で試し直す）
      <div className="title-panel title-join">
        <p className="title-panel__title">ロビーに入る</p>
        <p className="title-panel__sub">ロビー作成者から6桁のIDを教えてもらい、入力してください</p>
        <label className="title-digits">
          <input
            type="text"
            // 「ロビーに入る」を選んだら、すぐ打ち始められるようにする（消えていく側では取らない）。入室の処理中は書き換えさせない
            autoFocus={live}
            readOnly={connecting}
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            maxLength={ID_LENGTH}
            value={targetId}
            aria-label="ロビーID（6桁の半角数字）"
            onChange={e => setTargetId(e.target.value.replace(/[^0-9]/g, '').slice(0, ID_LENGTH))}
            onKeyDown={e => { if (e.key === 'Enter' && targetId.length === ID_LENGTH) connectToHost(); }}
          />
          {Array.from({ length: ID_LENGTH }, (_, i) => (
            <span key={i} aria-hidden="true"
              className={`title-digits__box ${i === Math.min(targetId.length, ID_LENGTH - 1) ? 'is-cur' : ''} ${i < targetId.length ? 'is-filled' : ''}`}>
              {targetId[i] ?? ''}
            </span>
          ))}
        </label>
        <div className="title-panel__back">
          <InkButton variant="usuzumi" onClick={handleCancelJoin} disabled={connecting}>戻る</InkButton>
        </div>
      </div>
    );
  };

  // ロゴとメニューは置き場所を分ける。メニューの中身（ランダムマッチの待ち表示・ロビーIDの入力など）が変わっても、
  // ロゴは動かず、メニューの場所だけが変わる。並びは TitleScreen.css：横長は16:9の舞台の左にロゴ・右にメニュー、
  // 縦長は上にロゴ・その下にメニュー（「遊び方」は右上に固定）
  const screen = (
    <div className={`title ${portrait ? 'title--port' : 'title--land'} ${animClass}`}>
      <button className="howto-open-btn" onClick={onOpenHowToPlay}>遊び方</button>

      {backgroundSrc && <img className="title-bg" src={backgroundSrc} alt="" />}

      <div className="title-logo-area">
        <img className="title-logo" src="/logo.png" alt="オレブレード" />
      </div>

      <div className="title-side" ref={sideRef}>
        {/* 縦長で入りきらないときは、この中身ごと縮める（useFitMenu） */}
        <div className="title-fit" ref={fitRef}>
        {/* 中身が変わるときは、前の中身を消しながら次の中身を出す（クロスフェード）。消えていく側は押せない */}
        <div className="title-swap">
          {exiting && exiting !== 'MATCHING' && (
            <div key={`out-${exiting}`} ref={outRef} className="title-swap__layer is-out" aria-hidden="true" inert>
              {renderPanel(exiting, false)}
            </div>
          )}
          <div key={panelKey} className={`title-swap__layer ${exiting ? 'is-in' : ''}`}>
            {renderPanel(panelKey, true)}
          </div>
        </div>

        {/* ロビーの作成・入室の処理中（ランダムマッチの探索中は、マッチングの表示が出ているので出さない） */}
        {connecting && !matchmaking && (
          <BusySheet title="通信中">
            {titleMode === "JOIN_INPUT" ? "ロビーに接続しています" : "ロビーを作成しています"}
          </BusySheet>
        )}

        {/* メッセージの場所はいつも取っておく（出たときにメニューが動かないように） */}
        {!matchmaking && (
          <div className="title-message" aria-live="polite">
            {systemMessage && <div className="title-message__text">{systemMessage}</div>}
          </div>
        )}
        </div>
      </div>
    </div>
  );

  return portrait ? screen : <Stage16x9>{screen}</Stage16x9>;
}