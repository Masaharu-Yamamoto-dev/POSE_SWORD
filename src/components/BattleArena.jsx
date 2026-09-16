import { useEffect } from 'react';
import { Unity, useUnityContext } from 'react-unity-webgl';
import './BattleArena.css';

// 2〜4人共通の対戦画面。プレイ中はUnity側の描画のみを表示する。
export default function BattleArena({ bridge, view, onLoadFailed }) {
  const { unityProvider, sendMessage, isLoaded, initialisationError, loadingProgression } = useUnityContext({
    loaderUrl: '/POSE_SWORD_Unity/Builds/ver3.1/Build/ver3.1.loader.js',
    dataUrl: '/POSE_SWORD_Unity/Builds/ver3.1/Build/ver3.1.data',
    frameworkUrl: '/POSE_SWORD_Unity/Builds/ver3.1/Build/ver3.1.framework.js',
    codeUrl: '/POSE_SWORD_Unity/Builds/ver3.1/Build/ver3.1.wasm',
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

  const waiting = room?.players.filter(p => !p.loaded) ?? [];

  return (
    <section className="mp-arena">
      <Unity unityProvider={unityProvider} style={{ width: '100%', height: '100%' }} tabIndex={0} />
      {!isLoaded && <div className="mp-loading">ゲームを読み込み中… {Math.round(loadingProgression * 100)}%</div>}
      {room?.phase === 'LOADING' && isLoaded && (
        <p className="mp-loading">読み込み待ち：{waiting.map(p => `P${p.slotIndex + 1} ${p.swordData.name}`).join('・')}</p>
      )}
      {room?.phase === 'COUNTDOWN' && <p className="mp-status">{Math.ceil(view?.sync?.countdownRemaining ?? 3)}</p>}
    </section>
  );
}
