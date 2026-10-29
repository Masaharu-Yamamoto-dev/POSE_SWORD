import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { CHARS_FILE, collectUiChars } from '../tools/fonts/ui-chars.mjs';

// 黒バラフォントの絞った版（最初に読み込む方）に、画面で使っている文字が全部入っているか。
// 漏れていると、その文字だけ全部の版が届くまで別のフォントで出てしまう。
test('画面で使っている文字は、黒バラフォントの絞った版に全部入っている', () => {
  const inSubset = new Set(readFileSync(CHARS_FILE, 'utf8'));
  // 黒バラフォント自体に無い文字（絵文字など）は、どちらの版でも別のフォントで出るので対象外
  const missing = [...collectUiChars()].filter(c => !inSubset.has(c) && !/\p{Extended_Pictographic}/u.test(c));
  assert.deepEqual(missing, [], `絞った版に無い文字: ${missing.join('')}\nnpm run font:subset で作り直してください`);
});
