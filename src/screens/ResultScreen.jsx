// src/screens/ResultScreen.jsx
import React from 'react';
import { styles } from '../styles';

export default function ResultScreen({
  matchResult,
  sendMessage,
  setIsReady,
  setIsEnemyUnityLoaded,
  connRef,
  setStep,
  handleLeave
}) {
  return (
    <div style={styles.container}>
      {matchResult.winnerImageSrc && (
        <img 
          src={matchResult.winnerImageSrc} 
          alt="Winner Background" 
          style={styles.bgImageCenter} 
        />
      )}
      
      <div style={styles.contentWrapper}>
        <h2 style={{ 
          fontSize: 'clamp(40px, 10vw, 70px)', 
          fontWeight: '900', 
          fontStyle: 'italic', 
          margin: '0 0 20px 0', 
          color: matchResult.iWon ? '#d32f2f' : '#1976d2', 
          textShadow: '2px 2px 0px #fff, -2px -2px 0px #fff, 2px -2px 0px #fff, -2px 2px 0px #fff, 4px 4px 10px rgba(0,0,0,0.3)' 
        }}>
          {matchResult.iWon ? "YOU WIN!!" : "YOU LOSE..."}
        </h2>

        <div style={{ backgroundColor: 'rgba(255, 255, 255, 0.9)', padding: '40px', borderRadius: '15px', boxShadow: '0 8px 20px rgba(0,0,0,0.2)', border: `4px solid ${matchResult.iWon ? '#d32f2f' : '#1976d2'}` }}>
          <h3 style={{ fontSize: 'clamp(20px, 5vw, 32px)', color: '#333', margin: '0 0 25px 0' }}>勝者: {matchResult.winnerName}</h3>
          <p style={{ fontSize: '20px', margin: '10px 0' }}>与えたダメージ: <strong>{matchResult.damageDealt}</strong></p>
          <p style={{ fontSize: '20px', margin: '10px 0' }}>受けたダメージ: <strong>{matchResult.damageTaken}</strong></p>
        </div>
        
        <div style={{ marginTop: '40px', display: 'flex', gap: '5%', width: '100%', maxWidth: '500px' }}>
          
          {/* ロビーに戻るボタン */}
          <div className="ink-btn-container" style={{ flex: 1 }}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#4CAF50' }}
              onClick={() => {
                try { sendMessage('GameManager', 'ResetMatch', ''); } catch(e) {}
                setIsReady(false);
                setIsEnemyUnityLoaded(false);
                if (connRef.current) connRef.current.send({ type: "SYNC_STATE", isReady: false });
                setStep("LOBBY");
              }}
            >
              ロビーに戻る
            </button>
          </div>

          {/* 退出するボタン */}
          <div className="ink-btn-container" style={{ flex: 1 }}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button 
              className="sharp-button"
              style={{ '--btn-color': '#666666' }}
              onClick={handleLeave}
            >
              退出する
            </button>
          </div>

        </div>
      </div>
    </div>
  );
}