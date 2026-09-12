/**
 * ランダムマッチの探索手順。
 *
 * 待合所は「誰が待っているか」しか知らないので、実際に相手を見つける段取りはここが持つ。
 * Reactにも通信方式にも依存しない素のクラスで、tick() を定期的に呼ぶだけで進む。
 *
 * room は次の契約を満たすアダプタ（実体は useRoom）。
 *   createRoom(targetSize) / joinRoom(roomId) / leave()
 *   state() -> { roomId, players, gameMode, phase, inRoom, error } | null
 */
export const SEARCH_INTERVAL = 4000;
export const HOST_INTERVAL = 8000;
export const JOIN_TIMEOUT = 10000;
export const EMPTY_POLLS_BEFORE_HOSTING = 2;
const SEEKING = ['ENTERING', 'FULL', 'SEARCHING', 'JOINING', 'HOSTING'];
const REASONS = {
  FULL: '今は混み合っています。空くまでお待ちください。',
  IP_LIMIT: '同じ回線からの待機が多すぎます。',
  TOO_OFTEN: '続けて試しすぎです。少し待ってください。',
  UNAVAILABLE: 'ランダムマッチは今使えません。ロビーIDを共有して対戦できます。',
  OFFLINE: '通信できませんでした。接続を確認してください。',
  ERROR: '待合所に接続できませんでした。',
};

export class MatchSeeker {
  constructor({ client, room, targetSize, now = () => Date.now(), onChange = () => {} }) {
    if (![2, 4].includes(targetSize)) throw new Error('対戦人数は2人か4人です。');
    this.client = client;
    this.room = room;
    this.targetSize = targetSize;
    this.now = now;
    this.onChange = onChange;
    this.phase = 'IDLE';
    this.ticket = null;
    this.hostToken = null;
    this.waiting = null;
    this.error = '';
    this.candidates = [];
    this.exclude = [];
    this.target = null;
    this.hosting = false;
    this.nextAt = 0;
    this.deadline = 0;
    this.emptyPolls = 0;
    this.startedAt = null;
    this.busy = false;
  }

  view() {
    return { phase: this.phase, seeking: SEEKING.includes(this.phase), waiting: this.waiting,
      error: this.error, targetSize: this.targetSize,
      elapsedMs: this.startedAt === null ? 0 : this.now() - this.startedAt };
  }
  notify() { this.onChange(this.view()); }
  enterPhase(phase) { this.phase = phase; this.notify(); }

  start() {
    if (this.phase !== 'IDLE') return;
    this.startedAt = this.now();
    this.enterPhase('ENTERING');
  }

  async tick() {
    if (this.busy || ['IDLE', 'DONE', 'CANCELLED', 'UNAVAILABLE'].includes(this.phase)) return;
    this.busy = true;
    try {
      await this.step(this.now());
    } catch (error) {
      this.error = error.message ?? '待合所との通信に失敗しました。';
      this.notify();
    } finally {
      this.busy = false;
    }
  }

  step(now) {
    switch (this.phase) {
      case 'ENTERING': return this.requestTicket();
      case 'FULL': if (now >= this.nextAt) this.enterPhase('ENTERING'); return;
      case 'SEARCHING': return this.search(now);
      case 'JOINING': return this.watchJoin(now);
      case 'HOSTING': return this.advertise(now, true);
      case 'ADVERTISING': return this.advertise(now, false);
      default: return;
    }
  }

  async requestTicket() {
    const result = await this.client.enter({ targetSize: this.targetSize });
    if (result.ok) {
      this.ticket = result.ticket;
      this.waiting = result.waiting ?? null;
      this.error = '';
      this.emptyPolls = 0;
      this.nextAt = 0;
      this.enterPhase('SEARCHING');
      return;
    }
    if (result.reason === 'UNAVAILABLE') { this.error = REASONS.UNAVAILABLE; this.enterPhase('UNAVAILABLE'); return; }
    this.waiting = result.waiting ?? this.waiting;
    this.error = REASONS[result.reason] ?? REASONS.ERROR;
    this.nextAt = this.now() + (result.retryAfter ?? 10) * 1000;
    this.enterPhase('FULL');
  }

  async search(now) {
    if (now < this.nextAt) return;
    const result = await this.client.poll({ ticket: this.ticket, targetSize: this.targetSize, exclude: this.exclude });
    if (!result.ok) return this.recover(result, now);
    this.waiting = result.waiting ?? this.waiting;
    this.error = '';
    if (result.rooms?.length) {
      this.candidates = result.rooms.map(candidate => candidate.roomId);
      this.emptyPolls = 0;
      return this.joinNext();
    }
    this.nextAt = now + SEARCH_INTERVAL;
    if (++this.emptyPolls >= EMPTY_POLLS_BEFORE_HOSTING) return this.beginHosting();
    this.notify();
  }

  joinNext() {
    const roomId = this.candidates.shift();
    if (!roomId) { this.nextAt = this.now(); this.enterPhase('SEARCHING'); return; }
    this.target = roomId;
    this.hosting = false;
    this.deadline = this.now() + JOIN_TIMEOUT;
    this.room.joinRoom(roomId);
    this.enterPhase('JOINING');
  }

  watchJoin(now) {
    const state = this.room.state();
    if (state?.inRoom && state.roomId === this.target) return this.matched();
    if (!state?.error && now < this.deadline) return;
    // 満員・対戦中・応答なし。その部屋はしばらく候補から外す。
    this.exclude = [...this.exclude, this.target].slice(-8);
    this.room.leave();
    this.joinNext();
  }

  beginHosting() {
    this.hosting = true;
    this.hostToken = null;
    this.nextAt = 0;
    this.room.createRoom(this.targetSize);
    this.enterPhase('HOSTING');
  }

  // 募集中のホストの周期処理。seekCandidates が真なら、自分より古い部屋への移籍も試す。
  async advertise(now, seekCandidates) {
    const state = this.room.state();
    if (!state?.roomId) return;
    if (seekCandidates && state.players > 1) return this.matched();
    if (!seekCandidates) {
      if (state.players < 2) { this.enterPhase('HOSTING'); return; }
      if (state.players >= this.targetSize || state.phase !== 'LOBBY') return this.finish();
    }
    if (now < this.nextAt) return;
    const result = await this.client.poll({ ticket: this.ticket, targetSize: this.targetSize, exclude: this.exclude,
      room: { roomId: state.roomId, players: state.players, gameMode: state.gameMode,
        phase: state.phase, hostToken: this.hostToken } });
    if (!result.ok) {
      if (result.reason !== 'ROOM_TAKEN') return this.recover(result, now);
      this.room.leave();                        // そのロビーIDは他人のもの。別のIDで作り直す。
      return this.beginHosting();
    }
    this.hostToken = result.hostToken ?? this.hostToken;
    this.waiting = result.waiting ?? this.waiting;
    this.nextAt = now + HOST_INTERVAL;
    const older = result.rooms?.[0];
    if (seekCandidates && older) return this.moveTo(older.roomId, state.roomId);
    this.notify();
  }

  // 先に待っている部屋があれば、自分の募集を畳んでそちらへ移る。券は手放さない。
  async moveTo(roomId, myRoomId) {
    await this.client.leave({ ticket: this.ticket, targetSize: this.targetSize,
      roomId: myRoomId, hostToken: this.hostToken, keepTicket: true });
    this.hostToken = null;
    this.room.leave();
    this.candidates = [roomId];
    this.joinNext();
  }

  matched() {
    if (this.hosting) { this.nextAt = this.now(); this.enterPhase('ADVERTISING'); return; }
    this.enterPhase('DONE');
    return this.release();
  }

  async finish() {
    const state = this.room.state();
    this.enterPhase('DONE');
    await this.release(state?.roomId ?? null);
  }

  async release(roomId = null) {
    const ticket = this.ticket;
    this.ticket = null;
    if (!ticket) return;
    await this.client.leave({ ticket, targetSize: this.targetSize, roomId, hostToken: this.hostToken });
  }

  recover(result, now) {
    if (result.reason === 'EXPIRED') { this.ticket = null; this.enterPhase('ENTERING'); return; }
    if (result.reason === 'UNAVAILABLE') { this.error = REASONS.UNAVAILABLE; this.enterPhase('UNAVAILABLE'); return; }
    this.error = REASONS[result.reason] ?? REASONS.ERROR;
    this.nextAt = now + SEARCH_INTERVAL;
    this.notify();
  }

  async cancel() {
    if (['DONE', 'CANCELLED'].includes(this.phase)) return;
    const roomId = this.room.state()?.roomId ?? null;
    this.room.leave();
    this.enterPhase('CANCELLED');
    await this.release(roomId);
  }
}
