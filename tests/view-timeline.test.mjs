import { test } from 'node:test';
import assert from 'node:assert/strict';
import { observeTimeline, disconnectTimeline } from '../src/MediaEngine.Web/wwwroot/js/view-timeline.js';

test('empty filtered results have no timeline element and must not throw', () => {
  for (const anchor of [null, undefined, { isConnected: false }]) {
    assert.doesNotThrow(() => observeTimeline(anchor, null));
    assert.doesNotThrow(() => disconnectTimeline(anchor));
  }
});
