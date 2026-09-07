export const PROTOCOL_VERSION = 2;
export const MAX_IMAGE_LENGTH = 4 * 1024 * 1024;

export function validateSword(sword) {
  if (!sword || typeof sword.name !== 'string' || !sword.name.trim() || sword.name.length > 100 ||
      !Number.isInteger(sword.hp) || sword.hp < 1 || sword.hp > 1000 ||
      !Number.isInteger(sword.attack) || sword.attack < 1 || sword.attack > 100 ||
      !Number.isInteger(sword.weight) || sword.weight < 1 || sword.weight > 100 ||
      typeof sword.imageStr !== 'string' || !sword.imageStr || sword.imageStr.length > MAX_IMAGE_LENGTH) {
    throw new Error('武器データが不正です。再錬成してください。');
  }
  const { name, hp, attack, weight, imageStr } = sword;
  return { name, hp, attack, weight, imageStr };
}

// The host owns this model. Transport identities never come from packet playerId fields.
export class HostRoom {
  constructor({ capacity = 4, roomEpoch, hostSword }) {
    if (![2, 4].includes(capacity) || !roomEpoch) throw new Error('Invalid room configuration');
    this.capacity = capacity;
    this.roomEpoch = roomEpoch;
    this.gameMode = '0';
    this.phase = 'LOBBY';
    this.revision = 0;
    this.readyVersion = 0;
    this.nextPlayer = 1;
    this.matchNumber = 0;
    this.matchId = null;
    this.connections = new Map();
    this.players = [this.makePlayer('p0', 0, hostSword)];
    this.inputs = new Map();
  }

  makePlayer(playerId, slotIndex, sword) {
    return { playerId, slotIndex, swordData: validateSword(sword), ready: false, loaded: false,
      connected: true, inLobby: true };
  }

  snapshot() {
    return structuredClone({ protocolVersion: PROTOCOL_VERSION, roomEpoch: this.roomEpoch,
      revision: this.revision, readyVersion: this.readyVersion, capacity: this.capacity, gameMode: this.gameMode,
      phase: this.phase, matchId: this.matchId, players: this.players });
  }

  reserve(connectionId) {
    if (this.phase !== 'LOBBY' || this.connections.has(connectionId) || this.connections.size >= this.capacity - 1) return false;
    this.connections.set(connectionId, null);
    return true;
  }

  join(connectionId, sword) {
    if (!this.connections.has(connectionId) || this.connections.get(connectionId) !== null || this.phase !== 'LOBBY') {
      throw new Error('入室を受け付けられません。');
    }
    const slot = Array.from({ length: this.capacity }, (_, i) => i).find(i => !this.players.some(p => p.slotIndex === i));
    const player = this.makePlayer(`p${this.nextPlayer++}`, slot, sword);
    this.connections.set(connectionId, player.playerId);
    this.players.push(player);
    this.resetReady();
    return player.playerId;
  }

  playerForConnection(connectionId) { return this.connections.get(connectionId); }
  get(playerId) { return this.players.find(p => p.playerId === playerId); }
  resetReady() { this.players.forEach(p => { p.ready = false; p.loaded = false; }); this.revision++; this.readyVersion++; }

  setGameMode(mode) {
    if (this.phase !== 'LOBBY' || !['0', '1'].includes(mode)) throw new Error('モードを変更できません。');
    this.gameMode = mode;
    this.resetReady();
  }

  setCapacity(capacity) {
    if (this.phase !== 'LOBBY' || ![2, 4].includes(capacity) || this.connections.size + 1 > capacity) {
      throw new Error('参加者と接続待ちの人数より少ない定員には変更できません。');
    }
    if (capacity === this.capacity) return;
    this.capacity = capacity;
    this.players.sort((a, b) => a.slotIndex - b.slotIndex).forEach((p, i) => { p.slotIndex = i; });
    this.resetReady();
  }

  updateSword(playerId, sword) {
    if (this.phase !== 'LOBBY' || !this.get(playerId)) throw new Error('武器を変更できません。');
    this.get(playerId).swordData = validateSword(sword);
    this.get(playerId).ready = false;
    this.revision++;
  }

  setReady(playerId, ready) {
    const player = this.get(playerId);
    if (this.phase !== 'LOBBY' || !player?.connected || !player.inLobby || typeof ready !== 'boolean') return false;
    player.ready = ready;
    this.revision++;
    return true;
  }

  canStart() {
    return this.phase === 'LOBBY' && this.players.length === this.capacity &&
      this.players.every(p => p.connected && p.ready && p.inLobby);
  }

  prepare() {
    if (!this.canStart()) throw new Error('全員の準備完了を待ってください。');
    this.matchId = `${this.roomEpoch}-${++this.matchNumber}`;
    this.phase = 'LOADING';
    this.inputs.clear();
    this.players.forEach(p => { p.loaded = false; p.inLobby = false; });
    this.revision++;
    return this.snapshot();
  }

  markLoaded(playerId, matchId) {
    if (this.phase !== 'LOADING' || this.matchId !== matchId || !this.get(playerId)?.connected) return false;
    this.get(playerId).loaded = true;
    if (this.players.every(p => p.loaded)) this.phase = 'COUNTDOWN';
    this.revision++;
    return true;
  }

  beginPlaying(matchId) {
    if (this.phase !== 'COUNTDOWN' || this.matchId !== matchId) return false;
    this.phase = 'PLAYING';
    this.revision++;
    return true;
  }

  acceptInput(connectionId, message, now) {
    const playerId = this.playerForConnection(connectionId);
    if (this.phase !== 'PLAYING' || !playerId || !this.get(playerId)?.connected ||
        message?.matchId !== this.matchId || !Number.isSafeInteger(message.seq) || message.seq < 1 ||
        message.action !== 'PRIMARY' || !['LEFT', 'RIGHT'].includes(message.direction)) return null;
    const prev = this.inputs.get(playerId);
    if (prev && (message.seq <= prev.seq || now - prev.time < 40)) return null;
    this.inputs.set(playerId, { seq: message.seq, time: now });
    return { playerId, matchId: this.matchId, seq: message.seq, action: 'PRIMARY', direction: message.direction };
  }

  removeConnection(connectionId) {
    const playerId = this.connections.get(connectionId);
    this.connections.delete(connectionId);
    if (!playerId) return { kind: 'NONE' };
    const player = this.get(playerId);
    player.connected = false;
    this.revision++;
    if (['LOADING', 'COUNTDOWN'].includes(this.phase)) {
      this.abort();
      return { kind: 'ABORT', playerId };
    }
    if (this.phase === 'PLAYING') return { kind: 'FORFEIT', playerId };
    this.players = this.players.filter(p => p.connected);
    if (this.phase === 'RESULT' && this.players.every(p => p.inLobby)) this.phase = 'LOBBY';
    this.resetReady();
    return { kind: 'LEFT', playerId };
  }

  abort() {
    this.phase = 'LOBBY';
    this.players = this.players.filter(p => p.connected);
    this.players.forEach(p => { p.inLobby = true; });
    this.resetReady();
  }

  finish(matchId) {
    if (this.phase !== 'PLAYING' || this.matchId !== matchId) return false;
    this.phase = 'RESULT';
    this.resetReady();
    return true;
  }

  returnToLobby(playerId) {
    if (!['RESULT', 'LOBBY'].includes(this.phase)) return false;
    const player = this.get(playerId);
    if (!player?.connected) return false;
    player.inLobby = true;
    this.players = this.players.filter(p => p.connected);
    if (this.players.every(p => p.inLobby)) this.phase = 'LOBBY';
    this.revision++;
    return true;
  }
}
