using System;
using System.Collections.Generic;
using UnityEngine;

// ▼【新規追加】SYNC(MultiplayerSync)を届いた順にため込み、「displayDelay 秒前」の時点を再生する時計。
// ホストもゲストも同じこれを通して画面を作るので、どちらの画面も同じだけ遅れて同じように滑らかになる。
//
// 以前のゲストは「届いた最新の位置へ毎フレーム寄せる」だけだったため、SYNCの届く間隔の乱れ(ジッター・再送待ち)が
// そのまま剣の速さの乱れになって見えていた。ため込んだ分でその乱れを吸収し、前後2つのSYNCの間を補間して表示する。
//
// 時刻はホストの物理の時刻(MultiplayerSync.time)の目盛りで数える。自分の時計とホストの時計の差(届くまでの時間)を、
// 直近の「一番早く届いたSYNC」から見積もり、「今 - その差 - displayDelay」を再生する。
// 届くたびに目標が飛び飛びに動く方式(最新のSYNCの時刻 - displayDelay)だと、届く間隔の乱れで再生の速さが揺れるので、
// 見積もった差が変わらない限り、再生の時計は実時間と同じ速さで一定に進む。
// ホストでは届くまでの時間がほぼ0なので、ちょうど displayDelay 秒前が表示される。
public class SyncPlayback
{
    // 一番早く届いたSYNCからどれだけ過去を表示するか(秒)。大きいほど通信の乱れに強く、小さいほど遅延が短い
    public float Delay = 0.1f;
    // 目標の時刻へ寄せる速さ(1/秒)。見積もった差が変わった時にだけ働く
    public float Nudge = 2f;
    // 目標からこれ以上ずれていたら、寄せずに飛ぶ(長い途切れの後など)
    public float SnapThreshold = 0.3f;
    // 届くまでの時間の見積もりに使う期間(秒)。この間で一番早く届いたものを基準にする
    public float OffsetWindow = 2f;
    // ▼ 決着後：ため込んだ残りを最後まで通常の速さで再生し切る。再生し切ったら Drained が立つ
    public bool Draining;
    // ▼【新規追加】ゲスト：ホストから届くまでの片道の時間(秒、往復の半分)。この分だけ再生を先へずらして、
    // ホストの画面と同じ瞬間を映す。ただし、ため込みが「送る間隔 + 届き方の揺れ」を下回らない範囲までしかずらさない
    // (下回ると届く前に追いついて止まり、カクつく)。ずらしきれない分だけ、ゲストはホストより遅れて見える
    public float LatencyCompensation;
    // 送る間隔の最大(物理の刻みに合わせて 20ms / 40ms が交互になる)
    public float MaxSendGap = 0.04f;
    public float AppliedCompensation { get; private set; }
    // ▼【新規追加】ゲスト：直近 NeedWindow 秒の届き方から見た「表示が止まらないために必要な遅延」(秒)。
    // ホストへ知らせ、ホストが全員の最大を共通の遅延(Delay)にする。
    // 1件ごとの必要値 = そのSYNCの遅れ(一番早く届いたものとの差) + 前のSYNCからの間隔 + 片道ぶんの補正 + 1コマぶんの余裕。
    // 間隔は MaxSendGap で頭打ちにする。カットインやヒットストップで物理が止まっている間の長い間隔は、剣も止まっていて見た目に出ないため
    public float NeedWindow = 10f;
    public float FrameMargin = 0.02f;
    public float NeededDelay { get; private set; }
    readonly List<(float local, float need)> needs = new List<(float, float)>();
    float lastSyncTime = -1f;

    readonly List<MultiplayerSync> queue = new List<MultiplayerSync>();
    // (届いた自分の時刻, 自分の時刻 - SYNCの時刻)
    readonly List<(float local, float offset)> offsets = new List<(float, float)>();
    int applied;        // queue[0..applied) は適用済み。queue[applied-1] が今の起点
    float renderTime;
    float minOffset, windowJitter;
    bool started;

    // 調査用の数え上げ(MultiplayerManager がまとめてログに出す)
    public int StatReceived, StatStalls;
    public float StatStallSeconds, StatMaxGap, StatMaxJitter;
    float lastArrival = -1f;

    public int Count => queue.Count;
    public MultiplayerSync Latest => queue.Count > 0 ? queue[queue.Count - 1] : null;
    public bool Drained => applied >= queue.Count;
    public float RenderTime => renderTime;

    public void Clear()
    {
        queue.Clear(); offsets.Clear(); applied = 0; renderTime = 0f; minOffset = 0f; windowJitter = 0f; started = false; Draining = false;
        AppliedCompensation = 0f;
        needs.Clear(); NeededDelay = 0f; lastSyncTime = -1f;
        lastArrival = -1f;
        ResetStats();
    }

    public void ResetStats()
    {
        StatReceived = 0; StatStalls = 0; StatStallSeconds = 0f; StatMaxGap = 0f; StatMaxJitter = 0f;
    }

    // localNow：届いた時の自分の Time.unscaledTime
    public void Enqueue(MultiplayerSync sync, float localNow)
    {
        if (sync == null) return;
        // 時刻を持たない古いビルドのホストから届いた時だけ、送信間隔(30回/秒)で時刻を補う
        var last = Latest;
        if (sync.time <= 0f) sync.time = last != null ? last.time + 1f / 30f : 0f;
        if (last != null && sync.time <= last.time) sync.time = last.time + 0.001f;
        queue.Add(sync);

        StatReceived++;
        if (lastArrival >= 0f) StatMaxGap = Mathf.Max(StatMaxGap, localNow - lastArrival);
        lastArrival = localNow;

        // 届くまでの時間の見積もり：期間内で一番小さい差(一番早く届いたもの)を使う
        offsets.Add((localNow, localNow - sync.time));
        offsets.RemoveAll(o => localNow - o.local > OffsetWindow);
        float min = float.MaxValue, max = float.MinValue;
        foreach (var o in offsets) { min = Mathf.Min(min, o.offset); max = Mathf.Max(max, o.offset); }
        minOffset = min;
        windowJitter = max - min;
        StatMaxJitter = Mathf.Max(StatMaxJitter, windowJitter);

        if (lastSyncTime >= 0f)
        {
            float lateness = (localNow - sync.time) - minOffset;
            float gap = Mathf.Min(sync.time - lastSyncTime, MaxSendGap);
            needs.Add((localNow, lateness + gap + LatencyCompensation + FrameMargin));
        }
        lastSyncTime = sync.time;
        needs.RemoveAll(n => localNow - n.local > NeedWindow);
        float need = 0f;
        foreach (var n in needs) need = Mathf.Max(need, n.need);
        NeededDelay = need;
    }

    // 時計を進め、再生時刻に達したSYNCを順に apply へ渡す。
    // 戻り値は位置補間用の前後2つ(from=起点、to=次。to が無ければ起点で止めて待つ)と、その間の割合 t
    public void Advance(float localNow, float dt, Action<MultiplayerSync> apply, out MultiplayerSync from, out MultiplayerSync to, out float t)
    {
        from = null; to = null; t = 0f;
        if (queue.Count == 0) return;
        float latest = Latest.time;
        AppliedCompensation = Mathf.Clamp(LatencyCompensation, 0f, Mathf.Max(0f, Delay - MaxSendGap - windowJitter));
        float target = localNow - minOffset + AppliedCompensation - Delay;
        if (!started)
        {
            renderTime = Mathf.Min(target, latest); started = true;
        }
        else if (Draining)
        {
            // 決着後：寄せずに実時間で進めて、最後(最新)まできっちり再生し切る
            renderTime = Mathf.Min(renderTime + dt, latest);
        }
        else
        {
            renderTime += dt;
            float error = target - renderTime;
            if (Mathf.Abs(error) > SnapThreshold) renderTime = target;
            else renderTime += error * Mathf.Clamp01(Nudge * dt);
            // 未来(まだ届いていない時刻)は見せない。届くまでその場で待つ(=カクつき。数えておく)
            if (renderTime > latest)
            {
                StatStalls++; StatStallSeconds += renderTime - latest;
                renderTime = latest;
            }
        }
        // 最初の1件はため込みを待たずに即座に出す(開始時に剣が無い画面を見せないため)
        if (applied == 0) { apply(queue[0]); applied = 1; }
        while (applied < queue.Count && queue[applied].time <= renderTime) { apply(queue[applied]); applied++; }
        // 起点より前の分は捨てる(起点だけ残す)
        if (applied > 1) { queue.RemoveRange(0, applied - 1); applied = 1; }
        from = queue[0];
        to = queue.Count > 1 ? queue[1] : null;
        if (to != null) t = Mathf.Clamp01((renderTime - from.time) / Mathf.Max(0.0001f, to.time - from.time));
    }
}
