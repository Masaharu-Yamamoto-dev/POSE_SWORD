// src/screens/SwordListScreen.jsx
import React, { useState, useEffect } from 'react';
import { styles } from '../styles';
import InkButton from '../components/InkButton.jsx';

// ==========================================
// 📖 柄（ヒルト）のマスターデータ辞書
// ==========================================
export const HILT_DATABASE = {
  "0": {
    name: "普通の柄",
    imageSrc: "/sword_handle.png",
    hpBonus: 20,
    attackBonus: 10,
    weightBonus: 5,
    skillName: "大回転斬",
    skillDescription: "敵に目掛けて回転突進する、必殺の一撃!"
  },
  "1": {
    name: "武骨な柄",
    imageSrc: "/sword_handle_1.png",
    hpBonus: -50,
    attackBonus: 30,
    weightBonus: 20,
    skillName: "巨大回転斬",
    skillDescription: "巨大化して周囲を薙ぎ払う。複数KOも狙えるロマン技!"
  },
  "2": {
    name: "悪魔の柄",
    imageSrc: "/sword_handle_2.png",
    hpBonus: 0,
    attackBonus: 15,
    weightBonus: 10,
    skillName: "オレ達アタック",
    skillDescription: "自分の分身を飛ばして攻撃する、武士道皆無の珍技!"
  },
  "3": {
    name: "大翼の柄",
    imageSrc: "/sword_handle_3.png",
    hpBonus: 30,
    attackBonus: 10,
    weightBonus: -10,
    skillName: "オレ達シールド",
    skillDescription: "自分の分身を周囲に展開する、攻防一体の妙技!"
  }
};

export default function SwordListScreen({
  direction = "forward",
  swordList, mySwordData, equipSword, deleteSword,
  startNewCrafting, startRecapture, updateSword, cancelList, toggleSwordFlip
}) {
  const [selectedId, setSelectedId] = useState(mySwordData?.id || swordList[0]?.id);
  const [isEditingName, setIsEditingName] = useState(false);
  const [editName, setEditName] = useState("");
  const [isHiltModalOpen, setIsHiltModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);

  const [transition, setTransition] = useState("enter");
  const [isSpinning, setIsSpinning] = useState(false);

  useEffect(() => {
    if (!swordList.find(s => s.id === selectedId)) {
      setSelectedId(swordList[0]?.id);
    }
  }, [swordList, selectedId]);

  // 🌟 初回入場アニメーション完了後にクラスを解除し、リスト更新（破棄等）時のフェードインを防止
  useEffect(() => {
    const timer = setTimeout(() => {
      setTransition("idle");
    }, 350);
    return () => clearTimeout(timer);
  }, []);

  const selectedSword = swordList.find(s => s.id === selectedId);
  if (!selectedSword) return null;

  const isEquipped = mySwordData?.id === selectedSword.id;
  const currentHilt = HILT_DATABASE[selectedSword.hiltType] || HILT_DATABASE["0"];

  const totalHp = Math.max(1, selectedSword.hp + currentHilt.hpBonus);
  const totalAttack = Math.max(1, selectedSword.attack + currentHilt.attackBonus);
  const totalWeight = Math.max(1, selectedSword.weight + currentHilt.weightBonus);

  const handleEditClick = () => {
    setEditName(selectedSword.name);
    setIsEditingName(true);
  };

  const handleNameSave = () => {
    const finalName = editName.trim() === "" ? "無銘の剣" : editName;
    updateSword(selectedSword.id, { name: finalName });
    setIsEditingName(false);
  };

  const handleHiltChange = (hiltId) => {
    updateSword(selectedSword.id, { hiltType: hiltId });
    setIsHiltModalOpen(false);
  };

  const handleSwordClick = () => {
    if (!isSpinning) {
      setIsSpinning(true);
    }
  };

  // 🌟 親コンポーネントの window.confirm による二重ダイアログ発火を完全にバイパスして破棄を実行
  const ConfirmDeleteSword = () => {
    if (selectedSword) {
      setTransition("idle");
      const origConfirm = window.confirm;
      window.confirm = () => true;
      try {
        deleteSword(selectedSword.id);
      } finally {
        window.confirm = origConfirm;
      }
      setIsDeleteModalOpen(false);
    }
  };

  // 画面遷移フック関数
  const onCancel = () => {
    if (transition !== "enter" && transition !== "idle") return;
    setTransition("exit-back");
    setTimeout(cancelList, 300);
  };

  const onRecapture = (id) => {
    if (transition !== "enter" && transition !== "idle") return;
    setTransition("exit-forward");
    setTimeout(() => startRecapture(id), 300);
  };

  const onNewCrafting = () => {
    if (transition !== "enter" && transition !== "idle") return;
    setTransition("exit-forward");
    setTimeout(startNewCrafting, 300);
  };

  const animClass = transition === "exit-back" ? "page-exit-back" :
    transition === "exit-forward" ? "page-exit-forward" :
      transition === "idle" ? "" :
        (direction === "back" ? "page-enter-back" : "page-enter-forward");

  return (
    <div className={animClass} style={{ ...styles.container, justifyContent: 'flex-start', paddingTop: '20px' }}>

      {/* 👑 ヘッダー部分 */}
      <div style={{ width: '100%', maxWidth: '1000px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0 20px', marginBottom: '20px', boxSizing: 'border-box' }}>
        <button className="sumi-btn" onClick={onCancel}>
          ◀ 戻る
        </button>
        <h2 style={{ margin: 0, fontSize: '32px', color: 'var(--sumi)', letterSpacing: '2px' }}>武 器 庫</h2>
        <div style={{ width: '80px' }}></div>
      </div>

      {/* ▼ 変更：左右のカラム分けを廃止し、2×2のグリッド（マス目）に直接配置して高さを同期 */}
      <div className="armory-grid" style={{ width: '100%', maxWidth: '1000px', display: 'grid', gridTemplateColumns: '1fr 1.5fr', gap: '20px', padding: '0 20px', boxSizing: 'border-box' }}>

        {/* ======================= 行1：左上（プレビュー） ======================= */}
        {/* 高さを固定せず、右上のパネルと自動で高さが揃うようにしました */}
        <div className="panel sumi-panel sumi-frame" style={{ minHeight: '280px', position: 'relative', display: 'flex', justifyContent: 'center', alignItems: 'flex-end', paddingBottom: '30px' }}>
          {isEquipped && <span key={selectedSword.id} className="hanko hanko--kin hanko--lg hanko--stamp" title="装備中" style={{ position: 'absolute', top: 14, left: 14 }}>装</span>}

          <div className="floating-sword" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', height: '100%' }}>
            <div
              className={isSpinning ? "spin-active" : ""}
              onClick={handleSwordClick}
              onAnimationEnd={() => setIsSpinning(false)}
              style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', cursor: 'pointer', transformOrigin: 'center center' }}
              title="クリックで回転"
            >
              <img
                src={selectedSword.imageSrc}
                alt="Blade"
                style={{ width: '150px', height: 'auto', zIndex: 1 }}
              />
              <img
                src={currentHilt.imageSrc}
                alt={currentHilt.name}
                style={{ width: '150px', height: 'auto', marginTop: '-40px', zIndex: 2 }}
              />
            </div>
          </div>
        </div>

        {/* ======================= 行1：右上（名前とステータス） ======================= */}
        <div className="panel sumi-panel sumi-frame" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid var(--kasure)', paddingBottom: '15px', marginBottom: '15px' }}>
            {isEditingName ? (
              <div style={{ display: 'flex', gap: '10px', width: '100%' }}>
                <input type="text" className="sumi-input" value={editName} onChange={(e) => setEditName(e.target.value)} maxLength={15} style={{ flex: 1, minWidth: 0, fontSize: '24px', textAlign: 'left' }} />
                <button className="sumi-btn" onClick={handleNameSave}>保存</button>
              </div>
            ) : (
              <>
                <h3 style={{ margin: 0, fontSize: '32px', color: 'var(--sumi)' }}>{selectedSword.name}</h3>
                <button onClick={handleEditClick} className="sumi-btn sumi-btn--sm" style={{ flexShrink: 0 }}>編集</button>
              </>
            )}
          </div>

          <div>
            <h4 style={{ margin: '0 0 15px 0', color: 'var(--usuzumi)' }}>総合ステータス</h4>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', fontSize: '18px', fontWeight: 'bold' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 4px', borderBottom: '1px solid var(--kasure)' }}>
                <span style={{ color: 'var(--usuzumi)' }}>HP</span>
                <span>{totalHp} <span style={{ fontSize: '14px', color: 'var(--usuzumi)', fontWeight: 'normal' }}>({selectedSword.hp} {currentHilt.hpBonus >= 0 ? `+${currentHilt.hpBonus}` : currentHilt.hpBonus})</span></span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 4px', borderBottom: '1px solid var(--kasure)' }}>
                <span style={{ color: 'var(--usuzumi)' }}>攻撃力</span>
                <span>{totalAttack} <span style={{ fontSize: '14px', color: 'var(--usuzumi)', fontWeight: 'normal' }}>({selectedSword.attack} {currentHilt.attackBonus >= 0 ? `+${currentHilt.attackBonus}` : currentHilt.attackBonus})</span></span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 4px', borderBottom: '1px solid var(--kasure)' }}>
                <span style={{ color: 'var(--usuzumi)' }}>重さ</span>
                <span>{totalWeight} <span style={{ fontSize: '14px', color: 'var(--usuzumi)', fontWeight: 'normal' }}>({selectedSword.weight} {currentHilt.weightBonus >= 0 ? `+${currentHilt.weightBonus}` : currentHilt.weightBonus})</span></span>
              </div>
            </div>
          </div>
        </div>

        {/* ======================= 行2：左下（アクションボタン） ======================= */}
        {/* 高さを自動で右下のパネルと同期させます */}
        <div className="panel sumi-panel sumi-frame" style={{ display: 'flex', flexDirection: 'column', gap: '10px', justifyContent: 'center' }}>
          <InkButton fit variant="shu" onClick={() => equipSword(selectedSword)} disabled={isEquipped}>
            {isEquipped ? "装備しています" : "これを装備する"}
          </InkButton>
          <div style={{ display: 'flex', gap: '10px' }}>
            <button className="sumi-btn" onClick={() => toggleSwordFlip(selectedSword.id)} style={{ flex: 1 }}>⇄ 左右反転</button>
            <button className="sumi-btn" onClick={() => onRecapture(selectedSword.id)} style={{ flex: 1 }}>撮り直し</button>
          </div>
          <button
            className="sumi-btn sumi-btn--shu"
            onClick={() => setIsDeleteModalOpen(true)}
            disabled={swordList.length === 1}
          >
            破棄する
          </button>
        </div>

        {/* ======================= 行2：右下（現在の柄と必殺技） ======================= */}
        <div className="panel sumi-panel sumi-frame" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '15px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
              {/* 🌟 柄のプレビュー画像を追加 */}
              <div style={{ width: '50px', height: '50px', border: '1px solid var(--kasure)', display: 'flex', justifyContent: 'center', alignItems: 'center', flexShrink: 0 }}>
                <img src={currentHilt.imageSrc} alt={currentHilt.name} style={{ width: '90%', height: 'auto', objectFit: 'contain' }} />
              </div>
              <div>
                <span style={{ fontSize: '12px', color: 'var(--usuzumi)', display: 'block' }}>現在の柄</span>
                <h4 style={{ margin: 0, fontSize: '24px', color: 'var(--sumi)' }}>{currentHilt.name}</h4>
              </div>
            </div>
            <button className="sumi-btn sumi-btn--sm" onClick={() => setIsHiltModalOpen(true)}>
              柄を変更する
            </button>
          </div>
          <div style={{ backgroundColor: 'var(--washi-deep)', padding: '15px' }}>
            <span style={{ fontSize: '12px', color: 'var(--shu)', display: 'block', marginBottom: '5px' }}>必殺技</span>
            <div style={{ fontWeight: 'bold', fontSize: '18px', marginBottom: '5px' }}>{currentHilt.skillName}</div>
            <div style={{ fontSize: '14px', color: 'var(--usuzumi)' }}>{currentHilt.skillDescription}</div>
          </div>
        </div>

      </div>

      {/* ======================= 画面下部：所持スロット ======================= */}
      <div style={{ width: '100%', maxWidth: '1000px', marginTop: '10px', padding: '20px', boxSizing: 'border-box', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        <p style={{ margin: '0 0 10px 0', fontWeight: 'bold', color: 'var(--usuzumi)', fontSize: '14px' }}>所持スロット ({swordList.length} / 3)</p>
        <div style={{ display: 'flex', gap: '20px', justifyContent: 'center' }}>
          {[0, 1, 2].map(index => {
            const sword = swordList[index];
            const isSelectedSlot = sword && selectedId === sword.id;
            const isEquippedSlot = sword && mySwordData?.id === sword.id;

            if (sword) {
              const slotHilt = HILT_DATABASE[sword.hiltType || "0"] || HILT_DATABASE["0"];
              return (
                <div
                  key={sword.id}
                  onClick={() => setSelectedId(sword.id)}
                  style={{
                    position: 'relative',
                    width: '80px',
                    height: '80px',
                    border: isSelectedSlot ? '3px solid var(--sumi)' : '1px solid var(--kasure)',
                    cursor: 'pointer',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'center',
                    alignItems: 'center',
                    overflow: 'hidden',
                    boxSizing: 'border-box'
                  }}
                >
                  {/* 左上に番号を表示 */}
                  <span style={{ position: 'absolute', top: 3, left: 5, fontSize: '11px', fontWeight: 'bold', color: isEquippedSlot ? 'var(--sumi)' : 'var(--usuzumi)', zIndex: 10 }}>
                    {index + 1}
                  </span>

                  {/* 装備中バッジ */}
                  {isEquippedSlot && (
                    <span className="hanko hanko--kin hanko--sm" title="装備中" style={{ position: 'absolute', top: 3, right: 3, fontSize: '12px', zIndex: 12 }}>装</span>
                  )}

                  {/* 🌟 剣（刃＋柄）の中央配置表示 */}
                  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', width: '100%', height: '100%', paddingTop: '6px' }}>
                    <img src={sword.imageSrc} alt="Blade" style={{ height: '32px', width: 'auto', objectFit: 'contain', zIndex: 1 }} />
                    <img src={slotHilt.imageSrc} alt="Hilt" style={{ height: '32px', width: 'auto', marginTop: '-10px', zIndex: 2, objectFit: 'contain' }} />
                  </div>
                </div>
              );
            } else {
              return (
                <div
                  key={`empty-${index}`}
                  onClick={onNewCrafting}
                  style={{
                    position: 'relative',
                    width: '80px',
                    height: '80px',
                    backgroundColor: 'transparent',
                    border: '2px dashed var(--kasure)',
                    cursor: 'pointer',
                    display: 'flex',
                    justifyContent: 'center',
                    alignItems: 'center',
                    color: 'var(--kasure)',
                    fontSize: '24px',
                    transition: '0.2s'
                  }}
                >
                  {/* 空きスロットにも左上に番号を表示 */}
                  <span style={{ position: 'absolute', top: 3, left: 5, fontSize: '11px', fontWeight: 'bold', color: 'var(--kasure)' }}>
                    {index + 1}
                  </span>
                  ＋
                </div>
              );
            }
          })}
        </div>
      </div>

      {/* ⚠️ 破棄確認モーダル */}
      {isDeleteModalOpen && (
        <div style={{ position: 'fixed', top: 0, left: 0, width: '100%', height: '100%', backgroundColor: 'rgba(23, 20, 18, 0.7)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 200 }}>
          <div className="sumi-frame" style={{ backgroundColor: 'var(--washi)', padding: '25px 30px', width: '90%', maxWidth: '420px', textAlign: 'center', boxSizing: 'border-box' }}>
            <h3 style={{ margin: '0 0 10px 0', fontSize: '22px', color: 'var(--sumi)' }}>剣を破棄しますか？</h3>
            <p style={{ margin: '0 0 20px 0', fontSize: '14px', color: 'var(--usuzumi)', lineHeight: '1.5' }}>
              「<strong style={{ color: 'var(--shu)' }}>{selectedSword.name}</strong>」を破棄します。<br />この操作は取り消せません。
            </p>
            <div style={{ display: 'flex', gap: '15px', justifyContent: 'center' }}>
              <button className="sumi-btn" onClick={() => setIsDeleteModalOpen(false)} style={{ flex: 1 }}>
                キャンセル
              </button>
              <button className="sumi-btn sumi-btn--shu" onClick={ConfirmDeleteSword} style={{ flex: 1 }}>
                破棄する
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 🛡️ 柄の変更モーダル */}
      {isHiltModalOpen && (
        <div style={{ position: 'fixed', top: 0, left: 0, width: '100%', height: '100%', backgroundColor: 'rgba(23, 20, 18, 0.7)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 100 }}>
          <div className="sumi-frame" style={{ backgroundColor: 'var(--washi)', padding: '30px', boxSizing: 'border-box', width: '90%', maxWidth: '600px', maxHeight: '80vh', display: 'flex', flexDirection: 'column' }}>
            <h2 style={{ margin: '0 0 20px 0', borderBottom: '2px solid var(--sumi)', paddingBottom: '10px', display: 'flex', justifyContent: 'space-between' }}>
              <span>柄（つか）の変更</span>
              <button onClick={() => setIsHiltModalOpen(false)} style={{ background: 'none', border: 'none', fontSize: '24px', cursor: 'pointer', color: 'var(--usuzumi)' }}>✖</button>
            </h2>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', overflowY: 'auto', paddingRight: '10px' }}>
              {Object.entries(HILT_DATABASE).map(([hiltId, hiltData]) => {
                const isCurrentlySet = (selectedSword.hiltType || "0") === hiltId;
                return (
                  <div key={hiltId} style={{ display: 'flex', border: isCurrentlySet ? '2px solid var(--sumi)' : '1px solid var(--kasure)', padding: '15px', alignItems: 'center', gap: '20px', backgroundColor: isCurrentlySet ? 'var(--washi-deep)' : 'transparent', transition: '0.2s' }}>
                    <div style={{ width: '60px', display: 'flex', justifyContent: 'center', alignItems: 'center', flexShrink: 0 }}>
                      <img src={hiltData.imageSrc} alt={hiltData.name} style={{ width: '100%', height: 'auto', objectFit: 'contain' }} />
                    </div>
                    <div style={{ flex: 1 }}>
                      <h4 style={{ margin: '0 0 5px 0', fontSize: '20px' }}>
                        {hiltData.name}
                        {isCurrentlySet && <span className="hanko hanko--kin hanko--sm" title="装着中" style={{ marginLeft: '10px', verticalAlign: 'middle' }}>装</span>}
                      </h4>
                      <div style={{ fontSize: '14px', color: 'var(--usuzumi)', marginBottom: '5px' }}>
                        <span>HP {hiltData.hpBonus > 0 ? `+${hiltData.hpBonus}` : hiltData.hpBonus}</span> |
                        <span> 攻 {hiltData.attackBonus > 0 ? `+${hiltData.attackBonus}` : hiltData.attackBonus}</span> |
                        <span> 重 {hiltData.weightBonus > 0 ? `+${hiltData.weightBonus}` : hiltData.weightBonus}</span>
                      </div>
                      <div style={{ fontSize: '12px', color: 'var(--shu)', display: 'inline-block' }}>
                        技: {hiltData.skillName}
                      </div>
                    </div>
                    <button
                      className="sumi-btn"
                      disabled={isCurrentlySet}
                      onClick={() => handleHiltChange(hiltId)}
                      style={{ flexShrink: 0 }}
                    >
                      {isCurrentlySet ? "装着中" : "変更する"}
                    </button>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      )}

      {/* 🌀 アニメーションCSS */}
      <style>{`
        .panel {
          padding: 20px;
          box-sizing: border-box;
        }

        @keyframes float {
          0%, 100% { transform: translateY(0); }
          50% { transform: translateY(-15px); }
        }
        .floating-sword {
          animation: float 3s ease-in-out infinite;
        }

        @keyframes spin {
          0% { transform: rotate(0deg); }
          100% { transform: rotate(360deg); }
        }
        .spin-active {
          animation: spin 0.6s cubic-bezier(0.4, 0, 0.2, 1);
        }

        @media (max-width: 800px) {
          .armory-grid {
            grid-template-columns: 1fr !important; /* スマホでは1列に自動で並び替わります */
          }
        }
      `}</style>
    </div>
  );
}