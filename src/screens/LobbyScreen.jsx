import { useState, useEffect, useLayoutEffect, useRef } from 'react';
import { PLAYER_COLORS, styles, swordImageSource } from '../styles';
import { MAX_PLAYERS, MIN_PLAYERS, SOLO_MODE_PLAYERS } from '../network/HostRoom';
import { HILT_DATABASE } from './hiltDatabase.js';
import InkButton from '../components/InkButton.jsx';
import { ConfirmSheet, EnsoInk } from '../components/SwordForgeParts.jsx';
import { STAT_LABELS, useAnimatedValues } from '../components/swordForge.js';
import './LobbyScreen.css';

// ==========================================
// 🌟 ステータス計算ヘルパー（柄補正適用・下限1設定）
// ==========================================
const getFinalStats = (sword) => {
  if (!sword) return { hp: 1, attack: 1, weight: 1 };
  const hilt = HILT_DATABASE[sword.hiltType || "0"] || HILT_DATABASE["0"];
  return {
    hp: Math.max(1, (sword.hp || 0) + (hilt.hpBonus || 0)),
    attack: Math.max(1, (sword.attack || 0) + (hilt.attackBonus || 0)),
    weight: Math.max(1, (sword.weight || 0) + (hilt.weightBonus || 0)),
  };
};

// 柄の補正を足す前の剣本体の値（独楽モード用）
const getBaseStats = (sword) => ({
  hp: Math.max(1, sword?.hp || 0),
  attack: Math.max(1, sword?.attack || 0),
  weight: Math.max(1, sword?.weight || 0),
});

// ほかの人の剣の本数（空きのスロットを除く）。古い送り方で一覧がないときは、装備中の1本だけ
const swordCountOf = swordData => swordData?.swords ? swordData.swords.filter(slot => slot && !slot.isEmpty).length : 1;
const hiltOf = sword => HILT_DATABASE[sword?.hiltType || "0"] || HILT_DATABASE["0"];
const bladeOf = sword => sword?.imageSrc || swordImageSource(sword);

// ルールの名前と説明。説明は以前のロビー画面の文言をそのまま使っている。
const MODE_NAME = { "0": "剣（横視点・重力）モード", "1": "独楽（見下ろし）モード" };
const MODE_SHORT = { "0": "剣", "1": "独楽" };
const MODE_DESC = { "0": "剣を振り回して戦うモード", "1": "独楽のようにぶつかり合う半自動戦闘モード" };
const SPECIAL_NAME = { none: "特殊ルールなし", lives: "残機制", solo: "1 vs 3" };

// 対戦で使う必殺技。剣モードは柄の必殺技、独楽は全員が竜巻猛突。1vs3 のボスは柄の必殺技に掌握斬が加わる
const TOP_SKILL = { skillName: "竜巻猛突", skillIcon: "/skill-icons/tornado.png" };
const BOSS_SKILL = { skillName: "掌握斬", skillIcon: "/skill-icons/grasp.png" };
const skillsOf = (sword, gameMode, isBoss) =>
  gameMode === "1" ? [TOP_SKILL] : isBoss ? [hiltOf(sword), BOSS_SKILL] : [hiltOf(sword)];
const SPECIAL_DESC = {
  none: "",
  lives: "所持している剣がすべて撃破されるまで敗北しません",
  solo: "1人（ボス）対3人。3人側は同士討ちしません。",
};

// ==========================================
// レーダーチャート（HP・攻・重の三角形）と、右に並べる数字（武器庫と同じ形。柄の補正の内訳は出さない）
// ==========================================
// 上限は錬成で出る値の範囲（HP 100〜1000 / 攻・重 1〜100）。柄の補正で超えた分は外周で止める。
const RADAR_MAX = { hp: 1000, attack: 100, weight: 100 };
const RADAR_AXES = [[0, -1], [0.866, 0.5], [-0.866, 0.5]]; // 上：HP 右下：攻 左下：重

const RadarChart = ({ stats, hidden }) => {
  const cx = 59, cy = 56, radius = 34;
  const point = (i, scale) =>
    `${(cx + RADAR_AXES[i][0] * radius * scale).toFixed(1)},${(cy + RADAR_AXES[i][1] * radius * scale).toFixed(1)}`;
  const ring = scale => [0, 1, 2].map(i => point(i, scale)).join(' ');
  const values = [stats.hp / RADAR_MAX.hp, stats.attack / RADAR_MAX.attack, stats.weight / RADAR_MAX.weight]
    .map(v => Math.max(0.04, Math.min(1, v)));

  return (
    <svg className="lobby-radar" viewBox="0 0 118 100" role="img"
      aria-label={hidden ? 'ステータスは非公開' : `HP ${stats.hp}、攻 ${stats.attack}、重 ${stats.weight}`}>
      <polygon className="lobby-radar__axis" points={ring(1)} />
      <polygon className="lobby-radar__axis" points={ring(0.5)} />
      {[0, 1, 2].map(i => {
        const [x, y] = point(i, 1).split(',');
        return <line key={i} className="lobby-radar__axis" x1={cx} y1={cy} x2={x} y2={y} />;
      })}
      <polygon className="lobby-radar__value" points={[0, 1, 2].map(i => point(i, values[i])).join(' ')} />
      <text x="59" y="13" textAnchor="middle">HP</text>
      <text x="99" y="89" textAnchor="middle">攻</text>
      <text x="19" y="89" textAnchor="middle">重</text>
    </svg>
  );
};

const StatsList = ({ values, hidden }) => (
  <div className="lobby-stats__list">
    {values.map((value, i) => (
      <div key={STAT_LABELS[i]} className="lobby-stats__row">
        <span className="lobby-stats__label">{STAT_LABELS[i]}</span>
        <span className="lobby-stats__value">{hidden ? '？？？' : Math.round(value)}</span>
      </div>
    ))}
  </div>
);

// 剣を持ち替えたときは、武器庫と同じく、チャートと数字を新しい値へなめらかに動かす
// hidden（1vs3 のボス）：試合では HP・攻撃などに倍率が掛かり、ここの値は当てにならないので数字は伏せる。
// チャートは外周いっぱいまで伸ばして、手の付けられない強さだけを見せる
const StatsPanel = ({ stats, hidden }) => {
  const target = hidden ? RADAR_MAX : stats;
  const [hp, attack, weight] = useAnimatedValues([target.hp, target.attack, target.weight]);
  return (
    <div className="lobby-stats">
      <RadarChart stats={{ hp, attack, weight }} hidden={hidden} />
      <StatsList values={[hp, attack, weight]} hidden={hidden} />
    </div>
  );
};

// ==========================================
// 剣の名前（長い名前も省略せずに出す）
// ==========================================
// 名札の下の行を列の幅いっぱいに使い、1行に入る範囲でできるだけ大きく（19px まで）。幅を測ってから決めるので、大きさは直接付ける
const NAME_SIZE = 19;

const SwordName = ({ name }) => {
  const ref = useRef(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    let alive = true;
    const apply = () => {
      if (!alive) return;
      el.style.fontSize = `${NAME_SIZE}px`;
      const room = el.clientWidth;
      const width = el.scrollWidth;
      if (width > room) el.style.fontSize = `${Math.floor(NAME_SIZE * room / width * 10) / 10}px`;
    };
    apply();
    // 文字の形が読み込み終わると幅が変わるので、合わせ直す
    document.fonts?.ready.then(apply);
    return () => { alive = false; };
  }, [name]);
  return <p ref={ref} className="lobby-col__name">{name}</p>;
};

// ==========================================
// 🌀 柄を合成した剣（押すと回る）
// ==========================================
// spinCount は「この剣が回された回数」で、持ち主が回すたびに全員の画面で1ずつ増える。
// 回数を key にして、増えるたびに回転のアニメーションを最初からやり直させる。
// 画面を開いた時点の回数（base）では回さない（武器庫から戻っただけで回り出さないように）。
// onSpin があるのは自分の剣だけ。ほかの人の剣は押せない。
// hiltOff（独楽モード）：独楽は柄を付けずに戦うので、柄を外して落とす。剣モードへ戻すと付け直す。
// 柄の場所はそのまま空けておき、刀身の位置は動かさない。
const SwordFigure = ({ sword, blade, spinCount, onSpin, hiltOff }) => {
  const [base] = useState(spinCount);
  const Body = onSpin ? 'button' : 'div';
  return (
    <div className="lobby-sword">
      {/* 入室で上から降ってくる・退室で上へ抜けていく動きはこの層で付ける（浮かぶ動き・回転とは別の層） */}
      <div className="lobby-sword__drop">
        <div className="lobby-sword__float">
          <Body
            key={spinCount}
            {...(onSpin ? { type: 'button', onClick: onSpin, title: 'クリックで回転' } : {})}
            className={`lobby-sword__body ${spinCount !== base ? 'is-spinning' : ''}`}
          >
            {blade && <img className="lobby-sword__blade" src={blade} alt="Blade" draggable={false} />}
            <img className={`lobby-sword__hilt ${hiltOff ? 'is-off' : ''}`} src={hiltOf(sword).imageSrc}
              alt={hiltOff ? '' : 'Hilt'} aria-hidden={hiltOff || undefined} draggable={false} />
          </Body>
        </div>
      </div>
    </div>
  );
};

// ==========================================
// ルール設定（ホスト以外は説明を読むだけ）
// ==========================================
const RuleSheet = ({ gameMode, special, canEdit, onGameMode, onSpecial, onClose }) => (
  <div className="lobby-veil" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
    <div className="lobby-sheet" role="dialog" aria-label="ルール設定">
      <h3>いまのルール</h3>
      <p className="lobby-sheet__name">{MODE_NAME[gameMode]}</p>
      <p className={`lobby-sheet__special ${special === 'none' ? 'is-none' : ''}`}>{SPECIAL_NAME[special]}</p>
      <div className="lobby-sheet__desc">
        <p>{MODE_DESC[gameMode]}</p>
        <p>{SPECIAL_DESC[special]}</p>
      </div>

      <div className="lobby-sheet__set">
        <div className="lobby-sheet__group">
          <b>戦い方</b>
          <div className="lobby-sheet__choices">
            {["0", "1"].map(mode => (
              <button key={mode} type="button" disabled={!canEdit}
                className={`fuda-btn ${gameMode === mode ? 'fuda-btn--on' : 'fuda-btn--off'}`}
                onClick={() => { if (gameMode !== mode) onGameMode(mode); }}>
                {MODE_SHORT[mode]}
              </button>
            ))}
          </div>
        </div>
        <div className="lobby-sheet__group">
          <b>特殊ルール</b>
          <div className="lobby-sheet__choices">
            {[["none", "なし"], ["lives", "残機制"], ["solo", "1 vs 3"]].map(([value, label]) => (
              <button key={value} type="button" disabled={!canEdit || (value === 'solo' && gameMode === '1')}
                className={`fuda-btn ${special === value ? 'fuda-btn--on' : 'fuda-btn--off'}`}
                onClick={() => { if (special !== value) onSpecial(value); }}>
                {label}
              </button>
            ))}
          </div>
        </div>
        <p className="lobby-sheet__note">{canEdit ? "" : "ルールを変えられるのはホストだけです"}</p>
      </div>

      <button type="button" className="fuda-btn lobby-sheet__close" onClick={onClose}>閉じる</button>
    </div>
  </div>
);

// 列の並び：自分をいつも左端に出し、ほかの人を席の番号順に続ける（空席はその後ろ）
const seatOrder = (players, myId) => {
  const me = players.find(p => p.playerId === myId);
  const others = players.filter(p => p.playerId !== myId).sort((a, b) => a.slotIndex - b.slotIndex);
  return me ? [me, ...others] : others;
};

// 退室した人の列を、剣が上へ抜けていくあいだだけ残しておく時間
const LEAVE_MS = 700;

// ==========================================
// ロビー画面本体
// ==========================================
// 1vs3（soloMode）だけは1人対3人が揃う必要があるので4人ちょうどを待つ。
export default function LobbyScreen({
  view, roomId, isCopied, handleCopyId,
  swordList, mySwordData, equipSword, reorderSwords,
  // 自分の剣の画像の選び方。プライバシーを設定した剣は、相手に見えているのと同じ加工済みの画像を出す
  imageOf = bladeOf,
  onReady, onGameMode, onLivesMode, onSoloMode, onBossPlayer, onSpin, onStart, onLeave, goToCrafting, error
}) {
  const [draggedIndex, setDraggedIndex] = useState(null);
  const [dragOverIndex, setDragOverIndex] = useState(null);
  const [transition, setTransition] = useState("enter");
  const [isRuleOpen, setIsRuleOpen] = useState(false);
  const [isLeaveOpen, setIsLeaveOpen] = useState(false);

  // ---- 入退室の演出 ----
  // 名簿が変わったら、前の名簿と比べて、入ってきた人（剣を上から降らせる）と出ていった人（剣を上へ抜けさせ、列をしばらく残す）を見つける。
  // この画面を開いた時点でいた人は降らせない（武器庫から戻っただけで全員が降ってこないように）
  const players = view?.room?.players;
  const [prevPlayers, setPrevPlayers] = useState(players);
  const [arrivals, setArrivals] = useState(() => new Set());
  const [leaving, setLeaving] = useState([]);
  if (players !== prevPlayers) {
    setPrevPlayers(players);
    const now = players ?? [], before = prevPlayers ?? [];
    const added = now.filter(p => !before.some(q => q.playerId === p.playerId));
    const gone = before.filter(p => p.playerId !== view?.localPlayerId && !now.some(q => q.playerId === p.playerId));
    if (added.length) setArrivals(set => new Set([...set, ...added.map(p => p.playerId)]));
    if (gone.length) {
      const order = seatOrder(before, view?.localPlayerId).map(p => p.playerId);
      setArrivals(set => new Set([...set].filter(id => !gone.some(p => p.playerId === id))));
      setLeaving(list => [...list, ...gone.map(p => ({ player: p, index: order.indexOf(p.playerId), key: `leaving-${p.playerId}-${list.length}` }))]);
    }
  }
  useEffect(() => {
    if (!leaving.length) return;
    const timer = setTimeout(() => setLeaving([]), LEAVE_MS);
    return () => clearTimeout(timer);
  }, [leaving]);

  // 入場時のアニメーションタイマー（350msでidleへ）
  useEffect(() => {
    const timer = setTimeout(() => {
      setTransition("idle");
    }, 350);
    return () => clearTimeout(timer);
  }, []);

  const room = view?.room;
  const me = room?.players.find(p => p.playerId === view.localPlayerId);
  if (!room || !me) return null;

  const isHost = view.isHost;
  const gameMode = room.gameMode;
  const livesMode = Boolean(room.livesMode);
  const soloMode = Boolean(room.soloMode);
  const special = soloMode ? 'solo' : livesMode ? 'lives' : 'none';
  const seatLimit = room.seatLimit ?? MAX_PLAYERS;
  const auto = Boolean(room.autoStart);
  // 1vs3 のボスは、ONにした時点ではホスト。ホストが指名し直せる。
  // ※ auto を参照するので、必ず auto の宣言より後に置くこと（前に置くと 1vs3 をONにした
  //   瞬間に初期化前アクセスで描画が落ちる）。
  const bossPlayerId = room.bossPlayerId ?? null;
  const canPickBoss = soloMode && isHost && !auto;
  const playerCount = room.players.length;
  const readyCount = room.players.filter(p => p.ready).length;
  const emptySlots = Array.from({ length: seatLimit }, (_, slot) => slot)
    .filter(slot => !room.players.some(p => p.slotIndex === slot));
  const seats = seatOrder(room.players, me.playerId).map(player => ({ slot: player.slotIndex, player }))
    .concat(emptySlots.map(slot => ({ slot, player: null })));
  // 出ていった人の列は、出ていく前の位置に差し込んで残す（列の数が変わらないよう、その分の空席を1つ減らす）
  leaving
    .filter(g => !room.players.some(p => p.playerId === g.player.playerId))
    .sort((a, b) => a.index - b.index)
    .forEach(g => {
      seats.splice(Math.min(g.index, seats.length), 0, { slot: g.player.slotIndex, player: g.player, leavingKey: g.key });
      const lastEmpty = seats.findLastIndex(seat => !seat.player);
      if (lastEmpty >= 0) seats.splice(lastEmpty, 1);
    });
  // 準備完了のあいだは、退室も武器の変更もできない。ランダムマッチ（auto）は準備完了の操作そのものがない。
  const locked = me.ready && !auto;
  const canChangeSword = !me.ready && !auto;
  // ホストが抜けるとロビーそのものがなくなるので、ホストのボタンは「ロビー解散」にする（ランダムマッチは誰がホストか見せないので退室のまま）
  const disbands = isHost && !auto;

  const autoStatus =
    room.startsInMs == null ? `対戦相手を待っています（あと${Math.max(0, MIN_PLAYERS - playerCount)}人で開始）`
    : room.startsInMs <= 10000 ? 'まもなく開始します'
    : `${Math.ceil(room.startsInMs / 1000)}秒以内に開始します`;

  // 1vs3 は4人ちょうどでしか成立しないので、不足している間は人数を出して待つ。
  const soloShortage = soloMode ? SOLO_MODE_PLAYERS - playerCount : 0;

  const status = auto ? autoStatus
    : soloShortage > 0 ? `1vs3 には${SOLO_MODE_PLAYERS}人必要です（あと${soloShortage}人）`
    : soloMode && !bossPlayerId ? 'ボスを指名してください'
    : playerCount < MIN_PLAYERS ? `参加者を待っています（最低${MIN_PLAYERS}人・最大${seatLimit}人）`
    : readyCount < playerCount ? `準備完了 ${readyCount} / ${playerCount}人`
    : room.swordsLoading ? 'メンバーの剣の情報を読み込んでいます'
    : isHost ? `${playerCount}人全員の準備が完了。対戦を開始できます`
    : `${playerCount}人全員の準備が完了。ホストの開始を待っています`;

  // 画面遷移（武器庫へ向かう際のフェードアウト）
  const handleGoToCrafting = (target) => {
    if (transition !== "enter" && transition !== "idle") return;
    setTransition("exit-back");
    setTimeout(() => goToCrafting(target), 300);
  };

  // 特殊ルールは「なし・残機制・1vs3」から一つ。残機制と1vs3はどちらかをONにすると、もう一方は自動で外れる。
  const handleSpecial = (value) => {
    if (value === 'lives') onLivesMode(true);
    else if (value === 'solo') onSoloMode(true);
    else if (livesMode) onLivesMode(false);
    else if (soloMode) onSoloMode(false);
  };

  // ドラッグ＆ドロップ並び替え処理
  const handleDragStart = (e, index) => {
    if (!swordList[index] || me.ready || auto) {
      e.preventDefault();
      return;
    }
    setDraggedIndex(index);
    e.dataTransfer.setData("text/plain", index.toString());
    e.dataTransfer.effectAllowed = "move";
  };

  const handleDragOver = (e, index) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
    if (dragOverIndex !== index) {
      setDragOverIndex(index);
    }
  };

  const handleDragLeave = () => {
    setDragOverIndex(null);
  };

  const handleDrop = (e, targetIndex) => {
    e.preventDefault();
    setDragOverIndex(null);

    const sourceIndexStr = e.dataTransfer.getData("text/plain");
    let sourceIndex = sourceIndexStr !== "" ? parseInt(sourceIndexStr, 10) : draggedIndex;

    if (
      sourceIndex === null ||
      sourceIndex === undefined ||
      isNaN(sourceIndex) ||
      me.ready ||
      auto
    ) {
      setDraggedIndex(null);
      return;
    }

    if (!swordList[sourceIndex]) {
      setDraggedIndex(null);
      return;
    }

    let actualTargetIndex = targetIndex;
    if (actualTargetIndex >= swordList.length) {
      actualTargetIndex = swordList.length - 1;
    }

    if (sourceIndex === actualTargetIndex) {
      setDraggedIndex(null);
      return;
    }

    const newList = [...swordList];
    const [movedItem] = newList.splice(sourceIndex, 1);
    newList.splice(actualTargetIndex, 0, movedItem);

    if (typeof reorderSwords === 'function') {
      reorderSwords(newList);
    }

    setDraggedIndex(null);
  };

  const handleDragEnd = () => {
    setDraggedIndex(null);
    setDragOverIndex(null);
  };

  // フェードイン・フェードアウト用アニメーションクラス
  const animClass = transition === "exit-back" ? "page-exit-back" :
                    transition === "exit-forward" ? "page-exit-forward" :
                    transition === "idle" ? "" : "page-enter-forward";

  // 自分の装備スロット：押して持ち替え、ドラッグで並び替え
  const renderMySlots = () => [0, 1, 2].map(index => {
    const sword = swordList[index];
    const isTarget = dragOverIndex === index;
    if (!sword) {
      return (
        <div
          key={`empty-${index}`}
          className={`lobby-slot is-empty ${isTarget ? 'is-target' : ''}`}
          data-n={index + 1}
          onDragOver={(e) => handleDragOver(e, index)}
          onDragLeave={handleDragLeave}
          onDrop={(e) => handleDrop(e, index)}
        >
          EMPTY
        </div>
      );
    }
    const isEquipped = mySwordData?.id === sword.id;
    return (
      <div
        key={sword.id}
        role="button"
        tabIndex={canChangeSword ? 0 : -1}
        aria-label={`剣${index + 1}に持ち替える`}
        className={`lobby-slot ${isEquipped ? 'is-equipped' : ''} ${canChangeSword ? 'is-usable' : ''} ${!canChangeSword && !isEquipped ? 'is-dim' : ''} ${draggedIndex === index ? 'is-dragging' : ''} ${isTarget ? 'is-target' : ''}`}
        data-n={index + 1}
        draggable={canChangeSword}
        onDragStart={(e) => handleDragStart(e, index)}
        onDragOver={(e) => handleDragOver(e, index)}
        onDragLeave={handleDragLeave}
        onDrop={(e) => handleDrop(e, index)}
        onDragEnd={handleDragEnd}
        onClick={() => { if (canChangeSword) equipSword(sword); }}
        onKeyDown={(e) => { if (canChangeSword && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); equipSword(sword); } }}
      >
        {isEquipped && <EnsoInk variant="rack" className="lobby-slot__enso" />}
        <img src={imageOf(sword)} alt="" draggable={false} />
      </div>
    );
  });

  const renderColumn = ({ slot, player, leavingKey }) => {
    if (!player) {
      return (
        <div key={`seat-${slot}`} className="lobby-col lobby-col--empty" style={{ '--pc': 'var(--kasure)' }}>
          <div className="lobby-col__tag"><span className="lobby-col__who">{slot + 1}P</span></div>
          <span className="lobby-col__wait">参加を待っています</span>
        </div>
      );
    }

    const isMe = !leavingKey && player.playerId === me.playerId;
    // 自分の剣は手元のデータ（柄の補正をここで足す）。ほかの人の剣は、補正済みの値が送られてくるのでそのまま出す。
    // 独楽モードは柄の補正を外して戦うので（HostRoom.js の swordForGameMode）、剣本体の値を出す。
    // 補正前の値を送ってこない古いクライアントの剣だけは、補正込みのまま出す（試合でもその値で戦う）。
    const sword = isMe && mySwordData ? mySwordData : player.swordData;
    const stats = gameMode === "1"
      ? (isMe && mySwordData ? getBaseStats(mySwordData) : sword.base ?? { hp: sword.hp, attack: sword.attack, weight: sword.weight })
      : isMe && mySwordData ? getFinalStats(mySwordData)
      : { hp: sword.hp, attack: sword.attack, weight: sword.weight };
    const isBoss = soloMode && player.playerId === bossPlayerId;
    const isPresent = player.connected && player.inLobby;
    const isReady = !auto && player.ready && isPresent;
    const waitLabel = !player.connected ? "切断" : !player.inLobby ? "結果\n確認中" : "準備\n中";
    // 名札は幅が限られるので、自分の列は「あなた」だけにする（自分がホストかどうかは対戦開始ボタンの有無で分かる）。
    const role = isMe ? "あなた" : player.playerId === 'p0' ? "ホスト" : "";
    const photo = isMe && mySwordData ? imageOf(sword) : bladeOf(sword);

    return (
      <div
        key={leavingKey ?? player.playerId}
        className={`lobby-col ${isMe ? 'is-me' : 'is-other'} ${isReady ? 'is-ready' : ''} ${isBoss ? 'is-boss' : ''} ${arrivals.has(player.playerId) && !leavingKey ? 'is-arriving' : ''} ${leavingKey ? 'is-leaving' : ''}`}
        aria-hidden={leavingKey ? true : undefined}
        style={{ '--pc': PLAYER_COLORS[player.slotIndex], '--seat': player.slotIndex }}
      >
        {isMe && <div className="lobby-col__light" aria-hidden="true" />}
        <div className="lobby-col__photo">{photo && <img src={photo} alt="" draggable={false} />}</div>

        <div className="lobby-col__tag">
          <span className="lobby-col__who">{player.slotIndex + 1}P{role && <small>{role}</small>}</span>
          <SwordName name={sword.name} />
        </div>

        {soloMode && (
          <div className="lobby-col__boss">
            {isBoss ? <span className="lobby-bossmark">ボス</span>
              : canPickBoss && (
                <button type="button" className="fuda-btn fuda-btn--sm" onClick={() => onBossPlayer(player.playerId)}>
                  ボスにする
                </button>
              )}
          </div>
        )}

        <div className="lobby-stage">
          <SwordFigure sword={sword} blade={photo} spinCount={view.spins?.[player.playerId] ?? 0} onSpin={isMe ? onSpin : undefined}
            hiltOff={gameMode === "1"} />
          <StatsPanel stats={stats} hidden={isBoss} />

          <div className="lobby-side">
            {auto ? (
              isMe && <span className="lobby-side__note">この装備で<br />参戦します</span>
            ) : (
              <>
                {isReady
                  ? <span key="done" className="lobby-seal lobby-seal--done">{"準備\n完了"}</span>
                  : <span key="wait" className="lobby-seal lobby-seal--wait">{waitLabel}</span>}
                {isMe && (
                  <button type="button" className="brush-link" onClick={() => onReady(!me.ready)}>
                    {me.ready ? "準備取消" : "準備完了"}
                  </button>
                )}
              </>
            )}
          </div>
        </div>

        <div className="lobby-row">
          {isMe ? (
            <div className="lobby-slots">{renderMySlots()}</div>
          ) : (
            <>
              <span className="lobby-count">
                剣{'\u3000'}<span>{swordCountOf(player.swordData)}</span> / 3
              </span>
              <span className="lobby-skills">
                {skillsOf(sword, gameMode, isBoss).map(skill => (
                  <span key={skill.skillName} className="skill__icon" title={skill.skillName}>
                    <img src={skill.skillIcon} alt={`必殺技：${skill.skillName}`} draggable={false} />
                  </span>
                ))}
              </span>
            </>
          )}
          {isMe && !auto && (
            <button
              type="button"
              className="fuda-btn"
              onClick={() => handleGoToCrafting("LOBBY")}
              disabled={me.ready}
              title={me.ready ? "準備完了のあいだは武器を変えられません" : undefined}
            >
              武器庫へ
            </button>
          )}
        </div>
      </div>
    );
  };

  return (
    // この画面は16:9の舞台（Stage16x9）の中に出る。上に錬成・武器庫と同じ見出しの帯、その下の残りの中央にロビーを置く
    <div className={`lobby-screen ${animClass}`}>
      <header className="lobby-header">
        <button
          type="button"
          className="brush-link"
          onClick={() => setIsLeaveOpen(true)}
          disabled={locked}
          title={locked ? `準備完了のあいだは${disbands ? "解散" : "退室"}できません` : undefined}
        >
          {disbands ? "ロビー解散" : "退室する"}
        </button>
        <h1>対戦ロビー</h1>
        <span className="lobby-header__count">人数{'\u3000'}<span>{playerCount}</span> / {seatLimit}</span>
      </header>

      <div className="lobby-main">
        <div className="lobby">

          <div className="lobby-cols" style={{ '--seats': seatLimit }}>
            {seats.map(renderColumn)}
          </div>

          {/* エラーは列のすぐ下（フッターの帯の上）に出す */}
          {error && (
            <p className="lobby-error" style={styles.errorMessage}>{error}</p>
          )}

          {/* フッター：左にルール、中央に対戦開始、右にロビーID */}
          <div className="lobby-bar">
            <div className="lobby-rule">
              <div className="lobby-rule__now">
                <small>ルール</small>
                <b>{MODE_SHORT[gameMode]}{special !== 'none' && `・${SPECIAL_NAME[special]}`}</b>
              </div>
              <button type="button" className="fuda-btn" onClick={() => setIsRuleOpen(true)}>ルール設定</button>
            </div>

            <div className="lobby-mid">
              {/* 対戦開始はホストだけ。ほかの人にも同じ大きさの見えないボタンを置き、帯の高さ（＝列の高さ）をホストとそろえる */}
              {isHost && !auto ? (
                <InkButton variant="shu" lit={view.canStart} onClick={onStart} disabled={!view.canStart}>
                  {soloMode ? "1 vs 3 で対戦開始" : `${playerCount}人で対戦開始`}
                </InkButton>
              ) : (
                <div className="lobby-mid__ghost" aria-hidden="true">
                  <InkButton variant="shu" disabled tabIndex={-1}>{soloMode ? "1 vs 3 で対戦開始" : `${playerCount}人で対戦開始`}</InkButton>
                </div>
              )}
              <p className="lobby-mid__status">{status}</p>
            </div>

            <div className="lobby-id">
              {auto ? (
                <div><small>対戦形式</small><b className="is-random">ランダムマッチ</b></div>
              ) : (
                <>
                  <div><small>ロビーID</small><b>{roomId || "----"}</b></div>
                  <button
                    type="button"
                    className={`fuda-btn lobby-copy ${isCopied ? 'fuda-btn--on' : ''}`}
                    onClick={handleCopyId}
                    aria-label={isCopied ? "コピーしました" : "ロビーIDをコピー"}
                  >
                    {isCopied
                      ? <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 8.5l4 4 8-9" /></svg>
                      : <svg viewBox="0 0 16 16" aria-hidden="true"><rect x="5.5" y="5.5" width="9" height="9" /><path d="M10.5 2.5h-9v9" /></svg>}
                    コピー
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      </div>

      {isLeaveOpen && (disbands ? (
        <ConfirmSheet title="ロビーを解散しますか？" confirmLabel="解散する"
          onConfirm={onLeave} onCancel={() => setIsLeaveOpen(false)}>
          参加している全員がロビーから退出します。
        </ConfirmSheet>
      ) : (
        <ConfirmSheet title="ロビーから退室しますか？" confirmLabel="退室する"
          onConfirm={onLeave} onCancel={() => setIsLeaveOpen(false)}>
          タイトルに戻ります。
        </ConfirmSheet>
      ))}

      {isRuleOpen && (
        <RuleSheet
          gameMode={gameMode}
          special={special}
          canEdit={isHost && !auto}
          onGameMode={onGameMode}
          onSpecial={handleSpecial}
          onClose={() => setIsRuleOpen(false)}
        />
      )}
    </div>
  );
}
