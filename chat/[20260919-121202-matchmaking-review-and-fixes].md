# ランダムマッチ上限設計のレビューと修正

- 日付: 2026-09-19
- ブランチ: `fix/matchlogic`
- 対象: ランダムマッチの待合所（`api/_match/store.js`, `api/match/[action].js`, `src/network/*`）
- 目的（ユーザー最優先方針）: **ユーザーがストレスを感じないこと**。特に **ユーザーが選んだルール（ゲームモード）が必ず満たされること** を優先する。

---

## 1. 背景

ユーザーから、現行のランダムマッチ上限設計について9項目の懸念が提示された。
「本当にその問題があるのか厳しく調査した上でプランを提案してほしい」との依頼。

調査対象コード:

- [api/_match/store.js](../api/_match/store.js) — 待合所ロジック（enter / poll / leave / candidates）
- [api/match/[action].js](../api/match/[action].js) — HTTP ハンドラ
- [api/_match/upstash.js](../api/_match/upstash.js) — Upstash REST クライアント（pipeline は非原子）
- [src/network/matchSeeker.js](../src/network/matchSeeker.js) — 探索の段取り
- [src/network/useRandomMatch.js](../src/network/useRandomMatch.js) — React 層
- [src/network/matchmaking.js](../src/network/matchmaking.js) — API 窓口
- [tests/match-store.test.js](../tests/match-store.test.js) / [tests/match-seeker.test.js](../tests/match-seeker.test.js)

---

## 2. 指摘の検証結果（厳しめ判定）

| # | 指摘 | 判定 | 要点 |
|---|------|------|------|
| 6 | gameModeがマッチ条件にない | **事実・最重要** | `candidates()` に mode 条件なし。探索者は希望modeをサーバに送ってすらいない。joiner は入室先の mode を継承（`useRandomMatch.js` の明示コメント）。画面は「希望ルール」を約束表示しているのに実対戦と食い違う → **ユーザー最優先要件に直接違反** |
| 1 | 2人戦/4人戦が全体16枠を共有 | **事実** | 部屋は帯別（`mm:queue:{size}`）だが、待機上限は `mm:tickets` グローバルで判定。混雑時に片方の帯/modeが他方を締め出す |
| 3 | poll で券TTLは延長、IP制限は延長せず | **事実（実害は限定）** | `poll()` は `mm:ip:*` を触らない。券は生きたままIPエントリだけ先に失効 → 同一回線から上限超えの新券が取れる。正規クライアントでは起きず、多重タブ/悪用向けの穴 |
| 5 | クライアント申告の players/phase を信頼 | **事実（blast radius は限定）** | `readRoom` は形式のみ検証。ただし1券=1部屋、券は上限/IP/レートで頭打ち。より現実的な実害は「peerが落ちたホストの募集が roomTTL の間（最大30秒）残り、探索者が空振りする」 |
| 8 | 4人部屋の分散 | **部分的に事実** | `players>1` で移籍停止するため2〜3人部屋同士は合流しない。単独ホストは oldest へ集約されるので限定的 |
| 2 | waiting.total が実探索者数と不一致 | **事実だが主に表示意味論** | `countWaiting` は全券を数える（ホスト含む）。バグではない |
| 4 | 原子性がなく上限超過しうる | **誇張・要訂正** | pipeline は非原子だが、`ZADD→ZRANK` 方式で「rank<16 を持てるのは最大16人」なので**上限は厳密な上界として守られる**。むしろ起こりうるのは逆の一時的な過剰拒否。上限突破はまず起きない |
| 7 | 「ランダム」でなく古い部屋優先 | **事実だが問題ではない** | oldest-first は集約を早め待ち時間を縮める良い設計。ランダム化は目的（ストレス低減）に逆行。**変更しない** |
| 9 | 16は負荷上限ではない | **事実だが監視の話** | バグではない。将来のオブザーバビリティ課題 |

---

## 3. 意思決定（ユーザー確認事項）

- **希望と違うゲームモードの部屋しか無いとき** → **厳密（希望modeのみ）**。無ければ自分でホストして待つ。ルール順守を優先。
- **ピーク時の同時待機人数** → **50人程度**。

この2点から:

- gameMode 厳密化は必須。
- プールは 2帯 × 2mode = 4。ただし **待機上限を余裕を持って上げれば（50 ≪ 上限）**、別キーへ分割せずとも枠の食い合いは実用上消える。→ **上限 16→64 の単純拡張で対応**（P0-B の簡略版）。

---

## 4. 実装した変更

### P0-A. gameMode を厳密なマッチ条件にする（最優先）

| ファイル | 変更 |
|---|---|
| `api/_match/store.js` | `poll` に `gameMode` 追加。`candidates()` に `entry.gameMode === gameMode` フィルタ。`wantMode = room ? room.gameMode : gameMode`（探索者は希望mode、ホストは自室と同modeの部屋だけを候補に） |
| `api/match/[action].js` | poll で `body.gameMode`（`'0'|'1'`）を検証して store へ渡す |
| `src/network/matchmaking.js` | poll が `gameMode` を送信 |
| `src/network/matchSeeker.js` | `gameMode` を保持・検証し、探索pollで送信 |
| `src/network/useRandomMatch.js` | `start(targetSize, gameMode)` の mode を seeker に伝達 |

→ 剣を選んだ人は剣の部屋にしか入らず、無ければ剣の部屋を建てて待つ。「希望ルール」表示と実対戦が必ず一致。

### P0-B（簡略版）. 待機上限の拡張

- `MATCH_MAX_WAITING` 既定 **16 → 64**。ピーク50 < 64 なので `FULL` 締め出しは実用上起きない。#1 を実用上解消。

### P1. 死に部屋で待たされるストレスの軽減

| ファイル | 変更 |
|---|---|
| `api/match/[action].js` | 部屋募集TTLを券TTLから分離。`MATCH_ROOM_TTL` 既定 **18秒**（券は30秒のまま）。落ちたホストの募集が最大30秒→約18秒で消える |
| `src/network/matchSeeker.js` | `JOIN_TIMEOUT` **10秒 → 6秒**。応答のない部屋を早く見切る |

- `HOST_INTERVAL`=8秒の生存ホストは 18秒TTL 内に必ず再広告するため、生きた部屋が誤消滅しない。

### P2. IP制限TTLの整合（#3 の穴塞ぎ）

| ファイル | 変更 |
|---|---|
| `api/_match/store.js` | `poll` に `ipHash` 追加。券が有効な間、`mm:ip:{hash}` の該当エントリも券と同じ期限へ延命 |
| `api/match/[action].js` | poll に `ipHash` を受け渡し |

- 「券をpollで延命 → IPエントリだけ先に失効 → 同一回線から上限超えで新券」という抜け道を封鎖。正規ユーザー体験に影響なし。

### 見送り（訂正済み）

- **#4 原子性**: 上限は厳密に守られるため対応不要（Lua不要）。
- **#7 ランダム化**: oldest-first を維持（待ち時間短縮に有利）。
- **#9 負荷監視**: 別タスクで将来対応。

---

## 5. テスト

追加したテスト（すべて pass）:

- `match-store.test.js`
  - 「希望と違うルールの部屋は候補に出ない」
  - 「ホストは自分と違うルールの部屋には移らない」
  - 「pollで延命した券はIP上限にも数え続けられ、3枚目は断られる」（fix無しだと失敗する回帰テスト）
- `match-seeker.test.js`
  - 「探索は選んだルールでのみ相手を探す」

結果:

- match 系テスト: **30/30 pass**（store + seeker）。
- 全体: 94テスト中 90 pass / 4 fail。**4failはすべて既存の flaky な `room.test.js`（武器・オートスタートのタイマー系）** で、変更前の baseline でも同様に失敗（`git stash` で確認済み）。本変更とは無関係。
- lint: 変更ファイルはすべてクリーン。既存の `HostRoom.js:63` の lint error は変更前から存在（未着手）。

---

## 6. 本番 env まとめ（調整可能）

```bash
MATCH_MAX_WAITING=64   # 待機上限（16→64、ピーク50想定に余裕）
MATCH_TICKET_TTL=30    # 券の有効期限（秒）
MATCH_ROOM_TTL=18      # 部屋募集の有効期限（秒、新規）
MATCH_PER_IP_MAX=2     # 同一IPの同時待機券数
```

---

## 7. 状態・残タスク

- 変更は `fix/matchlogic` に**未コミット**。
- `src/network/debug.js` は本件と無関係の未追跡ファイル（コミット対象外）。
- 将来: #9 の負荷監視（poll間隔 / 部屋数 / 接続失敗率 / 平均マッチ時間 / API 4xx-5xx）を別タスクで。
