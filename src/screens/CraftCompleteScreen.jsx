// 錬成完了画面。横長は16:9の舞台（左に剣、右に名前・ステータス・必殺技・設定）、縦長は上から順に積む。
// 名前・ステータスの後ろ（縦長は画面全体）に自分の姿を大きく薄く敷き、設定とボタンは墨の筆跡で区切って控えめに見せる。
// 縦長は武器庫と同じく、見本の下（情報 → 柄 → プライバシー）をスクロールさせ、下へ流すと上端に小さな情報の帯を出す。
// 登場の演出：黒い影の刀身と柄が合わさる → その瞬間に影が消えて姿が出て、円相が描かれ、光り、剣が弾み、墨が飛び散る →
// 名前とステータスが左から入り、名前を書き、数字を数え上げる → 設定とボタンが出る
import { useEffect, useRef, useState } from 'react';
import InkButton from '../components/InkButton.jsx';
import Stage16x9 from '../components/Stage16x9.jsx';
import {
  CompactBar, EnsoInk, HiltPicker, InkBurst, NameEditor, PrivacyPicker, RadarChart, SkillInfo, StatsList, SwordStand,
} from '../components/SwordForgeParts.jsx';
import {
  baseStatsOf, bonusOf, prefersReducedMotion, totalsOf, useAnimatedValues, useCompactLevel, usePortrait, useStageScale,
} from '../components/swordForge.js';
import { privacyOf } from '../swordPrivacy.js';
import './CraftCompleteScreen.css';

const INTRO_MS = 2300;
const NAME_MAX = 15;
// 舞台がこれより小さく縮んでいるときは、名前の書き換え欄を画面の上端に等倍で出す
const OVERLAY_SCALE = 0.8;
// 縦長：これらが帯の後ろへ隠れた順に、上端の小さな情報の帯へ出す
const COMPACT_PARTS = ['.forge-done__name', '.forge-done__stats', '.forge-done__skill'];

export default function CraftCompleteScreen({
  sword, updateSword, updatePrivacy, setStep, startNewCrafting, startRecapture, toggleSwordFlip, craftReturnStep, swordListLength,
  // false のときは完成の演出をせず、ふつうに画面を出す（撮り直しを取りやめて戻ってきたとき）
  intro = true,
}) {
  const portrait = usePortrait();
  const scale = useStageScale();
  const [playing, setPlaying] = useState(() => intro && !prefersReducedMotion());
  const [transition, setTransition] = useState('enter');
  const [compactLevel, onScroll] = useCompactLevel(COMPACT_PARTS);
  const [spin, setSpin] = useState(0);
  const [guided, setGuided] = useState(false);
  const ensoRef = useRef(null);

  useEffect(() => {
    if (!playing) return;
    const timer = setTimeout(() => setPlaying(false), INTRO_MS);
    return () => clearTimeout(timer);
  }, [playing]);

  const hiltType = sword?.hiltType || '0';
  const base = baseStatsOf(sword);
  const bonus = bonusOf(hiltType);
  const totals = totalsOf(sword, hiltType);
  // 演出では 0 から数え上げる。柄を替えたときは今の値から動かす
  const shown = useAnimatedValues(totals, {
    from: playing ? [0, 0, 0] : null,
    delay: playing ? 600 : 0,
    duration: playing ? 900 : 450,
  });

  if (!sword) return null;

  const privacy = privacyOf(sword);
  const onepoint = privacy.mode === 'onepoint';
  // ワンポイントのときは、見本の剣を押す・なぞると丸が動く（プライバシーの設定はいつも見える所にある）
  const editable = onepoint;
  const showGuide = editable && !guided && !playing;

  const go = (next, action) => {
    if (transition !== 'enter') return;
    setTransition(next);
    setTimeout(action, 300);
  };
  const onReturn = () => go('exit-back', () => setStep(craftReturnStep));
  const onArmory = () => go('exit-forward', () => setStep('SWORD_LIST'));
  const onAnother = () => go('exit-forward', startNewCrafting);
  const onRecapture = () => go('exit-forward', () => startRecapture(sword.id));
  const onFlip = () => toggleSwordFlip(sword.id);
  // 出ていくときは、ほかの画面と同じく横へずれながら消える。演出なしで戻ってきたとき（撮り直しの取りやめ）は、左から入る
  const exitClass = transition === 'exit-back' ? 'page-exit-back' : transition === 'exit-forward' ? 'page-exit-forward'
    : !intro ? 'page-enter-back' : '';

  // 案内は、剣に触った時点で消す
  const onSpotStart = () => setGuided(true);
  const onSpotChange = spot => updatePrivacy(sword.id, { mode: privacy.mode, spot });
  // ワンポイントの丸は、演出の間は出さずに姿をそのまま見せ、演出が終わってから出す
  const shownPrivacy = playing && onepoint ? { ...privacy, mode: 'none' } : sword.privacy;
  const onPrivacy = next => updatePrivacy(sword.id, next);
  const onHilt = id => updateSword(sword.id, { hiltType: id });
  const onName = name => updateSword(sword.id, { name });

  const stage = (
    <div className="forge-done__stage">
      <div className="forge-done__enso" ref={ensoRef}><EnsoInk variant="craft" draw={playing} /></div>
      {playing && <InkBurst originRef={ensoRef} delay={560} />}
      <div className="forge-done__sword">
        <SwordStand
          imageSrc={sword.imageSrc}
          hiltType={hiltType}
          privacy={shownPrivacy}
          editable={editable && !playing}
          onSpotStart={onSpotStart}
          onSpotChange={onSpotChange}
          onPress={() => setSpin(n => n + 1)}
          spin={spin}
          float={!playing}
          shadow={playing}
          showGuide={showGuide}
        />
      </div>
      <button type="button" className="forge-done__tool forge-brush-under forge-done__flip intro-late" onClick={onFlip}>
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3 V21 M8 7 L3 12 L8 17 Z M16 7 L21 12 L16 17 Z" /></svg>
        左右反転
      </button>
      <button type="button" className="forge-done__tool forge-brush-under forge-done__recapture intro-late" onClick={onRecapture}>
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 8 H8 L10 5 H14 L16 8 H20 V19 H4 Z" /><circle cx="12" cy="13" r="3.5" /></svg>
        撮り直す
      </button>
      {playing && <div className="forge-done__flash" aria-hidden="true" />}
    </div>
  );

  const nameEditor = (
    <NameEditor
      name={sword.name}
      onSave={onName}
      maxLen={NAME_MAX}
      fitMax={portrait ? 44 : 64}
      iconOnly={portrait}
      overlay={!portrait && scale < OVERLAY_SCALE}
      className="forge-done__name intro-slide"
      buttonClassName="intro-late"
    />
  );

  const stats = (
    <div className="forge-done__stats intro-slide">
      <RadarChart base={base} shown={shown} totals={totals} />
      <StatsList base={base} bonus={bonus} shown={shown} />
    </div>
  );

  const privacyPicker = <PrivacyPicker privacy={sword.privacy} onChange={onPrivacy} thumbSrc={sword.imageSrc} />;
  const hiltPicker = <HiltPicker hiltType={hiltType} onChange={onHilt} />;
  const returnLabel = craftReturnStep === 'TITLE' ? 'タイトルに戻って対戦！' : 'ロビーに戻って対戦！';
  // もう1本錬成できるのは、剣が3本そろっていないとき（撮り直しのあとでも出す）
  const canCraftAnother = swordListLength < 3;
  const links = (
    <>
      <button type="button" className="forge-link" onClick={onArmory}>武器庫へ</button>
      {canCraftAnother && <button type="button" className="forge-link" onClick={onAnother}>もう1本錬成する</button>}
    </>
  );
  const rootClass = `forge forge-quiet forge-done ${playing ? 'is-intro' : ''} ${exitClass}`;
  // 背景の自分の姿。自分の画面にだけ出すものなので、プライバシーの設定に関係なく元の写真を使う。左右反転は、見本の剣と一緒にすぐ映す
  const figure = (
    <div className="forge-figure forge-done__figure" aria-hidden="true">
      <img src={sword.imageSrc} alt="" draggable={false} />
    </div>
  );
  // ステータスと設定の境目に引く墨の筆跡（武器庫の刀掛けの地面と同じ）
  const ground = <span className="inkline forge-ground" aria-hidden="true" />;

  if (portrait) {
    return (
      <div className={`${rootClass} forge-done--port`}>
        {figure}
        <div className="forge-done__main">
          {stage}
          <div className={`forge-scroll-wrap ${compactLevel ? `is-compact-${compactLevel}` : ''}`}>
            <div className="forge-scroll" onScroll={onScroll}>
              <div className="forge-done__info">
                <h1 className="forge-done__title intro-slide">錬成完了</h1>
                {nameEditor}
                {stats}
                <SkillInfo hiltType={hiltType} className="forge-done__skill intro-slide" />
              </div>
              <div className="forge-done__settings intro-late">
                {ground}
                <section className="forge-done__section">
                  <div className="forge-label">柄</div>
                  {hiltPicker}
                </section>
                <section className="forge-done__section">
                  <div className="forge-label">プライバシー</div>
                  {privacyPicker}
                </section>
              </div>
            </div>
            <CompactBar name={sword.name} shown={shown} hiltType={hiltType} level={compactLevel} />
          </div>
        </div>
        <div className="forge-done__actions forge-bottom-bar intro-late">
          <div className="forge-primary"><InkButton variant="shu" lit onClick={onReturn}>{returnLabel}</InkButton></div>
          <div className="forge-done__links">{links}</div>
        </div>
      </div>
    );
  }

  return (
    <Stage16x9>
      <div className={`${rootClass} forge-done--land`}>
        {stage}
        <div className="forge-done__side">
          <div className="forge-done__info">
            {figure}
            <div className="forge-done__info-body">
              <h1 className="forge-done__title intro-slide">錬成完了</h1>
              {nameEditor}
              {stats}
              <SkillInfo hiltType={hiltType} className="forge-done__skill intro-slide" />
            </div>
          </div>
          <div className="forge-done__spacer" />
          <div className="forge-done__bottom intro-late">
            {ground}
            <div className="forge-done__settings">
              <section>
                <div className="forge-label">柄</div>
                {hiltPicker}
              </section>
              <section>
                <div className="forge-label">プライバシー</div>
                {privacyPicker}
              </section>
            </div>
            <div className="forge-done__actions">
              <div className="forge-primary is-plain"><InkButton variant="shu" lit onClick={onReturn}>{returnLabel}</InkButton></div>
              <div className="forge-done__links">{links}</div>
            </div>
          </div>
        </div>
      </div>
    </Stage16x9>
  );
}
