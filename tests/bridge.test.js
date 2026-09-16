import test from 'node:test';
import assert from 'node:assert/strict';
import { UnityBattleBridge } from '../src/network/UnityBattleBridge.js';

test('initialization waits for both loader and scene, then runs exactly once', () => {
  const calls = [];
  const bridge = new UnityBattleBridge();
  bridge.dispatch({ method: 'InitializeMultiplayer', data: { matchId: 'm1' } });
  bridge.setSender((...args) => calls.push(args));
  assert.equal(calls.length, 0);
  bridge.sceneReady();
  bridge.sceneReady();
  assert.equal(calls.length, 1);
  assert.deepEqual(calls[0], ['GameManager', 'InitializeMultiplayer', '{"matchId":"m1"}']);
});

test('cancelled loading cannot start later when Unity finally loads', () => {
  const calls = [];
  const bridge = new UnityBattleBridge();
  bridge.dispatch({ method: 'InitializeMultiplayer', data: { matchId: 'old' } });
  bridge.dispatch({ method: 'StopMultiplayer', data: { matchId: 'old' } });
  bridge.sceneReady(); bridge.setSender((...args) => calls.push(args));
  assert.equal(calls.length, 0);
  bridge.dispatch({ method: 'InitializeMultiplayer', data: { matchId: 'new' } });
  bridge.dispatch({ method: 'SyncMultiplayer', data: { matchId: 'old', tick: 10 } });
  assert.equal(calls.length, 1);
});
