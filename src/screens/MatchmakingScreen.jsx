import { useEffect, useState } from 'react';
import InkButton from '../components/InkButton.jsx';
import { EnsoInk } from '../components/SwordForgeParts.jsx';

const MESSAGES = {
  ENTERING: '待合所に接続しています…',
  SEARCHING: '対戦相手を探しています…',
  JOINING: '見つかった部屋に参加しています…',
  HOSTING: 'あなたの部屋で相手を待っています…',
  FULL: '今は混み合っています',
  UNAVAILABLE: 'ランダムマッチを利用できません',
};

// ランダムマッチの待ち表示。タイトル画面のメニューの場所に差し込んで使う
// （ロゴ・背景の剣・「遊び方」はタイトル画面のものがそのまま残る）。
// 探している間は、完成画面と同じ円相を描いては消し、その真ん中に経過時間を出す
// startedAt：待ち始めた時刻（App が持つ）。表示が作り直されても、経過時間はそこから数える
export default function MatchmakingScreen({ view, gameMode = '0', startedAt = 0, onCancel }) {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, []);
  const elapsed = startedAt ? Math.max(0, Math.floor((now - startedAt) / 1000)) : 0;

  const phase = view?.phase ?? 'ENTERING';
  const waiting = view?.waiting ?? null;
  const targetSize = view?.targetSize ?? 2;
  const stopped = ['FULL', 'UNAVAILABLE'].includes(phase);
  const time = `${Math.floor(elapsed / 60)}:${String(elapsed % 60).padStart(2, '0')}`;

  return (
    <div className="title-panel title-matching">
      <p className="title-panel__title">{targetSize}人でランダムマッチ</p>
      <p className="title-panel__sub">希望ルール：{gameMode === '1' ? '独楽' : '剣'}</p>

      <div className={`title-matching__ring ${stopped ? 'is-stopped' : ''}`}>
        {!stopped && <EnsoInk variant="craft" className="title-matching__enso" />}
        <span className="title-matching__time">{stopped ? '' : time}</span>
      </div>

      <p className={`title-matching__status ${stopped ? 'is-stopped' : ''}`}>{MESSAGES[phase] ?? MESSAGES.SEARCHING}</p>
      {view?.error && <p className="title-matching__error">{view.error}</p>}

      {/* 人数は、届く前から場所を取っておく（届いたときに下の「やめる」が動かないように） */}
      <div className="title-matching__meta">
        {waiting && (
          <>
            <span>マッチング待機中<b>{waiting.total}</b>人</span>
            {waiting[targetSize] != null && <span>うち{targetSize}人戦<b>{waiting[targetSize]}</b>人</span>}
          </>
        )}
      </div>

      {phase === 'FULL' && (
        <p className="title-panel__note">空きが出しだい自動で再開します。ロビーIDを共有すれば、待たずに対戦できます。</p>
      )}

      <div className="title-panel__back">
        <InkButton variant="usuzumi" onClick={onCancel}>やめる</InkButton>
      </div>
    </div>
  );
}
