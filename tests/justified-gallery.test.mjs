import { test } from 'node:test';
import assert from 'node:assert/strict';
import { rows } from '../src/MediaEngine.Web/wwwroot/js/justified-gallery.js';

test('mixed rows share height and fill available width except final row', () => {
  const ratios = [.5, 1, 1.5, 2, .7, 1.2, 3, .6, 1, 1.8, 1.4];
  for (const width of [320, 768, 1280, 1600]) {
    const layout = rows(ratios, width);
    assert.equal(layout.flat().length, ratios.length);
    layout.forEach((row, index) => {
      assert.ok(row.every(box => box.height === row[0].height));
      const used = row.reduce((sum, box) => sum + box.width, 0) + 7 * (row.length - 1);
      assert.ok(used <= width + .001);
      if (index < layout.length - 1) assert.ok(Math.abs(used - width) < .001);
    });
  }
});
test('ordinary assets retain aspect ratios and sparse final row never inflates', () => {
  const boxes = rows([.5, 1, 2], 1600).flat();
  assert.deepEqual(boxes.map(box => box.width / box.height), [.5, 1, 2]);
  assert.ok(boxes.every(box => box.height === 160));
});
test('extreme and unknown dimensions have finite bounded boxes', () => {
  for (const box of rows([100, .001, NaN, 0], 800).flat()) {
    assert.ok(Number.isFinite(box.width) && box.width > 0 && box.width <= 800);
    assert.ok(Number.isFinite(box.height) && box.height > 0);
  }
});
