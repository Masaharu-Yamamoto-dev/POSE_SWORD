// src/screens/TitleScreen.jsx
import { styles } from '../styles';
import InkButton from '../components/InkButton.jsx';

export default function TitleScreen({
  direction = "forward", // 🌟 追加：遷移方向を受け取る
  mySwordData,
  titleMode,          
  targetId,           
  setTargetId,        
  systemMessage,
  goToCrafting,
  handleCreateRoom,
  handleJoinRoom,
  handleCancelJoin,   
  connectToHost,
  connecting,
  openRandomMatch,
  startRandomMatch,
  matchMode,
  setMatchMode,
  onOpenHowToPlay
}) {
  // 🌟 追加：アニメーションクラスの判定
  const animClass = direction === "back" ? "page-enter-back" : "page-enter-forward";

  return (
    // 中身はウィンドウの上下中央に置く（「遊び方」は画面の右上に固定なので動かない）
    <div className={animClass} style={{ ...styles.container, justifyContent: 'center', minHeight: '100svh' }}>
      <button className="howto-open-btn" onClick={onOpenHowToPlay}>遊び方</button>

      {mySwordData?.imageSrc && (
        <img src={mySwordData.imageSrc} alt="Background Sword" style={{ ...styles.bgImageCenter, transform: 'translate(-50%, -50%)', opacity: 0.22 }} />
      )}
      
      <div style={styles.contentWrapper}>
        {/* ロゴはウィンドウの高さに合わせて先に縮める（下のボタンをできるだけ元の大きさで残す）。
            480px はロゴ以外（ボタン・余白）が使う高さ */}
        <img src="/logo.png" alt="オレブレード" style={{ width: '90%', maxWidth: '800px', maxHeight: 'max(120px, calc(100svh - 480px))', marginBottom: '40px', objectFit: 'contain' }} />
        
        {titleMode === "DEFAULT" ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', width: '100%', maxWidth: '300px' }}>
            <InkButton variant="shu" onClick={() => goToCrafting("TITLE")}>{mySwordData ? "武器庫を開く" : "剣を錬成する"}</InkButton>
            
            <div className="brush-rule" style={{ margin: '10px 0' }}></div>
            
            <p style={{ color: 'var(--usuzumi)', fontSize: '14px', margin: '0 0 -10px 0' }}>
              {mySwordData ? "2〜4人で対戦できます" : "対戦するには、先に剣を錬成してください"}
            </p>

            <InkButton onClick={openRandomMatch} disabled={!mySwordData || connecting}>ランダムマッチ</InkButton>

            <InkButton onClick={handleCreateRoom} disabled={!mySwordData || connecting}>ロビーを作成</InkButton>

            <InkButton onClick={handleJoinRoom} disabled={!mySwordData || connecting}>ロビーに入る</InkButton>
          </div>
        ) : titleMode === "MATCH_SIZE" ? (
          <div className="sumi-panel sumi-frame" style={{ width: '100%', maxWidth: '400px', padding: '25px', margin: '20px 0' }}>
            <p style={{ fontSize: '18px', color: 'var(--sumi)', margin: '0 0 5px 0' }}>
              何人で戦いますか
            </p>
            <p style={{ fontSize: '13px', color: 'var(--usuzumi)', margin: '0 0 20px 0' }}>
              見知らぬ相手と自動で合流します。集まりしだい開始します。
            </p>

            <div style={{ display: 'flex', gap: '10px', marginBottom: '10px' }}>
              {[["0", "剣"], ["1", "独楽"]].map(([value, label]) => (
                <button
                  key={value}
                  className={`sumi-btn ${matchMode === value ? 'sumi-btn--on' : ''}`}
                  onClick={() => setMatchMode(value)}
                  style={{ flex: 1 }}
                >
                  {label}
                </button>
              ))}
            </div>
            <p style={{ fontSize: '12px', color: 'var(--usuzumi)', margin: '0 0 20px 0' }}>
              相手の部屋に入ったときは、その部屋のルールになります
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '15px' }}>
              {[2, 4].map(size => (
                <InkButton key={size} onClick={() => startRandomMatch(size)}>{size}人で戦う</InkButton>
              ))}
            </div>

            <div style={{ marginTop: '25px', display: 'flex', justifyContent: 'center' }}>
              <InkButton variant="usuzumi" style={{ width: '200px' }} onClick={handleCancelJoin}>戻る</InkButton>
            </div>
          </div>
        ) : (
          <div className="sumi-panel sumi-frame" style={{ width: '100%', maxWidth: '400px', padding: '25px', margin: '20px 0' }}>
            <p style={{ fontSize: '18px', color: 'var(--sumi)', margin: '0 0 20px 0' }}>
              ロビーID（6桁の数字）を入力
            </p>
            
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '10px' }}>
              <input 
                type="text" 
                value={targetId} 
                onChange={(e) => setTargetId(e.target.value)} 
                placeholder="例: 123456" 
                maxLength={6}
                className="sumi-input"
                style={{ letterSpacing: '4px', width: '180px' }}
              />
              <button
                className="sumi-btn"
                onClick={connectToHost}
                disabled={connecting}
              >
                接続
              </button>
            </div>

            <div style={{ marginTop: '30px', width: '100%', display: 'flex', justifyContent: 'center' }}>
              <InkButton variant="usuzumi" style={{ width: '200px' }} onClick={handleCancelJoin}>戻る</InkButton>
            </div>
          </div>
        )}

        {/* メッセージがあるときだけ場所を取る（空のまま高さを確保すると、画面に収まって見えるのに縦スクロールが出る） */}
        {systemMessage && (
          <div style={{ minHeight: '50px', display: 'flex', alignItems: 'center', justifyContent: 'center', marginTop: '30px', width: '90%' }}>
            <div style={{ ...styles.errorMessage, margin: '0', width: '100%', fontSize: 'clamp(12px, 3.5vw, 16px)' }}>
              {systemMessage}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}