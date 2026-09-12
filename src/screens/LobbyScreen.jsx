import { PLAYER_COLORS, styles, swordImageSource } from '../styles';
import { MAX_PLAYERS, MIN_PLAYERS } from '../network/HostRoom';

// 部屋は4席。2人以上そろえば、その人数のまま対戦を始められる。
export default function LobbyScreen({
  view, roomId, isCopied, handleCopyId,
  swordList, mySwordData, equipSword,
  onReady, onGameMode, onStart, onLeave, goToCrafting, error
}) {
  const room = view?.room;
  const me = room?.players.find(p => p.playerId === view.localPlayerId);
  if (!room || !me) return null;

  const isHost = view.isHost;
  const gameMode = room.gameMode;
  const playerCount = room.players.length;
  const readyCount = room.players.filter(p => p.ready).length;
  const otherSeats = Array.from({ length: MAX_PLAYERS }, (_, slot) => slot)
    .filter(slot => slot !== me.slotIndex)
    .map(slot => ({ slot, player: room.players.find(p => p.slotIndex === slot) }));

  const status =
    playerCount < MIN_PLAYERS ? `参加者を待っています（最低${MIN_PLAYERS}人・最大${MAX_PLAYERS}人）`
    : readyCount < playerCount ? `準備完了 ${readyCount} / ${playerCount}人`
    : isHost ? `${playerCount}人全員の準備が完了。対戦を開始できます`
    : `${playerCount}人全員の準備が完了。ホストの開始を待っています`;

  return (
    <div style={{ ...styles.container, padding: '10px', alignItems: 'flex-start' }}>
      <div className="lobby-grid">

        {/* 左上：自分の領域 */}
        <div className="lobby-panel self-panel" style={{ display: 'flex', flexDirection: 'column' }}>
          <h2 style={{ margin: '0 0 15px 0', fontSize: '28px', color: PLAYER_COLORS[me.slotIndex], borderBottom: `2px solid ${PLAYER_COLORS[me.slotIndex]}`, paddingBottom: '5px' }}>
            {me.slotIndex + 1}P: あなた ({isHost ? "ホスト" : "ゲスト"})
          </h2>

          {mySwordData ? (
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flex: 1, justifyContent: 'center' }}>

              {/* 所持している剣だけを表示し、準備前なら持ち替えられる */}
              <div style={{ display: 'flex', justifyContent: 'center', gap: '15px', width: '100%', marginBottom: '15px' }}>
                {swordList.map(sword => {
                  const isEquippedSlot = mySwordData?.id === sword.id;
                  return (
                    <div
                      key={sword.id}
                      onClick={() => { if (!me.ready) equipSword(sword); }}
                      style={{ position: 'relative', width: '60px', height: '60px', backgroundColor: 'white', borderRadius: '10px', border: isEquippedSlot ? '4px solid #2196F3' : '2px solid #ccc', cursor: (isEquippedSlot || me.ready) ? 'default' : 'pointer', display: 'flex', justifyContent: 'center', alignItems: 'center', opacity: (isEquippedSlot || !me.ready) ? 1 : 0.5, transition: '0.2s' }}
                    >
                      {isEquippedSlot && <div style={{ position: 'absolute', top: -5, left: -5, width: '110%', backgroundColor: '#2196F3', color: 'white', fontSize: '10px', fontWeight: 'bold' }}>装備</div>}
                      <img src={sword.imageSrc} alt="" style={{ maxWidth: '80%', maxHeight: '80%' }} />
                    </div>
                  );
                })}
              </div>

              <div style={{ height: '180px', display: 'flex', alignItems: 'center', justifyContent: 'center', marginBottom: '10px' }}>
                <img src={mySwordData.imageSrc} style={{ maxHeight: '100%', maxWidth: '100%', objectFit: 'contain' }} alt="My Sword" />
              </div>

              <h3 style={{ margin: '0 0 10px 0', fontSize: '24px' }}>{mySwordData.name}</h3>
              <div style={{ display: 'flex', gap: '15px', fontWeight: 'bold', backgroundColor: 'rgba(255,255,255,0.5)', padding: '8px 20px', borderRadius: '20px', marginBottom: '20px' }}>
                <span style={{ color: '#d32f2f' }}>HP: {mySwordData.hp}</span>
                <span style={{ color: '#f57c00' }}>攻: {mySwordData.attack}</span>
                <span style={{ color: '#558b2f' }}>重: {mySwordData.weight}</span>
              </div>

              <div style={{ display: 'flex', gap: '15px', width: '100%', maxWidth: '400px' }}>
                <button
                  style={{ flex: 1, padding: '15px', fontSize: '18px', backgroundColor: me.ready ? '#ccc' : '#607d8b', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: me.ready ? 'not-allowed' : 'pointer', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' }}
                  onClick={() => goToCrafting("LOBBY")}
                  disabled={me.ready}
                >
                  🔧 武器庫へ
                </button>

                {!me.ready ? (
                  <button
                    style={{ flex: 1.5, padding: '15px', fontSize: '18px', backgroundColor: '#4CAF50', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' }}
                    onClick={() => onReady(true)}
                  >
                    ✅ 準備完了
                  </button>
                ) : (
                  <button
                    style={{ flex: 1.5, padding: '15px', fontSize: '18px', backgroundColor: '#f44336', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer', boxShadow: 'inset 0 4px 6px rgba(0,0,0,0.2)' }}
                    onClick={() => onReady(false)}
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

        {/* 右上：他の3席 */}
        <div className="others-panel">
          {otherSeats.map(({ slot, player }) => (
            <div key={slot} className="lobby-panel mini-panel" style={{ borderColor: player?.ready ? '#4CAF50' : '#ccc' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid #eee', paddingBottom: '5px', marginBottom: '10px' }}>
                <span style={{ fontWeight: 'bold', color: PLAYER_COLORS[slot] }}>
                  {slot + 1}P: {player ? (player.playerId === 'p0' ? "ホスト" : "プレイヤー") : "空き枠"}
                </span>
                {player && (
                  <span style={{ backgroundColor: player.ready ? '#4CAF50' : '#9e9e9e', color: 'white', padding: '3px 10px', borderRadius: '15px', fontSize: '12px', fontWeight: 'bold' }}>
                    {!player.connected ? "切断" : !player.inLobby ? "結果を確認中" : player.ready ? "✅ 準備OK" : "⏳ 準備中"}
                  </span>
                )}
              </div>

              {player ? (
                <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
                  <div style={{ width: '60px', height: '60px', flexShrink: 0, backgroundColor: '#f5f5f5', borderRadius: '8px', display: 'flex', justifyContent: 'center', alignItems: 'center' }}>
                    <img src={swordImageSource(player.swordData)} style={{ maxHeight: '90%', maxWidth: '90%' }} alt="" />
                  </div>
                  <div style={{ textAlign: 'left' }}>
                    <p style={{ margin: '0 0 5px 0', fontWeight: 'bold', fontSize: '16px' }}>{player.swordData.name}</p>
                    <p style={{ margin: 0, fontSize: '12px', color: '#666' }}>HP:{player.swordData.hp} / 攻:{player.swordData.attack} / 重:{player.swordData.weight}</p>
                  </div>
                </div>
              ) : (
                <div style={{ height: '60px', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#aaa', fontStyle: 'italic' }}>
                  参加を待っています…
                </div>
              )}
            </div>
          ))}
        </div>

        {/* 左下：ルールと設定 */}
        <div className="lobby-panel rule-panel" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          <h3 style={{ margin: '0 0 10px 0', color: '#333', fontSize: '20px' }}>⚔️ バトルルール</h3>

          <div style={{ display: 'flex', gap: '20px', alignItems: 'center' }}>
            <div style={{ width: '100px', height: '100px', backgroundColor: '#e0f7fa', borderRadius: '10px', display: 'flex', justifyContent: 'center', alignItems: 'center', fontSize: '40px' }}>
              {gameMode === "1" ? "🌀" : "🗡️"}
            </div>

            <div style={{ flex: 1, textAlign: 'left' }}>
              <h4 style={{ margin: '0 0 5px 0', fontSize: '22px' }}>
                {gameMode === "1" ? "独楽（見下ろし）モード" : "剣（横視点・重力）モード"}
              </h4>
              <p style={{ margin: '0 0 15px 0', color: '#666', fontSize: '14px' }}>
                {playerCount >= 3 ? "最後の1人になるまで戦う個人戦" : gameMode === "1" ? "独楽のようにぶつかり合う半自動戦闘モード" : "剣を振り回して戦うモード"}
              </p>

              {isHost && (
                <button
                  style={{ padding: '8px 15px', backgroundColor: '#2196F3', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer' }}
                  onClick={() => onGameMode(gameMode === "1" ? "0" : "1")}
                >
                  ルールを変更する
                </button>
              )}
            </div>
          </div>
        </div>

        {/* 右下：ID・開始・退出 */}
        <div className="lobby-panel system-panel" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>

          <div style={{ backgroundColor: '#e8eaf6', padding: '15px', borderRadius: '8px', marginBottom: '15px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div style={{ textAlign: 'left' }}>
              <span style={{ fontSize: '12px', color: '#666', display: 'block' }}>ロビーID</span>
              <span style={{ fontSize: '24px', fontWeight: 'bold', letterSpacing: '2px', color: '#3f51b5' }}>{roomId || "----"}</span>
            </div>
            <button
              onClick={handleCopyId}
              style={{ padding: '10px 15px', backgroundColor: isCopied ? '#4CAF50' : '#fff', color: isCopied ? '#fff' : '#333', border: '1px solid #ccc', borderRadius: '5px', cursor: 'pointer', fontWeight: 'bold' }}
            >
              {isCopied ? "✓ コピー" : "📋 コピー"}
            </button>
          </div>

          <p style={{ margin: '0 0 12px 0', fontWeight: 'bold', color: '#333' }}>
            現在 {playerCount}人 ／ 最大 {MAX_PLAYERS}人
            <span style={{ display: 'block', fontSize: '13px', fontWeight: 'normal', color: '#666', marginTop: '4px' }}>{status}</span>
          </p>

          {isHost && (
            <button
              style={{ padding: '15px', fontSize: '20px', backgroundColor: view.canStart ? '#d32f2f' : '#ccc', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: view.canStart ? 'pointer' : 'not-allowed', width: '100%', marginBottom: '10px' }}
              onClick={onStart}
              disabled={!view.canStart}
            >
              ⚔️ {playerCount}人で対戦開始
            </button>
          )}

          <button
            style={{ padding: '15px', fontSize: '18px', backgroundColor: '#666', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer', width: '100%' }}
            onClick={onLeave}
          >
            退出する
          </button>

          {error && (
            <p style={{ ...styles.errorMessage, margin: '15px 0 0 0', fontSize: '14px' }}>⚠️ {error}</p>
          )}
        </div>

      </div>

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
      `}</style>
    </div>
  );
}
