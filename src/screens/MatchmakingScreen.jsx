import { useEffect, useState } from 'react';
import { styles } from '../styles';
import InkButton from '../components/InkButton.jsx';

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
        <h2 style={{ fontSize: 'clamp(24px, 6vw, 40px)', margin: '0 0 10px 0' }}>
          {targetSize}人でランダムマッチ
        </h2>
        <p style={{ margin: '0 0 20px 0', color: 'var(--usuzumi)', fontSize: '14px' }}>
          希望ルール：{gameMode === '1' ? '独楽' : '剣'}
        </p>

        <div className="sumi-panel sumi-frame" style={{ width: '100%', maxWidth: '440px', padding: '30px', margin: '20px 0' }}>
          {!stopped && (
            <div style={{ width: '60px', height: '60px', margin: '0 auto 20px', borderRadius: '50%',
              border: '6px solid var(--washi-deep)', borderTopColor: 'var(--sumi)', animation: 'mm-spin 1s linear infinite' }} />
          )}

          <p style={{ fontSize: '18px', fontWeight: 'bold', margin: '0 0 10px 0', color: stopped ? 'var(--shu)' : 'var(--sumi)' }}>
            {MESSAGES[phase] ?? MESSAGES.SEARCHING}
          </p>

          {view?.error && (
            <p style={{ margin: '0 0 10px 0', fontSize: '14px', color: 'var(--shu)' }}>{view.error}</p>
          )}

          {!stopped && (
            <p style={{ margin: '0 0 6px 0', fontSize: '32px', color: 'var(--sumi)' }}>
              {Math.floor(elapsed / 60)}:{String(elapsed % 60).padStart(2, '0')}
            </p>
          )}

          {waiting && (
            <p style={{ margin: 0, fontSize: '14px', color: 'var(--usuzumi)' }}>
              いま待っている人：{waiting.total}人
              {waiting[targetSize] != null && `（うち${targetSize}人戦 ${waiting[targetSize]}人）`}
            </p>
          )}

          {phase === 'FULL' && (
            <p style={{ margin: '10px 0 0 0', fontSize: '13px', color: 'var(--usuzumi)' }}>
              空きが出しだい自動で再開します。ロビーIDを共有すれば、待たずに対戦できます。
            </p>
          )}
        </div>

        <div style={{ marginTop: '40px', width: '100%', maxWidth: '260px' }}>
          <InkButton variant="usuzumi" onClick={onCancel}>やめる</InkButton>
        </div>
      </div>

      <style>{`@keyframes mm-spin { to { transform: rotate(360deg); } }`}</style>
    </div>
  );
}
