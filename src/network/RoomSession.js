import { HostRoom, PROTOCOL_VERSION, validateSword } from './HostRoom.js';

const ACTIVE = ['LOADING', 'COUNTDOWN', 'PLAYING'];
const withoutImages = room => ({ ...room, players: room.players.map(p => {
  const stats = { ...p.swordData };
  delete stats.imageStr;
  return { ...p, swordData: stats };
}) });

// Transport adapter for PeerJS DataConnection. Time is injected so barriers,
// disconnects and retransmission can be tested without browser timers.
export class RoomSession {
  constructor({ isHost = false, roomEpoch = '', capacity = 4, sword, now = () => performance.now(),
    onChange = () => {}, onUnity = () => {} }) {
    this.isHost = isHost;
    this.epoch = roomEpoch;
    this.sword = validateSword(sword);
    this.now = now;
    this.onChange = onChange;
    this.onUnity = onUnity;
    this.host = isHost ? new HostRoom({ roomEpoch, capacity, hostSword: sword }) : null;
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
    this.room = this.host.snapshot();
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
        if (!this.localPlayerId || !m.room || m.room.players?.length > 4 ||
            (this.room && m.room.revision < this.room.revision)) return;
        const previous = this.room;
        this.room = { ...m.room, players: m.room.players.map(p => ({ ...p,
          swordData: { ...previous?.players.find(old => old.playerId === p.playerId)?.swordData, ...p.swordData } })) };
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
  setCapacity(capacity) { if (!this.closed && this.isHost) { this.host.setCapacity(capacity); this.publish(); } }
  sendToHost(type, data) { const link = this.links.values().next().value; if (link) this.send(link, type, data); }

  prepare() {
    if (!this.view().canStart) throw new Error('全員の準備と武器データの受信を待ってください。');
    this.host.prepare(); this.publish();
    const spawnSlots = Array.from({ length: this.room.capacity }, (_, i) => i);
    for (let i = spawnSlots.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1)); [spawnSlots[i], spawnSlots[j]] = [spawnSlots[j], spawnSlots[i]];
    }
    const config = { matchId: this.room.matchId, gameMode: this.room.gameMode,
      players: this.room.players.map((p, i) => ({ playerId: p.playerId, slotIndex: p.slotIndex, spawnIndex: spawnSlots[i], swordData: p.swordData })) };
    this.loadDeadline = this.now() + 60000;
    this.broadcast('PREPARE', config); this.initialize(config);
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
      if (!['LEFT', 'RIGHT'].includes(data.direction) || data.action !== 'PRIMARY') return;
      const input = { matchId: data.matchId, seq: ++this.sequence, action: 'PRIMARY', direction: data.direction };
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
