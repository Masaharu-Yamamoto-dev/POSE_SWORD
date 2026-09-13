import { useCallback, useEffect, useRef, useState } from 'react';
import { MatchSeeker } from './matchSeeker.js';
import { createMatchClient } from './matchmaking.js';

const TICK_INTERVAL = 500;
const blank = () => ({ roomId: null, players: 0, gameMode: '0', phase: 'LOBBY', inRoom: false, error: null });

// MatchSeeker（探索の段取り）と useRoom（実際のP2P接続）をつなぐ層。
// 段取り自体は素のクラス側にあり、ここは最新の部屋の状態を渡すだけにする。
export function useRandomMatch({ room, sword, client = null }) {
  const [view, setView] = useState(null);
  const [matchClient] = useState(() => client ?? createMatchClient());
  const seekerRef = useRef(null);
  const roomRef = useRef(room);
  const swordRef = useRef(sword);
  const modeRef = useRef('0');
  const stateRef = useRef(blank());

  useEffect(() => { roomRef.current = room; swordRef.current = sword; });

  // 部屋の状態は毎レンダーで最新にする。ホストのロビーIDは部屋が立ってから公開する。
  useEffect(() => {
    const current = room.view;
    stateRef.current = {
      roomId: current?.room ? room.roomId : null,
      players: current?.room?.players.length ?? 0,
      gameMode: current?.room?.gameMode ?? '0',
      phase: current?.room?.phase ?? 'LOBBY',
      inRoom: Boolean(current?.room) && !current.closed,
      error: room.error || null,
    };
  });

  const [adapter] = useState(() => ({
    createRoom(targetSize) {
      stateRef.current = blank();
      roomRef.current.createRoom(swordRef.current,
        { seatLimit: targetSize, autoStart: true, gameMode: modeRef.current });
    },
    joinRoom(roomId) {
      stateRef.current = blank();
      roomRef.current.joinRoom(roomId, swordRef.current);
    },
    leave() {
      stateRef.current = blank();
      roomRef.current.leave();
    },
    state() { return stateRef.current; },
  }));

  // 自分がホストになったときのルール。他人の部屋に入った場合はその部屋のルールに従う。
  const start = useCallback((targetSize, gameMode = '0') => {
    modeRef.current = gameMode;
    seekerRef.current?.cancel();
    const seeker = new MatchSeeker({ client: matchClient, room: adapter, targetSize, onChange: setView });
    seekerRef.current = seeker;
    seeker.start();
  }, [adapter, matchClient]);

  const cancel = useCallback(() => {
    const seeker = seekerRef.current;
    seekerRef.current = null;
    setView(null);
    seeker?.cancel();
  }, []);

  useEffect(() => {
    const interval = setInterval(() => { seekerRef.current?.tick(); }, TICK_INTERVAL);
    return () => { clearInterval(interval); seekerRef.current?.cancel(); };
  }, []);

  return { view, start, cancel };
}
