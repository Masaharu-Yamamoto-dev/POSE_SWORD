import { useState } from 'react';
import './HowToPlayPanel.css';
import kenSeiseiImg from './Ken_Seisei.png';
import swordModeImg from './Sword_Mode.png';
import komaModeImg from './Koma_Mode.png';

const PAGES = [
  {
    title: '剣を作ろう',
    image: kenSeiseiImg,
    description: '自分の写真を撮ると、自動的に画像が切り抜かれて、ステータスが生成され、剣が作られます。',
  },
  {
    title: '剣モードの遊び方',
    image: swordModeImg,
    description: '画面の左右をタップ／クリックすると、左右に移動できます。SPが100たまると、必殺技を放てます。',
  },
  {
    title: 'コマモードの遊び方',
    image: komaModeImg,
    description: '半自動で対戦が進みます。SPが70以上たまると、必殺技を放てます。',
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
      <div className="howto-panel glass" onClick={(e) => e.stopPropagation()}>
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
            ← 戻る
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
              次へ →
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
