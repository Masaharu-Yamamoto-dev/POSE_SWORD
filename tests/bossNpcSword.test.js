import test from 'node:test';
import assert from 'node:assert/strict';
import { Buffer } from 'node:buffer';
import { BOSS_NPC_SWORD, NPC_PLAYER_ID } from '../src/network/bossNpcSword.js';
import { bossStats, validateSword } from '../src/network/HostRoom.js';

test('the NPC boss sword passes the same validation as a player sword', () => {
  const sword = validateSword(BOSS_NPC_SWORD);
  assert.equal(sword.name, BOSS_NPC_SWORD.name);
  assert.equal(sword.hiltType, '0');
  assert.equal(sword.swords.length, 3, '人間の剣と同じく3本ぶんの枠に揃う');
  assert.equal(sword.swords[0].isEmpty, false);
});

test('the NPC blade image is a raw base64 PNG, like the images players send', () => {
  assert.ok(!BOSS_NPC_SWORD.imageStr.startsWith('data:'), 'Unity は data: の付かない base64 を受け取る');
  const bytes = Buffer.from(BOSS_NPC_SWORD.imageStr, 'base64');
  assert.deepEqual([...bytes.subarray(0, 8)], [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
});

test('the NPC id cannot collide with a human player id', () => {
  assert.doesNotMatch(NPC_PLAYER_ID, /^p\d+$/);
});

test('the NPC boss sword cannot be changed at runtime', () => {
  assert.ok(Object.isFrozen(BOSS_NPC_SWORD));
  assert.deepEqual(bossStats(BOSS_NPC_SWORD), { hp: 1600, attack: 175, weight: 60 });
});
