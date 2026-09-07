import React, { useState, useRef, useEffect } from 'react';
import { Peer } from 'peerjs';
import { Unity, useUnityContext } from 'react-unity-webgl';
import './App.css';
import { styles } from './styles';

import TitleScreen from './screens/TitleScreen';
import LobbyScreen from './screens/LobbyScreen';
import ResultScreen from './screens/ResultScreen';

// STUN + TURN サーバー設定
const PEER_ICE_CONFIG = {
  config: {
    iceServers: [
      { urls: 'stun:stun.l.google.com:19302' },
      { urls: 'stun:stun1.l.google.com:19302' },
      { urls: 'turn:openrelay.metered.ca:80',              username: 'openrelayproject', credential: 'openrelayproject' },
      { urls: 'turn:openrelay.metered.ca:443',             username: 'openrelayproject', credential: 'openrelayproject' },
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

  const [mySwordData, setMySwordData] = useState(null);
  const mySwordRef = useRef(null);
  useEffect(() => { mySwordRef.current = mySwordData; }, [mySwordData]);

  const [enemySwordData, setEnemySwordData] = useState(null);
  const enemySwordRef = useRef(null);
  useEffect(() => { enemySwordRef.current = enemySwordData; }, [enemySwordData]);

  // ゲームモードの管理 ("1" = 独楽, "0" = 剣)
  const [gameMode, setGameMode] = useState("0");
  const gameModeRef = useRef("0");
  useEffect(() => { gameModeRef.current = gameMode; }, [gameMode]);

  // 相手のUnity(WebGL)のロードが完了したかを管理するフラグ
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

    const myInitialHp = mySwordRef.current?.hp ?? 100;
    const enemyInitialHp = enemySwordRef.current?.hp ?? 100;

    const hostName = currentRole === "HOST" ? mySwordRef.current?.name : enemySwordRef.current?.name;
    const clientName = currentRole === "CLIENT" ? mySwordRef.current?.name : enemySwordRef.current?.name;

    const iWon = (currentRole === "CLIENT" && clientWon) || (currentRole === "HOST" && !clientWon);
    const winnerRole = clientWon ? "CLIENT" : "HOST";
    const winnerImageSrc = clientWon
      ? (currentRole === "CLIENT" ? mySwordRef.current?.imageSrc : enemySwordRef.current?.imageSrc)
      : (currentRole === "HOST" ? mySwordRef.current?.imageSrc : enemySwordRef.current?.imageSrc);

    let damageDealt, damageTaken;
    if (currentRole === "HOST") {
      damageDealt = Math.max(0, enemyInitialHp - syncData.clientSword.hp);
      damageTaken  = Math.max(0, myInitialHp  - syncData.hostSword.hp);
    } else {
      damageDealt = Math.max(0, enemyInitialHp - syncData.hostSword.hp);
      damageTaken  = Math.max(0, myInitialHp  - syncData.clientSword.hp);
    }

    setMatchResult({
      winnerName: clientWon ? (clientName || "クライアントソード") : (hostName || "ホストブレード"),
      damageDealt,
      damageTaken,
      iWon,             
      winnerRole,       
      winnerImageSrc    
    });
    
    console.log("🏁 決着！演出終了を待機します...");
    
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
    if (step === "PLAYING" && isLoaded && connection) {
      console.log("📡 自分のUnityロード完了。相手に通知します。");
      connection.send({ type: "PEER_UNITY_LOADED" });
    }
  }, [step, isLoaded, connection]);

  useEffect(() => {
    if (step === "PLAYING" && isLoaded && isEnemyUnityLoaded && pendingBattleRef.current !== null) {
      const { mode, startJson, gameModeStr } = pendingBattleRef.current;
      pendingBattleRef.current = null;
      
      console.log("🏆 両端末のUnityロードが完全同期！バトルを同時開幕します！");
      
      sendMessage('GameManager', 'SetHostMode', mode);
      sendMessage('GameManager', 'SetGameMode', gameModeStr);
      sendMessage('GameManager', 'StartBattle', JSON.stringify(startJson));
    }
  }, [step, isLoaded, isEnemyUnityLoaded]);

  useEffect(() => {
  if (isReady && isEnemyReady && step === "LOBBY") {
    launchUnityBattle(role, mySwordData, enemySwordData);
  }
}, [isReady, isEnemyReady]);

  useEffect(() => {
    window.ReactApp = {
      receiveFromUnity: (type, jsonString) => {
        const data = JSON.parse(jsonString);
        const currentRole = roleRef.current;
        const currentConn = connRef.current;

        if (type === "SYNC" && currentRole === "HOST") {
          syncCountRef.current.fromUnity++; 
          if (currentConn) {
            syncCountRef.current.toPeer++;  
            currentConn.send({ type: "SYNC", ...data });
          }
          if (data.hostSword.hp <= 0 || data.clientSword.hp <= 0) {
            if (handleGameOverRef.current) handleGameOverRef.current(data);
          }
        } 
        else if (type === "INPUT" && currentConn ) {
          currentConn.send({ type: "INPUT", ...data });
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
    const { name, hp, attack, weight, imageStr } = mySwordData;
    const statsOnly = { name, hp, attack, weight };
    const fullData  = { name, hp, attack, weight, imageStr };

    const t1 = setTimeout(() => {
      connection.send({ type: "EXCHANGE_SWORD", swordData: statsOnly });
    }, 300);

    const t2 = setTimeout(() => {
      connection.send({ type: "EXCHANGE_SWORD", swordData: fullData });
    }, 1000);

    const retry = setInterval(() => {
      if (enemySwordRef.current?.imageSrc) { clearInterval(retry); return; }
      connection.send({ type: "EXCHANGE_SWORD", swordData: fullData });
    }, 2000);

    return () => { clearTimeout(t1); clearTimeout(t2); clearTimeout(retry); };
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

  const goToCrafting = (returnStep) => {
    setUserName(mySwordRef.current ? mySwordRef.current.baseName : "");
    setCraftReturnStep(returnStep);
    setStep("NAME_INPUT");
  };

  const resetToTitle = (msg = "") => {
    if (peerRef.current) {
      peerRef.current.destroy(); 
      peerRef.current = null;
    }
    setConnection(null);
    setMyPeerId("");
    setTargetId("");
    setIsReady(false);
    setIsEnemyReady(false);
    setIsEnemyUnityLoaded(false);
    setCountdown(null);
    setRole(null);
    setSystemMessage(msg);
    setTitleMode("DEFAULT");
    setGameMode("1");
    setStep("TITLE");
  };

  const handleLeave = () => {
    if (connRef.current) connRef.current.send({ type: "LEAVE" });
    resetToTitle(""); 
  };

  const handleCopyId = () => {
    if (!myPeerId) return;
    navigator.clipboard.writeText(myPeerId).then(() => {
      setIsCopied(true);
      setTimeout(() => setIsCopied(false), 2000); 
    }).catch(err => {
      console.error("クリップボードへのコピーに失敗しました:", err);
    });
  };

  const handleCreateRoom = () => {
    setSystemMessage("");
    isRejectedRef.current = false;
    setRole("HOST"); 

    const attemptCreatePeer = (retriesLeft) => {
      const hostId = Math.floor(100000 + Math.random() * 900000).toString();
      const peer = new Peer(hostId, PEER_ICE_CONFIG);

      peer.on('open', (id) => {
        setMyPeerId(id);
        peerRef.current = peer;
        setStep("LOBBY");

        peer.on('connection', (incomingConn) => { 
          if (connRef.current) {
            incomingConn.on('open', () => {
              incomingConn.send({ type: "ROOM_FULL" });
              setTimeout(() => incomingConn.close(), 500);
            });
            return;
          }

          incomingConn.on('open', () => {
            setConnection(incomingConn); 
            setupConnection(incomingConn);
            incomingConn.send({ type: "ROOM_ACCEPTED" });
            incomingConn.send({ type: "SYNC_GAMEMODE", gameMode: gameModeRef.current });
          });
        });
      });

      peer.on('error', (err) => {
        if (err.type === 'unavailable-id') {
          peer.destroy();
          if (retriesLeft > 0) attemptCreatePeer(retriesLeft - 1);
          else {
            alert("サーバーが混雑しています。少し時間を置いて再度お試しください。");
            resetToTitle("");
          }
        }
      });
    };
    attemptCreatePeer(5);
  };

  const handleJoinRoom = () => {
    setSystemMessage("");
    setTargetId("");
    isRejectedRef.current = false;
    setRole("CLIENT"); 
    setTitleMode("JOIN_INPUT");

    const peer = new Peer(PEER_ICE_CONFIG);
    peer.on('open', (id) => setMyPeerId(id));

    peer.on('error', (err) => {
      if (err.type === 'peer-unavailable') {
        setSystemMessage("入力されたIDのロビーが見つかりません。");
      } else {
        setSystemMessage("通信エラーが発生しました。");
      }
    });

    peerRef.current = peer;
  };

  const handleCancelJoin = () => {
    setTitleMode("DEFAULT");
    setSystemMessage("");
    setRole(null);
    if (peerRef.current) {
      peerRef.current.destroy();
      peerRef.current = null;
    }
  };

  const connectToHost = () => {
    setSystemMessage(""); 

    if (!targetId.trim()) {
      setSystemMessage("ロビーIDを入力してください。");
      return;
    }

    const isOnlyNumbers = /^\d+$/.test(targetId);
    if (!isOnlyNumbers) {
      setSystemMessage("IDが不適切です。半角数字のみで入力してください。");
      return;
    }

    if (targetId.length < 6) {
      setSystemMessage("ロビーIDは6桁の数字で入力してください。");
      return;
    }

    setSystemMessage("接続中..."); 
    if (!peerRef.current || !targetId) return;
    const conn = peerRef.current.connect(targetId);
    conn.on('open', () => { 
        setConnection(conn); 
        setupConnection(conn); 
    });
  };

  const setupConnection = (conn) => {
    conn.on('data', (data) => {
      const currentRole = roleRef.current;
      switch (data.type) {
        case "ROOM_ACCEPTED":
          if (currentRole === "CLIENT") {
            setSystemMessage("");
            setStep("LOBBY");
          }
          break;

        case "ROOM_FULL":
          if (currentRole === "CLIENT") {
            isRejectedRef.current = true;
            setConnection(null);
            setTitleMode("JOIN_INPUT");
            setSystemMessage("このロビーはすでに満員（対戦中）です。");
          }
          break;

        case "SYNC_GAMEMODE":
          if (currentRole === "CLIENT") setGameMode(data.gameMode);
          break;
        case "EXCHANGE_SWORD": {
          const incoming = data.swordData;
          setEnemySwordData(prev => {
            const merged = { ...(prev || {}), ...incoming };
            if (incoming.imageStr) {
              merged.imageSrc = incoming.imageStr.startsWith("data:") ? incoming.imageStr : "data:image/png;base64," + incoming.imageStr;
            }
            return merged;
          });
          break;
        }
        case "SYNC_STATE": 
          if (data.swordData) {
            const enemyData2 = { ...data.swordData };
            if (enemyData2.imageStr) {
              enemyData2.imageSrc = enemyData2.imageStr.startsWith("data:") ? enemyData2.imageStr : "data:image/png;base64," + enemyData2.imageStr;
            }
            setEnemySwordData(enemyData2);
          }
          setIsEnemyReady(data.isReady);
          break;

        case "PEER_UNITY_LOADED":
          console.log("📥 相手のUnityロード完了通知を受信しました！");
          setIsEnemyUnityLoaded(true);
          break;

        case "LEAVE":
          resetToTitle("相手がロビーを退出しました。"); 
          break;
        case "INPUT":
          try { sendMessageRef.current('GameManager', 'ReceiveInput', JSON.stringify(data)); } catch(e) {}
          break;
        case "SYNC":
          if (currentRole === "CLIENT") {
            try { sendMessageRef.current('GameManager', 'SyncTransform', JSON.stringify(data)); } catch(e) {}
            if (data.hostSword?.hp <= 0 || data.clientSword?.hp <= 0) {
              if (handleGameOverRef.current) handleGameOverRef.current(data);
            }
          }
          break;
        default: break;
      }
    });

    conn.on('close', () => {
      if (isRejectedRef.current) {
        isRejectedRef.current = false;
      } else if (peerRef.current && stepRef.current !== "TITLE") {
        resetToTitle("通信が切断されました。");
      }
    });
  };

  const launchUnityBattle = (currentRole, myData, enemyData) => {
    const hostData = currentRole === "HOST" ? myData : enemyData;
    const clientData = currentRole === "CLIENT" ? myData : enemyData;
    
    if (!hostData || !clientData) {
      alert("データの準備ができていません。");
      return;
    }
    
    const toUnityData = (data) => ({ name: data.name, hp: data.hp, attack: data.attack, weight: data.weight, imageStr: data.imageStr });
    const startJson = { hostSword: toUnityData(hostData), clientSword: toUnityData(clientData) };
    const mode = currentRole === "HOST" ? 1 : 0;
    
    pendingBattleRef.current = { mode, startJson, gameModeStr: gameModeRef.current };
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

    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;

    const context = canvas.getContext('2d');
    
    context.save();
    context.scale(-1, 1);
    context.translate(-canvas.width, 0);
    context.drawImage(videoRef.current, 0, 0, canvas.width, canvas.height);
    context.restore(); 
    
    const base64Full = canvas.toDataURL('image/jpeg');
    setCapturedImage(base64Full);

    const base64DataOnly = base64Full.split(',')[1]; 
    const pythonApiUrl = `${import.meta.env.VITE_API_URL ?? 'https://akequreru-pose-sword-api.hf.space'}/cutout`;

    fetch(pythonApiUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ imageData: base64DataOnly, userName: userName })
    })
    .then((response) => {
      if (!response.ok) throw new Error(`HTTPエラー: ${response.status}`);
      return response.json();
    })
    .then((data) => {
      setMySwordData({
        baseName: userName,
        name: data.swordName || "無銘の剣",
        hp: data.params.hp,
        attack: data.params.attack,
        weight: data.params.weight,
        imageStr: data.imageData,  
        imageSrc: "data:image/png;base64," + data.imageData 
      });
      setIsReady(false);
      if (connection) connection.send({ type: "SYNC_STATE", isReady: false });

      setStep("CRAFT_COMPLETE");
    })
    .catch((error) => {
      console.error("PythonAPI通信エラー:", error);
      alert("AIサーバーとの通信に失敗しました。");
      setStep("CRAFT_POSE"); 
    });
  };

  const renderScreen = () => {
    switch (step) {
      case "TITLE":
        return (
          <TitleScreen 
            mySwordData={mySwordData}
            titleMode={titleMode}
            targetId={targetId}
            setTargetId={setTargetId}
            systemMessage={systemMessage}
            goToCrafting={goToCrafting}
            handleCreateRoom={handleCreateRoom}
            handleJoinRoom={handleJoinRoom}
            handleCancelJoin={handleCancelJoin} 
            connectToHost={connectToHost}
          />
        );

      case "NAME_INPUT":
        const isNameUnchangedOrEmpty = !userName.trim() || (mySwordData && userName === mySwordData.baseName);

        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2>名前の入力</h2>
              <p style={{ color: '#555', marginBottom: '20px' }}>あなたの名前を教えてください</p>
              <input
                type="text"
                value={userName}
                onChange={(e) => setUserName(e.target.value)}
                placeholder="名前を入力"
                maxLength={10}
                style={styles.input}
              />
              
              {mySwordData && (
                <div style={{ marginTop: '20px' }}>
                  <div className={`ink-btn-container ${isNameUnchangedOrEmpty ? 'disabled' : ''}`}>
                    <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                    <button 
                      className="sharp-button"
                      style={{ '--btn-color': '#2196F3' }}
                      onClick={() => {
                        const newFullName = mySwordData.name.replace(mySwordData.baseName, userName);
                        setMySwordData({ ...mySwordData, baseName: userName, name: newFullName });
                        setStep(craftReturnStep);
                      }}
                      disabled={isNameUnchangedOrEmpty}
                    >
                      名前だけ変更して戻る
                    </button>
                  </div>
                </div>
              )}

              <div style={{ marginTop: '20px', display: 'flex', gap: '4%', width: '100%', maxWidth: '400px' }}>
                <div className="ink-btn-container" style={{ flex: 1 }}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    onClick={() => {
                      setUserName(mySwordData ? mySwordData.baseName : "");
                      setStep(craftReturnStep);
                    }}
                  >
                    キャンセル
                  </button>
                </div>

                <div className={`ink-btn-container ${!userName.trim() ? 'disabled' : ''}`} style={{ flex: 1 }}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                  style={{ '--btn-color': '#4CAF50' }}
                    className="sharp-button"
                    onClick={() => setStep("CRAFT_POSE")}
                    disabled={!userName.trim()}
                  >
                    {mySwordData ? "ポーズを撮り直す" : "ポーズを撮影する"}
                  </button>
                </div>

              </div>
            </div>
          </div>
        );

      case "CRAFT_POSE":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2>ポーズ撮影</h2>
              <div style={{ position: 'relative', width: '400px', marginBottom: '20px' }}>
                <video ref={videoRef} autoPlay playsInline style={styles.video} />
                {captureCountdown !== null && (
                  <div style={styles.countdownOverlay}>
                    {captureCountdown > 0 ? captureCountdown : "📸"}
                  </div>
                )}
              </div>
              <canvas ref={canvasRef} width="640" height="480" style={{ display: 'none' }} />
              
              <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '300px' }}>
                <div className={`ink-btn-container ${captureCountdown !== null ? 'disabled' : ''}`}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#ff9800' }}
                    onClick={startCaptureCountdown} 
                    disabled={captureCountdown !== null}
                  >
                    {captureCountdown !== null ? "ポーズをとれ！" : "撮影する！"}
                  </button>
                </div>

                <div className={`ink-btn-container ${captureCountdown !== null ? 'disabled' : ''}`}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#666666' }}
                    onClick={() => setStep("NAME_INPUT")}
                    disabled={captureCountdown !== null}
                  >
                    {captureCountdown !== null ? "" : "戻る"}
                  </button>
                </div>

              </div>
            </div>
          </div>
        );

      case "CRAFTING_API":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2 style={{ fontFamily: "'Kurobara Gothic', sans-serif", letterSpacing: '0.1em' }}>錬成中...</h2>
              
              {capturedImage && (
                <div style={{ marginBottom: '20px', borderRadius: '0', overflow: 'hidden',  width: '320px' }}>
                  <img src={capturedImage} alt="Captured Pose" style={{ width: '100%', display: 'block' }} />
                </div>
              )}
              
              <div style={{ margin: '20px 0', fontSize: '60px', animation: 'spin 3s linear infinite' }}>
                ⚙️
              </div>
              
              <p style={{ 
                marginTop: '50px', 
                fontSize: '24px', 
                fontWeight: 'bold', 
                color: '#000', 
                fontFamily: "'Kurobara Gothic', sans-serif",
                letterSpacing: '0.05em' 
              }}>
                剣を錬成中...
              </p>
              
              <style>{`@keyframes spin { 100% { transform: rotate(360deg); } }`}</style>
            </div>
          </div>
        );

      case "CRAFT_COMPLETE":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h1 style={{ 
                fontSize: '48px', 
                color: '#000', 
                margin: '20px 0', 
                letterSpacing: '0.05em',
                fontFamily: "'Kurobara Gothic', sans-serif" 
              }}>
                錬成完了！
              </h1>
              
              {mySwordData && (
                <div style={{ margin: '20px 0', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                  <div style={styles.swordCard}>
                    {mySwordData.imageSrc ? (
                      <img src={mySwordData.imageSrc} alt="My Sword" style={styles.previewImage} />
                    ) : (
                      <div style={styles.previewImage}>画像受信中...</div>
                    )}
                    <p style={{ ...styles.swordName, color: '#000' }}>{mySwordData.name}</p>
                    <div style={styles.statsBox}>
                      HP:{mySwordData.hp} 攻撃:{mySwordData.attack} 重さ:{mySwordData.weight}
                    </div>
                  </div>
                </div>
              )}
              
              <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '300px', marginTop: '10px' }}>
                <div className="ink-btn-container">
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#4CAF50' }}
                    onClick={() => setStep(craftReturnStep)}
                  >
                    {craftReturnStep === "TITLE" ? "タイトルに戻って対戦だ！" : "ロビーに戻って対戦だ！"}
                  </button>
                </div>

                <div className="ink-btn-container">
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#000' }}
                    onClick={() => goToCrafting(craftReturnStep)}
                  >
                    剣を再錬成する
                  </button>
                </div>

              </div>
            </div>
          </div>
        );

      case "LOBBY":
        return (
          <LobbyScreen 
            role={role}
            mySwordData={mySwordData}
            enemySwordData={enemySwordData}
            connection={connection}
            myPeerId={myPeerId}
            isCopied={isCopied}
            handleCopyId={handleCopyId}
            gameMode={gameMode}
            setGameMode={setGameMode}
            countdown={countdown}
            isReady={isReady}
            setIsReady={setIsReady}
            isEnemyReady={isEnemyReady}
            handleReady={handleReady}
            goToCrafting={goToCrafting}
            handleLeave={handleLeave}
          />
        );

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
        return (
          <ResultScreen 
            matchResult={matchResult}
            sendMessage={sendMessage}
            setIsReady={setIsReady}
            setIsEnemyUnityLoaded={setIsEnemyUnityLoaded}
            connRef={connRef}
            setStep={setStep}
            handleLeave={handleLeave}
          />
        );
      default: return <div>Error</div>;
    }
  };

  return <div style={{ fontFamily: 'sans-serif', textAlign: 'center', backgroundColor: '#f5f5f5', minHeight: '100vh', position: 'relative', overflowY: 'auto', boxSizing: 'border-box' }}>{renderScreen()}</div>;
}