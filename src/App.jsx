// src/App.jsx
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
import { useRoom } from './network/useRoom.js';
import { useRandomMatch } from './network/useRandomMatch.js';

const PEER_ICE_CONFIG = {
  config: {
    iceServers: [
      { urls: 'stun:stun.l.google.com:19302' },
      { urls: 'stun:stun1.l.google.com:19302' },
      { urls: 'turn:openrelay.metered.ca:80', username: 'openrelayproject', credential: 'openrelayproject' },
      { urls: 'turn:openrelay.metered.ca:443', username: 'openrelayproject', credential: 'openrelayproject' },
      { urls: 'turn:openrelay.metered.ca:443?transport=tcp', username: 'openrelayproject', credential: 'openrelayproject' },
    ]
  }
};

const ACTIVE_PHASES = ['LOADING', 'COUNTDOWN', 'PLAYING'];
const ROOM_STEPS = ['LOBBY', 'PLAYING', 'RESULT'];
const swordKey = sword => sword && `${sword.id}:${sword.name}:${sword.hp}:${sword.attack}:${sword.weight}:${sword.hiltType}:${sword.imageStr?.length ?? 0}`;

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
  const [craftReturnStep, setCraftReturnStep] = useState("TITLE");

  const [swordList, setSwordList] = useState([]);
  const [craftingTargetId, setCraftingTargetId] = useState(null);
  const [mySwordData, setMySwordData] = useState(null);
  const mySwordRef = useRef(null);
  useEffect(() => { mySwordRef.current = mySwordData; }, [mySwordData]);

  // 🌟 通信・Unity送信用のデータ生成（柄のステータス補正を反映）
  const createSyncSwordData = (list, equipped) => {
    if (!equipped) return null;

    // 3本分の配列を生成（無いスロットは空のダミー）
    const swords = [0, 1, 2].map(index => {
      const sword = list[index];
      if (sword) {
        const stats = getFinalStats(sword);
        return {
          name: sword.name,
          hp: stats.hp,         // ★ 補正適用後（下限1）
          attack: stats.attack, // ★ 補正適用後（下限1）
          weight: stats.weight, // ★ 補正適用後（下限1）
          imageStr: sword.imageStr,
          hiltType: Number(sword.hiltType || 0),
          isEmpty: false
        };
      } else {
        return {
          name: "empty",
          hp: 1,
          attack: 1,
          weight: 1,
          imageStr: "",
          hiltType: 0,
          isEmpty: true
        };
      }
    });

    const equippedIndex = Math.max(0, list.findIndex(s => s.id === equipped.id));
    const equippedStats = getFinalStats(equipped);

    const result = {
      ...equipped,
      hp: equippedStats.hp,         // ★ メイン装備のステータスも補正済みに上書き
      attack: equippedStats.attack,
      weight: equippedStats.weight,
      hiltType: Number(equipped.hiltType || 0),
      swords: swords,
      equippedIndex: equippedIndex
    };
    return result;
  };

  const currentSyncSword = createSyncSwordData(swordList, mySwordData);

  const [userName, setUserName] = useState("");
  const [targetId, setTargetId] = useState("");
  const [captureCountdown, setCaptureCountdown] = useState(null);
  const videoRef = useRef(null);
  const canvasRef = useRef(null);
  const [capturedImage, setCapturedImage] = useState(null);
  const [systemMessage, setSystemMessage] = useState("");
  const [isCopied, setIsCopied] = useState(false);

  // 画面遷移の方向と、撮り直しの判定
  const [transitionDir, setTransitionDir] = useState("forward");
  const [lastCraftWasRecapture, setLastCraftWasRecapture] = useState(false);

  // 撮影時のフラッシュ演出用
  const [isFlash, setIsFlash] = useState(false);

  const resetToTitleRef = useRef(null);
  const room = useRoom({
    peerOptions: PEER_ICE_CONFIG,
    onClosed: message => resetToTitleRef.current?.(message || "ロビーとの接続が終了しました。"),
  });
  const view = room.view;
  const sentSwordRef = useRef(null);
  const [matchSize, setMatchSize] = useState(2);
  const [matchMode, setMatchMode] = useState("0");
  const randomMatch = useRandomMatch({ room, sword: currentSyncSword });

  const resetToTitle = useCallback((msg = "") => {
    setTitleMode("DEFAULT"); setTargetId(""); setSystemMessage(msg); setStep("TITLE");
    sentSwordRef.current = null;
  }, []);
  useEffect(() => { resetToTitleRef.current = resetToTitle; }, [resetToTitle]);

  const roomScreen = (() => {
    if (!view?.room || view.closed) return null;
    const me = view.room.players.find(p => p.playerId === view.localPlayerId);
    if (ACTIVE_PHASES.includes(view.room.phase)) return "PLAYING";
    if (view.result && me && !me.inLobby) return "RESULT";
    return "LOBBY";
  })();
  const seeking = Boolean(randomMatch.view?.seeking);

  const logicalScreen = seeking ? "MATCHING"
    : ROOM_STEPS.includes(step) || step === "TITLE" ? (roomScreen ?? "TITLE")
      : step;

  const [displayScreen, setDisplayScreen] = useState(logicalScreen);
  const [isBlackout, setIsBlackout] = useState(false);

  useEffect(() => {
    if (logicalScreen === displayScreen) return;

    const isNetworkTransition =
      ROOM_STEPS.includes(logicalScreen) || logicalScreen === "MATCHING" ||
      ROOM_STEPS.includes(displayScreen) || displayScreen === "MATCHING";

    if (isNetworkTransition) {
      setIsBlackout(true);
      const timer = setTimeout(() => {
        setDisplayScreen(logicalScreen);
        setIsBlackout(false);
      }, 300);
      return () => clearTimeout(timer);
    } else {
      setDisplayScreen(logicalScreen);
    }
  }, [logicalScreen, displayScreen]);

  useEffect(() => {
    if (displayScreen === "LOBBY" || displayScreen === "PLAYING" || room?.error) {
      setIsBlackout(false);
    }
  }, [displayScreen, room?.error]);

  // 🌟 装備変更やデータ更新時に補正済みの最新情報をルームに送信
  useEffect(() => {
    if (!view?.room || !mySwordData) return;
    const key = swordKey(mySwordData);
    if (sentSwordRef.current === key) return;
    sentSwordRef.current = key;
    room.updateSword(currentSyncSword);
  }, [mySwordData, view?.room, room, currentSyncSword]);

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

  // 🌟 ロビーや武器庫からの並び替え関数
  const reorderSwords = (newList) => {
    setSwordList(newList);
  };

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
    sentSwordRef.current = swordKey(mySwordData);
    randomMatch.start(size, matchMode);
  };

  const cancelRandomMatch = () => {
    randomMatch.cancel();
    resetToTitle("");
  };

  const findNewOpponents = () => {
    room.leave();
    sentSwordRef.current = swordKey(mySwordData);
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
    sentSwordRef.current = swordKey(mySwordData);
    room.createRoom(currentSyncSword);
  };

  const connectToHost = () => {
    setSystemMessage("");
    if (!targetId.trim()) return setSystemMessage("IDを入力してください。");
    if (!/^\d+$/.test(targetId)) return setSystemMessage("半角数字のみで入力してください。");
    if (targetId.length < 6) return setSystemMessage("6桁で入力してください。");
    if (!mySwordData) return setSystemMessage("先に剣を錬成してください。");

    sentSwordRef.current = swordKey(mySwordData);
    room.joinRoom(targetId, currentSyncSword);
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
      canvas.width = video.videoWidth; canvas.height = video.videoHeight;
      const context = canvas.getContext('2d');
      context.save(); context.scale(-1, 1); context.translate(-canvas.width, 0);
      context.drawImage(videoRef.current, 0, 0, canvas.width, canvas.height);
      context.restore();

      const base64Full = canvas.toDataURL('image/jpeg');
      setCapturedImage(base64Full);
      const base64DataOnly = base64Full.split(',')[1];
      const pythonApiUrl = `${import.meta.env.VITE_API_URL ?? '/api'}/cutout`;

      fetch(pythonApiUrl, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ imageData: base64DataOnly, userName: userName }) })
        .then(res => { if (!res.ok) throw new Error(`HTTPエラー`); return res.json(); })
        .then((data) => {
          setLastCraftWasRecapture(!!craftingTargetId);

          if (craftingTargetId) {
            const updatedProps = { hp: data.params.hp, attack: data.params.attack, weight: data.params.weight, imageStr: data.imageData, imageSrc: "data:image/png;base64," + data.imageData };
            updateSword(craftingTargetId, updatedProps);
            const targetOld = swordList.find(s => s.id === craftingTargetId);
            if (targetOld) setMySwordData({ ...targetOld, ...updatedProps });
            setCraftingTargetId(null);
          } else {
            const newSword = { id: Date.now().toString(), baseName: userName, name: data.swordName || "無銘の剣", hp: data.params.hp, attack: data.params.attack, weight: data.params.weight, imageStr: data.imageData, imageSrc: "data:image/png;base64," + data.imageData, hiltType: "0" };
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
    switch (displayScreen) {
      case "TITLE":
        return <TitleScreen mySwordData={mySwordData} titleMode={titleMode} targetId={targetId} setTargetId={setTargetId} systemMessage={room.error || systemMessage} goToCrafting={goToCrafting} handleCreateRoom={handleCreateRoom} handleJoinRoom={handleJoinRoom} handleCancelJoin={handleCancelJoin} connectToHost={connectToHost} connecting={room.connecting} openRandomMatch={openRandomMatch} startRandomMatch={startRandomMatch} matchMode={matchMode} setMatchMode={setMatchMode} />;
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
        return <LobbyScreen view={view} roomId={room.roomId} isCopied={isCopied} handleCopyId={handleCopyId} swordList={swordList} mySwordData={mySwordData} equipSword={equipSword} reorderSwords={reorderSwords} onReady={room.setReady} onGameMode={room.setGameMode} onLivesMode={room.setLivesMode} onStart={room.start} onLeave={handleLeave} goToCrafting={goToCrafting} error={room.error} />;
      case "RESULT":
        return <ResultScreen view={view} onReturnToLobby={room.returnToLobby} onLeave={handleLeave} onFindNewOpponents={view?.room?.autoStart ? findNewOpponents : null} />;
      default: return null;
    }
  };

  return (
    <div style={{ fontFamily: 'sans-serif', textAlign: 'center', backgroundColor: '#f5f5f5', minHeight: '100vh', position: 'relative', overflow: 'hidden' }}>

      {/* 撮影フラッシュ演出用のスタイルと要素 */}
      <style>{`
        @keyframes flashFade {
          0% { opacity: 1; }
          100% { opacity: 0; }
        }
      `}</style>
      {isFlash && <div style={{ position: 'fixed', inset: 0, backgroundColor: '#fff', zIndex: 9999, pointerEvents: 'none', animation: 'flashFade 0.5s ease-out forwards' }}></div>}

      {displayScreen !== "PLAYING" && renderScreen()}

      {room.hasArena && (
        <div style={{ ...styles.unityContainer, display: displayScreen === "PLAYING" ? 'flex' : 'none', margin: '0 auto' }}>
          <BattleArena bridge={room.bridge} view={view} onLoadFailed={room.reportLoadFailure} />
        </div>
      )}
      {displayScreen === "PLAYING" && !room.hasArena && (
        <div style={{ ...styles.container, justifyContent: 'center', minHeight: '100vh' }}>
          <p style={{ fontSize: '20px', fontWeight: 'bold' }}>対戦を準備しています…</p>
        </div>
      )}
      <div className={`blackout-overlay ${isBlackout ? 'active' : ''}`}></div>
    </div>
  );
}