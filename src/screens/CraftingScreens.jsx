// src/screens/CraftingScreens.jsx
import React from 'react';
import { styles } from '../styles';

export function NameInputScreen({ userName, setUserName, setStep, handleCancel }) {
  return (
    <div style={styles.container}>
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

        <div style={{ marginTop: '30px', display: 'flex', gap: '4%', width: '100%', maxWidth: '400px' }}>
          <div className="ink-btn-container" style={{ flex: 1 }}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            {}
            <button className="sharp-button" onClick={handleCancel}>
              キャンセル
            </button>
          </div>

          <div className={`ink-btn-container ${!userName.trim() ? 'disabled' : ''}`} style={{ flex: 1 }}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              style={{ '--btn-color': '#4CAF50' }}
              className="sharp-button"
              onClick={() => setStep("CRAFT_POSE")}
              disabled={!userName.trim()}
            >
              ポーズを撮影する
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export function CraftPoseScreen({ videoRef, canvasRef, captureCountdown, startCaptureCountdown, handleBack }) {
  return (
    <div style={styles.container}>
      <div style={styles.contentWrapper}>
        <h2>ポーズ撮影</h2>
        <div style={{ position: 'relative', width: '400px', marginBottom: '20px' }}>
          <video ref={videoRef} autoPlay playsInline style={styles.video} />
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

          <div className={`ink-btn-container ${captureCountdown !== null ? 'disabled' : ''}`}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            {}
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#666666' }}
              onClick={handleBack}
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
    <div style={styles.container}>
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

export function CraftCompleteScreen({ mySwordData, setStep, startNewCrafting, craftReturnStep }) {
  return (
    <div style={styles.container}>
      <div style={styles.contentWrapper}>
        <h1 style={{ fontSize: '48px', color: '#000', margin: '20px 0', letterSpacing: '0.05em', fontFamily: "'Kurobara Gothic', sans-serif" }}>錬成完了！</h1>
        
        {mySwordData && (
          <div style={{ margin: '20px 0', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <div style={styles.swordCard}>
              {mySwordData.imageSrc ? (
                <img src={mySwordData.imageSrc} alt="My Sword" style={styles.previewImage} />
              ) : (
                <div style={styles.previewImage}>画像受信中...</div>
              )}
              <p style={{ ...styles.swordName, color: '#000' }}>{mySwordData.name}</p>
              <div style={styles.statsBox}>HP:{mySwordData.hp} 攻撃:{mySwordData.attack} 重さ:{mySwordData.weight}</div>
            </div>
          </div>
        )}
        
        <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '300px', marginTop: '10px' }}>
          
          {/* ▼ 追加：タイトル or ロビーに戻ってすぐ対戦するボタン（一番上） */}
          <div className="ink-btn-container">
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#FF9800' }} // 目立つオレンジ色
              onClick={() => setStep(craftReturnStep)}
            >
              {craftReturnStep === "TITLE" ? "タイトルに戻って対戦だ！" : "ロビーに戻って対戦だ！"}
            </button>
          </div>

          <div className="ink-btn-container">
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#4CAF50' }}
              onClick={() => setStep("SWORD_LIST")}
            >
              武器庫（一覧）へ進む
            </button>
          </div>

          <div className="ink-btn-container">
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#000' }}
              onClick={startNewCrafting}
            >
              続けてもう1本錬成する
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}