// ▼【調査用】SYNC が止まって見える原因の切り分け。
// 今の通信路は「確実に・順番どおり」なので、途中の1個が落ちると、再送が届くまで後ろの SYNC も渡されない。
// それが止まりの原因なのか、Wi-Fi 自体が止まっているのかを確かめるため、同じ接続の中に
// 「順番を守らない・再送しない」別ルートを1本足し、SYNC を送るたびに番号と時刻だけの小さな印(12バイト)を流す。
// ゲストは両方の届き方を比べ、ホストへ知らせる。ホストのコンソールに [SyncProbe] として出る。
//   今の通信路だけが止まり、その間も別ルートは届いている → 再送待ち(別ルートに SYNC を移せば直る)
//   両方とも同じ時に止まっている                        → 回線そのものが止まっている(別ルートでは直らない)
// 1回目の確認で後者だった(2026-11-25)。次の確認として、ゲストからも別ルートで 20ms ごとに小さな印(4バイト)を送る。
// スマホの Wi-Fi は自分から送らないでいると電波を休ませ、親機がためた分を約100msごとにまとめて受け取ることがある。
// 送り続けて止まりが減るなら、それが原因(そのまま対策にもなる)。ホストは上り(ゲストからホスト)の届き方も測って出す。
// 確かめ終わったら SYNC_PROBE_ENABLED を false にする。
export const SYNC_PROBE_ENABLED = true;
// ゲストから印を送り続けるか。false にすると1回目と同じ(比べるため)
export const PROBE_KEEPALIVE = true;
export const PROBE_KEEPALIVE_MS = 20;
// 両側が同じ番号で作る(negotiated)ので、つなぎ直しの手続きはいらない。PeerJS 自身の通信路は 0 か 1 を使う
export const SYNC_PROBE_CHANNEL_ID = 100;
// ゲストがまとめてホストへ知らせる間隔(ms)
export const PROBE_REPORT_MS = 5000;
// 届いた間隔が、送った間隔よりこれ以上長かったら「待たされた」として数える(ms。どちらも20msごとに送っている)。
// カットインやヒットストップでホストが送っていない間は、送った間隔も長いので数えない
export const PROBE_STALL_MS = 50;
// 上りの印がこれ以上空いたら、試合の切れ目とみなして比べない
const UP_RESTART_MS = 5000;
const MAX_STALLS = 8;
const KEEP_MS = 3000;

export class SyncProbe {
  // setInterval をそのまま持たせて this.setTimer() と呼ぶと、ブラウザでは Illegal invocation になる。包んで渡す
  constructor(now, { setTimer = (fn, ms) => setInterval(fn, ms), clearTimer = id => clearInterval(id) } = {}) {
    this.now = now;
    this.setTimer = setTimer; this.clearTimer = clearTimer;
    this.channel = null;
    this.seq = 0;
    this.upSeq = 0;
    this.timer = null;
    // 別ルートで届いた印(tick → 自分の時刻)。今の通信路で同じ tick が届いた時に、どれだけ遅れたかを見る
    this.probeAt = new Map();
    // 別ルートで届いた印の並び({ t, tick })。今の通信路が止まっている間に、その先の印が別ルートで何個届いたかを数える
    this.probeTimes = [];
    this.lastUp = null; this.maxUpSeq = -1;
    this.reset();
    this.resetUp();
    this.idle();
  }

  // 試合の外：前の試合の届いた時刻と比べないよう、間隔の起点を忘れる
  idle() {
    this.lastReliable = null; this.lastReliableHost = null; this.lastReliableTick = null;
    this.lastProbe = null; this.lastProbeHost = null;
    this.maxSeq = -1;
    this.awaitLag = null;
  }

  reset() {
    this.windowStart = this.now();
    this.rel = { count: 0, maxGap: 0, stalls: 0 };
    this.probe = { count: 0, maxGap: 0, lost: 0, late: 0, stalls: 0 };
    this.maxLag = 0;
    this.stalls = [];
    this.skipped = 0;
  }

  resetUp() { this.up = { count: 0, maxGap: 0, lost: 0, stalls: 0 }; }

  // PeerJS の DataConnection がつながったら、同じ RTCPeerConnection の中に別ルートを作る
  attach(conn) {
    const start = () => {
      const pc = conn.peerConnection;
      if (!pc || this.channel || typeof pc.createDataChannel !== 'function') return;
      try {
        this.use(pc.createDataChannel('sync-probe', { negotiated: true, id: SYNC_PROBE_CHANNEL_ID, ordered: false, maxRetransmits: 0 }));
      } catch { this.channel = null; }
    };
    if (conn.open) start(); else conn.on('open', start);
  }

  use(channel) {
    this.channel = channel;
    channel.binaryType = 'arraybuffer';
    channel.onmessage = e => {
      if (!(e.data instanceof ArrayBuffer)) return;
      if (e.data.byteLength === 4) this.upArrived(new Int32Array(e.data)[0]);
      else this.received(e.data);
    };
  }

  get open() { return this.channel?.readyState === 'open'; }

  send(values) {
    if (!this.open || this.channel.bufferedAmount > 4096) return;
    try { this.channel.send(new Int32Array(values).buffer); } catch { /* 測るだけなので無視 */ }
  }

  // ホスト：SYNC を送るたびに呼ぶ。hostMs は SYNC に入っているホストの時刻(ms)。
  // 詰まっている時は足さない(そこで止まると測りたいものが混ざる)
  sendTick(tick, hostMs) { if (this.open) this.send([++this.seq, tick, hostMs | 0]); }

  // ゲスト：試合中だけ、20ms ごとに上りの印を送る
  keepAlive(active) {
    if (active && PROBE_KEEPALIVE && this.open) {
      this.timer ??= this.setTimer(() => this.send([++this.upSeq]), PROBE_KEEPALIVE_MS);
    } else if (this.timer != null) { this.clearTimer(this.timer); this.timer = null; }
  }

  // ホスト：ゲストからの上りの印が届いた
  upArrived(seq) {
    const t = this.now();
    if (this.lastUp != null && (t - this.lastUp > UP_RESTART_MS || seq <= 1)) { this.lastUp = null; this.maxUpSeq = -1; }
    this.up.count++;
    if (this.maxUpSeq >= 0 && seq <= this.maxUpSeq) { this.up.lost = Math.max(0, this.up.lost - 1); return; }
    if (this.maxUpSeq >= 0) this.up.lost += seq - this.maxUpSeq - 1;
    this.maxUpSeq = seq;
    if (this.lastUp != null) {
      const gap = t - this.lastUp;
      this.up.maxGap = Math.max(this.up.maxGap, gap);
      if (gap - PROBE_KEEPALIVE_MS >= PROBE_STALL_MS) this.up.stalls++;
    }
    this.lastUp = t;
  }

  // ホスト：前回からの上りのまとめを返して数え直す
  takeUp() {
    const up = { ...this.up, maxGap: Math.round(this.up.maxGap) };
    this.resetUp();
    return up;
  }

  received(data) {
    if (data.byteLength < 12) return;
    const [seq, tick, hostMs] = new Int32Array(data, 0, 3);
    const t = this.now();
    this.probe.count++;
    if (this.maxSeq >= 0 && seq <= this.maxSeq) { this.probe.late++; this.probe.lost = Math.max(0, this.probe.lost - 1); }
    else {
      if (this.maxSeq >= 0) this.probe.lost += seq - this.maxSeq - 1;
      this.maxSeq = seq;
      if (this.lastProbe != null) {
        const gap = t - this.lastProbe;
        this.probe.maxGap = Math.max(this.probe.maxGap, gap);
        if (gap - Math.max(0, hostMs - this.lastProbeHost) >= PROBE_STALL_MS) this.probe.stalls++;
      }
      this.lastProbe = t; this.lastProbeHost = hostMs;
    }
    // 同じ SYNC は今の通信路の方が先に処理される(同じ固まりで届き、先に送っている)。
    // 止まりを終えた SYNC の印が後から来たら、今の通信路は遅れていなかった(0ms)
    if (this.awaitLag?.tick === tick) { this.awaitLag.entry.lag = 0; this.awaitLag = null; }
    this.probeAt.set(tick, t);
    this.probeTimes.push({ t, tick });
    this.prune(t);
  }

  // ゲスト：今の通信路で SYNC が届いた時に呼ぶ。hostMs は SYNC に入っているホストの時刻(ms)
  reliableArrived(tick, hostMs) {
    const t = this.now();
    this.rel.count++;
    const seen = this.probeAt.get(tick);
    const lag = seen == null ? null : t - seen;
    if (lag != null) this.maxLag = Math.max(this.maxLag, lag);
    if (this.lastReliable != null) {
      const gap = t - this.lastReliable;
      this.rel.maxGap = Math.max(this.rel.maxGap, gap);
      const sent = Number.isFinite(hostMs) && this.lastReliableHost != null ? Math.max(0, hostMs - this.lastReliableHost) : 0;
      if (gap - sent >= PROBE_STALL_MS) {
        this.rel.stalls++;
        if (this.stalls.length < MAX_STALLS) {
          const from = this.lastReliable, fromTick = this.lastReliableTick ?? -Infinity;
          // 止まっている間に別ルートで届いた、その先の SYNC の印の数(止まる直前の SYNC の印は数えない)
          const during = this.probeTimes.filter(p => p.t > from && p.t < t && p.tick > fromTick).length;
          const entry = { gap: Math.round(gap), sent: Math.round(sent), during, lag: lag == null ? null : Math.round(lag) };
          this.stalls.push(entry);
          if (lag == null) this.awaitLag = { tick, entry };
        }
      }
    }
    this.lastReliable = t; this.lastReliableTick = tick;
    this.lastReliableHost = Number.isFinite(hostMs) ? hostMs : null;
    this.prune(t);
  }

  prune(t) {
    while (this.probeTimes.length && t - this.probeTimes[0].t > KEEP_MS) this.probeTimes.shift();
    for (const [tick, at] of this.probeAt) { if (t - at > KEEP_MS) this.probeAt.delete(tick); else break; }
  }

  // ゲスト：PROBE_REPORT_MS ごとにまとめを返して数え直す。まだその時でなければ null
  report() {
    const t = this.now();
    if (t - this.windowStart < PROBE_REPORT_MS) return null;
    const r = { seconds: Math.round((t - this.windowStart) / 100) / 10, open: this.open, keepAlive: this.timer != null,
      relCount: this.rel.count, relMaxGap: Math.round(this.rel.maxGap), relStalls: this.rel.stalls,
      probeCount: this.probe.count, probeMaxGap: Math.round(this.probe.maxGap),
      probeLost: this.probe.lost, probeLate: this.probe.late, probeStalls: this.probe.stalls,
      maxLag: Math.round(this.maxLag), stalls: this.stalls };
    this.reset();
    return r;
  }

  close() {
    this.keepAlive(false);
    try { this.channel?.close(); } catch { /* 閉じるだけ */ }
    this.channel = null;
  }
}

const num = (v, max) => (Number.isFinite(v) && v >= 0 && v <= max ? Math.round(v) : 0);

// コンソールに出す1件分の文。r はゲストから届いた値なので、数字以外は捨てる。
// skipped と up はホストだけが持っている(ゲスト自身のコンソールでは null)。
// 文字列の字は画面のフォントの絞った版(tests/font-subset.test.js)に数えられるので、すでに入っている字とかなだけで書く
export function formatProbeReport(playerId, r, skipped = null, up = null) {
  if (!r.open && !num(r.probeCount, 1e6)) return `[SyncProbe] ${playerId} 別ルートが開いていません`;
  let head = `[SyncProbe] ${playerId} ${num(r.seconds, 600)}秒 ゲストから送り続け=${r.keepAlive === true ? 'あり' : 'なし'}: ` +
    `今のルート 届いた${num(r.relCount, 1e6)} 一番空いた間${num(r.relMaxGap, 6e4)}ms 待たされた${num(r.relStalls, 1e6)}回 / ` +
    `別ルート 届いた${num(r.probeCount, 1e6)} 失った${num(r.probeLost, 1e6)} 順番が前後${num(r.probeLate, 1e6)} ` +
    `一番空いた間${num(r.probeMaxGap, 6e4)}ms 待たされた${num(r.probeStalls, 1e6)}回 / ` +
    `同じSYNCが今のルートでおくれた最大${num(r.maxLag, 6e4)}ms`;
  if (skipped != null) head += ` / ホストが送らなかったSYNC ${skipped}`;
  if (up) head += ` / 上り(ゲストからホスト) 届いた${up.count} 失った${up.lost} 一番空いた間${up.maxGap}ms 待たされた${up.stalls}回`;
  const lines = [head];
  for (const s of Array.isArray(r.stalls) ? r.stalls.slice(0, MAX_STALLS) : []) {
    const lag = Number.isFinite(s?.lag) ? `${num(s.lag, 6e4)}ms` : 'わからない(別ルートで失った)';
    lines.push(`    今のルートが${num(s?.gap, 6e4)}ms空いた(ホストが送った間は${num(s?.sent, 6e4)}ms): ` +
      `その間に別ルートで届いた先のSYNC ${num(s?.during, 1e4)} / 空きを終えたSYNCが別ルートよりおくれた分 ${lag}`);
  }
  return lines.join('\n');
}
