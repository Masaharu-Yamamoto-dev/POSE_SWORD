// src/screens/SwordListScreen.jsx
import React, { useState, useEffect } from 'react';
import { styles } from '../styles';

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
    <div className={animClass} style={{ ...styles.container, justifyContent: 'flex-start', paddingTop: '20px', backgroundColor: '#eef2f5' }}>

      {/* 👑 ヘッダー部分 */}
      <div style={{ width: '100%', maxWidth: '1000px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0 20px', marginBottom: '20px', boxSizing: 'border-box' }}>
        <button style={{ padding: '10px 20px', backgroundColor: '#607d8b', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer', boxShadow: '0 2px 4px rgba(0,0,0,0.2)' }} onClick={onCancel}>
          ◀ 戻る
        </button>
        <h2 style={{ margin: 0, fontSize: '32px', color: '#333', letterSpacing: '2px' }}>武 器 庫</h2>
        <div style={{ width: '80px' }}></div>
      </div>

      {/* ▼ 変更：左右のカラム分けを廃止し、2×2のグリッド（マス目）に直接配置して高さを同期 */}
      <div className="armory-grid" style={{ width: '100%', maxWidth: '1000px', display: 'grid', gridTemplateColumns: '1fr 1.5fr', gap: '20px', padding: '0 20px', boxSizing: 'border-box' }}>

        {/* ======================= 行1：左上（プレビュー） ======================= */}
        {/* 高さを固定せず、右上のパネルと自動で高さが揃うようにしました */}
        <div className="panel" style={{ minHeight: '280px', position: 'relative', display: 'flex', justifyContent: 'center', alignItems: 'flex-end', paddingBottom: '30px', border: isEquipped ? '4px solid #4CAF50' : '2px solid transparent' }}>
          {isEquipped && <div style={{ position: 'absolute', top: 10, left: 10, backgroundColor: '#4CAF50', color: 'white', padding: '5px 15px', fontWeight: 'bold', borderRadius: '5px' }}>★ 装備中</div>}

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
        <div className="panel" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid #eee', paddingBottom: '15px', marginBottom: '15px' }}>
            {isEditingName ? (
              <div style={{ display: 'flex', gap: '10px', width: '100%' }}>
                <input type="text" value={editName} onChange={(e) => setEditName(e.target.value)} maxLength={15} style={{ flex: 1, padding: '10px', fontSize: '24px', fontWeight: 'bold', border: '2px solid #2196F3', borderRadius: '5px' }} />
                <button onClick={handleNameSave} style={{ padding: '0 20px', backgroundColor: '#4CAF50', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer' }}>保存</button>
              </div>
            ) : (
              <>
                <h3 style={{ margin: 0, fontSize: '32px', color: '#000' }}>{selectedSword.name}</h3>
                <button onClick={handleEditClick} style={{ background: 'none', border: 'none', fontSize: '24px', cursor: 'pointer' }}>✏️</button>
              </>
            )}
          </div>

          <div>
            <h4 style={{ margin: '0 0 15px 0', color: '#666' }}>総合ステータス</h4>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', fontSize: '18px', fontWeight: 'bold' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', backgroundColor: '#f5f5f5', padding: '10px 15px', borderRadius: '8px' }}>
                <span style={{ color: '#d32f2f' }}>HP</span>
                <span>{totalHp} <span style={{ fontSize: '14px', color: '#888', fontWeight: 'normal' }}>({selectedSword.hp} {currentHilt.hpBonus >= 0 ? `+${currentHilt.hpBonus}` : currentHilt.hpBonus})</span></span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', backgroundColor: '#f5f5f5', padding: '10px 15px', borderRadius: '8px' }}>
                <span style={{ color: '#f57c00' }}>攻撃力</span>
                <span>{totalAttack} <span style={{ fontSize: '14px', color: '#888', fontWeight: 'normal' }}>({selectedSword.attack} {currentHilt.attackBonus >= 0 ? `+${currentHilt.attackBonus}` : currentHilt.attackBonus})</span></span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', backgroundColor: '#f5f5f5', padding: '10px 15px', borderRadius: '8px' }}>
                <span style={{ color: '#558b2f' }}>重さ</span>
                <span>{totalWeight} <span style={{ fontSize: '14px', color: '#888', fontWeight: 'normal' }}>({selectedSword.weight} {currentHilt.weightBonus >= 0 ? `+${currentHilt.weightBonus}` : currentHilt.weightBonus})</span></span>
              </div>
            </div>
          </div>
        </div>

        {/* ======================= 行2：左下（アクションボタン） ======================= */}
        {/* 高さを自動で右下のパネルと同期させます */}
        <div className="panel" style={{ display: 'flex', flexDirection: 'column', gap: '10px', justifyContent: 'center' }}>
          <button
            style={{ padding: '15px', fontSize: '18px', fontWeight: 'bold', backgroundColor: isEquipped ? '#ccc' : '#2196F3', color: 'white', border: 'none', borderRadius: '8px', cursor: isEquipped ? 'default' : 'pointer' }}
            onClick={() => equipSword(selectedSword)}
            disabled={isEquipped}
          >
            {isEquipped ? "装備しています" : "⚔️ これを装備する"}
          </button>
          <div style={{ display: 'flex', gap: '10px' }}>
            <button onClick={() => toggleSwordFlip(selectedSword.id)} style={{ flex: 1, padding: '12px', backgroundColor: '#9C27B0', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer' }}>⇄ 左右反転</button>
            <button onClick={() => onRecapture(selectedSword.id)} style={{ flex: 1, padding: '12px', backgroundColor: '#ff9800', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer' }}>📸 撮り直し</button>
          </div>
          <button
            onClick={() => setIsDeleteModalOpen(true)}
            disabled={swordList.length === 1}
            style={{ padding: '12px', backgroundColor: swordList.length === 1 ? '#e0e0e0' : '#f44336', color: swordList.length === 1 ? '#9e9e9e' : 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: swordList.length === 1 ? 'not-allowed' : 'pointer' }}
          >
            🗑️ 破棄する
          </button>
        </div>

        {/* ======================= 行2：右下（現在の柄と必殺技） ======================= */}
        <div className="panel" style={{ borderLeft: '5px solid #2196F3', display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '15px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
              {/* 🌟 柄のプレビュー画像を追加 */}
              <div style={{ width: '50px', height: '50px', backgroundColor: '#fff', borderRadius: '8px', border: '1px solid #ddd', display: 'flex', justifyContent: 'center', alignItems: 'center', flexShrink: 0 }}>
                <img src={currentHilt.imageSrc} alt={currentHilt.name} style={{ width: '90%', height: 'auto', objectFit: 'contain' }} />
              </div>
              <div>
                <span style={{ fontSize: '12px', color: '#666', display: 'block' }}>現在の柄</span>
                <h4 style={{ margin: 0, fontSize: '24px', color: '#333' }}>{currentHilt.name}</h4>
              </div>
            </div>
            <button
              onClick={() => setIsHiltModalOpen(true)}
              style={{ /* ...既存のスタイル... */ }}
            >
              ⚙️ 柄を変更する
            </button>
          </div>
          <div style={{ backgroundColor: '#e3f2fd', padding: '15px', borderRadius: '8px' }}>
            <span style={{ fontSize: '12px', color: '#1976d2', fontWeight: 'bold', display: 'block', marginBottom: '5px' }}>⚔️ 必殺技</span>
            <div style={{ fontWeight: 'bold', fontSize: '18px', marginBottom: '5px' }}>{currentHilt.skillName}</div>
            <div style={{ fontSize: '14px', color: '#555' }}>{currentHilt.skillDescription}</div>
          </div>
        </div>

      </div>

      {/* ======================= 画面下部：所持スロット ======================= */}
      <div style={{ width: '100%', maxWidth: '1000px', marginTop: '10px', padding: '20px', boxSizing: 'border-box', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        <p style={{ margin: '0 0 10px 0', fontWeight: 'bold', color: '#555', fontSize: '14px' }}>所持スロット ({swordList.length} / 3)</p>
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
                    backgroundColor: 'white',
                    borderRadius: '10px',
                    border: isSelectedSlot
                      ? '3px solid #2196F3'
                      : isEquippedSlot
                        ? '3px solid #4CAF50'
                        : '2px solid #ccc',
                    cursor: 'pointer',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'center',
                    alignItems: 'center',
                    overflow: 'hidden',
                    boxShadow: '0 2px 5px rgba(0,0,0,0.1)'
                  }}
                >
                  {/* 左上に番号を表示 */}
                  <span style={{ position: 'absolute', top: 3, left: 5, fontSize: '11px', fontWeight: 'bold', color: isEquippedSlot ? '#4CAF50' : '#888', zIndex: 10 }}>
                    {index + 1}
                  </span>

                  {/* 装備中バッジ */}
                  {isEquippedSlot && (
                    <div style={{ position: 'absolute', top: -1, right: -1, backgroundColor: '#4CAF50', color: 'white', fontSize: '9px', fontWeight: 'bold', padding: '1px 5px', borderRadius: '0 6px 0 4px', zIndex: 12 }}>
                      装備
                    </div>
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
                    backgroundColor: 'rgba(255,255,255,0.5)',
                    borderRadius: '10px',
                    border: '2px dashed #aaa',
                    cursor: 'pointer',
                    display: 'flex',
                    justifyContent: 'center',
                    alignItems: 'center',
                    color: '#888',
                    fontSize: '24px',
                    transition: '0.2s'
                  }}
                >
                  {/* 空きスロットにも左上に番号を表示 */}
                  <span style={{ position: 'absolute', top: 3, left: 5, fontSize: '11px', fontWeight: 'bold', color: '#aaa' }}>
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
        <div style={{ position: 'fixed', top: 0, left: 0, width: '100%', height: '100%', backgroundColor: 'rgba(0,0,0,0.7)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 200 }}>
          <div style={{ backgroundColor: '#fff', borderRadius: '15px', padding: '25px 30px', width: '90%', maxWidth: '420px', textAlign: 'center', boxShadow: '0 10px 30px rgba(0,0,0,0.4)' }}>
            <div style={{ fontSize: '40px', marginBottom: '10px' }}>⚠️</div>
            <h3 style={{ margin: '0 0 10px 0', fontSize: '22px', color: '#333' }}>剣を破棄しますか？</h3>
            <p style={{ margin: '0 0 20px 0', fontSize: '14px', color: '#666', lineHeight: '1.5' }}>
              「<strong style={{ color: '#d32f2f' }}>{selectedSword.name}</strong>」を破棄します。<br />この操作は取り消せません。
            </p>
            <div style={{ display: 'flex', gap: '15px', justifyContent: 'center' }}>
              <button
                onClick={() => setIsDeleteModalOpen(false)}
                style={{ flex: 1, padding: '12px', backgroundColor: '#e0e0e0', color: '#333', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer', fontSize: '15px' }}
              >
                キャンセル
              </button>
              <button
                onClick={ConfirmDeleteSword}
                style={{ flex: 1, padding: '12px', backgroundColor: '#f44336', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer', fontSize: '15px' }}
              >
                破棄する
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 🛡️ 柄の変更モーダル */}
      {isHiltModalOpen && (
        <div style={{ position: 'fixed', top: 0, left: 0, width: '100%', height: '100%', backgroundColor: 'rgba(0,0,0,0.7)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 100 }}>
          <div style={{ backgroundColor: '#fff', borderRadius: '15px', padding: '30px', width: '90%', maxWidth: '600px', maxHeight: '80vh', display: 'flex', flexDirection: 'column', position: 'relative', boxShadow: '0 10px 30px rgba(0,0,0,0.5)' }}>
            <h2 style={{ margin: '0 0 20px 0', borderBottom: '2px solid #2196F3', paddingBottom: '10px', display: 'flex', justifyContent: 'space-between' }}>
              <span>柄（つか）の変更</span>
              <button onClick={() => setIsHiltModalOpen(false)} style={{ background: 'none', border: 'none', fontSize: '24px', cursor: 'pointer', color: '#999' }}>✖</button>
            </h2>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', overflowY: 'auto', paddingRight: '10px' }}>
              {Object.entries(HILT_DATABASE).map(([hiltId, hiltData]) => {
                const isCurrentlySet = (selectedSword.hiltType || "0") === hiltId;
                return (
                  <div key={hiltId} style={{ display: 'flex', border: isCurrentlySet ? '3px solid #4CAF50' : '1px solid #ccc', borderRadius: '10px', padding: '15px', alignItems: 'center', gap: '20px', backgroundColor: isCurrentlySet ? '#f1f8e9' : '#fff', transition: '0.2s' }}>
                    <div style={{ width: '60px', display: 'flex', justifyContent: 'center', alignItems: 'center', flexShrink: 0 }}>
                      <img src={hiltData.imageSrc} alt={hiltData.name} style={{ width: '100%', height: 'auto', objectFit: 'contain' }} />
                    </div>
                    <div style={{ flex: 1 }}>
                      <h4 style={{ margin: '0 0 5px 0', fontSize: '20px' }}>
                        {hiltData.name}
                        {isCurrentlySet && <span style={{ marginLeft: '10px', fontSize: '12px', color: 'white', backgroundColor: '#4CAF50', padding: '2px 8px', borderRadius: '10px', verticalAlign: 'middle' }}>装着中</span>}
                      </h4>
                      <div style={{ fontSize: '14px', color: '#666', marginBottom: '5px', fontWeight: 'bold' }}>
                        <span style={{ color: '#d32f2f' }}>HP {hiltData.hpBonus > 0 ? `+${hiltData.hpBonus}` : hiltData.hpBonus}</span> |
                        <span style={{ color: '#f57c00' }}> 攻 {hiltData.attackBonus > 0 ? `+${hiltData.attackBonus}` : hiltData.attackBonus}</span> |
                        <span style={{ color: '#558b2f' }}> 重 {hiltData.weightBonus > 0 ? `+${hiltData.weightBonus}` : hiltData.weightBonus}</span>
                      </div>
                      <div style={{ fontSize: '12px', color: '#1976d2', fontWeight: 'bold', backgroundColor: '#e3f2fd', padding: '4px 8px', borderRadius: '4px', display: 'inline-block' }}>
                        技: {hiltData.skillName}
                      </div>
                    </div>
                    <button
                      disabled={isCurrentlySet}
                      onClick={() => handleHiltChange(hiltId)}
                      style={{ padding: '12px 20px', backgroundColor: isCurrentlySet ? '#ccc' : '#2196F3', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: isCurrentlySet ? 'default' : 'pointer', flexShrink: 0, fontSize: '16px' }}
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
          background-color: #fff;
          border-radius: 12px;
          padding: 20px;
          box-shadow: 0 4px 6px rgba(0,0,0,0.05);
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