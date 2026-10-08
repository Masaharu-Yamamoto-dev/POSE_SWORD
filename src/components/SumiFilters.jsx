// 墨の部品（sumi.css）が使うSVGフィルタの定義。画面には何も描かない。
// アプリ全体で1回だけ置けば、どの画面からでも filter: url(#sumi-rough) のように使える。
export default function SumiFilters() {
  return (
    <svg width="0" height="0" style={{ position: 'absolute' }} aria-hidden="true">
      {/* 線をわずかに揺らして、筆で引いたように見せる */}
      <filter id="sumi-rough" x="-5%" y="-5%" width="110%" height="110%">
        <feTurbulence type="fractalNoise" baseFrequency="0.035" numOctaves="3" seed="4" result="noise" />
        <feDisplacementMap in="SourceGraphic" in2="noise" scale="3.5" />
      </filter>
      {/* 太い線の縁だけを細かく欠けさせる。線そのものはまっすぐのまま */}
      <filter id="sumi-edge" x="-5%" y="-40%" width="110%" height="180%">
        <feTurbulence type="fractalNoise" baseFrequency="0.09" numOctaves="2" seed="7" result="noise" />
        <feDisplacementMap in="SourceGraphic" in2="noise" scale="3.5" />
      </filter>
      {/* 落款用：縁を欠けさせ、朱肉のムラ（小さな抜け）を付ける */}
      <filter id="sumi-hanko" x="-10%" y="-10%" width="120%" height="120%">
        <feTurbulence type="fractalNoise" baseFrequency="0.6" numOctaves="2" seed="11" result="noise" />
        <feDisplacementMap in="SourceGraphic" in2="noise" scale="1.8" result="chipped" />
        <feTurbulence type="fractalNoise" baseFrequency="1.4" numOctaves="1" seed="3" result="grain" />
        <feColorMatrix in="grain" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 -2 2.2" result="holes" />
        <feComposite in="chipped" in2="holes" operator="in" />
      </filter>
    </svg>
  );
}
