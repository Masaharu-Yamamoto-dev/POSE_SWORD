// src/screens/SwordListScreen.jsx
import React, { useState, useEffect } from 'react';
import { styles } from '../styles';

export default function SwordListScreen({ swordList, mySwordData, equipSword, deleteSword, startNewCrafting, startRecapture, updateSword, cancelList, toggleSwordFlip }) {
  const [selectedId, setSelectedId] = useState(mySwordData?.id || swordList[0]?.id);
  const [isEditingName, setIsEditingName] = useState(false);
  const [editName, setEditName] = useState("");

  useEffect(() => {
    if (!swordList.find(s => s.id === selectedId)) setSelectedId(swordList[0]?.id);
  }, [swordList, selectedId]);

  const selectedSword = swordList.find(s => s.id === selectedId);
  if (!selectedSword) return null;

  const isEquipped = mySwordData?.id === selectedSword.id;

  const handleEditClick = () => {
    setEditName(selectedSword.name);
    setIsEditingName(true);
  };

  const handleNameSave = () => {
    const finalName = editName.trim() === "" ? "無銘の剣" : editName;
    updateSword(selectedSword.id, { name: finalName });
    setIsEditingName(false);
  };

  const handleFlip = () => toggleSwordFlip(selectedSword.id);

  return (
    <div style={{ ...styles.container, justifyContent: 'flex-start', paddingTop: '20px' }}>
      
      <div style={{ width: '100%', maxWidth: '800px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0 20px', boxSizing: 'border-box' }}>
        <button style={{ padding: '10px 20px', backgroundColor: '#666', color: 'white', border: 'none', fontWeight: 'bold', cursor: 'pointer', boxShadow: '0 2px 4px rgba(0,0,0,0.2)' }} onClick={cancelList}>
          ◀ 戻る
        </button>
        <h2 style={{ margin: 0, fontSize: '28px', color: '#333' }}>武器庫</h2>
        <div style={{ width: '80px' }}></div>
      </div>

      <div style={{ ...styles.contentWrapper, width: '100%', maxWidth: '800px', marginTop: '10px', padding: '20px' }}>
        
        {/* 中央の剣 詳細エリア */}
        <div style={{ position: 'relative', width: '100%', backgroundColor: 'rgba(255,255,255,0.8)', padding: '20px', borderRadius: '15px', border: isEquipped ? '4px solid #4CAF50' : '2px solid #ccc', boxSizing: 'border-box', boxShadow: '0 4px 10px rgba(0,0,0,0.1)' }}>
          {isEquipped && <div style={{ position: 'absolute', top: '-15px', left: '-15px', backgroundColor: '#4CAF50', color: 'white', padding: '5px 15px', fontWeight: 'bold', borderRadius: '5px', transform: 'rotate(-5deg)', zIndex: 2 }}>★ 装備中</div>}

          <div style={{ display: 'flex', justifyContent: 'center', height: '200px', marginBottom: '10px' }}>
            <img 
              src={selectedSword.imageSrc} alt={selectedSword.name} 
              style={{ maxHeight: '100%', maxWidth: '100%', objectFit: 'contain', transition: 'transform 0.3s' }} 
            />
          </div>

          <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '10px', height: '40px', marginBottom: '10px' }}>
            {isEditingName ? (
              <>
                <input type="text" value={editName} onChange={(e) => setEditName(e.target.value)} maxLength={15} style={{ padding: '5px', fontSize: '20px', fontWeight: 'bold', width: '200px', textAlign: 'center' }} />
                <button onClick={handleNameSave} style={{ padding: '5px 10px', backgroundColor: '#4CAF50', color: 'white', border: 'none', fontWeight: 'bold', cursor: 'pointer' }}>保存</button>
              </>
            ) : (
              <>
                <h3 style={{ margin: 0, fontSize: '28px', color: '#000' }}>{selectedSword.name}</h3>
                <button onClick={handleEditClick} style={{ background: 'none', border: 'none', fontSize: '20px', cursor: 'pointer' }}>✏️</button>
              </>
            )}
          </div>

          <div style={{ display: 'flex', justifyContent: 'center', gap: '15px', fontSize: '18px', fontWeight: 'bold', color: '#333', marginBottom: '20px' }}>
            <span style={{ backgroundColor: '#e3f2fd', padding: '5px 15px', borderRadius: '20px' }}>HP: {selectedSword.hp}</span>
            <span style={{ backgroundColor: '#ffebee', padding: '5px 15px', borderRadius: '20px' }}>攻撃: {selectedSword.attack}</span>
            <span style={{ backgroundColor: '#fff3e0', padding: '5px 15px', borderRadius: '20px' }}>重さ: {selectedSword.weight}</span>
          </div>

          <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'center', gap: '10px' }}>
            <button style={{ padding: '12px 20px', fontSize: '16px', fontWeight: 'bold', backgroundColor: isEquipped ? '#ccc' : '#2196F3', color: 'white', border: 'none', borderRadius: '5px', cursor: isEquipped ? 'default' : 'pointer', width: '100%', maxWidth: '300px' }} onClick={() => equipSword(selectedSword)} disabled={isEquipped}>
              {isEquipped ? "⚔️ 装備しています" : "⚔️ これを装備する"}
            </button>
            
            <div style={{ display: 'flex', gap: '10px', width: '100%', maxWidth: '300px' }}>
              <button onClick={handleFlip} style={{ flex: 1, padding: '10px', backgroundColor: '#9C27B0', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer' }}>⇄ 左右反転</button>
              <button onClick={() => startRecapture(selectedSword.id)} style={{ flex: 1, padding: '10px', backgroundColor: '#ff9800', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer' }}>📸 姿の撮り直し</button>
            </div>

            <div style={{ display: 'flex', gap: '10px', width: '100%', maxWidth: '300px' }}>
              <button disabled style={{ flex: 1, padding: '10px', backgroundColor: '#e0e0e0', color: '#9e9e9e', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'not-allowed' }}>🔒 柄の変更</button>
              <button onClick={() => deleteSword(selectedSword.id)} disabled={swordList.length === 1} style={{ flex: 1, padding: '10px', backgroundColor: swordList.length === 1 ? '#e0e0e0' : '#f44336', color: swordList.length === 1 ? '#9e9e9e' : 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: swordList.length === 1 ? 'not-allowed' : 'pointer' }}>
                🗑️ 破棄する
              </button>
            </div>
          </div>
        </div>

        {/* 下部 スロット */}
        <div style={{ marginTop: '30px', padding: '15px', backgroundColor: 'rgba(0,0,0,0.05)', borderRadius: '10px' }}>
          <p style={{ margin: '0 0 10px 0', fontWeight: 'bold', color: '#555' }}>所持スロット ({swordList.length} / 3)</p>
          <div style={{ display: 'flex', justifyContent: 'center', gap: '15px' }}>
            {[0, 1, 2].map(index => {
              const sword = swordList[index];
              const isSelectedSlot = sword && selectedId === sword.id;
              const isEquippedSlot = sword && mySwordData?.id === sword.id;

              if (sword) {
                return (
                  <div key={sword.id} onClick={() => setSelectedId(sword.id)} style={{ position: 'relative', width: '80px', height: '80px', backgroundColor: 'white', borderRadius: '10px', border: isSelectedSlot ? '4px solid #2196F3' : '2px solid #ccc', cursor: 'pointer', display: 'flex', justifyContent: 'center', alignItems: 'center', overflow: 'hidden' }}>
                    {isEquippedSlot && <div style={{ position: 'absolute', top: 0, left: 0, width: '100%', backgroundColor: '#4CAF50', color: 'white', fontSize: '10px', fontWeight: 'bold' }}>装備</div>}
                    <img src={sword.imageSrc} alt="" style={{ maxWidth: '80%', maxHeight: '80%' }} />
                  </div>
                );
              } else {
                return (
                  <div key={`empty-${index}`} onClick={startNewCrafting} style={{ width: '80px', height: '80px', backgroundColor: 'rgba(255,255,255,0.5)', borderRadius: '10px', border: '2px dashed #aaa', cursor: 'pointer', display: 'flex', justifyContent: 'center', alignItems: 'center', color: '#888', fontSize: '30px', transition: '0.2s' }}>
                    ＋
                  </div>
                );
              }
            })}
          </div>
        </div>

      </div>
    </div>
  );
}