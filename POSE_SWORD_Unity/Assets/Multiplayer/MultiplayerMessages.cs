using System;
using UnityEngine;

[Serializable] public class MultiplayerPlayerConfig
{
    public string playerId;
    public int slotIndex;
    public int spawnIndex;
    public SwordData swordData;
    // ▼【新規追加】1vs3の陣営。0=ボス(1人側) / 1=トリオ(3人側)。
    // soloModeでない試合では全員0で、その場合は陣営を使わず従来どおりの個人戦として扱う
    public int team;
}
// ▼【新規追加】1vs3のボス強化。全クライアントが同じ値を使う必要があるため、
// ホストが決めた値をこの形で配る（Web側 HostRoom.js の SOLO_BUFF が出どころ）
[Serializable] public class MultiplayerSoloBuff
{
    public float hpMultiplier;
    public float attackMultiplier;
    public float spGainMultiplier;
    // 剣の大きさに掛ける。当たり判定も一緒に大きくなる
    public float scaleMultiplier;
    public float maxSp;
    public float suppressRadius;
    // 掌握斬の強さ。動きと時間（吸引・溜め・薙ぎ払いの秒数、拘束時間など）は配らず、
    // MultiplayerManager の Inspector で決める（全員が同じビルドを使うので値は揃う）
    public float judgmentDamageMultiplier;
    public float judgmentKnockback;
}
[Serializable] public class MultiplayerConfig
{
    public string matchId;
    public string localPlayerId;
    public bool isHost;
    public string gameMode;
    // ▼【新規追加】残機モード：剣/独楽どちらとも組み合わせられる独立したON/OFFフラグ。
    // trueの時だけ各playerのswordData.swords[]を持ち替え用のライフとして使う
    public bool livesMode;
    // ▼【新規追加】1vs3モード。trueの時だけ players[].team と soloBuff を使う
    public bool soloMode;
    public MultiplayerSoloBuff soloBuff;
    public MultiplayerPlayerConfig[] players;
}
[Serializable] public class MultiplayerCommand
{
    public string matchId;
    public string playerId;
    public int seq;
    public string action;
    public string direction;
}
[Serializable] public class MultiplayerPlayerState
{
    public string playerId;
    public float x, y, rotation, centerX, centerY, sp, scale;
    public int hp, dashType;
    public bool isDashing;
    public string targetPlayerId;
    // ▼【新規追加】ダメージを伴わない柄迫り合い・壁バウンドなどの演出はHost側のOnCollisionEnter2Dでしか
    // 発生しないため、ゲスト側にも一度きりのVFX/SEとして伝えるための単調増加カウンタ(増えたら1回再生)
    public int clashSeq;
    // ▼【新規追加】このプレイヤーが直近でクリティカル/弱点ヒットを受けたかどうかを、
    // clashSeqと同じ単調増加カウンタ方式でゲストに伝える(増えたらそのSYNC由来のダメージはクリティカル扱い)
    public int critSeq;
    // ▼【新規追加】残機モード：現在使用中の剣のインデックス(0始まり)と残りの剣の本数(現在の剣を含む)。
    // ゲスト側はlifeIndexが増えたことを検知して、そのプレイヤーの持ち替え演出(剣の再生成・復活)を再現する
    public int lifeIndex;
    public int livesRemaining;
    // ▼【新規追加】1vs3：ボスの制圧を受けている残り秒数。0なら通常。ゲストはこの値から
    // 操作不能の表示を出し、自分の必殺技ボタンを隠す
    public float suppressedRemaining;
    // ▼【新規追加】大回転斬の段階（SpinDashStage）。ゲストはこれを見て、跳ぶ・溜める・撃ち出す・終わり際の演出を出す
    public int spinPhase;
    // ▼【新規追加】巨大回転斬の段階（GiantSpinStage。ふらつきは含まない）。ゲストはこれを見て、柄が光る・巨大化・一回転などの演出を出す
    public int giantPhase;
    // ▼【新規追加】オレ達アタックの段階（CloneRushChargeStage）と、残りの発射数。ゲストはこれを見て溜めと残りの数の演出を出す
    public int clonePhase;
    public int cloneShots;
}
// ▼【新規追加】分身突進（hiltType:"2"）・リーフシールド（hiltType:"3"）など、
// 本体以外に複数体表示する"付随体"1体分の見た目同期用データ
[Serializable] public class MultiplayerCloneState
{
    public string id;
    public string ownerId;
    public float x, y, rotation;
    public Color color;
    // ▼【新規追加】残機モード：この分身が持ち主の何番目の剣(0=現在装備中)の形をしているか。
    // -1は「残機モードでない、または特定の剣に紐付かない」ことを表し、持ち主の現在の刀身を使う
    public int spriteIndex = -1;
    // ▼【新規追加】オレ達アタックの分身の段階（CloneRushPhase。オレ達シールドの分身は0）。
    // ゲストは段階が0でない分身にオレ達アタックの演出を付け、0の分身にはオレ達シールドの演出を付ける
    public int phase;
}
// ▼【新規追加】前回のSYNC以降に消えたオレ達アタックの分身と、その消え方（CloneRushEnd）。ゲストが同じ消え方の演出を出す
[Serializable] public class MultiplayerCloneEnd
{
    public string id;
    public int kind;
    public float x, y, angle;
}
// ▼【新規追加】実際に通ったダメージ1件分。ダメージ数字を「与えた／受けた／他人同士」で出し分けるため、
// 攻撃した人と当たった位置も一緒に送る。seq は試合ごとの通し番号で、ゲストは前回より大きいものだけ表示する
[Serializable] public class MultiplayerHitEvent
{
    public int seq;
    public string attackerId;
    public string targetId;
    public int damage;
    public bool crit;
    // この一撃で剣が壊れたか（残機制で次の剣に持ち替わった時も含む）。撃破の数字は剣についていかせない
    public bool lethal;
    // 試合を決めた一撃か。決着の数字と撃破演出の印は消さずに残し、カメラもこれを基に寄る
    public bool final;
    public float x, y;
}
[Serializable] public class MultiplayerSync
{
    public string matchId;
    public int tick;
    public string phase;
    public float countdownRemaining;
    public MultiplayerPlayerState[] players;
    public MultiplayerCloneState[] clones;
    // 前回のSYNC以降に通ったダメージ（Hostが溜めて、SYNCを送るたびに空にする）
    public MultiplayerHitEvent[] hits;
    public MultiplayerCloneEnd[] cloneEnds;
    // 1vs3：ボスが掌握を撃った回数と撃った場所。ゲストは回数が増えたのを見て範囲の輪を出す
    public int judgmentSeq;
    public float judgmentX;
    public float judgmentY;
}
[Serializable] public class MultiplayerScore
{
    public string playerId;
    public int rank, damageDealt, damageTaken, kills, eliminationTick;
    public string eliminationReason;
}
[Serializable] public class MultiplayerResult
{
    public string matchId;
    public string winnerId;
    public bool draw;
    // ▼【新規追加】勝利した陣営(引き分け・未決着は -1)。1vs3では勝者が3人になりうるため
    // winnerId が空になる。その場合はこちらを見ること
    public int winnerTeam;
    public MultiplayerScore[] standings;
}
