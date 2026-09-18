export const PROTOCOL_VERSION = 5;
export const MAX_IMAGE_LENGTH = 4 * 1024 * 1024;
// 席数は2〜4。通常ロビーは4席で、そろった人数のまま試合を始める。
// ランダムマッチは希望人数をそのまま席数にし、autoStart で準備ボタンなしに開始する。
export const MAX_PLAYERS = 4;
export const MIN_PLAYERS = 2;
// 1vs3（ボス1人 対 トリオ3人）。席が全部埋まらないと成立しないので4人ちょうどを要求する。
export const SOLO_MODE_PLAYERS = 4;
// ボス側の強化。全クライアントが同じ値を使う必要があるため、ここで決めて試合設定に載せて配る。
// 数値はすべて調整中。ここだけを直せば全員に反映される。
export const SOLO_BUFF = Object.freeze({
  hpMultiplier: 3.0,        // MatchRules へ渡すHPと Unity 側の maxHp に掛ける
  // 攻撃力に掛ける。ボスの通常攻撃・必殺技・薙ぎ払いのすべてに効く。
  // 3人を相手にするので、通常攻撃1発でトリオの一撃を上回るくらいを狙っている。
  attackMultiplier: 2.0,
  spGainMultiplier: 1.5,    // 時間経過・被弾によるSP獲得量に掛ける
  // 見た目の大きさに掛ける。剣の当たり判定も一緒に大きくなるので、間合いにも効く。
  scaleMultiplier: 1.5,
  maxSp: 200,               // ボスだけSPゲージが2段階（100=通常必殺 / 200=制圧）
  suppressRadius: 8.0,      // 掌握が届く半径
  // 操作不能になる秒数。溜め1秒＋薙ぎ払いの後、ボスが自由に殴れる時間がここから引いた分になる。
  // 長くすると「3人が何もできずに見ているだけ」の時間がそのまま伸びるので、上げ下げの影響が一番大きい。
  suppressDuration: 4.0,
  // 掌握は「引き寄せ → 薙ぎ払い」の2段構え。下は薙ぎ払いまでの調整値。
  judgmentPullSeconds: 1.0,      // 引き寄せてから斬るまでの溜め
  judgmentPullForce: 60.0,       // 引き寄せる力（質量に掛ける）。重力の約6倍
  // 通常攻撃のダメージ式は「衝突速度(上限20) × 攻撃力 × 0.05」なので、最大の一撃が攻撃力ぶん。
  // 薙ぎ払いはその2発ぶんに留め、とどめは残りの拘束時間で自分で殴って取る想定。
  judgmentDamageMultiplier: 1.3,
  // Impulse で質量を掛けるため、この値がそのまま「速度変化(units/秒)」になる。
  // シーンの通常衝突が bounceForce 20 なので、その2倍を手応えの基準にしている。
  // 上げすぎると相手が遠くへ散り、せっかくの拘束時間を追いかけるだけで使ってしまう。
  judgmentKnockback: 40.0,
  // 薙ぎ払いの動き。ボスが範囲の中心で大きく回って斬り抜ける。
  judgmentSweepSeconds: 0.4,   // 薙ぎ払っている時間
  // 回転速度（度/秒）。既存の巨大回転斬が1080なので、それより遅くして一振りとして読めるようにする。
  // 720 × 0.4秒 ＝ 約0.8回転。ちょうど一回転させたいなら judgmentSweepSeconds を 0.5 にする。
  judgmentSweepSpin: 720.0,
  judgmentSweepScale: 2.0,     // 薙ぎ払い中の剣の大きさ（元の大きさに掛ける）
});

export function validateSword(sword) {
  if (!sword || typeof sword.name !== 'string' || !sword.name.trim() || sword.name.length > 100 ||
      !Number.isInteger(sword.hp) || sword.hp < 1 || sword.hp > 1000 ||
      !Number.isInteger(sword.attack) || sword.attack < 1 || sword.attack > 100 ||
      !Number.isInteger(sword.weight) || sword.weight < 1 || sword.weight > 100 ||
      typeof sword.imageStr !== 'string' || !sword.imageStr || sword.imageStr.length > MAX_IMAGE_LENGTH) {
    throw new Error('武器データが不正です。再錬成してください。');
  }

  // 1. 柄（hiltType）の取得
  // ▼【修正】Unity側(SwordBattle.UltimateRoutine)が直接switchしている値("0"=デフォルト,"1"〜"3")に
  // 合わせる。以前は'default'という別の文字列を使っており、Unity側のswitchに一致せず柄を変更しても
  // 常にデフォルト技のままになっていた
  const hiltType = typeof sword.hiltType === 'string' ? sword.hiltType : '0';

  // 2. 装備インデックス（equippedIndex）の取得
  const equippedIndex = Number.isInteger(sword.equippedIndex) && sword.equippedIndex >= 0 && sword.equippedIndex <= 2 
    ? sword.equippedIndex 
    : 0;

  // 3. 3本分の配列（swords）の構築
  let swords = [];
  if (Array.isArray(sword.swords)) {
    swords = sword.swords.map(s => {
      if (!s) return { name: 'empty', hp: 1, attack: 1, weight: 1, imageStr: '', hiltType: '0', isEmpty: true };
      return {
        name: typeof s.name === 'string' ? s.name : 'empty',
        hp: Number.isInteger(s.hp) ? s.hp : 1,
        attack: Number.isInteger(s.attack) ? s.attack : 1,
        weight: Number.isInteger(s.weight) ? s.weight : 1,
        imageStr: typeof s.imageStr === 'string' ? s.imageStr : '',
        hiltType: typeof s.hiltType === 'string' ? s.hiltType : '0',
        isEmpty: Boolean(s.isEmpty)
      };
    });
  } else {
    swords = [
      { name: sword.name, hp: sword.hp, attack: sword.attack, weight: sword.weight, imageStr: sword.imageStr, hiltType, isEmpty: false },
      { name: 'empty', hp: 1, attack: 1, weight: 1, imageStr: '', hiltType: '0', isEmpty: true },
      { name: 'empty', hp: 1, attack: 1, weight: 1, imageStr: '', hiltType: '0', isEmpty: true }
    ];
  }

  // 4. すべての拡張データを含めて返す（ここで確実に返却する！）
  return {
    name: sword.name,
    hp: sword.hp,
    attack: sword.attack,
    weight: sword.weight,
    imageStr: sword.imageStr,
    hiltType: hiltType,
    swords: swords,
    equippedIndex: equippedIndex
  };
}

// The host owns this model. Transport identities never come from packet playerId fields.
export class HostRoom {
  constructor({ roomEpoch, hostSword, seatLimit = MAX_PLAYERS, autoStart = false, gameMode = '0',
    livesMode = false, soloMode = false }) {
    if (!roomEpoch || !Number.isInteger(seatLimit) || seatLimit < MIN_PLAYERS || seatLimit > MAX_PLAYERS ||
        !['0', '1'].includes(gameMode) || typeof livesMode !== 'boolean' || typeof soloMode !== 'boolean' ||
        (soloMode && livesMode)) {
      throw new Error('Invalid room configuration');
    }
    this.seatLimit = seatLimit;
    this.autoStart = autoStart;
    this.roomEpoch = roomEpoch;
    this.gameMode = gameMode;
    this.livesMode = livesMode;
    this.soloMode = soloMode;
    // 1vs3 で誰がボスをやるか。ホストが指名するまで null で、その間は開始できない。
    this.bossPlayerId = null;
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
      revision: this.revision, readyVersion: this.readyVersion, seatLimit: this.seatLimit, autoStart: this.autoStart, gameMode: this.gameMode,
      livesMode: this.livesMode, soloMode: this.soloMode, bossPlayerId: this.bossPlayerId,
      phase: this.phase, matchId: this.matchId, players: this.players });
  }

  reserve(connectionId) {
    if (this.phase !== 'LOBBY' || this.connections.has(connectionId) || this.connections.size >= this.seatLimit - 1) return false;
    this.connections.set(connectionId, null);
    return true;
  }

  join(connectionId, sword) {
    if (!this.connections.has(connectionId) || this.connections.get(connectionId) !== null || this.phase !== 'LOBBY') {
      throw new Error('入室を受け付けられません。');
    }
    const slot = Array.from({ length: this.seatLimit }, (_, i) => i).find(i => !this.players.some(p => p.slotIndex === i));
    const player = this.makePlayer(`p${this.nextPlayer++}`, slot, sword);
    this.connections.set(connectionId, player.playerId);
    this.players.push(player);
    this.resetReady();
    return player.playerId;
  }

  playerForConnection(connectionId) { return this.connections.get(connectionId); }
  get(playerId) { return this.players.find(p => p.playerId === playerId); }
  resetReady() { this.players.forEach(p => { p.ready = false; p.loaded = false; }); this.revision++; this.readyVersion++; }
  // Unity requires slots 0..n-1, so vacated seats close up while the room waits in the lobby.
  compactSlots() { this.players.sort((a, b) => a.slotIndex - b.slotIndex).forEach((p, i) => { p.slotIndex = i; }); }

  setGameMode(mode) {
    if (this.phase !== 'LOBBY' || !['0', '1'].includes(mode)) throw new Error('モードを変更できません。');
    this.gameMode = mode;
    this.resetReady();
  }

  // 残機モード：剣/独楽どちらとも組み合わせられる独立したON/OFFトグル
  setLivesMode(enabled) {
    if (this.phase !== 'LOBBY' || typeof enabled !== 'boolean') throw new Error('モードを変更できません。');
    this.livesMode = enabled;
    // 1vs3 との併用は当面見送る。ボスの実効HPが「剣の本数 × 強化倍率」で膨らみ、調整が追えなくなるため。
    if (enabled) this.soloMode = false;
    this.resetReady();
  }

  // 1vs3：剣/独楽どちらとも組み合わせられる独立したON/OFFトグル。ボスはホストが指名する。
  setSoloMode(enabled) {
    if (this.phase !== 'LOBBY' || typeof enabled !== 'boolean') throw new Error('モードを変更できません。');
    this.soloMode = enabled;
    if (enabled) this.livesMode = false;
    this.resetReady();
  }

  // ボスの指名。誰がボスかは戦い方に直結するが、ここでは準備完了を解除しない
  // （ホストが指名を試行錯誤するたびに全員がやり直しになるのを避けるため）。
  setBossPlayer(playerId) {
    const player = this.get(playerId);
    if (this.phase !== 'LOBBY' || !player?.connected || !player.inLobby) {
      throw new Error('ボスを指名できません。');
    }
    this.bossPlayerId = playerId;
    this.revision++;
  }

  // 指名した人が抜けたら指名も外す。残ったままだと誰もいない席を指したまま開始できなくなる。
  pruneBossPlayer() {
    if (this.bossPlayerId && !this.get(this.bossPlayerId)) this.bossPlayerId = null;
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
    // 1vs3 は1人対3人が揃い、かつボスが指名されて初めて成立する。
    if (this.soloMode) {
      if (this.players.length !== SOLO_MODE_PLAYERS) return false;
      const boss = this.get(this.bossPlayerId);
      if (!boss?.connected || !boss.inLobby) return false;
    }
    return this.phase === 'LOBBY' && this.players.length >= MIN_PLAYERS && this.players.length <= this.seatLimit &&
      this.players.every(p => p.connected && p.inLobby && (this.autoStart || p.ready));
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
        message?.matchId !== this.matchId || !Number.isSafeInteger(message.seq) || message.seq < 1) return null;
    const isPrimary = message.action === 'PRIMARY' && ['LEFT', 'RIGHT'].includes(message.direction);
    // SUPPRESS はボスの制圧。撃てるかどうか（陣営・SP量）はUnity側のBattlePoliciesが決めるので、
    // ここでは他の入力と同じく順序と間隔だけを見る。
    const isUltimate = message.action === 'ULTIMATE' || message.action === 'SUPPRESS';
    if (!isPrimary && !isUltimate) return null;
    const prev = this.inputs.get(playerId);
    if (prev && (message.seq <= prev.seq || now - prev.time < 40)) return null;
    this.inputs.set(playerId, { seq: message.seq, time: now });
    return isPrimary
      ? { playerId, matchId: this.matchId, seq: message.seq, action: 'PRIMARY', direction: message.direction }
      : { playerId, matchId: this.matchId, seq: message.seq, action: message.action };
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
    this.pruneBossPlayer();
    this.compactSlots();
    if (this.phase === 'RESULT' && this.players.every(p => p.inLobby)) this.phase = 'LOBBY';
    this.resetReady();
    return { kind: 'LEFT', playerId };
  }

  abort() {
    this.phase = 'LOBBY';
    this.players = this.players.filter(p => p.connected);
    this.pruneBossPlayer();
    this.players.forEach(p => { p.inLobby = true; });
    this.compactSlots();
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
    this.pruneBossPlayer();
    this.compactSlots();
    if (this.players.every(p => p.inLobby)) this.phase = 'LOBBY';
    this.revision++;
    return true;
  }
}
