using System;
using UnityEngine;

[Serializable] public class MultiplayerPlayerConfig
{
    public string playerId;
    public int slotIndex;
    public int spawnIndex;
    public SwordData swordData;
}
[Serializable] public class MultiplayerConfig
{
    public string matchId;
    public string localPlayerId;
    public bool isHost;
    public string gameMode;
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
}
// ▼【新規追加】分身突進（hiltType:"2"）・リーフシールド（hiltType:"3"）など、
// 本体以外に複数体表示する"付随体"1体分の見た目同期用データ
[Serializable] public class MultiplayerCloneState
{
    public string id;
    public string ownerId;
    public float x, y, rotation;
    public Color color;
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
    public MultiplayerScore[] standings;
}
