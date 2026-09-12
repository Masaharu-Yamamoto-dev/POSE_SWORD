import test from 'node:test';
import assert from 'node:assert/strict';
import { MatchSeeker, SEARCH_INTERVAL, HOST_INTERVAL, JOIN_TIMEOUT } from '../src/network/matchSeeker.js';

// useRoom の代わり。部屋の成立や失敗をテストから手で起こす。
function fakeRoom() {
  return {
    calls: [],
    current: null,
    createRoom(targetSize) {
      this.calls.push(['create', targetSize]);
      this.current = { roomId: null, players: 1, gameMode: '0', phase: 'LOBBY', inRoom: true, error: null };
    },
    joinRoom(roomId) {
      this.calls.push(['join', roomId]);
      this.current = { roomId, players: 0, gameMode: '0', phase: 'LOBBY', inRoom: false, error: null };
    },
    leave() { this.calls.push(['leave']); this.current = null; },
    state() { return this.current; },
    opened(roomId) { this.current = { ...this.current, roomId }; },
    accepted(players = 2) { this.current = { ...this.current, inRoom: true, players }; },
    update(patch) { this.current = { ...this.current, ...patch }; },
    failed(error = '満員です') { this.current = { ...this.current, error }; },
  };
}

function setup({ enter, poll, targetSize = 2 } = {}) {
  const nowRef = { value: 1_000 };
  const calls = [];
  const client = {
    calls,
    async enter(args) {
      calls.push(['enter', args]);
      return enter ? enter(args, calls) : { ok: true, ticket: 'ticket-1', waiting: { total: 1 } };
    },
    async poll(args) {
      calls.push(['poll', args]);
      return poll ? poll(args, calls) : { ok: true, rooms: [], waiting: { total: 1 } };
    },
    async leave(args) { calls.push(['leave', args]); return { ok: true }; },
  };
  const room = fakeRoom();
  const seeker = new MatchSeeker({ client, room, targetSize, now: () => nowRef.value });
  return { seeker, client, room, calls, advance: ms => { nowRef.value += ms; } };
}

const lastCall = (calls, kind) => calls.filter(([name]) => name === kind).at(-1)?.[1];

test('満員なら待ってから入場をやり直す', async () => {
  let attempt = 0;
  const s = setup({ enter: () => (attempt++ === 0
    ? { ok: false, reason: 'FULL', waiting: { total: 16 }, retryAfter: 10 }
    : { ok: true, ticket: 'ticket-1', waiting: { total: 1 } }) });
  s.seeker.start();
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'FULL');
  assert.equal(s.seeker.view().waiting.total, 16);
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'FULL');
  s.advance(10_000);
  await s.seeker.tick();
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'SEARCHING');
});

test('待合所が使えなければ探索をあきらめて知らせる', async () => {
  const s = setup({ enter: () => ({ ok: false, reason: 'UNAVAILABLE' }) });
  s.seeker.start();
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'UNAVAILABLE');
  assert.match(s.seeker.view().error, /ロビーID/);
});

test('候補が無いまま空振りが続くと自分がホストになる', async () => {
  const s = setup();
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'SEARCHING');
  s.advance(SEARCH_INTERVAL);
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'HOSTING');
  assert.deepEqual(s.room.calls.at(-1), ['create', 2]);
});

test('候補が見つかれば参加し、成立したら券を返して終わる', async () => {
  const s = setup({ poll: () => ({ ok: true, rooms: [{ roomId: '111111', players: 1 }], waiting: { total: 2 } }) });
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'JOINING');
  assert.deepEqual(s.room.calls.at(-1), ['join', '111111']);
  s.room.accepted(2);
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'DONE');
  assert.deepEqual(lastCall(s.calls, 'leave'),
    { ticket: 'ticket-1', targetSize: 2, roomId: null, hostToken: null });
});

test('入れなかった部屋は順に見送り、除外して探索へ戻る', async () => {
  let served = false;
  const s = setup({ poll: () => (served ? { ok: true, rooms: [], waiting: { total: 1 } }
    : (served = true, { ok: true, rooms: [{ roomId: '111111' }, { roomId: '222222' }], waiting: { total: 3 } })) });
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  s.room.failed();
  await s.seeker.tick();
  assert.deepEqual(s.room.calls.at(-1), ['join', '222222']);
  s.advance(JOIN_TIMEOUT);
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'SEARCHING');
  await s.seeker.tick();
  assert.deepEqual(lastCall(s.calls, 'poll').exclude, ['111111', '222222']);
});

test('ひとりで待つホストは、先に待っている部屋へ券を持ったまま移る', async () => {
  const s = setup({ poll: args => (args.room
    ? { ok: true, hostToken: 'host-1', rooms: [{ roomId: '999999' }], waiting: { total: 2 } }
    : { ok: true, rooms: [], waiting: { total: 1 } }) });
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  s.advance(SEARCH_INTERVAL);
  await s.seeker.tick();
  s.room.opened('111111');
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'JOINING');
  assert.deepEqual(lastCall(s.calls, 'leave'),
    { ticket: 'ticket-1', targetSize: 2, roomId: '111111', hostToken: 'host-1', keepTicket: true });
  assert.deepEqual(s.room.calls.at(-1), ['join', '999999']);
});

test('ロビーIDが先客に使われていたら別のIDで作り直す', async () => {
  const s = setup({ poll: args => (args.room ? { ok: false, reason: 'ROOM_TAKEN' }
    : { ok: true, rooms: [], waiting: { total: 1 } }) });
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  s.advance(SEARCH_INTERVAL);
  await s.seeker.tick();
  s.room.opened('111111');
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'HOSTING');
  assert.deepEqual(s.room.calls.slice(-2), [['leave'], ['create', 2]]);
});

test('4人部屋のホストは人が来ても募集を続け、満員で取り下げる', async () => {
  const s = setup({ targetSize: 4,
    poll: () => ({ ok: true, hostToken: 'host-1', rooms: [], waiting: { total: 2 } }) });
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  s.advance(SEARCH_INTERVAL);
  await s.seeker.tick();
  s.room.opened('111111');
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'HOSTING');

  s.room.update({ players: 2 });
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'ADVERTISING');
  s.advance(HOST_INTERVAL);
  await s.seeker.tick();
  assert.equal(lastCall(s.calls, 'poll').room.players, 2, '募集は続いている');

  s.room.update({ players: 4 });
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'DONE');
  assert.deepEqual(lastCall(s.calls, 'leave'),
    { ticket: 'ticket-1', targetSize: 4, roomId: '111111', hostToken: 'host-1' });
});

test('試合が始まったホストは募集を取り下げる', async () => {
  const s = setup({ targetSize: 4, poll: () => ({ ok: true, hostToken: 'host-1', rooms: [], waiting: { total: 2 } }) });
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  s.advance(SEARCH_INTERVAL);
  await s.seeker.tick();
  s.room.opened('111111');
  await s.seeker.tick();
  s.room.update({ players: 2 });
  await s.seeker.tick();
  s.room.update({ phase: 'LOADING' });
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'DONE');
  assert.equal(lastCall(s.calls, 'leave').roomId, '111111');
});

test('券が失効していたら入場からやり直す', async () => {
  const s = setup({ poll: () => ({ ok: false, reason: 'EXPIRED' }) });
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'ENTERING');
  await s.seeker.tick();
  assert.equal(s.seeker.view().phase, 'SEARCHING');
});

test('中止すると部屋も券も残らない', async () => {
  const s = setup();
  s.seeker.start();
  await s.seeker.tick();
  await s.seeker.tick();
  s.advance(SEARCH_INTERVAL);
  await s.seeker.tick();
  s.room.opened('111111');
  await s.seeker.cancel();
  assert.equal(s.seeker.view().phase, 'CANCELLED');
  assert.deepEqual(s.room.calls.at(-1), ['leave']);
  assert.deepEqual(lastCall(s.calls, 'leave'),
    { ticket: 'ticket-1', targetSize: 2, roomId: '111111', hostToken: null });
  await s.seeker.tick();
  assert.equal(s.calls.filter(([name]) => name === 'poll').length, 2);
});
