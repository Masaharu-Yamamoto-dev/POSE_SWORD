import { useState, useRef, useEffect, useCallback } from 'react';
import './App.css';
import { styles } from './styles';

import TitleScreen from './screens/TitleScreen';
import LobbyScreen from './screens/LobbyScreen';
import ResultScreen from './screens/ResultScreen';
import { NameInputScreen, CraftPoseScreen, CraftingApiScreen, CraftCompleteScreen } from './screens/CraftingScreens';
import SwordListScreen from './screens/SwordListScreen';
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
// 武器の中身が変わったかどうかだけを見る。IDが同じでも撮り直しを検出する。
const swordKey = sword => sword && `${sword.id}:${sword.name}:${sword.hp}:${sword.attack}:${sword.weight}:${sword.imageStr?.length ?? 0}`;

export default function PoseSwordWeb() {
  const [step, setStep] = useState("TITLE");

  const [titleMode, setTitleMode] = useState("DEFAULT");
  const [craftReturnStep, setCraftReturnStep] = useState("TITLE");

  // ストレージと剣のリスト管理
  const [swordList, setSwordList] = useState([]);
  const [craftingTargetId, setCraftingTargetId] = useState(null);
  const [mySwordData, setMySwordData] = useState(null);
  const mySwordRef = useRef(null);
  useEffect(() => { mySwordRef.current = mySwordData; }, [mySwordData]);

  const [userName, setUserName] = useState("");
  const [targetId, setTargetId] = useState("");

  const [captureCountdown, setCaptureCountdown] = useState(null);
  const videoRef = useRef(null);
  const canvasRef = useRef(null);

  const [capturedImage, setCapturedImage] = useState(null);
  const [systemMessage, setSystemMessage] = useState("");
  const [isCopied, setIsCopied] = useState(false);

  const resetToTitleRef = useRef(null);
  const room = useRoom({
    peerOptions: PEER_ICE_CONFIG,
    onClosed: message => resetToTitleRef.current?.(message || "ロビーとの接続が終了しました。"),
  });
  const view = room.view;
  const sentSwordRef = useRef(null);
  const [matchSize, setMatchSize] = useState(2);
  const [matchMode, setMatchMode] = useState("0");
  const randomMatch = useRandomMatch({ room, sword: mySwordData });

  const resetToTitle = useCallback((msg = "") => {
    setTitleMode("DEFAULT"); setTargetId(""); setSystemMessage(msg); setStep("TITLE");
    sentSwordRef.current = null;
  }, []);
  useEffect(() => { resetToTitleRef.current = resetToTitle; }, [resetToTitle]);

  // 部屋の局面がそのまま画面になる。錬成中（武器庫・撮影）だけは自分の画面を保つ。
  const roomScreen = (() => {
    if (!view?.room || view.closed) return null;
    const me = view.room.players.find(p => p.playerId === view.localPlayerId);
    if (ACTIVE_PHASES.includes(view.room.phase)) return "PLAYING";
    if (view.result && me && !me.inLobby) return "RESULT";
    return "LOBBY";
  })();
  // 相手を探している間は、自分の部屋ができていても探索画面を出し続ける。
  const seeking = Boolean(randomMatch.view?.seeking);
  // 部屋が無い状態で部屋の画面は出さない。
  const screen = seeking ? "MATCHING"
    : ROOM_STEPS.includes(step) || step === "TITLE" ? (roomScreen ?? "TITLE")
    : step;

  // 武器を持ち替えたら部屋にも反映する（本人の準備は解除される）
  useEffect(() => {
    if (!view?.room || !mySwordData) return;
    const key = swordKey(mySwordData);
    if (sentSwordRef.current === key) return;
    sentSwordRef.current = key;
    room.updateSword(mySwordData);
  }, [mySwordData, view?.room, room]);

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

  // ===============================
  // ▼ 武器庫（アーセナル）機能
  // ===============================
  const equipSword = (sword) => {
    setMySwordData(sword);
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
      canvas.width = img.width;
      canvas.height = img.height;
      const ctx = canvas.getContext('2d');
      
      // 画像を物理的に反転させる
      ctx.translate(canvas.width, 0);
      ctx.scale(-1, 1);
      ctx.drawImage(img, 0, 0);

      const flippedSrc = canvas.toDataURL('image/png');
      const flippedStr = flippedSrc.split(',')[1];

      // 新しく生成した反転画像データで上書き保存！
      updateSword(targetId, {
        imageSrc: flippedSrc,
        imageStr: flippedStr
      });
    };
  };

  const startRecapture = (targetId) => {
    const target = swordList.find(s => s.id === targetId);
    if (target) {
      setCraftingTargetId(targetId);
      setUserName(target.baseName);
      setStep("CRAFT_POSE");
    }
  };

  const startNewCrafting = () => {
    setCraftingTargetId(null);
    setUserName("");
    setStep("NAME_INPUT");
  };

  const goToCrafting = (returnStep) => {
    // ロビーから武器庫へ行く間は準備完了を外す
    if (returnStep === "LOBBY") room.setReady(false);
    setCraftReturnStep(returnStep);
    if (swordList.length === 0) {
      setCraftingTargetId(null);
      setUserName("");
      setStep("NAME_INPUT");
    } else {
      setStep("SWORD_LIST");
    }
  };

  const cancelList = () => setStep(craftReturnStep);
  // ===============================

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

  // 同じ部屋を畳んで、新しい相手を探しに行く
  const findNewOpponents = () => {
    room.leave();
    sentSwordRef.current = swordKey(mySwordData);
    randomMatch.start(matchSize, matchMode);
  };

  const handleCopyId = () => {
    if (!room.roomId) return;
    navigator.clipboard.writeText(room.roomId).then(() => { 
      setIsCopied(true); 
      setTimeout(() => setIsCopied(false), 2000); 
    }).catch(e => console.error(e));
  };

  const handleCreateRoom = () => {
    if (!mySwordData) return;
    setSystemMessage(""); setTitleMode("DEFAULT");
    sentSwordRef.current = swordKey(mySwordData);
    room.createRoom(mySwordData);
  };

  const handleJoinRoom = () => {
    setSystemMessage(""); setTargetId(""); setTitleMode("JOIN_INPUT");
  };

  const handleCancelJoin = () => {
    setTitleMode("DEFAULT"); setSystemMessage("");
    room.leave();
  };

  const connectToHost = () => {
    setSystemMessage("");
    if (!targetId.trim()) return setSystemMessage("IDを入力してください。");
    if (!/^\d+$/.test(targetId)) return setSystemMessage("半角数字のみで入力してください。");
    if (targetId.length < 6) return setSystemMessage("6桁で入力してください。");
    if (!mySwordData) return setSystemMessage("先に剣を錬成してください。");
    setSystemMessage("接続中...");
    sentSwordRef.current = swordKey(mySwordData);
    room.joinRoom(targetId, mySwordData);
  };

  const startCaptureCountdown = () => setCaptureCountdown(5);

  const executeCaptureAndCraft = () => {
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
    // 既定は Vercel 関数経由。関数側が Cloud Run の錬成APIへ中継し、APIキーを付ける。
    const pythonApiUrl = `${import.meta.env.VITE_API_URL ?? '/api'}/cutout`;

    fetch(pythonApiUrl, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ imageData: base64DataOnly, userName: userName }) })
    .then(res => { if (!res.ok) throw new Error(`HTTPエラー`); return res.json(); })
    .then((data) => {
      if (craftingTargetId) {
        // 【A】姿の撮り直し（上書き）の場合
        const updatedProps = {
          hp: data.params.hp,
          attack: data.params.attack,
          weight: data.params.weight,
          imageStr: data.imageData,
          imageSrc: "data:image/png;base64," + data.imageData
        };
        updateSword(craftingTargetId, updatedProps);
        
        const targetOld = swordList.find(s => s.id === craftingTargetId);
        if (targetOld) {
          setMySwordData({ ...targetOld, ...updatedProps });
        }
        
        setCraftingTargetId(null);
      } else {
        // 【B】新規錬成の場合
        const newSword = {
          id: Date.now().toString(),
          baseName: userName,
          name: data.swordName || "無銘の剣",
          hp: data.params.hp,
          attack: data.params.attack,
          weight: data.params.weight,
          imageStr: data.imageData,  
          imageSrc: "data:image/png;base64," + data.imageData,
          hiltType: "default"
        };
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
  };

  // ▼ 名前入力画面でキャンセルした時の処理
  const handleCancelCrafting = () => {
    setUserName("");
    setCraftingTargetId(null);
    if (swordList.length > 0) {
      setStep("SWORD_LIST");
    } else {
      setStep(craftReturnStep);
    }
  };

  // ▼ ポーズ撮影画面で戻るを押した時の処理
  const handleBackFromPose = () => {
    if (craftingTargetId !== null) {
      setCraftingTargetId(null);
      setStep("SWORD_LIST");
    } else {
      setStep("NAME_INPUT");
    }
  };

  const renderScreen = () => {
    switch (screen) {
      case "TITLE":
        return <TitleScreen mySwordData={mySwordData} titleMode={titleMode} targetId={targetId} setTargetId={setTargetId} systemMessage={room.error || systemMessage} goToCrafting={goToCrafting} handleCreateRoom={handleCreateRoom} handleJoinRoom={handleJoinRoom} handleCancelJoin={handleCancelJoin} connectToHost={connectToHost} connecting={room.connecting} openRandomMatch={openRandomMatch} startRandomMatch={startRandomMatch} matchMode={matchMode} setMatchMode={setMatchMode} />;

      case "MATCHING":
        return <MatchmakingScreen view={randomMatch.view} mySwordData={mySwordData} gameMode={matchMode} onCancel={cancelRandomMatch} />;
      
      case "NAME_INPUT":
        return <NameInputScreen userName={userName} setUserName={setUserName} mySwordData={mySwordData} setMySwordData={setMySwordData} setStep={setStep} craftReturnStep={craftReturnStep} handleCancel={handleCancelCrafting}/>;
      
      case "CRAFT_POSE":
        return <CraftPoseScreen videoRef={videoRef} canvasRef={canvasRef} captureCountdown={captureCountdown} startCaptureCountdown={startCaptureCountdown} setStep={setStep} handleBack={handleBackFromPose}/>;
      
      case "CRAFTING_API":
        return <CraftingApiScreen capturedImage={capturedImage} />;
      
      case "CRAFT_COMPLETE":
        return <CraftCompleteScreen mySwordData={mySwordData} setStep={setStep} startNewCrafting={startNewCrafting} craftReturnStep={craftReturnStep} />;

      case "SWORD_LIST":
        return <SwordListScreen swordList={swordList} mySwordData={mySwordData} equipSword={equipSword} deleteSword={deleteSword} startNewCrafting={startNewCrafting} startRecapture={startRecapture} updateSword={updateSword} cancelList={cancelList} toggleSwordFlip={toggleSwordFlip} />;

      case "LOBBY":
        return <LobbyScreen view={view} roomId={room.roomId} isCopied={isCopied} handleCopyId={handleCopyId} swordList={swordList} mySwordData={mySwordData} equipSword={equipSword} onReady={room.setReady} onGameMode={room.setGameMode} onStart={room.start} onLeave={handleLeave} goToCrafting={goToCrafting} error={room.error} />;

      case "RESULT":
        return <ResultScreen view={view} onReturnToLobby={room.returnToLobby} onLeave={handleLeave}
          onFindNewOpponents={view?.room?.autoStart ? findNewOpponents : null} />;
      
      default: return null;
    }
  };

  return (
    <div style={{ fontFamily: 'sans-serif', textAlign: 'center', backgroundColor: '#f5f5f5', minHeight: '100vh', position: 'relative', overflow: 'hidden' }}>
      {screen !== "PLAYING" && renderScreen()}
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
