import { useEffect, useRef, useState } from 'react';
import { LOBBY_FADE_MS, MATCH_INTRO_MS } from '../network/RoomSession.js';

// 演出が消えていく時間（MatchIntro.css の .match-cover.is-leaving と同じ）
const INTRO_FADE_MS = 250;
// 対戦開始の演出の進み具合。新しい試合の読み込みが始まったら
//   fade：ロビーの上に和紙をかぶせて消す（LOBBY_FADE_MS）
//   show：演出を見せる。MATCH_INTRO_MS 経ったら onDone でホストに知らせ、
//         全員の読み込みと演出が済んでカウントダウンが始まったら（countdownBegun。PLAYING 以降も）次へ
//   leave：演出ごと高速フェードして、下の Unity のカウントダウンを見せる（INTRO_FADE_MS）
//   done：おしまい
// MATCH_INTRO_MS 経っても誰かが読み込み中・演出の途中なら waiting（誰を待っているかは出さない）。
export function useMatchIntro(matchId, phase, countdownBegun, onDone) {
  const [intro, setIntro] = useState({ matchId: null, stage: 'done', timeUp: false });
  const ready = (phase === 'COUNTDOWN' && countdownBegun) || phase === 'PLAYING';
  const doneRef = useRef(onDone);
  useEffect(() => { doneRef.current = onDone; }, [onDone]);

  // 読み込みが始まった試合は、ロビーを消すところから始める
  if (phase === 'LOADING' && matchId && intro.matchId !== matchId) {
    setIntro({ matchId, stage: 'fade', timeUp: false });
  }
  // 最低限の時間が経って全員そろったら消し始める
  if (intro.stage === 'show' && intro.timeUp && ready && intro.matchId === matchId) {
    setIntro({ ...intro, stage: 'leave' });
  }

  const { stage, timeUp } = intro;
  const id = intro.matchId;
  useEffect(() => {
    const next = stage === 'fade' ? [LOBBY_FADE_MS, { stage: 'show' }]
      : stage === 'show' && !timeUp ? [MATCH_INTRO_MS, { timeUp: true }]
      : stage === 'leave' ? [INTRO_FADE_MS, { stage: 'done' }]
      : null;
    if (!next) return;
    const timer = setTimeout(() => setIntro(i => i.matchId === id ? { ...i, ...next[1] } : i), next[0]);
    return () => clearTimeout(timer);
  }, [id, stage, timeUp]);

  // 自分の画面で演出を見終えた。ホストは全員の分がそろってからカウントダウンを始める
  useEffect(() => {
    if (id && timeUp) doneRef.current?.(id);
  }, [id, timeUp]);

  return { matchId: id, stage, waiting: stage === 'show' && timeUp && !ready };
}
