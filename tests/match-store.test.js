import test from 'node:test';
import assert from 'node:assert/strict';
import { MatchStore } from '../api/_match/store.js';

// Upstash クライアントの契約を満たす最小の偽物。
// pipeline(commands) は各コマンドの結果を順に返し、HGETALL だけオブジェクトへ正規化する。
function fakeRedis(nowRef = { value: 0 }) {
  const keys = new Map();
  const zset = key => { if (!keys.has(key)) keys.set(key, new Map()); return keys.get(key); };
  const hash = zset;
  const sorted = key => [...zset(key).entries()]
    .sort((a, b) => a[1] - b[1] || (a[0] < b[0] ? -1 : 1));

  const run = ([name, key, ...args]) => {
    switch (name) {
      case 'ZREMRANGEBYSCORE': {
        const max = Number(args[1]);
        let removed = 0;
        for (const [member, score] of zset(key)) if (score <= max) { zset(key).delete(member); removed++; }
        return removed;
      }
      case 'ZADD': {
        const flags = args.filter(a => typeof a === 'string' && ['XX', 'NX', 'CH'].includes(a));
        const [score, member] = args.slice(flags.length);
        const exists = zset(key).has(member);
        if (flags.includes('XX') && !exists) return 0;
        if (flags.includes('NX') && exists) return 0;
        const changed = !exists || zset(key).get(member) !== Number(score);
        zset(key).set(member, Number(score));
        return flags.includes('CH') ? (changed ? 1 : 0) : (exists ? 0 : 1);
      }
      case 'ZRANK': {
        const index = sorted(key).findIndex(([member]) => member === args[0]);
        return index === -1 ? null : index;
      }
      case 'ZRANGE': return sorted(key).map(([member]) => member);
      case 'ZREM': return zset(key).delete(args[0]) ? 1 : 0;
      case 'HGETALL': return Object.fromEntries(hash(key));
      case 'HSET': { hash(key).set(args[0], args[1]); return 1; }
      case 'HDEL': return hash(key).delete(args[0]) ? 1 : 0;
      case 'INCR': { const next = (keys.get(key) ?? 0) + 1; keys.set(key, next); return next; }
      case 'SET': {
        const nx = args.includes('NX');
        const expiresAt = args.includes('EX') ? nowRef.value + Number(args[args.indexOf('EX') + 1]) * 1000 : Infinity;
        const current = keys.get(key);
        if (nx && current && current.expiresAt > nowRef.value) return null;
        keys.set(key, { value: args[0], expiresAt });
        return 'OK';
      }
      default: throw new Error(`未対応のコマンド: ${name}`);
    }
  };
  return { commands: [], async pipeline(cmds) { this.commands.push(...cmds); return cmds.map(run); } };
}

function setup(options = {}) {
  const nowRef = { value: 1_000_000 };
  const redis = fakeRedis(nowRef);
  const store = new MatchStore({ redis, maxWaiting: 4, perIpMax: 2, ticketTtlMs: 30_000,
    roomTtlMs: 30_000, holdMs: 5_000, now: () => nowRef.value, ...options });
  return { redis, store, advance(ms) { nowRef.value += ms; }, at: () => nowRef.value };
}

const host = (roomId, extra = {}) => ({ roomId, players: 1, gameMode: '0', phase: 'LOBBY', ...extra });

test('入場は上限まで許可され、超えた人は待機数つきで断られる', async () => {
  const s = setup();
  const tickets = [];
  for (let i = 0; i < 4; i++) {
    const result = await s.store.enter({ targetSize: 2, ipHash: `ip-${i}` });
    assert.equal(result.ok, true);
    tickets.push(result.ticket);
  }
  assert.equal(new Set(tickets).size, 4);
  const full = await s.store.enter({ targetSize: 2, ipHash: 'ip-late' });
  assert.equal(full.ok, false);
  assert.equal(full.reason, 'FULL');
  assert.equal(full.waiting.total, 4);
  // 断られた券は席を占有しない
  const again = await s.store.enter({ targetSize: 2, ipHash: 'ip-late' });
  assert.equal(again.reason, 'FULL');
  assert.equal(again.waiting.total, 4);
});

test('入場の連打は断られ、間隔をあければ通る', async () => {
  const s = setup();
  const first = await s.store.enter({ targetSize: 2, ipHash: 'a', rateLimitKey: 'mm:rate:a' });
  assert.equal(first.ok, true);
  const soon = await s.store.enter({ targetSize: 2, ipHash: 'a', rateLimitKey: 'mm:rate:a' });
  assert.equal(soon.ok, false);
  assert.equal(soon.reason, 'TOO_OFTEN');
  assert.equal(s.store.now() > 0, true);
  s.advance(5_001);
  assert.equal((await s.store.enter({ targetSize: 2, ipHash: 'b', rateLimitKey: 'mm:rate:a' })).ok, true);
});

test('同一IPが持てる券には上限がある', async () => {
  const s = setup();
  assert.equal((await s.store.enter({ targetSize: 2, ipHash: 'same' })).ok, true);
  assert.equal((await s.store.enter({ targetSize: 2, ipHash: 'same' })).ok, true);
  const third = await s.store.enter({ targetSize: 2, ipHash: 'same' });
  assert.equal(third.ok, false);
  assert.equal(third.reason, 'IP_LIMIT');
  // 別のIPはまだ入れる（全体の上限には達していない）
  assert.equal((await s.store.enter({ targetSize: 4, ipHash: 'other' })).ok, true);
});

test('期限切れの券は掃除され、その枠は次の人に渡る', async () => {
  const s = setup();
  for (let i = 0; i < 4; i++) await s.store.enter({ targetSize: 2, ipHash: `ip-${i}` });
  assert.equal((await s.store.enter({ targetSize: 2, ipHash: 'late' })).ok, false);
  s.advance(31_000);
  const revived = await s.store.enter({ targetSize: 2, ipHash: 'late' });
  assert.equal(revived.ok, true);
  assert.equal(revived.waiting.total, 1);
});

test('待機数は人数帯ごとの内訳を返す', async () => {
  const s = setup({ maxWaiting: 8 });
  await s.store.enter({ targetSize: 2, ipHash: 'a' });
  await s.store.enter({ targetSize: 2, ipHash: 'b' });
  const last = await s.store.enter({ targetSize: 4, ipHash: 'c' });
  assert.deepEqual(last.waiting, { total: 3, 2: 2, 4: 1 });
});

test('失効した券でのpollはやり直しを求める', async () => {
  const s = setup();
  const { ticket } = await s.store.enter({ targetSize: 2, ipHash: 'a' });
  s.advance(31_000);
  const result = await s.store.poll({ ticket, targetSize: 2 });
  assert.equal(result.ok, false);
  assert.equal(result.reason, 'EXPIRED');
});

test('pollは券の期限を延ばす', async () => {
  const s = setup();
  const { ticket } = await s.store.enter({ targetSize: 2, ipHash: 'a' });
  s.advance(20_000);
  assert.equal((await s.store.poll({ ticket, targetSize: 2 })).ok, true);
  s.advance(20_000);
  assert.equal((await s.store.poll({ ticket, targetSize: 2 })).ok, true);
});

test('ホストの部屋が探索者に候補として返る', async () => {
  const s = setup();
  const hostTicket = (await s.store.enter({ targetSize: 2, ipHash: 'host' })).ticket;
  const announced = await s.store.poll({ ticket: hostTicket, targetSize: 2, room: host('482913') });
  assert.ok(announced.hostToken);

  const guest = (await s.store.enter({ targetSize: 2, ipHash: 'guest' })).ticket;
  const found = await s.store.poll({ ticket: guest, targetSize: 2 });
  assert.equal(found.rooms.length, 1);
  assert.equal(found.rooms[0].roomId, '482913');
  assert.equal(found.rooms[0].players, 1);
  assert.equal(found.rooms[0].gameMode, '0');
});

test('満員・対戦中・除外指定の部屋は候補に出ない', async () => {
  const s = setup({ maxWaiting: 8 });
  const a = (await s.store.enter({ targetSize: 2, ipHash: 'a' })).ticket;
  const b = (await s.store.enter({ targetSize: 2, ipHash: 'b' })).ticket;
  const c = (await s.store.enter({ targetSize: 2, ipHash: 'c' })).ticket;
  const seeker = (await s.store.enter({ targetSize: 2, ipHash: 'd' })).ticket;
  await s.store.poll({ ticket: a, targetSize: 2, room: host('111111', { players: 2 }) });
  await s.store.poll({ ticket: b, targetSize: 2, room: host('222222', { phase: 'PLAYING' }) });
  await s.store.poll({ ticket: c, targetSize: 2, room: host('333333') });
  const found = await s.store.poll({ ticket: seeker, targetSize: 2, exclude: ['333333'] });
  assert.deepEqual(found.rooms, []);
});

test('候補は待ち時間の長い順に返り、渡した直後は他の人に渡らない', async () => {
  const s = setup({ maxWaiting: 8 });
  const older = (await s.store.enter({ targetSize: 4, ipHash: 'older' })).ticket;
  await s.store.poll({ ticket: older, targetSize: 4, room: host('111111') });
  s.advance(5_000);
  const newer = (await s.store.enter({ targetSize: 4, ipHash: 'newer' })).ticket;
  await s.store.poll({ ticket: newer, targetSize: 4, room: host('222222') });

  const first = (await s.store.enter({ targetSize: 4, ipHash: 'first' })).ticket;
  const seen = await s.store.poll({ ticket: first, targetSize: 4 });
  assert.deepEqual(seen.rooms.map(r => r.roomId), ['111111', '222222']);

  const second = (await s.store.enter({ targetSize: 4, ipHash: 'second' })).ticket;
  assert.deepEqual((await s.store.poll({ ticket: second, targetSize: 4 })).rooms, []);
  s.advance(5_001);
  assert.deepEqual((await s.store.poll({ ticket: second, targetSize: 4 })).rooms.map(r => r.roomId),
    ['111111', '222222']);
});

test('他人のロビーIDは横取りできない', async () => {
  const s = setup({ maxWaiting: 8 });
  const owner = (await s.store.enter({ targetSize: 2, ipHash: 'owner' })).ticket;
  await s.store.poll({ ticket: owner, targetSize: 2, room: host('482913') });
  const thief = (await s.store.enter({ targetSize: 2, ipHash: 'thief' })).ticket;
  const stolen = await s.store.poll({ ticket: thief, targetSize: 2, room: host('482913', { hostToken: 'にせもの' }) });
  assert.equal(stolen.ok, false);
  assert.equal(stolen.reason, 'ROOM_TAKEN');
});

test('期限切れの部屋は候補から消える', async () => {
  const s = setup({ maxWaiting: 8 });
  const owner = (await s.store.enter({ targetSize: 2, ipHash: 'owner' })).ticket;
  await s.store.poll({ ticket: owner, targetSize: 2, room: host('482913') });
  s.advance(31_000);
  const seeker = (await s.store.enter({ targetSize: 2, ipHash: 'seeker' })).ticket;
  assert.deepEqual((await s.store.poll({ ticket: seeker, targetSize: 2 })).rooms, []);
});

test('退場すると券も部屋も残らない', async () => {
  const s = setup();
  const { ticket } = await s.store.enter({ targetSize: 2, ipHash: 'a' });
  const { hostToken } = await s.store.poll({ ticket, targetSize: 2, room: host('482913') });
  await s.store.leave({ ticket, targetSize: 2, roomId: '482913', hostToken });

  const seeker = (await s.store.enter({ targetSize: 2, ipHash: 'b' })).ticket;
  const after = await s.store.poll({ ticket: seeker, targetSize: 2 });
  assert.deepEqual(after.rooms, []);
  assert.equal(after.waiting.total, 1);
  assert.equal((await s.store.poll({ ticket, targetSize: 2 })).reason, 'EXPIRED');
});

test('募集だけ取り下げても待機の権利は残る', async () => {
  const s = setup();
  const { ticket } = await s.store.enter({ targetSize: 2, ipHash: 'a' });
  const { hostToken } = await s.store.poll({ ticket, targetSize: 2, room: host('482913') });
  await s.store.leave({ ticket, targetSize: 2, roomId: '482913', hostToken, keepTicket: true });

  const seeker = (await s.store.enter({ targetSize: 2, ipHash: 'b' })).ticket;
  const after = await s.store.poll({ ticket: seeker, targetSize: 2 });
  assert.deepEqual(after.rooms, []);
  assert.equal(after.waiting.total, 2);
  assert.equal((await s.store.poll({ ticket, targetSize: 2 })).ok, true);
});

test('ひとりで待っているホストには、自分より古い部屋だけが見える', async () => {
  const s = setup({ maxWaiting: 8 });
  const older = (await s.store.enter({ targetSize: 4, ipHash: 'older' })).ticket;
  await s.store.poll({ ticket: older, targetSize: 4, room: host('111111') });
  s.advance(1_000);
  const newer = (await s.store.enter({ targetSize: 4, ipHash: 'newer' })).ticket;
  const first = await s.store.poll({ ticket: newer, targetSize: 4, room: host('222222') });
  assert.deepEqual(first.rooms.map(r => r.roomId), ['111111']);
  // 誰か来た部屋は移籍しない
  s.advance(6_000);
  const withGuest = await s.store.poll({ ticket: newer, targetSize: 4,
    room: host('222222', { players: 2, hostToken: first.hostToken }) });
  assert.deepEqual(withGuest.rooms, []);
});
