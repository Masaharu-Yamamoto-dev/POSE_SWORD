import React from 'react';
import { styles } from '../styles';

export default function LobbyScreen({
  role, mySwordData, enemySwordData, connection,
  lobbyId, isCopied, handleCopyId, 
  gameMode, setGameMode, countdown,
  isReady, setIsReady, isEnemyReady, handleReady,
  goToCrafting, handleLeave,
  swordList, equipSword
}) {

  const mockPlayers = [
    { id: '1P', isMe: true, data: mySwordData, isReady: isReady },
    { id: '2P', isMe: false, data: enemySwordData, isReady: isEnemyReady },
    { id: '3P', isMe: false, data: null, isReady: false },
    { id: '4P', isMe: false, data: null, isReady: false }
  ];

  return (
    <div style={{ ...styles.container, padding: '10px', alignItems: 'flex-start' }}>
      
      <div className="lobby-grid">
        
        {/* ======================================= */}
        {/* 左上：1P あなたの領域 */}
        {/* ======================================= */}
        <div className="lobby-panel self-panel" style={{ display: 'flex', flexDirection: 'column' }}>
          <h2 style={{ margin: '0 0 15px 0', fontSize: '28px', color: '#1976d2', borderBottom: '2px solid #1976d2', paddingBottom: '5px' }}>
            1P: あなた ({role === "HOST" ? "ホスト" : "ゲスト"})
          </h2>
          
          {mySwordData ? (
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flex: 1, justifyContent: 'center' }}>
              
              {/* ▼ 変更：空きスロット（＋）は表示せず、所持している剣だけを表示 */}
              <div style={{ display: 'flex', justifyContent: 'center', gap: '15px', width: '100%', marginBottom: '15px' }}>
                {swordList.map(sword => {
                  const isEquippedSlot = mySwordData?.id === sword.id;
                  return (
                    <div 
                      key={sword.id} 
                      onClick={() => { if (!isReady) equipSword(sword); }}
                      style={{ position: 'relative', width: '60px', height: '60px', backgroundColor: 'white', borderRadius: '10px', border: isEquippedSlot ? '4px solid #2196F3' : '2px solid #ccc', cursor: (isEquippedSlot || isReady) ? 'default' : 'pointer', display: 'flex', justifyContent: 'center', alignItems: 'center', opacity: (isEquippedSlot || !isReady) ? 1 : 0.5, transition: '0.2s' }}
                    >
                      {isEquippedSlot && <div style={{ position: 'absolute', top: -5, left: -5, width: '110%', backgroundColor: '#2196F3', color: 'white', fontSize: '10px', fontWeight: 'bold' }}>装備</div>}
                      <img src={sword.imageSrc} alt="" style={{ maxWidth: '80%', maxHeight: '80%' }} />
                    </div>
                  );
                })}
              </div>

              {/* 剣の画像メイン表示 */}
              <div style={{ height: '180px', display: 'flex', alignItems: 'center', justifyContent: 'center', marginBottom: '10px' }}>
                <img src={mySwordData.imageSrc} style={{ maxHeight: '100%', maxWidth: '100%', objectFit: 'contain' }} alt="My Sword" />
              </div>

              {/* 剣のステータス */}
              <h3 style={{ margin: '0 0 10px 0', fontSize: '24px' }}>{mySwordData.name}</h3>
              <div style={{ display: 'flex', gap: '15px', fontWeight: 'bold', backgroundColor: 'rgba(255,255,255,0.5)', padding: '8px 20px', borderRadius: '20px', marginBottom: '20px' }}>
                <span style={{ color: '#d32f2f' }}>HP: {mySwordData.hp}</span>
                <span style={{ color: '#f57c00' }}>攻: {mySwordData.attack}</span>
                <span style={{ color: '#558b2f' }}>重: {mySwordData.weight}</span>
              </div>

              {/* アクションボタン */}
              <div style={{ display: 'flex', gap: '15px', width: '100%', maxWidth: '400px' }}>
                <button 
                  style={{ flex: 1, padding: '15px', fontSize: '18px', backgroundColor: isReady ? '#ccc' : '#607d8b', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: isReady ? 'not-allowed' : 'pointer', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' }}
                  onClick={() => goToCrafting("LOBBY")}
                  disabled={isReady}
                >
                  🔧 武器庫へ
                </button>

                {!isReady ? (
                  <button 
                    style={{ flex: 1.5, padding: '15px', fontSize: '18px', backgroundColor: '#4CAF50', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' }}
                    onClick={handleReady}
                  >
                    ✅ 準備完了
                  </button>
                ) : (
                  <button 
                    style={{ flex: 1.5, padding: '15px', fontSize: '18px', backgroundColor: '#f44336', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer', boxShadow: 'inset 0 4px 6px rgba(0,0,0,0.2)' }}
                    onClick={() => { setIsReady(false); if(connection) connection.send({ type: "SYNC_STATE", isReady: false }); }}
                  >
                    🔄 準備取消
                  </button>
                )}
              </div>
            </div>
          ) : (
             <div style={{ height: '300px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>剣がありません</div>
          )}
        </div>

        {/* ======================================= */}
        {/* 右上：他の3人の状態（高さを左側に合わせる） */}
        {/* ======================================= */}
        <div className="others-panel">
          {mockPlayers.slice(1).map((p) => (
            <div key={p.id} className="lobby-panel mini-panel" style={{ borderColor: p.isReady ? '#4CAF50' : '#ccc' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid #eee', paddingBottom: '5px', marginBottom: '10px' }}>
                <span style={{ fontWeight: 'bold', color: '#555' }}>{p.id}: {p.data ? "プレイヤー" : "空き枠"}</span>
                {p.data && (
                  <span style={{ backgroundColor: p.isReady ? '#4CAF50' : '#9e9e9e', color: 'white', padding: '3px 10px', borderRadius: '15px', fontSize: '12px', fontWeight: 'bold' }}>
                    {p.isReady ? "✅ 準備OK" : "⏳ 準備中"}
                  </span>
                )}
              </div>
              
              {p.data ? (
                <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
                  <div style={{ width: '60px', height: '60px', flexShrink: 0, backgroundColor: '#f5f5f5', borderRadius: '8px', display: 'flex', justifyContent: 'center', alignItems: 'center' }}>
                    <img src={p.data.imageSrc} style={{ maxHeight: '90%', maxWidth: '90%' }} alt="" />
                  </div>
                  <div>
                    <p style={{ margin: '0 0 5px 0', fontWeight: 'bold', fontSize: '16px' }}>{p.data.name}</p>
                    <p style={{ margin: 0, fontSize: '12px', color: '#666' }}>HP:{p.data.hp} / 攻:{p.data.attack} / 重:{p.data.weight}</p>
                  </div>
                </div>
              ) : (
                <div style={{ height: '60px', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#aaa', fontStyle: 'italic' }}>
                  待機中...
                </div>
              )}
            </div>
          ))}
        </div>

        {/* ======================================= */}
        {/* 左下：ルールと設定 */}
        {/* ======================================= */}
        <div className="lobby-panel rule-panel" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          <h3 style={{ margin: '0 0 10px 0', color: '#333', fontSize: '20px' }}>⚔️ バトルルール</h3>
          
          <div style={{ display: 'flex', gap: '20px', alignItems: 'center' }}>
            <div style={{ width: '100px', height: '100px', backgroundColor: '#e0f7fa', borderRadius: '10px', display: 'flex', justifyContent: 'center', alignItems: 'center', fontSize: '40px' }}>
              {gameMode === "1" ? "🌀" : "🗡️"}
            </div>
            
            <div style={{ flex: 1 }}>
              <h4 style={{ margin: '0 0 5px 0', fontSize: '22px' }}>
                {gameMode === "1" ? "独楽（見下ろし）モード" : "剣（横視点・重力）モード"}
              </h4>
              <p style={{ margin: '0 0 15px 0', color: '#666', fontSize: '14px' }}>
                {gameMode === "1" ? "独楽のようにぶつかり合う半自動戦闘モード" : "剣を振り回して戦うモード"}
              </p>
              
              {role === "HOST" && (
                <button 
                  style={{ padding: '8px 15px', backgroundColor: '#2196F3', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer' }}
                  onClick={() => {
                    const nextMode = gameMode === "1" ? "0" : "1";
                    setGameMode(nextMode);
                    if(connection) connection.send({ type: "SYNC_GAMEMODE", gameMode: nextMode });
                  }}
                >
                  ルールを変更する
                </button>
              )}
            </div>
          </div>
        </div>

        {/* ======================================= */}
        {/* 右下：ID・システム（高さが左下と揃う） */}
        {/* ======================================= */}
        <div className="lobby-panel system-panel" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          
          {/* ▼ 変更：スッキリしたID表示 */}
          <div style={{ backgroundColor: '#e8eaf6', padding: '15px', borderRadius: '8px', marginBottom: '15px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div>
              <span style={{ fontSize: '12px', color: '#666', display: 'block' }}>ロビーID</span>
              <span style={{ fontSize: '24px', fontWeight: 'bold', letterSpacing: '2px', color: '#3f51b5' }}>{lobbyId || "----"}</span>
            </div>
            <button 
              onClick={handleCopyId}
              style={{ padding: '10px 15px', backgroundColor: isCopied ? '#4CAF50' : '#fff', color: isCopied ? '#fff' : '#333', border: '1px solid #ccc', borderRadius: '5px', cursor: 'pointer', fontWeight: 'bold' }}
            >
              {isCopied ? "✓ コピー" : "📋 コピー"}
            </button>
          </div>

          <button 
            style={{ padding: '15px', fontSize: '18px', backgroundColor: '#666', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer', width: '100%' }}
            onClick={handleLeave}
          >
            退出する
          </button>
        </div>

      </div>

      {countdown !== null && (
        <div style={{ position: 'fixed', top: 0, left: 0, width: '100%', height: '100%', backgroundColor: 'rgba(0,0,0,0.7)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 100 }}>
          <h2 style={{ fontSize: '120px', color: 'red', animation: 'pulse 1s infinite', textShadow: '0 0 20px yellow' }}>
            {countdown > 0 ? countdown : "START!"}
          </h2>
        </div>
      )}

      <style>{`
        .lobby-grid { 
          display: grid; 
          grid-template-columns: 1fr; 
          gap: 15px; 
          width: 100%; 
          max-width: 1100px; 
          margin: 0 auto; 
        }
        
        .lobby-panel { 
          background-color: rgba(255, 255, 255, 0.9); 
          border-radius: 15px; 
          padding: 20px; 
          box-shadow: 0 4px 10px rgba(0,0,0,0.1); 
          box-sizing: border-box; 
        }
        
        .others-panel { 
          display: flex; 
          flex-direction: column; 
          gap: 15px; 
        }
        
        .mini-panel { 
          padding: 15px; 
          border-left: 5px solid #ccc; 
          flex: 1; /* 親の高さに合わせて均等に伸びる */
          display: flex;
          flex-direction: column;
          justify-content: center;
        }

        @media (min-width: 800px) {
          .lobby-grid { 
            grid-template-columns: 1.5fr 1fr; 
            grid-template-rows: auto auto; 
            align-items: stretch; /* 同じ行の箱の高さを完全に一致させる */
          }
          .self-panel { grid-column: 1 / 2; grid-row: 1 / 2; }
          .others-panel { grid-column: 2 / 3; grid-row: 1 / 2; }
          .rule-panel { grid-column: 1 / 2; grid-row: 2 / 3; }
          .system-panel { grid-column: 2 / 3; grid-row: 2 / 3; }
        }
        @keyframes pulse { 0% { transform: scale(1); } 50% { transform: scale(1.1); } 100% { transform: scale(1); } }
      `}</style>
    </div>
  );
}