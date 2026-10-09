import { useEffect, useRef, useState } from 'react';

// 撮影用カメラ。active の間だけ映像を videoRef に流す。
// カメラが複数ある端末では切り替え、ズームできるカメラではズームを操作できる（無い端末では null / false を返す）。
export function useCamera(active, videoRef) {
  // 使うカメラ。null のあいだは端末任せ（スマホなら自撮り側を優先）
  const [deviceId, setDeviceId] = useState(null);
  const [devices, setDevices] = useState([]);
  // 今映しているカメラ：{ deviceId, label, mirrored }
  const [current, setCurrent] = useState(null);
  // ズームできるカメラのときだけ { min, max, step, value }
  const [zoom, setZoom] = useState(null);
  // 映像の大きさ（ピクセル）。届くまでは null
  const [size, setSize] = useState(null);
  const trackRef = useRef(null);

  useEffect(() => {
    if (!active) return;
    let cancelled = false;
    let stream = null;
    const video = videoRef.current;

    // 映像の大きさは video 要素から取る。端末を回したときも resize で追従できる
    const syncAspect = () => {
      if (video?.videoWidth && video.videoHeight) setSize({ width: video.videoWidth, height: video.videoHeight });
    };
    video?.addEventListener('loadedmetadata', syncAspect);
    video?.addEventListener('resize', syncAspect);

    // zoom: true はズーム操作の許可を求める指定（PCのズーム対応カメラ用。非対応の環境では無視される）
    const constraints = deviceId ? { deviceId: { exact: deviceId }, zoom: true } : { facingMode: 'user', zoom: true };
    navigator.mediaDevices.getUserMedia({ video: constraints })
      .then(async (s) => {
        if (cancelled) { s.getTracks().forEach(track => track.stop()); return; }
        stream = s;
        const track = s.getVideoTracks()[0];
        trackRef.current = track;
        if (video) video.srcObject = s;

        const settings = track.getSettings();
        const range = track.getCapabilities?.().zoom;
        // 自撮り側は鏡のように左右を反転して見せる。外側カメラはそのまま
        setCurrent({ deviceId: settings.deviceId, label: track.label, mirrored: settings.facingMode !== 'environment' });
        setZoom(range && range.max > range.min
          ? { min: range.min, max: range.max, step: range.step || 0.1, value: settings.zoom ?? range.min }
          : null);

        // カメラの一覧は、使用を許可された後でないと名前が取れない
        const all = await navigator.mediaDevices.enumerateDevices();
        if (!cancelled) setDevices(all.filter(d => d.kind === 'videoinput'));
      })
      .catch((err) => {
        console.error("カメラエラー:", err);
        // 選んだカメラが使えなければ（抜かれた等）、端末任せに戻す
        if (!cancelled && deviceId) setDeviceId(null);
      });

    return () => {
      cancelled = true;
      video?.removeEventListener('loadedmetadata', syncAspect);
      video?.removeEventListener('resize', syncAspect);
      if (stream) stream.getTracks().forEach(track => track.stop());
      trackRef.current = null;
    };
  }, [active, deviceId, videoRef]);

  const switchCamera = () => {
    if (devices.length < 2) return;
    const index = devices.findIndex(d => d.deviceId === current?.deviceId);
    setDeviceId(devices[(index + 1) % devices.length].deviceId);
  };

  const changeZoom = (value) => {
    const track = trackRef.current;
    if (!track || !zoom) return;
    setZoom({ ...zoom, value });
    track.applyConstraints({ advanced: [{ zoom: value }] }).catch(err => console.error("ズームエラー:", err));
  };

  return {
    // 映像の縦横比（幅 ÷ 高さ）。スマホを縦に持つと 1 より小さくなる
    aspect: size ? size.width / size.height : 4 / 3,
    mirrored: current?.mirrored ?? true,
    label: current?.label ?? '',
    canSwitch: devices.length > 1,
    switchCamera,
    zoom,
    changeZoom,
  };
}
