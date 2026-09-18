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
    public float maxSp;
    public float suppressRadius;
    public float suppressDuration;
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
}
[Serializable] public class MultiplayerSync
{
    public string matchId;
    public int tick;
    public string phase;
    public float countdownRemaining;
    public MultiplayerPlayerState[] players;
    public MultiplayerCloneState[] clones;
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
