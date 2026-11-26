using System;
using System.Collections.Generic;
using UnityEngine;

// ▼【新規追加】SYNC(MultiplayerSync)を、項目名のない「整数の並び」(MultiplayerSyncWire.s)に詰める・戻す。
//
// SYNCは React が一度 JavaScript のデータに戻し、PeerJS がバイナリ(BinaryPack)に詰め直して送る。BinaryPack では
// 項目名が毎回その文字数ぶん入り、小数は桁数に関係なく9バイトになる。一方、小さい整数は1〜3バイトで済む。
// そこで項目名をなくし、小数は決まった倍率を掛けた整数にして送る(受け取ったら同じ倍率で割る)。
// 4人対戦でも1つのパケット(約1.1KB)に収めるのが目的。
//
// React が見る matchId と tick だけは名前付きのまま残す(部屋の確認と、古いSYNCを捨てる判定に使うため)。
// プレイヤーは試合の設定(config.players)の並び順で送り、IDは送らない(両側で同じ設定を持っている)。
public static class SyncCodec
{
    // 並び方を変えたら上げる。違う版のSYNCは読まずに捨てる
    public const int Version = 1;

    // 倍率(受け取る側はこれで割る)
    const float PositionScale = 1000f;   // 位置・重心：0.001単位
    const float RotationScale = 100f;    // 回転：0.01°単位
    const float SizeScale = 1000f;       // 剣の大きさ：0.001単位
    const float SpScale = 10f;           // SP：0.1単位(切り捨て。四捨五入だと、ホストでは撃てないのにゲストでは撃てる見た目になりうる)
    const float SecondsScale = 1000f;    // 時刻・秒数：1ms単位
    const float RatioScale = 1000f;      // 独楽の回転の溜まり具合など(0〜1程度)：0.001単位
    const float ColorScale = 255f;

    static readonly string[] Phases = { "IDLE", "LOADING", "COUNTDOWN", "PLAYING", "RESULT" };
    const string CloneIdPrefix = "clone";

    // 1件あたりの数(並び方を変えたらここと Version も合わせる)
    const int HeaderFields = 8, PlayerFields = 21, CloneFields = 11, HitFields = 7, CloneEndFields = 5;

    public static MultiplayerSyncWire Encode(MultiplayerSync sync, IList<string> playerIds)
    {
        var s = new List<int>(16 + playerIds.Count * PlayerFields);
        s.Add(Version);
        s.Add(Q(sync.time, SecondsScale));
        s.Add(Q(sync.delay, SecondsScale));
        s.Add(Math.Max(0, Array.IndexOf(Phases, sync.phase)));
        s.Add(Q(sync.countdownRemaining, SecondsScale));
        s.Add(sync.judgmentSeq);
        s.Add(Q(sync.judgmentX, PositionScale));
        s.Add(Q(sync.judgmentY, PositionScale));

        // プレイヤー：設定の並び順に、全員ぶん必ず入れる
        s.Add(playerIds.Count);
        foreach (var id in playerIds)
        {
            var p = Find(sync.players, id) ?? new MultiplayerPlayerState();
            s.Add(Q(p.x, PositionScale)); s.Add(Q(p.y, PositionScale));
            s.Add(Q(Mathf.Repeat(p.rotation, 360f), RotationScale));
            s.Add(Q(p.centerX, PositionScale)); s.Add(Q(p.centerY, PositionScale));
            s.Add(p.hp);
            s.Add(Floor(p.sp, SpScale));
            s.Add(Q(p.scale, SizeScale));
            s.Add(p.dashType);
            s.Add(p.isDashing ? 1 : 0);
            s.Add(IndexOf(playerIds, p.targetPlayerId));
            s.Add(p.clashSeq); s.Add(p.critSeq);
            s.Add(p.lifeIndex); s.Add(p.livesRemaining);
            s.Add(Q(p.suppressedRemaining, SecondsScale));
            s.Add(p.spinPhase); s.Add(p.giantPhase); s.Add(p.clonePhase); s.Add(p.cloneShots);
            s.Add(Q(p.komaSpin, RatioScale));
        }

        // 分身：IDは "clone" + 通し番号 なので番号だけ送る。持ち主はプレイヤーの並び順の番号
        var clones = new List<MultiplayerCloneState>();
        if (sync.clones != null)
            foreach (var c in sync.clones)
                if (c != null && CloneNumber(c.id) >= 0 && IndexOf(playerIds, c.ownerId) >= 0) clones.Add(c);
        s.Add(clones.Count);
        foreach (var c in clones)
        {
            s.Add(CloneNumber(c.id)); s.Add(IndexOf(playerIds, c.ownerId));
            s.Add(Q(c.x, PositionScale)); s.Add(Q(c.y, PositionScale));
            s.Add(Q(Mathf.Repeat(c.rotation, 360f), RotationScale));
            s.Add(Q(c.color.r, ColorScale)); s.Add(Q(c.color.g, ColorScale)); s.Add(Q(c.color.b, ColorScale)); s.Add(Q(c.color.a, ColorScale));
            s.Add(c.spriteIndex); s.Add(c.phase);
        }

        // ダメージの記録：真偽は1つの数にまとめる(1=クリティカル、2=撃破、4=決着)
        var hits = sync.hits ?? Array.Empty<MultiplayerHitEvent>();
        int hitCount = 0;
        foreach (var h in hits) if (h != null) hitCount++;
        s.Add(hitCount);
        foreach (var h in hits)
        {
            if (h == null) continue;
            s.Add(h.seq); s.Add(IndexOf(playerIds, h.attackerId)); s.Add(IndexOf(playerIds, h.targetId)); s.Add(h.damage);
            s.Add((h.crit ? 1 : 0) | (h.lethal ? 2 : 0) | (h.final ? 4 : 0));
            s.Add(Q(h.x, PositionScale)); s.Add(Q(h.y, PositionScale));
        }

        // 分身の消え方
        var ends = new List<MultiplayerCloneEnd>();
        if (sync.cloneEnds != null)
            foreach (var e in sync.cloneEnds) if (e != null && CloneNumber(e.id) >= 0) ends.Add(e);
        s.Add(ends.Count);
        foreach (var e in ends)
        {
            s.Add(CloneNumber(e.id)); s.Add(e.kind);
            s.Add(Q(e.x, PositionScale)); s.Add(Q(e.y, PositionScale)); s.Add(Q(e.angle, RotationScale));
        }

        return new MultiplayerSyncWire { matchId = sync.matchId, tick = sync.tick, s = s.ToArray() };
    }

    // 読めない(版が違う・数が足りない・人数が合わない)時は null を返す
    public static MultiplayerSync Decode(MultiplayerSyncWire wire, IList<string> playerIds)
    {
        var s = wire?.s;
        if (s == null || s.Length < HeaderFields + 1 || s[0] != Version) return null;
        int i = 1;
        var sync = new MultiplayerSync
        {
            matchId = wire.matchId, tick = wire.tick,
            time = s[i++] / SecondsScale,
            delay = s[i++] / SecondsScale,
        };
        int phase = s[i++];
        sync.phase = phase >= 0 && phase < Phases.Length ? Phases[phase] : "IDLE";
        sync.countdownRemaining = s[i++] / SecondsScale;
        sync.judgmentSeq = s[i++];
        sync.judgmentX = s[i++] / PositionScale;
        sync.judgmentY = s[i++] / PositionScale;

        int playerCount = s[i++];
        if (playerCount != playerIds.Count || !Fits(s, i, playerCount, PlayerFields)) return null;
        sync.players = new MultiplayerPlayerState[playerCount];
        for (int n = 0; n < playerCount; n++)
        {
            var p = new MultiplayerPlayerState { playerId = playerIds[n] };
            p.x = s[i++] / PositionScale; p.y = s[i++] / PositionScale;
            p.rotation = s[i++] / RotationScale;
            p.centerX = s[i++] / PositionScale; p.centerY = s[i++] / PositionScale;
            p.hp = s[i++];
            p.sp = s[i++] / SpScale;
            p.scale = s[i++] / SizeScale;
            p.dashType = s[i++];
            p.isDashing = s[i++] != 0;
            p.targetPlayerId = IdAt(playerIds, s[i++]);
            p.clashSeq = s[i++]; p.critSeq = s[i++];
            p.lifeIndex = s[i++]; p.livesRemaining = s[i++];
            p.suppressedRemaining = s[i++] / SecondsScale;
            p.spinPhase = s[i++]; p.giantPhase = s[i++]; p.clonePhase = s[i++]; p.cloneShots = s[i++];
            p.komaSpin = s[i++] / RatioScale;
            sync.players[n] = p;
        }

        if (i >= s.Length) return null;
        int cloneCount = s[i++];
        if (!Fits(s, i, cloneCount, CloneFields)) return null;
        sync.clones = new MultiplayerCloneState[cloneCount];
        for (int n = 0; n < cloneCount; n++)
        {
            var c = new MultiplayerCloneState { id = CloneIdPrefix + s[i++], ownerId = IdAt(playerIds, s[i++]) };
            c.x = s[i++] / PositionScale; c.y = s[i++] / PositionScale;
            c.rotation = s[i++] / RotationScale;
            c.color = new Color(s[i++] / ColorScale, s[i++] / ColorScale, s[i++] / ColorScale, s[i++] / ColorScale);
            c.spriteIndex = s[i++]; c.phase = s[i++];
            sync.clones[n] = c;
        }

        if (i >= s.Length) return null;
        int hitCount = s[i++];
        if (!Fits(s, i, hitCount, HitFields)) return null;
        sync.hits = new MultiplayerHitEvent[hitCount];
        for (int n = 0; n < hitCount; n++)
        {
            var h = new MultiplayerHitEvent { seq = s[i++], attackerId = IdAt(playerIds, s[i++]), targetId = IdAt(playerIds, s[i++]), damage = s[i++] };
            int flags = s[i++];
            h.crit = (flags & 1) != 0; h.lethal = (flags & 2) != 0; h.final = (flags & 4) != 0;
            h.x = s[i++] / PositionScale; h.y = s[i++] / PositionScale;
            sync.hits[n] = h;
        }

        if (i >= s.Length) return null;
        int endCount = s[i++];
        if (!Fits(s, i, endCount, CloneEndFields)) return null;
        sync.cloneEnds = new MultiplayerCloneEnd[endCount];
        for (int n = 0; n < endCount; n++)
        {
            var e = new MultiplayerCloneEnd { id = CloneIdPrefix + s[i++], kind = s[i++] };
            e.x = s[i++] / PositionScale; e.y = s[i++] / PositionScale; e.angle = s[i++] / RotationScale;
            sync.cloneEnds[n] = e;
        }
        return sync;
    }

    // ▼ 調査用：PeerJS(BinaryPack)で送る時のおおよその大きさ(バイト)。React が足す項目(type・protocolVersion・roomEpoch)も含める。
    // roomEpoch は matchId の「-」より前と同じなので、そこから見積もる
    public static int EstimatePackedBytes(MultiplayerSyncWire wire, int protocolVersion)
    {
        string matchId = wire.matchId ?? "";
        int dash = matchId.LastIndexOf('-');
        string epoch = dash > 0 ? matchId.Substring(0, dash) : matchId;
        int bytes = 1; // 6項目の入れ物
        bytes += PackedString("matchId") + PackedString(matchId);
        bytes += PackedString("tick") + PackedInt(wire.tick);
        bytes += PackedString("s") + PackedArrayHeader(wire.s?.Length ?? 0);
        if (wire.s != null) foreach (int v in wire.s) bytes += PackedInt(v);
        bytes += PackedString("type") + PackedString("SYNC");
        bytes += PackedString("protocolVersion") + PackedInt(protocolVersion);
        bytes += PackedString("roomEpoch") + PackedString(epoch);
        return bytes;
    }

    static int PackedInt(int n)
    {
        if (n >= -32 && n <= 127) return 1;
        if (n >= -128 && n <= 255) return 2;
        if (n >= -32768 && n <= 65535) return 3;
        return 5;
    }
    static int PackedString(string text) { int length = text.Length; return (length <= 15 ? 1 : length <= 65535 ? 3 : 5) + length; }
    static int PackedArrayHeader(int length) => length <= 15 ? 1 : length <= 65535 ? 3 : 5;

    // 倍率を掛けて整数にする。NaN や極端な値で桁あふれしないよう、整数で表せる範囲に収める
    static int Q(float value, float scale)
    {
        if (float.IsNaN(value)) return 0;
        double scaled = Math.Round((double)value * scale);
        return (int)Math.Max(int.MinValue + 1, Math.Min(int.MaxValue, scaled));
    }
    static int Floor(float value, float scale)
    {
        if (float.IsNaN(value)) return 0;
        double scaled = Math.Floor((double)value * scale + 1e-4);
        return (int)Math.Max(int.MinValue + 1, Math.Min(int.MaxValue, scaled));
    }

    static bool Fits(int[] s, int start, int count, int fields) => count >= 0 && count <= 1000 && start + (long)count * fields <= s.Length;

    static MultiplayerPlayerState Find(MultiplayerPlayerState[] players, string id)
    {
        if (players == null) return null;
        foreach (var p in players) if (p != null && p.playerId == id) return p;
        return null;
    }

    static int IndexOf(IList<string> ids, string id) => id == null ? -1 : ids.IndexOf(id);
    static string IdAt(IList<string> ids, int index) => index >= 0 && index < ids.Count ? ids[index] : null;

    static int CloneNumber(string id)
    {
        if (id == null || !id.StartsWith(CloneIdPrefix, StringComparison.Ordinal)) return -1;
        return int.TryParse(id.Substring(CloneIdPrefix.Length), out int number) && number >= 0 ? number : -1;
    }
}
