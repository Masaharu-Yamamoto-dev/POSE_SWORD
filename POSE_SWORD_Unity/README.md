# ⚔️ 2D Physics Sword Battle (Unity WebGL)

React等のWebフロントエンドと連携して動作する、物理演算ベースの2D剣戟対戦ゲームのUnityプロジェクトです。
Web側から渡されたJSONパラメータをもとに剣を錬成し、P2P通信またはローカルでの対戦を行います。

## ✨ 主な機能と演出 (Features)

* **物理演算バトルシステム**
  * 剣の衝突速度（Relative Velocity）に応じた動的ダメージ計算
  * 刃先（指定角度）によるクリティカルヒット判定（ダメージ3倍）
  * 柄への攻撃による弱点判定（ダメージ2倍）
* **ドラマチックな決着演出**
  * 打撃時のヒットストップ＆カメラシェイク演出
  * HP0（決着）時の強制スローモーション＆ロングエコー（残響）演出
  * 試合終了フラグ（`matchEnded`）による厳密な時間制御（相打ち時のバグ防止）
* **リッチなUI (ユーザーインターフェース)**
  * TextMeshPro（Noto Sans JP等）による高品質な日本語フォント描画
  * ダメージ時に遅れて追従する「遅延HPバー（赤ゲージ）」システム
  * 角丸を排除したシャープな専用フレームUI
* **動的サウンドシステム (AudioMixer)**
  * 通常時とクリティカル時のSE切り替え
  * ゲームのタイムスケールに連動したピッチ低下とリバーブ（エコー）の自動適用

---

## 🌐 Webフロントエンドとの連携仕様 (Integration Guide)

Web側からUnity（WebGL）へデータを渡し、ゲームを制御するための仕様です。

### 1. 剣の生成パラメータ（JSON仕様）
Web側から以下のJSONデータを `SwordGenerator` に渡すことで剣を錬成します。
Web側のUI（1〜100）とUnityの物理エンジン用数値を分離し、自動でマッピング変換しています。

```json
{
  "name": "エクスカリバー",
  "hp": 1000,
  "attack": 100,
  "weight": 100
}
```
* **`hp` (100 〜 1000)**: そのままHPとして適用されます。
* **`attack` (1 〜 100)**: Unity内で `10 〜 100` の物理ダメージ倍率に自動変換されます。
* **`weight` (1 〜 100)**: Unity内で `5.0 〜 30.0` のRigidbody質量（mass）に自動変換されます。

### 2. Unityへの関数呼び出し (SendMessage)
Web側（JavaScript/React）からUnityの関数を実行する際のコマンドです。

**音量調整** (0.0がミュート、1.0が最大音量)
```javascript
window.unityInstance.SendMessage('GameManager', 'SetVolume', 0.5);
```

**試合のリセット** (もう一度遊ぶ)
```javascript
window.unityInstance.SendMessage('NetworkManager', 'ResetMatch', '');
```

---

## 🎮 Unity単体での4人対戦（エディタ確認用）

React・PeerJSを立ち上げなくても、Unityの **Play ボタンだけ**で4人個人戦を動かして確認できます。
`Assets/Scenes/SampleScene.unity` の `GameManager` に付いている `LocalFourPlayerDriver` がその役目を持ちます。

### 使い方

1. `SampleScene` を開いて Play を押す。
2. 4本の剣が生成され、3秒のカウントダウンのあと最後の1人になるまで戦います。
3. 画面上部のHUDに、各プレイヤーのHP・SP・状態・狙っている相手が出ます。決着すると順位表が出ます。

### 操作

| | 左 | 右 |
|---|---|---|
| P1 | `A` / 画面左半分をクリック | `D` / 画面右半分をクリック |
| P2 | `←` | `→` |
| P3 | `J` | `L` |
| P4 | `Numpad4` または `N` | `Numpad6` または `M` |

| キー | 動作 |
|---|---|
| `R` | 再戦（新しい試合IDで作り直す） |
| `2` / `4` | 2人戦・4人戦の切り替え（その場で再開） |
| `K` | 剣モード / 独楽モードの切り替え（その場で再開） |
| `B` | BOTのON/OFF（既定はOFF） |
| `H` | HUDのON/OFF |

BOTは既定でOFFです。何も操作しなければ、剣は元どおりその場に立ったまま動きません（自動で回転もしません）。
`B` を押すとBOTが有効になり、3秒間キー入力が無いプレイヤーを自動で動かすので、4人が戦う様子を最後まで観察できます。

> **注意：** 剣モードでは1回の入力ごとに `spinTorque`（-500）の角力積が掛かります。BOTをONにすると入力が連射されるため、
> `angularDamping` が 0.05 と低い剣は回転が溜まって回り続けます。これは物理設定ではなく入力の頻度によるものなので、
> 落ち着いた挙動で見たい場合は BOT をOFFのまま手動で操作するか、Inspectorの `botInterval` を大きくしてください。

### 使用しているアセット

刃には `Assets/Sword` にある既存の切り抜き画像をそのまま使います。画像を新しく作ったりはしません。

| | 刃の画像 | 元サイズ | ワールド上の刃 |
|---|---|---|---|
| P1 / P3 | `sampleA.png` | 291×438 | 3.00 × 4.52 unit |
| P2 / P4 | `sampleB.png` | 179×267 | 3.00 × 4.47 unit |

柄は剣のテンプレート（`PlayerSword` の子 `Handle-A`）に `sword_handle.png` が付いており、
`MultiplayerManager` がテンプレートを複製する際に4本とも自動で引き継がれます。

画像の取り込み設定が `isReadable: 0` のため `EncodeToPNG` は使えません。ドライバは元のPNGファイルを直接読んで
base64化し、本番でReactが送ってくるのと同じ形式で `SwordGenerator` に渡しています（エディタ専用処理）。
当たり判定も本番と同じく、この切り抜きのアルファから `PolygonCollider2D` が生成されます。

Inspectorの `刃に使う切り抜き画像` を差し替えれば別の画像でも試せます。
`sampleC.png` は 457×177 で被写体が右端に寄った未整形のカットのため、既定では使っていません
（刃が 3.0 × 1.16 unit と極端に平たくなり、当たり判定も中心からずれます）。使いたい場合はInspectorから割り当ててください。

`剣データ` に `SwordData` 形式のJSON（`Assets/Sword/dummy_sword.json` など）を入れると、そちらが優先され、
ステータスも含めて実際の試合データと同じ形で試せます。人数・モード・HP・BOTの速さもInspectorから変更できます。

### 仕組み

`LocalFourPlayerDriver` はゲームロジックを持たず、**本番でReactが担当している通信部分だけ**をローカルに置き換えたものです。
名簿の作成 → `InitializeMultiplayer` → `MP_INITIALIZED` を受けて `BeginMultiplayer` → 入力を `ReceiveMultiplayerInput` に中継 → `MP_SYNC` / `MP_RESULT` を受信、という本番と同じ手順をそのまま通ります。
判定・HP・順位は従来どおり `MatchRules.cs` が正で、`MultiplayerManager.cs` にはエディタ用の分岐を一切足していません。

`autoStart` のチェックを外すと、従来の2人用デバッグ動作（`SceneController` の `debugBattleJsonFile` と `T` キー）に戻ります。

### テスト

```sh
python3 ../scripts/check-unity-scripts.py   # 全スクリプトの型チェック
bash ../scripts/test-match-rules.sh         # 勝敗ルールのEditModeテスト
```

シーンを実際に動かす確認は、Unityの Test Runner の **PlayMode** で `PoseSword.Multiplayer.PlayMode.Tests` を実行してください。
`LocalFourPlayerDriverTests` が、剣4本の生成・カウントダウン通過・BOTだけで決着まで進むことを確認します。

---

## 🛠️ ビルド設定 (WebGL Build Settings)

本番環境（Webサーバー上）でのエラーや通信ズレを防ぐため、WebGLビルド時に以下の設定を必ず適用しています。
また、既に出力したものは\POSE_SWORD_Unity\Buildsに保存されています

* **Run In Background**: `ON`
  * *理由:* プレイヤーが別のタブを開いた際にゲームの時間が止まり、通信対戦がズレるのを防ぐため。
* **Compression Format**: `Disabled`
  * *理由:* GzipやBrotli圧縮による、Webサーバー側の解凍設定ミストラブル（ロードで止まるエラー）を完全に防ぐため。

---

## 📂 フォルダ構成と主要スクリプト (Scripts)

* `SwordBattle.cs`: ダメージ計算、ヒットストップ、決着時の演出制御を担うコアスクリプト。
* `SwordGenerator.cs`: JSONを受け取り、ステータスを物理演算パラメータに変換・適用するスクリプト。
* `AudioManager.cs`: AudioMixerを制御し、マスター音量やスローモーション時のエコー・ピッチを管理するスクリプト。
* `NetworkManager.cs`: 試合の同期状態や、再戦時のシーンリセット処理を管理するスクリプト。

## ⚠️ 開発時の注意点
* UIのフォントを追加・変更する場合は、必ず TextMeshPro の `Font Asset` を作成し、Generation Settings を `Dynamic` に設定してください。
* オーディオミキサーの構成を変更する際は、Unityの再生モードを停止してから行ってください。