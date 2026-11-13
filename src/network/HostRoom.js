// 6: PREPARE から剣画像を抜いた（ゲストは ROSTER で受け取った画像を使う）
// 7: 1vs3 の NPC ボス。名簿に接続を持たないプレイヤー（npc: true）が入る
import { BOSS_NPC_SWORD, NPC_PLAYER_ID } from './bossNpcSword.js';

export const PROTOCOL_VERSION = 7;
export const MAX_IMAGE_LENGTH = 4 * 1024 * 1024;
// 剣本体のステータス上限(image-process/stats.pyでの生成上限: hp 100〜1000 / attack・weight 1〜100)に、
// 柄(SwordListScreenのHILT_DATABASE)が乗せうる最大の補正値を足した、正規のプレイで届きうる本当の上限。
// ここへ送られてくる値は柄補正を適用した後の最終値なので、剣本体だけの上限で弾くと
// 満タン剣+ボーナス柄のような正規の組み合わせまで「武器データが不正」で弾いてしまう。
export const MAX_HP = 1030;     // 1000(剣本体) + 30(大翼の柄)
export const MAX_ATTACK = 130;  // 100(剣本体) + 30(武骨な柄)
export const MAX_WEIGHT = 120;  // 100(剣本体) + 20(武骨な柄)
// 席数は2〜4。通常ロビーは4席で、そろった人数のまま試合を始める。
// ランダムマッチは希望人数をそのまま席数にし、autoStart で準備ボタンなしに開始する。
export const MAX_PLAYERS = 4;
export const MIN_PLAYERS = 2;
// 1vs3（ボス1人 対 トリオ3人）。席が全部埋まらないと成立しないので4人ちょうどを要求する。
export const SOLO_MODE_PLAYERS = 4;
// ボス側の強化。全クライアントが同じ値を使う必要があるため、ここで決めて試合設定に載せて配る。
// 数値はすべて調整中。ここだけを直せば全員に反映される。
export const SOLO_BUFF = Object.freeze({
  hpMultiplier: 2.0,        // MatchRules へ渡すHPと Unity 側の maxHp に掛ける
  // 攻撃力に掛ける。ボスの通常攻撃・必殺技・薙ぎ払いのすべてに効く。
  // 3人を相手にするので、通常攻撃1発でトリオの一撃を上回るくらいを狙っている。
  attackMultiplier: 3.5,
  spGainMultiplier: 2.5,    // 時間経過・被弾によるSP獲得量に掛ける
  // 見た目の大きさに掛ける。剣の当たり判定も一緒に大きくなるので、間合いにも効く。
  scaleMultiplier: 2.5,
  maxSp: 200,               // ボスだけSPゲージが2段階（100=通常必殺 / 200=制圧）
  suppressRadius: 12.0,      // 掌握が届く半径
  // 掌握斬の動きと時間（拘束時間・吸引・溜め・薙ぎ払いの秒数と回転速度）はここでは決めない。
  // Unity の MultiplayerManager の Inspector（「1vs3：掌握斬の動き」）で調整する。
  // 通常攻撃のダメージ式は「衝突速度(上限20) × 攻撃力 × 0.05」なので、最大の一撃が攻撃力ぶん。
  // 薙ぎ払いはその2発ぶんに留め、とどめは残りの拘束時間で自分で殴って取る想定。
  judgmentDamageMultiplier: 1.3,
  // Impulse で質量を掛けるため、この値がそのまま「速度変化(units/秒)」になる。
  // シーンの通常衝突が bounceForce 20 なので、その2.75倍。一気に力強く薙ぎ払った手応えを出すため強めにしている。
  // 上げすぎると相手が遠くへ散り、せっかくの拘束時間を追いかけるだけで使ってしまう。
  judgmentKnockback: 55.0,
});

// ボスのHPの上限。Unity の MatchRules.MaxHitPoints と同じ値にすること。
export const SOLO_MAX_HP = 4000;
// Unity の Mathf.RoundToInt と同じ丸め（.5 は偶数側へ）。Math.round は .5 を切り上げるので、
// 倍率 2.5 / 3.5 で奇数の値を掛けると試合の値と1ずれる。
const roundHalfEven = value => {
  const floor = Math.floor(value);
  const diff = value - floor;
  if (diff > 0.5) return floor + 1;
  if (diff < 0.5) return floor;
  return floor % 2 === 0 ? floor : floor + 1;
};
// 1vs3 のボスが試合で実際に戦う値（ロビーの表示用）。Unity 側の
// MultiplayerManager.BuffedHp / ApplyBossBuff と必ず同じ式にすること。重さには倍率を掛けない。
export const bossStats = stats => ({
  hp: Math.min(SOLO_MAX_HP, Math.max(1, roundHalfEven(stats.hp * SOLO_BUFF.hpMultiplier))),
  attack: Math.max(1, roundHalfEven(stats.attack * SOLO_BUFF.attackMultiplier)),
  weight: stats.weight,
});

// 柄の補正を外した剣本体の値（錬成で出る範囲: hp 100〜1000 / attack・weight 1〜100）。
// 独楽モードは柄が見えず技も全員共通なので、柄の補正を乗せずにこちらで戦う（swordForGameMode）。
// 引き算で戻すと、補正で下限1に張り付いた値（重さ5の剣に重さ-10の柄など）の元が分からないので、
// 送る側が補正前の値を別に持たせる。古いクライアントが送ってこない時は null（＝補正込みのまま戦う）。
export const MAX_BASE_HP = 1000;
export const MAX_BASE_STAT = 100;
const validBase = base => base && Number.isInteger(base.hp) && base.hp >= 1 && base.hp <= MAX_BASE_HP &&
  Number.isInteger(base.attack) && base.attack >= 1 && base.attack <= MAX_BASE_STAT &&
  Number.isInteger(base.weight) && base.weight >= 1 && base.weight <= MAX_BASE_STAT
  ? { hp: base.hp, attack: base.attack, weight: base.weight } : null;

export function validateSword(sword) {
  if (!sword || typeof sword.name !== 'string' || !sword.name.trim() ||
      !Number.isInteger(sword.hp) || sword.hp < 1 || sword.hp > MAX_HP ||
      !Number.isInteger(sword.attack) || sword.attack < 1 || sword.attack > MAX_ATTACK ||
      !Number.isInteger(sword.weight) || sword.weight < 1 || sword.weight > MAX_WEIGHT ||
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
  let swords;
  if (Array.isArray(sword.swords)) {
    swords = sword.swords.map(s => {
      if (!s) return { name: 'empty', hp: 1, attack: 1, weight: 1, imageStr: '', hiltType: '0', isEmpty: true };
      return {
        name: typeof s.name === 'string' ? s.name : 'empty',
        hp: Number.isInteger(s.hp) && s.hp >= 1 && s.hp <= MAX_HP ? s.hp : 1,
        attack: Number.isInteger(s.attack) && s.attack >= 1 && s.attack <= MAX_ATTACK ? s.attack : 1,
        weight: Number.isInteger(s.weight) && s.weight >= 1 && s.weight <= MAX_WEIGHT ? s.weight : 1,
        imageStr: typeof s.imageStr === 'string' ? s.imageStr : '',
        hiltType: typeof s.hiltType === 'string' ? s.hiltType : '0',
        base: validBase(s.base),
        isEmpty: Boolean(s.isEmpty)
      };
    });
  } else {
    swords = [
      { name: sword.name, hp: sword.hp, attack: sword.attack, weight: sword.weight, imageStr: sword.imageStr, hiltType,
        base: validBase(sword.base), isEmpty: false },
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
    base: validBase(sword.base),
    swords: swords,
    equippedIndex: equippedIndex
  };
}

// 試合で使う値。独楽モード（gameMode "1"）だけ、柄の補正を外した剣本体の値に差し替える。
// 手持ちの剣（swords[]）も揃えないと、残機の持ち替えで補正込みの値に戻ってしまう。
export function swordForGameMode(swordData, gameMode) {
  if (gameMode !== '1' || !swordData) return swordData;
  const strip = s => s && s.base ? { ...s, hp: s.base.hp, attack: s.base.attack, weight: s.base.weight } : s;
  return { ...strip(swordData), swords: Array.isArray(swordData.swords) ? swordData.swords.map(strip) : swordData.swords };
}

// The host owns this model. Transport identities never come from packet playerId fields.
export class HostRoom {
  constructor({ roomEpoch, hostSword, seatLimit = MAX_PLAYERS, autoStart = false, gameMode = '0',
    livesMode = false, soloMode = false }) {
    if (!roomEpoch || !Number.isInteger(seatLimit) || seatLimit < MIN_PLAYERS || seatLimit > MAX_PLAYERS ||
        !['0', '1'].includes(gameMode) || typeof livesMode !== 'boolean' || typeof soloMode !== 'boolean' ||
        (soloMode && livesMode) || (soloMode && gameMode === '1')) {
      throw new Error('Invalid room configuration');
    }
    this.seatLimit = seatLimit;
    this.autoStart = autoStart;
    this.roomEpoch = roomEpoch;
    this.gameMode = gameMode;
    this.livesMode = livesMode;
    this.soloMode = soloMode;
    // 1vs3 で誰がボスをやるか。1vs3 をONにした時点ではホスト（p0）で、ホストが指名し直せる。
    // 1vs3 でないあいだは null。
    this.bossPlayerId = soloMode ? 'p0' : null;
    // 1vs3 のボスを NPC にするか。部屋を作る時点では使わず、ロビーで setNpcBoss() から切り替える。
    this.npcBoss = false;
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

  // NPC ボス。PeerJS の接続を持たないので、準備完了・ロード完了・ロビー復帰を自分で送ってこない。
  // そのため最初から全部済んだ状態で作り、resetReady() と prepare() でもその状態を保つ。
  // connected を true にしておくと、切断した人を名簿から外す filter(p => p.connected) にも消されない。
  makeNpc(slotIndex) {
    return { playerId: NPC_PLAYER_ID, npc: true, slotIndex, swordData: validateSword(BOSS_NPC_SWORD),
      ready: true, loaded: true, connected: true, inLobby: true };
  }

  snapshot() {
    return structuredClone({ protocolVersion: PROTOCOL_VERSION, roomEpoch: this.roomEpoch,
      revision: this.revision, readyVersion: this.readyVersion, seatLimit: this.seatLimit, autoStart: this.autoStart, gameMode: this.gameMode,
      livesMode: this.livesMode, soloMode: this.soloMode, bossPlayerId: this.bossPlayerId, npcBoss: this.npcBoss,
      phase: this.phase, matchId: this.matchId, players: this.players });
  }

  // 席は「ホスト1 + 接続（入室済みと入室待ち）+ NPC」で数える。NPC は接続を持たないので、
  // connections だけで数えると NPC がいても人間がもう1人入れてしまう。
  reserve(connectionId) {
    if (this.phase !== 'LOBBY' || this.connections.has(connectionId) || this.seatsTaken() >= this.seatLimit) return false;
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
  seatsTaken() { return 1 + this.connections.size + this.players.filter(p => p.npc).length; }
  // NPC は準備完了を出し直せないので外さない。外すと canStart() が二度と true にならない。
  resetReady() { this.players.forEach(p => { p.ready = !!p.npc; p.loaded = !!p.npc; }); this.revision++; this.readyVersion++; }
  // Unity requires slots 0..n-1, so vacated seats close up while the room waits in the lobby.
  compactSlots() { this.players.sort((a, b) => a.slotIndex - b.slotIndex).forEach((p, i) => { p.slotIndex = i; }); }

  // ルールの変更では準備完了を解除しない（ホストがルールを選び直すたびに全員がやり直しになるのを避けるため）。
  // setGameMode / setLivesMode / setSoloMode のどれも同じ扱い。
  setGameMode(mode) {
    if (this.phase !== 'LOBBY' || !['0', '1'].includes(mode)) throw new Error('モードを変更できません。');
    this.gameMode = mode;
    // 独楽には 1vs3 がないので、1vs3 のまま独楽にしたら特殊ルールは「なし」に戻す
    if (mode === '1') { this.soloMode = false; this.bossPlayerId = null; this.removeNpc(); }
    this.revision++;
  }

  // 残機モード：剣/独楽どちらとも組み合わせられる独立したON/OFFトグル
  setLivesMode(enabled) {
    if (this.phase !== 'LOBBY' || typeof enabled !== 'boolean') throw new Error('モードを変更できません。');
    this.livesMode = enabled;
    // 1vs3 との併用は当面見送る。ボスの実効HPが「剣の本数 × 強化倍率」で膨らみ、調整が追えなくなるため。
    if (enabled) { this.soloMode = false; this.bossPlayerId = null; this.removeNpc(); }
    this.revision++;
  }

  // 1vs3：剣モードだけのON/OFFトグル（独楽には 1vs3 がない）。ボスはホストが指名する。
  setSoloMode(enabled) {
    if (this.phase !== 'LOBBY' || typeof enabled !== 'boolean') throw new Error('モードを変更できません。');
    if (enabled && this.gameMode === '1') throw new Error('独楽モードでは 1vs3 を選べません。');
    this.soloMode = enabled;
    if (enabled) this.livesMode = false;
    else this.removeNpc();
    // ONにしたら、まずホストをボスにしておく（指名し忘れて開始できないのを避ける）。OFFにしたら指名も外す。
    this.bossPlayerId = enabled ? (this.get(this.bossPlayerId) ? this.bossPlayerId : 'p0') : null;
    this.revision++;
  }

  // 1vs3 のボスを NPC にする／人間に戻す。NPC は空いている席を1つ使う。
  // ルール変更の扱いなので、setSoloMode と同じく準備完了は解除しない。
  setNpcBoss(enabled) {
    if (this.phase !== 'LOBBY' || typeof enabled !== 'boolean' || !this.soloMode) throw new Error('モードを変更できません。');
    if (enabled === this.npcBoss) return;
    if (enabled) {
      // 入室待ちの接続も席を使っているので、名簿の人数ではなく seatsTaken() で数える
      if (this.seatsTaken() >= this.seatLimit) throw new Error('満席のためNPCを入れられません。');
      const slot = Array.from({ length: this.seatLimit }, (_, i) => i).find(i => !this.players.some(p => p.slotIndex === i));
      this.players.push(this.makeNpc(slot));
      this.npcBoss = true;
      this.bossPlayerId = NPC_PLAYER_ID;
    } else this.removeNpc();
    this.revision++;
  }

  // NPC は 1vs3 でしか意味がないので、1vs3 を外す操作（1vs3 OFF・残機制・独楽）ではいっしょに外す。
  // ボスが NPC だったら、ホストに戻す（1vs3 のままなら）。
  removeNpc() {
    if (!this.npcBoss && !this.players.some(p => p.npc)) return;
    this.players = this.players.filter(p => !p.npc);
    this.npcBoss = false;
    if (this.bossPlayerId === NPC_PLAYER_ID) this.bossPlayerId = this.soloMode ? 'p0' : null;
    this.compactSlots();
  }

  // ボスの指名。誰がボスかは戦い方に直結するが、ここでは準備完了を解除しない
  // （ホストが指名を試行錯誤するたびに全員がやり直しになるのを避けるため）。
  setBossPlayer(playerId) {
    // NPC ボスのあいだはボスを固定する。人間を指名すると NPC がトリオ側に入ってしまう。
    if (this.npcBoss) throw new Error('NPCボスのあいだはボスを指名できません。');
    const player = this.get(playerId);
    if (this.phase !== 'LOBBY' || !player?.connected || !player.inLobby) {
      throw new Error('ボスを指名できません。');
    }
    this.bossPlayerId = playerId;
    this.revision++;
  }

  // 指名した人が抜けたら、ボスをホストに戻す。残ったままだと誰もいない席を指したまま開始できなくなる。
  pruneBossPlayer() {
    if (this.bossPlayerId && !this.get(this.bossPlayerId)) this.bossPlayerId = this.soloMode ? 'p0' : null;
  }

  updateSword(playerId, sword) {
    if (this.phase !== 'LOBBY' || !this.get(playerId)) throw new Error('武器を変更できません。');
    this.get(playerId).swordData = validateSword(sword);
    this.get(playerId).ready = false;
    this.revision++;
  }

  setReady(playerId, ready) {
    const player = this.get(playerId);
    // 対戦後、ほかの人がまだ結果画面にいるあいだ（phase は RESULT のまま）でも、
    // 先にロビーへ戻った人は準備完了にできる。開始できるのは全員が戻って LOBBY になってから（canStart 側で見る）。
    if (!['LOBBY', 'RESULT'].includes(this.phase) || !player?.connected || !player.inLobby || typeof ready !== 'boolean') return false;
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
    // NPC は INITIALIZED も RETURN も送らないので、ロード完了・ロビーにいる状態のままにする。
    // 外すと markLoaded の「全員ロード完了」と returnToLobby の「全員ロビー」が永遠に揃わない。
    this.players.forEach(p => { if (!p.npc) { p.loaded = false; p.inLobby = false; } });
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
