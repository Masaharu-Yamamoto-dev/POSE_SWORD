import { useEffect } from 'react';
import { Unity, useUnityContext } from 'react-unity-webgl';
import './BattleArena.css';

export default function BattleArena({ bridge, view, onLoadFailed }) {
  const { unityProvider, sendMessage, isLoaded, initialisationError, loadingProgression } = useUnityContext({
    loaderUrl: '/POSE_SWORD_Unity/Builds/ver3.0/Build/ver3.0.loader.js',
    dataUrl: '/POSE_SWORD_Unity/Builds/ver3.0/Build/ver3.0.data',
    frameworkUrl: '/POSE_SWORD_Unity/Builds/ver3.0/Build/ver3.0.framework.js',
    codeUrl: '/POSE_SWORD_Unity/Builds/ver3.0/Build/ver3.0.wasm',
  });
  
  const room = view?.room;
  const matchId = room?.matchId;

  useEffect(() => {
    const interceptedSendMessage = (gameObject, methodName, param) => {
      if (typeof param === 'string' && param.includes('swordData')) {
        try {
          let data = JSON.parse(param);

          console.log("【BattleArena側】Unityに届く直前のデータ:", data);
          
          console.log(`[React -> Unity 送信データ] ${methodName}:`, data);

          const playersArray = Array.isArray(data) ? data : (data.players || [data]);
          playersArray.forEach((p, idx) => {
            const sword = p.swordData || p;
            console.log(`--- プレイヤー ${idx + 1} ---`);
            console.log("  ・名前:", sword.name);
            console.log("  ・柄 (hiltType):", sword.hiltType);
            console.log("  ・3本リスト (swords):", sword.swords);
            console.log("  ・装備インデックス (equippedIndex):", sword.equippedIndex);
          });
          console.groupEnd();

          param = JSON.stringify(data);
        } catch (e) {
          console.error("JSON parse error:", e);
        }
      }
      
      sendMessage(gameObject, methodName, param);
    };

    bridge.setSender(isLoaded ? interceptedSendMessage : null);
    
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