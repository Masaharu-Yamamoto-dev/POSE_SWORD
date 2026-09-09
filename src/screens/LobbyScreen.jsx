// src/screens/LobbyScreen.jsx
import React from 'react';
import { styles } from '../styles';

export default function LobbyScreen({
  role,
  mySwordData,
  enemySwordData,
  connection,
  myPeerId,
  isCopied,
  handleCopyId,
  gameMode,
  setGameMode,
  countdown,
  isReady,
  setIsReady,
  isEnemyReady,
  handleReady,
  goToCrafting,
  handleLeave
}) {
  
  // プレイヤーのカードを描画する関数
  const renderPlayerSide = (targetRole) => {
    const isMine = targetRole === role;
    const data = isMine ? mySwordData : enemySwordData;

    return (
      <div style={{ width: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        <div style={styles.swordCard}>
          <h3 style={{ margin: '0 0 10px 0', color: '#000' }}>
            {isMine ? "あなた" : "対戦相手"}
          </h3>
          {data ? (
            <>
              {data.imageSrc ? <img src={data.imageSrc} style={styles.previewImage} alt="Sword" /> : <div style={styles.previewImage}>画像受信中...</div>}
              <p style={{ ...styles.swordName, color: '#000' }}>{data.name}</p>
              <div style={styles.statsBox}>HP:{data.hp} 攻撃:{data.attack} 重さ:{data.weight}</div>
            </>
          ) : <div style={{ height: '200px', display: 'flex', alignItems: 'center' }}>{isMine ? "未錬成" : "待機中..."}</div>}
        </div>

        <div style={{ height: '80px', marginTop: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', width: '100%' }}>
          {isMine && (
            <div className="ink-btn-container" style={{ width: '100%' }}>
              <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
              <button 
                className="sharp-button" 
                style={{ '--btn-color': '#000', fontSize: 'clamp(14px, 4vw, 18px)', padding: '15px 10px' }} 
                onClick={() => goToCrafting("LOBBY")}
              >
                ⚔️ 剣を再錬成する
              </button>
            </div>
          )}
        </div>
      </div>
    );
  };

  return (
    <div style={styles.container}>
      <div style={styles.contentWrapper}>
        <h2>ロビー（対戦準備）</h2>
        
        {/* ホスト専用のID表示エリア */}
        {role === "HOST" && (
          <div style={{ width: '100%', maxWidth: '800px', marginBottom: '20px', padding: '15px', backgroundColor: '#e3f2fd', borderRadius: '8px', border: '1px solid #90caf9', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '15px', flexWrap: 'wrap', boxSizing: 'border-box' }}>
            <p style={{ margin: 0, fontWeight: 'bold', color: '#333' }}>
              {connection ? "通信相手と接続済み" : "このIDを相手に入力させてください 👉"}
            </p>
            <span style={{ fontSize: '32px', fontWeight: 'bold', color: 'blue', letterSpacing: '4px' }}>
              {myPeerId || "取得中..."}
            </span>
            {myPeerId && (
              <button
                style={{ padding: '8px 12px', fontSize: '14px', backgroundColor: isCopied ? '#4CAF50' : '#e0e0e0', color: isCopied ? '#fff' : '#333', border: 'none', cursor: 'pointer', fontWeight: 'bold', boxShadow: '0 2px 4px rgba(0,0,0,0.1)' }}
                onClick={handleCopyId}
              >
                {isCopied ? "✓ コピー済" : "📋 コピー"}
              </button>
            )}
          </div>
        )}

        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '2%', width: '100%', maxWidth: '800px', margin: '10px 0 20px 0' }}>
          <div style={{ width: '45%' }}>{renderPlayerSide("HOST")}</div>
          <div style={{ width: '10%', textAlign: 'center' }}><span style={styles.vsText}>VS</span></div>
          <div style={{ width: '45%' }}>{renderPlayerSide("CLIENT")}</div>
        </div>

        <div style={styles.connectedBox}>
          <div style={styles.modeBox}>
            <h3 style={{ margin: '0 0 10px 0', color: '#333' }}>バトルモード</h3>
            {role === "HOST" ? (
              <select value={gameMode} onChange={(e) => { setGameMode(e.target.value); if(connection) connection.send({ type: "SYNC_GAMEMODE", gameMode: e.target.value }); }} 
                style={{ padding: '8px', fontSize: '16px', borderRadius: '0' }}>
                <option value="1">🌀 独楽（見下ろし）モード</option>
                <option value="0">⚔️ 剣（横視点・重力）モード</option>
              </select>
            ) : (
              <div style={{ fontSize: '18px', fontWeight: 'bold' }}>
                {gameMode === "1" ? "🌀 独楽（見下ろし）モード" : "⚔️ 剣（横視点・重力）モード"}
              </div>
            )}
          </div>

          {countdown !== null ? (
            <h2 style={{ fontSize: '48px', color: 'red', animation: 'pulse 1s infinite' }}>{countdown > 0 ? countdown : "START!"}</h2>
          ) : (
            <div>
              <div style={{ display: 'flex', justifyContent: 'center', gap: '20px', margin: '10px 0 20px 0', flexWrap: 'wrap' }}>
                {role === "HOST" ? (
                  <>
                    <div style={styles.readyBox(isReady)}>自分: {isReady ? "準備OK!" : "準備中..."}</div>
                    <div style={styles.readyBox(isEnemyReady)}>相手: {isEnemyReady ? "準備OK!" : "準備中..."}</div>
                  </>
                ) : (
                  <>
                    <div style={styles.readyBox(isEnemyReady)}>相手: {isEnemyReady ? "準備OK!" : "準備中..."}</div>
                    <div style={styles.readyBox(isReady)}>自分: {isReady ? "準備OK!" : "準備中..."}</div>
                  </>
                )}
              </div>
              
              <div style={{ display: 'flex', justifyContent: 'center', gap: '15px' }}>
                {!isReady ? (
                  <button
                    style={{ ...styles.button, backgroundColor: (mySwordData && enemySwordData) ? '#4CAF50' : 'gray', color: 'white' }}
                    onClick={handleReady}
                    disabled={!mySwordData || !enemySwordData}
                  >
                    {mySwordData && enemySwordData ? "準備完了（バトルへ）" : "剣のデータが不足しています"}
                  </button>
                ) : (
                  <button
                    style={{ ...styles.button, backgroundColor: countdown !== null ? 'gray' : '#f44336', color: 'white', cursor: countdown !== null ? 'not-allowed' : 'pointer' }}
                    onClick={() => { setIsReady(false); if(connection) connection.send({ type: "SYNC_STATE", isReady: false }); }}
                    disabled={countdown !== null} 
                  >
                    準備を取り消す
                  </button>
                )}
              </div>
            </div>
          )}
        </div>
        
        <div className="ink-btn-container" style={{ marginTop: '30px', width: '100%', maxWidth: '300px' }}>
          <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
          <button className="sharp-button" style={{ '--btn-color': '#000' }} onClick={handleLeave}>退出する</button>
        </div>
      </div>
      <style>{`@keyframes pulse { 0% { transform: scale(1); } 50% { transform: scale(1.1); } 100% { transform: scale(1); } }`}</style>
    </div>
  );
}