#!/usr/bin/env node
/**
 * 本物の Upstash に対して待合所の一連の流れを試す。
 *
 *   UPSTASH_REDIS_REST_URL=https://xxx.upstash.io \
 *   UPSTASH_REDIS_REST_TOKEN=AX... \
 *   node scripts/check-match-kv.mjs
 *
 * 実際の鍵（mm:*）を使うので、必ず後始末する。券も部屋も30秒で失効するため、
 * 途中で失敗しても残骸はすぐ消える。
 */
import { MatchStore } from '../api/_match/store.js';
import { createUpstashClient } from '../api/_match/upstash.js';

const url = process.env.UPSTASH_REDIS_REST_URL;
const token = process.env.UPSTASH_REDIS_REST_TOKEN;
if (!url || !token) {
  console.error('UPSTASH_REDIS_REST_URL と UPSTASH_REDIS_REST_TOKEN を指定してください。');
  process.exit(1);
}

const store = new MatchStore({ redis: createUpstashClient({ url, token }), maxWaiting: 16 });
const size = 4;
const roomId = String(Math.floor(100000 + Math.random() * 900000));
const step = (label, detail) => console.log(`  ✓ ${label}${detail ? `　${detail}` : ''}`);

let hostTicket = null;
let guestTicket = null;
let hostToken = null;

try {
  console.log(`\n待合所の疎通確認（${new URL(url).host}）\n`);

  const host = await store.enter({ targetSize: size });
  if (!host.ok) throw new Error(`入場できません: ${host.reason}`);
  hostTicket = host.ticket;
  step('入場', `待機 ${host.waiting.total}人`);

  const announced = await store.poll({ ticket: hostTicket, targetSize: size,
    room: { roomId, players: 1, gameMode: '0', phase: 'LOBBY', hostToken: null } });
  if (!announced.ok) throw new Error(`募集を登録できません: ${announced.reason}`);
  hostToken = announced.hostToken;
  step('部屋の登録', `ロビーID ${roomId}`);

  const guest = await store.enter({ targetSize: size });
  if (!guest.ok) throw new Error(`2人目が入場できません: ${guest.reason}`);
  guestTicket = guest.ticket;
  const found = await store.poll({ ticket: guestTicket, targetSize: size });
  const hit = found.rooms.find(r => r.roomId === roomId);
  if (!hit) throw new Error('登録したはずの部屋が見つかりません');
  step('探索', `${hit.roomId} を発見（待機 ${found.waiting.total}人）`);

  const second = await store.poll({ ticket: guestTicket, targetSize: size });
  if (second.rooms.some(r => r.roomId === roomId)) throw new Error('渡した直後の部屋が再び配られました');
  step('ソフト予約', '直後の再配布なし');

  await store.leave({ ticket: guestTicket, targetSize: size });
  guestTicket = null;
  await store.leave({ ticket: hostTicket, targetSize: size, roomId, hostToken });
  hostTicket = null;
  step('退場', '券と部屋を削除');

  const after = await store.enter({ targetSize: size });
  if (!after.ok) throw new Error(`後始末の確認に失敗: ${after.reason}`);
  const empty = await store.poll({ ticket: after.ticket, targetSize: size });
  await store.leave({ ticket: after.ticket, targetSize: size });
  if (empty.rooms.some(r => r.roomId === roomId)) throw new Error('削除した部屋がまだ見えます');
  step('後始末', `残った待機 ${Math.max(0, empty.waiting.total - 1)}人`);

  console.log('\n待合所は正常に動いています。\n');
} catch (error) {
  console.error(`\n失敗: ${error.message}\n`);
  process.exitCode = 1;
} finally {
  if (guestTicket) await store.leave({ ticket: guestTicket, targetSize: size }).catch(() => {});
  if (hostTicket) await store.leave({ ticket: hostTicket, targetSize: size, roomId, hostToken }).catch(() => {});
}
