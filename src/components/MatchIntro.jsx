import { PLAYER_COLORS, swordImageSource } from '../styles';
import { MATCH_INTRO_MS } from '../network/RoomSession.js';
import { hiltOf } from './swordForge.js';
import './MatchIntro.css';

// 火の粉（剣）と速度線（独楽）の粒の数。位置や速さは MatchIntro.css で粒ごとに決めている
const SPARKS = 20;
const STREAKS = 12;

// 刀身の画像。ロビーと同じく、送られてきた公開用の画像（プライバシー設定を反映済み）を使う
const bladeOf = sword => sword?.imageSrc || swordImageSource(sword);

// 残機制で、装備中の剣の後ろに立たせる控えの剣（空きの枠は出さない）
const reservesOf = sword => (sword?.swords ?? [])
  .filter((slot, i) => slot && !slot.isEmpty && i !== sword.equippedIndex)
  .slice(0, 2);
const swordCountOf = sword => sword?.swords ? sword.swords.filter(slot => slot && !slot.isEmpty).length : 1;

// 刀身＋柄（ロビーの剣と同じ組み方。大きさは --k で変える）。独楽は柄を外す
const Sword = ({ sword, className = '', hilt = true }) => (
  <div className={`mi-sword ${className}`}>
    <img className="mi-sword__blade" src={bladeOf(sword)} alt="" draggable={false} />
    {hilt && <img className="mi-sword__hilt" src={hiltOf(sword?.hiltType).imageSrc} alt="" draggable={false} />}
  </div>
);

// ==========================================
// 対戦開始の演出（ロード画面を兼ねる）
// ==========================================
// 1280×720 の舞台（Stage16x9）の中に出す。プレイヤー色の斜めのパネルを 1P から順に並べ、
// 剣・名前・ルールを見せる。1vs3 はボスを左に置き、ボスと3人の間に VS。
// 読み込みの状況は人ごとには出さず、下の墨の線が最低限の表示時間（MATCH_INTRO_MS）を刻むだけ。
export default function MatchIntro({ room, localPlayerId, waiting }) {
  const koma = room.gameMode === '1';
  const solo = Boolean(room.soloMode);
  const lives = !solo && Boolean(room.livesMode);
  const bySlot = [...room.players].sort((a, b) => a.slotIndex - b.slotIndex);
  const players = solo
    ? [...bySlot.filter(p => p.playerId === room.bossPlayerId), ...bySlot.filter(p => p.playerId !== room.bossPlayerId)]
    : bySlot;
  const n = Math.min(4, Math.max(2, players.length));

  const ruleName = solo ? '1 vs 3' : lives ? '残機制' : '一本勝負';
  const desc = (koma ? '独楽のようにぶつかり合う半自動戦闘モード' : '剣を振り回して戦うモード')
    + (solo ? '　／　1人（ボス）対3人。3人側は同士討ちしません'
      : lives ? '　／　所持している剣がすべて撃破されるまで敗北しません' : '');

  return (
    <div className={`match-intro mi-n${n} ${koma ? 'is-koma' : ''} ${lives ? 'is-lives' : ''} ${solo ? 'is-solo' : ''} ${waiting ? 'is-waiting' : ''}`}
      style={{ '--intro': `${MATCH_INTRO_MS}ms` }}>
      <div className="mi-stage">
        {/* 背景の動き：剣は鍛冶場の火の粉、独楽は速度線 */}
        {koma
          ? <div className="mi-bg mi-bg--speed" aria-hidden="true">{Array.from({ length: STREAKS }, (_, i) => <i key={i} />)}</div>
          : <div className="mi-bg mi-bg--sparks" aria-hidden="true">{Array.from({ length: SPARKS }, (_, i) => <i key={i} />)}</div>}

        <div className="mi-panels">
          {players.map((player, i) => {
            const sword = player.swordData;
            const isBoss = solo && player.playerId === room.bossPlayerId;
            const count = swordCountOf(sword);
            return (
              <section
                key={player.playerId}
                className={`mi-panel ${i % 2 ? 'from-below' : ''} ${isBoss ? 'is-boss' : ''}`}
                style={{ '--pc': PLAYER_COLORS[player.slotIndex], '--d': `${i * 0.1}s` }}
              >
                {/* その人の姿を大きく薄く（一番後ろ） */}
                <img className="mi-panel__figure" src={bladeOf(sword)} alt="" draggable={false} />
                <div className="mi-panel__ghost" aria-hidden="true">{isBoss ? 'BOSS' : `${player.slotIndex + 1}P`}</div>
                <div className="mi-panel__brush" aria-hidden="true" />
                <div className="mi-panel__brush2" aria-hidden="true" />
                {i > 0 && <><span className="mi-panel__edge" aria-hidden="true" /><span className="mi-panel__mark" aria-hidden="true" /></>}

                <div className="mi-panel__art">
                  <span className="mi-panel__shadow" aria-hidden="true" />
                  {lives && reservesOf(sword).map((slot, k) => (
                    <Sword key={k} sword={slot} hilt={!koma} className={`mi-sword--reserve mi-sword--r${k}`} />
                  ))}
                  {koma
                    ? <div className="mi-koma"><div className="mi-koma__spin"><Sword sword={sword} hilt={false} /></div></div>
                    : <Sword sword={sword} className="mi-sword--main" />}
                </div>

                <div className="mi-plate">
                  <div className="mi-plate__top">
                    <span className="mi-plate__badge">{player.slotIndex + 1}P</span>
                    <span className="mi-plate__name">{sword?.name}</span>
                    {isBoss && <small className="mi-plate__tag mi-plate__tag--boss">ボス</small>}
                    {player.playerId === localPlayerId && <small className="mi-plate__tag">あなた</small>}
                  </div>
                  {lives && (
                    <span className="mi-plate__stock">
                      残機{[0, 1, 2].map(k => <i key={k} className={k < count ? '' : 'is-off'} />)}
                    </span>
                  )}
                </div>
              </section>
            );
          })}
        </div>

        {solo && <div className="mi-vs" aria-hidden="true"><span>VS</span></div>}

        <header className="mi-rule">
          <span className="mi-rule__mode">{koma ? '独楽' : '剣'}</span>
          <div className="mi-rule__text">
            <h1 className="mi-rule__name">{ruleName}</h1>
            <p className="mi-rule__desc">{desc}</p>
          </div>
        </header>

        {/* 最低限の表示時間を刻む墨の線。経っても誰かが読み込み中なら「準備中」とだけ出す */}
        <div className="mi-timer" aria-hidden="true"><div className="mi-timer__ink" /><span className="mi-timer__tip" /></div>
        {waiting && <div className="mi-waiting">準備中<span className="mi-dots"><i /><i /><i /></span></div>}
      </div>
      <div className="mi-flash" aria-hidden="true" />
    </div>
  );
}
