// 入力欄のキー操作を Unity に止めさせない。
//
// 対戦の画面（Unity）は、対戦が終わってロビー・武器庫へ戻っても、部屋にいる間は裏で動き続ける（App.jsx の hasArena）。
// Unity はページ全体のキー入力を受け取り、Backspace・Delete・矢印キーなどはブラウザの既定の動作を止める（preventDefault）。
// そのままだと、一度対戦したあとに名前を書き換えようとしても、文字を消せず、カーソルも動かせない。
//
// 入力欄で打ったキーだけは、そのイベントの preventDefault を何もしないものに差し替えて、既定の動作（文字を消す等）を残す。
// Unity にもキーは届くが、隠れている間は何も起きない。対戦中は入力欄がないので、ゲームの操作には影響しない。
// Unity より先に呼ばれる必要があるので、ページの読み込み時（Unity を読み込む前）に window の捕捉段階で登録する。
const isEditable = el =>
  el instanceof HTMLElement && (el.isContentEditable || el.matches('input, textarea, select'));

const keepDefault = e => {
  if (isEditable(e.target)) e.preventDefault = () => {};
};

export function guardTextInputKeys() {
  for (const type of ['keydown', 'keypress', 'keyup']) window.addEventListener(type, keepDefault, true);
}
