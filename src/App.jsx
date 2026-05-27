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

  // 【追加】システム通知用のState（alertの代わり）
  const [systemMessage, setSystemMessage] = useState("");

  const { unityProvider, sendMessage, isLoaded } = useUnityContext({
    loaderUrl: "/Build/PoseSword.loader.js",
    dataUrl: "/Build/PoseSword.data",
    frameworkUrl: "/Build/PoseSword.framework.js",
    codeUrl: "/Build/PoseSword.wasm",
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
    
    // 【修正】準備状態のリセットは「ゲームオーバー時（リザルト画面突入時）」に行う
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

  // 【修正】ロビーに戻る関数（通知メッセージを受け取れるように）
  const resetToLobby = (msg = "") => {
    // 自分が意図的に切断した場合は、自身のon('close')イベントを無視させる
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
    setSystemMessage(msg); // 画面に表示するメッセージをセット
    setStep("LOBBY");
  };

  const handleLeave = () => {
    if (connRef.current) {
      connRef.current.send({ type: "LEAVE" });
    }
    resetToLobby(""); // 自分から抜けた場合はエラーメッセージを表示しない
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
        // 【修正】単純なREADYではなく、状態を同期するSYNC_STATEに変更
        case "SYNC_STATE": 
          console.log("【受信】相手の準備状態が更新されました:", data.isReady);
          if (data.swordData) setEnemySwordData(data.swordData);
          setIsEnemyReady(data.isReady);
          break;

        case "LEAVE":
          resetToLobby("相手が部屋を退出しました。"); // alertではなく画面メッセージ
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

    // 【修正】通信が切れた時、まだPeerが存在している場合のみエラーを表示
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
      // 自分の準備完了状態を相手に同期
      connection.send({ type: "SYNC_STATE", isReady: true, swordData: mySwordRef.current });
    }
  };

  const captureAndCraft = () => {
    setIsCrafting(true);
    const canvas = canvasRef.current;
    const context = canvas.getContext('2d');
    context.drawImage(videoRef.current, 0, 0, canvas.width, canvas.height);
    const base64Image = canvas.toDataURL('image/jpeg');

    setTimeout(() => {
      setMySwordData({
        name: role === "HOST" ? "ホストブレード" : "クライアントソード",
        hp: 100, attack: 20, weight: 10, imageStr: base64Image
      });
      setIsCrafting(false);
      setStep("MATCHING");
    }, 1500);
  };

  const renderScreen = () => {
    switch (step) {
      case "LOBBY":
        return (
          <div style={styles.container}>
            <h1>POSE SWORD</h1>
            
            {/* 【追加】エラーや退出メッセージをここに表示 */}
            {systemMessage && (
              <div style={styles.errorMessage}>
                ⚠️ {systemMessage}
              </div>
            )}

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
            <p>あなたのID: <strong style={{ color: 'blue' }}>{myPeerId || "取得中..."}</strong></p>
            {role === "HOST" && !connection && <p>このIDをClientに教えてください...</p>}
            {role === "CLIENT" && !connection && (
              <div>
                <input type="text" value={targetId} onChange={(e) => setTargetId(e.target.value)} placeholder="HostのIDを入力" />
                <button style={styles.button} onClick={connectToHost}>接続</button>
              </div>
            )}
            
            {connection && (
              <div style={styles.connectedBox}>
                <h3 style={{ color: 'green' }}>✅ 接続完了！</h3>

                {countdown !== null ? (
                  <div>
                    <h2 style={{ fontSize: '48px', color: 'red' }}>
                      {countdown > 0 ? countdown : "START!"}
                    </h2>
                  </div>
                ) : (
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'center', gap: '20px', margin: '20px 0' }}>
                      <div style={styles.readyBox(isReady)}>
                        自分: {isReady ? "準備OK!" : "準備中..."}
                      </div>
                      <div style={styles.readyBox(isEnemyReady)}>
                        相手: {isEnemyReady ? "準備OK!" : "準備中..."}
                      </div>
                    </div>

                    {!isReady ? (
                      <button style={{ ...styles.button, backgroundColor: 'orange', color: 'white' }} onClick={handleReady}>
                        準備OK（バトルへ）
                      </button>
                    ) : (
                      <p style={{ fontWeight: 'bold' }}>相手の準備を待っています...</p>
                    )}

                    <button style={{ ...styles.button, backgroundColor: 'gray', color: 'white', marginTop: '30px' }} onClick={handleLeave}>
                      退出する
                    </button>
                  </div>
                )}
              </div>
            )}
            {!connection && (
              <button style={{ ...styles.button, backgroundColor: 'gray', color: 'white', marginTop: '30px' }} onClick={() => resetToLobby("")}>
                キャンセル
              </button>
            )}
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
                  window.ReactApp.receiveFromUnity("SYNC", JSON.stringify({
                    hostSword: { hp: 50 }, clientSword: { hp: 100 }
                  }));
                } else {
                  window.ReactApp.receiveFromUnity("INPUT", JSON.stringify({ action: "JUMP" }));
                }
              }}>
                {role === "HOST" ? "【Host】UnityがSYNCを送るフリ" : "【Client】UnityがINPUTを送るフリ"}
              </button>

              <button style={{...styles.button, backgroundColor: '#333', color: '#fff'}} onClick={() => {
                if (role === "HOST") {
                  window.ReactApp.receiveFromUnity("SYNC", JSON.stringify({
                    hostSword: { hp: 0 }, clientSword: { hp: 100 }
                  }));
                }
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
                
                // 【追加】自分が「もう一度遊ぶ」を押して未準備になったことを相手に同期
                if (connRef.current && mySwordRef.current) {
                  connRef.current.send({ type: "SYNC_STATE", isReady: false, swordData: mySwordRef.current });
                }
              }}>
                もう一度遊ぶ（待機画面へ）
              </button>

              <button style={{ ...styles.button, backgroundColor: 'gray', color: 'white' }} onClick={handleLeave}>
                退出する
              </button>
            </div>
          </div>
        );
      default: return <div>Error</div>;
    }
  };

  return <div style={{ fontFamily: 'sans-serif', textAlign: 'center' }}>{renderScreen()}</div>;
}

const styles = {
  container: { padding: '50px', display: 'flex', flexDirection: 'column', alignItems: 'center' },
  button: { padding: '10px 20px', margin: '10px', fontSize: '18px', cursor: 'pointer', borderRadius: '5px' },
  connectedBox: { marginTop: '30px', padding: '20px', backgroundColor: '#f0fff0', borderRadius: '8px' },
  video: { width: '400px', borderRadius: '8px', backgroundColor: '#000', marginBottom: '20px' },
  unityContainer: { width: '800px', height: '450px', backgroundColor: '#222', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '4px solid #555' },
  debugPanel: { marginTop: '20px', padding: '15px', border: '2px dashed gray', borderRadius: '8px', backgroundColor: '#f9f9f9', textAlign: 'center' },
  readyBox: (isReady) => ({
    padding: '10px 20px',
    border: `2px solid ${isReady ? 'green' : 'gray'}`,
    backgroundColor: isReady ? '#e0ffe0' : '#f0f0f0',
    borderRadius: '8px',
    fontWeight: 'bold',
    minWidth: '100px'
  }),
  errorMessage: { // 追加されたエラーメッセージ用のスタイル
    padding: '15px 25px', 
    backgroundColor: '#ffdddd', 
    color: '#cc0000', 
    borderRadius: '8px', 
    marginBottom: '20px', 
    fontWeight: 'bold',
    border: '1px solid #cc0000'
  }
};