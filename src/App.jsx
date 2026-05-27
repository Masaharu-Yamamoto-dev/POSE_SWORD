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

  const [myPeerId, setMyPeerId] = useState("");
  const [targetId, setTargetId] = useState("");
  const peerRef = useRef(null);

  const [isCrafting, setIsCrafting] = useState(false);
  const videoRef = useRef(null);
  const canvasRef = useRef(null);
  const [matchResult, setMatchResult] = useState({ winnerName: "", damageDealt: 0, damageTaken: 0 });

  const [isReady, setIsReady] = useState(false);
  const [isEnemyReady, setIsEnemyReady] = useState(false);
  const [countdown, setCountdown] = useState(null);
  const [systemMessage, setSystemMessage] = useState("");

  const { unityProvider, sendMessage, isLoaded } = useUnityContext({
    loaderUrl: "../POSE_SWORD_Unity/Builds/ver1.0/Build/ver1.0.loader.js",
    dataUrl: "../POSE_SWORD_Unity/Builds/ver1.0/Build/ver1.0.data",
    frameworkUrl: "../POSE_SWORD_Unity/Builds/ver1.0/Build/ver1.0.framework.js",
    codeUrl: "../POSE_SWORD_Unity/Builds/ver1.0/Build/ver1.0.wasm",
  });

  const handleGameOverRef = useRef(null);

  const handleGameOver = (syncData) => {
    const currentRole = roleRef.current;
    const clientWon = syncData.hostSword.hp <= 0;
    setMatchResult({
      winnerName: clientWon ? "Client側の剣" : "Host側の剣",
      damageDealt: currentRole === "HOST" ? (clientWon ? 0 : 100) : (clientWon ? 100 : 0),
      damageTaken: currentRole === "HOST" ? (clientWon ? 100 : 0) : (clientWon ? 0 : 100)
    });
    
    setIsReady(false);
    setIsEnemyReady(false);
    setCountdown(null);
    setStep("RESULT");
  };

  useEffect(() => { handleGameOverRef.current = handleGameOver; });

  useEffect(() => {
    window.ReactApp = {
      receiveFromUnity: (type, jsonString) => {
        const data = JSON.parse(jsonString);
        const currentRole = roleRef.current;
        const currentConn = connRef.current;

        if (type === "SYNC" && currentRole === "HOST") {
          if (currentConn) currentConn.send({ type: "SYNC", ...data });
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
        launchUnityBattle(roleRef.current, mySwordRef.current, enemySwordRef.current);
      }
    }
  }, [countdown]);

  // 【追加】通信が繋がった瞬間に、お互いの剣のデータを送り合う（プレビュー用）
  useEffect(() => {
    if (connection && mySwordRef.current) {
      // 少しだけ待ってから送る（接続直後のパケットロス防止）
      setTimeout(() => {
        connection.send({ type: "EXCHANGE_SWORD", swordData: mySwordRef.current });
      }, 500);
    }
  }, [connection]);

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
    const peer = new Peer();
    peer.on('open', (id) => setMyPeerId(id));
    peer.on('connection', (conn) => { setConnection(conn); setupConnection(conn); });
    peerRef.current = peer;
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
        // 【追加】プレビュー用の剣データを受信
        case "EXCHANGE_SWORD":
          console.log("【受信】相手の剣データを確認しました");
          setEnemySwordData(data.swordData);
          break;

        case "SYNC_STATE": 
          if (data.swordData) setEnemySwordData(data.swordData);
          setIsEnemyReady(data.isReady);
          break;

        case "LEAVE":
          resetToLobby("相手が部屋を退出しました。"); 
          break;

        case "INPUT":
          if (currentRole === "HOST") {
            try { sendMessage('GameManager', 'ReceiveInput', JSON.stringify(data)); } catch(e) {}
          }
          break;

        case "SYNC":
          if (currentRole === "CLIENT") {
            try { sendMessage('GameManager', 'SyncTransform', JSON.stringify(data)); } catch(e) {}
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
    const startJson = {
      hostSword: currentRole === "HOST" ? myData : enemyData,
      clientSword: currentRole === "CLIENT" ? myData : enemyData
    };
    
    try {
      const mode = currentRole === "HOST" ? 1 : 0;
      sendMessage('GameManager', 'SetHostMode', mode);
      sendMessage('SceneController', 'StartBattle', JSON.stringify(startJson));
    } catch (e) {
      console.warn("※Unity未ロードによるSendMessageスキップ");
    }
    
    setStep("PLAYING");
  };

  const handleReady = () => {
    setIsReady(true);
    if (connection && mySwordRef.current) {
      connection.send({ type: "SYNC_STATE", isReady: true });
    }
  };

  const captureAndCraft = () => {
    setIsCrafting(true);
    const canvas = canvasRef.current;
    const context = canvas.getContext('2d');
    context.drawImage(videoRef.current, 0, 0, canvas.width, canvas.height);
    
    const base64Full = canvas.toDataURL('image/jpeg');
    const base64DataOnly = base64Full.split(',')[1]; 

    // ※ PythonAPIのIPアドレスに書き換えてください（例: 192.168.x.x）
    const pythonApiUrl = 'http://127.0.0.1:8000/cutout';

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
      const resultImageUrl = "data:image/png;base64," + data.imageData;
      setMySwordData({
        name: role === "HOST" ? "ホストブレード" : "クライアントソード",
        hp: data.params.hp,
        attack: data.params.attack,
        weight: data.params.weight,
        imageStr: resultImageUrl
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
            <video ref={videoRef} autoPlay playsInline style={styles.video} />
            <canvas ref={canvasRef} width="640" height="480" style={{ display: 'none' }} />
            <button style={{ ...styles.button, backgroundColor: isCrafting ? 'gray' : 'orange' }} onClick={captureAndCraft} disabled={isCrafting}>
              {isCrafting ? "錬成中..." : "撮影して剣を錬成！"}
            </button>
            <button style={{...styles.button, backgroundColor: '#333', color: 'white', display: 'block', margin: '20px auto'}} onClick={() => resetToLobby("")}>
              タイトルに戻る
            </button>
          </div>
        );

      case "MATCHING":
        return (
          <div style={styles.container}>
            <h2>マッチング待機</h2>
            
            {/* 接続前の表示 */}
            {!connection && (
              <div>
                <p>あなたのID: <strong style={{ color: 'blue' }}>{myPeerId || "取得中..."}</strong></p>
                {role === "HOST" && <p>このIDをClientに教えてください...</p>}
                {role === "CLIENT" && (
                  <div>
                    <input type="text" value={targetId} onChange={(e) => setTargetId(e.target.value)} placeholder="HostのIDを入力" />
                    <button style={styles.button} onClick={connectToHost}>接続</button>
                  </div>
                )}
              </div>
            )}
            
            {/* 【追加】剣のプレビュー画面（自分と相手） */}
            <div style={styles.previewContainer}>
              {/* 自分の剣 */}
              {mySwordData && (
                <div style={styles.swordCard}>
                  <h3 style={{ margin: '0 0 10px 0', color: '#333' }}>自分の剣</h3>
                  <img src={mySwordData.imageStr} alt="My Sword" style={styles.previewImage} />
                  <p style={styles.swordName}>{mySwordData.name}</p>
                  <div style={styles.statsBox}>
                    <span>HP: {mySwordData.hp}</span>
                    <span>攻撃: {mySwordData.attack}</span>
                    <span>重さ: {mySwordData.weight}</span>
                  </div>
                </div>
              )}

              {/* VSマーク */}
              {connection && <div style={styles.vsText}>VS</div>}

              {/* 相手の剣 */}
              {connection && (
                <div style={styles.swordCard}>
                  <h3 style={{ margin: '0 0 10px 0', color: '#ff4444' }}>相手の剣</h3>
                  {enemySwordData ? (
                    <>
                      <img src={enemySwordData.imageStr} alt="Enemy Sword" style={styles.previewImage} />
                      <p style={styles.swordName}>{enemySwordData.name}</p>
                      <div style={styles.statsBox}>
                        <span>HP: {enemySwordData.hp}</span>
                        <span>攻撃: {enemySwordData.attack}</span>
                        <span>重さ: {enemySwordData.weight}</span>
                      </div>
                    </>
                  ) : (
                    <div style={{ height: '200px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                      <p>データ受信中...</p>
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* 接続後の準備・カウントダウンUI */}
            {connection && (
              <div style={styles.connectedBox}>
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
            <h2>バトル中！</h2>
            <div style={styles.unityContainer}>
              <Unity unityProvider={unityProvider} style={{ width: '100%', height: '100%' }} />
            </div>
            {!isLoaded && <p style={{color: '#ff4444', fontWeight: 'bold'}}>※Unity未ロード（ダミー画面）</p>}
            <div style={styles.debugPanel}>
              <h3>🛠 Unity連携デバッグパネル</h3>
              <button style={styles.button} onClick={() => {
                if (role === "HOST") {
                  window.ReactApp.receiveFromUnity("SYNC", JSON.stringify({ hostSword: { hp: 50 }, clientSword: { hp: 100 } }));
                } else {
                  window.ReactApp.receiveFromUnity("INPUT", JSON.stringify({ action: "JUMP" }));
                }
              }}>
                {role === "HOST" ? "【Host】UnityがSYNCを送るフリ" : "【Client】UnityがINPUTを送るフリ"}
              </button>
              <button style={{...styles.button, backgroundColor: '#333', color: '#fff'}} onClick={() => {
                if (role === "HOST") window.ReactApp.receiveFromUnity("SYNC", JSON.stringify({ hostSword: { hp: 0 }, clientSword: { hp: 100 } }));
              }}>
                強制決着テスト（Host専用）
              </button>
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
  video: { width: '400px', borderRadius: '8px', backgroundColor: '#000', marginBottom: '20px' },
  unityContainer: { width: '800px', height: '450px', backgroundColor: '#222', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '4px solid #555' },
  debugPanel: { marginTop: '20px', padding: '15px', border: '2px dashed gray', borderRadius: '8px', backgroundColor: '#f9f9f9', textAlign: 'center' },
  readyBox: (isReady) => ({
    padding: '10px 20px', border: `2px solid ${isReady ? '#4CAF50' : '#9e9e9e'}`, backgroundColor: isReady ? '#e8f5e9' : '#f5f5f5', borderRadius: '8px', fontWeight: 'bold', minWidth: '100px'
  }),
  errorMessage: { padding: '15px 25px', backgroundColor: '#ffdddd', color: '#cc0000', borderRadius: '8px', marginBottom: '20px', fontWeight: 'bold', border: '1px solid #cc0000' },
  
  // 【追加】プレビュー表示用のスタイル
  previewContainer: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '20px', margin: '20px 0', width: '100%', maxWidth: '800px' },
  swordCard: { flex: 1, backgroundColor: '#fff', borderRadius: '12px', padding: '15px', boxShadow: '0 4px 10px rgba(0,0,0,0.15)', display: 'flex', flexDirection: 'column', alignItems: 'center', border: '2px solid #e0e0e0' },
  previewImage: { width: '100%', height: '200px', objectFit: 'contain', backgroundColor: '#f0f0f0', borderRadius: '8px', marginBottom: '10px' },
  swordName: { fontSize: '20px', fontWeight: 'bold', margin: '5px 0' },
  statsBox: { display: 'flex', justifyContent: 'center', gap: '10px', fontSize: '14px', fontWeight: 'bold', color: '#555', backgroundColor: '#f9f9f9', padding: '5px 10px', borderRadius: '5px', width: '100%' },
  vsText: { fontSize: '36px', fontWeight: '900', fontStyle: 'italic', color: '#ff9800', textShadow: '2px 2px 0px #000' }
};