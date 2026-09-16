import { useEffect } from 'react';
import { Unity, useUnityContext } from 'react-unity-webgl';
import './BattleArena.css';

// 2〜4人共通の対戦画面。プレイ中はUnity側の描画のみを表示する。
export default function BattleArena({ bridge, view, onLoadFailed }) {
  const { unityProvider, sendMessage, isLoaded, initialisationError, loadingProgression } = useUnityContext({
    // ビルドはバージョン付きのフォルダに置く。差し替えるときはここだけ変える。
    // 古いビルドを残しておけば、問題があれば戻せる。
    loaderUrl: '/multiplayer/ver3.2/Build/ver3.2.loader.js',
    dataUrl: '/multiplayer/ver3.2/Build/ver3.2.data',
    frameworkUrl: '/multiplayer/ver3.2/Build/ver3.2.framework.js',
    codeUrl: '/multiplayer/ver3.2/Build/ver3.2.wasm',
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


  return (
    <section className="mp-arena">
      <Unity unityProvider={unityProvider} style={{ width: '100%', height: '100%' }} tabIndex={0} />
      {/* 対戦中の表示はUnity側が持つ。ここはゲーム本体の取得中だけ（Unityがまだ動いていないため）。 */}
      {!isLoaded && <div className="mp-loading">ゲームを読み込み中… {Math.round(loadingProgression * 100)}%</div>}
    </section>
  );
}
