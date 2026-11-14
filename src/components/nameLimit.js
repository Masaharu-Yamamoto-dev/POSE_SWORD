// 文字数に上限のある名前の入力欄（名前の入力・錬成完了と武器庫の名前の書き換え）。
// 上限を超える入力は、新しく入れた文字のほうを入る分だけにする（入っている文字は消さない）。
// 先頭から上限で切ると、途中に1文字入れたときに末尾が消えてしまうため。
// 文字数は見た目の1文字ずつ（Array.from。絵文字などの2単位の文字も1文字）で数える。
import { useRef } from 'react';

// 前の値 prev から次の値 next に変わったとき、上限 max に収めた値と、そのあとのカーソルの位置を返す。
// 前後で変わらなかった部分を除いた真ん中を「新しく入れた文字」とみなし、その頭から入る分だけ残す。
// caret は文字列の位置（input の selectionStart と同じ単位）。収める必要がなかったときは null
export function fitInsert(prev, next, max) {
  const a = Array.from(prev);
  const b = Array.from(next);
  if (b.length <= max) return { text: next, caret: null };
  let head = 0;
  while (head < a.length && head < b.length && a[head] === b[head]) head++;
  let tail = 0;
  while (tail < a.length - head && tail < b.length - head && a[a.length - 1 - tail] === b[b.length - 1 - tail]) tail++;
  const room = Math.max(0, max - head - tail);
  const before = [...b.slice(0, head), ...b.slice(head, b.length - tail).slice(0, room)];
  // 前の値がすでに上限を超えていた（変換の途中など）ときも、上限は必ず守る
  const text = [...before, ...b.slice(b.length - tail)].slice(0, max).join('');
  return { text, caret: Math.min(before.join('').length, text.length) };
}

// input に渡す onChange・onCompositionStart・onCompositionEnd を返す。
// 日本語の変換中は切らずにそのまま見せ、確定したときに上限に収める（変換中に切ると変換が崩れるため）
export function useMaxCharsInput(value, setValue, max) {
  const base = useRef(null); // 変換が始まる前の値（変換中だけ入っている）
  const apply = (el, prev) => {
    const { text, caret } = fitInsert(prev, el.value, max);
    setValue(text);
    // 値を差し替えるとカーソルが末尾へ飛ぶので、入れた所の後ろへ戻す
    // （React が入力欄に値を書き戻した後に動かす。値が変わらず描き直しがないときも書き戻しはあるので、effect ではなくタイマー）
    if (caret != null) setTimeout(() => { if (document.activeElement === el) el.setSelectionRange(caret, caret); });
  };
  return {
    onCompositionStart: () => { base.current = value; },
    onCompositionEnd: e => {
      const prev = base.current ?? value;
      base.current = null;
      apply(e.target, prev);
    },
    onChange: e => {
      if (base.current != null || e.nativeEvent.isComposing) setValue(e.target.value);
      else apply(e.target, value);
    },
  };
}
