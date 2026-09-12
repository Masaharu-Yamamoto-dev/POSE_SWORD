import { useEffect, useState } from 'react';
import { styles } from '../styles';

const MESSAGES = {
  ENTERING: '待合所に接続しています…',
  SEARCHING: '対戦相手を探しています…',
  JOINING: '見つかった部屋に参加しています…',
  HOSTING: 'あなたの部屋で相手を待っています…',
  FULL: '今は混み合っています',
  UNAVAILABLE: 'ランダムマッチを利用できません',
};

export default function MatchmakingScreen({ view, mySwordData, gameMode = '0', onCancel }) {
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => {
    const startedAt = Date.now();
    const interval = setInterval(() => setElapsed(Math.floor((Date.now() - startedAt) / 1000)), 1000);
    return () => clearInterval(interval);
  }, []);

  const phase = view?.phase ?? 'ENTERING';
  const waiting = view?.waiting ?? null;
  const targetSize = view?.targetSize ?? 2;
  const stopped = ['FULL', 'UNAVAILABLE'].includes(phase);

  return (
    <div style={styles.container}>
      {mySwordData?.imageSrc && (
        <img src={mySwordData.imageSrc} alt="" style={{ ...styles.bgImageCenter, transform: 'translate(-50%, -50%)' }} />
      )}

      <div style={styles.contentWrapper}>
        <h2 style={{ fontSize: 'clamp(24px, 6vw, 40px)', margin: '0 0 10px 0', color: '#333' }}>
          {targetSize}人でランダムマッチ
        </h2>
        <p style={{ margin: '0 0 20px 0', color: '#666', fontSize: '14px' }}>
          希望ルール：{gameMode === '1' ? '🌀 独楽' : '🗡️ 剣'}
        </p>

        <div className="glass" style={{ width: '100%', maxWidth: '440px', padding: '30px', boxSizing: 'border-box' }}>
          {!stopped && (
            <div style={{ width: '60px', height: '60px', margin: '0 auto 20px', borderRadius: '50%',
              border: '6px solid #e0e0e0', borderTopColor: '#2196F3', animation: 'mm-spin 1s linear infinite' }} />
          )}

          <p style={{ fontSize: '18px', fontWeight: 'bold', margin: '0 0 10px 0', color: stopped ? '#c62828' : '#333' }}>
            {MESSAGES[phase] ?? MESSAGES.SEARCHING}
          </p>

          {view?.error && (
            <p style={{ margin: '0 0 10px 0', fontSize: '14px', color: '#c62828' }}>{view.error}</p>
          )}

          {!stopped && (
            <p style={{ margin: '0 0 6px 0', fontSize: '32px', fontWeight: 'bold', color: '#3f51b5', fontFamily: 'sans-serif' }}>
              {Math.floor(elapsed / 60)}:{String(elapsed % 60).padStart(2, '0')}
            </p>
          )}

          {waiting && (
            <p style={{ margin: 0, fontSize: '14px', color: '#666' }}>
              いま待っている人：{waiting.total}人
              {waiting[targetSize] != null && `（うち${targetSize}人戦 ${waiting[targetSize]}人）`}
            </p>
          )}

          {phase === 'FULL' && (
            <p style={{ margin: '10px 0 0 0', fontSize: '13px', color: '#666' }}>
              空きが出しだい自動で再開します。ロビーIDを共有すれば、待たずに対戦できます。
            </p>
          )}
        </div>

        <div style={{ marginTop: '40px', width: '100%', maxWidth: '260px' }}>
          <div className="ink-btn-container">
            <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
            <button className="sharp-button" style={{ '--btn-color': '#666666' }} onClick={onCancel}>
              やめる
            </button>
          </div>
        </div>
      </div>

      <style>{`@keyframes mm-spin { to { transform: rotate(360deg); } }`}</style>
    </div>
  );
}
