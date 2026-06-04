import React, { useState, useRef, useEffect } from 'react';
import { Peer } from 'peerjs';
import { Unity, useUnityContext } from 'react-unity-webgl';
import './App.css';

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

  // ゲームモードの管理 ("1" = 独楽, "0" = 剣)
  const [gameMode, setGameMode] = useState("1");
  const gameModeRef = useRef("1");
  useEffect(() => { gameModeRef.current = gameMode; }, [gameMode]);

  // 相手のUnity(WebGL)のロードが完了したかを管理するフラグ
  const [isEnemyUnityLoaded, setIsEnemyUnityLoaded] = useState(false);
  const enemyUnityLoadedRef = useRef(false);
  useEffect(() => { enemyUnityLoadedRef.current = isEnemyUnityLoaded; }, [isEnemyUnityLoaded]);

  // ▼【復活】消えてしまっていたユーザー名管理のステート
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
    loaderUrl: "/POSE_SWORD_Unity/Builds/ver2.7.1/Build/ver2.7.1.loader.js",
    dataUrl: "/POSE_SWORD_Unity/Builds/ver2.7.1/Build/ver2.7.1.data",
    frameworkUrl: "/POSE_SWORD_Unity/Builds/ver2.7.1/Build/ver2.7.1.framework.js",
    codeUrl: "/POSE_SWORD_Unity/Builds/ver2.7.1/Build/ver2.7.1.wasm",
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
  
  // 1. 自分のUnityロードが終わったら、通信相手に通知
  useEffect(() => {
    if (step === "PLAYING" && isLoaded && connection) {
      console.log("📡 自分のUnityロード完了。相手に通知します。");
      connection.send({ type: "PEER_UNITY_LOADED" });
    }
  }, [step, isLoaded, connection]);

  // 2. 両方のUnityロードが完全に揃ったら、同時に命令を撃ち込む！
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
    setIsEnemyUnityLoaded(false);
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
    setTargetId("");
    isRejectedRef.current = false;
    setRole("CLIENT"); 
    setStep("CLIENT_WAIT");
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

  const connectToHost = () => {
    setSystemMessage(""); // 一旦メッセージをクリア

    // 1. 空欄チェック
    if (!targetId.trim()) {
      setSystemMessage("ロビーIDを入力してください。");
      return;
    }

    // 2. 半角数字以外のチェック（正規表現で数字だけか判定）
    const isOnlyNumbers = /^\d+$/.test(targetId);
    if (!isOnlyNumbers) {
      setSystemMessage("IDが不適切です。半角数字のみで入力してください。");
      return;
    }

    // 3. 桁数チェック（6桁未満の場合）
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
          <div style={styles.container}>
            {mySwordData?.imageSrc && (
              <img src={mySwordData.imageSrc} alt="Background Sword" style={styles.bgImageCenter} />
            )}
            
            <div style={styles.contentWrapper}>
              <img src="/logo.png" alt="オレブレード" style={{ maxWidth: '800px', marginBottom: '40px' }} />
              
              <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', width: '300px' }}>
                {/* 1. 剣を錬成するボタン */}
                <div className="ink-btn-container">
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#4CAF50' }}
                    onClick={() => goToCrafting("TITLE")}
                  >
                    {mySwordData ? "⚔️ 剣を再錬成する" : "⚔️ 剣を錬成する"}
                  </button>
                </div>
                
                <div style={{ borderTop: '2px solid #ddd', margin: '10px 0' }}></div>
                
                {!mySwordData && (
                  <p style={{ color: '#888', fontSize: '14px', margin: '0 0 -10px 0', fontWeight: 'bold' }}>
                    対戦するには、先に剣を錬成してください
                  </p>
                )}

                {/* 2. 部屋を作るボタン */}
                <div className={`ink-btn-container ${!mySwordData ? 'disabled' : ''}`}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    onClick={handleCreateRoom}
                    disabled={!mySwordData}
                  >
                    ロビーを作成
                  </button>
                </div>

                {/* 3. 部屋に入るボタン */}
                <div className={`ink-btn-container ${!mySwordData ? 'disabled' : ''}`}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    onClick={handleJoinRoom}
                    disabled={!mySwordData}
                  >
                    ロビーに入る
                  </button>
                </div>
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
              
              {/* 1. 名前だけ変更して戻るボタン（幅制限の300pxを解除） */}
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

              {/* キャンセルと次へのボタン群（横並びに戻し、幅制限を解除） */}
              <div style={{ marginTop: '20px', display: 'flex', gap: '15px' }}>
                
                {/* 2. キャンセル（戻る）ボタン */}
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

                {/* 3. 次へ（撮影へ） / 新しくポーズを撮り直すボタン */}
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
              
              {/* ▼ ボタン群をまとめるコンテナ（幅を300pxに固定し、縦に並べる） */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '300px' }}>
                
                {/* 1. 撮影する / ポーズをとって！ ボタン（オレンジ） */}
                <div className={`ink-btn-container ${captureCountdown !== null ? 'disabled' : ''}`}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#ff9800' }} /* 撮影のオレンジ色 */
                    onClick={startCaptureCountdown} 
                    disabled={captureCountdown !== null}
                  >
                    {captureCountdown !== null ? "ポーズをとれ！" : "撮影する！"}
                  </button>
                </div>

                {/* 2. 名前入力に戻る ボタン（グレー） */}
                <div className={`ink-btn-container ${captureCountdown !== null ? 'disabled' : ''}`}>
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#666666' }} /* 戻るアクションのグレー */
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
              
              {/* ▼ フォントを黒薔薇に変更、色を黒に指定、マージンで位置を調整 */}
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
              
              {/* ボタン群（幅300px・縦並び） */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: '15px', width: '300px', marginTop: '10px' }}>
                
                {/* 1. 対戦へ進むボタン */}
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

                {/* 2. 剣を再錬成するボタン */}
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

      case "HOST_WAIT":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2 style={{ letterSpacing: '0.1em' }}>ロビーID表示</h2>
              
              {/* ▼ ここを変更：className="glass" を追加し、スタイルのborder等を削除 */}
              <div className="glass" style={{ margin: '20px 0', padding: '30px', width: '100%', maxWidth: '500px', boxSizing: 'border-box' }}>
                
                <p style={{ fontSize: '18px', margin: '0', color: '#555', fontWeight: 'bold' }}>あなたのロビーID</p>
                
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '15px', margin: '20px 0' }}>
                  <p style={{ 
                    fontSize: '48px', 
                    color: 'blue', 
                    fontFamily: 'sans-serif', 
                    fontWeight: 'bold', 
                    letterSpacing: '8px', 
                    margin: '0' 
                  }}>
                    {myPeerId || "取得中..."}
                  </p>
                  
                  {myPeerId && (
                    <button 
                      style={{ 
                        padding: '10px 16px', 
                        fontSize: '16px', 
                        backgroundColor: isCopied ? '#4CAF50' : '#e0e0e0', 
                        color: isCopied ? '#fff' : '#333',
                        border: 'none', 
                        borderRadius: '0', 
                        cursor: 'pointer',
                        fontFamily: 'sans-serif',
                        fontWeight: 'bold',
                        boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                        transition: 'all 0.1s'
                      }} 
                      onClick={handleCopyId}
                    >
                      {isCopied ? "✓ コピーしました" : "📋 コピー"}
                    </button>
                  )}
                </div>
                
                <p style={{ margin: '0', fontSize: '16px', fontWeight: 'bold', color: '#000' }}>
                  このIDを対戦相手に教えてください。
                </p>
              </div>

              {/* タイトルに戻るボタン */}
              <div style={{ width: '300px', marginTop: '50px' }}>
                <div className="ink-btn-container">
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#666666' }}
                    onClick={handleLeave}
                  >
                    タイトルに戻る
                  </button>
                </div>
              </div>

            </div>
          </div>
        );

      case "CLIENT_WAIT":
        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2 style={{ letterSpacing: '0.1em' }}>ロビーID入力</h2>
              
              {/* ▼ ボックスを.glass（直角すりガラス）に変更 */}
              <div className="glass">
                <p style={{ fontSize: '18px', fontWeight: 'bold', color: '#000', margin: '0 0 20px 0' }}>
                  HostのロビーID（6桁の数字）を入力
                </p>
                
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '10px' }}>
                  {/* ▼ 入力欄の角丸(styles.inputの初期値)をインラインで打ち消して直角化 */}
                  <input 
                    type="text" 
                    value={targetId} 
                    onChange={(e) => setTargetId(e.target.value)} 
                    placeholder="例: 123456" 
                    maxLength={6}
                    style={{ 
                      ...styles.input, 
                      borderRadius: '0', 
                      border: '2px solid #000',
                      letterSpacing: '4px', 
                      width: '180px',
                      fontFamily: 'sans-serif', /* 数字が綺麗に見えるフォント */
                      fontWeight: 'bold'
                    }} 
                  />
                  
                  {/* ▼【変更】接続ボタン：墨なし、ホストのコピーボタンと対になる直角の緑ボタン */}
                  <button 
                    style={{ 
                      padding: '10px 20px', 
                      fontSize: '18px', 
                      backgroundColor: '#4CAF50', 
                      color: 'white',
                      border: 'none',
                      borderRadius: '0',
                      cursor: 'pointer',
                      fontFamily: 'sans-serif',
                      fontWeight: 'bold',
                      boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
                      transition: 'background-color 0.1s'
                    }} 
                    onClick={connectToHost}
                  >
                    接続
                  </button>
                </div>
              </div>

              {/* エラーメッセージ領域の高さを固定 */}
              <div style={{ height: '50px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                {systemMessage && (
                  <div style={{ ...styles.errorMessage, margin: '0' }}>
                    ⚠️ {systemMessage}
                  </div>
                )}
              </div>

              {/* ▼ タイトルに戻るボタン（墨ホバーエフェクト版・幅300px） */}
              <div style={{ width: '300px', marginTop: '10px' }}>
                <div className="ink-btn-container">
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#666666' }}
                    onClick={handleLeave}
                  >
                    タイトルに戻る
                  </button>
                </div>
              </div>

            </div>
          </div>
        );

      case "LOBBY":
        // 剣カードとボタンをレンダリングする補助関数
        const renderPlayerSide = (targetRole) => {
          const isMine = targetRole === role;
          const data = isMine ? mySwordData : enemySwordData;

          return (
            <div style={{ width: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
              <div style={styles.swordCard}>
                <h3 style={{ margin: '0 0 10px 0', color: '#000' }}>
                  {isMine ? "あなた" : "対戦相手"}
                </h3>
                {data ? (
                  <>
                    {data.imageSrc ? <img src={data.imageSrc} style={styles.previewImage} /> : <div style={styles.previewImage}>画像受信中...</div>}
                    <p style={{ ...styles.swordName, color: '#000' }}>{data.name}</p>
                    <div style={styles.statsBox}>HP:{data.hp} 攻撃:{data.attack} 重さ:{data.weight}</div>
                  </>
                ) : <div style={{ height: '200px' }}>未錬成</div>}
              </div>

              {/* 再錬成ボタン：自分のみ表示 */}
              <div style={{ height: '80px', marginTop: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', width: '100%' }}>
                {isMine && (
                  <div className="ink-btn-container" style={{ width: '100%' }}>
                    <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                    <button 
                      className="sharp-button" 
                      style={{ '--btn-color': '#000', fontSize: '18px', padding: '15px 20px' }} 
                      onClick={() => goToCrafting("LOBBY")}
                    >
                      ⚔️ 剣を再錬成する
                    </button>
                  </div>
                )}
              </div>
            </div>
          );
        };

        return (
          <div style={styles.container}>
            <div style={styles.contentWrapper}>
              <h2>ロビー（対戦準備）</h2>
              
              {/* ▼ 左右を固定して配置 */}
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '20px', width: '100%', maxWidth: '800px', margin: '20px 0' }}>
                
                {/* 左側：常にHOSTを表示 */}
                <div style={{ flex: 1 }}>{renderPlayerSide("HOST")}</div>
                
                {/* 中央：VS */}
                <div style={styles.vsText}>VS</div>
                
                {/* 右側：常にCLIENTを表示 */}
                <div style={{ flex: 1 }}>{renderPlayerSide("CLIENT")}</div>
              </div>

              <div style={styles.connectedBox}>
                <div style={styles.modeBox}>
                  <h3 style={{ margin: '0 0 10px 0', color: '#333' }}>バトルモード</h3>
                  {role === "HOST" ? (
                    <select value={gameMode} onChange={(e) => { setGameMode(e.target.value); connection.send({ type: "SYNC_GAMEMODE", gameMode: e.target.value }); }} 
                      style={{ padding: '8px', fontSize: '16px', borderRadius: '0' }}>
                      <option value="1">🌀 独楽（見下ろし）モード</option>
                      <option value="0">⚔️ 剣（横視点・重力）モード</option>
                    </select>
                  ) : (
                    <div style={{ fontSize: '18px', fontWeight: 'bold' }}>
                      {gameMode === "1" ? "🌀 独楽（見下ろし）モード" : "⚔️ 剣（横視点・重力）モード"}
                    </div>
                  )}
                </div>

                {countdown !== null ? (
                  <h2 style={{ fontSize: '48px', color: 'red', animation: 'pulse 1s infinite' }}>{countdown > 0 ? countdown : "START!"}</h2>
                ) : (
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'center', gap: '20px', margin: '10px 0 20px 0' }}>
                      {role === "HOST" ? (
                        <>
                          <div style={styles.readyBox(isReady)}>自分: {isReady ? "準備OK!" : "準備中..."}</div>
                          <div style={styles.readyBox(isEnemyReady)}>相手: {isEnemyReady ? "準備OK!" : "準備中..."}</div>
                        </>
                      ) : (
                        <>
                          <div style={styles.readyBox(isEnemyReady)}>相手: {isEnemyReady ? "準備OK!" : "準備中..."}</div>
                          <div style={styles.readyBox(isReady)}>自分: {isReady ? "準備OK!" : "準備中..."}</div>
                        </>
                      )}
                    </div>
                    
                    <div style={{ display: 'flex', justifyContent: 'center', gap: '15px' }}>
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
                          onClick={() => { setIsReady(false); connection.send({ type: "SYNC_STATE", isReady: false }); }}
                          disabled={countdown !== null} 
                        >
                          準備を取り消す
                        </button>
                      )}
                    </div>
                  </div>
                )}
              </div>
              
              <div className="ink-btn-container" style={{ marginTop: '30px' }}>
                <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                <button className="sharp-button" style={{ '--btn-color': '#000' }} onClick={handleLeave}>退出する</button>
              </div>
            </div>
            <style>{`@keyframes pulse { 0% { transform: scale(1); } 50% { transform: scale(1.1); } 100% { transform: scale(1); } }`}</style>
          </div>
        );

      case "PLAYING":
        return (
          <div style={{ ...styles.container, position: 'relative' }}>
            <div style={{ ...styles.unityContainer, position: 'relative' }}>
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
              
              {/* ▼ ボタン群を墨ボタン形式に変更 */}
              <div style={{ marginTop: '40px', display: 'flex', gap: '20px' }}>
                
                {/* 1. ロビーに戻るボタン（緑系の墨ボタン） */}
                <div className="ink-btn-container">
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#4CAF50' }}
                    onClick={() => {
                      try { sendMessage('GameManager', 'ResetMatch', ''); } catch(e) {}
                      setIsReady(false);
                      setIsEnemyUnityLoaded(false);
                      if (connRef.current) connRef.current.send({ type: "SYNC_STATE", isReady: false });
                      setStep("LOBBY");
                    }}
                  >
                    ロビーに戻る
                  </button>
                </div>

                {/* 2. 退出するボタン（グレー系の墨ボタン） */}
                <div className="ink-btn-container">
                  <img src="/sumi_touka.png" className="ink-hover-effect" alt="" />
                  <button 
                    className="sharp-button"
                    style={{ '--btn-color': '#666666' }}
                    onClick={handleLeave}
                  >
                    退出する
                  </button>
                </div>

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
  container: { padding: '30px', display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%', height: '100%', fontFamily: 'Kurobara, serif' },
  contentWrapper: { zIndex: 1, position: 'relative', display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' },
  
  bgImageCenter: { position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)', height: '100vh', opacity: 0.15, pointerEvents: 'none', zIndex: 0 },

  button: { padding: '10px 20px', fontSize: '18px', cursor: 'pointer', borderRadius: '5px', fontWeight: 'bold', border: 'none', boxShadow: '0 2px 4px rgba(0,0,0,0.2)' ,fontFamily: 'Kurobara, serif'},
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
  statsBox: { display: 'flex', justifyContent: 'center', gap: '10px', fontSize: '14px', fontWeight: 'bold', color: '#555', backgroundColor: '#f9f9f9', padding: '5px 10px', borderRadius: '5px', width: '100%'},
  vsText: { fontSize: '36px', fontWeight: '900', fontStyle: 'italic', color: '#ff9800', textShadow: '2px 2px 0px #000' },
  countdownOverlay: { 
    position: 'absolute',
    top: '20px',
    left: '50%',
    transform: 'translateX(-50%)',
    fontSize: '80px',
    fontWeight: 'bold',
    color: 'rgba(255, 255, 255, 0.7)',
    textShadow: '0 0 20px red, 2px 2px 0px #000, -2px -2px 0px #000, 2px -2px 0px #000, -2px 2px 0px #000',
    pointerEvents: 'none',
    zIndex: 10
  },
  loadingOverlay: {
    position: 'absolute',
    top: 0, left: 0, width: '100%', height: '100%',
    backgroundColor: 'rgba(0, 0, 0, 0.9)',
    display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
    zIndex: 100
  },
  loadingSpinner: {
    width: '50px', height: '50px',
    border: '5px solid rgba(255,255,255,0.3)',
    borderTop: '5px solid orange',
    borderRadius: '50%',
    animation: 'spin 1s linear infinite'
  }
};