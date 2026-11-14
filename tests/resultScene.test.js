import test from 'node:test';
import assert from 'node:assert/strict';
import { RESULT_MOTIONS, pickMotion, resultScene, sortStandings } from '../src/screens/resultScene.js';

// 結果の形は Unity（MatchRules.cs / MultiplayerManager.cs）が送るものに合わせる
const players = ['p0', 'p1', 'p2', 'p3'].map((playerId, slotIndex) => ({ playerId, slotIndex, swordData: { name: playerId } }));
const score = (playerId, rank, kills = 0, damageDealt = 0) => ({ playerId, rank, kills, damageDealt, damageTaken: 0 });
const room = (extra = {}) => ({ gameMode: '0', livesMode: false, soloMode: false, bossPlayerId: null, ...extra });
const soloRoom = (extra = {}) => room({ soloMode: true, bossPlayerId: 'p0', ...extra });

test('一本勝負で一人が勝つと win。演出は跳ね返りか天から突き刺さるのどちらか', () => {
  const result = { matchId: 'm1', winnerId: 'p2', draw: false, winnerTeam: 2,
    standings: [score('p2', 1, 3), score('p0', 2), score('p1', 3), score('p3', 4)] };
  const scene = resultScene({ result, room: room(), players });
  assert.equal(scene.kind, 'win');
  assert.equal(scene.winnerId, 'p2');
  assert.ok(RESULT_MOTIONS.includes(scene.motion));
  assert.equal(scene.lives, false);
  assert.equal(scene.koma, false);
});

test('一人の勝者の演出は試合ごとにランダムで、同じ試合なら毎回同じ', () => {
  const seen = new Set();
  for (let i = 0; i < 40; i++) seen.add(pickMotion(`match-${i}`));
  assert.deepEqual([...seen].sort(), [...RESULT_MOTIONS].sort());
  assert.equal(pickMotion('match-7'), pickMotion('match-7'));
});

test('演出を指定したときはそれを使う', () => {
  const result = { matchId: 'm1', winnerId: 'p1', draw: false, standings: [score('p1', 1), score('p0', 2)] };
  assert.equal(resultScene({ result, room: room(), players, motion: 'drop' }).motion, 'drop');
  assert.equal(resultScene({ result, room: room(), players, motion: 'bounce' }).motion, 'bounce');
});

test('残機制の勝ちは win で、控えの剣を出す印が付く', () => {
  const result = { matchId: 'm2', winnerId: 'p1', draw: false, standings: [score('p1', 1), score('p0', 2)] };
  const scene = resultScene({ result, room: room({ livesMode: true }), players });
  assert.equal(scene.kind, 'win');
  assert.equal(scene.lives, true);
});

test('独楽は同じ演出を柄なしで出す印が付く', () => {
  const result = { matchId: 'm3', winnerId: 'p1', draw: false, standings: [score('p1', 1), score('p0', 2)] };
  const scene = resultScene({ result, room: room({ gameMode: '1' }), players });
  assert.equal(scene.kind, 'win');
  assert.equal(scene.koma, true);
});

test('個人戦の引き分けは、1位の人たちだけが相打ちに出る（先に倒れた人は出ない）', () => {
  // MatchRulesTests.SimultaneousEliminationsShareRankAndMutualFinalHitIsDraw と同じ形
  const result = { matchId: 'm4', winnerId: null, draw: true, winnerTeam: -1,
    standings: [score('p2', 3), score('p3', 3), score('p0', 1, 2, 200), score('p1', 1, 2, 200)] };
  const scene = resultScene({ result, room: room(), players });
  assert.equal(scene.kind, 'draw');
  assert.deepEqual(scene.lineupIds, ['p0', 'p1']);
  assert.deepEqual(scene.clasherIds, ['p0', 'p1']);
  assert.equal(scene.motion, null);
});

test('4人同時の引き分けは4本が相打ちに出る', () => {
  const result = { matchId: 'm5', winnerId: null, draw: true,
    standings: ['p3', 'p1', 'p0', 'p2'].map(id => score(id, 1, 1, 100)) };
  const scene = resultScene({ result, room: room(), players });
  assert.equal(scene.kind, 'draw');
  assert.deepEqual(scene.lineupIds, ['p0', 'p1', 'p2', 'p3']);
});

test('表の並び：順位 → 撃破の多い順 → 与ダメージの多い順 → プレイヤー番号の若い順', () => {
  const sorted = sortStandings([
    score('p3', 1, 1, 200), score('p1', 1, 1, 200), score('p2', 1, 1, 300), score('p0', 2, 5, 900), score('p4', 1, 2, 10),
  ], { slotOf: id => Number(id.slice(1)) });
  assert.deepEqual(sorted.map(s => s.playerId), ['p4', 'p2', 'p1', 'p3', 'p0']);
});

test('1vs3 でボスが勝つと bossWin。いつも天から突き刺さる演出で、3人は表の順に足元へ', () => {
  const result = { matchId: 'm6', winnerId: 'p0', draw: false, winnerTeam: 0,
    standings: [score('p0', 1, 3), score('p3', 2, 0, 50), score('p1', 2, 0, 120), score('p2', 2, 0, 120)] };
  const scene = resultScene({ result, room: soloRoom(), players });
  assert.equal(scene.kind, 'bossWin');
  assert.equal(scene.motion, 'drop');
  assert.equal(scene.winnerId, 'p0');
  assert.deepEqual(scene.challengerIds, ['p1', 'p2', 'p3']);
});

test('1vs3 で3人側が勝つと teamWin（winnerId は空）。並びは撃破 → 与ダメ → 番号', () => {
  const result = { matchId: 'm7', winnerId: '', draw: false, winnerTeam: 1,
    standings: [score('p1', 1, 0, 310), score('p2', 1, 1, 380), score('p3', 1, 0, 290), score('p0', 2, 1, 650)] };
  const scene = resultScene({ result, room: soloRoom(), players });
  assert.equal(scene.kind, 'teamWin');
  assert.equal(scene.winnerId, null);
  assert.deepEqual(scene.lineupIds, ['p2', 'p1', 'p3']);
  assert.deepEqual(scene.standings.map(s => s.playerId), ['p2', 'p1', 'p3', 'p0']);
});

test('1vs3 の3人側が1人だけで勝った（winnerId にその人が入る）ときも teamWin', () => {
  const two = players.slice(0, 2);
  const result = { matchId: 'm8', winnerId: 'p1', draw: false, winnerTeam: 1, standings: [score('p1', 1), score('p0', 2)] };
  const scene = resultScene({ result, room: soloRoom(), players: two });
  assert.equal(scene.kind, 'teamWin');
  assert.deepEqual(scene.lineupIds, ['p1']);
});

test('1vs3 の引き分けは soloDraw。ボスと3人がぶつかり、並ぶのはボスと3人側の先頭。表はボスが一番上', () => {
  // MatchRulesTests.MutualTeamWipeIsADraw と同じ流れ（先に2人が倒れ、最後の1人とボスが相打ち）
  const result = { matchId: 'm9', winnerId: null, draw: true, winnerTeam: -1,
    standings: [score('p1', 4), score('p2', 3), score('p3', 1, 1, 300), score('p0', 1, 3, 600)] };
  const scene = resultScene({ result, room: soloRoom(), players });
  assert.equal(scene.kind, 'soloDraw');
  assert.equal(scene.standings[0].playerId, 'p0');
  assert.deepEqual(scene.clasherIds, ['p0', 'p3', 'p2', 'p1']);
  assert.deepEqual(scene.lineupIds, ['p0', 'p3']);
});

test('1vs3 は残機制の印が付かない（ルール上いっしょに選べない）', () => {
  const result = { matchId: 'm10', winnerId: 'p0', draw: false, standings: [score('p0', 1), score('p1', 2)] };
  assert.equal(resultScene({ result, room: soloRoom({ livesMode: true }), players }).lives, false);
});

test('勝者も1位の組も分からないときは演出なし', () => {
  assert.equal(resultScene({ result: { matchId: 'x', winnerId: '', draw: false, standings: [] }, room: room(), players }).kind, 'none');
  assert.equal(resultScene({ result: null, room: room(), players }).kind, 'none');
  // 引き分けでも1位が1人しかいなければ相打ちにしない
  assert.equal(resultScene({ result: { matchId: 'y', draw: true, standings: [score('p0', 1), score('p1', 2)] }, room: room(), players }).kind, 'none');
});
