import test from 'node:test';
import assert from 'node:assert/strict';
import { base64Of, newPrivacy, publicImageOf } from '../src/swordPrivacy.js';

const sword = (mode, extra = {}) => ({
  imageSrc: 'data:image/png;base64,ORIGINAL',
  silhouetteSrc: 'data:image/png;base64,SILHOUETTE',
  onepointSrc: 'data:image/png;base64,ONEPOINT',
  privacy: newPrivacy(mode),
  ...extra,
});

test('the natural mode shows the photo as it is', () => {
  assert.equal(publicImageOf(sword('none')), 'data:image/png;base64,ORIGINAL');
});

test('the one-point mode shows the image with the circle burned in', () => {
  assert.equal(publicImageOf(sword('onepoint')), 'data:image/png;base64,ONEPOINT');
});

test('until the one-point image is ready, the silhouette stands in, never the photo', () => {
  assert.equal(publicImageOf(sword('onepoint', { onepointSrc: '' })), 'data:image/png;base64,SILHOUETTE');
});

test('the silhouette mode shows the silhouette', () => {
  assert.equal(publicImageOf(sword('silhouette')), 'data:image/png;base64,SILHOUETTE');
});

test('a random match always uses the silhouette, whatever the setting', () => {
  for (const mode of ['none', 'onepoint', 'silhouette']) {
    assert.equal(publicImageOf(sword(mode), true), 'data:image/png;base64,SILHOUETTE');
  }
});

test('when no processed image exists, privacy modes send nothing rather than the photo', () => {
  for (const [mode, random] of [['onepoint', false], ['silhouette', false], ['none', true]]) {
    const image = publicImageOf(sword(mode, { silhouetteSrc: '', onepointSrc: '' }), random);
    assert.equal(image, '');
    assert.equal(base64Of(image), '');
  }
});

test('a sword saved before privacy existed counts as natural', () => {
  const old = sword('none');
  delete old.privacy;
  assert.equal(publicImageOf(old), 'data:image/png;base64,ORIGINAL');
});

test('the circle always starts in the middle of the photo, at a middle size', () => {
  assert.deepEqual(newPrivacy('onepoint').spot, { x: 0.5, y: 0.5, r: 0.17 });
});
