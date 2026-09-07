import { useEffect, useRef, useState } from 'react';
import { Peer } from 'peerjs';
import { Unity, useUnityContext } from 'react-unity-webgl';
import { RoomSession } from '../network/RoomSession.js';
import { UnityBattleBridge } from '../network/UnityBattleBridge.js';
import './MultiplayerGame.css';

const colors = ['#2469df', '#d33143', '#248843', '#9a52c5'];
const imageSource = sword => sword?.imageStr?.startsWith('data:') ? sword.imageStr : `data:image/png;base64,${sword?.imageStr ?? ''}`;

function BattleCanvas({ bridge, session }) {
  const { unityProvider, sendMessage, isLoaded, initialisationError, loadingProgression } = useUnityContext({
    loaderUrl: '/multiplayer/Build/multiplayer.loader.js',
    dataUrl: '/multiplayer/Build/multiplayer.data',
    frameworkUrl: '/multiplayer/Build/multiplayer.framework.js',
    codeUrl: '/multiplayer/Build/multiplayer.wasm',
  });
  useEffect(() => {
    bridge.setSender(isLoaded ? sendMessage : null);
    return () => bridge.setSender(null);
  }, [bridge, isLoaded, sendMessage]);
  useEffect(() => {
    if (initialisationError) session.unityEvent('LOAD_FAILED', { matchId: session.view().room?.matchId });
  }, [initialisationError, session]);
  return <>
    <Unity unityProvider={unityProvider} style={{ width: '100%', height: '100%' }} tabIndex={0} />
    {!isLoaded && <div className="mp-loading">ゲームを読み込み中… {Math.round(loadingProgression * 100)}%</div>}
  </>;
}

export default function MultiplayerGame({ sword, peerOptions, onExit }) {
  const [view, setView] = useState(null);
  const [roomId, setRoomId] = useState('');
  const [targetId, setTargetId] = useState('');
  const [error, setError] = useState('');
  const [connecting, setConnecting] = useState(false);
  const [hasCanvas, setHasCanvas] = useState(false);
  const [session, setSession] = useState(null);
  const [bridge] = useState(() => new UnityBattleBridge());
  const sessionRef = useRef(null);
  const peerRef = useRef(null);
  const connectionTimeoutRef = useRef(null);

  useEffect(() => {
    const api = { receiveFromUnity(type, json) {
      try {
        const data = JSON.parse(json);
        if (type === 'MP_READY') { bridge.sceneReady(); return; }
        sessionRef.current?.unityEvent(type.replace(/^MP_/, ''), data);
      } catch { setError('ゲームとの通信を処理できませんでした。'); }
    } };
    window.MultiplayerApp = api;
    const interval = setInterval(() => sessionRef.current?.pump(), 250);
    return () => {
      clearInterval(interval);
      clearTimeout(connectionTimeoutRef.current);
      sessionRef.current?.close(); peerRef.current?.destroy();
      if (window.MultiplayerApp === api) delete window.MultiplayerApp;
    };
  }, [bridge]);

  const connect = isHost => {
    if (!isHost && !/^\d{6}$/.test(targetId)) { setError('6桁のルームIDを入力してください。'); return; }
    setConnecting(true); setError('');
    const id = isHost ? String(Math.floor(100000 + Math.random() * 900000)) : targetId;
    setRoomId(id);
    const peer = isHost ? new Peer(id, peerOptions) : new Peer(peerOptions);
    peerRef.current = peer;
    const session = new RoomSession({ isHost, roomEpoch: isHost ? crypto.randomUUID() : '', sword,
      onChange: state => { setView(state); if (state.room || state.closed) setConnecting(false); },
      onUnity: command => {
        if (command.method === 'InitializeMultiplayer') setHasCanvas(true);
        bridge.dispatch(command);
      },
    });
    sessionRef.current = session;
    setSession(session);
    connectionTimeoutRef.current = setTimeout(() => {
      if (peerRef.current === peer && (!peer.open || !session.view().room)) {
        setError('接続が時間内に完了しませんでした。'); session.close(false); peer.destroy(); setConnecting(false);
      }
    }, 15000);
    if (isHost) peer.on('connection', conn => session.attach(conn));
    peer.on('open', () => {
      if (peerRef.current !== peer) return;
      if (isHost) { clearTimeout(connectionTimeoutRef.current); setView(session.view()); setConnecting(false); }
      else session.attach(peer.connect(targetId, { reliable: true, serialization: 'binary', metadata: { protocolVersion: 2 } }));
    });
    peer.on('error', err => {
      setError(err.type === 'unavailable-id' ? 'このIDは使用中です。もう一度部屋を作成してください。' : '接続できませんでした。ルームIDと通信状態を確認してください。');
      clearTimeout(connectionTimeoutRef.current);
      session.close(false); peer.destroy(); setConnecting(false);
    });
  };

  const leave = () => {
    sessionRef.current?.close(); peerRef.current?.destroy(); onExit();
  };
  const room = view?.room;
  const me = room?.players.find(p => p.playerId === view.localPlayerId);
  const active = room && ['LOADING', 'COUNTDOWN', 'PLAYING'].includes(room.phase) && !view.closed;
  const resultVisible = view?.result && (!me?.inLobby || view.closed);
  const myState = view?.sync?.players?.find(p => p.playerId === view.localPlayerId);

  return <main className="mp-game">
    <header><h1>4人個人戦</h1>{roomId && <span>ルームID：<strong>{roomId}</strong></span>}</header>
    {(error || view?.error) && <p role="alert" className="mp-error">{error || view.error}</p>}
    {!room && !view?.closed && <section className="mp-entry">
      <p>4人がそれぞれの端末で参加し、最後まで残った1人が勝利します。</p>
      <button disabled={connecting} onClick={() => connect(true)}>4人部屋を作成</button>
      <label>ルームID<input inputMode="numeric" maxLength={6} value={targetId} onChange={e => setTargetId(e.target.value)} placeholder="123456" /></label>
      <button disabled={connecting} onClick={() => connect(false)}>部屋に参加</button>
      {connecting && <p role="status">接続中…</p>}
    </section>}
    {room && !active && !resultVisible && !view.closed && <>
      <div className="mp-options">
        <label>モード <select value={room.gameMode} disabled={!view.isHost || room.phase !== 'LOBBY'} onChange={e => session.setGameMode(e.target.value)}>
          <option value="0">剣</option><option value="1">独楽</option>
        </select></label>
        <button onClick={() => navigator.clipboard.writeText(roomId).catch(() => setError('ルームIDを選択してコピーしてください。'))}>IDをコピー</button>
      </div>
      <div className="mp-roster">{Array.from({ length: 4 }, (_, slot) => {
        const player = room.players.find(p => p.slotIndex === slot);
        return <article key={slot} style={{ borderColor: colors[slot] }}>
          <h2>P{slot + 1} {player?.playerId === view.localPlayerId ? 'あなた' : ''}{player?.playerId === 'p0' ? '（ホスト）' : ''}</h2>
          {player ? <><img src={imageSource(player.swordData)} alt={player.swordData.name} />
            <strong>{player.swordData.name}</strong><p>HP {player.swordData.hp} / 攻撃 {player.swordData.attack} / 重さ {player.swordData.weight}</p>
            <p>{!player.connected ? '切断' : !player.inLobby ? '結果を確認中' : player.ready ? '準備完了' : '準備中'}</p></> : <p>参加待ち</p>}
        </article>;
      })}</div>
      <div className="mp-options">
        <button disabled={room.phase !== 'LOBBY'} onClick={() => session.setReady(!me?.ready)}>{me?.ready ? '準備を取り消す' : '準備完了'}</button>
        {view.isHost && <button disabled={!view.canStart} onClick={() => { try { session.prepare(); } catch (e) { setError(e.message); } }}>全員で対戦開始</button>}
      </div>
      <p>4人全員の準備が揃うと、ホストが対戦を開始できます。</p>
    </>}
    {hasCanvas && <section className="mp-arena" style={{ display: active ? 'block' : 'none' }}>
      <BattleCanvas bridge={bridge} session={session} />
      <div className="mp-hud">{room?.players.map(p => {
        const state = view?.sync?.players?.find(s => s.playerId === p.playerId);
        return <div key={p.playerId} className={p.playerId === view.localPlayerId ? 'mp-self' : ''} style={{ borderColor: colors[p.slotIndex] }}>
          <strong>P{p.slotIndex + 1} {p.swordData.name}{myState?.targetPlayerId === p.playerId ? ' ← 狙い' : ''}</strong>
          <progress max={p.swordData.hp} value={state?.hp ?? p.swordData.hp} />
          <span>HP {state?.hp ?? p.swordData.hp} / SP {Math.floor(state?.sp ?? 0)}{state?.hp === 0 ? ' — 脱落' : ''}</span>
        </div>;
      })}</div>
      {room?.phase === 'LOADING' && <p className="mp-status">読み込み待ち：{room.players.filter(p => !p.loaded).map(p => `P${p.slotIndex + 1}`).join('・')}</p>}
      {room?.phase === 'COUNTDOWN' && <p className="mp-status">{Math.ceil(view?.sync?.countdownRemaining ?? 3)}</p>}
      {myState?.hp === 0 && <p className="mp-status">観戦中</p>}
    </section>}
    {resultVisible && <section className="mp-results">
      <h2>{view.result.draw ? '引き分け' : view.result.winnerId === view.localPlayerId ? 'あなたの勝利！' : '試合終了'}</h2>
      <table><thead><tr><th>順位</th><th>プレイヤー</th><th>与ダメージ</th><th>被ダメージ</th><th>撃破</th></tr></thead>
        <tbody>{[...view.result.standings].sort((a, b) => a.rank - b.rank).map(score => <tr key={score.playerId}>
          <td>{score.rank}位</td><td>{room.players.find(p => p.playerId === score.playerId)?.swordData.name ?? score.playerId}{score.eliminationReason === 'DISCONNECTED' ? '（切断）' : ''}</td>
          <td>{score.damageDealt}</td><td>{score.damageTaken}</td><td>{score.kills}</td>
        </tr>)}</tbody></table>
      {!view.closed && <button onClick={() => session.returnToLobby()}>ロビーに戻る</button>}
    </section>}
    <footer>{view?.isHost && active && <p>ホストが退出すると、全員の試合が終了します。</p>}
      <button onClick={leave}>{room ? '退出してタイトルへ' : 'タイトルに戻る'}</button></footer>
  </main>;
}
