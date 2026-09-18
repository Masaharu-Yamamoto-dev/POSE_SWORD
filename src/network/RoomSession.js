import { HostRoom, MAX_PLAYERS, PROTOCOL_VERSION, SOLO_BUFF, validateSword } from './HostRoom.js';

const ACTIVE = ['LOADING', 'COUNTDOWN', 'PLAYING'];
// 自動開始の部屋のタイミング。席が埋まれば少し待って開始し、埋まらなければ人数を切り上げる。
export const AUTO_START_DELAY = 6000;
export const FILL_TIMEOUT = 60000;
// 通信が遅いと剣画像(ROSTER)の受信完了がAUTO_START_DELAYより遅れ、猶予が
// 実質ゼロのまま開始してしまうことがあった。受信完了からも別途この分だけ待つ。
export const ASSETS_READY_DELAY = 3000;
// 剣画像は ROSTER でしか配らない。STATE は同じ形のまま imageStr だけを落とす。
// 残機モードの swords[] は3本それぞれが画像を持つので、そこまで潜って削る。
// ここを浅く削ると STATE 1通が数MBになり、心拍が詰まって接続が切れる。
const stripImages = sword => {
  const stats = { ...sword };
  delete stats.imageStr;
  if (Array.isArray(stats.swords)) stats.swords = stats.swords.map(slot => {
    const copy = { ...slot };
    delete copy.imageStr;
    return copy;
  });
  return stats;
};
const withoutImages = room => ({ ...room,
  players: room.players.map(p => ({ ...p, swordData: stripImages(p.swordData) })) });

// 画像を抜いた STATE が、ROSTER で受け取った画像を消してしまわないように重ねる。
// swords[] はスロットの位置そのものが意味を持つので、添字で対応させる。
const mergeSword = (previous, next) => {
  const merged = { ...previous, ...next };
  if (Array.isArray(next?.swords)) {
    merged.swords = next.swords.map((slot, i) => ({ ...previous?.swords?.[i], ...slot }));
  }
  return merged;
};

// Transport adapter for PeerJS DataConnection. Time is injected so barriers,
// disconnects and retransmission can be tested without browser timers.
export class RoomSession {
  constructor({ isHost = false, roomEpoch = '', seatLimit, autoStart = false, gameMode = '0', livesMode = false,
    soloMode = false, sword,
    now = () => performance.now(), random = Math.random, onChange = () => {}, onUnity = () => {} }) {
    this.isHost = isHost;
    this.epoch = roomEpoch;
    this.sword = validateSword(sword);
    this.now = now;
    // 時計と同じく乱数も注入する。開始時のスポーン順とボスの抽選をテストから固定できるようにするため。
    this.random = random;
    this.onChange = onChange;
    this.onUnity = onUnity;
    this.host = isHost
      ? new HostRoom({ roomEpoch, hostSword: sword, seatLimit, autoStart, gameMode, livesMode, soloMode })
      : null;
    this.localPlayerId = isHost ? 'p0' : null;
    this.links = new Map();
    this.room = this.host?.snapshot() ?? null;
    this.result = null;
    this.resultPlayers = [];
    this.sync = null;
    this.closed = false;
    this.error = '';
    this.assetVersion = 0;
    this.loadDeadline = Infinity;
    this.lastHeartbeat = -Infinity;
    this.sequence = 0;
    this.autoStartAt = null;
    this.gatheredAt = null;
    this.fullAt = null;
    this.readyAt = null;
  }

  view() {
    return { room: this.room, localPlayerId: this.localPlayerId, isHost: this.isHost,
      result: this.result, resultPlayers: this.resultPlayers, sync: this.sync, closed: this.closed, error: this.error,
      canStart: !this.closed && !!this.host?.canStart() &&
        [...this.links.values()].every(l => l.playerId && l.assetAck === this.assetVersion) };
  }
  notify() { this.onChange(this.view()); }
  command(method, data) { this.onUnity({ method, data }); }

  send(link, type, data = {}) {
    if (!link.conn.open) return;
    const packet = { ...data, type, protocolVersion: PROTOCOL_VERSION, roomEpoch: this.epoch };
    if (type === 'SYNC' && link.conn.bufferSize > 0) { link.pendingSync = packet; return; }
    try { link.conn.send(packet); } catch { this.disconnected(link); link.conn.close(); }
  }
  broadcast(type, data) { for (const link of this.links.values()) if (link.playerId) this.send(link, type, data); }

  attach(conn) {
    if (this.closed) { conn.close(); return; }
    const link = { conn, attachedAt: this.now(), lastSeen: this.now(), playerId: null, assetAck: -1, resultAck: null, lastResult: 0 };
    if (this.isHost && !this.host.reserve(conn.peer)) {
      const reject = () => { this.send(link, 'REJECT', { reason: '部屋が満員、または対戦中です。' }); conn.close(); };
      if (conn.open) reject(); else conn.on('open', reject);
      return;
    }
    if (!this.isHost && this.links.size) { conn.close(); return; }
    this.links.set(conn.peer, link);
    conn.on('data', data => this.receive(link, data));
    conn.on('close', () => this.disconnected(link));
    conn.on('error', () => { this.disconnected(link); conn.close(); });
    if (!this.isHost) {
      const join = () => this.send(link, 'JOIN', { swordData: this.sword });
      if (conn.open) join(); else conn.on('open', join);
    }
  }

  publish(withAssets = false) {
    this.room = { ...this.host.snapshot(),
      startsInMs: this.autoStartAt === null ? null : Math.max(0, this.autoStartAt - this.now()) };
    if (withAssets) {
      this.assetVersion++;
      this.broadcast('ROSTER', { room: this.room, assetVersion: this.assetVersion });
    } else this.broadcast('STATE', { room: withoutImages(this.room) });
    this.notify();
  }

  receive(link, message) {
    if (this.closed || this.links.get(link.conn.peer) !== link || !message || typeof message !== 'object') return;
    if (message.protocolVersion !== PROTOCOL_VERSION) {
      this.send(link, 'REJECT', { reason: 'ゲームのバージョンが異なります。再読み込みしてください。' });
      link.conn.close(); return;
    }
    if (message.type !== 'JOIN' && message.type !== 'ACCEPT' && message.type !== 'REJECT' &&
        message.roomEpoch !== this.epoch) return;
    link.lastSeen = this.now();
    try {
      if (this.isHost) this.receiveAsHost(link, message);
      else this.receiveAsGuest(link, message);
    } catch (e) {
      this.error = e.message;
      if (!link.playerId && this.isHost) { this.send(link, 'REJECT', { reason: e.message }); link.conn.close(); }
      this.notify();
    }
  }

  receiveAsHost(link, m) {
    if (m.type === 'JOIN') {
      if (link.playerId) return;
      link.playerId = this.host.join(link.conn.peer, m.swordData);
      this.send(link, 'ACCEPT', { playerId: link.playerId });
      this.publish(true); return;
    }
    if (!link.playerId) return;
    switch (m.type) {
      case 'ROSTER_ACK':
        if (m.assetVersion === this.assetVersion) link.assetAck = m.assetVersion;
        this.notify(); break;
      case 'READY':
        if (m.readyVersion === this.room.readyVersion && this.host.setReady(link.playerId, m.ready)) this.publish(); break;
      case 'SWORD':
        this.host.updateSword(link.playerId, m.swordData);
        this.publish(true); break;
      case 'INITIALIZED': this.loaded(link.playerId, m.matchId); break;
      case 'LOAD_FAILED':
        if (m.matchId === this.room.matchId && ACTIVE.includes(this.room.phase)) this.abort('ゲームの初期化に失敗しました。');
        break;
      case 'INPUT': {
        const input = this.host.acceptInput(link.conn.peer, m, this.now());
        if (input) this.command('ReceiveMultiplayerInput', input);
        break;
      }
      case 'RESULT_ACK':
        if (m.matchId === this.result?.matchId) link.resultAck = m.matchId; break;
      case 'RETURN':
        if (m.matchId === this.room.matchId && this.host.returnToLobby(link.playerId)) this.publish(); break;
      case 'LEAVE': this.disconnected(link); link.conn.close(); break;
      case 'PING': this.send(link, 'PONG'); break;
      default: break; // Guests may not publish state, modes, sync or results.
    }
  }

  receiveAsGuest(link, m) {
    switch (m.type) {
      case 'ACCEPT':
        if (this.localPlayerId) return;
        this.epoch = m.roomEpoch; this.localPlayerId = m.playerId; link.playerId = 'p0'; break;
      case 'REJECT': this.error = m.reason; this.close(false); return;
      case 'ROSTER':
      case 'STATE': {
        if (!this.localPlayerId || !m.room || m.room.players?.length > MAX_PLAYERS ||
            (this.room && m.room.revision < this.room.revision)) return;
        const previous = this.room;
        this.room = { ...m.room, players: m.room.players.map(p => ({ ...p,
          swordData: mergeSword(previous?.players.find(old => old.playerId === p.playerId)?.swordData, p.swordData) })) };
        if (m.type === 'ROSTER') {
          this.room.players.forEach(p => validateSword(p.swordData));
          this.send(link, 'ROSTER_ACK', { assetVersion: m.assetVersion });
        }
        break;
      }
      case 'PREPARE':
        if (!this.room || m.matchId !== this.room.matchId || this.room.phase !== 'LOADING' || this.initializing === m.matchId) return;
        this.initialize(m); break;
      case 'SYNC':
        if (m.matchId !== this.room?.matchId || !['COUNTDOWN', 'PLAYING'].includes(this.room.phase) ||
            !Number.isSafeInteger(m.tick) || m.tick <= (this.sync?.tick ?? -1)) return;
        this.sync = this.payload(m); this.command('SyncMultiplayer', this.sync); break;
      case 'RESULT':
        if (m.matchId !== this.room?.matchId || !['PLAYING', 'RESULT'].includes(this.room.phase)) return;
        if (!this.result) {
          this.resultPlayers = structuredClone(this.room.players);
          this.result = this.payload(m); this.command('FinishMultiplayer', this.result);
        }
        this.send(link, 'RESULT_ACK', { matchId: m.matchId }); break;
      case 'ABORT':
        if (m.matchId !== this.room?.matchId) return;
        this.error = m.reason; this.initializing = null;
        this.command('StopMultiplayer', { matchId: m.matchId }); break;
      case 'PING': this.send(link, 'PONG'); break;
      case 'CLOSED': this.error = 'ホストが部屋を終了しました。'; this.close(false); return;
      default: break;
    }
    this.notify();
  }

  payload(message) {
    const data = { ...message };
    delete data.type; delete data.protocolVersion; delete data.roomEpoch;
    return data;
  }
  setReady(ready) {
    if (this.closed) return;
    if (this.isHost) { if (this.host.setReady('p0', ready)) this.publish(); }
    else this.sendToHost('READY', { ready, readyVersion: this.room?.readyVersion });
  }
  setGameMode(mode) { if (!this.closed && this.isHost) { this.host.setGameMode(mode); this.publish(); } }
  setLivesMode(enabled) { if (!this.closed && this.isHost) { this.host.setLivesMode(enabled); this.publish(); } }
  setSoloMode(enabled) { if (!this.closed && this.isHost) { this.host.setSoloMode(enabled); this.publish(); } }
  setBossPlayer(playerId) { if (!this.closed && this.isHost) { this.host.setBossPlayer(playerId); this.publish(); } }
  updateSword(sword) {
    if (this.closed) return;
    this.sword = validateSword(sword);
    if (this.isHost) { this.host.updateSword('p0', this.sword); this.publish(true); }
    else this.sendToHost('SWORD', { swordData: this.sword });
  }
  sendToHost(type, data) { const link = this.links.values().next().value; if (link) this.send(link, type, data); }

  prepare() {
    if (!this.view().canStart) throw new Error('全員の準備と武器データの受信を待ってください。');
    this.host.prepare(); this.publish();
    const config = { matchId: this.room.matchId, gameMode: this.room.gameMode, livesMode: this.room.livesMode,
      soloMode: this.room.soloMode, soloBuff: this.room.soloMode ? { ...SOLO_BUFF } : null,
      players: this.assignRoles(this.room.players) };
    this.loadDeadline = this.now() + 60000;
    this.broadcast('PREPARE', config); this.initialize(config);
  }

  // Fisher-Yates。乱数は注入されたものを使うので、テストから並びを固定できる。
  shuffle(items) {
    for (let i = items.length - 1; i > 0; i--) {
      const j = Math.floor(this.random() * (i + 1));
      [items[i], items[j]] = [items[j], items[i]];
    }
    return items;
  }

  // 陣営とスポーン位置を決めて PREPARE で全員に配る。
  // 1vs3 ではホストが指名したボスを spawnIndex 0 に固定し、トリオだけを残りの席で
  // シャッフルする。こうしないと陣営が入り混じった配置で試合が始まってしまう。
  assignRoles(players) {
    const describe = (player, team, spawnIndex) => ({ playerId: player.playerId,
      slotIndex: player.slotIndex, spawnIndex, team, swordData: player.swordData });
    if (!this.room.soloMode) {
      const spawnSlots = this.shuffle(players.map((_, i) => i));
      return players.map((player, i) => describe(player, 0, spawnSlots[i]));
    }
    // 指名が有効かどうかは canStart() が既に確かめているので、ここでは見つかる前提でよい
    const bossId = this.room.bossPlayerId;
    const trioSpawns = this.shuffle(Array.from({ length: players.length - 1 }, (_, i) => i + 1));
    let nextTrioSpawn = 0;
    return players.map(player => player.playerId === bossId
      ? describe(player, 0, 0)
      : describe(player, 1, trioSpawns[nextTrioSpawn++]));
  }

  initialize(config) {
    this.initializing = config.matchId;
    this.result = null; this.resultPlayers = []; this.sync = null; this.sequence = 0; this.error = '';
    this.command('InitializeMultiplayer', { ...config, localPlayerId: this.localPlayerId, isHost: this.isHost });
    this.notify();
  }

  loaded(playerId, matchId) {
    if (!this.host.markLoaded(playerId, matchId)) return;
    this.publish();
    if (this.room.phase === 'COUNTDOWN') {
      this.loadDeadline = Infinity;
      this.command('BeginMultiplayer', { matchId });
    }
  }

  unityEvent(type, data) {
    if (this.closed || !data || data.matchId !== this.room?.matchId) return;
    if (type === 'INITIALIZED') {
      if (this.isHost) this.loaded('p0', data.matchId); else this.sendToHost('INITIALIZED', data);
    } else if (type === 'LOAD_FAILED') {
      if (this.isHost) this.abort('ゲームの初期化に失敗しました。'); else this.sendToHost('LOAD_FAILED', data);
    } else if (type === 'INPUT' && this.room.phase === 'PLAYING') {
      const isPrimary = data.action === 'PRIMARY' && ['LEFT', 'RIGHT'].includes(data.direction);
      // SUPPRESS はボスの制圧。撃てる条件はUnity側が判断するので、ここは中継するだけ。
      const isUltimate = data.action === 'ULTIMATE' || data.action === 'SUPPRESS';
      if (!isPrimary && !isUltimate) return;
      const input = isPrimary
        ? { matchId: data.matchId, seq: ++this.sequence, action: 'PRIMARY', direction: data.direction }
        : { matchId: data.matchId, seq: ++this.sequence, action: data.action };
      if (this.isHost) this.command('ReceiveMultiplayerInput', { ...input, playerId: 'p0' });
      else this.sendToHost('INPUT', input);
    } else if (this.isHost && type === 'PLAYING') {
      if (this.host.beginPlaying(data.matchId)) this.publish();
    } else if (this.isHost && type === 'SYNC' && ['COUNTDOWN', 'PLAYING'].includes(this.room.phase)) {
      if (!Number.isSafeInteger(data.tick) || data.tick <= (this.sync?.tick ?? -1)) return;
      this.sync = data; this.broadcast('SYNC', data); this.notify();
    } else if (this.isHost && type === 'RESULT' && this.host.finish(data.matchId)) {
      this.resultPlayers = structuredClone(this.room.players);
      this.result = data; this.publish(); this.broadcast('RESULT', data); this.notify();
    }
  }

  autoStartTick(now) {
    if (!this.isHost || !this.host.autoStart) return;
    if (this.room.phase !== 'LOBBY' || !this.host.canStart()) { this.cancelAutoStart(); return; }
    const full = this.room.players.length >= this.host.seatLimit;
    this.gatheredAt ??= now;
    if (full) this.fullAt ??= now; else this.fullAt = null;
    const deadline = full ? this.fullAt + AUTO_START_DELAY : this.gatheredAt + FILL_TIMEOUT;
    if (deadline !== this.autoStartAt) { this.autoStartAt = deadline; this.publish(); }
    // 武器データ(画像込み)が全員に届くまでは開始しない（view().canStart が受信完了を見ている）。
    // 通信が遅いと受信完了がdeadlineより後になり得るので、その場合は受信完了からも
    // ASSETS_READY_DELAY分だけ別途待つ（そうしないと猶予が実質ゼロになってしまう）
    const ready = this.view().canStart;
    if (ready) this.readyAt ??= now; else this.readyAt = null;
    if (now >= deadline && ready && now >= this.readyAt + ASSETS_READY_DELAY) { this.cancelAutoStart(); this.prepare(); }
  }

  cancelAutoStart() {
    this.gatheredAt = null; this.fullAt = null; this.readyAt = null;
    if (this.autoStartAt === null) return;
    this.autoStartAt = null;
    if (this.room.phase === 'LOBBY') this.publish();
  }

  abort(reason) {
    const matchId = this.room.matchId;
    this.broadcast('ABORT', { matchId, reason });
    this.command('StopMultiplayer', { matchId });
    this.error = reason; this.initializing = null; this.loadDeadline = Infinity;
    this.host.abort(); this.publish();
  }

  returnToLobby() {
    if (this.closed) return;
    if (this.isHost) { if (this.host.returnToLobby('p0')) this.publish(); }
    else this.sendToHost('RETURN', { matchId: this.room?.matchId });
  }

  disconnected(link) {
    if (this.links.get(link.conn.peer) !== link) return;
    this.links.delete(link.conn.peer);
    if (this.closed) return;
    if (this.isHost) {
      const change = this.host.removeConnection(link.conn.peer);
      if (change.kind === 'ABORT') this.abort('参加者との接続が切れたため、開始を取り消しました。');
      else {
        if (change.kind === 'FORFEIT') this.command('ForfeitMultiplayer', { matchId: this.room.matchId, playerId: change.playerId });
        this.publish();
      }
    } else {
      this.error ||= 'ホストとの接続が切れました。';
      this.close(false);
    }
  }

  pump() {
    if (this.closed) return;
    const now = this.now();
    if (this.isHost && this.room.phase === 'LOADING' && now >= this.loadDeadline) this.abort('読み込みが時間内に完了しませんでした。');
    for (const link of [...this.links.values()]) {
      if (now - link.lastSeen >= 10000 || (!link.playerId && now - link.attachedAt >= 10000)) {
        this.disconnected(link); link.conn.close(); continue;
      }
      if (link.pendingSync && link.conn.bufferSize === 0) {
        const sync = link.pendingSync; link.pendingSync = null;
        if (sync.matchId === this.room?.matchId && ['COUNTDOWN', 'PLAYING'].includes(this.room.phase)) this.send(link, 'SYNC', this.payload(sync));
      }
      if (this.isHost && this.result && link.playerId && link.resultAck !== this.result.matchId && now - link.lastResult >= 1000) {
        this.send(link, 'RESULT', this.result); link.lastResult = now;
      }
      if (now - this.lastHeartbeat >= 1000) this.send(link, 'PING');
    }
    if (now - this.lastHeartbeat >= 1000) this.lastHeartbeat = now;
    this.autoStartTick(now);
  }

  close(notifyPeers = true) {
    if (this.closed) return;
    this.closed = true;
    if (notifyPeers) {
      if (this.isHost) this.broadcast('CLOSED'); else this.sendToHost('LEAVE');
    }
    if (ACTIVE.includes(this.room?.phase)) this.command('StopMultiplayer', { matchId: this.room.matchId });
    for (const link of this.links.values()) link.conn.close();
    this.links.clear(); this.notify();
  }
}
