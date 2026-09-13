#!/usr/bin/env node
/**
 * Vercel関数（/api/match/*）を、HTTPサーバーを立てずに直接呼んで確かめる。
 *
 *   set -a; source .env.local; set +a
 *   node scripts/check-match-api.mjs
 *
 * 入力検証・状態コード・IPごとの連打制限まで含めて、本物のKVに対して通す。
 */
import handler from '../api/match/[action].js';

const call = async (action, body, ip) => {
  let status = 200;
  let payload = null;
  const res = {
    setHeader() { return this; },
    status(code) { status = code; return this; },
    json(data) { payload = data; return this; },
  };
  await handler({ method: 'POST', headers: { 'x-forwarded-for': ip }, query: { action }, body }, res);
  return { status, payload };
};

const expect = (label, actual, wanted) => {
  const ok = actual === wanted;
  console.log(`  ${ok ? '✓' : '✗'} ${label}　${ok ? actual : `${actual}（期待 ${wanted}）`}`);
  if (!ok) process.exitCode = 1;
  return ok;
};

const roomId = String(Math.floor(100000 + Math.random() * 900000));
const hostIp = '198.51.100.10';
const guestIp = '198.51.100.20';
let hostTicket = null;
let guestTicket = null;
let hostToken = null;

console.log('\nVercel関数の確認（本物のKVを使用）\n');

const bad = await call('enter', { targetSize: 3 }, hostIp);
expect('3人戦は受け付けない', bad.status, 400);

const host = await call('enter', { targetSize: 4 }, hostIp);
expect('ホストの入場', host.status, 200);
hostTicket = host.payload?.ticket ?? null;
console.log(`      待機 ${host.payload?.waiting?.total}人 / 上限 ${process.env.MATCH_MAX_WAITING ?? 16}人・券の有効 ${host.payload?.ttlSeconds}秒`);

const tooOften = await call('enter', { targetSize: 4 }, hostIp);
expect('同じIPからの連打を弾く', tooOften.payload?.error, 'TOO_OFTEN');

const announced = await call('poll', { ticket: hostTicket, targetSize: 4, roomId,
  players: 1, gameMode: '0', phase: 'LOBBY' }, hostIp);
expect('部屋の登録', announced.payload?.ok, true);
hostToken = announced.payload?.hostToken ?? null;

const malformed = await call('poll', { ticket: hostTicket, targetSize: 4, roomId,
  players: 99, gameMode: '0', phase: 'LOBBY' }, hostIp);
expect('おかしな人数は受け付けない', malformed.status, 400);

const guest = await call('enter', { targetSize: 4 }, guestIp);
expect('別のIPからの入場', guest.status, 200);
guestTicket = guest.payload?.ticket ?? null;

const found = await call('poll', { ticket: guestTicket, targetSize: 4 }, guestIp);
expect('探索で部屋が見つかる', found.payload?.rooms?.some(r => r.roomId === roomId), true);

const stolen = await call('poll', { ticket: guestTicket, targetSize: 4, roomId,
  players: 1, gameMode: '0', phase: 'LOBBY', hostToken: 'にせもの' }, guestIp);
expect('他人のロビーIDを奪えない', stolen.payload?.reason, 'ROOM_TAKEN');

const expired = await call('poll', { ticket: '000000000001.dead', targetSize: 4 }, guestIp);
expect('失効した券はやり直しを求める', expired.payload?.reason, 'EXPIRED');

expect('ゲストの退場', (await call('leave', { ticket: guestTicket, targetSize: 4 }, guestIp)).status, 200);
expect('ホストの退場', (await call('leave', { ticket: hostTicket, targetSize: 4, roomId, hostToken }, hostIp)).status, 200);

console.log(process.exitCode ? '\n失敗した項目があります。\n' : '\nVercel関数は正常に動いています。\n');
