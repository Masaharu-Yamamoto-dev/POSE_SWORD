// src/screens/TitleScreen.jsx
import React from 'react';
import { styles } from '../styles';

export default function TitleScreen({
  mySwordData,
  titleMode,          
  targetId,           
  setTargetId,        
  systemMessage,
  goToCrafting,
  handleCreateRoom,
  handleJoinRoom,
  handleCancelJoin,   
  connectToHost,
  connecting,
  openRandomMatch,
  startRandomMatch,
  matchMode,
  setMatchMode
}) {
  return (
    <div style={styles.container}>
      {mySwordData?.imageSrc && (
        <img src={mySwordData.imageSrc} alt="Background Sword" style={{ ...styles.bgImageCenter, transform: 'translate(-50%, -50%)' }} />
      )}
      
      <div style={styles.contentWrapper}>
        <img src="/logo.png" alt="オレブレード" style={{ width: '90%', maxWidth: '800px', marginBottom: '40px', objectFit: 'contain' }} />
        
        {/* ▼ titleMode によって表示を切り替え */}
        {titleMode === "DEFAULT" ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', width: '100%', maxWidth: '300px' }}>
            <div className="ink-btn-container">
              <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
              <button 
                className="sharp-button"
                style={{ '--btn-color': '#4CAF50' }}
                onClick={() => goToCrafting("TITLE")}
              >
                {mySwordData ? "⚔️ 剣を再錬成する" : "⚔️ 剣を錬成する"}
              </button>
            </div>
            
            <div style={{ borderTop: '2px solid #ddd', margin: '10px 0' }}></div>
            
            <p style={{ color: '#888', fontSize: '14px', margin: '0 0 -10px 0', fontWeight: 'bold' }}>
              {mySwordData ? "2〜4人で対戦できます" : "対戦するには、先に剣を錬成してください"}
            </p>

            <div className={`ink-btn-container ${!mySwordData ? 'disabled' : ''}`}>
              <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
              <button className="sharp-button" style={{ '--btn-color': '#d32f2f' }}
                onClick={openRandomMatch} disabled={!mySwordData || connecting}>
                ⚡ ランダムマッチ
              </button>
            </div>

            <div className={`ink-btn-container ${!mySwordData ? 'disabled' : ''}`}>
              <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
              <button className="sharp-button" onClick={handleCreateRoom} disabled={!mySwordData || connecting}>
                ロビーを作成
              </button>
            </div>

            <div className={`ink-btn-container ${!mySwordData ? 'disabled' : ''}`}>
              <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
              <button className="sharp-button" onClick={handleJoinRoom} disabled={!mySwordData || connecting}>
                ロビーに入る
              </button>
            </div>
          </div>
        ) : titleMode === "MATCH_SIZE" ? (
          /* ▼ ランダムマッチの人数選択 */
          <div className="glass" style={{ width: '100%', maxWidth: '400px', padding: '25px', boxSizing: 'border-box' }}>
            <p style={{ fontSize: '18px', fontWeight: 'bold', color: '#000', margin: '0 0 5px 0' }}>
              何人で戦いますか
            </p>
            <p style={{ fontSize: '13px', color: '#666', margin: '0 0 20px 0' }}>
              見知らぬ相手と自動で合流します。集まりしだい開始します。
            </p>

            {/* 自分が部屋を立てたときのルール。相手の部屋に入った場合はその部屋に従う。 */}
            <div style={{ display: 'flex', gap: '10px', marginBottom: '10px' }}>
              {[["0", "🗡️ 剣"], ["1", "🌀 独楽"]].map(([value, label]) => (
                <button
                  key={value}
                  onClick={() => setMatchMode(value)}
                  style={{
                    flex: 1, padding: '12px', fontSize: '16px', fontWeight: 'bold', cursor: 'pointer',
                    borderRadius: '8px', fontFamily: 'inherit',
                    border: matchMode === value ? '3px solid #2196F3' : '2px solid #ccc',
                    backgroundColor: matchMode === value ? '#e3f2fd' : '#fff',
                    color: matchMode === value ? '#1565c0' : '#666',
                  }}
                >
                  {label}
                </button>
              ))}
            </div>
            <p style={{ fontSize: '12px', color: '#888', margin: '0 0 20px 0' }}>
              相手の部屋に入ったときは、その部屋のルールになります
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '15px' }}>
              {[2, 4].map(size => (
                <div key={size} className="ink-btn-container">
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button className="sharp-button" onClick={() => startRandomMatch(size)}>
                    {size}人で戦う
                  </button>
                </div>
              ))}
            </div>

            <div style={{ marginTop: '25px', display: 'flex', justifyContent: 'center' }}>
              <div className="ink-btn-container" style={{ width: '200px' }}>
                <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                <button className="sharp-button" style={{ '--btn-color': '#666666' }} onClick={handleCancelJoin}>
                  戻る
                </button>
              </div>
            </div>
          </div>
        ) : (
          /* ▼ ID入力モードのUI */
          <div className="glass" style={{ width: '100%', maxWidth: '400px', padding: '25px', boxSizing: 'border-box' }}>
            <p style={{ fontSize: '18px', fontWeight: 'bold', color: '#000', margin: '0 0 20px 0' }}>
              ロビーID（6桁の数字）を入力
            </p>
            
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '10px' }}>
              <input 
                type="text" 
                value={targetId} 
                onChange={(e) => setTargetId(e.target.value)} 
                placeholder="例: 123456" 
                maxLength={6}
                style={{ 
                  ...styles.input, 
                  borderRadius: '0', 
                  border: '2px solid #000',
                  letterSpacing: '4px', 
                  width: '180px',
                  fontFamily: 'sans-serif',
                  fontWeight: 'bold'
                }} 
              />
              <button 
                style={{ 
                  padding: '10px 20px', 
                  fontSize: '18px', 
                  backgroundColor: '#4CAF50', 
                  color: 'white',
                  border: 'none',
                  borderRadius: '0',
                  cursor: 'pointer',
                  fontFamily: 'sans-serif',
                  fontWeight: 'bold',
                  boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                  transition: 'background-color 0.1s'
                }} 
                onClick={connectToHost}
                disabled={connecting}
              >
                接続
              </button>
            </div>

            {/* キャンセルボタン */}
            <div style={{ marginTop: '30px', width: '100%', display: 'flex', justifyContent: 'center' }}>
              <div className="ink-btn-container" style={{ width: '200px' }}>
                <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                <button 
                  className="sharp-button"
                  style={{ '--btn-color': '#666666' }}
                  onClick={handleCancelJoin}
                >
                  戻る
                </button>
              </div>
            </div>
          </div>
        )}

        <div style={{ height: '50px', display: 'flex', alignItems: 'center', justifyContent: 'center', marginTop: '30px', width: '90%' }}>
          {systemMessage && (
            <div style={{ ...styles.errorMessage, margin: '0', width: '100%', fontSize: 'clamp(12px, 3.5vw, 16px)' }}>
              ⚠️ {systemMessage}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}