export const styles = {
  container: { padding: '30px', display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%', height: '100%', fontFamily: 'Kurobara, serif', boxSizing: 'border-box', overflowX: 'hidden' },
  contentWrapper: { zIndex: 1, position: 'relative', display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' },
  
  bgImageCenter: { position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)', height: '100vh', opacity: 0.15, pointerEvents: 'none', zIndex: 0 },

  button: { padding: '10px 20px', fontSize: '18px', cursor: 'pointer', borderRadius: '5px', fontWeight: 'bold', border: 'none', boxShadow: '0 2px 4px rgba(0,0,0,0.2)' ,fontFamily: 'Kurobara, serif'},
  input: { padding: '10px', fontSize: '20px', width: '100%', maxWidth: '250px', textAlign: 'center', borderRadius: '5px', border: '2px solid #ccc', boxSizing: 'border-box' },
  connectedBox: { marginTop: '10px', padding: '20px', backgroundColor: '#ffffff', borderRadius: '8px', width: '100%', maxWidth: '600px', boxShadow: '0 4px 6px rgba(0,0,0,0.1)' },
  modeBox: { marginBottom: '20px', padding: '15px', backgroundColor: '#f0f8ff', borderRadius: '8px', border: '1px solid #cce7ff' },
  video: { width: '100%', maxWidth: '400px', height: 'auto', borderRadius: '8px', backgroundColor: '#000', display: 'block', transform: 'scaleX(-1)' },
  unityContainer: { width: '100%', maxWidth: '100vw', aspectRatio: '16 / 9', backgroundColor: '#222', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '4px solid #555', boxSizing: 'border-box' },
  readyBox: (isReady) => ({ padding: '10px 20px', border: `2px solid ${isReady ? '#4CAF50' : '#9e9e9e'}`, backgroundColor: isReady ? '#e8f5e9' : '#f5f5f5', borderRadius: '8px', fontWeight: 'bold', minWidth: '100px' }),
  errorMessage: { padding: '15px 25px', backgroundColor: '#ffdddd', color: '#cc0000', borderRadius: '8px', fontWeight: 'bold', border: '1px solid #cc0000' }, // marginを削除しインラインで制御
  previewContainer: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '20px', margin: '20px 0', width: '100%', maxWidth: '800px' },
  swordCard: { width: '100%', backgroundColor: '#fff', borderRadius: '12px', padding: '10px', boxShadow: '0 4px 10px rgba(0,0,0,0.15)', display: 'flex', flexDirection: 'column', alignItems: 'center', border: '2px solid #e0e0e0', boxSizing: 'border-box' },
  previewImage: { width: '100%', maxHeight: '200px', objectFit: 'contain', backgroundColor: '#f0f0f0', borderRadius: '8px', marginBottom: '10px' },
  swordName: { fontSize: 'clamp(14px, 3.5vw, 20px)', fontWeight: 'bold', margin: '5px 0' },
  statsBox: { display: 'flex', justifyContent: 'center', gap: '5px', fontSize: 'clamp(10px, 2.5vw, 14px)', fontWeight: 'bold', color: '#555', backgroundColor: '#f9f9f9', padding: '5px', borderRadius: '5px', width: '100%', boxSizing: 'border-box', flexWrap: 'wrap' },
  vsText: { fontSize: 'clamp(16px, 5vw, 36px)', fontWeight: '900', fontStyle: 'italic', color: '#ff9800', textShadow: '2px 2px 0px #000' },
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
  loadingOverlay: {
    position: 'absolute',
    top: 0, left: 0, width: '100%', height: '100%',
    backgroundColor: 'rgba(0, 0, 0, 0.9)',
    display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
    zIndex: 100
  },
  loadingSpinner: {
    width: '50px', height: '50px',
    border: '5px solid rgba(255,255,255,0.3)',
    borderTop: '5px solid orange',
    borderRadius: '50%',
    animation: 'spin 1s linear infinite'
  }
};