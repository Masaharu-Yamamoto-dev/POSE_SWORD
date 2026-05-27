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