import { useState } from 'react';
import './HowToPlayPanel.css';
import forgeImg from './HowTo_Forge.jpg';
import armoryImg from './HowTo_Armory.jpg';
import swordBattleImg from './HowTo_SwordBattle.jpg';
import specialImg from './HowTo_Special.jpg';
import komaBattleImg from './HowTo_KomaBattle.jpg';
import soloImg from './HowTo_Solo.jpg';
import resultImg from './HowTo_Result.jpg';

const PAGES = [
  {
    title: '剣を作ろう',
    image: forgeImg,
    description: '自分の写真を撮ると、自動的に画像が切り抜かれて、ステータスが生成され、剣が作られます。',
  },
  {
    title: '武器庫で剣を整えよう',
    image: armoryImg,
    description: '剣は3本まで持てます。柄を付け替えると、HP・攻撃・重さが変わります。プライバシー設定で、写真をシルエットなどに隠して戦うこともできます。',
  },
  {
    title: '剣モードの遊び方',
    image: swordBattleImg,
    description: '画面の左右をタップ／クリックすると、その方向へ移動できます。剣をぶつけて、相手のHPを0にしましょう。',
  },
  {
    title: '必殺技を放とう',
    image: specialImg,
    description: 'SPがたまると、画面下の丸いボタンが光ります。ボタンを押すと（PCならスペースキーでも）必殺技を放てます。必殺技は柄によって変わります。剣モードではSP100、コマモードではSP70から放てます。',
  },
  {
    title: 'コマモードの遊び方',
    image: komaBattleImg,
    description: '剣がコマのように回りながら、半自動で対戦が進みます。タップ／クリックするとSPを使って突進し、相手を牽制できます。',
  },
  {
    title: '特殊ルール',
    image: soloImg,
    description: 'ロビーでは特殊ルールを選べます。「残機制」は手持ちの剣で順番に戦います。「1 vs 3」はボス1人と3人で戦い、ボスはSPを200までためると「掌握斬」を放てます。',
  },
  {
    title: '結果を確認しよう',
    image: resultImg,
    description: '試合が終わると順位が出ます。与えたダメージ・受けたダメージ・撃破数も確認できます。',
  },
];

export default function HowToPlayPanel({ open, onClose }) {
  const [page, setPage] = useState(0);

  if (!open) return null;

  const handleClose = () => {
    setPage(0);
    onClose();
  };

  const isLast = page === PAGES.length - 1;
  const current = PAGES[page];

  return (
    <div className="howto-overlay" onClick={handleClose}>
      <div className="howto-panel" onClick={(e) => e.stopPropagation()}>
        <button className="howto-close" onClick={handleClose} aria-label="閉じる">×</button>

        <p className="howto-step">{page + 1} / {PAGES.length}</p>
        <h2 className="howto-title">{current.title}</h2>
        <div className="howto-image-wrap">
          <img src={current.image} alt={current.title} />
        </div>
        <p className="howto-description">{current.description}</p>

        <div className="howto-dots">
          {PAGES.map((_, i) => (
            <span key={i} className={`howto-dot ${i === page ? 'active' : ''}`} />
          ))}
        </div>

        <div className="howto-nav">
          <button
            className="howto-nav-btn"
            onClick={() => setPage((p) => p - 1)}
            disabled={page === 0}
          >
            戻る
          </button>
          {isLast ? (
            <button className="howto-nav-btn howto-nav-btn--primary" onClick={handleClose}>
              閉じる
            </button>
          ) : (
            <button
              className="howto-nav-btn howto-nav-btn--primary"
              onClick={() => setPage((p) => p + 1)}
            >
              次へ
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
