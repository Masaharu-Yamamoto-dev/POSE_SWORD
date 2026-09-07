import React, { useState, useRef, useEffect } from 'react';
import { Peer } from 'peerjs';
import { Unity, useUnityContext } from 'react-unity-webgl';
import './App.css';
import { styles } from './styles';

import TitleScreen from './screens/TitleScreen';
import LobbyScreen from './screens/LobbyScreen';
import ResultScreen from './screens/ResultScreen';
import { NameInputScreen, CraftPoseScreen, CraftingApiScreen, CraftCompleteScreen } from './screens/CraftingScreens';
import SwordListScreen from './screens/SwordListScreen';
import MultiplayerGame from './components/MultiplayerGame.jsx';

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

export default function PoseSwordWeb() {
  const [step, setStep] = useState("TITLE");
  const stepRef = useRef(step);
  useEffect(() => { stepRef.current = step; }, [step]);

  const [titleMode, setTitleMode] = useState("DEFAULT");
  const [craftReturnStep, setCraftReturnStep] = useState("TITLE");

  const [role, setRole] = useState(null);
  const roleRef = useRef(null);
  useEffect(() => { roleRef.current = role; }, [role]);
  
  const [connection, setConnection] = useState(null);
  const connRef = useRef(null);
  useEffect(() => { connRef.current = connection; }, [connection]);

  // ストレージと剣のリスト管理
  const [swordList, setSwordList] = useState([]);
  const [craftingTargetId, setCraftingTargetId] = useState(null);
  const [mySwordData, setMySwordData] = useState(null);
  const mySwordRef = useRef(null);
  useEffect(() => { mySwordRef.current = mySwordData; }, [mySwordData]);

  const [enemySwordData, setEnemySwordData] = useState(null);
  const enemySwordRef = useRef(null);
  useEffect(() => { enemySwordRef.current = enemySwordData; }, [enemySwordData]);

  const [gameMode, setGameMode] = useState("0");
  const gameModeRef = useRef("0");
  useEffect(() => { gameModeRef.current = gameMode; }, [gameMode]);

  const [isEnemyUnityLoaded, setIsEnemyUnityLoaded] = useState(false);
  const enemyUnityLoadedRef = useRef(false);
  useEffect(() => { enemyUnityLoadedRef.current = isEnemyUnityLoaded; }, [isEnemyUnityLoaded]);

  const [userName, setUserName] = useState("");
  const [myPeerId, setMyPeerId] = useState("");
  const [targetId, setTargetId] = useState("");
  const peerRef = useRef(null);
  const isRejectedRef = useRef(false);

  const [captureCountdown, setCaptureCountdown] = useState(null);
  const videoRef = useRef(null);
  const canvasRef = useRef(null);
  
  const [capturedImage, setCapturedImage] = useState(null);
  const [matchResult, setMatchResult] = useState({ winnerName: "", damageDealt: 0, damageTaken: 0, iWon: false, winnerRole: "HOST", winnerImageSrc: null });

  const [isReady, setIsReady] = useState(false);
  const [isEnemyReady, setIsEnemyReady] = useState(false);
  const [countdown, setCountdown] = useState(null);
  const [systemMessage, setSystemMessage] = useState("");
  const [isCopied, setIsCopied] = useState(false);

  const { unityProvider, sendMessage, isLoaded } = useUnityContext({
    loaderUrl: "/POSE_SWORD_Unity/Builds/ver2.10/Build/ver2.10.loader.js",
    dataUrl: "/POSE_SWORD_Unity/Builds/ver2.10/Build/ver2.10.data",
    frameworkUrl: "/POSE_SWORD_Unity/Builds/ver2.10/Build/ver2.10.framework.js",
    codeUrl: "/POSE_SWORD_Unity/Builds/ver2.10/Build/ver2.10.wasm",
  });

  const pendingBattleRef = useRef(null);
  const syncCountRef = useRef({ fromUnity: 0, toPeer: 0, fromPeer: 0, toUnity: 0 });

  const sendMessageRef = useRef(sendMessage);
  useEffect(() => { sendMessageRef.current = sendMessage; }, [sendMessage]);

  const isFinishingRef = useRef(false);
  const handleGameOverRef = useRef(null);

  const handleGameOver = (syncData) => {
    if (isFinishingRef.current) return;
    isFinishingRef.current = true;
    const currentRole = roleRef.current;
    const clientWon = syncData.hostSword.hp <= 0;

    const hostName = currentRole === "HOST" ? mySwordRef.current?.name : enemySwordRef.current?.name;
    const clientName = currentRole === "CLIENT" ? mySwordRef.current?.name : enemySwordRef.current?.name;

    const iWon = (currentRole === "CLIENT" && clientWon) || (currentRole === "HOST" && !clientWon);
    
    setMatchResult({
      winnerName: clientWon ? (clientName || "クライアントソード") : (hostName || "ホストブレード"),
      damageDealt: currentRole === "HOST" ? Math.max(0, (enemySwordRef.current?.hp ?? 100) - syncData.clientSword.hp) : Math.max(0, (enemySwordRef.current?.hp ?? 100) - syncData.hostSword.hp),
      damageTaken: currentRole === "HOST" ? Math.max(0, (mySwordRef.current?.hp ?? 100) - syncData.hostSword.hp) : Math.max(0, (mySwordRef.current?.hp ?? 100) - syncData.clientSword.hp),
      iWon,
      winnerRole: clientWon ? "CLIENT" : "HOST",
      winnerImageSrc: clientWon ? (currentRole === "CLIENT" ? mySwordRef.current?.imageSrc : enemySwordRef.current?.imageSrc) : (currentRole === "HOST" ? mySwordRef.current?.imageSrc : enemySwordRef.current?.imageSrc)
    });
    
    setTimeout(() => {
      if (stepRef.current === "PLAYING") {
        setIsReady(false);
        setIsEnemyReady(false);
        setCountdown(null);
        setStep("RESULT");
      }
      isFinishingRef.current = false;
    }, 3000); 
  };

  useEffect(() => { handleGameOverRef.current = handleGameOver; });
  
  useEffect(() => {
    if (step === "PLAYING" && isLoaded && connection) connection.send({ type: "PEER_UNITY_LOADED" });
  }, [step, isLoaded, connection]);

  useEffect(() => {
    if (step === "PLAYING" && isLoaded && isEnemyUnityLoaded && pendingBattleRef.current !== null) {
      const { mode, startJson, gameModeStr } = pendingBattleRef.current;
      pendingBattleRef.current = null;
      sendMessage('GameManager', 'SetHostMode', mode);
      sendMessage('GameManager', 'SetGameMode', gameModeStr);
      sendMessage('GameManager', 'StartBattle', JSON.stringify(startJson));
    }
  }, [step, isLoaded, isEnemyUnityLoaded]);

  useEffect(() => {
    if (isReady && isEnemyReady && step === "LOBBY") launchUnityBattle(role, mySwordData, enemySwordData);
  }, [isReady, isEnemyReady]);

  useEffect(() => {
    window.ReactApp = {
      receiveFromUnity: (type, jsonString) => {
        const data = JSON.parse(jsonString);
        if (type === "SYNC" && roleRef.current === "HOST") {
          if (connRef.current) connRef.current.send({ type: "SYNC", ...data });
          if (data.hostSword.hp <= 0 || data.clientSword.hp <= 0) if (handleGameOverRef.current) handleGameOverRef.current(data);
        } else if (type === "INPUT" && connRef.current) {
          connRef.current.send({ type: "INPUT", ...data });
        }
      }
    };
  }, []);

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
    if (!connection || !mySwordData) return;
    const { name, hp, attack, weight, imageStr, hiltType } = mySwordData;
    const statsOnly = { name, hp, attack, weight, hiltType };
    const fullData  = { name, hp, attack, weight, imageStr, hiltType };

    const t1 = setTimeout(() => connection.send({ type: "EXCHANGE_SWORD", swordData: statsOnly }), 300);
    const t2 = setTimeout(() => connection.send({ type: "EXCHANGE_SWORD", swordData: fullData }), 1000);
    const retry = setInterval(() => {
      if (enemySwordRef.current?.imageSrc) { clearInterval(retry); return; }
      connection.send({ type: "EXCHANGE_SWORD", swordData: fullData });
    }, 2000);

    return () => { clearTimeout(t1); clearTimeout(t2); clearInterval(retry); };
  }, [connection, mySwordData]);

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

  const resetToTitle = (msg = "") => {
    if (peerRef.current) { peerRef.current.destroy(); peerRef.current = null; }
    setConnection(null); setMyPeerId(""); setTargetId(""); setIsReady(false); setIsEnemyReady(false); setIsEnemyUnityLoaded(false); setCountdown(null); setRole(null); setSystemMessage(msg); setTitleMode("DEFAULT"); setGameMode("1"); setEnemySwordData(null); setStep("TITLE");
  };

  const handleLeave = () => {
    if (connRef.current) connRef.current.send({ type: "LEAVE" });
    resetToTitle(""); 
  };

  const handleCopyId = () => {
    const idToCopy = roleRef.current === "HOST" ? myPeerId : targetId;
    if (!idToCopy) return;
    navigator.clipboard.writeText(idToCopy).then(() => { 
      setIsCopied(true); 
      setTimeout(() => setIsCopied(false), 2000); 
    }).catch(e => console.error(e));
  };

  const handleCreateRoom = () => {
    setSystemMessage(""); isRejectedRef.current = false; setRole("HOST"); 
    const attemptCreatePeer = (retriesLeft) => {
      const peer = new Peer(Math.floor(100000 + Math.random() * 900000).toString(), PEER_ICE_CONFIG);
      peer.on('open', (id) => {
        setMyPeerId(id); peerRef.current = peer; setStep("LOBBY");
        peer.on('connection', (incomingConn) => { 
          if (connRef.current) {
            incomingConn.on('open', () => { incomingConn.send({ type: "ROOM_FULL" }); setTimeout(() => incomingConn.close(), 500); });
            return;
          }
          incomingConn.on('open', () => {
            setConnection(incomingConn); setupConnection(incomingConn);
            incomingConn.send({ type: "ROOM_ACCEPTED" });
          });
        });
      });
      peer.on('error', (err) => {
        if (err.type === 'unavailable-id') {
          peer.destroy();
          if (retriesLeft > 0) attemptCreatePeer(retriesLeft - 1);
          else { alert("混雑しています。再度お試しください。"); resetToTitle(""); }
        }
      });
    };
    attemptCreatePeer(5);
  };

  const handleJoinRoom = () => {
    setSystemMessage(""); setTargetId(""); isRejectedRef.current = false; setRole("CLIENT"); setTitleMode("JOIN_INPUT");
    const peer = new Peer(PEER_ICE_CONFIG);
    peer.on('open', (id) => setMyPeerId(id));
    peer.on('error', (err) => setSystemMessage(err.type === 'peer-unavailable' ? "ロビーが見つかりません。" : "通信エラーが発生しました。"));
    peerRef.current = peer;
  };

  const handleCancelJoin = () => {
    setTitleMode("DEFAULT"); setSystemMessage(""); setRole(null);
    if (peerRef.current) { peerRef.current.destroy(); peerRef.current = null; }
  };

  const connectToHost = () => {
    setSystemMessage(""); 
    if (!targetId.trim()) return setSystemMessage("IDを入力してください。");
    if (!/^\d+$/.test(targetId)) return setSystemMessage("半角数字のみで入力してください。");
    if (targetId.length < 6) return setSystemMessage("6桁で入力してください。");
    setSystemMessage("接続中..."); 
    if (!peerRef.current || !targetId) return;
    const conn = peerRef.current.connect(targetId);
    conn.on('open', () => { setConnection(conn); setupConnection(conn); });
  };

  const setupConnection = (conn) => {
    conn.on('data', (data) => {
      const currentRole = roleRef.current;
      switch (data.type) {
        case "ROOM_ACCEPTED":
          if (currentRole === "CLIENT") { setSystemMessage(""); setStep("LOBBY"); }
          break;
        case "ROOM_FULL":
          if (currentRole === "CLIENT") { isRejectedRef.current = true; setConnection(null); setTitleMode("JOIN_INPUT"); setSystemMessage("このロビーは満員です。"); }
          break;
        case "SYNC_GAMEMODE":
          if (currentRole === "CLIENT") setGameMode(data.gameMode);
          break;
        case "EXCHANGE_SWORD": {
          const incoming = data.swordData;
          setEnemySwordData(prev => {
            const merged = { ...(prev || {}), ...incoming };
            if (incoming.imageStr) merged.imageSrc = incoming.imageStr.startsWith("data:") ? incoming.imageStr : "data:image/png;base64," + incoming.imageStr;
            return merged;
          });
          break;
        }
        case "SYNC_STATE": 
          if (data.swordData) {
            const enemyData2 = { ...data.swordData };
            if (enemyData2.imageStr) enemyData2.imageSrc = enemyData2.imageStr.startsWith("data:") ? enemyData2.imageStr : "data:image/png;base64," + enemyData2.imageStr;
            setEnemySwordData(enemyData2);
          }
          setIsEnemyReady(data.isReady);
          break;
        case "PEER_UNITY_LOADED":
          setIsEnemyUnityLoaded(true);
          break;
        case "LEAVE":
          resetToTitle("相手が退出しました。"); 
          break;
        case "INPUT":
          try { sendMessageRef.current('GameManager', 'ReceiveInput', JSON.stringify(data)); } catch(e) {}
          break;
        case "SYNC":
          if (currentRole === "CLIENT") {
            try { sendMessageRef.current('GameManager', 'SyncTransform', JSON.stringify(data)); } catch(e) {}
            if (data.hostSword?.hp <= 0 || data.clientSword?.hp <= 0) if (handleGameOverRef.current) handleGameOverRef.current(data);
          }
          break;
        default: break;
      }
    });
    conn.on('close', () => {
      if (isRejectedRef.current) isRejectedRef.current = false;
      else if (peerRef.current && stepRef.current !== "TITLE") resetToTitle("切断されました。");
    });
  };

  const launchUnityBattle = (currentRole, myData, enemyData) => {
    if (!myData || !enemyData) return alert("データがありません。");
    
    const toUnityData = (data) => ({ 
      name: data.name, hp: data.hp, attack: data.attack, weight: data.weight, imageStr: data.imageStr,
      hiltType: data.hiltType || "default"
    });
    const startJson = { hostSword: toUnityData(currentRole === "HOST" ? myData : enemyData), clientSword: toUnityData(currentRole === "CLIENT" ? myData : enemyData) };
    
    pendingBattleRef.current = { mode: currentRole === "HOST" ? 1 : 0, startJson, gameModeStr: gameModeRef.current };
    setStep("PLAYING"); 
  };

  const handleReady = () => {
    setIsReady(true);
    if (connection && mySwordRef.current) connection.send({ type: "SYNC_STATE", isReady: true });
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
    const pythonApiUrl = `${import.meta.env.VITE_API_URL ?? 'https://akequreru-pose-sword-api.hf.space'}/cutout`;

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

      setIsReady(false);
      if (connection) connection.send({ type: "SYNC_STATE", isReady: false });
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
    switch (step) {
      case "MULTIPLAYER":
        return <MultiplayerGame sword={mySwordData} peerOptions={PEER_ICE_CONFIG} onExit={() => setStep("TITLE")} />;
      case "TITLE":
        return <TitleScreen onStartMultiplayer={() => setStep("MULTIPLAYER")} mySwordData={mySwordData} titleMode={titleMode} targetId={targetId} setTargetId={setTargetId} systemMessage={systemMessage} goToCrafting={goToCrafting} handleCreateRoom={handleCreateRoom} handleJoinRoom={handleJoinRoom} handleCancelJoin={handleCancelJoin} connectToHost={connectToHost} />;
      
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
        return <LobbyScreen role={role} mySwordData={mySwordData} enemySwordData={enemySwordData} connection={connection} lobbyId={role === "HOST" ? myPeerId : targetId} isCopied={isCopied} handleCopyId={handleCopyId} gameMode={gameMode} setGameMode={setGameMode} countdown={countdown} isReady={isReady} setIsReady={setIsReady} isEnemyReady={isEnemyReady} handleReady={handleReady} goToCrafting={goToCrafting} handleLeave={handleLeave} swordList={swordList} equipSword={equipSword} />;

      case "PLAYING":
        return (
          <div style={{ ...styles.container, padding: 0, position: 'relative', width: '100%', height: '100vh', justifyContent: 'center' }}>
            <div style={{ ...styles.unityContainer, position: 'relative', width: '100%', maxHeight: '100vh' }}>
              {(!isLoaded || !isEnemyUnityLoaded) && (
                <div style={styles.loadingOverlay}>
                  <div style={styles.loadingSpinner}></div>
                  <p style={{ color: 'white', fontSize: '20px', fontWeight: 'bold', marginTop: '20px' }}>
                    {!isLoaded ? "あなたのUnityを読み込み中..." : "対戦相手の読み込みを待っています..."}
                  </p>
                </div>
              )}
              <Unity unityProvider={unityProvider} style={{ width: '100%', height: '100%' }} />
            </div>
          </div>
        );

      case "RESULT":
        return <ResultScreen matchResult={matchResult} sendMessage={sendMessage} setIsReady={setIsReady} setIsEnemyUnityLoaded={setIsEnemyUnityLoaded} connRef={connRef} setStep={setStep} handleLeave={handleLeave} />;
      
      default: return <div>Error</div>;
    }
  };

  return <div style={{ fontFamily: 'sans-serif', textAlign: 'center', backgroundColor: '#f5f5f5', minHeight: '100vh', position: 'relative', overflow: 'hidden' }}>{renderScreen()}</div>;
}
