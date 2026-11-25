import test from 'node:test';
import assert from 'node:assert/strict';
import { PROBE_REPORT_MS, SYNC_PROBE_CHANNEL_ID, SyncProbe, formatProbeReport } from '../src/network/syncProbe.js';

const mark = (seq, tick, hostMs) => new Int32Array([seq, tick, hostMs]).buffer;

function fakeChannel() {
  return { readyState: 'open', bufferedAmount: 0, sent: [], send(data) { this.sent.push(data); } };
}

test('the probe channel is negotiated on the same peer connection once the connection opens', () => {
  const made = [];
  const handlers = {};
  const conn = { open: false, on: (type, fn) => { handlers[type] = fn; },
    peerConnection: { createDataChannel: (label, options) => { made.push(options); return fakeChannel(); } } };
  const probe = new SyncProbe(() => 0);
  probe.attach(conn);
  assert.equal(made.length, 0);
  handlers.open();
  assert.deepEqual(made, [{ negotiated: true, id: SYNC_PROBE_CHANNEL_ID, ordered: false, maxRetransmits: 0 }]);
  assert.equal(probe.channel.binaryType, 'arraybuffer');
});

test('a connection without a peer connection (tests, old browsers) simply has no probe', () => {
  const probe = new SyncProbe(() => 0);
  probe.attach({ open: true, on() {} });
  assert.equal(probe.open, false);
  probe.sendTick(1, 0); // 何もしない
});

test('the host sends a small numbered mark per sync and skips it while backed up', () => {
  const probe = new SyncProbe(() => 0);
  const channel = fakeChannel(); probe.use(channel);
  probe.sendTick(7, 1234);
  channel.bufferedAmount = 5000; probe.sendTick(8, 1254);
  assert.equal(channel.sent.length, 1);
  assert.deepEqual([...new Int32Array(channel.sent[0])], [1, 7, 1234]);
});

test('a stall only on the reliable path is told apart from a stall on both paths', () => {
  let now = 0;
  const probe = new SyncProbe(() => now);
  // 20msごとに両方で届く
  for (let tick = 1; tick <= 5; tick++) {
    now = tick * 20;
    probe.received(mark(tick, tick, tick * 20));
    probe.reliableArrived(tick, tick * 20);
  }
  // 再送待ち：別ルートは届き続けるが、今のルートは tick 6〜9 がまとめて 100ms 後に届く
  for (let tick = 6; tick <= 9; tick++) { now = tick * 20; probe.received(mark(tick, tick, tick * 20)); }
  now = 200;
  for (let tick = 6; tick <= 9; tick++) probe.reliableArrived(tick, tick * 20);
  // 回線ごと止まった：両方とも 120ms 届かない
  now = 320; probe.received(mark(10, 10, 200)); probe.reliableArrived(10, 200);
  // ホストが送っていない間(カットイン)は数えない
  now = 620; probe.received(mark(11, 11, 500)); probe.reliableArrived(11, 500);

  now = PROBE_REPORT_MS;
  const r = probe.report();
  assert.equal(r.relStalls, 2);
  assert.equal(r.probeStalls, 1);
  assert.deepEqual(r.stalls[0], { gap: 100, sent: 20, during: 4, lag: 80 });
  assert.deepEqual(r.stalls[1], { gap: 120, sent: 20, during: 0, lag: 0 });
  assert.equal(r.maxLag, 80);
  assert.equal(r.probeLost, 0);
  // 数え直している
  now += PROBE_REPORT_MS;
  assert.equal(probe.report().relCount, 0);
});

test('lost and reordered marks are counted', () => {
  let now = 0;
  const probe = new SyncProbe(() => now);
  probe.received(mark(1, 1, 0));
  now = 20; probe.received(mark(3, 3, 40));
  now = 25; probe.received(mark(2, 2, 20));
  now = 40; probe.received(mark(5, 5, 80));
  now = PROBE_REPORT_MS;
  const r = probe.report();
  assert.equal(r.probeLate, 1);
  assert.equal(r.probeLost, 1);
});

test('the host log line keeps only numbers from the guest report', () => {
  const line = formatProbeReport('p1', { seconds: 5, open: true, keepAlive: true, relCount: 250, relMaxGap: 'x', relStalls: 1,
    probeCount: 248, probeLost: 2, probeLate: 0, probeMaxGap: 40, probeStalls: 0, maxLag: 80,
    stalls: [{ gap: 100, sent: 20, during: 4, lag: null }] }, 3, { count: 250, lost: 0, maxGap: 30, stalls: 0 });
  assert.match(line, /^\[SyncProbe\] p1 5秒 ゲストから送り続け=あり: 今のルート 届いた250 一番空いた間0ms 待たされた1回/);
  assert.match(line, /ホストが送らなかったSYNC 3 \/ 上り\(ゲストからホスト\) 届いた250 失った0 一番空いた間30ms 待たされた0回/);
  assert.match(line, /100ms空いた\(ホストが送った間は20ms\): その間に別ルートで届いた先のSYNC 4 \/ .*わからない/);
  // ゲスト自身のコンソールには、ホストだけが知っている数は出さない
  assert.doesNotMatch(formatProbeReport('p1', { open: true, probeCount: 1 }), /ホストが送らなかった|上り/);
  assert.equal(formatProbeReport('p2', { open: false, probeCount: 0 }, 0), '[SyncProbe] p2 別ルートが開いていません');
});

test('when the mark ending a stall arrives right after the sync, the reliable path was not late', () => {
  let now = 0;
  const probe = new SyncProbe(() => now);
  now = 20; probe.reliableArrived(1, 20); probe.received(mark(1, 1, 20));
  now = 140; probe.reliableArrived(2, 40); probe.received(mark(2, 2, 40));
  now = PROBE_REPORT_MS;
  // 止まる直前の SYNC の印(tick 1)は、止まっている間に届いた数に入れない
  assert.deepEqual(probe.report().stalls, [{ gap: 120, sent: 20, during: 0, lag: 0 }]);
});

test('the guest keeps sending a small mark every 20ms only during a match', () => {
  const timers = [];
  const probe = new SyncProbe(() => 0, { setTimer: (fn, ms) => { timers.push({ fn, ms, on: true }); return timers.length - 1; },
    clearTimer: id => { timers[id].on = false; } });
  const channel = fakeChannel(); probe.use(channel);
  probe.keepAlive(true); probe.keepAlive(true);
  assert.equal(timers.length, 1);
  assert.equal(timers[0].ms, 20);
  timers[0].fn(); timers[0].fn();
  assert.deepEqual(channel.sent.map(b => [...new Int32Array(b)]), [[1], [2]]);
  probe.keepAlive(false);
  assert.equal(timers[0].on, false);
  probe.keepAlive(true); probe.close();
  assert.equal(timers[1].on, false);
});

test('the host measures the upstream marks from the guest', () => {
  let now = 0;
  const probe = new SyncProbe(() => now);
  const channel = fakeChannel(); probe.use(channel);
  const up = seq => channel.onmessage({ data: new Int32Array([seq]).buffer });
  now = 20; up(1); now = 40; up(2); now = 160; up(4); now = 170; up(3);
  assert.deepEqual(probe.takeUp(), { count: 4, maxGap: 120, lost: 0, stalls: 1 });
  assert.deepEqual(probe.takeUp(), { count: 0, maxGap: 0, lost: 0, stalls: 0 });
  // 試合の切れ目(長く空いた)は待たされたに数えない
  now = 10000; up(1); now = 10020; up(2);
  assert.deepEqual(probe.takeUp(), { count: 2, maxGap: 20, lost: 0, stalls: 0 });
});

test('the default timer is called the way browsers require (not as a method of the probe)', t => {
  // ブラウザの setInterval は、ほかのものの this で呼ぶと Illegal invocation になる。それをまねる
  const calls = [];
  t.mock.method(globalThis, 'setInterval', function (fn, ms) {
    if (this !== undefined && this !== globalThis) throw new TypeError('Illegal invocation');
    calls.push(ms); return 1;
  });
  t.mock.method(globalThis, 'clearInterval', function () {
    if (this !== undefined && this !== globalThis) throw new TypeError('Illegal invocation');
  });
  const probe = new SyncProbe(() => 0);
  probe.use(fakeChannel());
  probe.keepAlive(true);
  probe.keepAlive(false);
  assert.deepEqual(calls, [20]);
});
