// src/screens/ResultScreen.jsx
import { PLAYER_COLORS, styles, swordImageSource } from '../styles';

const REASONS = { DISCONNECTED: '（切断）', FORFEIT: '（切断）' };

// 2〜4人の順位表。1位以外は自分の順位を見出しに出す。
export default function ResultScreen({ view, onReturnToLobby, onLeave }) {
  const result = view?.result;
  if (!result) return null;

  const players = view.resultPlayers ?? [];
  const standings = [...(result.standings ?? [])].sort((a, b) => a.rank - b.rank);
  const playerOf = id => players.find(p => p.playerId === id);
  const myScore = standings.find(s => s.playerId === view.localPlayerId);
  const iWon = !result.draw && result.winnerId === view.localPlayerId;
  const winner = playerOf(result.winnerId);
  const headline = result.draw ? "DRAW" : iWon ? "YOU WIN!!" : myScore ? `${myScore.rank}位` : "試合終了";
  const accent = iWon ? '#d32f2f' : '#1976d2';

  return (
    <div style={styles.container}>
      {winner && <img src={swordImageSource(winner.swordData)} alt="" style={styles.bgImageCenter} />}

      <div style={styles.contentWrapper}>
        <h2 style={{
          fontSize: 'clamp(40px, 10vw, 70px)',
          fontWeight: '900',
          fontStyle: 'italic',
          margin: '0 0 20px 0',
          color: accent,
          textShadow: '2px 2px 0px #fff, -2px -2px 0px #fff, 2px -2px 0px #fff, -2px 2px 0px #fff, 4px 4px 10px rgba(0,0,0,0.3)'
        }}>
          {headline}
        </h2>

        <div style={{ backgroundColor: 'rgba(255, 255, 255, 0.9)', padding: '25px', borderRadius: '15px', boxShadow: '0 8px 20px rgba(0,0,0,0.2)', border: `4px solid ${accent}`, width: '100%', maxWidth: '700px', boxSizing: 'border-box' }}>
          <h3 style={{ fontSize: 'clamp(18px, 4vw, 28px)', color: '#333', margin: '0 0 20px 0' }}>
            {result.draw ? "引き分け" : `勝者: ${winner?.swordData.name ?? "不明"}`}
          </h3>

          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 'clamp(12px, 3vw, 16px)' }}>
            <thead>
              <tr style={{ borderBottom: '2px solid #ddd', color: '#666' }}>
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
                return (
                  <tr key={score.playerId} style={{ borderBottom: '1px solid #eee', backgroundColor: isMe ? '#fffde7' : 'transparent', fontWeight: isMe ? 'bold' : 'normal' }}>
                    <td style={{ padding: '10px 4px', fontSize: '18px' }}>{score.rank}位</td>
                    <td style={{ padding: '10px 4px' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', justifyContent: 'flex-start' }}>
                        {player && <img src={swordImageSource(player.swordData)} alt="" style={{ width: '40px', height: '40px', objectFit: 'contain' }} />}
                        <span style={{ color: player ? PLAYER_COLORS[player.slotIndex] : '#666' }}>
                          {player ? `${player.slotIndex + 1}P ${player.swordData.name}` : score.playerId}
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

        <div style={{ marginTop: '40px', display: 'flex', gap: '5%', width: '100%', maxWidth: '500px' }}>
          {!view.closed && (
            <div className="ink-btn-container" style={{ flex: 1 }}>
              <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
              <button className="sharp-button" style={{ '--btn-color': '#4CAF50' }} onClick={onReturnToLobby}>
                ロビーに戻る
              </button>
            </div>
          )}

          <div className="ink-btn-container" style={{ flex: 1 }}>
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button className="sharp-button" style={{ '--btn-color': '#666666' }} onClick={onLeave}>
              退出する
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
