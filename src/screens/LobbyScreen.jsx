import React, { useState, useEffect } from 'react';
import { PLAYER_COLORS, styles, swordImageSource } from '../styles';
import { MAX_PLAYERS, MIN_PLAYERS, SOLO_MODE_PLAYERS } from '../network/HostRoom';
import { HILT_DATABASE } from './SwordListScreen';
import InkButton from '../components/InkButton.jsx';
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

const hiltOf = sword => HILT_DATABASE[sword?.hiltType || "0"] || HILT_DATABASE["0"];
const bladeOf = sword => sword?.imageSrc || swordImageSource(sword);

// ルールの名前と説明。説明は以前のロビー画面の文言をそのまま使っている。
const MODE_NAME = { "0": "剣（横視点・重力）モード", "1": "独楽（見下ろし）モード" };
const MODE_SHORT = { "0": "剣", "1": "独楽" };
const MODE_DESC = { "0": "剣を振り回して戦うモード", "1": "独楽のようにぶつかり合う半自動戦闘モード" };
const SPECIAL_NAME = { none: "特殊ルールなし", lives: "残機制", solo: "1 vs 3" };
const SPECIAL_DESC = {
  none: "",
  lives: "所持している剣がすべて撃破されるまで敗北しません",
  solo: "1人（ボス）対3人。3人側は同士討ちしません。",
};

// ==========================================
// レーダーチャート（HP・攻・重の三角形）
// ==========================================
// 上限は錬成で出る値の範囲（HP 100〜1000 / 攻・重 1〜100）。柄の補正で超えた分は外周で止める。
const RADAR_MAX = { hp: 1000, attack: 100, weight: 100 };
const RADAR_AXES = [[0, -1], [0.866, 0.5], [-0.866, 0.5]]; // 上：HP　右下：攻　左下：重

const RadarChart = ({ stats }) => {
  const cx = 59, cy = 56, radius = 34;
  const point = (i, scale) =>
    `${(cx + RADAR_AXES[i][0] * radius * scale).toFixed(1)},${(cy + RADAR_AXES[i][1] * radius * scale).toFixed(1)}`;
  const ring = scale => [0, 1, 2].map(i => point(i, scale)).join(' ');
  const values = [stats.hp / RADAR_MAX.hp, stats.attack / RADAR_MAX.attack, stats.weight / RADAR_MAX.weight]
    .map(v => Math.max(0.04, Math.min(1, v)));

  return (
    <svg className="lobby-radar" viewBox="0 0 118 100" role="img"
      aria-label={`HP ${stats.hp}、攻 ${stats.attack}、重 ${stats.weight}`}>
      <polygon className="lobby-radar__axis" points={ring(1)} />
      <polygon className="lobby-radar__axis" points={ring(0.5)} />
      {[0, 1, 2].map(i => {
        const [x, y] = point(i, 1).split(',');
        return <line key={i} className="lobby-radar__axis" x1={cx} y1={cy} x2={x} y2={y} />;
      })}
      <polygon className="lobby-radar__value" points={[0, 1, 2].map(i => point(i, values[i])).join(' ')} />
      <text x="59" y="12" textAnchor="middle">HP <tspan>{stats.hp}</tspan></text>
      <text x="96" y="92" textAnchor="middle">攻 <tspan>{stats.attack}</tspan></text>
      <text x="22" y="92" textAnchor="middle">重 <tspan>{stats.weight}</tspan></text>
    </svg>
  );
};

// ==========================================
// 🌀 柄を合成した剣（押すと回る）
// ==========================================
// spinCount は「この剣が回された回数」で、持ち主が回すたびに全員の画面で1ずつ増える。
// 回数を key にして、増えるたびに回転のアニメーションを最初からやり直させる。
// 画面を開いた時点の回数（base）では回さない（武器庫から戻っただけで回り出さないように）。
// onSpin があるのは自分の剣だけ。ほかの人の剣は押せない。
const SwordFigure = ({ sword, spinCount, onSpin }) => {
  const [base] = useState(spinCount);
  const blade = bladeOf(sword);
  const Body = onSpin ? 'button' : 'div';
  return (
    <div className="lobby-sword">
      <div className="lobby-sword__float">
        <Body
          key={spinCount}
          {...(onSpin ? { type: 'button', onClick: onSpin, title: 'クリックで回転' } : {})}
          className={`lobby-sword__body ${spinCount !== base ? 'is-spinning' : ''}`}
        >
          {blade && <img className="lobby-sword__blade" src={blade} alt="Blade" draggable={false} />}
          <img className="lobby-sword__hilt" src={hiltOf(sword).imageSrc} alt="Hilt" draggable={false} />
        </Body>
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
              <button key={value} type="button" disabled={!canEdit}
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

// ==========================================
// ロビー画面本体
// ==========================================
// 1vs3（soloMode）だけは1人対3人が揃う必要があるので4人ちょうどを待つ。
export default function LobbyScreen({
  view, roomId, isCopied, handleCopyId,
  swordList, mySwordData, equipSword, reorderSwords,
  onReady, onGameMode, onLivesMode, onSoloMode, onBossPlayer, onSpin, onStart, onLeave, goToCrafting, error
}) {
  const [draggedIndex, setDraggedIndex] = useState(null);
  const [dragOverIndex, setDragOverIndex] = useState(null);
  const [transition, setTransition] = useState("enter");
  const [isRuleOpen, setIsRuleOpen] = useState(false);

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
  // 列の並び：自分をいつも左端に出し、ほかの人を席の番号順に続ける。空席はその後ろ。
  const others = room.players.filter(p => p.playerId !== me.playerId).sort((a, b) => a.slotIndex - b.slotIndex);
  const emptySlots = Array.from({ length: seatLimit }, (_, slot) => slot)
    .filter(slot => !room.players.some(p => p.slotIndex === slot));
  const seats = [me, ...others].map(player => ({ slot: player.slotIndex, player }))
    .concat(emptySlots.map(slot => ({ slot, player: null })));
  // 準備完了のあいだは、退室も武器の変更もできない。ランダムマッチ（auto）は準備完了の操作そのものがない。
  const locked = me.ready && !auto;
  const canChangeSword = !me.ready && !auto;

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
        <img src={sword.imageSrc} alt="" draggable={false} />
      </div>
    );
  });

  // ほかの人の装備スロット：見るだけ
  const renderOtherSlots = (swordData) => [0, 1, 2].map(index => {
    const slot = swordData.swords?.[index];
    if (!slot || slot.isEmpty) {
      return <div key={index} className="lobby-slot is-empty" data-n={index + 1}>EMPTY</div>;
    }
    const isEquipped = (swordData.equippedIndex ?? 0) === index;
    return (
      <div key={index} className={`lobby-slot ${isEquipped ? 'is-equipped' : 'is-dim'}`} data-n={index + 1}>
        {slot.imageStr && <img src={swordImageSource(slot)} alt="" draggable={false} />}
      </div>
    );
  });

  const renderColumn = ({ slot, player }) => {
    if (!player) {
      return (
        <div key={`seat-${slot}`} className="lobby-col lobby-col--empty" style={{ '--pc': 'var(--kasure)' }}>
          <div className="lobby-col__tag"><span className="lobby-col__who">{slot + 1}P</span></div>
          <span className="lobby-col__wait">参加を待っています</span>
        </div>
      );
    }

    const isMe = player.playerId === me.playerId;
    // 自分の剣は手元のデータ（柄の補正をここで足す）。ほかの人の剣は、補正済みの値が送られてくるのでそのまま出す。
    const sword = isMe && mySwordData ? mySwordData : player.swordData;
    const stats = isMe && mySwordData ? getFinalStats(mySwordData)
      : { hp: sword.hp, attack: sword.attack, weight: sword.weight };
    const isBoss = soloMode && player.playerId === bossPlayerId;
    const isPresent = player.connected && player.inLobby;
    const isReady = !auto && player.ready && isPresent;
    const waitLabel = !player.connected ? "切断" : !player.inLobby ? "結果\n確認中" : "準備\n中";
    // 名札は幅が限られるので、自分の列は「あなた」だけにする（自分がホストかどうかは対戦開始ボタンの有無で分かる）。
    const role = isMe ? "あなた" : player.playerId === 'p0' ? "ホスト" : "";
    const photo = bladeOf(sword);

    return (
      <div
        key={player.playerId}
        className={`lobby-col ${isMe ? 'is-me' : ''} ${isReady ? 'is-ready' : ''} ${isBoss ? 'is-boss' : ''}`}
        style={{ '--pc': PLAYER_COLORS[player.slotIndex], '--seat': player.slotIndex }}
      >
        <div className="lobby-col__photo">{photo && <img src={photo} alt="" draggable={false} />}</div>

        <div className="lobby-col__tag">
          <span className="lobby-col__who">{player.slotIndex + 1}P{role && <small>{role}</small>}</span>
          <p className="lobby-col__name">{sword.name}</p>
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
          <SwordFigure sword={sword} spinCount={view.spins?.[player.playerId] ?? 0} onSpin={isMe ? onSpin : undefined} />
          <RadarChart stats={stats} />

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
          <div className="lobby-slots">
            {isMe ? renderMySlots() : renderOtherSlots(player.swordData)}
          </div>
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
    <div className={animClass} style={{ ...styles.container, padding: '20px 16px' }}>
      <div className="lobby">

        <div className="lobby-top">
          <button
            type="button"
            className="brush-link"
            onClick={onLeave}
            disabled={locked}
            title={locked ? "準備完了のあいだは退室できません" : undefined}
          >
            退室する
          </button>
          <h2>対戦ロビー</h2>
          <span></span>
        </div>

        <div className="lobby-cols" style={{ '--seats': seatLimit }}>
          {seats.map(renderColumn)}
        </div>

        <div className="lobby-bar">
          <div className="lobby-rule">
            <div className="lobby-rule__now">
              <small>ルール</small>
              <b>{MODE_SHORT[gameMode]}{special !== 'none' && `・${SPECIAL_NAME[special]}`}</b>
            </div>
            <button type="button" className="fuda-btn" onClick={() => setIsRuleOpen(true)}>ルール設定</button>
          </div>

          <div className="lobby-mid">
            {isHost && !auto && (
              <InkButton variant="shu" lit={view.canStart} onClick={onStart} disabled={!view.canStart}>
                {soloMode ? "1 vs 3 で対戦開始" : `${playerCount}人で対戦開始`}
              </InkButton>
            )}
            <p className="lobby-mid__status">
              現在 {playerCount}人 ／ 最大 {seatLimit}人　{status}
            </p>
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

        {error && (
          <p className="lobby-error" style={styles.errorMessage}>{error}</p>
        )}
      </div>

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
