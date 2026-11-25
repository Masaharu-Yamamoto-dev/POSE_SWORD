// ▼【調査用】SYNC が止まって見える原因の切り分け。
// 今の通信路は「確実に・順番どおり」なので、途中の1個が落ちると、再送が届くまで後ろの SYNC も渡されない。
// それが止まりの原因なのか、Wi-Fi 自体が止まっているのかを確かめるため、同じ接続の中に
// 「順番を守らない・再送しない」別ルートを1本足し、SYNC を送るたびに番号と時刻だけの小さな印(12バイト)を流す。
// ゲストは両方の届き方を比べ、ホストへ知らせる。ホストのコンソールに [SyncProbe] として出る。
//   今の通信路だけが止まり、その間も別ルートは届いている → 再送待ち(別ルートに SYNC を移せば直る)
//   両方とも同じ時に止まっている                        → 回線そのものが止まっている(別ルートでは直らない)
// 確かめ終わったら SYNC_PROBE_ENABLED を false にする(または別ルートを SYNC 用にする)。
export const SYNC_PROBE_ENABLED = true;
// 両側が同じ番号で作る(negotiated)ので、つなぎ直しの手続きはいらない。PeerJS 自身の通信路は 0 か 1 を使う
export const SYNC_PROBE_CHANNEL_ID = 100;
// ゲストがまとめてホストへ知らせる間隔(ms)
export const PROBE_REPORT_MS = 5000;
// 届いた間隔が、ホストが送った間隔よりこれ以上長かったら「止まり」として数える(ms。送っているのは20msごと)。
// カットインやヒットストップでホストが送っていない間は、送った間隔も長いので止まりに入らない
export const PROBE_STALL_MS = 50;
const MAX_STALLS = 8;
const KEEP_MS = 3000;

export class SyncProbe {
  constructor(now) {
    this.now = now;
    this.channel = null;
    this.seq = 0;
    this.reset();
    this.idle();
    // 別ルートで届いた時刻(tick → 自分の時刻)。今の通信路で同じ tick が届いた時に、どれだけ遅れたかを見る
    this.probeAt = new Map();
    // 別ルートで届いた時刻の並び。今の通信路が止まっている間に別ルートで何個届いたかを数える
    this.probeTimes = [];
  }

  // 試合の外：前の試合の届いた時刻と比べないよう、間隔の起点を忘れる
  idle() {
    this.lastReliable = null; this.lastReliableHost = null;
    this.lastProbe = null; this.lastProbeHost = null;
    this.maxSeq = -1;
  }

  reset() {
    this.windowStart = this.now();
    this.rel = { count: 0, maxGap: 0, stalls: 0 };
    this.probe = { count: 0, maxGap: 0, lost: 0, late: 0, stalls: 0 };
    this.maxLag = 0;
    this.stalls = [];
    this.skipped = 0;
  }

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
    channel.onmessage = e => this.received(e.data);
  }

  get open() { return this.channel?.readyState === 'open'; }

  // ホスト：SYNC を送るたびに呼ぶ。hostMs は SYNC に入っているホストの時刻(ms)。
  // 詰まっている時は足さない(そこで止まると測りたいものが混ざる)
  sendTick(tick, hostMs) {
    if (!this.open || this.channel.bufferedAmount > 4096) return;
    try { this.channel.send(new Int32Array([++this.seq, tick, hostMs | 0]).buffer); } catch { /* 測るだけなので無視 */ }
  }

  received(data) {
    if (!(data instanceof ArrayBuffer) || data.byteLength < 12) return;
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
    this.probeAt.set(tick, t);
    this.probeTimes.push(t);
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
          const from = this.lastReliable;
          const during = this.probeTimes.filter(p => p > from && p < t).length;
          this.stalls.push({ gap: Math.round(gap), sent: Math.round(sent), during, lag: lag == null ? null : Math.round(lag) });
        }
      }
    }
    this.lastReliable = t; this.lastReliableHost = Number.isFinite(hostMs) ? hostMs : null;
    this.prune(t);
  }

  prune(t) {
    while (this.probeTimes.length && t - this.probeTimes[0] > KEEP_MS) this.probeTimes.shift();
    for (const [tick, at] of this.probeAt) { if (t - at > KEEP_MS) this.probeAt.delete(tick); else break; }
  }

  // ゲスト：PROBE_REPORT_MS ごとにまとめを返して数え直す。まだその時でなければ null
  report() {
    const t = this.now();
    if (t - this.windowStart < PROBE_REPORT_MS) return null;
    const r = { seconds: Math.round((t - this.windowStart) / 100) / 10, open: this.open,
      relCount: this.rel.count, relMaxGap: Math.round(this.rel.maxGap), relStalls: this.rel.stalls,
      probeCount: this.probe.count, probeMaxGap: Math.round(this.probe.maxGap),
      probeLost: this.probe.lost, probeLate: this.probe.late, probeStalls: this.probe.stalls,
      maxLag: Math.round(this.maxLag), stalls: this.stalls };
    this.reset();
    return r;
  }

  close() { try { this.channel?.close(); } catch { /* 閉じるだけ */ } this.channel = null; }
}

const num = (v, max) => (Number.isFinite(v) && v >= 0 && v <= max ? Math.round(v) : 0);

// ホストのコンソールに出す1件分の文。ゲストから届いた値なので、数字以外は捨てる。
// 文字列の字は画面のフォントの絞った版(tests/font-subset.test.js)に数えられるので、すでに入っている字とかなだけで書く
export function formatProbeReport(playerId, r, skipped) {
  if (!r.open && !num(r.probeCount, 1e6)) return `[SyncProbe] ${playerId} 別ルートが開いていません`;
  const lines = [`[SyncProbe] ${playerId} ${num(r.seconds, 600)}秒: ` +
    `今のルート 届いた${num(r.relCount, 1e6)} 一番空いた間${num(r.relMaxGap, 6e4)}ms 待たされた${num(r.relStalls, 1e6)}回 / ` +
    `別ルート 届いた${num(r.probeCount, 1e6)} 失った${num(r.probeLost, 1e6)} 順番が前後${num(r.probeLate, 1e6)} ` +
    `一番空いた間${num(r.probeMaxGap, 6e4)}ms 待たされた${num(r.probeStalls, 1e6)}回 / ` +
    `同じSYNCが今のルートでおくれた最大${num(r.maxLag, 6e4)}ms / ホストが送らなかったSYNC ${skipped}`];
  for (const s of Array.isArray(r.stalls) ? r.stalls.slice(0, MAX_STALLS) : []) {
    const lag = Number.isFinite(s?.lag) ? `${num(s.lag, 6e4)}ms` : 'わからない(別ルートで失った)';
    lines.push(`    今のルートが${num(s?.gap, 6e4)}ms空いた(ホストが送った間は${num(s?.sent, 6e4)}ms): ` +
      `その間に別ルートで届いた数 ${num(s?.during, 1e4)} / 空きを終えたSYNCが別ルートよりおくれた分 ${lag}`);
  }
  return lines.join('\n');
}
