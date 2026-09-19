import { useState, useRef, useEffect, useCallback } from 'react';
import './App.css';
import { styles } from './styles';

import TitleScreen from './screens/TitleScreen';
import LobbyScreen from './screens/LobbyScreen';
import ResultScreen from './screens/ResultScreen';
import { NameInputScreen, CraftPoseScreen, CraftingApiScreen, CraftCompleteScreen } from './screens/CraftingScreens';
import SwordListScreen, { HILT_DATABASE } from './screens/SwordListScreen';
import MatchmakingScreen from './screens/MatchmakingScreen';
import BattleArena from './components/BattleArena.jsx';
import HowToPlayPanel from './components/HowToPlayPanel.jsx';
import { useRoom } from './network/useRoom.js';
import { useRandomMatch } from './network/useRandomMatch.js';
import { useIceConfig } from './network/useIceConfig.js';

const ACTIVE_PHASES = ['LOADING', 'COUNTDOWN', 'PLAYING'];
const ROOM_STEPS = ['LOBBY', 'PLAYING', 'RESULT'];

// 🌟 画像を真っ黒なシルエットに変換する関数
const makeSilhouetteImage = (base64Src) => {
  return new Promise((resolve, reject) => {
    if (!base64Src) return resolve("");
    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement("canvas");
      canvas.width = img.width;
      canvas.height = img.height;
      const ctx = canvas.getContext("2d");
      ctx.drawImage(img, 0, 0);
      ctx.globalCompositeOperation = "source-in";
      ctx.fillStyle = "#000000";
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      resolve(canvas.toDataURL("image/png"));
    };
    img.onerror = reject;
    img.src = base64Src;
  });
};

// 武器の中身が変わったかどうかだけを見る。
const swordKey = sword => sword && `${sword.id}:${sword.name}:${sword.hp}:${sword.attack}:${sword.weight}:${sword.hiltType}:${sword.imageStr?.length ?? 0}`;

// 🌟 残機モード用のswords[](装備中以外も含む3本分)まで含めた同期判定キー
const syncKey = sync => {
  if (!sync) return null;
  const slots = (sync.swords || [])
    .map(s => `${s.name}:${s.hp}:${s.attack}:${s.weight}:${s.imageStr?.length ?? 0}:${s.hiltType ?? '0'}:${s.isEmpty ? 1 : 0}`)
    .join('|');
  return `${swordKey(sync)}:${sync.equippedIndex}:${slots}`;
};

// 🌟 柄の補正適用および下限1の設定を行うヘルパー関数
const getFinalStats = (sword) => {
  if (!sword) return { hp: 1, attack: 1, weight: 1 };
  const hilt = HILT_DATABASE[sword.hiltType || "0"] || HILT_DATABASE["0"];
  return {
    hp: Math.max(1, (sword.hp || 0) + (hilt.hpBonus || 0)),
    attack: Math.max(1, (sword.attack || 0) + (hilt.attackBonus || 0)),
    weight: Math.max(1, (sword.weight || 0) + (hilt.weightBonus || 0)),
  };
};

export default function PoseSwordWeb() {
  const [step, setStep] = useState("TITLE");
  const [titleMode, setTitleMode] = useState("DEFAULT");
  const [showHowToPlay, setShowHowToPlay] = useState(false);
  const [craftReturnStep, setCraftReturnStep] = useState("TITLE");

  const [swordList, setSwordList] = useState([]);
  const [craftingTargetId, setCraftingTargetId] = useState(null);
  const [mySwordData, setMySwordData] = useState(null);
  const mySwordRef = useRef(null);
  useEffect(() => { mySwordRef.current = mySwordData; }, [mySwordData]);

// 🌟 通信・Unity送信用のデータ生成（isRandomMatchフラグで画像をすり替え）
  const createSyncSwordData = (list, equipped, isRandomMatch = false) => {
    if (!equipped) return null;

    const swords = [0, 1, 2].map(index => {
      const sword = list[index];
      if (sword) {
        const stats = getFinalStats(sword);
        
        // 🌟 シルエットを使う場合、imageSrc と imageStr の両方をすり替える
        const sendSrc = (isRandomMatch && sword.silhouetteSrc) ? sword.silhouetteSrc : sword.imageSrc;
        // silhouetteSrc に含まれる "data:image/png;base64," の部分を消して抽出
        const sendStr = (isRandomMatch && sword.silhouetteSrc) ? sword.silhouetteSrc.split(',')[1] : sword.imageStr;

        return {
          name: sword.name,
          hp: stats.hp,
          attack: stats.attack,
          weight: stats.weight,
          imageStr: sendStr, // 🌟 Unityが画像を生成する時に使うデータ
          imageSrc: sendSrc, // 🌟 相手のReact画面で表示する時に使うデータ
          imageStr: sword.imageStr,
          hiltType: typeof sword.hiltType === 'string' ? sword.hiltType : String(sword.hiltType || 0),
          isEmpty: false
        };
      } else {
        return {
          name: "empty",
          hp: 1,
          attack: 1,
          weight: 1,
          imageStr: "",
          hiltType: "0",
          isEmpty: true
        };
      }
    });

    const equippedIndex = Math.max(0, list.findIndex(s => s.id === equipped.id));
    const equippedStats = getFinalStats(equipped);
    
    // 🌟 装備中の剣（自分自身のメイン剣）についても同様に両方をすり替え
    const mainSendSrc = (isRandomMatch && equipped.silhouetteSrc) ? equipped.silhouetteSrc : equipped.imageSrc;
    const mainSendStr = (isRandomMatch && equipped.silhouetteSrc) ? equipped.silhouetteSrc.split(',')[1] : equipped.imageStr;

    return {
      ...equipped,
      hp: equippedStats.hp,
      attack: equippedStats.attack,
      weight: equippedStats.weight,
      imageStr: mainSendStr, // 🌟 修正：ここが元の画像のままになっていたのが原因
      imageSrc: mainSendSrc, 
      hiltType: typeof equipped.hiltType === 'string' ? equipped.hiltType : String(equipped.hiltType || 0),
      swords: swords,
      equippedIndex: equippedIndex
    };
  };

  const [userName, setUserName] = useState("");
  const [targetId, setTargetId] = useState("");
  const [captureCountdown, setCaptureCountdown] = useState(null);
  const videoRef = useRef(null);
  const canvasRef = useRef(null);
  const [capturedImage, setCapturedImage] = useState(null);
  const [systemMessage, setSystemMessage] = useState("");
  const [isCopied, setIsCopied] = useState(false);

  // 🌟 ランダムマッチ状態の管理を追加
  const [isRandomMatchActive, setIsRandomMatchActive] = useState(false);

  // 🌟 アニメーション・撮り直し用の状態管理
  const [transitionDir, setTransitionDir] = useState("forward");
  const [lastCraftWasRecapture, setLastCraftWasRecapture] = useState(false);
  const [isFlash, setIsFlash] = useState(false);

  const resetToTitleRef = useRef(null);
  // STUN/TURN は毎回サーバーに発行してもらう。固定の資格情報を埋め込まない。
  const { peerOptions } = useIceConfig();
  const room = useRoom({
    peerOptions,
    onClosed: message => resetToTitleRef.current?.(message || "ロビーとの接続が終了しました。"),
  });
  const view = room.view;
  const sentSwordRef = useRef(null);
  const [matchSize, setMatchSize] = useState(2);
  const [matchMode, setMatchMode] = useState("0");

  // 🌟 isRandomMatchActive の状態を渡してデータを生成
  const currentSyncSword = createSyncSwordData(swordList, mySwordData, isRandomMatchActive);
  const randomMatch = useRandomMatch({ room, sword: currentSyncSword });

  const resetToTitle = useCallback((msg = "") => {
    setTitleMode("DEFAULT"); setTargetId(""); setSystemMessage(msg); setStep("TITLE");
    setIsRandomMatchActive(false); // 🌟 追加
    sentSwordRef.current = null;
  }, []);
  useEffect(() => { resetToTitleRef.current = resetToTitle; }, [resetToTitle]);

  // ▼【復元】決着演出(2.5秒)を見せるためのディレイ
  const resultMatchId = view?.result?.matchId ?? null;
  const [resultDelayDone, setResultDelayDone] = useState(false);
  useEffect(() => {
    if (!resultMatchId) { setResultDelayDone(false); return; }
    setResultDelayDone(false);
    const timer = setTimeout(() => setResultDelayDone(true), 2500);
    return () => clearTimeout(timer);
  }, [resultMatchId]);

  const roomScreen = (() => {
    if (!view?.room || view.closed) return null;
    const me = view.room.players.find(p => p.playerId === view.localPlayerId);
    if (ACTIVE_PHASES.includes(view.room.phase)) return "PLAYING";
    if (view.result && me && !me.inLobby) return resultDelayDone ? "RESULT" : "PLAYING";
    return "LOBBY";
  })();
  const seeking = Boolean(randomMatch.view?.seeking);

  // 🌟 displayScreen を廃止し、直接 screen を使ってアニメーションを即座に発動させる
  const screen = seeking ? "MATCHING"
    : ROOM_STEPS.includes(step) || step === "TITLE" ? (roomScreen ?? "TITLE")
    : step;

  useEffect(() => {
    if (!view?.room || !mySwordData) return;
    const key = syncKey(currentSyncSword);
    if (sentSwordRef.current === key) return;
    sentSwordRef.current = key;
    room.updateSword(currentSyncSword);
  }, [mySwordData, swordList, view?.room, room, currentSyncSword]);

  useEffect(() => {
    let stream = null;
    if (step === "CRAFT_POSE") {
      navigator.mediaDevices.getUserMedia({ video: true })
        .then((s) => { stream = s; if (videoRef.current) videoRef.current.srcObject = stream; })
        .catch((err) => console.error("カメラエラー:", err));
    }
    return () => { if (stream) stream.getTracks().forEach(track => track.stop()); };
  }, [step]);

  useEffect(() => {
    if (captureCountdown !== null) {
      if (captureCountdown > 0) {
        const timer = setTimeout(() => setCaptureCountdown(captureCountdown - 1), 1000);
        return () => clearTimeout(timer);
      } else {
        setCaptureCountdown(null);
        executeCaptureAndCraft();
      }
    }
  }, [captureCountdown]);

  const equipSword = (sword) => setMySwordData(sword);
  const reorderSwords = (newList) => setSwordList(newList);

  const deleteSword = (idToRemove) => {
    if (!window.confirm("本当にこの剣を破棄しますか？")) return;
    const newList = swordList.filter(s => s.id !== idToRemove);
    setSwordList(newList);
    if (mySwordRef.current?.id === idToRemove) setMySwordData(newList.length > 0 ? newList[0] : null);
  };

  const updateSword = (targetId, newProps) => {
    setSwordList(prev => {
      const newList = prev.map(s => s.id === targetId ? { ...s, ...newProps } : s);
      return newList;
    });
    if (mySwordRef.current?.id === targetId) setMySwordData(prev => ({ ...prev, ...newProps }));
  };

  const toggleSwordFlip = (targetId) => {
    const target = swordList.find(s => s.id === targetId);
    if (!target) return;
    const img = new Image();
    img.src = target.imageSrc;
    img.onload = () => {
      const canvas = document.createElement('canvas');
      canvas.width = img.width; canvas.height = img.height;
      const ctx = canvas.getContext('2d');
      ctx.translate(canvas.width, 0); ctx.scale(-1, 1); ctx.drawImage(img, 0, 0);
      const flippedSrc = canvas.toDataURL('image/png');
      const flippedStr = flippedSrc.split(',')[1];
      updateSword(targetId, { imageSrc: flippedSrc, imageStr: flippedStr });
    };
  };

  const startRecapture = (targetId) => {
    const target = swordList.find(s => s.id === targetId);
    if (target) {
      setCraftingTargetId(targetId); setUserName(target.baseName);
      setTransitionDir("forward");
      setStep("CRAFT_POSE");
    }
  };

  const startNewCrafting = () => {
    setCraftingTargetId(null); setUserName("");
    setTransitionDir("forward");
    setStep("NAME_INPUT");
  };

  const goToCrafting = (returnStep) => {
    if (returnStep === "LOBBY") room.setReady(false);
    setCraftReturnStep(returnStep);
    setTransitionDir("forward");
    if (swordList.length === 0) {
      setCraftingTargetId(null); setUserName(""); setStep("NAME_INPUT");
    } else {
      setStep("SWORD_LIST");
    }
  };

  const cancelList = () => {
    setTransitionDir("back");
    setStep(craftReturnStep);
  };

  const handleLeave = () => {
    randomMatch.cancel();
    room.leave();
    resetToTitle("");
  };

  const openRandomMatch = () => {
    if (!mySwordData) return;
    setSystemMessage(""); setTitleMode("MATCH_SIZE");
  };

  const startRandomMatch = (size) => {
    if (!mySwordData) return;
    setMatchSize(size);
    setSystemMessage(""); setTitleMode("DEFAULT");
    setIsRandomMatchActive(true); // 🌟 追加
    sentSwordRef.current = syncKey(createSyncSwordData(swordList, mySwordData, true)); // 🌟 追加
    randomMatch.start(size, matchMode);
  };

  const cancelRandomMatch = () => {
    randomMatch.cancel();
    setIsRandomMatchActive(false); // 🌟 追加
    resetToTitle("");
  };

  const findNewOpponents = () => {
    room.leave();
    setIsRandomMatchActive(true); // 🌟 追加
    sentSwordRef.current = syncKey(createSyncSwordData(swordList, mySwordData, true)); // 🌟 追加
    randomMatch.start(matchSize, matchMode);
  };

  const handleCopyId = () => {
    if (!room.roomId) return;
    navigator.clipboard.writeText(room.roomId).then(() => {
      setIsCopied(true); setTimeout(() => setIsCopied(false), 2000);
    }).catch(e => console.error(e));
  };

  const handleCreateRoom = () => {
    if (!mySwordData) return;
    setSystemMessage(""); setTitleMode("DEFAULT");
    setIsRandomMatchActive(false); // 🌟 追加
    sentSwordRef.current = syncKey(createSyncSwordData(swordList, mySwordData, false)); // 🌟 追加
    room.createRoom(createSyncSwordData(swordList, mySwordData, false)); // 🌟 追加
  };

  const connectToHost = () => {
    setSystemMessage("");
    if (!targetId.trim()) return setSystemMessage("IDを入力してください。");
    if (!/^\d+$/.test(targetId)) return setSystemMessage("半角数字のみで入力してください。");
    if (targetId.length < 6) return setSystemMessage("6桁で入力してください。");
    if (!mySwordData) return setSystemMessage("先に剣を錬成してください。");

    setIsRandomMatchActive(false); // 🌟 追加
    sentSwordRef.current = syncKey(createSyncSwordData(swordList, mySwordData, false)); // 🌟 追加
    room.joinRoom(targetId, createSyncSwordData(swordList, mySwordData, false)); // 🌟 追加
  };

  const handleJoinRoom = () => {
    setSystemMessage(""); setTargetId(""); setTitleMode("JOIN_INPUT");
  };

  const handleCancelJoin = () => {
    setTitleMode("DEFAULT"); setSystemMessage("");
    room.leave();
  };

  const startCaptureCountdown = () => setCaptureCountdown(5);

  const executeCaptureAndCraft = () => {
    setIsFlash(true);
    setTimeout(() => setIsFlash(false), 500);

    setTimeout(() => {
      setStep("CRAFTING_API");
      const video = videoRef.current;
      const canvas = canvasRef.current;
      if (!video || !canvas) return;
      canvas.width = video.videoWidth; canvas.height = video.videoHeight;
      const context = canvas.getContext('2d');
      context.save(); context.scale(-1, 1); context.translate(-canvas.width, 0);
      context.drawImage(video, 0, 0, canvas.width, canvas.height);
      context.restore();

      const base64Full = canvas.toDataURL('image/jpeg');
      setCapturedImage(base64Full);
      const base64DataOnly = base64Full.split(',')[1];
      const pythonApiUrl = `${import.meta.env.VITE_API_URL ?? '/api'}/cutout`;

      // 🌟 asyncを追加
      fetch(pythonApiUrl, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ imageData: base64DataOnly, userName: userName }) })
        .then(res => { if (!res.ok) throw new Error(`HTTPエラー`); return res.json(); })
        .then(async (data) => {
          setLastCraftWasRecapture(!!craftingTargetId);
          
          // 🌟 シルエット画像の生成処理を追加
          const newImageSrc = "data:image/png;base64," + data.imageData;
          const silhouette = await makeSilhouetteImage(newImageSrc);

          if (craftingTargetId) {
            const updatedProps = { hp: data.params.hp, attack: data.params.attack, weight: data.params.weight, imageStr: data.imageData, imageSrc: newImageSrc, silhouetteSrc: silhouette };
            updateSword(craftingTargetId, updatedProps);
            const targetOld = swordList.find(s => s.id === craftingTargetId);
            if (targetOld) setMySwordData({ ...targetOld, ...updatedProps });
            setCraftingTargetId(null);
          } else {
            const newSword = { id: Date.now().toString(), baseName: userName, name: data.swordName || "無銘の剣", hp: data.params.hp, attack: data.params.attack, weight: data.params.weight, imageStr: data.imageData, imageSrc: newImageSrc, silhouetteSrc: silhouette, hiltType: "0" };
            setSwordList(prev => {
              let updatedList = [...prev];
              if (updatedList.length >= 3) updatedList.shift();
              updatedList.push(newSword);
              return updatedList;
            });
            setMySwordData(newSword);
          }
          setStep("CRAFT_COMPLETE");
        })
        .catch((error) => { console.error(error); alert("AIサーバーとの通信に失敗しました。"); setStep("CRAFT_POSE"); });
    }, 50);
  };

  const handleCancelCrafting = () => {
    setUserName(""); setCraftingTargetId(null);
    setTransitionDir("back");
    if (swordList.length > 0) setStep("SWORD_LIST");
    else setStep(craftReturnStep);
  };

  const handleBackFromPose = () => {
    setTransitionDir("back");
    if (craftingTargetId !== null) {
      setCraftingTargetId(null); setStep("SWORD_LIST");
    } else {
      setStep("NAME_INPUT");
    }
  };

  const renderScreen = () => {
    switch (screen) {
      case "TITLE":
        return <TitleScreen mySwordData={mySwordData} titleMode={titleMode} targetId={targetId} setTargetId={setTargetId} systemMessage={room.error || systemMessage} goToCrafting={goToCrafting} handleCreateRoom={handleCreateRoom} handleJoinRoom={handleJoinRoom} handleCancelJoin={handleCancelJoin} connectToHost={connectToHost} connecting={room.connecting} openRandomMatch={openRandomMatch} startRandomMatch={startRandomMatch} matchMode={matchMode} setMatchMode={setMatchMode} direction={transitionDir} onOpenHowToPlay={() => setShowHowToPlay(true)}/>;
      case "MATCHING":
        return <MatchmakingScreen view={randomMatch.view} mySwordData={mySwordData} gameMode={matchMode} onCancel={cancelRandomMatch} />;
      case "NAME_INPUT":
        return <NameInputScreen direction={transitionDir} userName={userName} setUserName={setUserName} mySwordData={mySwordData} setMySwordData={setMySwordData} setStep={setStep} handleCancel={handleCancelCrafting} />;
      case "CRAFT_POSE":
        return <CraftPoseScreen direction={transitionDir} videoRef={videoRef} canvasRef={canvasRef} captureCountdown={captureCountdown} startCaptureCountdown={startCaptureCountdown} forceCapture={() => setCaptureCountdown(0)} handleBack={handleBackFromPose} />;
      case "CRAFTING_API":
        return <CraftingApiScreen capturedImage={capturedImage} />;
      case "CRAFT_COMPLETE":
        return <CraftCompleteScreen mySwordData={mySwordData} setStep={(s) => { setTransitionDir("forward"); setStep(s); }} startNewCrafting={startNewCrafting} craftReturnStep={craftReturnStep} swordListLength={swordList.length} isRecapture={lastCraftWasRecapture} />;
      case "SWORD_LIST":
        return <SwordListScreen direction={transitionDir} swordList={swordList} mySwordData={mySwordData} equipSword={equipSword} deleteSword={deleteSword} startNewCrafting={startNewCrafting} startRecapture={startRecapture} updateSword={updateSword} cancelList={cancelList} toggleSwordFlip={toggleSwordFlip} />;
      case "LOBBY":
        return <LobbyScreen view={view} roomId={room.roomId} isCopied={isCopied} handleCopyId={handleCopyId} swordList={swordList} mySwordData={mySwordData} equipSword={equipSword} reorderSwords={reorderSwords} onReady={room.setReady} onGameMode={room.setGameMode} onLivesMode={room.setLivesMode} onSoloMode={room.setSoloMode} onBossPlayer={room.setBossPlayer} onStart={room.start} onLeave={handleLeave} goToCrafting={goToCrafting} error={room.error} />;
      case "RESULT":
        return <ResultScreen view={view} onReturnToLobby={room.returnToLobby} onLeave={handleLeave} onFindNewOpponents={view?.room?.autoStart ? findNewOpponents : null} />;
      default: return null;
    }
  };

  return (
    <div style={{ fontFamily: 'sans-serif', textAlign: 'center', backgroundColor: '#f5f5f5', minHeight: '100vh', position: 'relative', overflow: 'hidden' }}>
      
      {/* 撮影フラッシュ演出用のスタイル */}
      <style>{`
        @keyframes flashFade {
          0% { opacity: 1; }
          100% { opacity: 0; }
        }
      `}</style>
      {isFlash && <div style={{ position: 'fixed', inset: 0, backgroundColor: '#fff', zIndex: 9999, pointerEvents: 'none', animation: 'flashFade 0.5s ease-out forwards' }}></div>}

      {/* 遊び方パネル（画面遷移せず、常に最前面にオーバーレイ表示） */}
      <HowToPlayPanel open={showHowToPlay} onClose={() => setShowHowToPlay(false)} />

      {/* 画面描画（遅延なしで瞬時に切り替わり、各画面のCSSアニメーションが発動します） */}
      {screen !== "PLAYING" && renderScreen()}

      {/* Unity側の描画 */}
      {room.hasArena && (
        <div style={{ ...styles.unityContainer, display: screen === "PLAYING" ? 'flex' : 'none', margin: '0 auto' }}>
          <BattleArena bridge={room.bridge} view={view} onLoadFailed={room.reportLoadFailure} />
        </div>
      )}
      {screen === "PLAYING" && !room.hasArena && (
        <div style={{ ...styles.container, justifyContent: 'center', minHeight: '100vh' }}>
          <p style={{ fontSize: '20px', fontWeight: 'bold' }}>対戦を準備しています…</p>
        </div>
      )}
    </div>
  );
}