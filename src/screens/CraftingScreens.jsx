import { useState } from 'react';
import InkButton from '../components/InkButton.jsx';
import { EnsoInk } from '../components/SwordForgeParts.jsx';
import ForgeHammer3D from '../components/ForgeHammer3D.jsx';
import '../components/SwordForgeParts.css';
import '../components/Stage16x9.css';
import { usePortrait, useStageScale } from '../components/swordForge.js';
import './CraftingScreens.css';

// カメラ枠の縦横比をCSSへ渡す（枠の大きさと並びは CraftingScreens.css）
const cameraFrameVars = (aspect = 4 / 3) => ({ '--cam-aspect': aspect.toFixed(4) });

// 名前は、剣の名前の前後に付く言葉（前に最大2文字「紅蓮」など、後ろに最大4文字「ブレード」など）の分だけ短くする。
// 剣の名前の上限（15文字）を超えないように
const USER_NAME_MAX = 15 - 2 - 4;

// ==========================================
// 作成の画面の枠（名前の入力・ポーズ撮影・錬成中で共通）
// ==========================================
// 横長は16:9の舞台に収め、縦長は画面いっぱい。上に武器庫と同じ見出しの帯（見出し・手順）を置く。
// 向きを変えても中の要素（カメラの映像）を作り直さないよう、縦横で同じ入れ子の形にして、見た目だけを切り替える
function CraftFrame({ title, step, animClass = '', className = '', children }) {
  const portrait = usePortrait();
  const scale = useStageScale();
  return (
    <div className={portrait ? 'craft-shell' : 'stage169'}>
      <div className={portrait ? 'craft-shell__inner' : 'stage169__canvas'} style={portrait ? undefined : { transform: `scale(${scale})` }}>
        <div className={`forge craft ${portrait ? 'craft--port' : 'craft--land'} ${className} ${animClass}`}>
          <header className="craft__header">
            <span />
            <h1>{title}</h1>
            {step ? <span className="craft__step">手順 <span>{step}</span> / 2</span> : <span />}
          </header>
          <div className="craft__body">{children}</div>
        </div>
      </div>
    </div>
  );
}

// 戻るときは、ほかの画面と同じく横へずれながら消えてから前の画面へ移る
function useLeave(direction) {
  const [leaving, setLeaving] = useState(false);
  const animClass = leaving ? 'page-exit-back' : direction === 'back' ? 'page-enter-back' : 'page-enter-forward';
  const leave = action => {
    if (leaving) return;
    setLeaving(true);
    setTimeout(action, 300);
  };
  return [animClass, leave, leaving];
}

// ズームの「引き」「寄り」の虫眼鏡（－と＋）
function ZoomIcon({ plus }) {
  return (
    <svg className="craft-zoom__icon" viewBox="0 0 24 24" role="img" aria-label={plus ? '寄り' : '引き'}>
      <circle cx="10.5" cy="10.5" r="6.5" />
      <path d={plus ? 'M7.5 10.5 H13.5 M10.5 7.5 V13.5 M15.3 15.3 L20 20' : 'M7.5 10.5 H13.5 M15.3 15.3 L20 20'} />
    </svg>
  );
}

// ==========================================
// 1. 名前入力画面
// ==========================================
// 入力欄は、錬成完了・武器庫の名前の書き換え欄と同じ形（朱の下線・右下に入れられる残りの文字数）
export function NameInputScreen({ direction = "forward", userName, setUserName, setStep, handleCancel }) {
  const [animClass, leave] = useLeave(direction);
  const count = Array.from(userName).length;
  const ready = userName.trim() !== '';
  const next = () => { if (ready) setStep("CRAFT_POSE"); };
  const onKeyDown = e => {
    if (e.nativeEvent.isComposing) return;
    if (e.key === 'Enter') next();
  };
  return (
    <CraftFrame title="名前の入力" step={1} animClass={animClass} className="craft--name">
      <div className="craft-name">
        <p className="craft-name__lead">あなたの名前を教えてください</p>
        <span className="name-edit__field craft-name__field">
          <input type="text" value={userName} aria-label="あなたの名前" placeholder="名前を入力" autoFocus
            onChange={e => setUserName(Array.from(e.target.value).slice(0, USER_NAME_MAX).join(''))} onKeyDown={onKeyDown} />
          <span className={`name-edit__count ${count >= USER_NAME_MAX ? 'is-full' : ''}`} aria-live="polite">{count} / {USER_NAME_MAX}</span>
        </span>
        <p className="craft-name__hint">名前は剣の名前に入ります（例：紅蓮〇〇ブレード）</p>
        <div className="craft-name__go">
          <div className="forge-primary is-plain">
            <InkButton variant="shu" lit onClick={next} disabled={!ready}>ポーズを撮影する</InkButton>
          </div>
          <InkButton variant="usuzumi" onClick={() => leave(handleCancel)}>戻る</InkButton>
        </div>
      </div>
    </CraftFrame>
  );
}

// ==========================================
// 2. 姿勢撮影画面
// ==========================================
// recapture：撮り直し（名前の入力を通らない）。手順の表示は出さない
export function CraftPoseScreen({ direction = "forward", recapture = false, camera, videoRef, canvasRef, captureCountdown, startCaptureCountdown, forceCapture, handleBack }) {
  const [animClass, leave, leaving] = useLeave(direction);
  const portrait = usePortrait();
  const counting = captureCountdown !== null;
  // カウントダウン：毎秒、完成画面と同じ円相を一筆で描き、描き終わると消えて次の数字へ（1 の次はそのまま撮影）。
  // 縦長はカメラ枠の上に重ね、横長は右の操作の空いた所（隠したカメラの操作の場所）に出す。
  // 数字ごとに作り直して、描く動きを毎秒はじめから流す
  const countdown = counting && captureCountdown > 0 && (
    <div className="craft-count" key={captureCountdown} aria-live="assertive">
      <EnsoInk variant="craft" className="craft-count__enso" />
      <span className="craft-count__num">{captureCountdown}</span>
    </div>
  );
  const zoom = camera.zoom;
  const zoomFill = zoom ? ((zoom.value - zoom.min) / (zoom.max - zoom.min || 1)) * 100 : 0;
  return (
    <CraftFrame title="ポーズ撮影" step={recapture ? null : 2} animClass={animClass} className="craft--pose">
      <div className="craft-split">
        <div className="craft-camera" style={cameraFrameVars(camera.aspect)}>
          <video ref={videoRef} autoPlay playsInline style={{ width: '100%', height: '100%', objectFit: 'cover', transform: camera.mirrored ? 'scaleX(-1)' : 'none' }} />
          {portrait && countdown}
        </div>
        <canvas ref={canvasRef} width="640" height="480" style={{ display: 'none' }} />

        <div className={`craft-side ${counting ? 'is-counting' : ''}`}>
          {/* カメラの操作：どの端末でも両方出し、使えない機能は押せない状態にする
              （ズームできないカメラ・カメラが1台だけの端末）。
              カウントダウン中は隠す。場所は取ったままにして、下のボタンが動かないようにする */}
          {!portrait && countdown}
          <div className="craft-camera-controls" style={{ visibility: counting ? 'hidden' : 'visible' }}>
            <label className={`craft-zoom ${zoom ? '' : 'is-off'}`}>
              <ZoomIcon plus={false} />
              {/* つまみは、ワンポイントの大きさと同じ筆跡の通り道 */}
              <span className="privacy__track" style={{ '--fill': `${zoomFill.toFixed(1)}%` }}>
                <input
                  type="range"
                  aria-label="ズーム"
                  min={zoom?.min ?? 1}
                  max={zoom?.max ?? 2}
                  step={zoom?.step ?? 0.1}
                  value={zoom?.value ?? 1}
                  onChange={(e) => camera.changeZoom(Number(e.target.value))}
                  disabled={!zoom}
                />
              </span>
              <ZoomIcon plus />
            </label>
            <button type="button" className="forge-link craft-switch" onClick={camera.switchCamera} disabled={!camera.canSwitch}>カメラを切り替える</button>
            {camera.label && <p className="craft-camera-label">{camera.label}</p>}
          </div>

          <div className="craft-buttons">
            <div className="forge-primary is-plain">
              <InkButton variant="shu" lit onClick={startCaptureCountdown} disabled={counting}>{counting ? "ポーズをとれ！" : "撮影する！"}</InkButton>
            </div>
            {counting && captureCountdown > 0 && (
              // 今すぐ撮る：カメラのシャッターの形を、筆で描いた墨の輪と朱の芯で作る（芯は1秒ごとに縮む）
              <button type="button" className="craft-now" onClick={forceCapture}>
                <svg viewBox="0 0 80 80" aria-hidden="true">
                  <circle className="craft-now__ring" cx="40" cy="40" r="34" />
                  <circle className="craft-now__core" cx="40" cy="40" r="22" />
                </svg>
                <span>今すぐ撮影</span>
              </button>
            )}
            <InkButton variant="usuzumi" onClick={() => leave(handleBack)} disabled={counting || leaving}>{counting ? "" : "戻る"}</InkButton>
          </div>
        </div>
      </div>
    </CraftFrame>
  );
}

// ==========================================
// 3. 錬成中画面
// ==========================================
// leaving：錬成が終わったら、動かさずにその場でフェードアウトする（消えきったら錬成完了の画面へ移る）
export function CraftingApiScreen({ capturedImage, aspect, leaving = false }) {
  return (
    <CraftFrame title="錬成中" animClass={leaving ? 'craft-fade-out' : ''}>
      <div className="craft-split">
        {/* 🌟 撮影時と全く同じサイズの枠 */}
        <div className="craft-camera" style={cameraFrameVars(aspect)}>
          {capturedImage && (
            <img src={capturedImage} alt="Captured Pose" style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
          )}
        </div>

        <div className="craft-side craft-side--wait">
          <ForgeHammer3D imageSrc={capturedImage} aspect={aspect} />
          <p className="craft-wait-text">剣を錬成中<i>.</i><i>.</i><i>.</i></p>
          <p className="craft-wait-note">※剣の錬成には時間がかかる場合があります</p>
        </div>
      </div>
    </CraftFrame>
  );
}
