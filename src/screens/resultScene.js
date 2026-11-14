// ==========================================
// 結果画面の「場面」を決める（画面は描かない。tests/resultScene.test.js で確かめる）
// ==========================================
// Unity（MatchRules）から届く結果の形：
//   個人戦の勝ち      … winnerId に勝者、draw: false。勝者が1位
//   1vs3 ボス側の勝ち … winnerId にボス。ボスが1位、3人は2位
//   1vs3 3人側の勝ち  … winnerId は空（3人側が1人だけのときはその人）。3人側が全員1位、ボスは2位
//   引き分け          … draw: true、winnerId は空。最後に同時に倒れた人たちが1位
//
// 返す kind：
//   win      … 一人の勝者（一本勝負・残機制）。motion で演出を選ぶ（bounce：跳ね返り／drop：天から突き刺さる）
//   bossWin  … 1vs3 でボスが勝った（3人が札のように落ち、ボスが突き刺さる）
//   teamWin  … 1vs3 で3人側が勝った（3人でボスを斬り結んで明転）
//   draw     … 1位が2人以上の引き分け（相打ち）
//   soloDraw … 1vs3 の引き分け（ボスと3人側の相打ち）
//   none     … 演出なし（勝者も1位の組も分からないとき）

export const RESULT_MOTIONS = ['bounce', 'drop'];

// 一人の勝者の演出は、試合ごとにランダム。試合IDから決めるので、同じ試合なら全員に同じ演出が出て、
// 画面を作り直しても変わらない
export function pickMotion(matchId) {
  let h = 0;
  for (const ch of String(matchId ?? '')) h = (h * 31 + ch.codePointAt(0)) >>> 0;
  return RESULT_MOTIONS[h % RESULT_MOTIONS.length];
}

// 表の並び順：順位 → 同じ順位なら撃破の多い順 → 与ダメージの多い順 → プレイヤー番号の若い順。
// 1vs3 の引き分けだけは、ボスをいちばん上にする
export function sortStandings(standings, { slotOf = () => 99, bossFirstId = null } = {}) {
  const boss = id => (bossFirstId != null && id === bossFirstId ? 1 : 0);
  return [...(standings ?? [])].sort((a, b) =>
    boss(b.playerId) - boss(a.playerId) ||
    a.rank - b.rank ||
    (b.kills ?? 0) - (a.kills ?? 0) ||
    (b.damageDealt ?? 0) - (a.damageDealt ?? 0) ||
    slotOf(a.playerId) - slotOf(b.playerId));
}

export function resultScene({ result, room, players = [], motion } = {}) {
  const has = id => players.some(p => p.playerId === id);
  const slotOf = id => players.find(p => p.playerId === id)?.slotIndex ?? 99;
  const solo = Boolean(room?.soloMode);
  const bossId = solo ? room?.bossPlayerId ?? null : null;
  const draw = Boolean(result?.draw);
  const standings = sortStandings(result?.standings, { slotOf, bossFirstId: solo && draw ? bossId : null });
  const base = {
    standings, kind: 'none', motion: null, winnerId: null, bossId,
    lineupIds: [], clasherIds: [], challengerIds: [],
    koma: room?.gameMode === '1', lives: Boolean(room?.livesMode) && !solo,
  };
  if (!result) return base;

  const ids = list => list.map(s => s.playerId).filter(has);
  const nonBoss = ids(standings.filter(s => s.playerId !== bossId));

  if (solo && bossId && has(bossId)) {
    if (!draw && result.winnerId === bossId) {
      return { ...base, kind: 'bossWin', motion: 'drop', winnerId: bossId, challengerIds: nonBoss.slice(0, 3) };
    }
    if (!draw) {
      const team = ids(standings.filter(s => s.rank === 1 && s.playerId !== bossId)).slice(0, 3);
      if (team.length) return { ...base, kind: 'teamWin', lineupIds: team };
    }
    if (draw && nonBoss.length) {
      const team = nonBoss.slice(0, 3);
      return { ...base, kind: 'soloDraw', lineupIds: [bossId, team[0]], clasherIds: [bossId, ...team] };
    }
  }

  if (draw) {
    const tied = ids(standings.filter(s => s.rank === 1)).slice(0, 4);
    return tied.length >= 2 ? { ...base, kind: 'draw', lineupIds: tied, clasherIds: tied } : base;
  }

  if (result.winnerId && has(result.winnerId)) {
    return { ...base, kind: 'win', winnerId: result.winnerId, motion: RESULT_MOTIONS.includes(motion) ? motion : pickMotion(result.matchId) };
  }
  return base;
}
