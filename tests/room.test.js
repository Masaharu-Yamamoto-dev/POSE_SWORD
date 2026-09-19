import test from 'node:test';
import assert from 'node:assert/strict';
import { HostRoom, MAX_HP, MAX_ATTACK, MAX_WEIGHT } from '../src/network/HostRoom.js';

const sword = { name: 'テスト剣', hp: 100, attack: 50, weight: 50, imageStr: 'aGVsbG8=' };
function fullRoom(count = 4) {
  const room = new HostRoom({ roomEpoch: 'room-a', hostSword: sword });
  for (let i = 1; i < count; i++) {
    room.reserve(`peer-${i}`);
    room.join(`peer-${i}`, sword);
  }
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  return room;
}

test('pending connections reserve seats synchronously, including the host', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword });
  assert.equal(room.reserve('a'), true);
  assert.equal(room.reserve('b'), true);
  assert.equal(room.reserve('c'), true);
  assert.equal(room.reserve('d'), false);
  assert.equal(room.reserve('a'), false);
  room.removeConnection('b');
  assert.equal(room.reserve('d'), true);
});

test('players keep distinct IDs when a vacated seat is reused', () => {
  const room = fullRoom();
  const oldId = room.playerForConnection('peer-1');
  room.removeConnection('peer-1');
  room.reserve('new-peer');
  room.join('new-peer', sword);
  assert.notEqual(room.playerForConnection('new-peer'), oldId);
  assert.equal(room.snapshot().players.length, 4);
  assert.ok(room.snapshot().players.every(p => !p.ready));
});

test('any two to four ready players may start, but not one and not a half-ready room', () => {
  const room = fullRoom();
  room.setReady(room.playerForConnection('peer-3'), false);
  assert.equal(room.canStart(), false);
  room.removeConnection('peer-3');
  assert.equal(room.canStart(), false); // 入退室は全員の準備を解除する
  assert.throws(() => room.prepare());
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), true);
  assert.equal(room.prepare().players.length, 3);
});

test('a two-seat room turns away a third connection', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword, seatLimit: 2 });
  assert.equal(room.reserve('a'), true);
  assert.equal(room.reserve('b'), false);
  room.join('a', sword);
  assert.deepEqual(room.snapshot().players.map(p => p.slotIndex), [0, 1]);
  assert.equal(room.snapshot().seatLimit, 2);
});

test('seat limits outside two to four are rejected', () => {
  for (const seatLimit of [1, 5, 2.5, '2', null]) {
    assert.throws(() => new HostRoom({ roomEpoch: 'a', hostSword: sword, seatLimit }));
  }
});

test('部屋を作るときに最初のルールを決められる', () => {
  const koma = new HostRoom({ roomEpoch: 'a', hostSword: sword, gameMode: '1' });
  assert.equal(koma.snapshot().gameMode, '1');
  assert.equal(new HostRoom({ roomEpoch: 'a', hostSword: sword }).snapshot().gameMode, '0');
  for (const gameMode of ['2', '', 0, null]) {
    assert.throws(() => new HostRoom({ roomEpoch: 'a', hostSword: sword, gameMode }));
  }
});

test('an auto-start room does not wait for ready buttons', () => {
  const auto = new HostRoom({ roomEpoch: 'a', hostSword: sword, seatLimit: 2, autoStart: true });
  auto.reserve('a'); auto.join('a', sword);
  assert.ok(auto.snapshot().players.every(p => !p.ready));
  assert.equal(auto.canStart(), true);
  const manual = new HostRoom({ roomEpoch: 'a', hostSword: sword, seatLimit: 2 });
  manual.reserve('a'); manual.join('a', sword);
  assert.equal(manual.canStart(), false);
});

test('an auto-start room still needs two connected players in the lobby', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword, autoStart: true });
  assert.equal(room.canStart(), false);
  room.reserve('a'); room.join('a', sword);
  assert.equal(room.canStart(), true);
  room.removeConnection('a');
  assert.equal(room.canStart(), false);
});

test('a lone host cannot start a match', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword });
  room.setReady('p0', true);
  assert.equal(room.canStart(), false);
  assert.throws(() => room.prepare());
});

test('vacated seats close up so slots stay contiguous for the arena', () => {
  const room = fullRoom();
  room.removeConnection('peer-1');
  assert.deepEqual(room.snapshot().players.map(p => p.slotIndex), [0, 1, 2]);
  assert.deepEqual(room.snapshot().players.map(p => p.playerId), ['p0', 'p2', 'p3']);
  room.reserve('late'); room.join('late', sword);
  assert.deepEqual(room.snapshot().players.map(p => p.slotIndex), [0, 1, 2, 3]);
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.deepEqual(room.prepare().players.map(p => p.slotIndex).sort(), [0, 1, 2, 3]);
});

test('all four initialize the same match before the countdown starts', () => {
  const room = fullRoom();
  const match = room.prepare();
  assert.equal(room.reserve('late-peer'), false);
  assert.equal(room.markLoaded('p0', 'old-match'), false);
  for (const p of match.players.slice(0, 3)) room.markLoaded(p.playerId, match.matchId);
  assert.equal(room.snapshot().phase, 'LOADING');
  room.markLoaded(match.players[3].playerId, match.matchId);
  assert.equal(room.snapshot().phase, 'COUNTDOWN');
});

test('input is bound to its connection, deduplicated, and blocked until play', () => {
  const room = fullRoom();
  const match = room.prepare();
  const msg = { matchId: match.matchId, seq: 1, action: 'PRIMARY', direction: 'RIGHT', playerId: 'p0' };
  assert.equal(room.acceptInput('peer-2', msg, 1000), null);
  for (const p of match.players) room.markLoaded(p.playerId, match.matchId);
  room.beginPlaying(match.matchId);
  const input = room.acceptInput('peer-2', msg, 1000);
  assert.equal(input.playerId, room.playerForConnection('peer-2'));
  assert.equal(room.acceptInput('peer-2', msg, 1200), null);
  assert.equal(room.acceptInput('unknown', { ...msg, seq: 2 }, 1200), null);
  assert.equal(room.acceptInput('peer-2', { ...msg, seq: 2, matchId: 'old' }, 1200), null);
  assert.equal(room.acceptInput('peer-2', { ...msg, seq: 2, action: 'Tornado' }, 1200), null);
  assert.equal(room.acceptInput('peer-2', { ...msg, seq: 2 }, 1001), null);
  assert.ok(room.acceptInput('peer-2', { ...msg, seq: 2 }, 1200));
});

test('a loading disconnect aborts the start; a playing disconnect is a forfeit', () => {
  const room = fullRoom();
  room.prepare();
  assert.equal(room.removeConnection('peer-3').kind, 'ABORT');
  assert.equal(room.snapshot().phase, 'LOBBY');
  const playing = fullRoom();
  const match = playing.prepare();
  for (const p of match.players) playing.markLoaded(p.playerId, match.matchId);
  playing.beginPlaying(match.matchId);
  assert.equal(playing.removeConnection('peer-3').kind, 'FORFEIT');
  assert.equal(playing.snapshot().phase, 'PLAYING');
});

test('changing weapons clears readiness and rejects invalid stats', () => {
  const room = fullRoom();
  room.updateSword('p0', { ...sword, name: '新しい剣' });
  assert.equal(room.canStart(), false);
  // 送られてくる値は柄補正込みの最終値なので、上限は剣本体の最大値そのものではなく
  // 柄が乗せうる最大の補正込みの値(MAX_HP等)。それより上だけを不正として弾く。
  for (const hp of [NaN, Infinity, -1, 0, MAX_HP + 1]) {
    assert.throws(() => room.updateSword('p0', { ...sword, hp }));
  }
  for (const attack of [NaN, Infinity, -1, 0, MAX_ATTACK + 1]) {
    assert.throws(() => room.updateSword('p0', { ...sword, attack }));
  }
  for (const weight of [NaN, Infinity, -1, 0, MAX_WEIGHT + 1]) {
    assert.throws(() => room.updateSword('p0', { ...sword, weight }));
  }
  assert.doesNotThrow(() => room.updateSword('p0', { ...sword, hp: MAX_HP, attack: MAX_ATTACK, weight: MAX_WEIGHT }));
  assert.throws(() => room.updateSword('missing', sword));
});

test('rematch uses a new match ID and clears previous load/input state', () => {
  const room = fullRoom();
  const first = room.prepare();
  for (const p of first.players) room.markLoaded(p.playerId, first.matchId);
  room.beginPlaying(first.matchId);
  room.finish(first.matchId);
  for (const p of first.players) room.returnToLobby(p.playerId);
  for (const p of first.players) room.setReady(p.playerId, true);
  const second = room.prepare();
  assert.notEqual(first.matchId, second.matchId);
  assert.equal(room.markLoaded('p0', first.matchId), false);
  assert.equal(room.snapshot().phase, 'LOADING');
});

test('a two-player room uses the same lifecycle', () => {
  const room = fullRoom(2);
  assert.equal(room.canStart(), true);
  assert.equal(room.prepare().players.length, 2);
});

test('snapshots cannot mutate authoritative room state', () => {
  const room = fullRoom();
  room.snapshot().players[0].swordData.hp = 1;
  assert.equal(room.snapshot().players[0].swordData.hp, 100);
});

test('leaving results releases the rematch barrier for the remaining players', () => {
  const room = fullRoom();
  const match = room.prepare();
  for (const p of match.players) room.markLoaded(p.playerId, match.matchId);
  room.beginPlaying(match.matchId);
  room.finish(match.matchId);
  for (const p of match.players.slice(0, 3)) room.returnToLobby(p.playerId);
  assert.equal(room.snapshot().phase, 'RESULT');
  room.removeConnection('peer-3');
  assert.equal(room.snapshot().phase, 'LOBBY');
  assert.equal(room.reserve('replacement'), true);
});

test('configuration changes invalidate all ready states and are locked during loading', () => {
  const room = fullRoom();
  room.setGameMode('1');
  assert.ok(room.snapshot().players.every(p => !p.ready));
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  room.prepare();
  assert.throws(() => room.setGameMode('0'));
  assert.throws(() => room.updateSword('p0', sword));
  assert.equal(room.setReady('p0', false), false);
});

test('invalid join does not occupy a permanent player seat', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword });
  room.reserve('bad');
  assert.throws(() => room.join('bad', { ...sword, hp: -1 }));
  assert.equal(room.snapshot().players.length, 1);
  room.removeConnection('bad');
  for (const id of ['a', 'b', 'c']) {
    assert.equal(room.reserve(id), true);
    room.join(id, sword);
  }
  assert.equal(room.snapshot().players.length, 4);
});

// ===== 1vs3（ボス vs 三人組） =====

test('solo mode waits for a full room of four before it may start', () => {
  const room = new HostRoom({ roomEpoch: 'solo', hostSword: sword, soloMode: true });
  for (const count of [2, 3]) {
    room.reserve(`peer-${count}`);
    room.join(`peer-${count}`, sword);
    for (const p of room.snapshot().players) room.setReady(p.playerId, true);
    room.setBossPlayer('p0');
    assert.equal(room.snapshot().players.length, count);
    assert.equal(room.canStart(), false, `${count}人では開始できない`);
    assert.throws(() => room.prepare());
  }
  room.reserve('peer-4');
  room.join('peer-4', sword);
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  room.setBossPlayer('p0');
  assert.equal(room.canStart(), true);
});

test('solo mode cannot start until the host names a boss', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.snapshot().bossPlayerId, null);
  assert.equal(room.canStart(), false, '指名前は開始できない');
  assert.throws(() => room.prepare());
  room.setBossPlayer('p2');
  assert.equal(room.snapshot().bossPlayerId, 'p2');
  assert.equal(room.canStart(), true);
});

test('naming a boss does not clear anyone\'s ready state', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  const version = room.snapshot().readyVersion;
  room.setBossPlayer('p1');
  room.setBossPlayer('p3');
  assert.ok(room.snapshot().players.every(p => p.ready), '指名を変えても準備完了は保たれる');
  assert.equal(room.snapshot().readyVersion, version);
});

test('a boss who leaves is unnamed again so the room cannot start', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  room.setBossPlayer(room.playerForConnection('peer-2'));
  room.removeConnection('peer-2');
  assert.equal(room.snapshot().bossPlayerId, null, '抜けた人の指名は外れる');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), false);
});

test('only a seated, connected player may be named boss', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  assert.throws(() => room.setBossPlayer('p9'));
  assert.throws(() => room.setBossPlayer(null));
  room.setBossPlayer('p0');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  room.prepare();
  assert.throws(() => room.setBossPlayer('p1'), 'ロビー外では指名できない');
});

test('a solo room that loses a player cannot start until the seat is filled again', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  room.setBossPlayer('p0');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), true);
  room.removeConnection('peer-3');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), false);
});

test('solo mode is published to guests and clears ready like any other rule change', () => {
  const room = fullRoom();
  assert.equal(room.snapshot().soloMode, false);
  room.setSoloMode(true);
  assert.equal(room.snapshot().soloMode, true);
  assert.ok(room.snapshot().players.every(p => !p.ready), 'ルール変更で準備完了は解除される');
});

test('solo mode and lives mode switch each other off instead of stacking', () => {
  const room = fullRoom();
  room.setLivesMode(true);
  room.setSoloMode(true);
  assert.equal(room.snapshot().livesMode, false);
  assert.equal(room.snapshot().soloMode, true);
  room.setLivesMode(true);
  assert.equal(room.snapshot().soloMode, false);
  assert.equal(room.snapshot().livesMode, true);
  assert.throws(() => new HostRoom({ roomEpoch: 'x', hostSword: sword, soloMode: true, livesMode: true }));
});

test('rules cannot be changed once the match has left the lobby', () => {
  const room = fullRoom();
  room.prepare();
  assert.throws(() => room.setSoloMode(true));
});

test('the suppress input is accepted and keeps its own action name', () => {
  const room = fullRoom();
  room.prepare();
  const matchId = room.snapshot().matchId;
  const suppress = { matchId, seq: 1, action: 'SUPPRESS' };
  assert.equal(room.acceptInput('peer-1', suppress, 1000), null, '対戦中でなければ通らない');
  for (const p of room.snapshot().players) room.markLoaded(p.playerId, matchId);
  room.beginPlaying(matchId);
  const accepted = room.acceptInput('peer-1', suppress, 1000);
  assert.equal(accepted.action, 'SUPPRESS');
  assert.equal(accepted.playerId, room.playerForConnection('peer-1'));
  assert.equal(accepted.direction, undefined);
  // 連番と間隔の制限は他の入力と同じ
  assert.equal(room.acceptInput('peer-1', suppress, 1200), null);
  assert.equal(room.acceptInput('peer-1', { ...suppress, seq: 2 }, 1001), null);
  assert.ok(room.acceptInput('peer-1', { ...suppress, seq: 2 }, 1200));
  // 知らないアクションは従来どおり捨てる
  assert.equal(room.acceptInput('peer-1', { ...suppress, seq: 3, action: 'STUN' }, 1400), null);
});
