import test from 'node:test';
import assert from 'node:assert/strict';
import { RoomSession } from '../src/network/RoomSession.js';

const sword = { name: '剣', hp: 100, attack: 50, weight: 50, imageStr: 'aGVsbG8=' };
// Queue messages like a DataChannel (never synchronously re-enter send()).
function network() {
  const queue = [];
  const link = (peer) => {
    const ends = [0, 1].map(() => ({ peer, open: true, bufferSize: 0, handlers: {}, sent: [],
      on(type, fn) { (this.handlers[type] ??= []).push(fn); },
      emit(type, value) { for (const fn of this.handlers[type] ?? []) fn(value); },
    }));
    ends.forEach((end, i) => {
      end.send = data => { end.sent.push(structuredClone(data)); queue.push(() => ends[1 - i].emit('data', structuredClone(data))); };
      end.close = () => { if (!end.open) return; ends.forEach(e => { e.open = false; }); queue.push(() => ends.forEach(e => e.emit('close'))); };
    });
    return ends;
  };
  return { link, flush() { let n = 0; while (queue.length) { if (++n > 10000) throw Error('message loop'); queue.shift()(); } } };
}

function setup() {
  const net = network();
  let now = 0;
  const commands = [[], [], [], []];
  const host = new RoomSession({ isHost: true, roomEpoch: 'epoch', sword, now: () => now,
    onUnity: c => commands[0].push(c) });
  const guests = [];
  const links = [];
  for (let i = 1; i <= 3; i++) {
    const guest = new RoomSession({ sword, now: () => now, onUnity: c => commands[i].push(c) });
    const [a, b] = net.link(`peer-${i}`);
    host.attach(a); guest.attach(b);
    guests.push(guest); links.push([a, b]);
    net.flush();
  }
  return { host, guests, links, commands, net, advance(ms) { now += ms; } };
}

function start(s) {
  s.host.setReady(true); s.guests.forEach(g => g.setReady(true)); s.net.flush();
  s.host.prepare(); s.net.flush();
  const matchId = s.host.view().room.matchId;
  [s.host, ...s.guests].forEach(p => p.unityEvent('INITIALIZED', { matchId })); s.net.flush();
  s.host.unityEvent('PLAYING', { matchId }); s.net.flush();
  return matchId;
}

test('four sessions receive the same roster and wait for every Unity instance', () => {
  const s = setup();
  for (const guest of s.guests) assert.equal(guest.view().room.players.length, 4);
  s.host.setReady(true); s.guests.forEach(g => g.setReady(true)); s.net.flush();
  assert.equal(s.host.view().canStart, true);
  s.host.prepare(); s.net.flush();
  const id = s.host.view().room.matchId;
  for (const p of [s.host, ...s.guests.slice(0, 2)]) p.unityEvent('INITIALIZED', { matchId: id });
  s.net.flush();
  assert.equal(s.host.view().room.phase, 'LOADING');
  s.guests[2].unityEvent('INITIALIZED', { matchId: id }); s.net.flush();
  assert.equal(s.host.view().room.phase, 'COUNTDOWN');
  assert.equal(s.commands[0].filter(c => c.method === 'BeginMultiplayer').length, 1);
  s.guests[2].unityEvent('INITIALIZED', { matchId: id }); s.net.flush();
  assert.equal(s.commands[0].filter(c => c.method === 'BeginMultiplayer').length, 1);
});

test('guest input goes once to its own sword on the host; guest sync is ignored', () => {
  const s = setup(); const matchId = start(s);
  const guest = s.guests[1];
  guest.unityEvent('INPUT', { matchId, action: 'PRIMARY', direction: 'LEFT', playerId: 'p0' }); s.net.flush();
  const input = s.commands[0].filter(c => c.method === 'ReceiveMultiplayerInput').at(-1).data;
  assert.equal(input.playerId, guest.view().localPlayerId);
  assert.equal(input.direction, 'LEFT');
  const before = s.commands[0].length;
  s.links[1][1].send({ type: 'SYNC', protocolVersion: 2, roomEpoch: 'epoch', matchId, tick: 999 }); s.net.flush();
  assert.equal(s.commands[0].length, before);
});

test('only host results end the match, are acknowledged, and survive a host disconnect', () => {
  const s = setup(); const matchId = start(s);
  const result = { matchId, draw: false, winnerId: 'p2', standings: [] };
  s.host.unityEvent('RESULT', result); s.net.flush();
  assert.deepEqual(s.guests[0].view().result, result);
  const sends = s.links[0][0].sent.filter(m => m.type === 'RESULT').length;
  s.advance(2000); s.host.pump(); s.net.flush();
  assert.equal(s.links[0][0].sent.filter(m => m.type === 'RESULT').length, sends);
  s.links[0][0].close(); s.net.flush();
  assert.deepEqual(s.guests[0].view().result, result);
  assert.equal(s.guests[0].view().closed, true);
});

test('loading timeout cancels all clients; next match ignores old initialization', () => {
  const s = setup();
  s.host.setReady(true); s.guests.forEach(g => g.setReady(true)); s.net.flush();
  s.host.prepare(); s.net.flush();
  const oldId = s.host.view().room.matchId;
  // Keep transports healthy while Unity fails to initialize.
  for (let i = 0; i < 61; i++) { s.advance(1000); s.guests.forEach(g => g.pump()); s.net.flush(); s.host.pump(); s.net.flush(); }
  assert.equal(s.host.view().room.phase, 'LOBBY');
  assert.equal(s.guests[0].view().room.phase, 'LOBBY');
  assert.equal(s.commands[1].filter(c => c.method === 'StopMultiplayer').length, 1);
  s.host.setReady(true); s.guests.forEach(g => g.setReady(true)); s.net.flush();
  s.host.prepare(); s.net.flush();
  s.guests[0].unityEvent('INITIALIZED', { matchId: oldId }); s.net.flush();
  assert.equal(s.host.view().room.players.find(p => p.playerId === s.guests[0].view().localPlayerId).loaded, false);
});

test('playing disconnect forfeits only that player and blocks replacement join', () => {
  const s = setup(); const matchId = start(s);
  s.links[0][0].close(); s.net.flush();
  assert.equal(s.host.view().room.phase, 'PLAYING');
  assert.deepEqual(s.commands[0].at(-1), { method: 'ForfeitMultiplayer', data: { matchId, playerId: s.guests[0].view().localPlayerId } });
  const [a, b] = s.net.link('late');
  const late = new RoomSession({ sword }); s.host.attach(a); late.attach(b); s.net.flush();
  assert.equal(late.view().closed, true);
});

test('snapshots exclude images and stale ticks, rematches reinitialize all participants', () => {
  const s = setup(); const matchId = start(s);
  s.host.unityEvent('SYNC', { matchId, tick: 10, players: [{ playerId: 'p0', hp: 100 }] }); s.net.flush();
  s.host.unityEvent('SYNC', { matchId, tick: 9, players: [] }); s.net.flush();
  assert.equal(s.guests[0].view().sync.tick, 10);
  assert.ok(s.links[0][0].sent.filter(m => m.type === 'STATE').every(m => !JSON.stringify(m).includes('imageStr')));
  s.host.unityEvent('RESULT', { matchId, standings: [], winnerId: 'p0' }); s.net.flush();
  [s.host, ...s.guests].forEach(p => p.returnToLobby()); s.net.flush();
  const nextId = start(s);
  assert.notEqual(nextId, matchId);
  assert.equal(s.guests[0].view().result, null);
});

test('protocol mismatch and a silent handshake release their reserved seats', () => {
  const s = setup();
  s.links[0][0].close(); s.net.flush();
  const [a, b] = s.net.link('old'); s.host.attach(a);
  b.send({ type: 'JOIN', protocolVersion: 1, swordData: sword }); s.net.flush();
  assert.equal(a.open, false);
  const [c] = s.net.link('silent'); s.host.attach(c);
  s.advance(11000); s.host.pump(); s.net.flush();
  assert.equal(c.open, false);
});

test('host cannot start until every guest acknowledges the latest weapon roster', () => {
  const s = setup();
  const send = s.links[2][1].send;
  s.links[2][1].send = m => { if (m.type !== 'ROSTER_ACK') send(m); };
  s.host.publish(true); s.net.flush();
  s.host.setReady(true);
  s.guests.forEach(g => g.setReady(true));
  s.net.flush();
  assert.equal(s.host.view().canStart, false);
  assert.throws(() => s.host.prepare());
});

test('unacknowledged results are resent; duplicate results do not replay finish', () => {
  const s = setup(); const matchId = start(s);
  const send = s.links[0][1].send;
  s.links[0][1].send = m => { if (m.type !== 'RESULT_ACK') send(m); };
  s.host.unityEvent('RESULT', { matchId, standings: [], winnerId: 'p0' }); s.net.flush();
  s.advance(1500); s.host.pump(); s.net.flush();
  assert.equal(s.links[0][0].sent.filter(m => m.type === 'RESULT').length, 2);
  assert.equal(s.commands[1].filter(c => c.method === 'FinishMultiplayer').length, 1);
});

test('backpressure replaces unsent sync with latest and discards it on result', () => {
  const s = setup(); const matchId = start(s);
  s.links[0][0].bufferSize = 1;
  for (const tick of [1, 2, 3]) s.host.unityEvent('SYNC', { matchId, tick, players: [] });
  s.net.flush();
  assert.equal(s.guests[0].view().sync, null);
  s.links[0][0].bufferSize = 0;
  s.host.pump(); s.net.flush();
  assert.equal(s.guests[0].view().sync.tick, 3);
  s.links[0][0].bufferSize = 1;
  s.host.unityEvent('SYNC', { matchId, tick: 4, players: [] });
  s.host.unityEvent('RESULT', { matchId, standings: [], winnerId: 'p0' }); s.net.flush();
  s.links[0][0].bufferSize = 0;
  s.host.pump(); s.net.flush();
  assert.equal(s.guests[0].view().sync.tick, 3);
});

test('an initialization failure cancels every participant before play', () => {
  const s = setup();
  s.host.setReady(true); s.guests.forEach(g => g.setReady(true)); s.net.flush();
  s.host.prepare(); s.net.flush();
  s.guests[1].unityEvent('LOAD_FAILED', { matchId: s.host.view().room.matchId }); s.net.flush();
  assert.equal(s.host.view().room.phase, 'LOBBY');
  for (const guest of s.guests) assert.equal(guest.view().room.phase, 'LOBBY');
});

test('a delayed ready message from before a mode change cannot ready the player again', () => {
  const s = setup();
  s.guests[0].setReady(true); // Queued on the old settings.
  s.host.setGameMode('1');
  s.net.flush();
  const playerId = s.guests[0].view().localPlayerId;
  assert.equal(s.host.view().room.players.find(p => p.playerId === playerId).ready, false);
});
