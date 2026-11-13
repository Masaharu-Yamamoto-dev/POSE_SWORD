# 1vs3 NPCボス 実装計画

作成：2026-11-13 / ブランチ：`feature/npc-boss`（`fixbossUI` から分岐）

## 目的

1vs3 の仕組みはそのままに、1人側（ボス）を NPC にできるモードを追加する。人間3人（ホスト＋ゲスト2人）対 NPC ボス。

## 設計方針（決定済み）

- NPC は **PeerJS の接続を持たないプレイヤー** として `HostRoom.players` に入る（`playerId: 'npc'`, `npc: true`）。
- NPC の入力は **ホストの Unity だけ** が作る（`BossNpcBrain`）。人間の入力と同じ `SwordBattle.ExecuteMultiplayer*` を直接呼ぶ。
  撃てる・動けるの判定は既存の `BattlePolicies` に任せる。
- ゲストは何もしない。NPC の剣は既存の SYNC で他の剣と同じように表示される。
- `MatchRules` / `BattlePolicies` / SYNC / 勝敗判定 / 同士討ち判定は **変更しない**。
- NPC ボスの強化は既存の `SOLO_BUFF` をそのまま使う。人数は人間3人＋NPC で4人ちょうど（`SOLO_MODE_PLAYERS`）。

## JS ↔ Unity の取り決め（この1点だけ）

PREPARE / `InitializeMultiplayer` の `players[]` の各要素に **`isNpc: boolean`** が乗る。
NPC は `playerId: 'npc'`, `team: 0`（ボス）, `spawnIndex: 0`。`team` と `spawnIndex` は既存の `assignRoles` が決める。

---

## 完了済み

### §1 `src/network/bossNpcSword.js`（＋ `bossNpcBlade.js`）
`NPC_PLAYER_ID = 'npc'` と `BOSS_NPC_SWORD`（validateSword を通る固定剣。画像は仮）。テスト `tests/bossNpcSword.test.js`。

### §2 `src/network/HostRoom.js`
- `PROTOCOL_VERSION` 7、`npcBoss` フィールド、`snapshot()` に `npcBoss`
- `makeNpc()` / `setNpcBoss(enabled)` / `removeNpc()` / `seatsTaken()`
- `reserve()` は `seatsTaken()` で席を数える（NPC と入室待ちを含む）
- `resetReady()` と `prepare()` は NPC の ready / loaded / inLobby を保つ
- 1vs3 OFF・残機制・独楽で NPC を外す。NPC ボス中は `setBossPlayer()` が throw
- テスト：`tests/room.test.js` の「1vs3 の NPC ボス」節

---

## 残り：JS 担当（§3〜§5）

### §3 `src/network/RoomSession.js`
1. `setBossPlayer`（L321）の直後に追加：
   ```js
   setNpcBoss(enabled) { if (!this.closed && this.isHost) { this.host.setNpcBoss(enabled); this.publish(true); } }
   ```
   `publish(true)` にする理由：NPC の剣画像をゲストへ ROSTER で配り直し、`assetVersion` を進めるため。
   開始ボタンは `assetsDelivered()` が全員の受け取りを待つので、画像が届く前に始まらない。
2. `assignRoles` の `describe`（L358-359）に `isNpc: !!player.npc` を追加。
   陣営とスポーンは `bossPlayerId === 'npc'` なので既存処理のまま team 0 / spawn 0 になる。

### §4 配線
- `src/network/useRoom.js` L185 の直後：`setNpcBoss: useCallback(enabled => act(s => s.setNpcBoss(enabled)), [act]),`
- `src/App.jsx` L600 の `<LobbyScreen>` に `onNpcBoss={room.setNpcBoss}` を追加。

### §5 `src/screens/LobbyScreen.jsx`
| 場所 | 変更 |
|---|---|
| L170 `RuleSheet` の props | `npcBoss, onNpcBoss, seatsFull` を追加 |
| L204（特殊ルールのグループの直後） | `special === 'solo'` のときだけ「ボス：プレイヤー / NPC」の2択グループ。既存の `fuda-btn` の書き方に合わせる。NPC は `disabled={!canEdit \|\| (seatsFull && !npcBoss)}` |
| L233 props | `onNpcBoss` を追加 |
| L281 付近 | `const npcBoss = Boolean(room.npcBoss);` |
| L289 | `canPickBoss` に `&& !npcBoss` |
| L320 | 1vs3 の人数不足の文言に、`npcBoss` のとき「（NPCボス込み）」を補う |
| L489 | `const isReady = player.npc \|\| (!auto && player.ready && isPresent);` |
| L492 | `role` に `player.npc ? "NPC"` を追加（`isMe` の次、`p0` の前） |
| L672 `<RuleSheet>` 呼び出し | `npcBoss={npcBoss} onNpcBoss={onNpcBoss} seatsFull={playerCount >= seatLimit}` |

`ResultScreen.jsx` / `MatchIntro.jsx` は `bossPlayerId` で判定しているので変更不要。

### JS のテスト（`tests/session.test.js`）
既存の 1vs3 の名簿テスト（L395〜）の後に追加：
- `assignRoles` で NPC が `team 0 / spawnIndex 0 / isNpc true`、人間は `isNpc false`
- `setNpcBoss(true)` で ROSTER が配り直される（`assetVersion` が進む）、PREPARE に `isNpc` が乗る

---

## 残り：Unity 担当（§6〜§8）

### §6 `POSE_SWORD_Unity/Assets/Multiplayer/MultiplayerMessages.cs`
`MultiplayerPlayerConfig`（L4-13）の `team` の後に `public bool isNpc;`。SYNC（`MultiplayerPlayerState`）には入れない。

### §7 `POSE_SWORD_Unity/Assets/Multiplayer/MultiplayerManager.cs`
1. `ValidateTeams()`（L418-）：
   - 通常の試合で `isNpc` が立っていたら例外（既存の `team != BossTeam` 判定に `|| p.isNpc` を足す）
   - 1vs3 で `isNpc` が2人以上、または `isNpc` が BossTeam 以外なら例外
2. `ApplySuppress`（L959）の直前に新規：
   ```csharp
   // NPC ボスの判断用。いま掌握斬を撃ったら何人巻き込めるかを、ApplySuppress と同じ判定で数える
   public int CountSuppressTargets(string bossId)
   ```
   中身は L967-969 と同じ `BattlePolicies.SuppressTargets(bossId, centre.x, centre.y, config.soloBuff.suppressRadius, TargetCandidates())` の件数。
   戻り値の型は `Core/BattlePolicies.cs` を見て合わせる。
3. `CreateSword`（L1273 `if (IsBoss(player)) ApplyBossBuff(battle);` の直後）：
   ```csharp
   if (IsHost && player.isNpc) obj.AddComponent<BossNpcBrain>().Init(this, battle);
   ```
   ホストだけ。剣は arena の子なので StopCurrent で一緒に破棄され、後始末は不要。

### §8 新規 `POSE_SWORD_Unity/Assets/Multiplayer/BossNpcBrain.cs`
- `Init(MultiplayerManager, SwordBattle)`、Inspector で調整できる `decisionInterval = 0.35f` / `suppressMinTargets = 2` / `ultimateRange = 6f`
- `Update` で `owner.IsPlaying && battle.IsAlive` のとき、`decisionInterval × Random(0.8〜1.2)` ごとに1回判断：
  1. `battle.SuppressReady && owner.CountSuppressTargets(battle.PlayerId) >= suppressMinTargets` → `ExecuteMultiplayerSuppress()`
  2. `currentSp >= ultimateSp && currentSp < maxSp` かつターゲットが `ultimateRange` 内 → `ExecuteMultiplayerUltimate()`
  3. それ以外でターゲットがいれば → `ExecuteMultiplayerAction(target.x > self.x)`
- ターゲットは `SwordController.enemyTarget`（ホストの `UpdateTargets` が味方を除いて 0.25 秒ごとに更新）。
- 直接呼び出しは `ReceiveMultiplayerInput` の seq / 40ms 制限を通らないので、間隔は Brain 自身で管理する。
- `.meta` は手で作らない（Unity が開いたときに生成する）。

---

## 並列作業のルール

- JS 担当は `src/` と `tests/` のみ、Unity 担当は `POSE_SWORD_Unity/Assets/` のみを触る。
- **フォントのサブセット（`npm run font:subset`）は担当者は実行しない。** 統合後に1回だけ作り直す。
  JS 担当は UI の文字を足すので `tests/font-subset.test.js` が落ちるのは想定内。それ以外のテストは全て通すこと。
- Unity の静的チェック（`npm run check:unity`）は既存コード（`SkillEffects.cs` などの新しい構文）で失敗しており、
  新しいコードの型チェックまで届かない。Unity 担当は既存 API をコードで照合して書く。最終確認は統合後に Unity Editor で行う。
- 作業後はそれぞれのブランチにコミットする。

## 統合後にやること

1. 両ブランチを `feature/npc-boss` にマージ
2. `npm run font:subset` → `npm test` → `npm run lint`
3. Unity Editor で開いてコンパイル確認 → ホスト1＋ゲスト2 の3タブで通しプレイ
4. WebGL ビルド（ver3.18）
