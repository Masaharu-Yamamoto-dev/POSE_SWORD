import { useCallback, useEffect, useRef, useState } from 'react';
import { Peer } from 'peerjs';
import { RoomSession } from './RoomSession.js';
import { PROTOCOL_VERSION } from './HostRoom.js';
import { UnityBattleBridge } from './UnityBattleBridge.js';

const CONNECT_TIMEOUT = 15000;
const ID_RETRIES = 5;
const newRoomId = () => String(Math.floor(100000 + Math.random() * 900000));

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

    const attempt = retriesLeft => {
      const id = isHost ? newRoomId() : targetId;
      if (isHost) setRoomId(id);
      const peer = isHost ? new Peer(id, peerOptions) : new Peer(peerOptions);
      peerRef.current = peer;
      timeoutRef.current = setTimeout(() => {
        if (peerRef.current !== peer || sessionRef.current !== session) return;
        if (!peer.open || (!isHost && !session.view().room)) {
          setError('接続が時間内に完了しませんでした。');
          teardown(false); setConnecting(false);
        }
      }, CONNECT_TIMEOUT);
      if (isHost) peer.on('connection', conn => session.attach(conn));
      peer.on('open', () => {
        if (peerRef.current !== peer) return;
        if (isHost) { clearTimeout(timeoutRef.current); setView(session.view()); setConnecting(false); }
        else session.attach(peer.connect(targetId, { reliable: true, serialization: 'binary', metadata: { protocolVersion: PROTOCOL_VERSION } }));
      });
      peer.on('error', err => {
        if (peerRef.current !== peer) return;
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
    updateSword: useCallback(sword => act(s => s.updateSword(sword)), [act]),
    start: useCallback(() => act(s => s.prepare()), [act]),
    returnToLobby: useCallback(() => act(s => s.returnToLobby()), [act]),
    reportLoadFailure: useCallback(matchId => sessionRef.current?.unityEvent('LOAD_FAILED', { matchId }), []),
  };
}
