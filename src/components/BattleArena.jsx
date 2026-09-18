import { useEffect } from 'react';
import { Unity, useUnityContext } from 'react-unity-webgl';
import './BattleArena.css';

export default function BattleArena({ bridge, view, onLoadFailed }) {
  const { unityProvider, sendMessage, isLoaded, initialisationError, loadingProgression } = useUnityContext({
    // ビルドはバージョン付きのフォルダに置く。差し替えるときはここだけ変える。
    // 古いビルドを残しておけば、問題があれば戻せる。
    // ※ main と 1vs3 をマージした直後は、このビルドにマージ後のコードが入っていない。
    //    Unity で再ビルドして、新しいバージョンのパスへ向け直すこと。
    loaderUrl: '/POSE_SWORD_Unity/Builds/ver3.11/Build/ver3.11.loader.js',
    dataUrl: '/POSE_SWORD_Unity/Builds/ver3.11/Build/ver3.11.data',
    frameworkUrl: '/POSE_SWORD_Unity/Builds/ver3.11/Build/ver3.11.framework.js',
    codeUrl: '/POSE_SWORD_Unity/Builds/ver3.11/Build/ver3.11.wasm',
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


  return (
    <section className="mp-arena">
      <Unity unityProvider={unityProvider} style={{ width: '100%', height: '100%' }} tabIndex={0} />
      {/* 対戦中の表示はUnity側が持つ。ここはゲーム本体の取得中だけ（Unityがまだ動いていないため）。 */}
      {!isLoaded && <div className="mp-loading">ゲームを読み込み中… {Math.round(loadingProgression * 100)}%</div>}
    </section>
  );
}