// P1〜P4の色。Unityの頭上マーカーと対応させる。
export const PLAYER_COLORS = ['#2469df', '#d33143', '#248843', '#9a52c5'];

export const swordImageSource = sword =>
  sword?.imageSrc ?? (sword?.imageStr?.startsWith('data:') ? sword.imageStr : `data:image/png;base64,${sword?.imageStr ?? ''}`);

export const styles = {
  container: { padding: '30px', display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%', height: '100%', fontFamily: 'Kurobara, serif', boxSizing: 'border-box', overflowX: 'hidden' },
  contentWrapper: { zIndex: 1, position: 'relative', display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' },
  
  bgImageCenter: { position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)', height: '100vh', opacity: 0.15, pointerEvents: 'none', zIndex: 0 },

  unityContainer: { width: '100%', maxWidth: '100vw', aspectRatio: '16 / 9', backgroundColor: 'var(--sumi)', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '4px solid var(--sumi)', boxSizing: 'border-box' },
  errorMessage: { padding: '8px 12px', color: 'var(--shu)', borderBottom: '2px solid var(--shu)', boxSizing: 'border-box' }, // marginを削除しインラインで制御
  previewImage: { width: '100%', maxHeight: '200px', objectFit: 'contain', backgroundColor: 'var(--washi-deep)', marginBottom: '10px' },
  swordName: { fontSize: 'clamp(14px, 3.5vw, 20px)', margin: '5px 0' },
  countdownOverlay: { 
    position: 'absolute',
    top: '20px',
    left: '50%',
    transform: 'translateX(-50%)',
    fontSize: '80px',
    fontWeight: 'bold',
    color: 'rgba(255, 255, 255, 0.7)',
    textShadow: '0 0 20px red, 2px 2px 0px #000, -2px -2px 0px #000, 2px -2px 0px #000, -2px 2px 0px #000',
    pointerEvents: 'none',
    zIndex: 10
  },
};