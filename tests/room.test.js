import test from 'node:test';
import assert from 'node:assert/strict';
import { HostRoom, MAX_HP, MAX_ATTACK, MAX_WEIGHT, swordForGameMode, validateSword } from '../src/network/HostRoom.js';
import { BOSS_NPC_SWORD, NPC_PLAYER_ID } from '../src/network/bossNpcSword.js';

const sword = { name: 'テスト剣', hp: 100, attack: 50, weight: 50, imageStr: 'aGVsbG8=' };
function fullRoom(count = 4) {
  const room = new HostRoom({ roomEpoch: 'room-a', hostSword: sword });
  for (let i = 1; i < count; i++) {
    room.reserve(`peer-${i}`);
    room.join(`peer-${i}`, sword);
  }
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  return room;
}

test('pending connections reserve seats synchronously, including the host', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword });
  assert.equal(room.reserve('a'), true);
  assert.equal(room.reserve('b'), true);
  assert.equal(room.reserve('c'), true);
  assert.equal(room.reserve('d'), false);
  assert.equal(room.reserve('a'), false);
  room.removeConnection('b');
  assert.equal(room.reserve('d'), true);
});

test('players keep distinct IDs when a vacated seat is reused', () => {
  const room = fullRoom();
  const oldId = room.playerForConnection('peer-1');
  room.removeConnection('peer-1');
  room.reserve('new-peer');
  room.join('new-peer', sword);
  assert.notEqual(room.playerForConnection('new-peer'), oldId);
  assert.equal(room.snapshot().players.length, 4);
  assert.ok(room.snapshot().players.every(p => !p.ready));
});

test('any two to four ready players may start, but not one and not a half-ready room', () => {
  const room = fullRoom();
  room.setReady(room.playerForConnection('peer-3'), false);
  assert.equal(room.canStart(), false);
  room.removeConnection('peer-3');
  assert.equal(room.canStart(), false); // 入退室は全員の準備を解除する
  assert.throws(() => room.prepare());
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), true);
  assert.equal(room.prepare().players.length, 3);
});

test('a two-seat room turns away a third connection', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword, seatLimit: 2 });
  assert.equal(room.reserve('a'), true);
  assert.equal(room.reserve('b'), false);
  room.join('a', sword);
  assert.deepEqual(room.snapshot().players.map(p => p.slotIndex), [0, 1]);
  assert.equal(room.snapshot().seatLimit, 2);
});

test('seat limits outside two to four are rejected', () => {
  for (const seatLimit of [1, 5, 2.5, '2', null]) {
    assert.throws(() => new HostRoom({ roomEpoch: 'a', hostSword: sword, seatLimit }));
  }
});

test('部屋を作るときに最初のルールを決められる', () => {
  const koma = new HostRoom({ roomEpoch: 'a', hostSword: sword, gameMode: '1' });
  assert.equal(koma.snapshot().gameMode, '1');
  assert.equal(new HostRoom({ roomEpoch: 'a', hostSword: sword }).snapshot().gameMode, '0');
  for (const gameMode of ['2', '', 0, null]) {
    assert.throws(() => new HostRoom({ roomEpoch: 'a', hostSword: sword, gameMode }));
  }
});

test('an auto-start room does not wait for ready buttons', () => {
  const auto = new HostRoom({ roomEpoch: 'a', hostSword: sword, seatLimit: 2, autoStart: true });
  auto.reserve('a'); auto.join('a', sword);
  assert.ok(auto.snapshot().players.every(p => !p.ready));
  assert.equal(auto.canStart(), true);
  const manual = new HostRoom({ roomEpoch: 'a', hostSword: sword, seatLimit: 2 });
  manual.reserve('a'); manual.join('a', sword);
  assert.equal(manual.canStart(), false);
});

test('an auto-start room still needs two connected players in the lobby', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword, autoStart: true });
  assert.equal(room.canStart(), false);
  room.reserve('a'); room.join('a', sword);
  assert.equal(room.canStart(), true);
  room.removeConnection('a');
  assert.equal(room.canStart(), false);
});

test('a lone host cannot start a match', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword });
  room.setReady('p0', true);
  assert.equal(room.canStart(), false);
  assert.throws(() => room.prepare());
});

test('vacated seats close up so slots stay contiguous for the arena', () => {
  const room = fullRoom();
  room.removeConnection('peer-1');
  assert.deepEqual(room.snapshot().players.map(p => p.slotIndex), [0, 1, 2]);
  assert.deepEqual(room.snapshot().players.map(p => p.playerId), ['p0', 'p2', 'p3']);
  room.reserve('late'); room.join('late', sword);
  assert.deepEqual(room.snapshot().players.map(p => p.slotIndex), [0, 1, 2, 3]);
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.deepEqual(room.prepare().players.map(p => p.slotIndex).sort(), [0, 1, 2, 3]);
});

test('all four initialize the same match before the countdown starts', () => {
  const room = fullRoom();
  const match = room.prepare();
  assert.equal(room.reserve('late-peer'), false);
  assert.equal(room.markLoaded('p0', 'old-match'), false);
  for (const p of match.players.slice(0, 3)) room.markLoaded(p.playerId, match.matchId);
  assert.equal(room.snapshot().phase, 'LOADING');
  room.markLoaded(match.players[3].playerId, match.matchId);
  assert.equal(room.snapshot().phase, 'COUNTDOWN');
});

test('input is bound to its connection, deduplicated, and blocked until play', () => {
  const room = fullRoom();
  const match = room.prepare();
  const msg = { matchId: match.matchId, seq: 1, action: 'PRIMARY', direction: 'RIGHT', playerId: 'p0' };
  assert.equal(room.acceptInput('peer-2', msg, 1000), null);
  for (const p of match.players) room.markLoaded(p.playerId, match.matchId);
  room.beginPlaying(match.matchId);
  const input = room.acceptInput('peer-2', msg, 1000);
  assert.equal(input.playerId, room.playerForConnection('peer-2'));
  assert.equal(room.acceptInput('peer-2', msg, 1200), null);
  assert.equal(room.acceptInput('unknown', { ...msg, seq: 2 }, 1200), null);
  assert.equal(room.acceptInput('peer-2', { ...msg, seq: 2, matchId: 'old' }, 1200), null);
  assert.equal(room.acceptInput('peer-2', { ...msg, seq: 2, action: 'Tornado' }, 1200), null);
  assert.equal(room.acceptInput('peer-2', { ...msg, seq: 2 }, 1001), null);
  assert.ok(room.acceptInput('peer-2', { ...msg, seq: 2 }, 1200));
});

test('a loading disconnect aborts the start; a playing disconnect is a forfeit', () => {
  const room = fullRoom();
  room.prepare();
  assert.equal(room.removeConnection('peer-3').kind, 'ABORT');
  assert.equal(room.snapshot().phase, 'LOBBY');
  const playing = fullRoom();
  const match = playing.prepare();
  for (const p of match.players) playing.markLoaded(p.playerId, match.matchId);
  playing.beginPlaying(match.matchId);
  assert.equal(playing.removeConnection('peer-3').kind, 'FORFEIT');
  assert.equal(playing.snapshot().phase, 'PLAYING');
});

test('changing weapons clears readiness and rejects invalid stats', () => {
  const room = fullRoom();
  room.updateSword('p1', { ...sword, name: '新しい剣' });
  assert.equal(room.canStart(), false);
  // ホストは準備完了を押さない（対戦開始が準備完了を兼ねる）ので、持ち替えても開始できるまま
  room.setReady('p1', true);
  room.updateSword('p0', { ...sword, name: '新しい剣' });
  assert.equal(room.canStart(), true);
  // 送られてくる値は柄補正込みの最終値なので、上限は剣本体の最大値そのものではなく
  // 柄が乗せうる最大の補正込みの値(MAX_HP等)。それより上だけを不正として弾く。
  for (const hp of [NaN, Infinity, -1, 0, MAX_HP + 1]) {
    assert.throws(() => room.updateSword('p0', { ...sword, hp }));
  }
  for (const attack of [NaN, Infinity, -1, 0, MAX_ATTACK + 1]) {
    assert.throws(() => room.updateSword('p0', { ...sword, attack }));
  }
  for (const weight of [NaN, Infinity, -1, 0, MAX_WEIGHT + 1]) {
    assert.throws(() => room.updateSword('p0', { ...sword, weight }));
  }
  assert.doesNotThrow(() => room.updateSword('p0', { ...sword, hp: MAX_HP, attack: MAX_ATTACK, weight: MAX_WEIGHT }));
  assert.throws(() => room.updateSword('missing', sword));
});

test('rematch uses a new match ID and clears previous load/input state', () => {
  const room = fullRoom();
  const first = room.prepare();
  for (const p of first.players) room.markLoaded(p.playerId, first.matchId);
  room.beginPlaying(first.matchId);
  room.finish(first.matchId);
  for (const p of first.players) room.returnToLobby(p.playerId);
  for (const p of first.players) room.setReady(p.playerId, true);
  const second = room.prepare();
  assert.notEqual(first.matchId, second.matchId);
  assert.equal(room.markLoaded('p0', first.matchId), false);
  assert.equal(room.snapshot().phase, 'LOADING');
});

test('a two-player room uses the same lifecycle', () => {
  const room = fullRoom(2);
  assert.equal(room.canStart(), true);
  assert.equal(room.prepare().players.length, 2);
});

test('snapshots cannot mutate authoritative room state', () => {
  const room = fullRoom();
  room.snapshot().players[0].swordData.hp = 1;
  assert.equal(room.snapshot().players[0].swordData.hp, 100);
});

test('leaving results releases the rematch barrier for the remaining players', () => {
  const room = fullRoom();
  const match = room.prepare();
  for (const p of match.players) room.markLoaded(p.playerId, match.matchId);
  room.beginPlaying(match.matchId);
  room.finish(match.matchId);
  for (const p of match.players.slice(0, 3)) room.returnToLobby(p.playerId);
  assert.equal(room.snapshot().phase, 'RESULT');
  room.removeConnection('peer-3');
  assert.equal(room.snapshot().phase, 'LOBBY');
  assert.equal(room.reserve('replacement'), true);
});

test('configuration changes keep ready states and are locked during loading', () => {
  const room = fullRoom();
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  const version = room.snapshot().readyVersion;
  room.setGameMode('1');
  room.setLivesMode(true);
  assert.ok(room.snapshot().players.every(p => p.ready), 'ルールを変えても準備完了は保たれる');
  assert.equal(room.snapshot().readyVersion, version);
  room.prepare();
  assert.throws(() => room.setGameMode('0'));
  assert.throws(() => room.updateSword('p0', sword));
  assert.equal(room.setReady('p0', false), false);
});

test('invalid join does not occupy a permanent player seat', () => {
  const room = new HostRoom({ roomEpoch: 'a', hostSword: sword });
  room.reserve('bad');
  assert.throws(() => room.join('bad', { ...sword, hp: -1 }));
  assert.equal(room.snapshot().players.length, 1);
  room.removeConnection('bad');
  for (const id of ['a', 'b', 'c']) {
    assert.equal(room.reserve(id), true);
    room.join(id, sword);
  }
  assert.equal(room.snapshot().players.length, 4);
});

// ===== 1vs3（ボス vs 三人組） =====

test('solo mode waits for a full room of four before it may start', () => {
  const room = new HostRoom({ roomEpoch: 'solo', hostSword: sword, soloMode: true });
  for (const count of [2, 3]) {
    room.reserve(`peer-${count}`);
    room.join(`peer-${count}`, sword);
    for (const p of room.snapshot().players) room.setReady(p.playerId, true);
    room.setBossPlayer('p0');
    assert.equal(room.snapshot().players.length, count);
    assert.equal(room.canStart(), false, `${count}人では開始できない`);
    assert.throws(() => room.prepare());
  }
  room.reserve('peer-4');
  room.join('peer-4', sword);
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  room.setBossPlayer('p0');
  assert.equal(room.canStart(), true);
});

test('solo mode starts with the host as boss and the host may name someone else', () => {
  const room = fullRoom();
  assert.equal(room.snapshot().bossPlayerId, null, '1vs3 でないあいだは誰もボスではない');
  room.setSoloMode(true);
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.snapshot().bossPlayerId, 'p0', 'ONにした時点ではホストがボス');
  assert.equal(room.canStart(), true, '指名し直さなくても開始できる');
  room.setBossPlayer('p2');
  assert.equal(room.snapshot().bossPlayerId, 'p2');
  assert.equal(room.canStart(), true);
  room.setSoloMode(false);
  assert.equal(room.snapshot().bossPlayerId, null, 'OFFにしたら指名も外れる');
  room.setSoloMode(true);
  room.setLivesMode(true);
  assert.equal(room.snapshot().bossPlayerId, null, '残機制に切り替えても指名は外れる');
});

test('naming a boss does not clear anyone\'s ready state', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  const version = room.snapshot().readyVersion;
  room.setBossPlayer('p1');
  room.setBossPlayer('p3');
  assert.ok(room.snapshot().players.every(p => p.ready), '指名を変えても準備完了は保たれる');
  assert.equal(room.snapshot().readyVersion, version);
});

test('when the boss leaves the host becomes boss again, and the room waits for a fourth player', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  room.setBossPlayer(room.playerForConnection('peer-2'));
  room.removeConnection('peer-2');
  assert.equal(room.snapshot().bossPlayerId, 'p0', '抜けた人の指名は外れ、ホストに戻る');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), false);
});

test('only a seated, connected player may be named boss', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  assert.throws(() => room.setBossPlayer('p9'));
  assert.throws(() => room.setBossPlayer(null));
  room.setBossPlayer('p0');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  room.prepare();
  assert.throws(() => room.setBossPlayer('p1'), 'ロビー外では指名できない');
});

test('a solo room that loses a player cannot start until the seat is filled again', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  room.setBossPlayer('p0');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), true);
  room.removeConnection('peer-3');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), false);
});

test('solo mode is published to guests and keeps ready like any other rule change', () => {
  const room = fullRoom();
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.snapshot().soloMode, false);
  room.setSoloMode(true);
  assert.equal(room.snapshot().soloMode, true);
  assert.ok(room.snapshot().players.every(p => p.ready), 'ルール変更では準備完了を解除しない');
});

test('solo mode and lives mode switch each other off instead of stacking', () => {
  const room = fullRoom();
  room.setLivesMode(true);
  room.setSoloMode(true);
  assert.equal(room.snapshot().livesMode, false);
  assert.equal(room.snapshot().soloMode, true);
  room.setLivesMode(true);
  assert.equal(room.snapshot().soloMode, false);
  assert.equal(room.snapshot().livesMode, true);
  assert.throws(() => new HostRoom({ roomEpoch: 'x', hostSword: sword, soloMode: true, livesMode: true }));
});

test('solo mode exists only in sword mode', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  room.setGameMode('1');
  assert.equal(room.snapshot().soloMode, false, '1vs3 のまま独楽にしたら、特殊ルールはなしに戻る');
  assert.equal(room.snapshot().bossPlayerId, null);
  assert.throws(() => room.setSoloMode(true));
  assert.throws(() => new HostRoom({ roomEpoch: 'x', hostSword: sword, gameMode: '1', soloMode: true }));
});

test('rules cannot be changed once the match has left the lobby', () => {
  const room = fullRoom();
  room.prepare();
  assert.throws(() => room.setSoloMode(true));
});

test('the suppress input is accepted and keeps its own action name', () => {
  const room = fullRoom();
  room.prepare();
  const matchId = room.snapshot().matchId;
  const suppress = { matchId, seq: 1, action: 'SUPPRESS' };
  assert.equal(room.acceptInput('peer-1', suppress, 1000), null, '対戦中でなければ通らない');
  for (const p of room.snapshot().players) room.markLoaded(p.playerId, matchId);
  room.beginPlaying(matchId);
  const accepted = room.acceptInput('peer-1', suppress, 1000);
  assert.equal(accepted.action, 'SUPPRESS');
  assert.equal(accepted.playerId, room.playerForConnection('peer-1'));
  assert.equal(accepted.direction, undefined);
  // 連番と間隔の制限は他の入力と同じ
  assert.equal(room.acceptInput('peer-1', suppress, 1200), null);
  assert.equal(room.acceptInput('peer-1', { ...suppress, seq: 2 }, 1001), null);
  assert.ok(room.acceptInput('peer-1', { ...suppress, seq: 2 }, 1200));
  // 知らないアクションは従来どおり捨てる
  assert.equal(room.acceptInput('peer-1', { ...suppress, seq: 3, action: 'STUN' }, 1400), null);
});

// ---- 1vs3 の NPC ボス ----
// ホスト + ゲスト2人 + NPC の部屋。全員準備完了まで済ませて返す。
function npcRoom() {
  const room = new HostRoom({ roomEpoch: 'npc', hostSword: sword, soloMode: true });
  room.setNpcBoss(true);
  for (const peer of ['peer-1', 'peer-2']) {
    room.reserve(peer);
    room.join(peer, sword);
  }
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  return room;
}

test('an NPC boss takes a seat, becomes the boss and is published to guests', () => {
  const room = npcRoom();
  const snap = room.snapshot();
  assert.equal(snap.npcBoss, true);
  assert.equal(snap.bossPlayerId, NPC_PLAYER_ID);
  assert.equal(snap.players.length, 4);
  const npc = snap.players.find(p => p.npc);
  assert.equal(npc.playerId, NPC_PLAYER_ID);
  assert.equal(npc.swordData.name, BOSS_NPC_SWORD.name);
  assert.deepEqual(snap.players.map(p => p.slotIndex).sort(), [0, 1, 2, 3], '席番号は0..3で重ならない');
  assert.equal(room.canStart(), true, '人間3人の準備完了とNPCで開始できる');
});

test('an NPC boss leaves room for only two guests', () => {
  const room = new HostRoom({ roomEpoch: 'npc', hostSword: sword, soloMode: true });
  room.setNpcBoss(true);
  assert.equal(room.reserve('peer-1'), true);
  assert.equal(room.reserve('peer-2'), true);
  assert.equal(room.reserve('peer-3'), false, 'NPCが1席使うので3人目のゲストは入れない');
  room.setNpcBoss(false);
  assert.equal(room.reserve('peer-3'), true, 'NPCを外せば席が空く');
});

test('the NPC cannot be added to a full room, counting guests still connecting', () => {
  const room = fullRoom();
  room.setSoloMode(true);
  assert.throws(() => room.setNpcBoss(true), /満席/);
  const waiting = new HostRoom({ roomEpoch: 'w', hostSword: sword, soloMode: true });
  for (const peer of ['peer-1', 'peer-2', 'peer-3']) waiting.reserve(peer);
  assert.throws(() => waiting.setNpcBoss(true), /満席/, '入室待ちの接続も席を使っている');
  assert.equal(waiting.snapshot().npcBoss, false);
});

test('the NPC stays ready through joins, leaves and a finished match', () => {
  const room = npcRoom();
  const npcReady = () => room.get(NPC_PLAYER_ID).ready;
  room.removeConnection('peer-2');
  assert.equal(npcReady(), true, '人が抜けて全員の準備完了が外れても、NPCは準備完了のまま');
  room.reserve('peer-3');
  room.join('peer-3', sword);
  assert.equal(npcReady(), true, '人が入っても同じ');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  room.prepare();
  const matchId = room.snapshot().matchId;
  for (const p of room.snapshot().players.filter(p => !p.npc)) room.markLoaded(p.playerId, matchId);
  room.beginPlaying(matchId);
  room.finish(matchId);
  assert.equal(npcReady(), true, '決着後も同じ');
});

test('three humans loading is enough to reach the countdown', () => {
  const room = npcRoom();
  room.prepare();
  const matchId = room.snapshot().matchId;
  assert.equal(room.get(NPC_PLAYER_ID).loaded, true, 'NPCはINITIALIZEDを送らないので最初からロード済み');
  for (const id of ['p0', 'p1']) room.markLoaded(id, matchId);
  assert.equal(room.snapshot().phase, 'LOADING');
  room.markLoaded('p2', matchId);
  assert.equal(room.snapshot().phase, 'COUNTDOWN');
});

test('the room returns to the lobby once every human is back, and can rematch', () => {
  const room = npcRoom();
  room.prepare();
  const matchId = room.snapshot().matchId;
  for (const id of ['p0', 'p1', 'p2']) room.markLoaded(id, matchId);
  room.beginPlaying(matchId);
  room.finish(matchId);
  for (const id of ['p0', 'p1', 'p2']) room.returnToLobby(id);
  assert.equal(room.snapshot().phase, 'LOBBY', 'NPCはRETURNを送らないが、人間が揃えばロビーに戻る');
  assert.equal(room.snapshot().bossPlayerId, NPC_PLAYER_ID, '再戦でもNPCがボスのまま');
  for (const p of room.snapshot().players) room.setReady(p.playerId, true);
  assert.equal(room.canStart(), true);
});

test('an aborted start keeps the NPC in the room', () => {
  const room = npcRoom();
  room.prepare();
  room.removeConnection('peer-1');
  assert.equal(room.snapshot().phase, 'LOBBY');
  assert.ok(room.get(NPC_PLAYER_ID), '切断した人だけが外れ、NPCは残る');
  assert.equal(room.snapshot().bossPlayerId, NPC_PLAYER_ID);
});

test('leaving 1vs3 for none, lives or top mode removes the NPC', () => {
  for (const leave of [r => r.setSoloMode(false), r => r.setLivesMode(true), r => r.setGameMode('1')]) {
    const room = npcRoom();
    leave(room);
    const snap = room.snapshot();
    assert.equal(snap.npcBoss, false);
    assert.equal(snap.bossPlayerId, null);
    assert.ok(snap.players.every(p => !p.npc));
    assert.deepEqual(snap.players.map(p => p.slotIndex).sort(), [0, 1, 2], '空いた席は詰める');
  }
});

test('turning the NPC off hands the boss back to the host and keeps ready', () => {
  const room = npcRoom();
  room.setNpcBoss(false);
  assert.equal(room.snapshot().bossPlayerId, 'p0');
  assert.ok(room.snapshot().players.every(p => p.ready), 'ルール変更なので準備完了は解除しない');
  assert.equal(room.canStart(), false, '1vs3 は4人必要なので、NPCを外すと開始できない');
});

test('nobody may be named boss while the NPC is the boss', () => {
  const room = npcRoom();
  assert.throws(() => room.setBossPlayer('p0'));
  assert.throws(() => room.setBossPlayer('p1'));
  assert.equal(room.snapshot().bossPlayerId, NPC_PLAYER_ID);
});

test('the NPC exists only in a 1vs3 lobby', () => {
  const room = new HostRoom({ roomEpoch: 'x', hostSword: sword });
  assert.throws(() => room.setNpcBoss(true), '1vs3 でなければNPCは入れられない');
  const npc = npcRoom();
  npc.prepare();
  assert.throws(() => npc.setNpcBoss(false), 'ロビーの外では切り替えられない');
});

test('a guest input can never act for the NPC', () => {
  const room = npcRoom();
  room.prepare();
  const matchId = room.snapshot().matchId;
  for (const id of ['p0', 'p1', 'p2']) room.markLoaded(id, matchId);
  room.beginPlaying(matchId);
  const accepted = room.acceptInput('peer-1', { matchId, seq: 1, action: 'PRIMARY', direction: 'LEFT', playerId: NPC_PLAYER_ID }, 1000);
  assert.equal(accepted.playerId, room.playerForConnection('peer-1'), 'playerIdは接続から決まり、パケットの値は使われない');
  assert.equal(room.playerForConnection(NPC_PLAYER_ID), undefined, 'NPCに対応する接続は存在しない');
});

test('独楽モードでは柄の補正を外した剣本体の値で戦う', () => {
  const base = { hp: 300, attack: 5, weight: 5 };
  // 重さ5に重さ-10の柄を付けると、補正込みの値は下限の1に張り付く。引き算では元の5に戻せない
  const slot = { name: '予備', hp: 330, attack: 15, weight: 1, imageStr: 'aGVsbG8=', hiltType: '3', base };
  const data = validateSword({ ...sword, hp: 330, attack: 15, weight: 1, hiltType: '3', base, swords: [slot, null, null] });
  assert.deepEqual(data.base, base);

  const koma = swordForGameMode(data, '1');
  assert.deepEqual([koma.hp, koma.attack, koma.weight], [300, 5, 5]);
  assert.deepEqual([koma.swords[0].hp, koma.swords[0].attack, koma.swords[0].weight], [300, 5, 5]);
  assert.equal(koma.swords[1].isEmpty, true);
  // 剣モードは補正込みのまま
  assert.equal(swordForGameMode(data, '0'), data);
});

test('補正前の値が無い・おかしい剣は、独楽モードでも補正込みのまま戦う', () => {
  const old = validateSword({ ...sword, hp: 330 });
  assert.equal(old.base, null);
  assert.equal(swordForGameMode(old, '1').hp, 330);
  for (const base of [{ hp: 0, attack: 5, weight: 5 }, { hp: 300, attack: 101, weight: 5 }, { hp: 300, attack: 5 }]) {
    assert.equal(validateSword({ ...sword, base }).base, null);
  }
});

test('bossStats matches Unity: buffs HP and attack with half-even rounding, caps HP, keeps weight', async () => {
  const { bossStats, SOLO_BUFF, SOLO_MAX_HP } = await import('../src/network/HostRoom.js');
  const plain = { hp: 400, attack: 40, weight: 30 };
  assert.deepEqual(bossStats(plain), {
    hp: Math.round(400 * SOLO_BUFF.hpMultiplier), attack: Math.round(40 * SOLO_BUFF.attackMultiplier), weight: 30,
  });
  // Mathf.RoundToInt rounds .5 to even (101 * 2.5 = 252.5 -> 252, 103 * 2.5 = 257.5 -> 258)
  if (SOLO_BUFF.hpMultiplier === 2.5) {
    assert.equal(bossStats({ hp: 101, attack: 1, weight: 1 }).hp, 252);
    assert.equal(bossStats({ hp: 103, attack: 1, weight: 1 }).hp, 258);
  }
  if (SOLO_BUFF.attackMultiplier === 3.5) {
    assert.equal(bossStats({ hp: 1, attack: 3, weight: 1 }).attack, 10); // 10.5 -> 10
    assert.equal(bossStats({ hp: 1, attack: 1, weight: 1 }).attack, 4);  // 3.5 -> 4
  }
  assert.equal(bossStats({ hp: 100000, attack: 1, weight: 1 }).hp, SOLO_MAX_HP);
});
