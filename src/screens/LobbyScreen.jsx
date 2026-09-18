import React, { useState, useEffect } from 'react';
import { PLAYER_COLORS, styles, swordImageSource } from '../styles';
import { MAX_PLAYERS, MIN_PLAYERS } from '../network/HostRoom';
import { HILT_DATABASE } from './SwordListScreen';

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

// ==========================================
// 🌀 自動でサイズ調整・柄を合成する剣コンポーネント
// ==========================================
const AnimatedSword = ({ sword, mode = "main", isEquippedSlot = false, onClick, disabled = false, opacity = 1 }) => {
  const [isSpinning, setIsSpinning] = useState(false);
  
  if (!sword) return null;
  
  const hilt = HILT_DATABASE[sword.hiltType || "0"] || HILT_DATABASE["0"];
  const bladeImage = sword.imageSrc || swordImageSource(sword);
  const canAnimate = mode !== "list";

  const handleClick = (e) => {
    if (canAnimate && !isSpinning) setIsSpinning(true);
    if (onClick && !disabled) onClick(e);
  };

  let containerStyle = {};
  let bladeHeight = "100px";
  let hiltHeight = "100px";
  let marginTop = "-30px";

  if (mode === "list") {
    containerStyle = {
      position: 'relative', width: '65px', height: '65px',
      backgroundColor: 'white', borderRadius: '10px',
      border: isEquippedSlot ? '3px solid #4CAF50' : '2px solid #ccc',
      cursor: disabled ? 'default' : 'pointer',
      display: 'flex', justifyContent: 'center', alignItems: 'flex-end',
      paddingBottom: '5px', opacity: opacity, transition: '0.2s', boxSizing: 'border-box', flexShrink: 0
    };
    bladeHeight = "32px";
    hiltHeight = "32px";
    marginTop = "-9px";
  } else if (mode === "other") {
    containerStyle = {
      width: '80px', height: '85px',
      backgroundColor: '#f5f5f5', borderRadius: '8px',
      display: 'flex', justifyContent: 'center', alignItems: 'flex-end',
      paddingBottom: '8px', flexShrink: 0, cursor: 'pointer'
    };
    bladeHeight = "48px";
    hiltHeight = "48px";
    marginTop = "-14px";
  } else {
    // main (自分の装備プレビュー用)
    containerStyle = {
      height: '230px', display: 'flex', justifyContent: 'center', alignItems: 'flex-end',
      cursor: 'pointer', paddingBottom: '10px', width: '100%'
    };
    bladeHeight = "120px";
    hiltHeight = "120px";
    marginTop = "-35px";
  }

  return (
    <div onClick={handleClick} style={containerStyle}>
      {isEquippedSlot && mode === "list" && (
        <div style={{ position: 'absolute', top: -5, right: -5, backgroundColor: '#4CAF50', color: 'white', fontSize: '9px', fontWeight: 'bold', padding: '1px 5px', borderRadius: '4px', zIndex: 12 }}>
          装備
        </div>
      )}
      <div className={canAnimate ? "floating-sword" : ""} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', height: '100%', width: '100%', pointerEvents: 'none' }}>
        <div
          className={isSpinning && canAnimate ? "spin-active" : ""}
          onAnimationEnd={() => setIsSpinning(false)}
          style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', transformOrigin: 'center center', width: '100%', justifyContent: 'flex-end', pointerEvents: 'none' }}
          title={canAnimate ? "クリックで回転" : ""}
        >
          <img src={bladeImage} alt="Blade" style={{ height: bladeHeight, width: 'auto', zIndex: 1, objectFit: 'contain', pointerEvents: 'none', userSelect: 'none' }} draggable={false} />
          <img src={hilt.imageSrc} alt="Hilt" style={{ height: hiltHeight, width: 'auto', marginTop: marginTop, zIndex: 2, objectFit: 'contain', pointerEvents: 'none', userSelect: 'none' }} draggable={false} />
        </div>
      </div>
    </div>
  );
};

// ==========================================
// ロビー画面本体
// ==========================================
export default function LobbyScreen({
  view, roomId, isCopied, handleCopyId,
  swordList, mySwordData, equipSword, reorderSwords,
  onReady, onGameMode, onLivesMode, onStart, onLeave, goToCrafting, error
}) {
  const [draggedIndex, setDraggedIndex] = useState(null);
  const [dragOverIndex, setDragOverIndex] = useState(null);
  const [transition, setTransition] = useState("enter");

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
  const seatLimit = room.seatLimit ?? MAX_PLAYERS;
  const auto = Boolean(room.autoStart);
  const playerCount = room.players.length;
  const readyCount = room.players.filter(p => p.ready).length;
  const otherSeats = Array.from({ length: seatLimit }, (_, slot) => slot)
    .filter(slot => slot !== me.slotIndex)
    .map(slot => ({ slot, player: room.players.find(p => p.slotIndex === slot) }));

  const autoStatus =
    room.startsInMs == null ? `対戦相手を待っています（あと${Math.max(0, MIN_PLAYERS - playerCount)}人で開始）`
    : room.startsInMs <= 10000 ? 'まもなく開始します'
    : `${Math.ceil(room.startsInMs / 1000)}秒以内に開始します`;

  const status = auto ? autoStatus
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

  // 自分の最終ステータス算出
  const myFinalStats = getFinalStats(mySwordData);

  // フェードイン・フェードアウト用アニメーションクラス
  const animClass = transition === "exit-back" ? "page-exit-back" :
                    transition === "exit-forward" ? "page-exit-forward" : 
                    transition === "idle" ? "" : "page-enter-forward";

  return (
    <div className={animClass} style={{ ...styles.container, padding: '10px', alignItems: 'flex-start' }}>
      <div className="lobby-grid">

        {/* =========================================
            左上：自分の領域
        ========================================= */}
        <div className="lobby-panel self-panel" style={{ display: 'flex', flexDirection: 'column' }}>
          
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: `2px solid ${PLAYER_COLORS[me.slotIndex]}`, paddingBottom: '5px', marginBottom: '15px' }}>
            <h2 style={{ margin: 0, fontSize: '26px', color: PLAYER_COLORS[me.slotIndex] }}>
              {me.slotIndex + 1}P: あなた {isHost && "(ホスト)"}
            </h2>
            <span style={{ backgroundColor: me.ready ? '#4CAF50' : '#9e9e9e', color: 'white', padding: '4px 12px', borderRadius: '15px', fontSize: '13px', fontWeight: 'bold' }}>
              {me.ready ? "✅ 準備OK" : "⏳ 準備中"}
            </span>
          </div>

          {mySwordData ? (
            <div style={{ display: 'flex', flexDirection: 'column', flex: 1, justifyContent: 'space-between' }}>
              
              <div style={{ display: 'flex', flexDirection: 'row', gap: '20px', alignItems: 'center', flex: 1 }}>
                
                {/* 左側：メイン剣 ＆ ステータス（柄補正適用・下限1適用済み） */}
                <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
                  <AnimatedSword sword={mySwordData} mode="main" />
                  
                  <h3 style={{ margin: '5px 0', fontSize: '24px', color: '#222' }}>{mySwordData.name}</h3>
                  <div style={{ display: 'flex', gap: '15px', fontWeight: 'bold', backgroundColor: 'rgba(0,0,0,0.05)', padding: '6px 18px', borderRadius: '20px' }}>
                    <span style={{ color: '#d32f2f' }}>HP: {myFinalStats.hp}</span>
                    <span style={{ color: '#f57c00' }}>攻: {myFinalStats.attack}</span>
                    <span style={{ color: '#558b2f' }}>重: {myFinalStats.weight}</span>
                  </div>
                </div>

                {/* 右側：装備リスト（3枠固定＆D&D対応） */}
                <div style={{ width: '75px', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '12px', paddingLeft: '15px', borderLeft: '2px dashed #ddd' }}>
                  {[0, 1, 2].map((index) => {
                    const sword = swordList[index];
                    const isEquippedSlot = sword && mySwordData?.id === sword.id;
                    const disabled = me.ready || auto;
                    const isDraggingThis = draggedIndex === index;
                    const isTargeted = dragOverIndex === index;

                    if (sword) {
                      return (
                        <div
                          key={sword.id}
                          draggable={!disabled}
                          onDragStart={(e) => handleDragStart(e, index)}
                          onDragOver={(e) => handleDragOver(e, index)}
                          onDragLeave={handleDragLeave}
                          onDrop={(e) => handleDrop(e, index)}
                          onDragEnd={handleDragEnd}
                          style={{
                            position: 'relative',
                            cursor: !disabled ? 'grab' : 'default',
                            opacity: isDraggingThis ? 0.4 : 1,
                            transform: isTargeted ? 'scale(1.08)' : 'scale(1)',
                            transition: 'transform 0.15s ease, opacity 0.15s ease',
                            borderRadius: '10px',
                            boxShadow: isTargeted ? '0 0 8px rgba(33, 150, 243, 0.6)' : 'none'
                          }}
                        >
                          <span style={{ position: 'absolute', top: 2, left: 4, fontSize: '10px', fontWeight: 'bold', color: isEquippedSlot ? '#4CAF50' : '#888', zIndex: 11, pointerEvents: 'none' }}>
                            {index + 1}
                          </span>
                          <AnimatedSword
                            sword={sword}
                            mode="list"
                            isEquippedSlot={isEquippedSlot}
                            disabled={disabled}
                            opacity={disabled && !isEquippedSlot ? 0.5 : 1}
                            onClick={() => { if (!me.ready && !auto) equipSword(sword); }}
                          />
                        </div>
                      );
                    } else {
                      return (
                        <div
                          key={`empty-${index}`}
                          onDragOver={(e) => handleDragOver(e, index)}
                          onDragLeave={handleDragLeave}
                          onDrop={(e) => handleDrop(e, index)}
                          style={{
                            position: 'relative', width: '65px', height: '65px',
                            backgroundColor: isTargeted ? 'rgba(33, 150, 243, 0.1)' : 'rgba(0,0,0,0.03)',
                            borderRadius: '10px',
                            border: isTargeted ? '2px dashed #2196F3' : '2px dashed #ccc',
                            display: 'flex', justifyContent: 'center',
                            alignItems: 'center', color: '#aaa', fontSize: '11px', fontWeight: 'bold',
                            boxSizing: 'border-box',
                            transition: 'all 0.15s ease'
                          }}
                        >
                          <span style={{ position: 'absolute', top: 2, left: 4, fontSize: '10px', fontWeight: 'bold', color: '#aaa', pointerEvents: 'none' }}>
                            {index + 1}
                          </span>
                          EMPTY
                        </div>
                      );
                    }
                  })}
                </div>

              </div>

              {/* 下部：ボタン群 */}
              <div style={{ marginTop: '15px' }}>
                {auto ? (
                  <p style={{ margin: 0, padding: '12px 20px', backgroundColor: '#e8f5e9', borderRadius: '8px', fontWeight: 'bold', color: '#2e7d32', textAlign: 'center' }}>
                    この装備で参戦します
                  </p>
                ) : (
                  <div style={{ display: 'flex', gap: '15px', width: '100%', justifyContent: 'center' }}>
                    <button
                      style={{ flex: 1, padding: '14px', fontSize: '16px', backgroundColor: me.ready ? '#ccc' : '#607d8b', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: me.ready ? 'not-allowed' : 'pointer', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' }}
                      onClick={() => handleGoToCrafting("LOBBY")}
                      disabled={me.ready}
                    >
                      🔧 武器庫へ
                    </button>

                    {!me.ready ? (
                      <button
                        style={{ flex: 1.5, padding: '14px', fontSize: '16px', backgroundColor: '#4CAF50', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' }}
                        onClick={() => onReady(true)}
                      >
                        ✅ 準備完了
                      </button>
                    ) : (
                      <button
                        style={{ flex: 1.5, padding: '14px', fontSize: '16px', backgroundColor: '#f44336', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer', boxShadow: 'inset 0 4px 6px rgba(0,0,0,0.2)' }}
                        onClick={() => onReady(false)}
                      >
                        🔄 準備取消
                      </button>
                    )}
                  </div>
                )}
              </div>

            </div>
          ) : (
             <div style={{ height: '300px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>剣がありません</div>
          )}
        </div>

        {/* =========================================
            右上：他の3席
        ========================================= */}
        <div className="others-panel">
          {otherSeats.map(({ slot, player }) => {
            const playerFinalStats = player ? getFinalStats(player.swordData) : null;
            return (
              <div key={slot} className="lobby-panel mini-panel" style={{ display: 'flex', flexDirection: 'row', alignItems: 'center', gap: '15px', padding: '12px 15px', borderColor: player?.ready ? '#4CAF50' : '#ccc' }}>
                
                <div style={{ width: '80px', height: '85px', display: 'flex', justifyContent: 'center', alignItems: 'center', backgroundColor: '#f9f9f9', borderRadius: '10px', flexShrink: 0 }}>
                  {player ? (
                    <AnimatedSword sword={player.swordData} mode="other" />
                  ) : (
                    <div style={{ color: '#ccc', fontSize: '12px', fontWeight: 'bold' }}>EMPTY</div>
                  )}
                </div>

                <div style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'center', textAlign: 'left' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
                    <span style={{ fontWeight: 'bold', fontSize: '18px', color: PLAYER_COLORS[slot] }}>
                      {slot + 1}P {player && player.playerId === 'p0' && "(ホスト)"}
                    </span>
                    {player && (
                      <span style={{ backgroundColor: player.ready ? '#4CAF50' : '#9e9e9e', color: 'white', padding: '2px 8px', borderRadius: '12px', fontSize: '11px', fontWeight: 'bold' }}>
                        {!player.connected ? "切断" : !player.inLobby ? "結果確認中" : player.ready ? "✅ 準備OK" : "⏳ 準備中"}
                      </span>
                    )}
                  </div>

                  {player ? (
                    <>
                      <p style={{ margin: '0 0 2px 0', fontWeight: 'bold', fontSize: '15px', color: '#333' }}>{player.swordData.name}</p>
                      <p style={{ margin: 0, fontSize: '12px', color: '#666' }}>
                        HP:{playerFinalStats.hp} / 攻:{playerFinalStats.attack} / 重:{playerFinalStats.weight}
                      </p>
                    </>
                  ) : (
                    <p style={{ margin: 0, fontSize: '13px', color: '#aaa', fontStyle: 'italic' }}>
                      参加を待っています…
                    </p>
                  )}
                </div>

              </div>
            );
          })}
        </div>

        {/* 左下：ルールと設定 */}
        <div className="lobby-panel rule-panel" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          <h3 style={{ margin: '0 0 10px 0', color: '#333', fontSize: '20px' }}>⚔️ バトルルール</h3>

          <div style={{ display: 'flex', gap: '20px', alignItems: 'center' }}>
            <div style={{ width: '90px', height: '90px', backgroundColor: '#e0f7fa', borderRadius: '10px', display: 'flex', justifyContent: 'center', alignItems: 'center', fontSize: '36px', flexShrink: 0 }}>
              {gameMode === "1" ? "🌀" : "🗡️"}
            </div>

            <div style={{ flex: 1, textAlign: 'left' }}>
              <h4 style={{ margin: '0 0 5px 0', fontSize: '20px' }}>
                {gameMode === "1" ? "独楽（見下ろし）モード" : "剣（横視点・重力）モード"}
                {livesMode && <span style={{ marginLeft: '8px', fontSize: '13px', color: '#c62828' }}>⚔️ 残機制</span>}
              </h4>
              <p style={{ margin: '0 0 10px 0', color: '#666', fontSize: '13px' }}>
                {playerCount >= 3 ? "最後の1人になるまで戦う個人戦" : gameMode === "1" ? "独楽のようにぶつかり合う半自動戦闘モード" : "剣を振り回して戦うモード"}
                {livesMode && "（所持している剣がすべて撃破されるまで敗北しません）"}
              </p>

              {isHost && !auto && (
                <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                  <button
                    style={{ padding: '6px 12px', backgroundColor: '#2196F3', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer', fontSize: '12px' }}
                    onClick={() => onGameMode(gameMode === "1" ? "0" : "1")}
                  >
                    ルールを変更する
                  </button>
                  <button
                    style={{ padding: '6px 12px', backgroundColor: livesMode ? '#c62828' : '#9e9e9e', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer', fontSize: '12px' }}
                    onClick={() => onLivesMode(!livesMode)}
                  >
                    {livesMode ? "⚔️ 残機制: ON" : "残機制: OFF"}
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* 右下：ID・開始・退出 */}
        <div className="lobby-panel system-panel" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
          {auto ? (
            <div style={{ backgroundColor: '#fdecea', padding: '12px', borderRadius: '8px', marginBottom: '12px' }}>
              <span style={{ fontSize: '12px', color: '#666', display: 'block' }}>対戦形式</span>
              <span style={{ fontSize: '18px', fontWeight: 'bold', color: '#c62828' }}>⚡ ランダムマッチ</span>
            </div>
          ) : (
            <div style={{ backgroundColor: '#e8eaf6', padding: '12px', borderRadius: '8px', marginBottom: '12px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div style={{ textAlign: 'left' }}>
                <span style={{ fontSize: '11px', color: '#666', display: 'block' }}>ロビーID</span>
                <span style={{ fontSize: '22px', fontWeight: 'bold', letterSpacing: '2px', color: '#3f51b5' }}>{roomId || "----"}</span>
              </div>
              <button
                onClick={handleCopyId}
                style={{ padding: '8px 12px', backgroundColor: isCopied ? '#4CAF50' : '#fff', color: isCopied ? '#fff' : '#333', border: '1px solid #ccc', borderRadius: '5px', cursor: 'pointer', fontWeight: 'bold', fontSize: '12px' }}
              >
                {isCopied ? "✓ コピー" : "📋 コピー"}
              </button>
            </div>
          )}

          <p style={{ margin: '0 0 10px 0', fontWeight: 'bold', color: '#333', fontSize: '14px' }}>
            現在 {playerCount}人 ／ 最大 {seatLimit}人
            <span style={{ display: 'block', fontSize: '12px', fontWeight: 'normal', color: '#666', marginTop: '2px' }}>{status}</span>
          </p>

          {isHost && !auto && (
            <button
              style={{ padding: '14px', fontSize: '18px', backgroundColor: view.canStart ? '#d32f2f' : '#ccc', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: view.canStart ? 'pointer' : 'not-allowed', width: '100%', marginBottom: '10px' }}
              onClick={onStart}
              disabled={!view.canStart}
            >
              ⚔️ {playerCount}人で対戦開始
            </button>
          )}

          <button
            style={{ padding: '12px', fontSize: '16px', backgroundColor: '#666', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer', width: '100%' }}
            onClick={onLeave}
          >
            退出する
          </button>

          {error && (
            <p style={{ ...styles.errorMessage, margin: '10px 0 0 0', fontSize: '13px' }}>⚠️ {error}</p>
          )}
        </div>

      </div>

      <style>{`
        @keyframes float {
          0%, 100% { transform: translateY(0); }
          50% { transform: translateY(-8px); }
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

        .lobby-grid { 
          display: grid; 
          grid-template-columns: 1fr; 
          gap: 15px; 
          width: 100%; 
          max-width: 1100px; 
          margin: 0 auto; 
        }
        
        .lobby-panel { 
          background-color: rgba(255, 255, 255, 0.95); 
          border-radius: 15px; 
          padding: 18px; 
          box-shadow: 0 4px 10px rgba(0,0,0,0.1); 
          box-sizing: border-box; 
        }
        
        .others-panel { 
          display: flex; 
          flex-direction: column; 
          gap: 12px; 
        }
        
        .mini-panel { 
          border-left: 5px solid #ccc; 
          flex: 1;
        }

        @media (min-width: 800px) {
          .lobby-grid { 
            grid-template-columns: 1.5fr 1fr; 
            grid-template-rows: auto auto; 
            align-items: stretch;
          }
          .self-panel { grid-column: 1 / 2; grid-row: 1 / 2; }
          .others-panel { grid-column: 2 / 3; grid-row: 1 / 2; }
          .rule-panel { grid-column: 1 / 2; grid-row: 2 / 3; }
          .system-panel { grid-column: 2 / 3; grid-row: 2 / 3; }
        }
      `}</style>
    </div>
  );
}