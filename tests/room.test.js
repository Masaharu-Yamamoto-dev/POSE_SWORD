import test from 'node:test';
import assert from 'node:assert/strict';
import { HostRoom } from '../src/network/HostRoom.js';

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
  for (const hp of [NaN, Infinity, -1, 0, 1001]) {
    assert.throws(() => room.updateSword('p0', { ...sword, hp }));
  }
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
