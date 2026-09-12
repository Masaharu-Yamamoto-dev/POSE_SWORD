/**
 * ランダムマッチの待合所（enter / poll / leave）。
 *
 * 対戦そのものはP2Pなので、ここは「誰が待っていて、どの部屋が募集中か」しか知らない。
 * KVの資格情報はこのサーバー側にとどまり、ブラウザには渡らない。
 */
import { createHash } from 'node:crypto';
import { MatchStore } from '../_match/store.js';
import { createUpstashClient } from '../_match/upstash.js';

export const config = { maxDuration: 10 };

const SIZES = [2, 4];
const PHASES = ['LOBBY', 'LOADING', 'COUNTDOWN', 'PLAYING', 'RESULT'];
const isRoomId = value => typeof value === 'string' && /^\d{6}$/.test(value);
const isToken = value => typeof value === 'string' && value.length > 0 && value.length <= 80;

const clientIpHash = req => {
  const forwarded = String(req.headers['x-forwarded-for'] ?? '').split(',')[0].trim();
  if (!forwarded) return null;
  return createHash('sha256').update(`${process.env.MATCH_IP_SALT ?? ''}:${forwarded}`).digest('hex').slice(0, 16);
};

const readRoom = body => {
  if (!body.roomId) return null;
  const players = Number(body.players);
  if (!isRoomId(body.roomId) || !Number.isInteger(players) || players < 1 || players > 4 ||
      !['0', '1'].includes(body.gameMode) || !PHASES.includes(body.phase) ||
      (body.hostToken != null && !isToken(body.hostToken))) {
    throw new Error('部屋の情報が不正です');
  }
  return { roomId: body.roomId, players, gameMode: body.gameMode, phase: body.phase,
    hostToken: body.hostToken ?? null };
};

export default async function handler(req, res) {
  if (req.method !== 'POST') return res.status(405).json({ error: 'Method Not Allowed' });

  const url = process.env.UPSTASH_REDIS_REST_URL;
  const token = process.env.UPSTASH_REDIS_REST_TOKEN;
  if (!url || !token) return res.status(503).json({ error: 'ランダムマッチは現在利用できません' });

  const ttlSeconds = Number(process.env.MATCH_TICKET_TTL ?? 30);
  const store = new MatchStore({
    redis: createUpstashClient({ url, token }),
    maxWaiting: Number(process.env.MATCH_MAX_WAITING ?? 16),
    perIpMax: Number(process.env.MATCH_PER_IP_MAX ?? 2),
    ticketTtlMs: ttlSeconds * 1000,
    roomTtlMs: ttlSeconds * 1000,
  });

  let body;
  try {
    body = typeof req.body === 'string' ? JSON.parse(req.body) : (req.body ?? {});
  } catch {
    return res.status(400).json({ error: '要求を解釈できませんでした' });
  }

  const targetSize = Number(body.targetSize);
  if (!SIZES.includes(targetSize)) return res.status(400).json({ error: 'targetSize は 2 か 4 です' });
  const ipHash = clientIpHash(req);

  try {
    switch (req.query.action) {
      case 'enter': {
        const result = await store.enter({ targetSize, ipHash,
          rateLimitKey: ipHash ? `mm:rate:${ipHash}` : null });
        if (result.ok) return res.status(200).json({ ticket: result.ticket, waiting: result.waiting, ttlSeconds });
        const retryAfter = result.retryAfter ?? 10;
        res.setHeader('Retry-After', String(retryAfter));
        return res.status(429).json({ error: result.reason, waiting: result.waiting ?? null, retryAfter });
      }
      case 'poll': {
        if (!isToken(body.ticket)) return res.status(400).json({ error: 'ticket がありません' });
        const exclude = Array.isArray(body.exclude) ? body.exclude.filter(isRoomId).slice(0, 8) : [];
        const result = await store.poll({ ticket: body.ticket, targetSize, exclude, room: readRoom(body) });
        return res.status(200).json(result);
      }
      case 'leave': {
        if (!isToken(body.ticket)) return res.status(400).json({ error: 'ticket がありません' });
        await store.leave({ ticket: body.ticket, targetSize, ipHash,
          roomId: isRoomId(body.roomId) ? body.roomId : null,
          hostToken: isToken(body.hostToken) ? body.hostToken : null,
          keepTicket: body.keepTicket === true });
        return res.status(200).json({ ok: true });
      }
      default:
        return res.status(404).json({ error: '不明な操作です' });
    }
  } catch (error) {
    if (error.message === '部屋の情報が不正です') return res.status(400).json({ error: error.message });
    console.error('[match]', error);
    return res.status(502).json({ error: '待合所に接続できませんでした' });
  }
}
