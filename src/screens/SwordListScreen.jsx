// src/screens/SwordListScreen.jsx
// 武器庫。上に持っている剣を刀掛けに並べ（選んだ剣は朱の円相の前で大きく）、下に選んだ剣の情報と設定。
// 刀掛け（見る所）／選んだ剣の情報（読む所：名前・装備の状態・ステータス・必殺技、後ろに自分の姿）／設定（触る所）の3つに分ける。
// 横長は16:9の舞台で、情報と設定（柄・プライバシー・手入れ）を左右に並べる。
// 縦長は手入れのボタンを画面の下に固定し、その上の情報と設定をスクロールさせる（下へ流すと、上端に小さな情報の帯が出る）。
// 剣はドラッグで並べ替えられる。「表示切替」で、刀掛けに剣ごとのステータスを出す
import { useEffect, useRef, useState } from 'react';
import Stage16x9 from '../components/Stage16x9.jsx';
import {
  CompactBar, ConfirmSheet, EnsoInk, HiltPicker, NameEditor, PrivacyPicker, RadarChart, SkillInfo, StatsList, SwordStand,
} from '../components/SwordForgeParts.jsx';
import {
  STAT_LABELS, baseStatsOf, bonusOf, hiltOf, totalsOf, useAnimatedValues, useCompactLevel, usePortrait, useStageScale,
} from '../components/swordForge.js';
import { privacyOf } from '../swordPrivacy.js';
import './SwordListScreen.css';

const MAX_SWORDS = 3;
const NAME_MAX = 15;
const OVERLAY_SCALE = 0.8;
// これより動いたらドラッグ（並べ替え）、それ未満なら押しただけ
const DRAG_THRESHOLD = 8;
// 縦長：これらが帯の後ろへ隠れた順に、上端の小さな情報の帯へ出す
const COMPACT_PARTS = ['.armory__name-row', '.armory__stats', '.armory__skill'];
const slotX = i => `${((i * 2 + 1) / 6 * 100).toFixed(3)}%`;
// 指を離した所が、3つに分けた刀掛けのどこに当たるか
const dropIndexOf = (from, dx, width, count) =>
  Math.max(0, Math.min(count - 1, Math.floor(((from * 2 + 1) / 6 * width + dx) / (width / 3))));

const CARE_ICONS = {
  flip: <path d="M12 3 V21 M8 7 L3 12 L8 17 Z M16 7 L21 12 L16 17 Z" />,
  recapture: <><path d="M4 8 H8 L10 5 H14 L16 8 H20 V19 H4 Z" /><circle cx="12" cy="13" r="3.5" /></>,
  view: <path d="M5 20 V12 M12 20 V5 M19 20 V14 M3 20 H21" />,
  delete: <path d="M4 7 H20 M9 7 V4 H15 V7 M6 7 L7 20 H17 L18 7 M10 11 V16 M14 11 V16" />,
};

// 必殺技のアイコンだけ（大きさは --skill-box）
const SkillIcon = ({ hiltType, className = '' }) => {
  const hilt = hiltOf(hiltType);
  return <span className={`skill__icon ${className}`}><img src={hilt.skillIcon} alt={hilt.skillName} /></span>;
};

export default function SwordListScreen({
  direction = "forward",
  swordList, mySwordData, equipSword, deleteSword, reorderSwords,
  startNewCrafting, startRecapture, updateSword, updatePrivacy, cancelList, toggleSwordFlip,
  // 戻る先（タイトルかロビーか）。左上のボタンに行き先を書く
  returnStep = 'TITLE',
}) {
  const portrait = usePortrait();
  const scale = useStageScale();
  const [pickedId, setSelectedId] = useState(mySwordData?.id || swordList[0]?.id);
  // 選んでいた剣が一覧から消えたら（破棄など）、先頭の剣を選んだことにする
  const selectedId = swordList.some(s => s.id === pickedId) ? pickedId : swordList[0]?.id;
  const [spins, setSpins] = useState({});
  const [guided, setGuided] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [drag, setDrag] = useState(null); // { id, from, dx, to }：並べ替えでつまんでいる剣
  const [transition, setTransition] = useState("enter");
  // ワンポイントの丸を刀掛けの剣で動かせるのは、プライバシーをいじっている間だけ
  // （プライバシーの札・大きさを触ると始まり、柄・手入れを触るか別の剣を選ぶと終わる）
  const [privOpen, setPrivOpen] = useState(false);
  // 表示切替：刀掛けに剣ごとのステータスを出す。開き直すたびに、いつもの表示から始まる
  const [overview, setOverview] = useState(false);
  const [nameEditing, setNameEditing] = useState(false);
  // 装備したときだけ「装備中」の札を押印のように押す
  const [stampId, setStampId] = useState(null);
  const [compactLevel, onScroll] = useCompactLevel(COMPACT_PARTS);
  const rackRef = useRef(null);
  const moveRef = useRef(null);
  const suppressRef = useRef(false);

  // 🌟 初回入場アニメーション完了後にクラスを解除し、リスト更新（破棄等）時のフェードインを防止
  useEffect(() => {
    const timer = setTimeout(() => setTransition("idle"), 350);
    return () => clearTimeout(timer);
  }, []);

  const selected = swordList.find(s => s.id === selectedId);
  const hiltType = selected?.hiltType || '0';
  const totals = totalsOf(selected, hiltType);
  const shown = useAnimatedValues(totals);

  if (!selected) return null;

  const base = baseStatsOf(selected);
  const bonus = bonusOf(hiltType);
  const privacy = privacyOf(selected);
  const editingPrivacy = privOpen && privacy.mode === 'onepoint' && !overview;
  const isEquipped = mySwordData?.id === selected.id;
  const canCraft = swordList.length < MAX_SWORDS;
  const isLast = swordList.length <= 1;

  const go = (next, action) => {
    if (transition !== "enter" && transition !== "idle") return;
    setTransition(next);
    setTimeout(action, 300);
  };
  const onCancel = () => go("exit-back", cancelList);
  const onNewCrafting = () => { if (canCraft) go("exit-forward", startNewCrafting); };

  const endPriv = () => setPrivOpen(false);
  const choose = id => {
    setSelectedId(id);
    setPrivOpen(false);
    setStampId(null);
  };
  const spin = id => setSpins(prev => ({ ...prev, [id]: (prev[id] ?? 0) + 1 }));
  // 案内の点線は、剣に触った時点で消す
  const onSpotStart = () => setGuided(true);
  const onSpotChange = spot => updatePrivacy(selected.id, { mode: privacy.mode, spot });
  const onPrivacy = next => { setPrivOpen(true); updatePrivacy(selected.id, next); };
  const onHilt = id => { endPriv(); updateSword(selected.id, { hiltType: id }); };
  const onEquip = () => { equipSword(selected); setStampId(selected.id); };
  const onDelete = () => {
    setTransition("idle");
    deleteSword(selected.id);
    setConfirmDelete(false);
  };

  const careButtons = [
    { id: 'flip', title: '左右反転', desc: '剣の見た目を左右反転させます', onClick: () => { endPriv(); toggleSwordFlip(selected.id); } },
    { id: 'recapture', title: '撮り直し', desc: '名前などは変えずに、剣を作り直します', onClick: () => { endPriv(); go("exit-forward", () => startRecapture(selected.id)); } },
    {
      id: 'view', title: '表示切替', pressed: overview,
      desc: overview ? '通常表示モードに切り替えます' : '詳細表示モードに切り替えます',
      onClick: () => { endPriv(); setOverview(v => !v); },
    },
    // 取り消せない操作なので、破棄だけは朱のまま
    { id: 'delete', title: '破棄する', danger: true, disabled: isLast, desc: isLast ? '最後の1本は破棄できません' : 'この剣を削除します', onClick: () => { endPriv(); setConfirmDelete(true); } },
  ];
  const care = withDesc => careButtons.map(b => (
    <button key={b.id} type="button" onClick={b.onClick} disabled={b.disabled} aria-pressed={b.id === 'view' ? b.pressed : undefined}
      className={`armory__care forge-frame ${b.pressed ? 'is-pressed' : ''} ${b.danger ? 'is-danger' : ''}`}>
      <svg viewBox="0 0 24 24" aria-hidden="true">{CARE_ICONS[b.id]}</svg>
      {withDesc
        ? <span className="armory__care-text"><span className="armory__care-title">{b.title}</span><span className="armory__care-desc">{b.desc}</span></span>
        : b.title}
    </button>
  ));

  // ---- 並べ替え（つまんで左右へ動かす） ----
  const onMoveStart = (e, sword, index) => {
    if (editingPrivacy) return;
    const rack = rackRef.current;
    const rect = rack.getBoundingClientRect();
    moveRef.current = {
      id: sword.id, from: index, sx: e.clientX, sy: e.clientY,
      width: rect.width, k: rect.width / rack.offsetWidth || 1, moved: false, pointerId: e.pointerId, el: e.currentTarget, dx: 0,
    };
  };
  const onMoveDrag = e => {
    const m = moveRef.current;
    if (!m) return;
    const dx = e.clientX - m.sx, dy = e.clientY - m.sy;
    if (!m.moved && Math.hypot(dx, dy) / m.k < DRAG_THRESHOLD) return;
    if (!m.moved) {
      m.moved = true;
      try { m.el.setPointerCapture(m.pointerId); } catch { /* 取れなくても動かせる */ }
    }
    m.dx = dx;
    setDrag({ id: m.id, from: m.from, dx: dx / m.k, to: dropIndexOf(m.from, dx, m.width, swordList.length) });
  };
  const onMoveEnd = () => {
    const m = moveRef.current;
    moveRef.current = null;
    if (!m?.moved) return;
    // ドラッグのあとに続けて届く「押した」は無視する
    suppressRef.current = true;
    setTimeout(() => { suppressRef.current = false; }, 0);
    const to = dropIndexOf(m.from, m.dx, m.width, swordList.length);
    setDrag(null);
    if (to === m.from) return;
    const next = swordList.filter(s => s.id !== m.id);
    next.splice(to, 0, swordList[m.from]);
    reorderSwords(next);
  };

  const selectedIndex = swordList.findIndex(s => s.id === selected.id);
  const equippedId = mySwordData?.id;
  const eqTag = cls => <span className={`armory__eq-tag ${cls}`}>装備中</span>;

  // 表示切替のときに、剣ごとに重ねる情報の窓（横長は名前・レーダー・数字・必殺技、縦長は数字と必殺技のアイコン）
  const card = (sword, index) => {
    const t = totalsOf(sword);
    const numbers = (
      <span className="armory__card-stats">
        {STAT_LABELS.map((label, i) => (
          <span key={label} className="armory__card-stat"><span className="armory__card-label">{label}</span><span className="armory__card-total">{t[i]}</span></span>
        ))}
      </span>
    );
    return (
      <div key={sword.id} className="armory__card" style={{ left: slotX(index) }} aria-label={`${sword.name}のステータス`}>
        {portrait ? (
          <>
            {sword.id === equippedId && eqTag('is-card')}
            {numbers}
            <SkillIcon hiltType={sword.hiltType} className="armory__card-icon" />
          </>
        ) : (
          <>
            <span className="armory__card-name">{sword.name}{sword.id === equippedId && eqTag('is-card')}</span>
            <span className="armory__card-row">
              <RadarChart base={baseStatsOf(sword)} shown={t} totals={t} className="armory__card-radar" />
              {numbers}
            </span>
            <span className="armory__card-skill">
              <SkillIcon hiltType={sword.hiltType} className="armory__card-icon" />
              {hiltOf(sword.hiltType).skillName}
            </span>
          </>
        )}
      </div>
    );
  };

  const rack = (
    <div className="armory__rack">
      <div className="armory__rack-inner" ref={rackRef}>
        <EnsoInk variant="rack" className="armory__enso" style={{ left: slotX(selectedIndex) }} />
        {/* 並べ替えの置き先：番号の朱の輪に加えて、自分の元の場所以外に重ねたら、その場所の剣の後ろに薄い朱の円相 */}
        {drag && drag.to !== drag.from && <EnsoInk variant="rack" className="armory__enso is-ghost" style={{ left: slotX(drag.to) }} />}
        <span className="inkline armory__ground" aria-hidden="true" />

        {swordList.map((sword, index) => {
          const on = sword.id === selected.id;
          const dragging = drag?.id === sword.id;
          const edit = on && editingPrivacy;
          const press = () => {
            if (suppressRef.current) return;
            if (!on) choose(sword.id);
            else if (!overview) spin(sword.id);
          };
          const size = overview ? 'var(--ov-scale)' : on ? 1 : 'var(--off-scale)';
          return (
            <div
              key={sword.id}
              className={`armory__item ${on ? 'is-on' : ''} ${dragging ? 'is-dragging' : ''} ${editingPrivacy ? '' : 'is-movable'}`}
              style={{ left: slotX(index), transform: `${dragging ? `translateX(${drag.dx.toFixed(1)}px) ` : ''}scale(${size})` }}
              onPointerDown={e => onMoveStart(e, sword, index)}
              onPointerMove={onMoveDrag}
              onPointerUp={onMoveEnd}
              onPointerCancel={onMoveEnd}
            >
              <SwordStand
                imageSrc={sword.imageSrc}
                hiltType={sword.hiltType || '0'}
                privacy={sword.privacy}
                editable={edit}
                onSpotStart={onSpotStart}
                onSpotChange={onSpotChange}
                onPress={press}
                spin={spins[sword.id] ?? 0}
                showGuide={edit && !guided}
                label={on ? (overview ? sword.name : '剣を回す') : `${sword.name}を選ぶ`}
                badge={sword.id === equippedId && !overview ? eqTag('') : null}
              />
            </div>
          );
        })}
        {swordList.map((sword, index) => (
          <span key={sword.id} className={`armory__item-name ${sword.id === selected.id ? 'is-on' : ''}`} style={{ left: slotX(index) }}>{sword.name}</span>
        ))}

        {/* 地面に書く番号（空きの番号も出す）。選んでいる剣は朱の丸、並べ替えの置き先は朱の輪 */}
        {[0, 1, 2].map(i => {
          const sword = swordList[i];
          const on = sword && sword.id === selected.id && !drag;
          return (
            <span key={i} aria-hidden="true" style={{ left: slotX(i) }}
              className={`armory__num ${sword ? '' : 'is-empty'} ${on ? 'is-on' : ''} ${drag && drag.to === i ? 'is-target' : ''}`}>
              {i + 1}
            </span>
          );
        })}

        {overview && swordList.map(card)}

        {canCraft && (
          <button type="button" className="armory__new" style={{ left: slotX(swordList.length) }} onClick={onNewCrafting} aria-label="新しい剣を錬成する">
            <span className="armory__new-circle"><svg viewBox="0 0 20 20" aria-hidden="true"><path d="M10 3v14M3 10h14" /></svg></span>
            新しく錬成する
          </button>
        )}
      </div>
    </div>
  );

  const header = (
    <div className="armory__header">
      <button type="button" className="forge-link armory__back" onClick={onCancel}>{returnStep === 'LOBBY' ? 'ロビーに戻る' : 'タイトルに戻る'}</button>
      <h1>武器庫</h1>
      <span className="armory__count">所持{portrait ? ' ' : '　'}<span>{swordList.length}</span>{portrait ? '/' : ' / '}3</span>
    </div>
  );

  // 背景の自分の姿（プライバシーに関係なく元の写真）。左右反転は、見本の剣と一緒にすぐ映す
  const figure = (
    <div className="forge-figure armory__figure" aria-hidden="true">
      <img src={selected.imageSrc} alt="" draggable={false} />
    </div>
  );

  // 装備の札。「装備中」「控え」は同じ大きさ。控えのときは真下に下線付きの「装備する」。札かボタンを押すと装備する
  const equip = !nameEditing && (
    <div className="armory__equip">
      {isEquipped
        ? <span role="status" className={`armory__badge is-eq ${stampId === selected.id ? 'is-stamped' : ''}`}>装備中</span>
        : (
          <>
            <button type="button" className="armory__badge is-spare" onClick={onEquip} aria-label="控え（押すとこの剣を装備します）">控え</button>
            <button type="button" className="forge-link armory__equip-link" onClick={onEquip}>装備する</button>
          </>
        )}
    </div>
  );

  const info = (
    <div className="armory__info">
      {!portrait && figure}
      <div className="armory__info-body">
        {equip}
        <div className={`armory__name-row ${nameEditing ? 'is-editing' : ''}`}>
          <NameEditor
            key={selected.id}
            name={selected.name}
            onSave={name => updateSword(selected.id, { name })}
            maxLen={NAME_MAX}
            fitMax={portrait ? 40 : 52}
            fitLines={portrait ? 2 : 1}
            overlay={!portrait && scale < OVERLAY_SCALE}
            className="armory__name"
            compact
            iconOnly={portrait}
            onEditingChange={setNameEditing}
          />
        </div>
        <div className="armory__stats">
          <RadarChart base={base} shown={shown} totals={totals} />
          <StatsList base={base} bonus={bonus} shown={shown} />
        </div>
        <SkillInfo hiltType={hiltType} className="armory__skill" />
      </div>
    </div>
  );

  const hiltSection = (
    <section className="armory__section" onPointerDown={endPriv}>
      <div className="forge-label">柄</div>
      <HiltPicker hiltType={hiltType} onChange={onHilt} />
    </section>
  );
  const privacySection = (
    <section className="armory__section">
      <div className="forge-label">プライバシー</div>
      <PrivacyPicker privacy={selected.privacy} onChange={onPrivacy} thumbSrc={selected.imageSrc} />
    </section>
  );

  const animClass = transition === "exit-back" ? "page-exit-back" :
    transition === "exit-forward" ? "page-exit-forward" :
      transition === "idle" ? "" :
        (direction === "back" ? "page-enter-back" : "page-enter-forward");
  const rootClass = `forge forge-quiet armory ${overview ? 'is-overview' : ''} ${animClass}`;

  const sheet = confirmDelete && (
    <ConfirmSheet title="剣を破棄しますか？" confirmLabel="破棄する" onConfirm={onDelete} onCancel={() => setConfirmDelete(false)}>
      「<strong>{selected.name}</strong>」を破棄します。<br />この操作は取り消せません。
    </ConfirmSheet>
  );

  if (portrait) {
    return (
      <div className={`${rootClass} armory--port`}>
        {figure}
        {header}
        {rack}
        <div className={`forge-scroll-wrap ${compactLevel ? `is-compact-${compactLevel}` : ''}`}>
          <div className="forge-scroll" onScroll={onScroll}>
            {info}
            <div className="armory__settings">
              <span className="inkline forge-ground" aria-hidden="true" />
              {hiltSection}
              {privacySection}
            </div>
          </div>
          {/* 下へスクロールして大きな情報の欄が流れたら、隠れた所から順に、上端の小さな情報の帯へ出す（名前・ステータスの数字・必殺技のアイコン） */}
          <CompactBar name={selected.name} shown={shown} hiltType={hiltType} level={compactLevel} />
        </div>
        {/* 手入れのボタンは画面の下に固定して、いつも出しておく */}
        <div className="armory__care-bar forge-bottom-bar">{care(false)}</div>
        {sheet}
      </div>
    );
  }

  return (
    <Stage16x9>
      <div className={`${rootClass} armory--land`}>
        {header}
        {rack}
        <div className="armory__body">
          {info}
          <div className="armory__settings">
            <div className="armory__settings-grid">
              {hiltSection}
              {privacySection}
            </div>
            <div className="armory__care-grid forge-rule">{care(true)}</div>
          </div>
        </div>
      </div>
      {sheet}
    </Stage16x9>
  );
}
