import './InkButton.css';

// 墨の筆跡が走るボタン。
// variant: 'sumi'（標準） / 'shu'（その画面の主役） / 'usuzumi'（戻る・やめるなど控えめな操作）
// fit: 横に並べる・パネルの中に置くときに付ける（墨をボタンの幅に合わせる）
// lit: 押せる状態のあいだ、墨を出したままにして脈打たせる（全員の準備がそろった対戦開始など）
// style は外側の枠に当たる（幅や flex の指定用）。それ以外の props は button にそのまま渡す。
export default function InkButton({ variant = 'sumi', fit = false, lit = false, disabled = false, style, children, ...buttonProps }) {
  return (
    <div className={`ink-btn ink-btn--${variant}${fit ? ' ink-btn--fit' : ''}${lit && !disabled ? ' ink-btn--lit' : ''}${disabled ? ' is-disabled' : ''}`} style={style}>
      <span className="ink-btn__stroke" aria-hidden="true" />
      <button className="ink-btn__label" disabled={disabled} {...buttonProps}>
        {children}
      </button>
    </div>
  );
}
