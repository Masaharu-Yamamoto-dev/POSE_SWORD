import React, { useState, useEffect } from 'react';
import { styles } from '../styles';
import { HILT_DATABASE } from './SwordListScreen';

// 🌟 共通：撮影画面と錬成中画面のサイズを完全に一致させるレスポンシブコンテナ
const COMMON_CAMERA_STYLE = {
  position: 'relative', 
  width: '100%', 
  maxWidth: '400px', 
  aspectRatio: '4/3', 
  backgroundColor: '#111', 
  marginBottom: '20px', 
  borderRadius: '8px', 
  display: 'flex', 
  justifyContent: 'center', 
  alignItems: 'center', 
  overflow: 'hidden',
  margin: '0 auto'
};

// ==========================================
// 1. 名前入力画面
// ==========================================
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

// ==========================================
// 2. 姿勢撮影画面
// ==========================================
export function CraftPoseScreen({ direction = "forward", videoRef, canvasRef, captureCountdown, startCaptureCountdown, forceCapture, handleBack }) {
  const [transition, setTransition] = useState("enter");

  const onBack = () => {
    if (transition !== "enter") return;
    setTransition("exit-back");
    setTimeout(handleBack, 300);
  };

  const animClass = transition === "exit-back" ? "page-exit-back" : 
                    (direction === "back" ? "page-enter-back" : "page-enter-forward");

  return (
    <div style={{ ...styles.container, overflowX: 'hidden' }} className={animClass}>
      <div style={styles.contentWrapper}>
        <h2>ポーズ撮影</h2>
        
        {/* 🌟 共通サイズコンテナを適用 */}
        <div style={COMMON_CAMERA_STYLE}>
          <video ref={videoRef} autoPlay playsInline style={{ width: '100%', height: '100%', objectFit: 'cover', transform: 'scaleX(-1)' }} />
          {captureCountdown !== null && (
            <div style={styles.countdownOverlay}>
              {captureCountdown > 0 ? captureCountdown : "📸"}
            </div>
          )}
        </div>

        <canvas ref={canvasRef} width="640" height="480" style={{ display: 'none' }} />
        
        <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '300px', margin: '0 auto' }}>
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

// ==========================================
// 3. 錬成中画面
// ==========================================
export function CraftingApiScreen({ capturedImage }) {
  return (
    <div style={{ ...styles.container, overflowX: 'hidden' }}>
      <div style={styles.contentWrapper}>
        <h2 style={{ fontFamily: "'Kurobara Gothic', sans-serif", letterSpacing: '0.1em', marginBottom: '20px' }}>錬成中...</h2>
        
        {/* 🌟 撮影時と全く同じサイズの枠 */}
        <div style={COMMON_CAMERA_STYLE}>
          {capturedImage && (
            <img src={capturedImage} alt="Captured Pose" style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
          )}
        </div>

        <div style={{ margin: '20px 0', fontSize: '60px', animation: 'spin 3s linear infinite' }}>⚙️</div>
        
        <p style={{ marginTop: '50px', fontSize: '24px', fontWeight: 'bold', color: '#000', fontFamily: "'Kurobara Gothic', sans-serif", letterSpacing: '0.05em' }}>
          剣を錬成中...
        </p>
        
        <style>{`@keyframes spin { 100% { transform: rotate(360deg); } }`}</style>
      </div>
    </div>
  );
}

// ==========================================
// 4. 錬成完了画面
// ==========================================
export function CraftCompleteScreen({ mySwordData, setStep, startNewCrafting, craftReturnStep, swordListLength, isRecapture }) {
  const [transition, setTransition] = useState("enter");
  const [animPhase, setAnimPhase] = useState("INIT");
  // 🌟 クリック回転用State
  const [isSpinning, setIsSpinning] = useState(false);

  useEffect(() => {
    // 0.5秒後：柄が下から衝突
    const t1 = setTimeout(() => setAnimPhase("CLASH"), 500); 
    // 1.0秒後：衝突の瞬間にフラッシュ
    const t2 = setTimeout(() => setAnimPhase("FLASH"), 1000);
    // 1.1秒後：完成品の剣と既存UIを「ドンッ」と表示
    const t3 = setTimeout(() => setAnimPhase("COMPLETE"), 1100);

    return () => { clearTimeout(t1); clearTimeout(t2); clearTimeout(t3); };
  }, []);

  const handleSwordClick = () => {
    if (!isSpinning) {
      setIsSpinning(true);
    }
  };

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

  const currentHilt = mySwordData ? (HILT_DATABASE[mySwordData.hiltType] || HILT_DATABASE["0"]) : null;
  const exitClass = transition === "exit-back" ? "page-exit-back" : transition === "exit-forward" ? "page-exit-forward" : "";

  return (
    <div style={{ ...styles.container, overflowX: 'hidden' }} className={exitClass}>
      <div style={{ ...styles.contentWrapper, maxWidth: '900px' }}>
        
        {/* 🌟 衝突の瞬間の短い白フラッシュ */}
        {animPhase === 'FLASH' && (
          <div style={{ position: 'fixed', inset: 0, backgroundColor: '#fff', zIndex: 9999, pointerEvents: 'none' }} />
        )}

        {/* 🌟 演出途中の状態（シルエットと柄の衝突） */}
        {animPhase !== 'COMPLETE' && (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', minHeight: '60vh', position: 'relative' }}>
            <img
              src={mySwordData?.imageSrc}
              alt="Blade Silhouette"
              style={{ 
                height: '250px', width: 'auto', objectFit: 'contain', zIndex: 2, 
                filter: 'brightness(0)' // 黒シルエット化
              }}
            />
            {currentHilt && (
              <img
                src={currentHilt.imageSrc}
                alt="Hilt Silhouette"
                style={{
                  height: '200px', width: 'auto', objectFit: 'contain', marginTop: '-60px', zIndex: 1,
                  filter: 'brightness(0)', // 黒シルエット化
                  transform: animPhase === 'INIT' ? 'translateY(200px)' : 'translateY(0)',
                  opacity: animPhase === 'INIT' ? 0 : 1,
                  transition: animPhase === 'CLASH' ? 'transform 0.4s cubic-bezier(0.1, 0.9, 0.2, 1), opacity 0.2s' : 'none'
                }}
              />
            )}
          </div>
        )}

        {/* 🌟 演出完了後：UIを左右に分割（左：巨大剣、右：文字とボタン） */}
        {animPhase === 'COMPLETE' && (
          <div style={{ width: '100%', display: 'flex', flexWrap: 'wrap', justifyContent: 'center', alignItems: 'center', gap: '50px', marginTop: '20px' }}>
            
            {/* 左側：完成した剣（ふわふわ浮遊 ＆ クリックで回転） */}
            {mySwordData && (
              <div className="slam-left-anim" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', minHeight: '350px' }}>
                <div 
                  className={`floating-sword ${isSpinning ? "spin-active" : ""}`}
                  onClick={handleSwordClick}
                  onAnimationEnd={() => setIsSpinning(false)}
                  style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', cursor: 'pointer', transformOrigin: 'center center' }}
                  title="クリックで回転"
                >
                  {mySwordData.imageSrc ? (
                    <>
                      <img src={mySwordData.imageSrc} alt="Blade" style={{ width: '250px', height: 'auto', zIndex: 1 }} />
                      {currentHilt && (
                        <img src={currentHilt.imageSrc} alt={currentHilt.name} style={{ width: '220px', height: 'auto', marginTop: '-60px', zIndex: 2 }} />
                      )}
                    </>
                  ) : (
                    <div style={styles.previewImage}>画像受信中...</div>
                  )}
                </div>
              </div>
            )}
            
            {/* 右側：文字・名前・ステータス・ボタン群 */}
            <div className="pop-in-anim" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '320px' }}>
              {/* 🌟 離すために margin-bottom を 35px に拡大 */}
              <h1 style={{ fontSize: '48px', color: '#000', margin: '0 0 35px 0', letterSpacing: '0.05em', fontFamily: "'Kurobara Gothic', sans-serif" }}>
                錬成完了！
              </h1>
              
              {mySwordData && (
                <>
                  <p style={{ ...styles.swordName, color: '#000', marginTop: '0', fontSize: '28px' }}>
                    {mySwordData.name}
                  </p>
                  <div style={{ ...styles.statsBox, marginBottom: '25px', width: '100%', fontSize: '18px' }}>
                    HP:{mySwordData.hp} 攻撃:{mySwordData.attack} 重さ:{mySwordData.weight}
                  </div>
                </>
              )}
              
              <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '100%' }}>
                
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
        )}
      </div>

      {/* 🌀 演出＆アクション用CSS */}
      <style>{`
        @keyframes slamLeft {
          0% { transform: translateX(50px) scale(1.3); opacity: 0; }
          50% { transform: translateX(-10px) scale(0.95); opacity: 1; }
          100% { transform: translateX(0) scale(1); opacity: 1; }
        }
        @keyframes popIn {
          0% { transform: scale(0.8) translateY(20px); opacity: 0; }
          100% { transform: scale(1) translateY(0); opacity: 1; }
        }
        .slam-left-anim {
          animation: slamLeft 0.5s cubic-bezier(0.175, 0.885, 0.32, 1.275) forwards;
        }
        .pop-in-anim {
          animation: popIn 0.5s cubic-bezier(0.175, 0.885, 0.32, 1.275) forwards;
        }

        /* 🌟 武器庫と同様の浮遊＆回転アニメーション */
        @keyframes float {
          0%, 100% { transform: translateY(0); }
          50% { transform: translateY(-15px); }
        }
        .floating-sword {
          animation: float 3s ease-in-out infinite;
        }

        @keyframes spin {
          0% { transform: rotate(0deg); }
          100% { transform: rotate(360deg); }
        }
        .spin-active {
          animation: spin 0.6s cubic-bezier(0.4, 0, 0.2, 1);
        }
      `}</style>
    </div>
  );
}