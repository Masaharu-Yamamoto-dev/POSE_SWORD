using System;
using System.Collections.Generic;
using UnityEngine;

// ▼【新規追加】SYNC(MultiplayerSync)を届いた順にため込み、「一番新しいSYNCより displayDelay 秒だけ過去」の時点を
// 再生する時計。ホストもゲストも同じこれを通して画面を作るので、どちらの画面も同じだけ遅れて同じように滑らかになる。
//
// 以前のゲストは「届いた最新の位置へ毎フレーム寄せる」だけだったため、SYNCの届く間隔の乱れ(ジッター・再送待ち)が
// そのまま剣の速さの乱れになって見えていた。ため込んだ分でその乱れを吸収し、前後2つのSYNCの間を補間して表示する。
//
// 時計(renderTime)はホストの Time.unscaledTime の目盛り(MultiplayerSync.time)で進める。ゲストは自分の時計と
// ホストの時計の差を知らなくてよく、「最新のSYNCの時刻 - displayDelay」を目標に、実時間で進めつつゆるやかに寄せる。
// ホストでは最新のSYNCが常に「今」なので、ちょうど displayDelay 秒前が表示される。
public class SyncPlayback
{
    // 一番新しいSYNCからどれだけ過去を表示するか(秒)。大きいほど通信の乱れに強く、小さいほど遅延が短い
    public float Delay = 0.1f;
    // 目標の時刻へ寄せる速さ(1/秒)。大きいと通信の乱れがそのまま速さの乱れとして見え、小さいと遅延が目標からずれたまま長く残る
    public float Nudge = 4f;
    // 目標からこれ以上ずれていたら、寄せずに飛ぶ(長い途切れの後など)
    public float SnapThreshold = 0.5f;
    // ▼ 決着後：ため込んだ残りを最後まで通常の速さで再生し切る(目標を「最新」にする)。再生し切ったら Drained が立つ
    public bool Draining;

    readonly List<MultiplayerSync> queue = new List<MultiplayerSync>();
    int applied;        // queue[0..applied) は適用済み。queue[applied-1] が今の起点
    float renderTime;
    bool started;

    public int Count => queue.Count;
    public MultiplayerSync Latest => queue.Count > 0 ? queue[queue.Count - 1] : null;
    public bool Drained => applied >= queue.Count;
    public float RenderTime => renderTime;

    public void Clear()
    {
        queue.Clear(); applied = 0; renderTime = 0f; started = false; Draining = false;
    }

    public void Enqueue(MultiplayerSync sync)
    {
        if (sync == null) return;
        // 時刻を持たない古いビルドのホストから届いた時だけ、送信間隔(30回/秒)で時刻を補う
        var last = Latest;
        if (sync.time <= 0f) sync.time = last != null ? last.time + 1f / 30f : 0f;
        // 届く順序は保証されている(reliable)が、念のため時刻が戻ったものは起点の直後に差し込まず捨てる
        if (last != null && sync.time <= last.time) sync.time = last.time + 0.001f;
        queue.Add(sync);
    }

    // 時計を dt(実時間)だけ進め、再生時刻に達したSYNCを順に apply へ渡す。
    // 戻り値は位置補間用の前後2つ(from=起点、to=次。to が無ければ起点で止めて待つ)と、その間の割合 t
    public void Advance(float dt, Action<MultiplayerSync> apply, out MultiplayerSync from, out MultiplayerSync to, out float t)
    {
        from = null; to = null; t = 0f;
        if (queue.Count == 0) return;
        float latest = Latest.time;
        float target = latest - Delay;
        if (!started)
        {
            renderTime = target; started = true;
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
            // 未来(まだ届いていない時刻)は見せない。届くまでその場で待つ
            if (renderTime > latest) renderTime = latest;
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
