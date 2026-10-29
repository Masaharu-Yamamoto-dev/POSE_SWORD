#!/usr/bin/env node
// 黒バラフォントの「絞った版」（public/kurobara-gothic-heavy-ui.woff2）に入れる文字を集める。
//
// 最初に絞った版（約60KB）を読み込んで画面の文字をすぐ黒バラフォントで出し、
// 裏で全部の版（1.0MB）を読み込んで差し替える（src/index.css の @font-face を参照）。
// 字形は同じなので、差し替わっても見た目は変わらない。
//
// 集めるのは src の画面に出る文字だけ。コメントの文字まで入れると4倍に膨らむので、
// JS/JSX は ESLint と同じ構文解析器（espree）でコメント以外の部分だけを読む。
// プレイヤーが入力する名前のために、かな・英数字・全角英数字・和文の記号は丸ごと入れておく。
//
// 使い方: npm run font:subset（この一覧を書き出し、続けて subset_font.py が絞った版を作る）
// tests/font-subset.test.js が、画面の文字がこの一覧に全部入っているかを確かめる。
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import * as espree from 'espree';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
export const CHARS_FILE = join(ROOT, 'tools', 'fonts', 'kurobara-ui-chars.txt');

const range = (from, to) => Array.from({ length: to - from + 1 }, (_, i) => String.fromCodePoint(from + i));
// 入力される名前にも備えて、文字の種類ごと丸ごと入れる分
export const BASE_CHARS = [
  ...range(0x20, 0x7e),     // 英数字・記号
  ...range(0x3000, 0x303f), // 和文の記号（、。「」など）
  ...range(0x3041, 0x309f), // ひらがな
  ...range(0x30a0, 0x30ff), // カタカナ
  ...range(0xff01, 0xff5e), // 全角英数字・記号
];

function walk(dir, exts) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return walk(path, exts);
    return exts.some(ext => entry.name.endsWith(ext)) ? [path] : [];
  });
}

// コメントは字句に含まれないので、字句の文字だけを拾えばコメントが除ける
function codeText(source) {
  const tokens = espree.tokenize(source, { ecmaVersion: 'latest', sourceType: 'module', ecmaFeatures: { jsx: true } });
  return tokens.map(token => token.value).join('');
}

// 画面に出る（かもしれない）ASCII以外の文字。かな等の BASE_CHARS は含めない
export function collectUiChars() {
  const src = join(ROOT, 'src');
  let text = '';
  for (const file of walk(src, ['.js', '.jsx'])) text += codeText(readFileSync(file, 'utf8'));
  // CSS は content: などの文字だけ。コメントは /* */ のみ
  for (const file of walk(src, ['.css'])) text += readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
  return new Set([...text].filter(c => c.codePointAt(0) > 0x7e));
}

export function allChars() {
  return [...new Set([...BASE_CHARS, ...collectUiChars()])].sort((a, b) => a.codePointAt(0) - b.codePointAt(0)).join('');
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const chars = allChars();
  writeFileSync(CHARS_FILE, chars + '\n');
  console.log(`${[...chars].length} 文字を ${CHARS_FILE} に書き出しました`);
}
