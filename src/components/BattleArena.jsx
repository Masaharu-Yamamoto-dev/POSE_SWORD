import { useEffect } from 'react';
import { Unity, useUnityContext } from 'react-unity-webgl';
import { PLAYER_COLORS } from '../styles';
import './BattleArena.css';

// 2〜4人共通の対戦画面。人数分のHUDを並べる。
export default function BattleArena({ bridge, view, onLoadFailed }) {
  const { unityProvider, sendMessage, isLoaded, initialisationError, loadingProgression } = useUnityContext({
    loaderUrl: '/multiplayer/Build/multiplayer.loader.js',
    dataUrl: '/multiplayer/Build/multiplayer.data',
    frameworkUrl: '/multiplayer/Build/multiplayer.framework.js',
    codeUrl: '/multiplayer/Build/multiplayer.wasm',
  });
  const room = view?.room;
  const matchId = room?.matchId;

  useEffect(() => {
    bridge.setSender(isLoaded ? sendMessage : null);
    return () => bridge.setSender(null);
  }, [bridge, isLoaded, sendMessage]);

  useEffect(() => {
    if (initialisationError) onLoadFailed(matchId);
  }, [initialisationError, matchId, onLoadFailed]);

  const myState = view?.sync?.players?.find(p => p.playerId === view.localPlayerId);
  const waiting = room?.players.filter(p => !p.loaded) ?? [];

  return (
    <section className="mp-arena">
      <Unity unityProvider={unityProvider} style={{ width: '100%', height: '100%' }} tabIndex={0} />
      {!isLoaded && <div className="mp-loading">ゲームを読み込み中… {Math.round(loadingProgression * 100)}%</div>}
      <div className="mp-hud">{room?.players.map(player => {
        const state = view?.sync?.players?.find(s => s.playerId === player.playerId);
        return (
          <div key={player.playerId}
            className={player.playerId === view.localPlayerId ? 'mp-self' : ''}
            style={{ borderColor: PLAYER_COLORS[player.slotIndex] }}>
            <strong>P{player.slotIndex + 1} {player.swordData.name}{myState?.targetPlayerId === player.playerId ? ' ← 狙い' : ''}</strong>
            <progress max={player.swordData.hp} value={state?.hp ?? player.swordData.hp} />
            <span>HP {state?.hp ?? player.swordData.hp} / SP {Math.floor(state?.sp ?? 0)}{state?.hp === 0 ? ' — 脱落' : ''}</span>
          </div>
        );
      })}</div>
      {room?.phase === 'LOADING' && isLoaded && (
        <p className="mp-loading">読み込み待ち：{waiting.map(p => `P${p.slotIndex + 1} ${p.swordData.name}`).join('・')}</p>
      )}
      {room?.phase === 'COUNTDOWN' && <p className="mp-status">{Math.ceil(view?.sync?.countdownRemaining ?? 3)}</p>}
      {room?.phase === 'PLAYING' && myState?.hp === 0 && <p className="mp-status">観戦中</p>}
    </section>
  );
}
