import { randomUUID } from 'node:crypto';

/**
 * ランダムマッチの待合所。
 *
 * 状態はすべてKVに置き、この層は「誰が待っているか」と「どの部屋が募集中か」だけを扱う。
 * 対戦そのものはP2Pなので、ここが落ちてもID共有のロビーは動き続ける。
 *
 * redis は次の契約を満たすオブジェクト。
 *   pipeline(commands) -> 各コマンドの結果の配列
 *   HGETALL はフィールド名をキーにしたオブジェクトへ正規化して返すこと。
 *
 * 待機人数の上限は「ZADDしてから自分の順位を見る」方式で守る。券の先頭に通し番号を
 * 埋めてあるので、期限（スコア）が同じミリ秒で並んでも順位は必ず到着順になる。
 * 後から入った人が先客より前に並ぶことがないため、スクリプトなしで上限を正確に守れる。
 */
const TICKETS = 'mm:tickets';
const SEQUENCE = 'mm:seq';
const SIZES = [2, 4];
const queueKey = size => `mm:queue:${size}`;
const ipKey = hash => `mm:ip:${hash}`;
const token = () => randomUUID().replace(/-/g, '');

const countWaiting = members => {
  const waiting = { total: members.length };
  for (const size of SIZES) waiting[size] = members.filter(m => m.startsWith(`${size}:`)).length;
  return waiting;
};

export class MatchStore {
  constructor({ redis, maxWaiting = 16, perIpMax = 2, ticketTtlMs = 30_000,
    roomTtlMs = 30_000, holdMs = 5_000, now = () => Date.now() }) {
    this.redis = redis;
    this.maxWaiting = maxWaiting;
    this.perIpMax = perIpMax;
    this.ticketTtlMs = ticketTtlMs;
    this.roomTtlMs = roomTtlMs;
    this.holdMs = holdMs;
    this.now = now;
  }

  async enter({ targetSize, ipHash, rateLimitKey = null, rateLimitSeconds = 5 }) {
    const now = this.now();
    const opening = [['INCR', SEQUENCE]];
    if (rateLimitKey) opening.push(['SET', rateLimitKey, '1', 'NX', 'EX', rateLimitSeconds]);
    const [sequence, accepted] = await this.redis.pipeline(opening);
    if (rateLimitKey && accepted === null) return { ok: false, reason: 'TOO_OFTEN', retryAfter: rateLimitSeconds };
    const ticket = `${String(sequence).padStart(12, '0')}.${token()}`;
    const member = `${targetSize}:${ticket}`;
    const commands = [
      ['ZREMRANGEBYSCORE', TICKETS, '-inf', now],
      ['ZADD', TICKETS, now + this.ticketTtlMs, member],
      ['ZRANK', TICKETS, member],
      ['ZRANGE', TICKETS, 0, -1],
    ];
    if (ipHash) commands.push(
      ['ZREMRANGEBYSCORE', ipKey(ipHash), '-inf', now],
      ['ZADD', ipKey(ipHash), now + this.ticketTtlMs, ticket],
      ['ZRANK', ipKey(ipHash), ticket],
    );
    const results = await this.redis.pipeline(commands);
    const [, , rank, members, , , ipRank] = results;

    const overCapacity = rank === null || Number(rank) >= this.maxWaiting;
    const overPerIp = Boolean(ipHash) && (ipRank === null || Number(ipRank) >= this.perIpMax);
    if (overCapacity || overPerIp) {
      const rollback = [['ZREM', TICKETS, member]];
      if (ipHash) rollback.push(['ZREM', ipKey(ipHash), ticket]);
      await this.redis.pipeline(rollback);
      return { ok: false, reason: overCapacity ? 'FULL' : 'IP_LIMIT',
        waiting: countWaiting(members.filter(m => m !== member)) };
    }
    return { ok: true, ticket, waiting: countWaiting(members) };
  }

  async poll({ ticket, targetSize, room = null, exclude = [] }) {
    const now = this.now();
    const member = `${targetSize}:${ticket}`;
    const queue = queueKey(targetSize);
    // 券の生存確認は延長前の一覧で行う。同じ時刻に2度pollされても失効と誤判定しない。
    const [, members, , raw] = await this.redis.pipeline([
      ['ZREMRANGEBYSCORE', TICKETS, '-inf', now],
      ['ZRANGE', TICKETS, 0, -1],
      ['ZADD', TICKETS, 'XX', now + this.ticketTtlMs, member],
      ['HGETALL', queue],
    ]);
    const waiting = countWaiting(members);
    if (!members.includes(member)) return { ok: false, reason: 'EXPIRED', waiting };

    const entries = Object.entries(raw ?? {}).map(([roomId, value]) => [roomId, JSON.parse(value)]);
    const live = entries.filter(([, entry]) => entry.expiresAt > now);
    const writes = entries.filter(([, entry]) => entry.expiresAt <= now).map(([roomId]) => ['HDEL', queue, roomId]);

    let hostToken = room?.hostToken ?? null;
    let mine = null;
    if (room) {
      mine = live.find(([roomId]) => roomId === room.roomId)?.[1] ?? null;
      if (mine && mine.hostToken !== room.hostToken) return { ok: false, reason: 'ROOM_TAKEN', waiting };
      hostToken = room.hostToken ?? token();
      writes.push(['HSET', queue, room.roomId, JSON.stringify({
        hostToken,
        players: room.players,
        gameMode: room.gameMode,
        phase: room.phase,
        createdAt: mine?.createdAt ?? now,
        expiresAt: now + this.roomTtlMs,
        heldUntil: mine?.heldUntil ?? 0,
      })]);
    }

    const rooms = this.candidates({ live, now, room, mine, exclude, targetSize });
    // 探索者に渡した部屋は少しの間ほかの人に渡さない。ホストの移籍先は押さえない。
    if (!room) {
      for (const [roomId, entry] of rooms) {
        writes.push(['HSET', queue, roomId, JSON.stringify({ ...entry, heldUntil: now + this.holdMs })]);
      }
    }
    if (writes.length) await this.redis.pipeline(writes);

    return { ok: true, hostToken, waiting,
      rooms: rooms.map(([roomId, entry]) => ({ roomId, players: entry.players, gameMode: entry.gameMode,
        waitingSeconds: Math.floor((now - entry.createdAt) / 1000) })) };
  }

  candidates({ live, now, room, mine, exclude, targetSize }) {
    // ホストは「自分より先に待っている部屋」へ移るためだけに候補を見る。誰か来ていれば動かない。
    if (room && room.players > 1) return [];
    const createdAt = room ? (mine?.createdAt ?? now) : Infinity;
    return live
      .filter(([roomId, entry]) => roomId !== room?.roomId && !exclude.includes(roomId) &&
        entry.phase === 'LOBBY' && entry.players < targetSize &&
        entry.createdAt < createdAt && (entry.heldUntil ?? 0) <= now)
      .sort((a, b) => a[1].createdAt - b[1].createdAt)
      .slice(0, room ? 1 : 3);
  }

  // keepTicket は「部屋の募集だけ取り下げて、待機の権利は保つ」場合に使う（ホストの移籍）。
  async leave({ ticket, targetSize, roomId = null, hostToken = null, ipHash = null, keepTicket = false }) {
    const queue = queueKey(targetSize);
    const commands = keepTicket ? [] : [['ZREM', TICKETS, `${targetSize}:${ticket}`]];
    if (ipHash && !keepTicket) commands.push(['ZREM', ipKey(ipHash), ticket]);
    if (roomId) commands.push(['HGETALL', queue]);
    const results = await this.redis.pipeline(commands);
    if (!roomId) return { ok: true };
    if (!results.length) return { ok: true };
    const raw = results.at(-1)?.[roomId];
    const entry = raw ? JSON.parse(raw) : null;
    if (entry && entry.hostToken === hostToken) await this.redis.pipeline([['HDEL', queue, roomId]]);
    return { ok: true };
  }
}
