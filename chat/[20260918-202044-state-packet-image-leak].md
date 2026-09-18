# 不具合報告：STATE パケットに剣画像が最大3枚そのまま乗っている

発見日：2026-09-18 / 発見の経緯：1vs3 実装（ステップ1）の検証中に `npm test` で発覚
ステータス：**未修正**（1vs3 とは別件のため手を付けていない）

---

## 1. 症状

`npm test` が 70/71 で、`tests/session.test.js:128` の
**「snapshots exclude images and stale ticks, rematches reinitialize all participants」** が失敗する。

```
AssertionError [ERR_ASSERTION]: The expression evaluated to a falsy value:

  assert.ok(s.links[0][0].sent.filter(m => m.type === 'STATE')
    .every(m => !JSON.stringify(m).includes('imageStr')))

    at tests/session.test.js:133
```

このテストは「ロビーの状態配信（`STATE`）に剣画像を載せない」という設計上の約束を検証している。
現在その約束が破れている。

## 2. 原因

[src/network/RoomSession.js:7-11](../src/network/RoomSession.js#L7-L11)

```js
const withoutImages = room => ({ ...room, players: room.players.map(p => {
  const stats = { ...p.swordData };
  delete stats.imageStr;          // ← 最上位の imageStr しか消していない
  return { ...p, swordData: stats };
}) });
```

`withoutImages` が書かれた当時、`swordData` は平坦な構造で `imageStr` を1つだけ持っていた。

その後、残機モード（コミット `962be7b three play`）で `validateSword` が
**入れ子の `swords` 配列**を返すようになった。

[src/network/HostRoom.js](../src/network/HostRoom.js) `validateSword` の返り値：

```js
{
  name, hp, attack, weight,
  imageStr,              // ← delete される
  hiltType,
  swords: [              // ← ここが手つかずで残る
    { name, hp, attack, weight, imageStr, hiltType, isEmpty },   // 最大3本
    ...
  ],
  equippedIndex
}
```

`delete stats.imageStr` は最上位しか消さないため、
**`swords[].imageStr` が最大3本分そのまま `STATE` パケットに乗り続けている。**

## 3. 影響

| 観点 | 内容 |
|---|---|
| 帯域 | `MAX_IMAGE_LENGTH` は 4MB。最悪ケースで **1プレイヤーあたり最大12MB** が `STATE` に乗る |
| 頻度 | `STATE` はロビーの状態が変わるたびに全員へブロードキャストされる（準備完了の切り替え、入退室、モード変更など）。毎フレームではないが、ロビー操作のたびに発生する |
| 設計意図 | 画像は `ROSTER`（`assetVersion` と `ROSTER_ACK` で受信確認する経路）だけで配るはずだった。`STATE` は軽量な差分配信という前提が崩れている |
| 実害 | 通信状況によってはロビー操作が重くなる。`RoomSession.send()` の `bufferSize` によるバックプレッシャは `SYNC` にしか効かないため、`STATE` は詰まっても送られ続ける |

## 4. 切り分け結果

1vs3 の実装（`MatchRules.cs` / `MatchRulesTests.cs`）を `git stash` で外した状態でも
**同じ1件が失敗する**ことを確認済み。**今回の変更とは無関係の既存不具合**。

```
--- 私の変更を外した状態 ---
ℹ tests 71
ℹ pass 70
ℹ fail 1
```

## 5. 修正案

`withoutImages` を入れ子構造に対応させる。`swords` 配列の各要素からも `imageStr` を落とす。

```js
const stripImages = sword => {
  const stats = { ...sword };
  delete stats.imageStr;
  if (Array.isArray(stats.swords)) stats.swords = stats.swords.map(stripImages);
  return stats;
};
const withoutImages = room => ({ ...room,
  players: room.players.map(p => ({ ...p, swordData: stripImages(p.swordData) })) });
```

### 確認すべき点

ゲスト側は `STATE` を受け取ったとき、前回保持していた画像とマージしている。

[src/network/RoomSession.js](../src/network/RoomSession.js) `receiveAsGuest` の `STATE` 処理：

```js
this.room = { ...m.room, players: m.room.players.map(p => ({ ...p,
  swordData: { ...previous?.players.find(old => old.playerId === p.playerId)?.swordData, ...p.swordData } })) };
```

このマージは **`swordData` の1階層目しか見ていない**。`swords` 配列は
「新しく届いた画像なしの配列」で丸ごと上書きされるため、
`withoutImages` を直すと**ゲスト側で控えの剣の画像が失われる**可能性がある。

したがって修正は2箇所セットで行う必要がある。

1. `withoutImages` で `swords[].imageStr` も落とす
2. ゲスト側のマージを `swords` 配列の要素単位まで行う（`equippedIndex` で対応付けるのではなく、
   配列の添字で突き合わせる）

### 影響を受けるもの

- `MultiplayerManager` の `GetReserveSprites()` / `UpdateReserveSwordIcons()` は
  控えの剣の画像を使う。ここが空にならないことを確認する
- 既存テスト `tests/session.test.js:128` が成功に転じること（テスト側の変更は不要なはず）

## 6. 対応方針（未決）

1vs3 の実装とは独立しているため、以下のいずれかを選ぶ。

- **別ブランチで先に修正する**（`fix/state-image-leak` など）。`feature/1vs3` が
  この不具合を抱えたままマージされるのを避けられる
- **1vs3 の後にまとめて対応する**
- `feature/1vs3` の中で一緒に直す（コミットは分ける）

現時点では**どれも実施していない**。
