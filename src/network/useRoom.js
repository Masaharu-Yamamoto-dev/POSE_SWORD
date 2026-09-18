import { useCallback, useEffect, useRef, useState } from 'react';
import { Peer } from 'peerjs';
import { RoomSession } from './RoomSession.js';
import { PROTOCOL_VERSION } from './HostRoom.js';
import { UnityBattleBridge } from './UnityBattleBridge.js';

const CONNECT_TIMEOUT = 15000;
const ID_RETRIES = 5;
// シグナリングが切れたときに繋ぎ直す回数。P2Pリンク自体は生きたままなので、
// ここで戻れれば対戦は途切れない。
const RECONNECT_RETRIES = 5;
const newRoomId = () => String(Math.floor(100000 + Math.random() * 900000));

// 通信の切り分け用のログ。端末のコンソールで
//   localStorage.setItem('POSE_SWORD_NET_DEBUG', '1')
// を実行して再読み込みすると、PeerJS の内部ログと接続の節目が出るようになる。
// 実機でも本番ビルドのまま切り替えられるよう、開発フラグではなく保存値で見る。
const netDebug = () => {
  try { return localStorage.getItem('POSE_SWORD_NET_DEBUG') === '1'; } catch { return false; }
};
const log = (...args) => { if (netDebug()) console.log('[net]', ...args); };
const traceConn = conn => {
  if (!netDebug() || !conn) return conn;
  log('conn created', conn.peer);
  conn.on('open', () => log('conn open', conn.peer));
  conn.on('close', () => log('conn close', conn.peer));
  conn.on('error', e => log('conn error', conn.peer, e?.type ?? e?.message));
  conn.on('iceStateChanged', state => log('ice', conn.peer, state));
  return conn;
};

// 2〜4人が同じ部屋に集まるためのPeerJS接続。ホストだけが名簿と試合進行を持つ。
export function useRoom({ peerOptions, onClosed }) {
  const [view, setView] = useState(null);
  const [roomId, setRoomId] = useState('');
  const [error, setError] = useState('');
  const [connecting, setConnecting] = useState(false);
  const [hasArena, setHasArena] = useState(false);
  const [bridge] = useState(() => new UnityBattleBridge());
  const sessionRef = useRef(null);
  const peerRef = useRef(null);
  const timeoutRef = useRef(null);
  const leavingRef = useRef(false);
  const closedRef = useRef(onClosed);

  const teardown = useCallback(notifyPeers => {
    clearTimeout(timeoutRef.current);
    leavingRef.current = true;
    sessionRef.current?.close(notifyPeers);
    sessionRef.current = null;
    peerRef.current?.destroy();
    peerRef.current = null;
    leavingRef.current = false;
  }, []);

  // ホストの退出や強制切断など、こちらから閉じていない終了。
  // 理由は消さずに残す。ランダムマッチの探索は、これを見て次の候補へすぐ移る。
  const dropped = useCallback(message => {
    teardown(false);
    setView(null); setRoomId(''); setConnecting(false); setHasArena(false);
    setError(message ?? '');
    closedRef.current?.(message);
  }, [teardown]);

  useEffect(() => { closedRef.current = onClosed; }, [onClosed]);

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
      teardown(true);
      if (window.MultiplayerApp === api) delete window.MultiplayerApp;
    };
  }, [bridge, teardown]);

  const open = useCallback((isHost, sword, targetId, roomOptions = {}) => {
    teardown(true);
    setError(''); setHasArena(false); setView(null); setConnecting(true);
    let session;
    try {
      session = new RoomSession({ isHost, roomEpoch: isHost ? crypto.randomUUID() : '', sword, ...roomOptions,
        onChange: state => {
          setView(state);
          if (state.room || state.closed) setConnecting(false);
          if (state.closed && !leavingRef.current) dropped(state.error);
        },
        onUnity: command => {
          if (command.method === 'InitializeMultiplayer') setHasArena(true);
          bridge.dispatch(command);
        },
      });
    } catch (e) {
      setError(e.message); setConnecting(false); return;
    }
    sessionRef.current = session;

    const options = { ...peerOptions, debug: netDebug() ? 3 : 0 };
    const attempt = retriesLeft => {
      const id = isHost ? newRoomId() : targetId;
      if (isHost) setRoomId(id);
      const peer = isHost ? new Peer(id, options) : new Peer(options);
      peerRef.current = peer;
      let opened = false;
      let reconnects = 0;
      log(isHost ? 'creating room' : 'joining room', id);
      timeoutRef.current = setTimeout(() => {
        if (peerRef.current !== peer || sessionRef.current !== session) return;
        if (!peer.open || (!isHost && !session.view().room)) {
          log('connect timeout', 'peer.open=' + peer.open, 'room=' + !!session.view().room);
          setError('接続が時間内に完了しませんでした。');
          teardown(false); setConnecting(false);
        }
      }, CONNECT_TIMEOUT);
      if (isHost) peer.on('connection', conn => { log('incoming', conn.peer); session.attach(traceConn(conn)); });
      peer.on('open', () => {
        if (peerRef.current !== peer) return;
        log('peer open', peer.id, isHost ? '(host)' : '(guest)');
        reconnects = 0;
        // 繋ぎ直したときも 'open' は再び飛ぶ。張るのは最初の一度だけにする。
        if (opened) return;
        opened = true;
        if (isHost) { clearTimeout(timeoutRef.current); setView(session.view()); setConnecting(false); return; }
        const conn = peer.connect(targetId, { reliable: true, serialization: 'binary', metadata: { protocolVersion: PROTOCOL_VERSION } });
        // 切断済みの Peer では connect() が undefined を返す。
        if (!conn) { setError('接続できませんでした。通信状態を確認してください。'); teardown(false); setConnecting(false); return; }
        session.attach(traceConn(conn));
      });
      // シグナリングが切れても既存のP2Pリンクは生きている。PeerJS は自動では
      // 戻らないので、ここで繋ぎ直す。放置するとホストのIDが誰からも引けなくなり、
      // 画面上は正常なのに誰も入れない部屋ができる。reconnect() は同じIDを取り直すので
      // ロビーIDは変わらない。
      peer.on('disconnected', () => {
        if (peerRef.current !== peer || peer.destroyed) return;
        if (++reconnects > RECONNECT_RETRIES) {
          log('reconnect gave up');
          setError('サーバーとの接続が切れました。もう一度お試しください。');
          teardown(false); setConnecting(false); return;
        }
        log('signaling disconnected, reconnecting', reconnects);
        try { peer.reconnect(); } catch { /* 直後の error で拾う */ }
      });
      peer.on('error', err => {
        if (peerRef.current !== peer) return;
        log('peer error', err.type, err.message);
        // シグナリングとの一時切断。上の disconnected で繋ぎ直すので部屋は畳まない。
        if (err.type === 'network') return;
        clearTimeout(timeoutRef.current);
        if (isHost && err.type === 'unavailable-id' && retriesLeft > 0) { peer.destroy(); attempt(retriesLeft - 1); return; }
        setError(err.type === 'peer-unavailable' ? 'ロビーが見つかりません。IDを確認してください。'
          : err.type === 'unavailable-id' ? '混雑しています。もう一度ロビーを作成してください。'
          : '接続できませんでした。通信状態を確認してください。');
        teardown(false); setConnecting(false);
      });
    };
    attempt(isHost ? ID_RETRIES : 0);
  }, [bridge, dropped, peerOptions, teardown]);

  const act = useCallback(run => {
    if (!sessionRef.current) return;
    try { run(sessionRef.current); setError(''); } catch (e) { setError(e.message); }
  }, []);

  return {
    view, roomId, error, connecting, hasArena, bridge,
    createRoom: useCallback((sword, roomOptions) => open(true, sword, undefined, roomOptions), [open]),
    joinRoom: useCallback((id, sword) => { setRoomId(id); open(false, sword, id); }, [open]),
    leave: useCallback(() => {
      teardown(true);
      setView(null); setRoomId(''); setError(''); setConnecting(false); setHasArena(false);
    }, [teardown]),
    setReady: useCallback(ready => act(s => s.setReady(ready)), [act]),
    setGameMode: useCallback(mode => act(s => s.setGameMode(mode)), [act]),
    setLivesMode: useCallback(enabled => act(s => s.setLivesMode(enabled)), [act]),
    setSoloMode: useCallback(enabled => act(s => s.setSoloMode(enabled)), [act]),
    setBossPlayer: useCallback(playerId => act(s => s.setBossPlayer(playerId)), [act]),
    updateSword: useCallback(sword => act(s => s.updateSword(sword)), [act]),
    start: useCallback(() => act(s => s.prepare()), [act]),
    returnToLobby: useCallback(() => act(s => s.returnToLobby()), [act]),
    reportLoadFailure: useCallback(matchId => sessionRef.current?.unityEvent('LOAD_FAILED', { matchId }), []),
  };
}
