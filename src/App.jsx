import React, { useState, useRef, useEffect } from 'react';
import { Peer } from 'peerjs';
import { Unity, useUnityContext } from 'react-unity-webgl';

export default function PoseSwordWeb() {
  const [step, setStep] = useState("LOBBY");
  const stepRef = useRef(step);
  useEffect(() => { stepRef.current = step; }, [step]);
  
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

  // ▼【新規追加】ゲームモードの管理 ("1" = 独楽, "0" = 剣)
  const [gameMode, setGameMode] = useState("1");
  const gameModeRef = useRef("1");
  useEffect(() => { gameModeRef.current = gameMode; }, [gameMode]);

  const [myPeerId, setMyPeerId] = useState("");
  const [targetId, setTargetId] = useState("");
  const peerRef = useRef(null);

  const [isCrafting, setIsCrafting] = useState(false);
  const [captureCountdown, setCaptureCountdown] = useState(null);
  const videoRef = useRef(null);
  const canvasRef = useRef(null);
  const [matchResult, setMatchResult] = useState({ winnerName: "", damageDealt: 0, damageTaken: 0 });

  const [isReady, setIsReady] = useState(false);
  const [isEnemyReady, setIsEnemyReady] = useState(false);
  const [countdown, setCountdown] = useState(null);
  const [systemMessage, setSystemMessage] = useState("");

  const { unityProvider, sendMessage, isLoaded } = useUnityContext({
    loaderUrl: "../POSE_SWORD_Unity/Builds/ver2.3/Build/ver2.3.loader.js",
    dataUrl: "../POSE_SWORD_Unity/Builds/ver2.3/Build/ver2.3.data",
    frameworkUrl: "../POSE_SWORD_Unity/Builds/ver2.3/Build/ver2.3.framework.js",
    codeUrl: "../POSE_SWORD_Unity/Builds/ver2.3/Build/ver2.3.wasm",
  });

  const pendingBattleRef = useRef(null);
  const syncCountRef = useRef({ fromUnity: 0, toPeer: 0, fromPeer: 0, toUnity: 0 });

  const sendMessageRef = useRef(sendMessage);
  useEffect(() => { sendMessageRef.current = sendMessage; }, [sendMessage]);

  const handleGameOverRef = useRef(null);

  const handleGameOver = (syncData) => {
    const currentRole = roleRef.current;
    const clientWon = syncData.hostSword.hp <= 0;

    const myInitialHp = mySwordRef.current?.hp ?? 100;
    const enemyInitialHp = enemySwordRef.current?.hp ?? 100;

    const hostName = currentRole === "HOST" ? mySwordRef.current?.name : enemySwordRef.current?.name;
    const clientName = currentRole === "CLIENT" ? mySwordRef.current?.name : enemySwordRef.current?.name;

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
      damageTaken
    });
    
    setIsReady(false);
    setIsEnemyReady(false);
    setCountdown(null);
    setStep("RESULT");
  };

  useEffect(() => { handleGameOverRef.current = handleGameOver; });

  useEffect(() => {
    if (isLoaded && pendingBattleRef.current !== null) {
      // ▼【修正】gameMode を取り出して Unity に送る
      const { mode, startJson, gameModeStr } = pendingBattleRef.current;
      pendingBattleRef.current = null;
      console.log("✅ Unity読み込み完了！保留中のバトルコマンドを送信します");
      
      console.log(`📡 SetHostMode(${mode})`);
      sendMessage('GameManager', 'SetHostMode', mode);
      
      // ▼【新規追加】Unity側の NetworkManager にゲームモードを指示する
      console.log(`📡 SetGameMode(${gameModeStr})`);
      sendMessage('GameManager', 'SetGameMode', gameModeStr);
      
      console.log(`📡 StartBattle`);
      sendMessage('GameManager', 'StartBattle', JSON.stringify(startJson)); // ※StartBattleはSceneControllerに変更済み
      
      console.log("✅ 全バトル初期化コマンド送信完了");
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
        else if (type === "INPUT" && currentConn && currentRole === "CLIENT") {
          currentConn.send({ type: "INPUT", ...data });
        }
      }
    };
  }, []);

  useEffect(() => {
    let stream = null;
    if (step === "CRAFT") {
      navigator.mediaDevices.getUserMedia({ video: true })
        .then((s) => { stream = s; if (videoRef.current) videoRef.current.srcObject = stream; })
        .catch((err) => console.error("カメラエラー:", err));
    }
    return () => { if (stream) stream.getTracks().forEach(track => track.stop()); };
  }, [step]);

  useEffect(() => {
    if (isReady && isEnemyReady && step === "MATCHING" && countdown === null) {
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
          alert("剣データの準備ができていません。");
        }
      }
    }
  }, [countdown]);

  useEffect(() => {
    if (connection && mySwordRef.current) {
      setTimeout(() => {
        connection.send({ type: "EXCHANGE_SWORD", swordData: mySwordRef.current });
      }, 500);
    }
  }, [connection]);

  useEffect(() => {
    if (captureCountdown !== null) {
      if (captureCountdown > 0) {
        const timer = setTimeout(() => setCaptureCountdown(captureCountdown - 1), 1000);
        return () => clearTimeout(timer);
      } else {
        setCaptureCountdown(null);
        executeCaptureAndCraft(); // 0になったら実際の撮影を実行
      }
    }
  }, [captureCountdown]);

  const resetToLobby = (msg = "") => {
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
    setMySwordData(null);
    setEnemySwordData(null);
    setRole(null);
    setSystemMessage(msg);
    setGameMode("1"); // ▼ 追加：モードリセット
    setStep("LOBBY");
  };

  const handleLeave = () => {
    if (connRef.current) {
      connRef.current.send({ type: "LEAVE" });
    }
    resetToLobby(""); 
  };

  const handleCreateRoom = () => {
    setSystemMessage("");
    setRole("HOST"); setStep("CRAFT");

    const maxRetries = 5;

    const attemptCreatePeer = (retriesLeft) => {
      // 6桁のランダムIDを生成
      const hostId = Math.floor(100000 + Math.random() * 900000).toString();
      const peer = new Peer(hostId);

      // 成功時：IDをセットし、接続待ち状態にする
      peer.on('open', (id) => {
        console.log(`部屋を作成しました。ID: ${id}`);
        setMyPeerId(id);
        peerRef.current = peer;
        
        peer.on('connection', (conn) => { 
          setConnection(conn); 
          setupConnection(conn); 
        });
      });

      // エラー時：ID重複なら自動リトライ、それ以外はエラー出力
      peer.on('error', (err) => {
        if (err.type === 'unavailable-id') {
          console.warn(`ID: ${hostId} は既に使用されています。`);
          peer.destroy(); // 失敗したPeerインスタンスを破棄

          if (retriesLeft > 0) {
            console.log(`新しいIDで再試行します... (残り ${retriesLeft} 回)`);
            attemptCreatePeer(retriesLeft - 1);
          } else {
            alert("サーバーが混雑しており、部屋の作成に失敗しました。少し時間を置いてから再度お試しください。");
            resetToLobby("");
          }
        } else {
          console.error("PeerJS通信エラー:", err);
        }
      });
    };

    attemptCreatePeer(maxRetries);
  };

  const handleJoinRoom = () => {
    setSystemMessage("");
    setRole("CLIENT"); setStep("CRAFT");
    const peer = new Peer();
    peer.on('open', (id) => setMyPeerId(id));
    peerRef.current = peer;
  };

  const connectToHost = () => {
    if (!peerRef.current || !targetId) return;
    const conn = peerRef.current.connect(targetId);
    conn.on('open', () => { setConnection(conn); setupConnection(conn); });
  };

  const setupConnection = (conn) => {
    conn.on('data', (data) => {
      const currentRole = roleRef.current;

      switch (data.type) {
        // ▼【新規追加】ゲームモードの同期受信（Client側）
        case "SYNC_GAMEMODE":
          console.log("【受信】ゲームモード変更:", data.gameMode);
          if (currentRole === "CLIENT") {
            setGameMode(data.gameMode);
          }
          break;

        case "EXCHANGE_SWORD":
          const enemyData1 = { ...data.swordData };
          if (enemyData1.imageStr && !enemyData1.imageSrc) {
            enemyData1.imageSrc = enemyData1.imageStr.startsWith("data:") 
              ? enemyData1.imageStr 
              : "data:image/png;base64," + enemyData1.imageStr;
          }
          setEnemySwordData(enemyData1);
          break;

        case "SYNC_STATE": 
          if (data.swordData) {
            const enemyData2 = { ...data.swordData };
            if (enemyData2.imageStr && !enemyData2.imageSrc) {
              enemyData2.imageSrc = enemyData2.imageStr.startsWith("data:") 
                ? enemyData2.imageStr 
                : "data:image/png;base64," + enemyData2.imageStr;
            }
            setEnemySwordData(enemyData2);
          }
          setIsEnemyReady(data.isReady);
          break;

        case "LEAVE":
          resetToLobby("相手が部屋を退出しました。"); 
          break;

        case "INPUT":
          if (currentRole === "HOST") {
            try {
              sendMessageRef.current('GameManager', 'ReceiveInput', JSON.stringify(data));
            } catch(e) {
              console.error("INPUT転送エラー:", e);
            }
          }
          break;

        case "SYNC":
          if (currentRole === "CLIENT") {
            syncCountRef.current.fromPeer++;   
            try {
              syncCountRef.current.toUnity++;  
              sendMessageRef.current('GameManager', 'SyncTransform', JSON.stringify(data));
            } catch(e) {
              syncCountRef.current.toUnity--;  
            }
            if (data.hostSword.hp <= 0 || data.clientSword.hp <= 0) {
              if (handleGameOverRef.current) handleGameOverRef.current(data);
            }
          }
          break;
        default:
          break;
      }
    });

    conn.on('close', () => {
      if (peerRef.current && stepRef.current !== "LOBBY") {
        resetToLobby("通信が切断されました。");
      }
    });
  };

  const launchUnityBattle = (currentRole, myData, enemyData) => {
    const hostData = currentRole === "HOST" ? myData : enemyData;
    const clientData = currentRole === "CLIENT" ? myData : enemyData;
    
    if (!hostData || !clientData) {
      alert("両方の剣データが準備できていません。もう一度やり直してください。");
      return;
    }
    
    const toUnityData = (data) => ({
      name: data.name,
      hp: data.hp,
      attack: data.attack,
      weight: data.weight,
      imageStr: data.imageStr  
    });

    const startJson = {
      hostSword: toUnityData(hostData),
      clientSword: toUnityData(clientData)
    };
    
    const mode = currentRole === "HOST" ? 1 : 0;
    
    // ▼【修正】ゲームモード (gameModeRef.current) も一緒に pending に保存する
    pendingBattleRef.current = { mode, startJson, gameModeStr: gameModeRef.current };
    setStep("PLAYING"); 
  };

  const handleReady = () => {
    setIsReady(true);
    if (connection && mySwordRef.current) {
      connection.send({ type: "SYNC_STATE", isReady: true });
    }
  };

  const startCaptureCountdown = () => {
    setCaptureCountdown(5); // 5秒のポーズ時間
  };

  // ▼【変更】カウントダウンが0になったら実行される実際の撮影＆API送信処理
  const executeCaptureAndCraft = () => {
    setIsCrafting(true);
    const canvas = canvasRef.current;
    const context = canvas.getContext('2d');
    
    // --- 画像を左右反転してキャンバスに描画 ---
    context.save();
    context.scale(-1, 1); // X軸を反転
    context.translate(-canvas.width, 0); // 反転した分だけ位置をずらす
    context.drawImage(videoRef.current, 0, 0, canvas.width, canvas.height);
    context.restore(); 
    // ------------------------------------------
    
    const base64Full = canvas.toDataURL('image/jpeg');
    const base64DataOnly = base64Full.split(',')[1]; 

    const pythonApiUrl = `${import.meta.env.VITE_API_URL ?? 'https://akequreru-pose-sword-api.hf.space'}/cutout`;

    fetch(pythonApiUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ imageData: base64DataOnly }) 
    })
    .then((response) => {
      if (!response.ok) throw new Error(`HTTPエラー: ${response.status}`);
      return response.json();
    })
    .then((data) => {
      setMySwordData({
        name: role === "HOST" ? "ホストブレード" : "クライアントソード",
        hp: data.params.hp,
        attack: data.params.attack,
        weight: data.params.weight,
        imageStr: data.imageData,  
        imageSrc: "data:image/png;base64," + data.imageData 
      });
      setIsCrafting(false);
      setStep("MATCHING");
    })
    .catch((error) => {
      console.error("PythonAPI通信エラー:", error);
      alert("AIサーバーとの通信に失敗しました。");
      setIsCrafting(false);
    });
  };

  const renderScreen = () => {
    switch (step) {
      case "LOBBY":
        return (
          <div style={styles.container}>
            <h1>POSE SWORD</h1>
            {systemMessage && <div style={styles.errorMessage}>⚠️ {systemMessage}</div>}
            <button style={styles.button} onClick={handleCreateRoom}>部屋を作る (Host)</button>
            <button style={styles.button} onClick={handleJoinRoom}>部屋に入る (Client)</button>
          </div>
        );

      case "CRAFT":
        return (
          <div style={styles.container}>
            <h2>剣の錬成</h2>
            
            {/* ▼【変更】ビデオ領域を相対配置にし、上にカウントダウンを被せる */}
            <div style={{ position: 'relative', width: '400px', marginBottom: '20px' }}>
              <video ref={videoRef} autoPlay playsInline style={styles.video} />
              
              {/* カウントダウン実行中のみ大きな数字（0の時は📸）を表示 */}
              {captureCountdown !== null && (
                <div style={styles.countdownOverlay}>
                  {captureCountdown > 0 ? captureCountdown : "📸"}
                </div>
              )}
            </div>

            <canvas ref={canvasRef} width="640" height="480" style={{ display: 'none' }} />
            
            {/* ボタンの制御: カウントダウン中や錬成中は押せないようにする */}
            <button 
              style={{ 
                ...styles.button, 
                backgroundColor: (isCrafting || captureCountdown !== null) ? 'gray' : 'orange' 
              }} 
              onClick={startCaptureCountdown} 
              disabled={isCrafting || captureCountdown !== null}
            >
              {isCrafting ? "錬成中..." : captureCountdown !== null ? "ポーズをとって！" : "撮影して剣を錬成！"}
            </button>
            <button 
              style={{
                ...styles.button, 
                backgroundColor: (isCrafting || captureCountdown !== null) ? 'gray' : '#333', 
                color: 'white', 
                display: 'block', 
                margin: '20px auto',
                opacity: (isCrafting || captureCountdown !== null) ? 0.5 : 1,
                cursor: (isCrafting || captureCountdown !== null) ? 'not-allowed' : 'pointer'
              }} 
              onClick={() => resetToLobby("")}
              disabled={isCrafting || captureCountdown !== null}
            >
              タイトルに戻る
            </button>
          </div>
        );

      case "MATCHING":
        return (
          <div style={styles.container}>
            <h2>マッチング待機</h2>
            
            {!connection && (
              <div style={{ marginBottom: '20px' }}>
                {role === "HOST" && (
                  <div>
                    <p style={{ fontSize: '20px', margin: '0' }}>あなたの部屋ID</p>
                    {/* 6桁の数字を大きく、字間を開けて見やすく表示 */}
                    <p style={{ fontSize: '48px', color: 'blue', fontWeight: 'bold', letterSpacing: '8px', margin: '10px 0' }}>
                      {myPeerId || "取得中..."}
                    </p>
                    <p>このIDをClient（対戦相手）に教えてください。</p>
                  </div>
                )}
                
                {role === "CLIENT" && (
                  <div style={{ padding: '20px', backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 2px 8px rgba(0,0,0,0.1)' }}>
                    <p style={{ fontSize: '18px', fontWeight: 'bold' }}>Hostの部屋ID（6桁の数字）を入力</p>
                    <input 
                      type="text" 
                      value={targetId} 
                      onChange={(e) => setTargetId(e.target.value)} 
                      placeholder="例: 123456" 
                      maxLength={6} // 6文字までしか入力できないように制限
                      style={{ 
                        padding: '10px', 
                        fontSize: '24px', 
                        width: '180px', 
                        textAlign: 'center', 
                        letterSpacing: '4px',
                        borderRadius: '5px',
                        border: '2px solid #ccc'
                      }} 
                    />
                    <button style={{ ...styles.button, marginLeft: '10px', backgroundColor: '#4CAF50', color: 'white' }} onClick={connectToHost}>
                      接続
                    </button>
                  </div>
                )}
              </div>
            )}
            
            <div style={styles.previewContainer}>
              {/* 表示用のデータを事前に整理し、常に左右の枠を表示する */}
              {(() => {
                const isHost = role === "HOST";
                const hostData = isHost ? mySwordData : enemySwordData;
                const clientData = !isHost ? mySwordData : enemySwordData;
                
                return (
                  <>
                    {/* 左側：ホストの剣 */}
                    <div style={styles.swordCard}>
                      <h3 style={{ margin: '0 0 10px 0', color: isHost ? '#000000' : '#ff4444' }}>
                        {isHost ? "あなた" : "対戦相手"}
                      </h3>
                      {hostData ? (
                        <>
                          <img src={hostData.imageSrc || hostData.imageStr} alt="Host Sword" style={styles.previewImage} />
                          <p style={styles.swordName}>{hostData.name}</p>
                          <div style={styles.statsBox}>
                            <span>HP: {hostData.hp}</span>
                            <span>攻撃: {hostData.attack}</span>
                            <span>重さ: {hostData.weight}</span>
                          </div>
                        </>
                      ) : (
                        <div style={{ height: '200px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                          <p style={{ color: '#888', fontWeight: 'bold' }}>未接続（ID入力待ち）</p>
                        </div>
                      )}
                    </div>

                    {/* 中央：VSマーク（常に出しておく） */}
                    <div style={styles.vsText}>VS</div>

                    {/* 右側：クライアントの剣 */}
                    <div style={styles.swordCard}>
                      <h3 style={{ margin: '0 0 10px 0', color: isHost ? '#FF4444' : '#000000' }}>
                        {!isHost ? "あなた" : "対戦相手"}
                      </h3>
                      {clientData ? (
                        <>
                          <img src={clientData.imageSrc || clientData.imageStr} alt="Client Sword" style={styles.previewImage} />
                          <p style={styles.swordName}>{clientData.name}</p>
                          <div style={styles.statsBox}>
                            <span>HP: {clientData.hp}</span>
                            <span>攻撃: {clientData.attack}</span>
                            <span>重さ: {clientData.weight}</span>
                          </div>
                        </>
                      ) : (
                        <div style={{ height: '200px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                          <p style={{ color: '#888', fontWeight: 'bold' }}>
                            {isHost ? "対戦相手の接続待機中..." : "データ受信中..."}
                          </p>
                        </div>
                      )}
                    </div>
                  </>
                );
              })()}
            </div>

            {connection && (
              <div style={styles.connectedBox}>

                {/* ▼【新規追加】ゲームモード選択UI */}
                <div style={styles.modeBox}>
                  <h3 style={{ margin: '0 0 10px 0' }}>バトルモード</h3>
                  {role === "HOST" ? (
                    <select
                      value={gameMode}
                      onChange={(e) => {
                        const newMode = e.target.value;
                        setGameMode(newMode);
                        connection.send({ type: "SYNC_GAMEMODE", gameMode: newMode });
                      }}
                      style={{ padding: '8px', fontSize: '16px', borderRadius: '5px', cursor: 'pointer' }}
                    >
                      <option value="1">🌀 独楽（見下ろし）モード</option>
                      <option value="0">⚔️ 剣（横視点・重力）モード</option>
                    </select>
                  ) : (
                    <div style={{ fontSize: '18px', fontWeight: 'bold', color: '#333' }}>
                      {gameMode === "1" ? "🌀 独楽（見下ろし）モード" : "⚔️ 剣（横視点・重力）モード"}
                    </div>
                  )}
                </div>
                {/* ▲ ここまで */}

                {countdown !== null ? (
                  <h2 style={{ fontSize: '48px', color: 'red', margin: '0' }}>{countdown > 0 ? countdown : "START!"}</h2>
                ) : (
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'center', gap: '20px', margin: '10px 0 20px 0' }}>
                      <div style={styles.readyBox(isReady)}>自分: {isReady ? "準備OK!" : "準備中..."}</div>
                      <div style={styles.readyBox(isEnemyReady)}>相手: {isEnemyReady ? "準備OK!" : "準備中..."}</div>
                    </div>
                    {!isReady ? (
                      <button style={{ ...styles.button, backgroundColor: 'orange', color: 'white' }} onClick={handleReady}>
                        準備OK（バトルへ）
                      </button>
                    ) : (
                      <p style={{ fontWeight: 'bold' }}>相手の準備を待っています...</p>
                    )}
                  </div>
                )}
              </div>
            )}
            
            <button style={{ ...styles.button, backgroundColor: 'gray', color: 'white', marginTop: '20px' }} onClick={connection ? handleLeave : () => resetToLobby("")}>
              {connection ? "退出する" : "キャンセル"}
            </button>
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
            <h2>決着！</h2>
            <h3>勝者: {matchResult.winnerName}</h3>
            <p>与えたダメージ: {matchResult.damageDealt}</p>
            <p>受けたダメージ: {matchResult.damageTaken}</p>
            <div style={{ marginTop: '30px' }}>
              <button style={{ ...styles.button, backgroundColor: 'orange', color: 'white' }} onClick={() => {
                try { sendMessage('GameManager', 'ResetMatch', ''); } catch(e) {}
                setStep("MATCHING");
                if (connRef.current && mySwordRef.current) {
                  connRef.current.send({ type: "SYNC_STATE", isReady: false });
                }
              }}>
                もう一度遊ぶ（待機画面へ）
              </button>
              <button style={{ ...styles.button, backgroundColor: 'gray', color: 'white' }} onClick={handleLeave}>退出する</button>
            </div>
          </div>
        );
      default: return <div>Error</div>;
    }
  };

  return <div style={{ fontFamily: 'sans-serif', textAlign: 'center', backgroundColor: '#f5f5f5', minHeight: '100vh' }}>{renderScreen()}</div>;
}

const styles = {
  container: { padding: '30px', display: 'flex', flexDirection: 'column', alignItems: 'center' },
  button: { padding: '10px 20px', margin: '10px', fontSize: '18px', cursor: 'pointer', borderRadius: '5px', fontWeight: 'bold', border: 'none', boxShadow: '0 2px 4px rgba(0,0,0,0.2)' },
  connectedBox: { marginTop: '10px', padding: '10px 20px', backgroundColor: '#ffffff', borderRadius: '8px', width: '100%', maxWidth: '600px', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' },
  modeBox: { marginBottom: '20px', padding: '15px', backgroundColor: '#f0f8ff', borderRadius: '8px', border: '1px solid #cce7ff' }, // 追加
  video: { width: '400px', borderRadius: '8px', backgroundColor: '#000', display: 'block', transform: 'scaleX(-1)' },
  unityContainer: { width: '800px', height: '450px', backgroundColor: '#222', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '4px solid #555' },
readyBox: (isReady) => ({
    padding: '10px 20px', border: `2px solid ${isReady ? '#4CAF50' : '#9e9e9e'}`, backgroundColor: isReady ? '#e8f5e9' : '#f5f5f5', borderRadius: '8px', fontWeight: 'bold', minWidth: '100px'
  }),
  errorMessage: { padding: '15px 25px', backgroundColor: '#ffdddd', color: '#cc0000', borderRadius: '8px', marginBottom: '20px', fontWeight: 'bold', border: '1px solid #cc0000' },
  previewContainer: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '20px', margin: '20px 0', width: '100%', maxWidth: '800px' },
  swordCard: { flex: 1, backgroundColor: '#fff', borderRadius: '12px', padding: '15px', boxShadow: '0 4px 10px rgba(0,0,0,0.15)', display: 'flex', flexDirection: 'column', alignItems: 'center', border: '2px solid #e0e0e0' },
  previewImage: { width: '100%', height: '200px', objectFit: 'contain', backgroundColor: '#f0f0f0', borderRadius: '8px', marginBottom: '10px' },
  swordName: { fontSize: '20px', fontWeight: 'bold', margin: '5px 0' },
  statsBox: { display: 'flex', justifyContent: 'center', gap: '10px', fontSize: '14px', fontWeight: 'bold', color: '#555', backgroundColor: '#f9f9f9', padding: '5px 10px', borderRadius: '5px', width: '100%' },
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
    pointerEvents: 'none', // クリック等の操作を邪魔しないようにする
    zIndex: 10
  },
};