import React, { useState, useRef, useEffect } from 'react';
import { Peer } from 'peerjs';
import { Unity, useUnityContext } from 'react-unity-webgl';

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

  const [gameMode, setGameMode] = useState("1");
  const gameModeRef = useRef("1");
  useEffect(() => { gameModeRef.current = gameMode; }, [gameMode]);

  const [userName, setUserName] = useState("");

  const [myPeerId, setMyPeerId] = useState("");
  const [targetId, setTargetId] = useState("");
  const peerRef = useRef(null);

  // ▼【追加】部屋が満員で弾かれたかどうかのフラグ
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
    loaderUrl: "/POSE_SWORD_Unity/Builds/ver2.6/Build/ver2.6.loader.js",
    dataUrl: "/POSE_SWORD_Unity/Builds/ver2.6/Build/ver2.6.data",
    frameworkUrl: "/POSE_SWORD_Unity/Builds/ver2.6/Build/ver2.6.framework.js",
    codeUrl: "/POSE_SWORD_Unity/Builds/ver2.6/Build/ver2.6.wasm",
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
    if (isLoaded && pendingBattleRef.current !== null) {
      const { mode, startJson, gameModeStr } = pendingBattleRef.current;
      pendingBattleRef.current = null;
      sendMessage('GameManager', 'SetHostMode', mode);
      sendMessage('GameManager', 'SetGameMode', gameModeStr);
      sendMessage('GameManager', 'StartBattle', JSON.stringify(startJson));
    }
  }, [step, isLoaded]);

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
    if (isReady && isEnemyReady && step === "LOBBY" && countdown === null) {
      setCountdown(3);
    }
  }, [isReady, isEnemyReady, step, countdown]);

  useEffect(() => {
    if (countdown !== null) {
      if (countdown > 0) {
        const timer = setTimeout(() => setCountdown(countdown - 1), 1000);
        return () => clearTimeout(timer);
      } else {
        setCountdown(null);
        setIsReady(false);
        setIsEnemyReady(false);
        if (mySwordRef.current && enemySwordRef.current) {
          launchUnityBattle(roleRef.current, mySwordRef.current, enemySwordRef.current);
        } else {
          alert("相手の剣データがまだ届いていません。");
        }
      }
    }
  }, [countdown]);

  useEffect(() => {
    if (!connection || !mySwordData) return;
    const { name, hp, attack, weight, imageStr } = mySwordData;
    const statsOnly = { name, hp, attack, weight };
    const fullData  = { name, hp, attack, weight, imageStr };

    connection.send({ type: "EXCHANGE_SWORD", swordData: fullData });

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
    setCountdown(null);
    setRole(null);
    setSystemMessage(msg);
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
    setStep("HOST_WAIT");

    const attemptCreatePeer = (retriesLeft) => {
      const hostId = Math.floor(100000 + Math.random() * 900000).toString();
      const peer = new Peer(hostId, PEER_ICE_CONFIG);

      peer.on('open', (id) => {
        setMyPeerId(id);
        peerRef.current = peer;
        peer.on('connection', (incomingConn) => { 
          //既に接続中（満員）の場合は弾く
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

            setStep((prev) => {
                if (["NAME_INPUT", "CRAFT_POSE", "CRAFTING_API", "CRAFT_COMPLETE"].includes(prev)) {
                    setCraftReturnStep("LOBBY");
                    return prev; 
                }
                return "LOBBY"; 
            });
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
    isRejectedRef.current = false;
    setRole("CLIENT"); 
    setStep("CLIENT_WAIT");
    const peer = new Peer(PEER_ICE_CONFIG);
    peer.on('open', (id) => setMyPeerId(id));

    peer.on('error', (err) => {
      if (err.type === 'peer-unavailable') {
        setSystemMessage("入力されたIDの部屋が見つかりません。");
      } else {
        setSystemMessage("通信エラーが発生しました。");
      }
    });

    peerRef.current = peer;
  };

  const connectToHost = () => {
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

        //ホストから入室許可が出た時だけロビーへ遷移
        case "ROOM_ACCEPTED":
          if (currentRole === "CLIENT") {
            setSystemMessage("");
            setStep("LOBBY");
          }
          break;

        // 満員通知を受け取った場合の処理
        case "ROOM_FULL":
          if (currentRole === "CLIENT") {
            isRejectedRef.current = true;
            setConnection(null);
            setStep("CLIENT_WAIT");
            setSystemMessage("この部屋はすでに満員（対戦中）です。");
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
        case "LEAVE":
          resetToTitle("相手が部屋を退出しました。"); 
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
      // 満員で弾かれただけの切断なら、タイトルに戻さない
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
      alert("両方の剣データが準備できていません。"); return;
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
    const canvas = canvasRef.current;
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
          <div style={styles.container}>
            {mySwordData?.imageSrc && (
              <img src={mySwordData.imageSrc} alt="Background Sword" style={styles.bgImageCenter} />
            )}
            
            <div style={styles.contentWrapper}>
              <img src="/logo-oreblade.png" alt="オレブレード" style={{ maxWidth: '500px', marginBottom: '40px' }} />
              
              <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', width: '300px' }}>
                <button 
                  style={{ ...styles.button, backgroundColor: '#ff9800', color: 'white', padding: '15px' }} 
                  onClick={() => goToCrafting("TITLE")}
                >
                  {mySwordData ? "⚔️ 剣を再錬成する" : "⚔️ 剣を錬成する"}
                </button>
                
                <div style={{ borderTop: '2px solid #ddd', margin: '10px 0' }}></div>
                
                {!mySwordData && (
                  <p style={{ color: '#888', fontSize: '14px', margin: '0 0 -10px 0', fontWeight: 'bold' }}>
                    ※遊ぶには、先に剣を錬成してください
                  </p>
                )}
                <button 
                  style={{ ...styles.button, backgroundColor: mySwordData ? '#4CAF50' : '#ccc', color: mySwordData ? 'white' : '#666', cursor: mySwordData ? 'pointer' : 'not-allowed' }} 
                  onClick={handleCreateRoom}
                  disabled={!mySwordData}
                >
                  部屋を作る (Host)
                </button>
                <button 
                  style={{ ...styles.button, backgroundColor: mySwordData ? '#2196F3' : '#ccc', color: mySwordData ? 'white' : '#666', cursor: mySwordData ? 'pointer' : 'not-allowed' }} 
                  onClick={handleJoinRoom}
                  disabled={!mySwordData}
                >
                  部屋に入る (Client)
                </button>
              </div>

              {/* ▼【変更】エラーメッセージ領域の高さを固定（レイアウトずれ防止） */}
              <div style={{ height: '50px', display: 'flex', alignItems: 'center', justifyContent: 'center', marginTop: '30px' }}>
                {systemMessage && (
                  <div style={{ ...styles.errorMessage, margin: '0' }}>
                    ⚠️ {systemMessage}
                  </div>
                )}
              </div>

            </div>
          </div>
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
                  <button 
                    style={{ ...styles.button, backgroundColor: isNameUnchangedOrEmpty ? 'gray' : '#2196F3', color: 'white', padding: '10px 30px' }} 
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
              )}

              <div style={{ marginTop: '20px', display: 'flex', gap: '15px' }}>
                <button 
                  style={{ ...styles.button, backgroundColor: '#888', color: 'white' }} 
                  onClick={() => {
                    setUserName(mySwordData ? mySwordData.baseName : "");
                    setStep(craftReturnStep);
                  }}
                >
                  キャンセル（戻る）
                </button>
                <button 
                  style={{ ...styles.button, backgroundColor: !userName.trim() ? 'gray' : '#4CAF50', color: 'white' }} 
                  onClick={() => setStep("CRAFT_POSE")}
                  disabled={!userName.trim()}
                >
                  {mySwordData ? "新しくポーズを撮り直す" : "次へ（撮影へ）"}
                </button>
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
              
              <button 
                style={{ ...styles.button, backgroundColor: captureCountdown !== null ? 'gray' : 'orange', padding: '15px 30px' }} 
                onClick={startCaptureCountdown} 
                disabled={captureCountdown !== null}
              >
                {captureCountdown !== null ? "ポーズをとって！" : "撮影する！"}
              </button>
              <button 
                style={{ ...styles.button, backgroundColor: '#e0e0e0', color: '#333', marginTop: '20px' }} 
                onClick={() => setStep("NAME_INPUT")}
                disabled={captureCountdown !== null}
              >
                名前入力に戻る
              </button>
            </div>
          </div>
        );

      case "CRAFTING_API":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2>錬成中...</h2>
              {capturedImage && (
                <div style={{ marginBottom: '20px', borderRadius: '8px', overflow: 'hidden', border: '4px solid #ddd', width: '320px' }}>
                  <img src={capturedImage} alt="Captured Pose" style={{ width: '100%', display: 'block' }} />
                </div>
              )}
              
              <div style={{ margin: '10px 0', fontSize: '60px', animation: 'spin 3s linear infinite' }}>
                ⚙️
              </div>
              <p style={{ fontSize: '18px', fontWeight: 'bold' }}>剣を錬成中...</p>
              <style>{`@keyframes spin { 100% { transform: rotate(360deg); } }`}</style>
            </div>
          </div>
        );

      case "CRAFT_COMPLETE":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2>錬成完了！</h2>
              {mySwordData && (
                <div style={{...styles.swordCard, maxWidth: '300px', margin: '20px 0'}}>
                  <img src={mySwordData.imageSrc} alt="My Sword" style={styles.previewImage} />
                  <p style={styles.swordName}>{mySwordData.name}</p>
                  <div style={styles.statsBox}>
                    <span>HP: {mySwordData.hp}</span>
                    <span>攻撃: {mySwordData.attack}</span>
                    <span>重さ: {mySwordData.weight}</span>
                  </div>
                </div>
              )}
              <div style={{ marginTop: '20px' }}>
                <button 
                  style={{ ...styles.button, backgroundColor: '#4CAF50', color: 'white', padding: '15px 30px' }} 
                  onClick={() => setStep(craftReturnStep)}
                >
                  {craftReturnStep === "TITLE" ? "タイトルに戻る" : "ロビー（または待機画面）に戻る"}
                </button>
              </div>
              <div style={{ marginTop: '10px' }}>
                <button style={{ ...styles.button, backgroundColor: 'transparent', border: '1px solid #ccc', color: '#333' }} onClick={() => goToCrafting(craftReturnStep)}>
                  作り直す
                </button>
              </div>
            </div>
          </div>
        );

      case "HOST_WAIT":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2>ロビーID表示</h2>
              <div style={{ margin: '20px 0', padding: '30px', backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 2px 8px rgba(0,0,0,0.1)' }}>
                <p style={{ fontSize: '18px', margin: '0' }}>あなたの部屋ID</p>
                
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '15px', margin: '15px 0' }}>
                  <p style={{ fontSize: '48px', color: 'blue', fontWeight: 'bold', letterSpacing: '8px', margin: '0' }}>
                    {myPeerId || "取得中..."}
                  </p>
                  
                  {myPeerId && (
                    <button 
                      style={{ ...styles.button, padding: '8px 16px', fontSize: '16px', backgroundColor: isCopied ? '#4CAF50' : '#e0e0e0', color: isCopied ? 'white' : '#333' }} 
                      onClick={handleCopyId}
                    >
                      {isCopied ? "✓ コピーしました" : "📋 コピー"}
                    </button>
                  )}
                </div>
                
                <p>このIDをClient（対戦相手）に教えてください。</p>
              </div>
              <button style={{ ...styles.button, backgroundColor: 'gray', color: 'white', marginTop: '20px' }} onClick={handleLeave}>
                タイトルに戻る
              </button>
            </div>
          </div>
        );

      case "CLIENT_WAIT":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2>ロビーID入力</h2>
              <div style={{ margin: '20px 0', padding: '20px', backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 2px 8px rgba(0,0,0,0.1)' }}>
                <p style={{ fontSize: '18px', fontWeight: 'bold' }}>Hostの部屋ID（6桁の数字）を入力</p>
                <input 
                  type="text" 
                  value={targetId} 
                  onChange={(e) => setTargetId(e.target.value)} 
                  placeholder="例: 123456" 
                  maxLength={6}
                  style={{ ...styles.input, letterSpacing: '4px', width: '180px' }} 
                />
                <button style={{ ...styles.button, marginLeft: '10px', backgroundColor: '#4CAF50', color: 'white' }} onClick={connectToHost}>
                  接続
                </button>
              </div>

              {/* エラーメッセージ領域の高さを固定 */}
              <div style={{ height: '50px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                {systemMessage && (
                  <div style={{ ...styles.errorMessage, margin: '0' }}>
                    ⚠️ {systemMessage}
                  </div>
                )}
              </div>

              <button style={{ ...styles.button, backgroundColor: 'gray', color: 'white', marginTop: '10px' }} onClick={handleLeave}>
                タイトルに戻る
              </button>
            </div>
          </div>
        );

      case "LOBBY":
        const isButtonsLocked = isReady || countdown !== null;

        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2>ロビー（対戦準備）</h2>
              
              <div style={styles.previewContainer}>
                {(() => {
                  const isHost = role === "HOST";
                  const hostData = isHost ? mySwordData : enemySwordData;
                  const clientData = !isHost ? mySwordData : enemySwordData;
                  
                  return (
                    <>
                      <div style={styles.swordCard}>
                        <h3 style={{ margin: '0 0 10px 0', color: isHost ? '#000000' : '#ff4444' }}>{isHost ? "あなた" : "対戦相手"}</h3>
                        {hostData ? (
                          <>
                            {hostData.imageSrc
                              ? <img src={hostData.imageSrc} alt="Host Sword" style={styles.previewImage} />
                              : <div style={{ ...styles.previewImage, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#aaa', fontSize: '13px' }}>画像受信中...</div>}
                            <p style={styles.swordName}>{hostData.name}</p>
                            <div style={styles.statsBox}>
                              <span>HP: {hostData.hp}</span>
                              <span>攻撃: {hostData.attack}</span>
                              <span>重さ: {hostData.weight}</span>
                            </div>
                          </>
                        ) : (
                          <div style={{ height: '200px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}><p style={{ color: '#888', fontWeight: 'bold' }}>未錬成</p></div>
                        )}
                      </div>

                      <div style={styles.vsText}>VS</div>

                      <div style={styles.swordCard}>
                        <h3 style={{ margin: '0 0 10px 0', color: isHost ? '#FF4444' : '#000000' }}>{!isHost ? "あなた" : "対戦相手"}</h3>
                        {clientData ? (
                          <>
                            {clientData.imageSrc
                              ? <img src={clientData.imageSrc} alt="Client Sword" style={styles.previewImage} />
                              : <div style={{ ...styles.previewImage, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#aaa', fontSize: '13px' }}>画像受信中...</div>}
                            <p style={styles.swordName}>{clientData.name}</p>
                            <div style={styles.statsBox}>
                              <span>HP: {clientData.hp}</span>
                              <span>攻撃: {clientData.attack}</span>
                              <span>重さ: {clientData.weight}</span>
                            </div>
                          </>
                        ) : (
                          <div style={{ height: '200px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}><p style={{ color: '#888', fontWeight: 'bold' }}>データ受信中...</p></div>
                        )}
                      </div>
                    </>
                  );
                })()}
              </div>

              <div style={styles.connectedBox}>
                <div style={styles.modeBox}>
                  <h3 style={{ margin: '0 0 10px 0', color: isButtonsLocked ? '#999' : '#333' }}>バトルモード</h3>
                  {role === "HOST" ? (
                    <select
                      value={gameMode}
                      onChange={(e) => {
                        const newMode = e.target.value;
                        setGameMode(newMode);
                        connection.send({ type: "SYNC_GAMEMODE", gameMode: newMode });
                      }}
                      style={{ padding: '8px', fontSize: '16px', borderRadius: '5px', cursor: isButtonsLocked ? 'not-allowed' : 'pointer', backgroundColor: isButtonsLocked ? '#f5f5f5' : '#fff', color: isButtonsLocked ? '#999' : '#000' }}
                      disabled={isButtonsLocked}
                    >
                      <option value="1">🌀 独楽（見下ろし）モード</option>
                      <option value="0">⚔️ 剣（横視点・重力）モード</option>
                    </select>
                  ) : (
                    <div style={{ fontSize: '18px', fontWeight: 'bold', color: '#555' }}>
                      {gameMode === "1" ? "🌀 独楽（見下ろし）モード" : "⚔️ 剣（横視点・重力）モード"}
                    </div>
                  )}
                </div>

                {countdown !== null ? (
                  <h2 style={{ fontSize: '48px', color: 'red', margin: '0', animation: 'pulse 1s infinite' }}>{countdown > 0 ? countdown : "START!"}</h2>
                ) : (
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'center', gap: '20px', margin: '10px 0 20px 0' }}>
                      <div style={styles.readyBox(isReady)}>自分: {isReady ? "準備OK!" : "準備中..."}</div>
                      <div style={styles.readyBox(isEnemyReady)}>相手: {isEnemyReady ? "準備OK!" : "準備中..."}</div>
                    </div>
                    
                    <div style={{ display: 'flex', justifyContent: 'center', gap: '15px' }}>
                      <button 
                        style={{ ...styles.button, backgroundColor: '#e0e0e0', color: isButtonsLocked ? '#aaa' : '#333', cursor: isButtonsLocked ? 'not-allowed' : 'pointer' }} 
                        onClick={() => goToCrafting("LOBBY")}
                        disabled={isButtonsLocked}
                      >
                        剣を再錬成する
                      </button>

                      {!isReady ? (
                        <button
                          style={{ ...styles.button, backgroundColor: (mySwordData && enemySwordData) ? '#4CAF50' : 'gray', color: 'white' }}
                          onClick={handleReady}
                          disabled={!mySwordData || !enemySwordData}
                        >
                          {mySwordData && enemySwordData ? "準備完了（バトルへ）" : "剣のデータが不足しています"}
                        </button>
                      ) : (
                        <button
                          style={{ ...styles.button, backgroundColor: countdown !== null ? 'gray' : '#f44336', color: 'white', cursor: countdown !== null ? 'not-allowed' : 'pointer' }}
                          onClick={() => {
                            setIsReady(false);
                            if (connection) connection.send({ type: "SYNC_STATE", isReady: false });
                          }}
                          disabled={countdown !== null} 
                        >
                          準備を取り消す
                        </button>
                      )}
                    </div>
                  </div>
                )}
              </div>
              
              <button 
                style={{ ...styles.button, backgroundColor: 'transparent', border: '1px solid gray', color: isButtonsLocked ? '#ccc' : 'gray', marginTop: '30px', cursor: isButtonsLocked ? 'not-allowed' : 'pointer' }} 
                onClick={handleLeave}
                disabled={isButtonsLocked} 
              >
                退出する
              </button>
            </div>
            <style>{`@keyframes pulse { 0% { transform: scale(1); } 50% { transform: scale(1.1); } 100% { transform: scale(1); } }`}</style>
          </div>
        );

      case "PLAYING":
        return (
          <div style={styles.container}>
            <div style={styles.unityContainer}>
              <Unity unityProvider={unityProvider} style={{ width: '100%', height: '100%' }} />
            </div>
          </div>
        );

      case "RESULT":
        return (
          <div style={styles.container}>
            {matchResult.winnerImageSrc && (
              <img 
                src={matchResult.winnerImageSrc} 
                alt="Winner Background" 
                style={styles.bgImageCenter} 
              />
            )}
            
            <div style={styles.contentWrapper}>
              <h2 style={{ 
                fontSize: '70px', 
                fontWeight: '900', 
                fontStyle: 'italic', 
                margin: '0 0 20px 0', 
                color: matchResult.iWon ? '#d32f2f' : '#1976d2', 
                textShadow: '2px 2px 0px #fff, -2px -2px 0px #fff, 2px -2px 0px #fff, -2px 2px 0px #fff, 4px 4px 10px rgba(0,0,0,0.3)' 
              }}>
                {matchResult.iWon ? "YOU WIN!!" : "YOU LOSE..."}
              </h2>

              <div style={{ backgroundColor: 'rgba(255, 255, 255, 0.9)', padding: '40px', borderRadius: '15px', boxShadow: '0 8px 20px rgba(0,0,0,0.2)', border: `4px solid ${matchResult.iWon ? '#d32f2f' : '#1976d2'}` }}>
                <h3 style={{ fontSize: '32px', color: '#333', margin: '0 0 25px 0' }}>勝者: {matchResult.winnerName}</h3>
                <p style={{ fontSize: '20px', margin: '10px 0' }}>与えたダメージ: <strong>{matchResult.damageDealt}</strong></p>
                <p style={{ fontSize: '20px', margin: '10px 0' }}>受けたダメージ: <strong>{matchResult.damageTaken}</strong></p>
              </div>
              
              <div style={{ marginTop: '40px', display: 'flex', gap: '20px' }}>
                <button style={{ ...styles.button, backgroundColor: 'orange', color: 'white', padding: '15px 30px' }} onClick={() => {
                  try { sendMessage('GameManager', 'ResetMatch', ''); } catch(e) {}
                  setIsReady(false);
                  if (connRef.current) connRef.current.send({ type: "SYNC_STATE", isReady: false });
                  setStep("LOBBY");
                }}>
                  ロビーに戻る
                </button>
                <button style={{ ...styles.button, backgroundColor: 'gray', color: 'white', padding: '15px 30px' }} onClick={handleLeave}>
                  退出する
                </button>
              </div>
            </div>
          </div>
        );
      default: return <div>Error</div>;
    }
  };

  return <div style={{ fontFamily: 'sans-serif', textAlign: 'center', backgroundColor: '#f5f5f5', minHeight: '100vh', position: 'relative', overflow: 'hidden' }}>{renderScreen()}</div>;
}

const styles = {
  container: { padding: '30px', display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%', height: '100%' },
  contentWrapper: { zIndex: 1, position: 'relative', display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' },
  
  bgImageCenter: { position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)', height: '100vh', opacity: 0.15, pointerEvents: 'none', zIndex: 0 },

  button: { padding: '10px 20px', fontSize: '18px', cursor: 'pointer', borderRadius: '5px', fontWeight: 'bold', border: 'none', boxShadow: '0 2px 4px rgba(0,0,0,0.2)' },
  input: { padding: '10px', fontSize: '20px', width: '250px', textAlign: 'center', borderRadius: '5px', border: '2px solid #ccc' },
  connectedBox: { marginTop: '10px', padding: '20px', backgroundColor: '#ffffff', borderRadius: '8px', width: '100%', maxWidth: '600px', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' },
  modeBox: { marginBottom: '20px', padding: '15px', backgroundColor: '#f0f8ff', borderRadius: '8px', border: '1px solid #cce7ff' },
  video: { width: '400px', borderRadius: '8px', backgroundColor: '#000', display: 'block', transform: 'scaleX(-1)' },
  unityContainer: { width: '800px', height: '450px', backgroundColor: '#222', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '4px solid #555' },
  readyBox: (isReady) => ({ padding: '10px 20px', border: `2px solid ${isReady ? '#4CAF50' : '#9e9e9e'}`, backgroundColor: isReady ? '#e8f5e9' : '#f5f5f5', borderRadius: '8px', fontWeight: 'bold', minWidth: '100px' }),
  errorMessage: { padding: '15px 25px', backgroundColor: '#ffdddd', color: '#cc0000', borderRadius: '8px', fontWeight: 'bold', border: '1px solid #cc0000' }, // marginを削除しインラインで制御
  previewContainer: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '20px', margin: '20px 0', width: '100%', maxWidth: '800px' },
  swordCard: { flex: 1, backgroundColor: '#fff', borderRadius: '12px', padding: '15px', boxShadow: '0 4px 10px rgba(0,0,0,0.15)', display: 'flex', flexDirection: 'column', alignItems: 'center', border: '2px solid #e0e0e0' },
  previewImage: { width: '100%', height: '200px', objectFit: 'contain', backgroundColor: '#f0f0f0', borderRadius: '8px', marginBottom: '10px' },
  swordName: { fontSize: '20px', fontWeight: 'bold', margin: '5px 0' },
  statsBox: { display: 'flex', justifyContent: 'center', gap: '10px', fontSize: '14px', fontWeight: 'bold', color: '#555', backgroundColor: '#f9f9f9', padding: '5px 10px', borderRadius: '5px', width: '100%' },
  vsText: { fontSize: '36px', fontWeight: '900', fontStyle: 'italic', color: '#ff9800', textShadow: '2px 2px 0px #000' },
  countdownOverlay: { position: 'absolute', top: '20px', left: '50%', transform: 'translateX(-50%)', fontSize: '80px', fontWeight: 'bold', color: 'rgba(255, 255, 255, 0.7)', textShadow: '0 0 20px red, 2px 2px 0px #000, -2px -2px 0px #000, 2px -2px 0px #000, -2px 2px 0px #000', pointerEvents: 'none', zIndex: 10 },
};