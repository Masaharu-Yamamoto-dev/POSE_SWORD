import { Fragment, useState, useEffect, useLayoutEffect, useRef } from 'react';
import { PLAYER_COLORS, styles, swordImageSource } from '../styles';
import { HILT_DATABASE } from './hiltDatabase.js';
import InkButton from '../components/InkButton.jsx';
import { InkBurst } from '../components/SwordForgeParts.jsx';
import { resultScene } from './resultScene.js';
import './ResultScreen.css';

// 勝者の剣の演出は motion で選ぶ（指定がなければ bounce）。
// bounce：横切り → 跳ね返り → ドン
//   SWIPE：刃先を前にして画面を横切る。真ん中だけスローになる
//   FLY  ：右から戻ってきて、画面の端で跳ね返りながらまっすぐ飛び回り、下へ抜ける
//   SLAM ：左の定位置にドンと着地する
const SWIPE_MS = 1200;
const FLY_MS = 1500;
const SLAM_MS = 500;
// drop：下から上へ横切って天に昇り、光の中から現れて落ちてきて突き刺さる
//   RISE  ：剣をまっすぐ立てたまま、下から上へ画面を横切る。真ん中だけスローになる
//   AIM   ：上空がきらりと光り、その光に隠れて小さな剣が現れる（光が引くと点の剣が見える）
//   FALL  ：回りながら大きくなって、定位置めがけて落ちてくる（だんだん速く）
//   IMPACT：突き刺さる。光と墨しぶき、地面に墨が広がり、剣が左右に揺れて止まる
const RISE_MS = 900;
const AIM_MS = 700;
// 上空の剣の位置（定位置の剣の真ん中から見たずれ）。光・落ちる動きの起点
const AIM_DX = 240, AIM_DY = -280;
// 下から上への横切りの位置（舞台の高さに対する割合）と、その時刻。真ん中の 0.6〜0.4 をゆっくり通る
const RISE_Y = [[0, 1.5], [0.33, 0.6], [0.67, 0.4], [1, -0.5]];
const FALL_MS = 650;
const IMPACT_MS = 500; // 揺れている途中で順位表を出し始める
// 残像の数（本体のすぐ後ろを少しずつ遅れて追う）
const GHOSTS = 4;
// 横切りの位置（舞台の幅に対する割合）と、その時刻（全体に対する割合）。真ん中の 0.4〜0.6 をゆっくり通る
const SWIPE_X = [[0, -0.35], [0.33, 0.4], [0.67, 0.6], [1, 1.4]];

// 引き分け（1位が2人以上）：相打ち
//   CLASH ：1位の剣が四方（2人なら左右）から刃先を向けて突っ込み、真ん中でぶつかる
//   FREEZE：ぶつかった瞬間に画面が止まり、白黒になる（決着がつかない間）
//   REPEL ：互いに弾かれ、回りながら画面の外へ飛んでいく
//   SLAM  ：そろって同時に定位置へドンと着地する（勝利と同じ動き）
const CLASH_MS = 520;
const FREEZE_MS = 650;
const REPEL_MS = 450;
// 引き分けの剣の残像の数と、飛んでいるときの大きさ
const DRAW_GHOSTS = 2;
const DRAW_FLY_K = 1.3;
// 引き分けの定位置の剣の大きさ（人数ごと。並べても左側に収まるように小さくする）
const DRAW_SLOT_K = { 2: 1.0, 3: 0.78, 4: 0.62 };
// 1vs3 の引き分けで、ボスの剣をほかより大きくする倍率（ぶつかる所と、最後に並べる所）
const DRAW_BOSS_K = 1.35;

// 1vs3 でボスが勝ったとき
//   CARDS：先に3人の剣が、落とした札のようにひらひらと落ちてきて、床に平らに当たって跳ね、寝て止まる（3D）
//   そのあと上空の光からボスが現れて突き刺さる（下から上への横切りはしない）。着地の瞬間に墨しぶきが大きく飛び、
//   地面が大きく揺れ、寝ている3人の剣が跳ねる
const CARDS_MS = 1150;
const BOSS_SLOT_K = 1.45;
// 寝る3人の剣：ボスの真ん中からの位置・床の上での向き・落ちる遅れ
const FALLEN = [
  { x: -150, rz: 70, delay: 0 },
  { x: 140, rz: -64, delay: 0.14 },
  { x: -5, rz: 100, delay: 0.28 },
];

// 1vs3 で3人側が勝ったとき：3人でボスを斬り結び、明転して結果の位置に3人が並ぶ
//   DUEL ：真ん中に大きなボスの剣。3人が順に角度を変えて、回転しながら斬り抜け、そのたびにボスが奥へよろめく（3Dで傾く）
//   FINAL：3人が三方からボスへまっすぐ突っ込み、当たる直前でスローになる。スローの間に画面が白くなっていき、真っ白（明転）
//   WHITE：真っ白のまま。この間に結果の位置へ3人を並べ替える（見えない）
//   UI   ：白がゆっくり引くと、結果の位置に3人が並んでいる
const DUEL_MS = 2000;
const FINAL_MS = 1100;
// 最後の突っ込み：はじめの 35% の時間で一気に寄り、残りはボスの手前でスローになって少しずつ迫る。その間に明転していく
const FINAL_RUSH = 0.35;
const WHITE_MS = 650;
const TEAM_HITS = [520, 1000, 1480]; // 斬り抜ける時刻（DUEL の始まりから）
const TEAM_PASS_MS = 340;
// 回転しながら飛ぶ剣：奥へ寝かせた円盤のように見せる（3D）
const spinPose = (rot, spin) => `perspective(900px) rotateX(38deg) rotate(${rot + spin}deg) scale(1.25)`;
const TEAM_DIRS = [[1, 0.38], [-1, 0.38], [0.3, -1]].map(([x, y]) => { const l = Math.hypot(x, y); return [x / l, y / l]; });
// 最後の突っ込みの向き（進む向き）：左上から・右上から・真下から
const TEAM_FINAL_DIRS = [[0.87, 0.5], [-0.87, 0.5], [0, -1]];

// 定位置（最後の表示）の剣の大きさ。飛んでいる剣・残像は等倍
const SLOT_K = 1.3;
// 残機制：控えの剣の真ん中からの横のずれ（大きさは ResultScreen.css の resultReserveIn で 0.62 倍）
const RESERVE_DX = 160;

// 刀身＋柄（独楽は柄なし）。定位置の剣と、飛んでいる剣・残像・控えの剣で同じ組み方にする。k は大きさの倍率
const WinnerSword = ({ sword, hilt, glow = true, k = 1 }) => (
  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
    <img src={sword?.imageSrc || swordImageSource(sword)} alt="" draggable={false}
      style={{ height: `${220 * k}px`, maxWidth: `${260 * k}px`, width: 'auto', objectFit: 'contain', zIndex: 1,
        filter: glow ? 'drop-shadow(0 0 15px rgba(255,215,0,0.6))' : 'none' }} />
    {hilt && <img src={hilt.imageSrc} alt="" draggable={false} style={{ height: `${180 * k}px`, width: 'auto', marginTop: `${-60 * k}px`, zIndex: 2, objectFit: 'contain' }} />}
  </div>
);

const hiltOfType = type => HILT_DATABASE[type || "0"] || HILT_DATABASE["0"];
// 残機制で、勝者がロビーで選んだ剣（装備中）以外の手持ちの剣（空きの枠は出さない。対戦開始の演出と同じ）
const reservesOf = sword => (sword?.swords ?? [])
  .filter((slot, i) => slot && !slot.isEmpty && i !== sword.equippedIndex)
  .slice(0, 2);

// motion：一人の勝者の演出を決めたいときだけ渡す（'bounce' / 'drop'）。渡さなければ試合ごとにランダム
export default function ResultScreen({ view, onReturnToLobby, onFindNewOpponents = null, motion }) {
  // 演出フェーズ
  //   bounce: INIT(暗転) -> SWIPE(横切り) -> FLY(暴れ回る) -> SLAM(定位置にドン) -> UI(表示)
  //   drop  : INIT(暗転) -> AIM(上空で光る) -> FALL(落ちる) -> IMPACT(突き刺さって揺れる) -> UI(表示)
  const [phase, setPhase] = useState('INIT');
  const rootRef = useRef(null);
  const impactRef = useRef(null);
  const flyRefs = useRef([]);
  const bossRef = useRef(null);
  const teamRefs = useRef([]);
  const streakRefs = useRef([]);
  const bossAnimRef = useRef(null);

  const result = view?.result;
  const players = view.resultPlayers ?? [];
  const playerOf = id => players.find(p => p.playerId === id);
  const room = view?.room;
  const soloMode = room?.soloMode;
  const bossPlayerId = room?.bossPlayerId;
  // ルールに応じた場面（どの演出か・表の並び順・誰を出すか）は resultScene.js で決める
  const scene = resultScene({ result, room, players, motion });
  const standings = scene.standings;
  const topKills = Math.max(0, ...standings.map(s => s.kills ?? 0));
  const topDealt = Math.max(0, ...standings.map(s => s.damageDealt ?? 0));
  // 独楽は柄を付けない（刀身だけ）
  const isKoma = scene.koma;
  const hiltFor = sword => (isKoma ? null : hiltOfType(sword?.hiltType));

  // 1vs3：ボス側の勝ち／3人側の勝ち
  const boss = scene.bossId ? playerOf(scene.bossId) : null;
  const bossWon = scene.kind === 'bossWin';
  const team = scene.kind === 'teamWin' ? scene.lineupIds.map(playerOf) : [];
  const teamWon = team.length > 0;
  const teamKey = team.map(p => p.playerId).join(',');
  // ボスが勝ったときに足元に転がる3人（並び順どおり）
  const challengers = scene.challengerIds.map(playerOf);
  // 一人の勝者（一本勝負・残機制・ボス側の勝ち）
  const winner = scene.winnerId ? playerOf(scene.winnerId) : null;
  // 一人の勝者の演出：bounce（跳ね返り）か drop（天から突き刺さる）。ボスが勝ったときは drop
  const drop = scene.motion === 'drop';
  const heroK = bossWon ? BOSS_SLOT_K : SLOT_K;

  // 勝者の剣と柄のデータ
  const winnerHilt = winner ? hiltFor(winner.swordData) : null;
  // 残機制：演出はロビーで選んだ剣が行い、最後に控えの剣が左右へ出てくる（1本なら左だけ）
  const reserves = winner && scene.lives ? reservesOf(winner.swordData) : [];
  // 引き分け：ぶつかり合う人たち（clashers）と、最後に並べる人たち（tied）。
  // 1vs3 の引き分けは、ぶつかるのがボスと3人側の3本、並べるのがボスと3人側で表のいちばん上の人
  const soloDraw = scene.kind === 'soloDraw';
  const tied = scene.kind === 'draw' || soloDraw ? scene.lineupIds.map(playerOf) : [];
  const clashers = scene.clasherIds.map(playerOf);
  const tiedKey = (soloDraw ? 'solo:' : '') + clashers.map(p => p.playerId).join(',');
  const drawRefs = useRef([]);

  useEffect(() => {
    if (!result) return;

    // 引き分け（1位が2人以上）：相打ち
    if (tiedKey) {
      const steps = [['CLASH', 350], ['FREEZE', CLASH_MS], ['REPEL', FREEZE_MS], ['SLAM', REPEL_MS], ['UI', SLAM_MS]];
      let at = 0;
      const timers = steps.map(([next, wait]) => { at += wait; return setTimeout(() => setPhase(next), at); });
      return () => timers.forEach(clearTimeout);
    }

    // 1vs3 の3人側の勝ち：斬り結んで明転
    if (teamKey) {
      const steps = [['DUEL', 350], ['FINAL', DUEL_MS], ['WHITE', FINAL_MS], ['UI', WHITE_MS]];
      let at = 0;
      const timers = steps.map(([next, wait]) => { at += wait; return setTimeout(() => setPhase(next), at); });
      return () => timers.forEach(clearTimeout);
    }

    // 勝者がいない場合（1位が分からない引き分けなど）はすぐUIを表示
    if (result.draw || !winner) {
      const t = setTimeout(() => setPhase('UI'), 0);
      return () => clearTimeout(t);
    }

    const steps = bossWon
      ? [['CARDS', 350], ['AIM', CARDS_MS], ['FALL', AIM_MS], ['IMPACT', FALL_MS], ['UI', IMPACT_MS]]
      : drop
      ? [['RISE', 350], ['AIM', RISE_MS], ['FALL', AIM_MS], ['IMPACT', FALL_MS], ['UI', IMPACT_MS]]
      : [['SWIPE', 350], ['FLY', SWIPE_MS], ['SLAM', FLY_MS], ['UI', SLAM_MS]];
    let at = 0;
    const timers = steps.map(([next, wait]) => { at += wait; return setTimeout(() => setPhase(next), at); });
    return () => timers.forEach(clearTimeout);
  }, [result, winner, drop, bossWon, tiedKey, teamKey]);

  // 3人側の勝ち：ボスの剣と、斬り抜ける3人・斬った跡の筋の動き
  useLayoutEffect(() => {
    if (!teamKey || (phase !== 'DUEL' && phase !== 'FINAL')) return;
    const root = rootRef.current;
    if (!root) return;
    const w = root.offsetWidth, h = root.offsetHeight;
    const cx = 0.5 * w, cy = 0.47 * h;
    const at = (el, x, y) => `${x - el.offsetWidth / 2}px ${y - el.offsetHeight / 2}px`;
    const anims = [];
    const b = bossRef.current;
    const bossPose = (ry, rz) => `perspective(900px) rotateY(${ry}deg) rotateZ(${rz}deg)`;

    if (phase === 'DUEL') {
      if (b) {
        // 奥から回り込むように現れ、斬られるたびに左右交互に奥へよろめいて戻る
        const kf = [
          { offset: 0, translate: at(b, cx, cy), transform: bossPose(-60, 0), opacity: 0 },
          { offset: 0.16, translate: at(b, cx, cy), transform: bossPose(0, 0), opacity: 1 },
        ];
        TEAM_HITS.forEach((t, i) => {
          const o = t / DUEL_MS, sgn = i % 2 ? -1 : 1;
          kf.push({ offset: o, translate: at(b, cx, cy), transform: bossPose(0, 0), opacity: 1 });
          kf.push({ offset: o + 0.04, translate: at(b, cx + sgn * 18, cy), transform: bossPose(sgn * 38, sgn * -7), opacity: 1 });
          kf.push({ offset: o + 0.16, translate: at(b, cx, cy), transform: bossPose(0, 0), opacity: 1 });
        });
        kf.push({ offset: 1, translate: at(b, cx, cy), transform: bossPose(0, 0), opacity: 1 });
        // ボスの動きは最後の突っ込み（FINAL）の間もそのまま止めておく。場面の切り替えで取り消すと、
        // 一瞬もとの見えない状態に戻って点滅して見えるので、ほかの動きとは別に持つ
        bossAnimRef.current?.cancel();
        bossAnimRef.current = b.animate(kf, { duration: DUEL_MS, fill: 'both', easing: 'ease-out' });
      }
      teamRefs.current.forEach((el, i) => {
        if (!el) return;
        const [dx, dy] = TEAM_DIRS[i];
        const rot = (Math.atan2(dy, dx) * 180) / Math.PI + 90; // 刃先を進む向きへ
        const far = 0.75 * w;
        // 回転しながら斬り抜ける（2回転半）
        anims.push(el.animate([
          { translate: at(el, cx - dx * far, cy - dy * far), transform: spinPose(rot, 0) },
          { translate: at(el, cx + dx * far, cy + dy * far), transform: spinPose(rot, 900) },
        ], { duration: TEAM_PASS_MS, delay: TEAM_HITS[i] - TEAM_PASS_MS / 2, easing: 'linear', fill: 'both' }));
      });
      // 斬った跡：剣が通るのに合わせて、進む向きへ一気に引かれる細い光の筋。引き終わると上下から細くなって消える
      streakRefs.current.forEach((el, i) => {
        if (!el) return;
        const [dx, dy] = TEAM_DIRS[i];
        const L = el.offsetWidth, H = el.offsetHeight;
        const base = `translate(${cx - (L / 2) * dx}px, ${cy - (L / 2) * dy - H / 2}px) rotate(${Math.atan2(dy, dx)}rad)`;
        // 筋の始まりから終わりまでを剣が通る時間（剣は舞台幅の1.5倍を TEAM_PASS_MS で進む）に合わせて、剣のすぐ後ろを引く
        const speed = (1.5 * w) / TEAM_PASS_MS;
        const drawMs = L / speed;
        const startMs = TEAM_HITS[i] - drawMs / 2;
        const total = drawMs + 420;
        anims.push(el.animate([
          { transform: `${base} scale(0, 1)`, opacity: 1, easing: 'linear' },
          { transform: `${base} scale(1, 1)`, opacity: 1, offset: drawMs / total, easing: 'ease-in' },
          { transform: `${base} scale(1.04, 0)`, opacity: 0.6 },
        ], { duration: total, delay: startMs, fill: 'both' }));
      });
    } else {
      // 三方から刃先を向けてまっすぐ突っ込み、当たる直前でスローになる（スローの間に明転していく）
      teamRefs.current.forEach((el, i) => {
        if (!el) return;
        const [dx, dy] = TEAM_FINAL_DIRS[i];
        const rot = (Math.atan2(dy, dx) * 180) / Math.PI + 90;
        const pose = `perspective(900px) rotate(${rot}deg) scale(1.25)`;
        anims.push(el.animate([
          { offset: 0, translate: at(el, cx - dx * 0.8 * w, cy - dy * 0.8 * w), transform: pose, easing: 'cubic-bezier(0.5, 0, 0.6, 1)' },
          { offset: FINAL_RUSH, translate: at(el, cx - dx * 230, cy - dy * 230), transform: pose, easing: 'linear' },
          { offset: 1, translate: at(el, cx - dx * 170, cy - dy * 170), transform: pose },
        ], { duration: FINAL_MS, fill: 'both' }));
      });
    }
    return () => anims.forEach(x => x.cancel());
  }, [phase, teamKey]);

  // 引き分けの剣の動き。ぶつかってから止まっている間（FREEZE）も同じ要素をそのまま止めておくので、
  // CLASH と FREEZE をひとまとまりにして、間で動きを取り消さないようにする
  const drawStage = phase === 'CLASH' || phase === 'FREEZE' ? 'CLASH' : phase === 'REPEL' ? 'REPEL' : null;
  useLayoutEffect(() => {
    if (!drawStage) return;
    const root = rootRef.current;
    if (!root) return;
    const w = root.offsetWidth, h = root.offsetHeight;
    const cx = 0.5 * w, cy = 0.46 * h;
    const n = Math.max(2, clashers.length);
    const anims = [];
    drawRefs.current.forEach((el, idx) => {
      if (!el) return;
      const t = Math.floor(idx / (DRAW_GHOSTS + 1)), g = idx % (DRAW_GHOSTS + 1);
      // 人ごとの向き：2人なら左と右、3〜4人なら周りから等間隔に。刃先（上）を真ん中へ向ける。
      // 1vs3 はボスが左から、3人が右上・右・右下からそろって突っ込む
      const a = soloDraw ? (t === 0 ? Math.PI : [-0.5, 0, 0.5][t - 1]) : Math.PI + (2 * Math.PI * t) / n;
      const dx = Math.cos(a), dy = Math.sin(a);
      const rot = (Math.atan2(-dy, -dx) * 180) / Math.PI + 90;
      const bw = el.offsetWidth, bh = el.offsetHeight;
      const k = soloDraw && t === 0 ? DRAW_FLY_K * DRAW_BOSS_K : DRAW_FLY_K;
      const reach = (bh * k) / 2 - 18; // 刃先どうしが少し重なる所で止まる
      const at = (r, ox = 0, oy = 0) => `${cx + dx * r + ox - bw / 2}px ${cy + dy * r + oy - bh / 2}px`;
      if (drawStage === 'CLASH') {
        anims.push(el.animate([
          { translate: at(0.9 * w), rotate: `${rot}deg`, scale: k },
          { translate: at(reach), rotate: `${rot}deg`, scale: k },
        ], { duration: CLASH_MS - 40, delay: g * 30, easing: 'cubic-bezier(0.5, 0, 0.9, 0.6)', fill: 'both' }));
      } else {
        // 弾かれて、少し上へ浮きながら回って外へ（左右で回る向きを逆に）
        const spin = (dx > 0 ? -1 : 1) * -540;
        anims.push(el.animate([
          { translate: at(reach), rotate: `${rot}deg`, scale: k },
          { translate: at(0.95 * w, 0, -0.25 * h), rotate: `${rot + spin}deg`, scale: k },
        ], { duration: REPEL_MS - 40, delay: g * 30, easing: 'cubic-bezier(0.2, 0.6, 0.6, 1)', fill: 'both' }));
      }
    });
    return () => anims.forEach(x => x.cancel());
    // clashers は描画ごとに作り直すので、中身が同じかどうかは tiedKey で見る
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [drawStage, tiedKey]);

  useLayoutEffect(() => {
    if (phase !== 'SWIPE' && phase !== 'FLY' && phase !== 'RISE') return;
    const root = rootRef.current;
    if (!root) return;
    const w = root.offsetWidth, h = root.offsetHeight;
    const swords = flyRefs.current.filter(Boolean);
    const anims = [];

    if (phase === 'RISE') {
      // まっすぐ立てたまま、上空の剣と同じ横の位置を下から上へ。速い → 真ん中でスロー → 一気に天へ抜ける
      const impact = impactRef.current, k = root.getBoundingClientRect().width / w || 1;
      const x = impact ? (impact.getBoundingClientRect().left + impact.getBoundingClientRect().width / 2 - root.getBoundingClientRect().left) / k + AIM_DX : 0.44 * w;
      swords.forEach((el, i) => {
        const bw = el.offsetWidth, bh = el.offsetHeight;
        anims.push(el.animate(RISE_Y.map(([offset, v]) => ({
          offset,
          translate: `${x - bw / 2}px ${v * h - bh / 2}px`,
          rotate: '0deg',
          scale: offset === 0 || offset === 1 ? 1.5 : 1.9,
        })), { duration: RISE_MS, delay: i * 30, easing: 'cubic-bezier(0.2, 0.7, 0.8, 0.3)', fill: 'both' }));
      });
    } else if (phase === 'SWIPE') {
      // 刃先を右に向けて寝かせ、少し右上がりに横切る。速い → 真ん中でスロー → 一気に抜ける
      const y = 0.46 * h;
      swords.forEach((el, i) => {
        const bw = el.offsetWidth, bh = el.offsetHeight;
        anims.push(el.animate(SWIPE_X.map(([offset, u]) => ({
          offset,
          translate: `${u * w - bw / 2}px ${y - bh / 2 - (u - 0.5) * 0.08 * h}px`,
          rotate: '86deg',
          scale: offset === 0 || offset === 1 ? 1.7 : 2.2,
        })), { duration: SWIPE_MS, delay: i * 30, easing: 'cubic-bezier(0.2, 0.7, 0.8, 0.3)', fill: 'both' }));
      });
    } else {
      // 右の画面外から戻り、上 → 下 → 左 → 上 → 右の端で跳ね返って、左下の画面外へ抜ける。
      // 剣の真ん中が端から少し内側に来た所で跳ね返る（回っている剣の先が端に当たって見える位置）
      const pts = [[1.12, 0.7], [0.72, 0.2], [0.42, 0.8], [0.1, 0.5], [0.42, 0.2], [0.9, 0.62], [0.55, 1.35]]
        .map(([u, v]) => [u * w, v * h]);
      const path = `path("M ${pts.map(([x, y]) => `${x} ${y}`).join(' L ')}")`;
      // 跳ね返るたびに回る向きを逆にする。回る角度は飛んだ距離に比例させ、跳ね返る瞬間と向きが変わる瞬間をそろえる
      const lens = pts.slice(1).map(([x, y], i) => Math.hypot(x - pts[i][0], y - pts[i][1]));
      const total = lens.reduce((a, b) => a + b, 0);
      const spin = [{ offset: 0, rotate: '0deg' }];
      let along = 0, angle = 0;
      lens.forEach((len, i) => {
        along += len;
        angle += (i % 2 ? -1 : 1) * (len / total) * 2600;
        spin.push({ offset: Math.min(1, along / total), rotate: `${angle}deg` });
      });
      // 道筋に沿って進むのは外側、回転は内側（同じ要素に掛けると、回転が進む向きまで回してしまう）。
      // 少しずつ加速し、抜け際がいちばん速い
      const timing = i => ({ duration: FLY_MS - 160, delay: i * 40, easing: 'cubic-bezier(0.4, 0.2, 0.8, 0.7)', fill: 'both' });
      swords.forEach((el, i) => {
        el.style.offsetPath = path;
        el.firstElementChild.style.scale = '1.3';
        anims.push(el.animate([{ offsetDistance: '0%' }, { offsetDistance: '100%' }], timing(i)));
        anims.push(el.firstElementChild.animate(spin, timing(i)));
      });
    }
    return () => anims.forEach(a => a.cancel());
  }, [phase]);

  if (!result) return null;

  const landed = phase === 'UI';
  const slammed = phase === 'SLAM' || landed;
  const impacted = phase === 'IMPACT' || landed;
  // 定位置の剣を見せるか・どの動きを付けるか（drop は定位置の剣そのものが上空から落ちてくる）
  const slotShown = drop ? !['INIT', 'CARDS', 'RISE'].includes(phase) : slammed;
  const slotClass = drop
    ? ({ AIM: 'result-drop-aim', FALL: 'result-drop-fall' }[phase] ?? '')
    : (slammed ? 'result-sword-slam' : '');
  const flying = phase === 'SWIPE' || phase === 'FLY' || phase === 'RISE';
  // 最後に横に並べる剣（引き分けの1位、または3人側の勝ち）
  const lineup = tied.length ? tied : team;
  const lineupShown = tied.length ? slammed : ['WHITE', 'UI'].includes(phase);
  const drawK = DRAW_SLOT_K[lineup.length] ?? 1;
  const teamStage = teamWon && (phase === 'DUEL' || phase === 'FINAL');
  // 3人側の勝ちの明転：舞台の中（白い幕）と外の余白（枠の影）を同じ速さで白くし、同じ速さで戻す
  const whiteOut = teamWon && (phase === 'FINAL' || phase === 'WHITE');
  // （それ以外の場面は、開幕の暗転が引く 0.5 秒）
  const whiteTransition = prop => (teamWon && phase === 'UI' ? `${prop} 1.3s ease-out`
    : whiteOut ? `${prop} ${Math.round(FINAL_MS * (1 - FINAL_RUSH))}ms ease-in ${Math.round(FINAL_MS * FINAL_RUSH)}ms`
    : `${prop} 0.5s`);

  return (
    // この画面は16:9の舞台（Stage16x9）の中に出る。大きさは vw / vh ではなく舞台基準の --stage-vw / --stage-vh で決める。
    // 開幕の暗転は box-shadow で舞台の外の余白まで広げる
    // 舞台からはみ出した剣や光は切り取る（overflow: clip）。舞台の外の余白の暗転・明転は、この枠自身の影（box-shadow）で付ける
    // （影は枠の切り取りを受けない）
    <div ref={rootRef} style={{
      ...styles.container, position: 'relative', justifyContent: 'center', overflow: 'clip',
      backgroundColor: phase === 'INIT' ? '#000' : 'transparent',
      boxShadow: `0 0 0 5000px ${phase === 'INIT' ? '#000' : whiteOut ? 'rgb(255, 253, 246)' : 'transparent'}`,
      transition: `background-color 0.5s, ${whiteTransition('box-shadow')}`,
    }}>

      {/* 🌟 背景の勝者の剣（元の画像が小さくても、舞台いっぱいまで引き伸ばして大きく敷く） */}
      {winner && (
        <img
          src={swordImageSource(winner.swordData)}
          alt=""
          className={landed ? 'result-bg-in' : ''}
          style={{
            ...styles.bgImageCenter,
            position: 'absolute',
            top: '50%',
            left: '50%',
            transform: 'translate(-50%, -50%)',
            height: 'calc(100 * var(--stage-vh))',
            width: 'calc(100 * var(--stage-vw))',
            objectFit: 'contain',
            opacity: landed ? 0.15 : 0,
            pointerEvents: 'none',
            zIndex: 0
          }}
        />
      )}

      {/* 🌟 引き分け・3人側の勝ち：並んだ人たちの剣を、舞台を等分して背景に薄く敷く */}
      {lineup.length > 0 && (
        <div className={landed ? 'result-bg-in-draw' : ''} aria-hidden="true" style={{
          position: 'absolute', inset: 0, display: 'flex', pointerEvents: 'none', zIndex: 0, opacity: landed ? 0.15 : 0,
        }}>
          {lineup.map(p => (
            <img key={p.playerId} src={swordImageSource(p.swordData)} alt="" style={{ flex: '1 1 0', minWidth: 0, height: '100%', objectFit: 'contain' }} />
          ))}
        </div>
      )}

      {/* 引き分け：ぶつかり合う剣と残像（弾かれる所で作り直す） */}
      {tied.length > 0 && drawStage && (
        <div className={phase === 'FREEZE' ? 'result-freeze' : ''} style={{ position: 'absolute', inset: 0, zIndex: 20, pointerEvents: 'none' }}>
          {clashers.flatMap((p, t) => Array.from({ length: DRAW_GHOSTS + 1 }, (_, g) => {
            const idx = t * (DRAW_GHOSTS + 1) + g;
            return (
              <div key={`${drawStage}-${idx}`} ref={el => { drawRefs.current[idx] = el; }} style={{
                position: 'absolute', left: 0, top: 0, zIndex: 10 - g,
                // 止まっている間は残像を消す（本体だけがぶつかったまま止まって見える）
                opacity: g === 0 ? 1 : phase === 'FREEZE' ? 0 : 0.3 - g * 0.08, filter: g === 0 ? 'none' : 'blur(1px)',
              }}>
                <WinnerSword sword={p.swordData} hilt={hiltFor(p.swordData)} glow={g === 0} />
              </div>
            );
          }))}
          {/* ぶつかった所の光と火花 */}
          {phase === 'FREEZE' && <span className="result-clash" aria-hidden="true" />}
        </div>
      )}

      {/* 3人側の勝ち：真ん中のボスの剣・斬り抜ける3人・斬った跡の筋 */}
      {teamStage && (
        <div style={{ position: 'absolute', inset: 0, zIndex: 20, pointerEvents: 'none' }}>
          {phase === 'DUEL' && team.map((p, i) => (
            <span key={`s${i}`} ref={el => { streakRefs.current[i] = el; }} aria-hidden="true" style={{
              position: 'absolute', left: 0, top: 0, width: '115%', height: '16px', zIndex: 3, opacity: 0, transformOrigin: '0 50%',
              // 周りのプレイヤー色の光は外側に付ける（内側の切り抜きで光まで切れないように）
              filter: `drop-shadow(0 0 4px ${PLAYER_COLORS[p.slotIndex]}) drop-shadow(0 0 12px ${PLAYER_COLORS[p.slotIndex]})`,
            }}>
              {/* 両端がとがった細い光（進む先のほうが太い）。芯は和紙色の白 */}
              <span style={{
                position: 'absolute', inset: 0,
                background: 'linear-gradient(180deg, rgba(255, 253, 246, 0) 0, rgb(255, 253, 246) 35%, rgb(255, 253, 246) 65%, rgba(255, 253, 246, 0) 100%)',
                clipPath: 'polygon(0 50%, 72% 0, 100% 50%, 72% 100%)',
              }} />
            </span>
          ))}
          {boss && (
            <div ref={bossRef} style={{ position: 'absolute', left: 0, top: 0, zIndex: 1, opacity: 0, filter: 'drop-shadow(0 0 24px rgba(179, 38, 30, 0.45))' }}>
              <WinnerSword sword={boss.swordData} hilt={hiltFor(boss.swordData)} glow={false} k={1.5} />
            </div>
          )}
          {team.map((p, i) => (
            <div key={`${phase}-${i}`} ref={el => { teamRefs.current[i] = el; }} style={{ position: 'absolute', left: 0, top: 0, zIndex: 5, translate: '-2000px 0' }}>
              <WinnerSword sword={p.swordData} hilt={hiltFor(p.swordData)} />
            </div>
          ))}
        </div>
      )}

      {/* 3人側の勝ち：明転（ぶつかった瞬間に真っ白になり、結果の画面が出てから引く） */}
      {/* 斬り合いの間から透明で置いておく（突っ込みが始まってから出すと、白くなっていく変化が付かない） */}
      {teamWon && ['DUEL', 'FINAL', 'WHITE', 'UI'].includes(phase) && (
        <div aria-hidden="true" style={{
          position: 'absolute', inset: 0, zIndex: 40, pointerEvents: 'none', background: 'rgb(255, 253, 246)',
          // FINAL の間にだんだん白くなり（最後に一気に）、ぶつかった瞬間に真っ白。UI でゆっくり引く
          opacity: whiteOut ? 1 : 0,
          transition: whiteTransition('opacity'),
        }} />
      )}

      {/* 飛んでいる剣と残像（場面ごとに作り直す。最後は画面の外へ抜け、定位置にドンと出る剣に入れ替わる） */}
      {winner && flying && Array.from({ length: GHOSTS + 1 }, (_, i) => (
        <div key={`${phase}-${i}`} ref={el => { flyRefs.current[i] = el; }} style={{
          position: 'absolute', left: 0, top: 0, zIndex: 20 - i, pointerEvents: 'none',
          offsetAnchor: 'center', offsetRotate: '0deg',
          opacity: i === 0 ? 1 : 0.36 - i * 0.07, filter: i === 0 ? 'none' : 'blur(1px)',
        }}>
          <div><WinnerSword sword={winner.swordData} hilt={winnerHilt} glow={i === 0} /></div>
        </div>
      ))}

      <div className={bossWon && phase === 'IMPACT' ? 'result-quake' : ''} style={{ width: '100%', maxWidth: '1180px', padding: '20px', boxSizing: 'border-box', zIndex: 1, position: 'relative' }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '40px', justifyContent: 'center', alignItems: 'center', minHeight: 'calc(60 * var(--stage-vh))' }}>

          {/* 左側：勝者の剣の定位置（飛んできた剣がここに収まる） */}
          <div style={{ position: 'relative', display: 'flex', flexDirection: 'column', alignItems: 'center', width: reserves.length || lineup.length || bossWon ? '420px' : '340px' }}>
            {/* 引き分け：並んだ人たちの剣が、そろって同時にドンと着地する／3人側の勝ち：明転の間にもう並んでいる（下をそろえて横に並ぶ） */}
            {lineup.length > 0 && (
              <div className={tied.length && slammed ? 'result-sword-slam' : ''} style={{
                display: 'flex', alignItems: 'flex-end', justifyContent: 'center', gap: `${12 * drawK}px`,
                height: `${350 * SLOT_K}px`, opacity: lineupShown ? 1 : 0,
              }}>
                {lineup.map(p => (
                  <WinnerSword key={p.playerId} sword={p.swordData} hilt={hiltFor(p.swordData)}
                    k={soloDraw && p.playerId === bossPlayerId ? drawK * DRAW_BOSS_K : drawK} />
                ))}
              </div>
            )}
            {/* drop：突き刺さった所に広がる墨（剣の後ろ） */}
            {winner && drop && impacted && !bossWon && <span className="result-drop-ground" aria-hidden="true" />}
            {winner && (
              <div className={slotClass} style={{ position: 'relative', zIndex: 1, height: `${350 * heroK}px`, display: 'flex', alignItems: 'flex-end', opacity: slotShown ? 1 : 0 }}>
                <div className={drop && impacted ? (bossWon ? 'result-boss-land' : 'result-drop-wobble') : ''} style={{ transformOrigin: '50% 100%' }}>
                  <WinnerSword sword={winner.swordData} hilt={winnerHilt} k={heroK} />
                </div>
              </div>
            )}
            {/* ボスが勝ったとき：3人の剣が札のように落ちてきて床に寝る（外側は位置と、ボス着地の衝撃で跳ねる動き／内側は札の落ち方） */}
            {bossWon && phase !== 'INIT' && challengers.map((p, i) => (
              <div key={p.playerId} className={phase === 'IMPACT' ? 'result-card-hop' : ''} aria-hidden="true"
                style={{ position: 'absolute', left: `calc(50% + ${FALLEN[i].x}px)`, bottom: '-40px', transform: 'translateX(-50%)', zIndex: 0, pointerEvents: 'none', animationDelay: `${i * 0.04}s` }}>
                <div className="result-card" style={{ '--rz': `${FALLEN[i].rz}deg`, animationDelay: `${FALLEN[i].delay}s` }}>
                  <WinnerSword sword={p.swordData} hilt={hiltFor(p.swordData)} glow={false} k={0.62} />
                </div>
              </div>
            ))}
            {/* 残機制の控えの剣：ドンと出たあと、真ん中（選んだ剣の後ろ）から外側へふわっと出て、小さめ・半透明で下をそろえて並ぶ */}
            {landed && reserves.map((slot, i) => (
              <div key={i} className="result-reserve" aria-hidden="true"
                style={{ '--dx': `${(i === 0 ? -1 : 1) * RESERVE_DX}px`, animationDelay: `${0.1 + i * 0.08}s` }}>
                <WinnerSword sword={slot} hilt={hiltFor(slot)} glow={false} k={SLOT_K} />
              </div>
            ))}
            {/* drop：突き刺さった瞬間の光と墨しぶき */}
            {winner && drop && impacted && <span className="result-drop-light" aria-hidden="true" />}
            {/* 上空の光：先に光が満ちて点の剣を隠し、剣が現れきってから引く */}
            {winner && drop && phase === 'AIM' && <span className="result-drop-glow" aria-hidden="true" style={{ left: `calc(50% + ${AIM_DX}px)`, top: `calc(50% + ${AIM_DY}px)` }} />}
            {/* 墨しぶきの出どころ：剣の根元（地面）。しぶきの大きさはこの幅で決まる */}
            <span ref={impactRef} aria-hidden="true" style={{ position: 'absolute', left: '50%', bottom: 0, width: bossWon ? '1100px' : '360px', height: 0, transform: 'translateX(-50%)' }} />
            {winner && drop && impacted && <InkBurst originRef={impactRef} delay={0} />}
          </div>

          {/* 右側：勝者・順位表・ボタン群（UIフェーズでフェードイン） */}
          <div style={{
            flex: 1, minWidth: '320px', maxWidth: '680px', display: 'flex', flexDirection: 'column', alignItems: 'center',
            opacity: landed ? 1 : 0, transform: landed ? 'translateX(0)' : 'translateX(50px)',
            transition: 'all 0.5s cubic-bezier(0.175, 0.885, 0.32, 1.275)'
          }}>

            <h2 style={{ fontSize: '52px', margin: '0 0 20px 0', color: 'var(--sumi)', display: 'flex', alignItems: teamWon ? 'center' : 'baseline', gap: '0.5em', whiteSpace: 'nowrap', maxWidth: '100%' }}>
              {result.draw ? "引き分け" : teamWon ? (
                <>
                  <span>勝者</span>
                  {/* 3人は1行ずつ */}
                  <span style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', textAlign: 'left', fontSize: '0.55em', lineHeight: 1.25, minWidth: 0 }}>
                    {team.map(p => (
                      <span key={p.playerId} style={{ color: PLAYER_COLORS[p.slotIndex], whiteSpace: 'nowrap' }}>{p.swordData?.name}</span>
                    ))}
                  </span>
                </>
              ) : winner ? (
                <>
                  <span>勝者</span>
                  <span style={{ color: PLAYER_COLORS[winner.slotIndex] }}>
                    {winner.swordData?.name}
                  </span>
                </>
              ) : "試合終了"}
            </h2>

            <div className="sumi-panel" style={{ padding: '20px', width: '100%' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  {/* 順位と名前は見れば分かるので見出しを付けない */}
                  <tr style={{ color: 'var(--usuzumi)', fontSize: '17px', whiteSpace: 'nowrap' }}>
                    <th colSpan={2} />
                    <th style={{ padding: '8px 6px', textAlign: 'center' }}>撃破数</th>
                    <th style={{ padding: '8px 6px', textAlign: 'center' }}>与ダメージ</th>
                    <th style={{ padding: '8px 6px', textAlign: 'center' }}>被ダメージ</th>
                  </tr>
                  {/* 見出しの下の線：ロビーの列の上の線と同じく、太めの線の縁を筆で引いたように欠けさせる */}
                  <tr aria-hidden="true"><td colSpan={5} style={{ padding: 0 }}><div className="result-rule result-rule--head" /></td></tr>
                </thead>
                <tbody>
                  {standings.map(score => {
                    const player = playerOf(score.playerId);
                    const isMe = score.playerId === view.localPlayerId;
                    const isBossRow = soloMode && score.playerId === bossPlayerId;
                    // 撃破数・与ダメージがいちばん多かった人（同じ数なら全員。0 は強調しない）は、数字を大きくして脈打たせる
                    const num = (value, top) => (
                      <td style={{ padding: '8px 6px', textAlign: 'center', fontSize: '26px' }}>
                        {top && value > 0 ? <span className="result-top">{value}</span> : value}
                      </td>
                    );

                    return (
                      // 1vs3 の引き分けは、順位をすべて1にする（ボスと3人側の相打ちで、どちらも負けていない）
                      <Fragment key={score.playerId}>
                      {/* 自分の行：自分のプレイヤー色を薄く敷く（左ほど濃く） */}
                      <tr style={{ background: isMe && player ? `linear-gradient(90deg, ${PLAYER_COLORS[player.slotIndex]}33, ${PLAYER_COLORS[player.slotIndex]}14)` : 'transparent' }}>
                        <td style={{ padding: '8px 10px', fontSize: '30px', textAlign: 'center', width: '1%' }}>{soloDraw ? 1 : score.rank}</td>
                        <td style={{ padding: '8px 6px' }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                            {player && <img src={swordImageSource(player.swordData)} alt="" style={{ width: '36px', height: '44px', objectFit: 'contain', flexShrink: 0 }} />}
                            {/* 上段：プレイヤー番号と「あなた」（小さめ）／下段：名前（大きめ・墨色・15文字まで収まる幅） */}
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', minWidth: 0, textAlign: 'left' }}>
                              <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '16px', lineHeight: 1.2, whiteSpace: 'nowrap' }}>
                                <span style={{ color: player ? PLAYER_COLORS[player.slotIndex] : 'var(--usuzumi)' }}>
                                  {player ? `${player.slotIndex + 1}P` : score.playerId}
                                </span>
                                {/* ロビーの名札と同じ「あなた」の墨札 */}
                                {isMe && <small style={{ padding: '1px 7px', fontSize: '14px', color: 'var(--washi)', background: 'var(--sumi)' }}>あなた</small>}
                                {/* ロビーのボスの札と同じ色（傾き・筆の揺らぎは付けない）。自分がボスなら「あなた」の右 */}
                                {isBossRow && <small style={{ padding: '1px 7px', fontSize: '14px', letterSpacing: '0.1em', color: 'var(--washi)', background: 'var(--kin)' }}>ボス</small>}
                              </div>
                              <div style={{ width: '15em', fontSize: '19px', lineHeight: 1.2, color: 'var(--sumi)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                {player?.swordData?.name}
                              </div>
                            </div>
                          </div>
                        </td>
                        {num(score.kills, score.kills === topKills)}
                        {num(score.damageDealt, score.damageDealt === topDealt)}
                        {num(score.damageTaken, false)}
                      </tr>
                      {/* 行の区切り：ロビーの下段の線と同じ、細い薄墨の線 */}
                      <tr aria-hidden="true"><td colSpan={5} style={{ padding: 0 }}><div className="result-rule" /></td></tr>
                      </Fragment>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {/* ボタン群 */}
            <div style={{ marginTop: '25px', display: 'flex', flexWrap: 'wrap', gap: '15px', width: '100%' }}>
              {!view.closed && (
                <InkButton variant="shu" style={{ flex: 1, minWidth: '150px' }} onClick={onReturnToLobby}>ロビーに戻る</InkButton>
              )}

              {onFindNewOpponents && (
                <InkButton style={{ flexBasis: '100%' }} onClick={onFindNewOpponents}>別の相手を探す</InkButton>
              )}
            </div>
          </div>
        </div>
      </div>

    </div>
  );
}
