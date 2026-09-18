import React, { useState } from 'react';
import { styles } from '../styles';
import { HILT_DATABASE } from './SwordListScreen';

export function NameInputScreen({ direction = "forward", userName, setUserName, setStep, handleCancel }) {
  const [transition, setTransition] = useState("enter");

  const onCancel = () => {
    if (transition !== "enter") return;
    setTransition("exit-back");
    setTimeout(handleCancel, 300);
  };

  const onNext = () => {
    if (transition !== "enter" || !userName.trim()) return;
    setTransition("exit-forward");
    setTimeout(() => setStep("CRAFT_POSE"), 300);
  };

  const animClass = transition === "exit-back" ? "page-exit-back" :
                    transition === "exit-forward" ? "page-exit-forward" : 
                    (direction === "back" ? "page-enter-back" : "page-enter-forward");

  return (
    // 🌟追加：横スクロールバー発生を防ぐため overflowX: 'hidden' を設定
    <div style={{ ...styles.container, overflowX: 'hidden' }} className={animClass}>
      <div style={styles.contentWrapper}>
        <h2>名前の入力</h2>
        <p style={{ color: '#555', marginBottom: '20px' }}>あなたの名前を教えてください</p>
        <input
          type="text"
          value={userName}
          onChange={(e) => setUserName(e.target.value)}
          placeholder="名前を入力"
          maxLength={10}
          style={styles.input}
        />

        <div style={{ marginTop: '30px', display: 'flex', flexDirection: 'column', gap: '15px', width: '100%', maxWidth: '300px', margin: '30px auto 0' }}>
          
          <div className={`ink-btn-container ${!userName.trim() ? 'disabled' : ''}`} style={{ width: '100%' }}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              style={{ '--btn-color': '#4CAF50' }}
              className="sharp-button"
              onClick={onNext}
              disabled={!userName.trim()}
            >
              ポーズを撮影する
            </button>
          </div>

          <div className="ink-btn-container" style={{ width: '100%' }}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button className="sharp-button" style={{ '--btn-color': '#666' }} onClick={onCancel}>
              キャンセル
            </button>
          </div>

        </div>
      </div>
    </div>
  );
}

// 🌟追加：forceCapture プロパティを受け取る
export function CraftPoseScreen({ direction = "forward", videoRef, canvasRef, captureCountdown, startCaptureCountdown, forceCapture, handleBack }) {
  const [transition, setTransition] = useState("enter");

  const onBack = () => {
    if (transition !== "enter") return;
    setTransition("exit-back");
    setTimeout(handleBack, 300);
  };

  // 親から渡された direction に応じてフェードイン方向を変える
  const animClass = transition === "exit-back" ? "page-exit-back" : 
                    (direction === "back" ? "page-enter-back" : "page-enter-forward");

  return (
    <div style={{ ...styles.container, overflowX: 'hidden' }} className={animClass}>
      <div style={styles.contentWrapper}>
        <h2>ポーズ撮影</h2>
        
        <div style={{ 
          position: 'relative', width: '100%', maxWidth: '400px', aspectRatio: '4/3', 
          backgroundColor: '#111', marginBottom: '20px', borderRadius: '8px', 
          display: 'flex', justifyContent: 'center', alignItems: 'center', overflow: 'hidden' 
        }}>
          {/* 🌟追加：transform: 'scaleX(-1)' を付けて左右反転 */}
          <video ref={videoRef} autoPlay playsInline style={{ width: '100%', height: '100%', objectFit: 'cover', transform: 'scaleX(-1)' }} />
          {captureCountdown !== null && (
            <div style={styles.countdownOverlay}>
              {captureCountdown > 0 ? captureCountdown : "📸"}
            </div>
          )}
        </div>

        <canvas ref={canvasRef} width="640" height="480" style={{ display: 'none' }} />
        
        <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '300px' }}>
          <div className={`ink-btn-container ${captureCountdown !== null ? 'disabled' : ''}`}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#ff9800' }}
              onClick={startCaptureCountdown} 
              disabled={captureCountdown !== null}
            >
              {captureCountdown !== null ? "ポーズをとれ！" : "撮影する！"}
            </button>
          </div>

          {/* 🌟追加：「今すぐ撮影！」ボタン */}
          {captureCountdown !== null && captureCountdown > 0 && (
            <div style={{ display: 'flex', justifyContent: 'center', margin: '-5px 0' }}>
              <button 
                onClick={forceCapture}
                style={{ 
                  padding: '8px 24px', backgroundColor: '#e91e63', color: '#fff', 
                  border: 'none', borderRadius: '25px', fontWeight: 'bold', fontSize: '14px',
                  cursor: 'pointer', boxShadow: '0 4px 6px rgba(0,0,0,0.3)',
                  animation: 'pulsePulse 1s infinite'
                }}
              >
                ⏩ 今すぐ撮影！
              </button>
              <style>{`
                @keyframes pulsePulse {
                  0% { transform: scale(1); }
                  50% { transform: scale(1.05); }
                  100% { transform: scale(1); }
                }
              `}</style>
            </div>
          )}

          <div className={`ink-btn-container ${captureCountdown !== null ? 'disabled' : ''}`}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#666666' }}
              onClick={onBack}
              disabled={captureCountdown !== null}
            >
              {captureCountdown !== null ? "" : "戻る"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export function CraftingApiScreen({ capturedImage }) {
  return (
    <div style={{ ...styles.container, overflowX: 'hidden' }} className="page-enter-forward">
      <div style={styles.contentWrapper}>
        <h2 style={{ fontFamily: "'Kurobara Gothic', sans-serif", letterSpacing: '0.1em' }}>錬成中...</h2>
        {capturedImage && (
          <div style={{ marginBottom: '20px', borderRadius: '0', overflow: 'hidden',  width: '320px' }}>
            <img src={capturedImage} alt="Captured Pose" style={{ width: '100%', display: 'block' }} />
          </div>
        )}
        <div style={{ margin: '20px 0', fontSize: '60px', animation: 'spin 3s linear infinite' }}>⚙️</div>
        <p style={{ marginTop: '50px', fontSize: '24px', fontWeight: 'bold', color: '#000', fontFamily: "'Kurobara Gothic', sans-serif", letterSpacing: '0.05em' }}>
          剣を錬成中...
        </p>
        <style>{`@keyframes spin { 100% { transform: rotate(360deg); } }`}</style>
      </div>
    </div>
  );
}

export function CraftCompleteScreen({ mySwordData, setStep, startNewCrafting, craftReturnStep, swordListLength, isRecapture }) {
  const [transition, setTransition] = useState("enter");

  const onReturnTitle = () => {
    if (transition !== "enter") return;
    setTransition("exit-back");
    setTimeout(() => setStep(craftReturnStep), 300);
  };

  const onGoArmory = () => {
    if (transition !== "enter") return;
    setTransition("exit-forward"); 
    setTimeout(() => setStep("SWORD_LIST"), 300);
  };

  const onCraftAnother = () => {
    if (transition !== "enter") return;
    setTransition("exit-forward"); 
    setTimeout(startNewCrafting, 300);
  };

  const animClass = transition === "exit-back" ? "page-exit-back" :
                    transition === "exit-forward" ? "page-exit-forward" : "page-enter-forward";

  const currentHilt = mySwordData ? (HILT_DATABASE[mySwordData.hiltType] || HILT_DATABASE["default"]) : null;

  return (
    <div style={{ ...styles.container, overflowX: 'hidden' }} className={animClass}>
      <div style={styles.contentWrapper}>
        <h1 style={{ fontSize: '48px', color: '#000', margin: '20px 0', letterSpacing: '0.05em', fontFamily: "'Kurobara Gothic', sans-serif" }}>錬成完了！</h1>
        
        {mySwordData && (
          <div style={{ margin: '20px 0', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <div style={styles.swordCard}>
              {mySwordData.imageSrc ? (
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', minHeight: '200px' }}>
                  <img src={mySwordData.imageSrc} alt="Blade" style={{ width: '150px', height: 'auto', zIndex: 1 }} />
                  {currentHilt && (
                    <img src={currentHilt.imageSrc} alt={currentHilt.name} style={{ width: '150px', height: 'auto', marginTop: '-40px', zIndex: 2 }} />
                  )}
                </div>
              ) : (
                <div style={styles.previewImage}>画像受信中...</div>
              )}
              <p style={{ ...styles.swordName, color: '#000', marginTop: '10px' }}>{mySwordData.name}</p>
              <div style={styles.statsBox}>HP:{mySwordData.hp} 攻撃:{mySwordData.attack} 重さ:{mySwordData.weight}</div>
            </div>
          </div>
        )}
        
        <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '300px', marginTop: '10px' }}>
          
          <div className="ink-btn-container">
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#FF9800' }}
              onClick={onReturnTitle}
            >
              {craftReturnStep === "TITLE" ? "タイトルに戻って対戦だ！" : "ロビーに戻って対戦だ！"}
            </button>
          </div>

          <div className="ink-btn-container">
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#4CAF50' }}
              onClick={onGoArmory}
            >
              武器庫（一覧）へ進む
            </button>
          </div>

          {!isRecapture && swordListLength < 3 && (
            <div className="ink-btn-container">
              <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
              <button 
                className="sharp-button"
                style={{ '--btn-color': '#000' }}
                onClick={onCraftAnother}
              >
                続けてもう1本錬成する
              </button>
            </div>
          )}

        </div>
      </div>
    </div>
  );
}