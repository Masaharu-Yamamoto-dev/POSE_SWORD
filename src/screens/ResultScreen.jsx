import React, { useState, useEffect } from 'react';
import { PLAYER_COLORS, styles, swordImageSource } from '../styles';
import { HILT_DATABASE } from './SwordListScreen';

const REASONS = { DISCONNECTED: '（切断）', FORFEIT: '（降参）' };

export default function ResultScreen({ view, onReturnToLobby, onFindNewOpponents = null }) {
  // 演出フェーズ: INIT -> SWIPE(横切り) -> WANDERING(右往左往) -> SLAM(左にドン) -> UI(表示)
  const [phase, setPhase] = useState('INIT');

  const result = view?.result;
  const players = view.resultPlayers ?? [];
  const standings = [...(result?.standings ?? [])].sort((a, b) => a.rank - b.rank);
  const playerOf = id => players.find(p => p.playerId === id);
  const myScore = standings.find(s => s.playerId === view.localPlayerId);
  const iWon = !result?.draw && result?.winnerId === view.localPlayerId;
  const winner = playerOf(result?.winnerId);
  
  const room = view?.room;
  const gameMode = room?.gameMode;
  const soloMode = room?.soloMode;
  const bossPlayerId = room?.bossPlayerId;
  const isKoma = gameMode === "1";

  const headline = result?.draw ? "DRAW" : iWon ? "YOU WIN!!" : myScore ? `${myScore.rank}位` : "試合終了";
  const accent = iWon ? '#d32f2f' : '#1976d2';

  // 勝者の剣と柄のデータ
  const winnerHilt = winner ? (HILT_DATABASE[winner.swordData?.hiltType || "0"] || HILT_DATABASE["0"]) : null;

  useEffect(() => {
    if (!result) return;
    
    // 引き分け、または勝者がいない場合はすぐUIを表示
    if (result.draw || !winner) {
      setPhase('UI');
      return;
    }

    const t1 = setTimeout(() => setPhase('SWIPE'), 100);
    const t2 = setTimeout(() => setPhase('WANDERING'), 1100); // 🌟 SWIPE(1.0s)終了後
    const t3 = setTimeout(() => setPhase('SLAM'), 2200);      // 🌟 WANDERING(1.1s)終了後
    const t4 = setTimeout(() => setPhase('UI'), 2700);         // 🌟 SLAM(0.5s)終了後

    return () => { clearTimeout(t1); clearTimeout(t2); clearTimeout(t3); clearTimeout(t4); };
  }, [result, winner]);

  if (!result) return null;

  // 剣のアニメーションクラス判定
  const swordAnimClass = 
    phase === 'SWIPE' ? 'result-sword-swipe' :
    phase === 'WANDERING' ? 'result-sword-wander' :
    (phase === 'SLAM' || phase === 'UI') ? 'result-sword-slam' : '';

  return (
    <div style={{ ...styles.container, overflowX: 'hidden', position: 'relative', backgroundColor: phase === 'INIT' ? '#000' : '#eef2f5', transition: 'background-color 0.5s' }}>
      
      {/* 🌟 背景の勝者の剣（画面いっぱいに収まるよう縦横比とサイズを調整） */}
      {winner && phase === 'UI' && (
        <img 
          src={swordImageSource(winner.swordData)} 
          alt="" 
          style={{
            ...styles.bgImageCenter,
            position: 'absolute',
            top: '50%',
            left: '50%',
            transform: 'translate(-50%, -50%)',
            maxHeight: '85vh',
            maxWidth: '85vw',
            width: 'auto',
            height: 'auto',
            objectFit: 'contain',
            pointerEvents: 'none',
            zIndex: 0
          }} 
        />
      )}

      <div style={{ width: '100%', maxWidth: '1000px', padding: '20px', boxSizing: 'border-box', zIndex: 1, position: 'relative' }}>
        
        {/* ==========================================
            UIフェーズ（演出完了後）の2カラムレイアウト
        ========================================== */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '40px', justifyContent: 'center', alignItems: 'center', minHeight: '60vh' }}>
          
          {/* 左側：勝者の剣の表示エリア（SLAM以降に定位置につく） */}
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '300px' }}>
            {winner && phase !== 'INIT' && (
              <div className={swordAnimClass} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', height: '350px' }}>
                <img src={swordImageSource(winner.swordData)} alt="Winner Blade" style={{ height: '220px', width: 'auto', objectFit: 'contain', zIndex: 1, filter: 'drop-shadow(0 0 15px rgba(255,215,0,0.6))' }} />
                {!isKoma && (
                  <img src={winnerHilt?.imageSrc} alt="Winner Hilt" style={{ height: '180px', width: 'auto', marginTop: '-60px', zIndex: 2, objectFit: 'contain' }} />
                )}
              </div>
            )}
            
            {/* 勝者の名前表示（SLAM以降、黒バラフォントを適用） */}
            <div style={{ 
              opacity: (phase === 'SLAM' || phase === 'UI') && winner ? 1 : 0, 
              transform: (phase === 'SLAM' || phase === 'UI') ? 'translateY(0)' : 'translateY(20px)',
              transition: 'all 0.4s ease 0.2s', textAlign: 'center', marginTop: '15px'
            }}>
              <span style={{ color: '#666', fontSize: '14px', fontWeight: 'bold' }}>WINNER</span>
              <h2 style={{ margin: 0, fontSize: '32px', color: PLAYER_COLORS[winner?.slotIndex || 0], textShadow: '1px 1px 0 #fff', fontFamily: "'Kurobara Gothic', sans-serif" }}>
                {soloMode && winner?.playerId === bossPlayerId ? "👑 " : ""}{winner?.swordData?.name}
              </h2>
            </div>
          </div>

          {/* 右側：順位表・テキスト・ボタン群（UIフェーズでフェードイン） */}
          <div style={{ 
            flex: 1, minWidth: '320px', maxWidth: '600px', display: 'flex', flexDirection: 'column', alignItems: 'center',
            opacity: phase === 'UI' ? 1 : 0, transform: phase === 'UI' ? 'translateX(0)' : 'translateX(50px)',
            transition: 'all 0.5s cubic-bezier(0.175, 0.885, 0.32, 1.275)'
          }}>
            
            <h2 style={{
              fontSize: 'clamp(40px, 8vw, 60px)', fontWeight: '900', fontStyle: 'italic', margin: '0 0 20px 0', color: accent,
              textShadow: '2px 2px 0px #fff, -2px -2px 0px #fff, 2px -2px 0px #fff, -2px 2px 0px #fff, 4px 4px 10px rgba(0,0,0,0.3)'
            }}>
              {headline}
            </h2>

            <div style={{ backgroundColor: 'rgba(255, 255, 255, 0.95)', padding: '20px', borderRadius: '15px', boxShadow: '0 8px 20px rgba(0,0,0,0.2)', border: `4px solid ${accent}`, width: '100%', boxSizing: 'border-box' }}>
              <h3 style={{ fontSize: '22px', color: '#333', margin: '0 0 15px 0', textAlign: 'center' }}>
                {result.draw ? "引き分け" : "最終結果"}
              </h3>

              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 'clamp(12px, 3vw, 15px)' }}>
                <thead>
                  <tr style={{ borderBottom: '2px solid #ddd', color: '#666', textAlign: 'left' }}>
                    <th style={{ padding: '8px 4px' }}>順位</th>
                    <th style={{ padding: '8px 4px' }}>プレイヤー</th>
                    <th style={{ padding: '8px 4px' }}>与</th>
                    <th style={{ padding: '8px 4px' }}>被</th>
                    <th style={{ padding: '8px 4px' }}>撃破</th>
                  </tr>
                </thead>
                <tbody>
                  {standings.map(score => {
                    const player = playerOf(score.playerId);
                    const isMe = score.playerId === view.localPlayerId;
                    const isBoss = soloMode && player?.playerId === bossPlayerId;

                    return (
                      <tr key={score.playerId} style={{ borderBottom: '1px solid #eee', backgroundColor: isMe ? '#fffde7' : 'transparent', fontWeight: isMe ? 'bold' : 'normal' }}>
                        <td style={{ padding: '10px 4px', fontSize: '16px' }}>{score.rank}位</td>
                        <td style={{ padding: '10px 4px' }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            {player && <img src={swordImageSource(player.swordData)} alt="" style={{ width: '30px', height: '30px', objectFit: 'contain' }} />}
                            <span style={{ color: player ? PLAYER_COLORS[player.slotIndex] : '#666' }}>
                              {player ? `${player.slotIndex + 1}P ${isBoss ? "👑 " : ""}${player.swordData.name}` : score.playerId}
                              {isMe ? "（あなた）" : ""}{REASONS[score.eliminationReason] ?? ""}
                            </span>
                          </div>
                        </td>
                        <td style={{ padding: '10px 4px' }}>{score.damageDealt}</td>
                        <td style={{ padding: '10px 4px' }}>{score.damageTaken}</td>
                        <td style={{ padding: '10px 4px' }}>{score.kills}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {/* ボタン群 */}
            <div style={{ marginTop: '25px', display: 'flex', flexWrap: 'wrap', gap: '15px', width: '100%' }}>
              {!view.closed && (
                <div className="ink-btn-container" style={{ flex: 1, minWidth: '150px' }}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button className="sharp-button" style={{ '--btn-color': '#4CAF50' }} onClick={onReturnToLobby}>
                    ロビーに戻る
                  </button>
                </div>
              )}

              {onFindNewOpponents && (
                <div className="ink-btn-container" style={{ flexBasis: '100%' }}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button className="sharp-button" style={{ '--btn-color': '#d32f2f' }} onClick={onFindNewOpponents}>
                    ⚡ 別の相手を探す
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* 🌀 勝者の剣のアニメーション用CSS */}
      <style>{`
        /* 🌟 1. 画面をズバッと横切る（個別イージングでヌルッと減速・スロー＆一気に画面外へ） */
        @keyframes swordSwipe {
          0% {
            position: fixed; top: 40%; left: -50vw; transform: rotate(90deg) scale(1.8); zIndex: 1000;
            animation-timing-function: cubic-bezier(0.1, 0.85, 0.25, 1);
          }
          40% {
            position: fixed; top: 40%; left: 42vw; transform: rotate(90deg) scale(2.2); zIndex: 1000;
            animation-timing-function: ease-in-out;
          }
          60% {
            position: fixed; top: 40%; left: 58vw; transform: rotate(90deg) scale(2.3); zIndex: 1000;
            animation-timing-function: cubic-bezier(0.75, 0, 0.9, 0.2);
          }
          100% {
            position: fixed; top: 40%; left: 150vw; transform: rotate(90deg) scale(1.8); zIndex: 1000;
          }
        }
        
        /* 🌟 2. 画面内を回転しながら高速で右往左往 */
        @keyframes swordWander {
          0% { position: fixed; top: -20vh; left: 80vw; transform: rotate(0deg) scale(1.5); zIndex: 1000; }
          25% { position: fixed; top: 80vh; left: 20vw; transform: rotate(360deg) scale(1.5); zIndex: 1000; }
          50% { position: fixed; top: 10vh; left: 50vw; transform: rotate(720deg) scale(1.5); zIndex: 1000; }
          75% { position: fixed; top: 60vh; left: 80vw; transform: rotate(1080deg) scale(1.5); zIndex: 1000; }
          100% { position: fixed; top: 40vh; left: -20vw; transform: rotate(1440deg) scale(1.5); zIndex: 1000; }
        }

        /* 3. 左側の定位置にドンッと着地 */
        @keyframes swordSlam {
          0% { transform: scale(3) rotate(360deg); opacity: 0; }
          100% { transform: scale(1) rotate(0deg); opacity: 1; }
        }

        .result-sword-swipe { animation: swordSwipe 1.0s linear forwards; }
        .result-sword-wander { animation: swordWander 1.1s linear forwards; }
        .result-sword-slam { animation: swordSlam 0.5s cubic-bezier(0.175, 0.885, 0.32, 1.275) forwards; }
      `}</style>
    </div>
  );
}