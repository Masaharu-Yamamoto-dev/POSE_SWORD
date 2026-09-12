# 4人個人戦：TDD実装状況

更新：2026-09-07

## 今回の実装範囲

[設計計画](FOUR_PLAYER_PLAN.md)に沿って、最初の実装単位としてロビーの状態管理と戦闘の勝敗ルールを追加した。既存の2人用画面・通信・物理処理への組み込みはまだ行っておらず、この変更だけでは4人対戦をプレイできない。

### ロビーの状態管理

`src/network/HostRoom.js`はホストが保持するモデル。PeerJSやReactに依存せず、2人／4人の名簿、接続予約、準備、初期化待ち、入力の本人確認、切断、再戦を扱う。

- 接続完了前に`reserve(connectionId)`で枠を確保し、同時入室で定員を超えない。
- `join`に成功した接続にはホストが人物IDを割り当てる。空き枠を再利用しても人物IDは再利用しない。
- `prepare()`は全員が揃い準備完了した場合だけ許可する。
- 全員の同じ試合の`markLoaded`が揃うと`COUNTDOWN`になる。ホストのカウントダウン完了後に`beginPlaying`を呼ぶ。
- `acceptInput`は受信接続から人物IDを決定し、パケット中の人物IDを使用しない。試合ID・操作名・連番・入力頻度も検証する。
- `removeConnection`は`ABORT`／`FORFEIT`／`LEFT`を返す。実際の中止通知やUnityへの脱落指示は呼び出し側が行う。
- 結果画面から全員が戻るか退出するとロビーへ移行する。新しい試合は新しいIDを使う。

武器検証の初期上限はHP 1～1000、攻撃・重量1～100、名前100文字、画像文字列4Mi文字。画像の形式・復号後サイズ・実際の出力との適合は転送処理実装時に確認する。現在の`validateSword`は画像のデコード検証までは行わない。

### 戦闘ルール

`POSE_SWORD_Unity/Assets/Multiplayer/Core/MatchRules.cs`はUnityエンジンに依存しないC#のルール。

- 2人／4人の生存状態、HP、順位、与被ダメージ、撃破数、脱落理由を管理する。
- ホストかどうかで勝敗の扱いを変えず、最後の1人まで続行する。
- `ResolveStep(tick, hits, forfeits)`へ物理ステップ1回分の攻撃候補をまとめて渡す。
- 同じ攻撃者→対象への複数コライダー接触は最大ダメージ1件に統合する。
- 同時攻撃はステップ開始時の生存状態で評価し、通知順序による相打ち結果の差を防ぐ。
- オーバーキルを除く実ダメージを攻撃者に按分し、撃破者・同順位・引き分けを確定する。
- 切断脱落は他人のダメージ実績・撃破数に加算しない。

人物ID配列の順番が固定のslot順を表す。ステップ番号は試合ごとに単調増加させる。HPの変更はこのルールを正としてUnityの表示へ反映する設計であり、既存の`SwordBattle.TakeDamage`と二重適用しない。

## テストの実行

```sh
npm test
npm run test:rules
```

- JavaScript：Nodeの標準テストランナーで13件。
- C#：Unity EditMode用のNUnitテスト10件。同じテストをUnity付属Monoでも実行する。
- C#の4人同時脱落テスト内では、衝突通知の24通りの順列を確認する。
- 新規JavaScriptとテストに対するESLintは成功した。
- `npm run build`は54モジュールの変換後に長時間進行が確認できず中断した。既存フロントエンド全体のビルド成功は未確認。今回の基盤モジュールはまだ`App.jsx`から使用していない。

`test:rules`は標準でUnity `6000.1.12f1`のmacOSインストールと、プロジェクトのPackageCache内のNUnitを参照する。環境が異なる場合は`UNITY_EDITOR_ROOT`（UnityのContents相当）と`NUNIT_DLL`（nunit.framework.dllの絶対パス）を指定する。

Mono用ランナーは引数なしの`[Test]`メソッドのみを実行し、Unityのシーン・物理演算・ライフサイクルは再現しない。Unityで実行する場合はTest RunnerのEditModeで`PoseSword.MatchRules.Tests`を選択する。

### Red → Green の記録

1. JavaScriptのテストを追加し、`HostRoom.js`未実装による失敗を確認した。
2. ロビー管理を実装し、最初の10件を成功させた。
3. C#のテストを追加し、`MatchRules`未実装のコンパイル失敗を確認した。
4. C#ルールを実装し、最初の7件を成功させた。
5. 結果画面での退出ケースを追加し、`RESULT`のまま再戦できない失敗を確認した。
6. 退出後の状態遷移を修正し、JavaScript全13件を成功させた。
7. 不正入力・整数上限・全衝突順序の検証を追加し、C#全10件を成功させた。

## 2026-09-12 の変更：2〜4人の可変人数に統合

「4人個人戦」専用画面をやめ、タイトルの「ロビーを作成」「ロビーに入る」を唯一の対戦導線にした。部屋は常に4席で、在室している2〜4人がそのまま試合の人数になる。

- `HostRoom`：定員選択（`setCapacity`）を廃止し、`MAX_PLAYERS`（4席）と`MIN_PLAYERS`（2人）に置き換えた。`canStart()`は「在室2人以上・全員が接続中・準備完了・ロビー在室」で成立する。退室・中止のたびに`compactSlots()`でスロットを0..n-1へ詰める（Unityが連番のスロットを要求するため）。
- `RoomSession`：出現位置の抽選を定員ではなく参加人数で行う。武器の持ち替えを部屋へ伝える`SWORD`メッセージと`updateSword()`を追加した。パケット仕様が変わったため`PROTOCOL_VERSION`を3へ上げた。
- Unity：`MatchRules`と`MultiplayerManager.ValidateConfig`が2〜4人を受け付ける。`SpawnPosition`は人数に応じて配置する（剣モードは横一列に等間隔、独楽モードの3人は三角配置）。
- 画面：`src/screens/LobbyScreen.jsx`が実際の名簿を表示し（自分＋他3席、空き枠は「参加を待っています」）、ホストの「◯人で対戦開始」ボタンを持つ。対戦画面は`src/components/BattleArena.jsx`、結果は`src/screens/ResultScreen.jsx`の順位表に統合した。`src/components/MultiplayerGame.jsx`は削除。
- 旧2人専用の通信（`App.jsx`のPeerJS直結）と`ver2.10`ビルドの対戦導線は使わなくなった。

WebGLビルド（`public/multiplayer/`）はこのリポジトリに未生成のため、Unity Editorの「POSE SWORD/Build four-player WebGL」で作成する必要がある。

## 残りの実装

1. PeerJSの接続管理を`HostRoom`に接続し、3接続の配布・ACK・期限・ハートビートを実装する。
2. ロビーを参加者一覧へ置換し、4人部屋・全員の準備・結果と観戦を表示する。
3. Unityの剣・HUDの生成、入力先、ターゲット、カメラを人物IDに対応させる。
4. 物理ステップ後の攻撃集約を実装し、`MatchRules`の結果をHP・脱落・終了演出へ反映する。
5. 人数配列のSYNCと試合IDをReact–Unity間で共有し、再戦時に初期化する。
6. WebGLを再ビルドし、2〜4台で両モード・切断・性能を検証する。

## 実行環境の制約

Unity EditorでのEditModeテストを試行したが、`No valid Unity Editor license found. Please activate your license.`で起動できなかった。C#のルールテストはMonoで実行できたが、Editorコンパイル・シーン実行・WebGLビルドは未検証。Unityでの検証を再開するには、この端末のEditorライセンスを有効にする必要がある。
