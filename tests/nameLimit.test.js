import test from 'node:test';
import assert from 'node:assert/strict';
import { fitInsert } from '../src/components/nameLimit.js';

const FULL = 'あいうえおかきくけこさしすせそ'; // 15文字

test('上限に収まる入力はそのまま通す', () => {
  assert.deepEqual(fitInsert('あいう', 'あいXう', 15), { text: 'あいXう', caret: null });
});

test('上限いっぱいのときに途中へ入れても、末尾は消えず、入れた文字のほうが入らない', () => {
  const next = FULL.slice(0, 3) + 'X' + FULL.slice(3);
  assert.deepEqual(fitInsert(FULL, next, 15), { text: FULL, caret: 3 });
});

test('末尾に足しても入らない', () => {
  assert.deepEqual(fitInsert(FULL, FULL + 'X', 15), { text: FULL, caret: 15 });
});

test('貼り付けで長い文字を入れたときは、入る分だけ入れた所に入れる', () => {
  // 12文字のところへ、3文字目の後ろに5文字貼り付ける → 3文字だけ入る
  const prev = FULL.slice(0, 12);
  const next = prev.slice(0, 3) + '一二三四五' + prev.slice(3);
  assert.deepEqual(fitInsert(prev, next, 15), { text: prev.slice(0, 3) + '一二三' + prev.slice(3), caret: 6 });
});

test('選んだ文字を置き換えるときは、消した分だけ入る', () => {
  // 「かき」を選んで「12345」に置き換える → 2文字分の空きに「12」が入る
  const next = 'あいうえお' + '12345' + FULL.slice(7);
  assert.deepEqual(fitInsert(FULL, next, 15), { text: 'あいうえお12' + FULL.slice(7), caret: 7 });
});

test('絵文字のような2単位の文字も1文字として数え、カーソルは文字列の位置で返す', () => {
  const prev = '😀'.repeat(15);
  const next = '😀X' + '😀'.repeat(14);
  assert.deepEqual(fitInsert(prev, next, 15), { text: prev, caret: 2 });
});

test('前の値がすでに上限を超えていても（変換の途中など）、上限は必ず守る', () => {
  const { text } = fitInsert(FULL + 'かな', FULL + 'カナ', 15);
  assert.equal(Array.from(text).length, 15);
});
