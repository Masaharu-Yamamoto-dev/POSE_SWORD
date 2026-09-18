# src/network/ メンタルモデル構築ガイド

対象読者：このリポジトリを触るが、JavaScript の経験はほとんど無い人。

`src/network/` の6ファイルについて、JavaScript の文法説明を挟みながら、
定義されているクラス・関数・変数と、その関係性をすべて分解する。

作成日：2026-09-18

---

## 目次

- [第0部：この6ファイルを読むための JavaScript 最小知識](#第0部この6ファイルを読むための-javascript-最小知識)
- [第1部：6ファイルの関係（依存の地図）](#第1部6ファイルの関係依存の地図)
- [第2部：ファイル別リファレンス](#第2部ファイル別リファレンス)
  - [2-1. HostRoom.js — 部屋の「正本」](#2-1-hostroomjs--部屋の正本)
  - [2-2. RoomSession.js — パケットの運び屋](#2-2-roomsessionjs--パケットの運び屋)
  - [2-3. useRoom.js — React と PeerJS の接着剤](#2-3-useroomjs--react-と-peerjs-の接着剤)
  - [2-4. UnityBattleBridge.js — 22行の門番](#2-4-unitybattlebridgejs--22行の門番)
  - [2-5. matchmaking.js — HTTP の窓口](#2-5-matchmakingjs--http-の窓口)
  - [2-6. matchSeeker.js — 相手探しの状態機械](#2-6-matchseekerjs--相手探しの状態機械)
  - [2-7. useRandomMatch.js — MatchSeeker と useRoom の仲人](#2-7-userandommatchjs--matchseeker-と-useroom-の仲人)
- [第3部：関係性 — 誰が誰を呼ぶか](#第3部関係性--誰が誰を呼ぶか)
- [第4部：通しで追う](#第4部通しで追う)
- [第5部：読むときの落とし穴](#第5部読むときの落とし穴)

---

# 第0部：この6ファイルを読むための JavaScript 最小知識

実際にこのコードに出てくるものだけに絞る。

### ① モジュール（ファイル間の受け渡し）

```js
export const PROTOCOL_VERSION = 3;      // 「外に公開する」印
export class HostRoom { ... }
```
```js
import { HostRoom, MAX_PLAYERS } from './HostRoom.js';   // 受け取る側
```

`export` が付いたものだけが他ファイルから見える。
付いていない `const ACTIVE`（RoomSession.js:3）はそのファイル専用。

### ② `const` / `let`

`const` は再代入禁止、`let` は可。ただし **`const` のオブジェクトの「中身」は変えられる**。

```js
const link = { lastSeen: 0 };
link.lastSeen = 100;   // OK（中身の変更）
link = {};             // エラー（再代入）
```

このコードはほぼ全部 `const`。

### ③ アロー関数 `=>`

`function` の短い書き方。

```js
const newRoomId = () => String(Math.floor(100000 + Math.random() * 900000));
// = function newRoomId() { return String(...); }
```

`{}` を書かなければ、その式の結果が自動的に返り値になる（`return` 不要）。

### ④ オブジェクトと「分割代入」

JavaScript のオブジェクトは C# の `Dictionary<string, object>` に近い。

```js
const player = { playerId: 'p0', slotIndex: 0, ready: false };
player.ready = true;
```

**引数での分割代入**がこのコードの至る所に出る。

```js
constructor({ roomEpoch, hostSword, seatLimit = MAX_PLAYERS, autoStart = false }) { ... }
//           ↑ 「1個のオブジェクトを受け取って、その中の roomEpoch / hostSword …を取り出す」
```

呼ぶときは `new HostRoom({ roomEpoch: 'abc', hostSword: sword })` のように
**名前付き引数**のように使える。`= MAX_PLAYERS` は省略時の既定値。

### ⑤ `class` / `this` / `new`

C# とほぼ同じ感覚で読める。ただし：

- `private` / `public` の区別が**無い**（全部公開）
- フィールド宣言が無く、**`constructor` の中で `this.xxx = ...` と書いた瞬間に生える**。
  だから「このクラスがどんな状態を持つか」を知るには constructor を読む必要がある
- 型注釈が無い

```js
export class HostRoom {
  constructor({ roomEpoch }) {
    this.phase = 'LOBBY';        // ここでフィールドが生まれる
    this.connections = new Map();
  }
  get(playerId) { return this.players.find(p => p.playerId === playerId); }  // メソッド（function 不要）
}
```

### ⑥ `Map` と 普通のオブジェクト

`new Map()` はキー→値の辞書。`.set(k,v)` `.get(k)` `.has(k)` `.delete(k)` `.size` `.values()`。

オブジェクト `{}` と違い、挿入順が保たれる・キーに何でも使える・サイズが取れるので、
「接続の一覧」のような動的な集合に使われている。

### ⑦ 配列メソッド（LINQ 相当）

| JS | C# / LINQ |
|---|---|
| `arr.find(p => p.id === x)` | `FirstOrDefault` |
| `arr.filter(p => p.connected)` | `Where` |
| `arr.map(p => p.name)` | `Select` |
| `arr.every(p => p.ready)` | `All` |
| `arr.some(p => p.ready)` | `Any` |
| `arr.forEach(p => ...)` | `foreach` |

`find` は**見つからないと `undefined`** を返す（例外ではない）。

### ⑧ `?.` と `??` と `??=` / `||=`

```js
view?.room            // view が null/undefined なら、全体が undefined（エラーにならない）
a ?? b                // a が null/undefined のときだけ b
this.gatheredAt ??= now;   // null/undefined のときだけ代入
this.error ||= 'ホストとの接続が切れました。';  // 空文字や null のときだけ代入
```

このコードは「まだ部屋が無いかもしれない」状態を大量に扱うので、`?.` が非常に多い。

### ⑨ スプレッド `...`

```js
const packet = { ...data, type, protocolVersion: PROTOCOL_VERSION };
// data の中身を全部展開したうえで、type などを足した新しいオブジェクト
```

「元を壊さずコピーして一部だけ変える」ための道具。React では**必須の作法**。

### ⑩ コールバック（関数を引数として渡す）

このディレクトリの設計の核心。

```js
new RoomSession({
  onChange: state => setView(state),      // 「状態が変わったらこれを呼んで」
  onUnity: command => bridge.dispatch(command),
});
```

RoomSession は React も Unity も知らない。
**「変化したら呼ぶ関数」を外から注入されるだけ**。これで依存の向きが一方向に保たれている。

### ⑪ `async` / `await`（非同期）

```js
async requestTicket() {
  const result = await this.client.enter({ targetSize: this.targetSize });  // 通信の完了を待つ
  if (result.ok) { ... }
}
```

`async` を付けた関数は**必ず Promise（＝将来の値）を返す**。`await` はその完了を待つ。
C# の `async/await` とほぼ同じだが、**JS はシングルスレッド**なので
「待っている間に他のイベントが割り込む」点に注意（→ `busy` フラグの理由、後述）。

### ⑫ `structuredClone`

オブジェクトを深くコピーする組み込み関数。`snapshot()` が使用。
**「渡した先で書き換えられても、こちらの正本は無傷」**を保証するため。

### ⑬ React の hooks（useRoom / useRandomMatch を読むために）

React の心の持ち方は1つだけ覚えれば足りる。

> **画面を更新するたびに、コンポーネント関数の中身が頭から丸ごと再実行される。**

そのため、関数内の普通の変数は毎回リセットされる。それを避ける道具が hooks。

| hook | 意味 | 注意 |
|---|---|---|
| `useState(初期値)` | `[値, 更新関数]` を返す。**更新関数を呼ぶと再描画される** | 値を直接書き換えてはいけない |
| `useRef(初期値)` | `.current` に何でも入る箱。**書き換えても再描画されない** | 再描画をまたいで生き残る |
| `useEffect(関数, [依存])` | 描画後に副作用を実行。返した関数が「後片付け」 | `[]` なら初回のみ、無指定なら毎回 |
| `useCallback(関数, [依存])` | 関数の実体を使い回す（毎回作り直さない） | |

**`useState` = 画面に出す値、`useRef` = 画面に出さないが保持したい物**、という使い分け。
このコードでは PeerJS の接続や RoomSession の実体は全部 `useRef` に入っている（描画とは無関係だから）。

---

# 第1部：6ファイルの関係（依存の地図）

```
                     ┌──────────────────────────────────────────┐
  React 世界          │  App.jsx                                  │
  （画面）            └────┬───────────────────────┬─────────────┘
                          │ useRoom()              │ useRandomMatch()
                     ┌────▼──────────┐        ┌────▼──────────────┐
  React と外界の      │ useRoom.js     │◄───────┤ useRandomMatch.js │
  つなぎ目            │ PeerJS を生成   │ adapter└────┬──────────────┘
                     └────┬───────┬───┘             │
                          │       │                  │
  ───────────────────────┼───────┼──────────────────┼────────────────
                          │       │                  │
  純粋な世界          ┌───▼────┐ ┌▼────────────────┐│ ┌──────────────┐
  （React も通信も    │RoomSess│ │UnityBattleBridge ││ │ MatchSeeker  │
    知らない）        │ion.js  │ └──────────────────┘│ │ matchSeeker  │
                     └───┬────┘                     │ └──────┬───────┘
                         │ new HostRoom()           │        │ client
                     ┌───▼────────┐                 │ ┌──────▼───────┐
                     │ HostRoom.js │                 │ │ matchmaking  │
                     │ 部屋の正本   │                 │ │ fetch の窓口  │
                     └─────────────┘                 │ └──────────────┘
```

**3つの世界に分かれている**、と覚える。

| 世界 | ファイル | 知っていること | 知らないこと |
|---|---|---|---|
| **純粋ロジック** | HostRoom, MatchSeeker, UnityBattleBridge | ルールだけ | React、PeerJS、fetch、時計 |
| **アダプタ** | RoomSession, matchmaking | パケット/HTTPの形 | React、画面 |
| **React 接続** | useRoom, useRandomMatch | React、PeerJS、タイマー | ゲームのルール |

矢印は**必ず上から下へ**しか向かない。
だから HostRoom と MatchSeeker は、ブラウザ無しで `npm test` でテストできる（`tests/` に71件）。

---

# 第2部：ファイル別リファレンス

## 2-1. HostRoom.js — 部屋の「正本」

→ [../src/network/HostRoom.js](../src/network/HostRoom.js)

**役割**：誰が座っていて、今どの局面で、誰が準備完了か。
**ホストのブラウザにだけ1個存在**する。通信もタイマーも一切知らない。

### 公開している定数と関数

| 名前 | 行 | 値 / 内容 |
|---|---|---|
| `PROTOCOL_VERSION` | 1 | `3`。全パケットに付き、違えば接続拒否 |
| `MAX_IMAGE_LENGTH` | 2 | 4MB。剣画像の上限 |
| `MAX_PLAYERS` / `MIN_PLAYERS` | 5-6 | `4` / `2` |
| `validateSword(sword)` | 8 | 検証して**必要な5項目だけのコピーを返す** |

`validateSword` は重要。不正なら例外を投げ、正常なら：

```js
const { name, hp, attack, weight, imageStr } = sword;   // 分割代入
return { name, hp, attack, weight, imageStr };          // 5項目だけの新オブジェクト
```

つまり **`id` / `baseName` / `imageSrc` / `hiltType` は通信に乗らない**。
副作用として、React 側で選んだ柄（`hiltType`）はオンライン対戦の Unity に届かず、
全員デフォルト必殺技になる。
（さらに React 側の値は `"hilt_2"` 形式、Unity 側の期待は `"1"` 形式で、そもそも語彙も合っていない。）

### `class HostRoom` の状態（constructor が持つフィールド）

| フィールド | 型 | 意味 |
|---|---|---|
| `seatLimit` | 2〜4 | 席数 |
| `autoStart` | bool | ランダムマッチの部屋なら true（準備ボタン無しで開始） |
| `roomEpoch` | string | 部屋の世代ID（`crypto.randomUUID()`）。前の部屋のパケットを弾く |
| `gameMode` | `'0'`/`'1'` | 剣モード / 独楽モード |
| `phase` | string | `LOBBY → LOADING → COUNTDOWN → PLAYING → RESULT` |
| `revision` | 数 | 状態が変わるたび +1。**古いパケットを捨てる基準** |
| `readyVersion` | 数 | 「準備が全解除された回数」。遅れて届いた READY を無効化 |
| `nextPlayer` | 数 | `p1`, `p2`… の採番カウンタ |
| `matchNumber` / `matchId` | 数 / string | 試合の通し番号と `${roomEpoch}-${n}` 形式のID |
| `connections` | `Map` | **接続ID → playerId**。席の予約中は値が `null` |
| `players` | 配列 | 名簿。1要素 = `{ playerId, slotIndex, swordData, ready, loaded, connected, inLobby }` |
| `inputs` | `Map` | playerId → 直近の `{ seq, time }`（連打制限用） |

> **設計の勘所**：`connections` のキーは PeerJS の接続ID。
> パケットの中の `playerId` は**信用しない**。
> 「自分は p0 だ」と嘘をつくパケットが来ても、接続IDから本当の playerId を引き直す
> （`acceptInput`:128、`removeConnection`:143）。

### メソッド一覧

| メソッド | 行 | 何をするか | 返り値 |
|---|---|---|---|
| `makePlayer` | 42 | 名簿1件を作る（剣を検証しつつ） | player |
| `snapshot` | 47 | **深いコピー**で今の部屋を1個の値にする | オブジェクト |
| `reserve(connId)` | 53 | 席を仮押さえ（満員/対戦中なら false） | bool |
| `join(connId, sword)` | 59 | 仮押さえ済みの接続を着席させる。**空いている最小の席番号**を割り当て | playerId |
| `playerForConnection` / `get` | 71-72 | 接続ID→playerId / playerId→player | |
| `resetReady` | 73 | 全員の準備を解除し `readyVersion` を進める | |
| `compactSlots` | 75 | 空席を詰めて 0,1,2… に並べ直す（**Unity が連番を要求するため**） | |
| `setGameMode` / `updateSword` / `setReady` | 77-97 | ロビー中だけ許される変更。どれも準備を解除する | |
| `canStart` | 98 | 2人以上・全員接続中・全員ロビー内・（手動なら）全員準備完了 | bool |
| `prepare` | 103 | `matchId` を発番し `LOADING` へ | snapshot |
| `markLoaded` | 113 | 全員揃ったら `COUNTDOWN` へ | bool |
| `beginPlaying` | 121 | `PLAYING` へ | bool |
| `acceptInput` | 128 | **入力の門番**。詳細↓ | 正規化済み入力 or `null` |
| `removeConnection` | 143 | 切断処理。**局面で意味が変わる**↓ | `{kind}` |
| `abort` | 162 | 開始失敗 → 全員ロビーへ戻す | |
| `finish` | 170 | `RESULT` へ | bool |
| `returnToLobby` | 177 | 1人がロビー復帰。全員戻れば `LOBBY` へ | bool |

**`acceptInput` の門番ロジック**（7重チェック）：

```js
if (this.phase !== 'PLAYING' || !playerId || !this.get(playerId)?.connected ||
    message?.matchId !== this.matchId || !Number.isSafeInteger(message.seq) || message.seq < 1) return null;
//  ①対戦中か ②本人か ③接続中か ④今の試合のものか ⑤連番が正当か
const prev = this.inputs.get(playerId);
if (prev && (message.seq <= prev.seq || now - prev.time < 40)) return null;
//  ⑥後戻りした連番は捨てる ⑦40ms以内の連打は捨てる
```

**`removeConnection` の返り値 `kind`**：

| 局面 | kind | 意味 |
|---|---|---|
| LOADING / COUNTDOWN | `ABORT` | 開始を取り消して全員ロビーへ |
| PLAYING | `FORFEIT` | 試合は続行、その人は敗退扱い（Unity へ通知） |
| それ以外 | `LEFT` | 名簿から外して席を詰める |

---

## 2-2. RoomSession.js — パケットの運び屋

→ [../src/network/RoomSession.js](../src/network/RoomSession.js)

**役割**：HostRoom（ルール）と PeerJS（回線）の間の通訳。
ホストでもゲストでも同じクラスを使い、`isHost` で振る舞いを変える。

### 公開しているもの

| 名前 | 行 | 意味 |
|---|---|---|
| `AUTO_START_DELAY` | 5 | 3000ms。満席になってから開始するまでの間 |
| `FILL_TIMEOUT` | 6 | 60000ms。埋まらなくても人数を切り上げて開始 |
| `RoomSession` | 15 | 本体 |
| （非公開）`ACTIVE` | 3 | `['LOADING','COUNTDOWN','PLAYING']` |
| （非公開）`withoutImages` | 7 | 剣画像を抜いたコピーを作る |

### 状態

| フィールド | 意味 |
|---|---|
| `isHost` / `epoch` / `sword` | 役割・部屋世代・自分の剣 |
| `now` | **時計を注入**（テストで偽の時計に差し替えられる） |
| `onChange` / `onUnity` | 外へ通知するコールバック2本 |
| `host` | ホストなら `HostRoom`、ゲストなら `null` ← **ここが役割の分かれ目** |
| `localPlayerId` | 自分のID。ホストは即 `'p0'`、ゲストは `ACCEPT` を受けるまで `null` |
| `links` | `Map`：接続ID → link オブジェクト |
| `room` | 今の部屋（ホストは自分の snapshot、ゲストは受信したもの） |
| `result` / `resultPlayers` / `sync` | 結果・結果時点の名簿・最新の同期データ |
| `closed` / `error` | 終了フラグ・直近のエラー文 |
| `assetVersion` | 画像を配った回数。ACK と突き合わせる |
| `loadDeadline` / `lastHeartbeat` / `sequence` | 読み込み期限 / 心拍 / 自分の入力連番 |
| `autoStartAt` / `gatheredAt` / `fullAt` | 自動開始の時刻計算用 |

**`link` オブジェクト**（接続1本分のメモ、`attach`:60）：

```js
{ conn,           // PeerJS の接続
  attachedAt,     // 繋がった時刻（握手が終わらない接続を切るため）
  lastSeen,       // 最後に何か受け取った時刻（10秒で切断とみなす）
  playerId,       // 着席後に入る
  assetAck,       // どの assetVersion まで受け取ったか
  resultAck,      // どの試合の結果を受け取ったか
  lastResult,     // 結果を最後に送った時刻（再送間隔）
  pendingSync }   // 回線が詰まっている間の保留 SYNC
```

### メソッドの分類

**(a) 外へ出す**

| メソッド | 行 | 内容 |
|---|---|---|
| `view()` | 42 | React に渡す「今の全景」を1個のオブジェクトで作る |
| `notify()` | 48 | `onChange(this.view())` を呼ぶ |
| `command(method, data)` | 49 | `onUnity({method, data})` を呼ぶ（→ Bridge → Unity） |
| `send` / `broadcast` | 51/57 | パケット送信。`protocolVersion` と `roomEpoch` を自動付与 |

`send` に仕込まれた2つの守り：

```js
if (!link.conn.open) return;                                     // 死んだ回線には送らない
if (type === 'SYNC' && link.conn.bufferSize > 0) { link.pendingSync = packet; return; }
//  ↑ 送信待ちが溜まっているなら SYNC は送らず保留（古い位置情報が列を作るのを防ぐ）
```

**(b) 接続の受け入れ** — `attach(conn)`:59

- ホスト：`host.reserve()` に失敗したら `REJECT` を送って閉じる。成功したら `links` に登録
- ゲスト：既に1本繋がっていたら拒否（ゲストはホスト1本だけ）。繋がったら即 `JOIN` を送る

**(c) 受信** — `receive` → `receiveAsHost` / `receiveAsGuest`

`receive`:88 が共通の門番：

```js
if (message.protocolVersion !== PROTOCOL_VERSION) { REJECT して切断 }
if (JOIN/ACCEPT/REJECT 以外 && message.roomEpoch !== this.epoch) return;  // 前の部屋の残響を捨てる
link.lastSeen = this.now();                                              // 心拍の更新
```

そして `isHost` で分岐。`receiveAsHost` の `default: break;` にはコメントで意図が書かれている
— *「ゲストは状態・モード・同期・結果を送ってはならない」*。
ゲストが `STATE` を偽造しても、ホストは黙って捨てる。

**パケット一覧**

```
ゲスト → ホスト : JOIN, READY, SWORD, INITIALIZED, LOAD_FAILED, INPUT,
                  RESULT_ACK, ROSTER_ACK, RETURN, LEAVE, PING
ホスト → ゲスト : ACCEPT, REJECT, ROSTER, STATE, PREPARE, SYNC,
                  RESULT, ABORT, CLOSED, PONG
```

**(d) 名簿の配信** — `publish(withAssets)`:78

```js
if (withAssets) { this.assetVersion++; broadcast('ROSTER', {room, assetVersion}); }   // 画像あり
else            { broadcast('STATE', { room: withoutImages(this.room) }); }            // 画像なし
```

剣が変わった時だけ `ROSTER`（重い）、それ以外は `STATE`（軽い）。
ゲスト側は前回の画像を保持してマージする（`receiveAsGuest` の STATE 処理:150-153）。

**(e) 試合の進行**

| メソッド | 誰が | 内容 |
|---|---|---|
| `prepare()` | ホスト | `canStart` 確認 → スポーン順をシャッフル → `PREPARE` を配信 → 自分も `initialize` |
| `initialize(config)` | 両方 | Unity へ `InitializeMultiplayer` を送る |
| `loaded(playerId, matchId)` | ホスト | 全員揃えば Unity へ `BeginMultiplayer` |
| `unityEvent(type, data)` | 両方 | **Unity から来たイベントの入口**（下で詳述） |
| `abort(reason)` | ホスト | 中断を配信して全員ロビーへ |

**`unityEvent` の分岐表** — ここが Unity 連携の要：

| Unity からの type | ホストの動き | ゲストの動き |
|---|---|---|
| `INITIALIZED` | `loaded('p0', …)` | ホストへ `INITIALIZED` を送る |
| `LOAD_FAILED` | `abort(…)` | ホストへ `LOAD_FAILED` |
| `INPUT` | 自分の入力を自分の Unity に即反映 | ホストへ `INPUT`（連番 `++this.sequence` 付き） |
| `PLAYING` | `host.beginPlaying()` | （無視） |
| `SYNC` | 全ゲストへ `SYNC` を配信 | （無視） |
| `RESULT` | `host.finish()` → 配信 | （無視） |

**(f) 自動開始** — `autoStartTick(now)`:261

```js
const deadline = full ? this.fullAt + AUTO_START_DELAY      // 満席なら3秒後
                      : this.gatheredAt + FILL_TIMEOUT;     // 埋まらなくても60秒後
if (now >= deadline && this.view().canStart) { this.cancelAutoStart(); this.prepare(); }
//                     ↑ 画像が全員に届いていなければ、期限が来ても始めない
```

**(g) 定期処理** — `pump()`:311

250ms ごとに呼ばれ、**時間に関する仕事を全部ここでやる**
（クラス内に `setTimeout` が一切無いので、テストで時計を進めるだけで全挙動を再現できる）：

1. 読み込みが60秒を超えたら中断
2. 10秒無通信の接続を切る／握手が10秒終わらない接続を切る
3. 保留していた `SYNC` を送る
4. 結果の ACK が無い相手へ1秒毎に再送
5. 1秒毎に `PING`
6. `autoStartTick`

**(h) 終了** — `close(notifyPeers)`:332、`disconnected(link)`:294

---

## 2-3. useRoom.js — React と PeerJS の接着剤

→ [../src/network/useRoom.js](../src/network/useRoom.js)

**役割**：PeerJS の `Peer` を作り、RoomSession を生かし、その `view()` を React の state に流す。
**ゲームのルールは一切持たない**。

### 定数

| 名前 | 値 |
|---|---|
| `CONNECT_TIMEOUT` | 15000ms |
| `ID_RETRIES` | 5（ロビーID衝突時の再試行） |
| `newRoomId()` | 6桁の数字文字列をランダム生成 |

### 内部の state と ref

```js
const [view, setView]       = useState(null);   // ← 画面に出す：部屋の全景
const [roomId, setRoomId]   = useState('');     // ← 画面に出す：6桁ID
const [error, setError]     = useState('');
const [connecting, setConnecting] = useState(false);
const [hasArena, setHasArena] = useState(false);  // Unity を DOM に出すか
const [bridge] = useState(() => new UnityBattleBridge());  // ← 一度だけ作る書き方

const sessionRef = useRef(null);   // RoomSession の実体（描画と無関係）
const peerRef    = useRef(null);   // Peer の実体
const timeoutRef = useRef(null);
const leavingRef = useRef(false);  // 「自分から閉じた」のか「落とされた」のかの区別
const closedRef  = useRef(onClosed);
```

> `useState(() => new UnityBattleBridge())` の関数形は **「初回だけ実行して」** という意味。
> `useState(new UnityBattleBridge())` と書くと毎描画で無駄にインスタンスを作ってしまう。

### 主要な部品

| 名前 | 行 | 内容 |
|---|---|---|
| `teardown(notifyPeers)` | 29 | タイマー停止 → session を閉じる → peer を破棄。`leavingRef` で「自発的」と印を付ける |
| `dropped(message)` | 39 | **相手都合の終了**。全 state をリセットし `onClosed` を呼ぶ（→ App が TITLE へ戻す） |
| `open(isHost, sword, targetId, roomOptions)` | 65 | 本体。RoomSession を作り、Peer を開き、接続する |
| `attempt(retriesLeft)` | 84 | ID衝突（`unavailable-id`）なら別IDで再挑戦 |
| `act(run)` | 117 | 「session があれば実行し、例外はエラー文として拾う」共通ラッパ |

### `useEffect` が2つやっていること（46行目）

```js
window.MultiplayerApp = { receiveFromUnity(type, json) { ... } };   // ← Unity から呼ばれる窓口
const interval = setInterval(() => sessionRef.current?.pump(), 250); // ← 心臓
return () => { clearInterval(interval); teardown(true); delete window.MultiplayerApp; };
```

**Unity → JS の唯一の入口がここ**。
Unity の `SendToReact("MP_SYNC", json)` が `window.MultiplayerApp.receiveFromUnity` を呼び、
`MP_` を剥がして `session.unityEvent('SYNC', data)` に渡す。
ただし `MP_READY` だけは session ではなく `bridge.sceneReady()` へ行く。

### 返り値（App.jsx が受け取るもの）

```js
{ view, roomId, error, connecting, hasArena, bridge,
  createRoom, joinRoom, leave,
  setReady, setGameMode, updateSword, start, returnToLobby, reportLoadFailure }
```

`start` は `session.prepare()`、`setReady` は `session.setReady()`… と、ほぼ RoomSession への転送。

---

## 2-4. UnityBattleBridge.js — 22行の門番

→ [../src/network/UnityBattleBridge.js](../src/network/UnityBattleBridge.js)

**役割**：Unity がまだ起動していない間、コマンドを取りこぼさない。

```js
class UnityBattleBridge {
  constructor() { this.ready = false;   // Unity のシーンが MP_READY を出したか
                  this.sender = null;   // react-unity-webgl の sendMessage（ロード完了で入る）
                  this.pending = null;  // 保留中の InitializeMultiplayer
                  this.matchId = null; }
```

**2つの準備完了を待ち合わせる**のがこのクラスの全て：

| 入口 | 誰が呼ぶ |
|---|---|
| `setSender(fn)` | BattleArena.jsx（Unity の wasm ロード完了時） |
| `sceneReady()` | useRoom（Unity から `MP_READY` が届いた時） |

`flush()` は「両方揃っていて保留があるなら送る」。どちらが先に来ても動く。

`dispatch(command)` の判断：

```js
if (method === 'InitializeMultiplayer') { this.matchId = data.matchId; this.pending = command; flush(); return; }
if (command.data.matchId !== this.matchId) return;   // 別の試合のコマンドは捨てる
if (method === 'StopMultiplayer') { this.pending = null; ...; this.matchId = null; }
```

> これで「読み込み中にキャンセルした古い試合が、Unity のロード完了後に突然始まる」事故を防いでいる。
> `tests/bridge.test.js` がまさにそれを検証している。

---

## 2-5. matchmaking.js — HTTP の窓口

→ [../src/network/matchmaking.js](../src/network/matchmaking.js)（37行）

**役割**：`/api/match/enter|poll|leave` を呼ぶだけ。
**例外を投げず、失敗も「断られた理由」として返す**のが約束。

```js
export function createMatchClient({ base = DEFAULT_BASE, fetchImpl = fetch } = {}) {
  const endpoint = normalizeBase(base);
  const call = async (action, body) => { ... };
  return { enter, poll, leave };
}
```

これは**クロージャ**というパターン。`class` を使わず、関数の中で作った `endpoint` を、
返されたオブジェクトの中の関数が覚え続ける（C# のラムダのキャプチャと同じ）。

返り値の形が統一されている：

| 状況 | 返り値 |
|---|---|
| 通信自体が失敗 | `{ ok: false, reason: 'OFFLINE' }` |
| 429（混雑） | `{ ok:false, reason, waiting, retryAfter }` |
| 503（KV未設定） | `{ ok:false, reason:'UNAVAILABLE' }` |
| その他エラー | `{ ok:false, reason:'ERROR' }` |
| 成功 | `{ ok:true, ...サーバーの返した中身 }` |

`fetchImpl = fetch` という引数は**テスト用の差し替え口**（偽の fetch を渡せる）。

---

## 2-6. matchSeeker.js — 相手探しの状態機械

→ [../src/network/matchSeeker.js](../src/network/matchSeeker.js)

**役割**：待合所は「誰が待っているか」しか知らないので、実際に相手を見つける**段取り**をここが持つ。
React も通信方式も知らない。

### 公開している調整値

| 名前 | 値 | 意味 |
|---|---|---|
| `SEARCH_INTERVAL` | 4000ms | 探索の間隔 |
| `HOST_INTERVAL` | 8000ms | 募集広告の間隔 |
| `JOIN_TIMEOUT` | 10000ms | 入室の待ち時間 |
| `EMPTY_POLLS_BEFORE_HOSTING` | 2 | 2回空振りしたら自分が部屋を立てる |

### 依存する2つの相手（constructor で注入）

```js
new MatchSeeker({ client, room, targetSize, now, onChange })
```

- `client` … matchmaking の `{enter, poll, leave}`
- `room` … **アダプタ**。`createRoom / joinRoom / leave / state()` の4つを持つ何か
  （実体は useRandomMatch が作る、useRoom の包み）

MatchSeeker は useRoom を直接知らない。この4メソッドの「契約」だけを知っている
（ファイル冒頭のコメントに明記）。だから `tests/match-seeker.test.js` では
`fakeRoom()` という偽物を渡せる。

### 状態（phase）の遷移図

```
     start()
        ↓
   ┌ ENTERING ┐ 券を取る
   │    │     └─ 混雑 → FULL ──(retryAfter後)──┘
   │    ↓ 成功
   │ SEARCHING ─── 候補あり ──→ JOINING ──成功──→ DONE
   │    │  ↑                      │ 失敗(満員/無応答)
   │    │  └──────────────────────┘ exclude に入れて次の候補へ
   │    │ 2回空振り
   │    ↓
   │  HOSTING ── 誰か来た ──→ ADVERTISING ── 満員/開始 ──→ DONE
   │    │
   │    └─ 自分より古い部屋を発見 → moveTo() → JOINING
   └─ cancel() → CANCELLED
```

`seeking` は `['ENTERING','FULL','SEARCHING','JOINING','HOSTING']` のいずれか。
**この間 App.jsx は探索画面を出し続ける**（自分の部屋が既にできていても）。

### メソッド

| メソッド | 行 | 内容 |
|---|---|---|
| `view()` | 49 | 画面用の要約（phase / seeking / waiting / error / elapsedMs） |
| `start()` | 57 | `ENTERING` へ |
| `tick()` | 63 | **外から500ms毎に呼ばれる唯一の入口** |
| `step(now)` | 76 | phase ごとに処理を振り分ける `switch` |
| `requestTicket()` | 88 | `client.enter` → 券取得 or FULL |
| `search(now)` | 106 | `client.poll` → 候補があれば `joinNext`、無ければカウント |
| `joinNext()` | 122 | 候補配列から1つ取り出して `room.joinRoom` |
| `watchJoin(now)` | 132 | 入室の成否を監視。失敗なら `exclude` に入れて次へ |
| `beginHosting()` | 142 | `room.createRoom` して募集側に回る |
| `advertise(now, seekCandidates)` | 151 | 募集を出しつつ、自分より古い部屋を探す |
| `moveTo(roomId, myRoomId)` | 177 | 自分の募集を畳んで移籍（**券は手放さない** `keepTicket: true`） |
| `matched` / `finish` / `release` | 186-198 | 成立処理と券の返却 |
| `recover(result, now)` | 205 | 失敗からの復帰（券失効なら `ENTERING` へ戻る） |
| `cancel()` | 213 | 中止 |

**`busy` フラグの意味**（`tick`:63）：

```js
async tick() {
  if (this.busy || ['IDLE','DONE','CANCELLED','UNAVAILABLE'].includes(this.phase)) return;
  this.busy = true;
  try { await this.step(this.now()); } catch (e) { ... } finally { this.busy = false; }
}
```

`step` の中で `await`（通信待ち）している間に、500ms のタイマーが次の `tick` を撃ってくる。
JS はシングルスレッドだが**待ち時間には他の処理が割り込める**ので、二重実行を `busy` で防いでいる。
C# の `lock` に相当する役目を、この1つの真偽値が果たしている。

---

## 2-7. useRandomMatch.js — MatchSeeker と useRoom の仲人

→ [../src/network/useRandomMatch.js](../src/network/useRandomMatch.js)

**役割**：MatchSeeker が求める `room` アダプタの契約を、useRoom の実物で満たす。

```js
const [adapter] = useState(() => ({
  createRoom(targetSize) { roomRef.current.createRoom(swordRef.current,
                             { seatLimit: targetSize, autoStart: true, gameMode: modeRef.current }); },
  joinRoom(roomId)       { roomRef.current.joinRoom(roomId, swordRef.current); },
  leave()                { roomRef.current.leave(); },
  state()                { return stateRef.current; },
}));
```

ここに **React 特有の罠への対処**が詰まっている：

- `adapter` は `useState(() => ...)` で**一度だけ**作られ、以後ずっと同じ実体。
  MatchSeeker は長生きなので、毎描画で作り直すと参照がズレる
- しかし adapter の中で使う `room` や `sword` は毎描画で新しくなる。
  そこで `roomRef.current` / `swordRef.current` 越しに読み、
  19行目の**依存配列なしの `useEffect`** で毎描画その ref を最新に更新している

```js
useEffect(() => { roomRef.current = room; swordRef.current = sword; });   // 依存配列が無い＝毎回実行
```

> **これは JS/React 特有の「古い値を掴んだままになる（stale closure）」問題への定石**。
> 「長生きするオブジェクトから短命な値を読むときは ref 越しにする」と覚える。

`stateRef` も同様に毎描画で作り直され、MatchSeeker が `state()` を呼んだ時に
必ず最新の部屋の様子が返るようにしている（22行目）。

返り値は `{ view, start(targetSize, gameMode), cancel() }` の3つだけ。

---

# 第3部：関係性 — 誰が誰を呼ぶか

```
【下向き（命令）】                          【上向き（通知＝コールバック）】

App.jsx                                     App.jsx
 ├ room.createRoom()                         ▲ setView() で再描画
 ▼                                           │
useRoom.open()                              useRoom の onChange
 ├ new RoomSession({onChange, onUnity})      ▲
 ├ new Peer() / peer.connect()               │ this.notify()
 ▼                                           │
RoomSession.prepare()                       RoomSession
 ├ this.host.prepare()  ─────────► HostRoom  ▲ session.unityEvent()
 ├ this.broadcast('PREPARE')► 相手のブラウザ  │
 └ this.command('Initialize…')               │ window.MultiplayerApp.receiveFromUnity()
    ▼ onUnity                                │
UnityBattleBridge.dispatch()                Unity（SendToReact）
    ▼ sender()
Unity（sendMessage）
```

**呼び出しの向きは常に一方向**。下の層（HostRoom）は上（RoomSession）を知らない。
上へ伝えたいことは**コールバックか返り値**でしか出ない。
この規律のおかげで、HostRoom 単体・MatchSeeker 単体でテストが書けている。

### クラス／関数の一覧（早見表）

| ファイル | 種別 | 名前 | 生存期間 | 何個できる |
|---|---|---|---|---|
| HostRoom.js | クラス | `HostRoom` | 部屋が続く間 | **ホストに1個だけ** |
| HostRoom.js | 関数 | `validateSword` | — | 都度呼ばれる |
| RoomSession.js | クラス | `RoomSession` | 部屋が続く間 | 各ブラウザに1個 |
| useRoom.js | 関数(hook) | `useRoom` | App が生きている間 | 1個 |
| UnityBattleBridge.js | クラス | `UnityBattleBridge` | App が生きている間 | 1個（部屋を跨いで再利用） |
| matchmaking.js | 関数 | `createMatchClient` | — | 1個作って使い回す |
| matchSeeker.js | クラス | `MatchSeeker` | 1回の探索の間 | 探索ごとに作り直す |
| useRandomMatch.js | 関数(hook) | `useRandomMatch` | App が生きている間 | 1個 |

---

# 第4部：通しで追う

### シナリオA：ロビーIDを共有して2人で対戦

```
[ホスト] App.handleCreateRoom()
  → useRoom.createRoom(sword) → open(true, …)
      → new RoomSession({isHost:true, roomEpoch: ランダムUUID})
          → new HostRoom() … players=[p0], phase='LOBBY'
      → new Peer('418203')  … 6桁IDで PeerJS に登録
      → peer.on('open') → setRoomId('418203'), setView(session.view())

[ゲスト] App.connectToHost()
  → useRoom.joinRoom('418203', sword) → open(false, …)
      → new Peer() → peer.connect('418203')
      → session.attach(conn) → conn が開いたら JOIN を送信

[ホスト] receiveAsHost('JOIN')
  → host.join(conn.peer, swordData) → 'p1' を採番、席1へ
  → ACCEPT を返す → publish(true) で ROSTER（画像あり）を配信

[ゲスト] receiveAsGuest('ACCEPT') → localPlayerId='p1'
       receiveAsGuest('ROSTER')  → 剣を検証 → ROSTER_ACK を返す
[ホスト] link.assetAck = assetVersion  → view().canStart が true になりうる

… 両者が setReady(true) …

[ホスト] App「開始」→ session.prepare()
  → host.prepare() で matchId 発番、phase='LOADING'
  → スポーン順シャッフル → PREPARE 配信 → 自分も initialize()
  → command('InitializeMultiplayer') → bridge → Unity

[両者] Unity が MP_INITIALIZED
  → window.MultiplayerApp.receiveFromUnity('MP_INITIALIZED', …)
  → session.unityEvent('INITIALIZED', …)
      ホスト: loaded('p0') / ゲスト: ホストへ INITIALIZED を送信
[ホスト] 全員 loaded → phase='COUNTDOWN' → Unity へ BeginMultiplayer
[ホスト] 3秒後 Unity が MP_PLAYING → host.beginPlaying() → phase='PLAYING'

[以降] ホスト Unity の MP_SYNC → broadcast('SYNC') → ゲスト Unity へ SyncMultiplayer
       ゲスト Unity の MP_INPUT → ホストへ INPUT → host.acceptInput() で検証
                                → ホスト Unity へ ReceiveMultiplayerInput
[決着] ホスト Unity の MP_RESULT → host.finish() → RESULT 配信（ACK まで再送）
```

### シナリオB：ランダムマッチ

```
App.startRandomMatch(2)
  → useRandomMatch.start(2, '0')
      → new MatchSeeker({client: matchClient, room: adapter, targetSize: 2})
      → seeker.start() → phase='ENTERING'

500ms 毎に seeker.tick()：
  ENTERING  → client.enter() → 券を取得 → SEARCHING
  SEARCHING → client.poll()  → 部屋なし（1回目・2回目）→ beginHosting()
  HOSTING   → adapter.createRoom(2) → useRoom.createRoom(sword, {autoStart:true})
              → RoomSession が autoStart:true の HostRoom を作る
            → client.poll({room: {roomId:'418203', players:1, …}}) で募集を掲示

（別の人の SEARCHING が この roomId を受け取り、joinRoom してくる）

  HOSTING   → adapter.state().players が 2 → matched() → ADVERTISING
  ADVERTISING → players >= targetSize → finish() → 券を返却、phase='DONE'

一方 RoomSession 側では pump() → autoStartTick() が独立して動いていて：
  満席 → 3秒後に自動で prepare() → シナリオAの後半と同じ流れ
```

**ポイント**：MatchSeeker は「部屋を立てて人を集める」ところまでで役目を終え、
**試合開始は RoomSession の `autoStartTick` が独立して行う**。
2つの仕組みが `adapter.state()` 経由で緩く繋がっているだけ、という構造。

---

# 第5部：読むときの落とし穴

1. **`view()` は毎回新しいオブジェクト**。
   React は「中身が同じでも参照が違えば変わったとみなす」ので、`notify()` を呼ぶたびに再描画される。
   逆に言えば、オブジェクトを直接書き換えて `notify()` を呼ばないと画面は更新されない。

2. **`this` の落とし穴は、このコードでは回避済み**。
   全メソッドがアロー関数経由（`() => this.xxx()`）で渡されているので、
   `this` が外れる典型的バグは無い。
   `setInterval(() => sessionRef.current?.pump(), 250)` のような書き方がそれ。

3. **ホストとゲストで同じクラスが全く違う顔をする**。
   `RoomSession` を読むときは、必ず `this.host` が `null` かどうかを意識する。
   `host` があればホスト、無ければゲスト。

4. **時間に関する処理は全部 `pump()` と `tick()` に集約されている**。
   クラスの中に `setTimeout` が無い。
   「なぜ○秒後に××が起きるのか」を探すときは、この2つのメソッドだけ見れば足りる。

5. **`validateSword` が剣のデータを削る**。
   `id` `baseName` `imageSrc` `hiltType` は通信に乗らない。
   柄ごとの必殺技がオンラインで出ないのはこれが原因
   （さらに React の `"hilt_2"` と Unity の `"1"` で語彙も不一致）。

6. **`exclude` は最大8件**（`.slice(-8)`）。入室に失敗した部屋を覚えておく短い記憶。

---

## 関連ドキュメント

- [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md) — image-process ディレクトリの分析
- [RANDOM_MATCH_PLAN.md](RANDOM_MATCH_PLAN.md) — ランダムマッチの設計計画
- [FOUR_PLAYER_IMPLEMENTATION.md](FOUR_PLAYER_IMPLEMENTATION.md) — 4人対戦の実装記録
