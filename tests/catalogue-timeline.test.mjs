import { test } from 'node:test';
import assert from 'node:assert/strict';
import { observe, jump, disconnect, isAtEnd } from '../src/MediaEngine.Web/wwwroot/js/catalogue-timeline.js';

test('end detection handles desktop, tablet, mobile and fractional scrolling', () => {
  for (const height of [820, 700, 520]) {
    assert.equal(isAtEnd(2000 - height, 2000, height), true);
    assert.equal(isAtEnd(2000 - height - .5, 2000, height), true);
    assert.equal(isAtEnd(2000 - height - 100, 2000, height), false);
  }
  assert.equal(isAtEnd(0, 520, 520), false, 'a short unscrolled list keeps its initial period');
});

test('catalogue timeline restores a period, tracks scrolling, scopes jumps and disposes', async () => {
  globalThis.location = { href: 'http://localhost/read/books?browse=timeline&period=decade' };
  const saved = new Map([['tuvima.timeline:/read/books?browse=timeline', '1995']]);
  globalThis.sessionStorage = { getItem: key => saved.get(key) ?? null, setItem: (key, value) => saved.set(key, value) };
  globalThis.innerHeight = 900;
  globalThis.matchMedia = () => ({ matches: true });
  let frame = 0;
  const frames = new Map();
  globalThis.requestAnimationFrame = fn => { frames.set(++frame, fn); return frame; };
  globalThis.cancelAnimationFrame = id => frames.delete(id);
  let disposed = 0;
  globalThis.ResizeObserver = class { observe() {} disconnect() { disposed++; } };
  const listeners = new Map();
  const scroller = { getBoundingClientRect: () => ({ top: 80, bottom: 900 }), addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
  globalThis.document = { scrollingElement: {} };
  let olderTop = 500, jumped = 0;
  const recent = { dataset: { timelineKey: '2024' }, getBoundingClientRect: () => ({ top: 100 }) };
  const older = { dataset: { timelineKey: '1995' }, getBoundingClientRect: () => ({ top: olderTop }), scrollIntoView: () => { jumped++; olderTop = 100; } };
  const rail = { style: { setProperty() {} }, getBoundingClientRect: () => ({ top: 100, bottom: 880 }), querySelector: () => null };
  const rootListeners = new Map();
  const root = { isConnected: true, addEventListener: (name, fn) => rootListeners.set(name, fn), removeEventListener: name => rootListeners.delete(name), closest: () => scroller, querySelectorAll: () => [recent, older], querySelector: selector => selector === '.view-timeline-scrubber' ? rail : selector === '[data-timeline-key="1995"]' ? older : null };
  const calls = [];
  const dotnet = { invokeMethodAsync: async (name, year) => { calls.push([name, year]); if (name === 'RestorePeriod') jump(root, year); } };
  observe(root, dotnet);
  await new Promise(resolve => setImmediate(resolve));
  const flush = () => { const pending = [...frames.values()]; frames.clear(); pending.forEach(fn => fn()); };
  flush();
  assert.deepEqual(calls.slice(0, 2), [['RestorePeriod', 1995], ['SetActivePeriod', 1995]]);
  assert.equal(jumped, 1);
  observe(root, dotnet);
  assert.equal(listeners.size, 1, 'rerenders must not duplicate observers');
  olderTop = 500;
  listeners.get('scroll')(); flush();
  assert.deepEqual(calls.at(-1), ['SetActivePeriod', 2024]);
  assert.equal(saved.get('tuvima.timeline:/read/books?browse=timeline'), '2024');
  Object.assign(scroller, {scrollTop:1180, scrollHeight:2000, clientHeight:820});
  listeners.get('scroll')(); flush();
  assert.deepEqual(calls.at(-1), ['SetActivePeriod', 1995], 'the last visible group wins at the bottom even below the activation line');
  assert.equal(jump(root, 1900), false, 'missing page keeps the pending jump');
  assert.equal(jump({ querySelector: () => null }, 1995), false, 'never jump to another instance');
  rootListeners.get('click')({ button: 0, target: { closest: () => ({ closest: () => older }) } });
  listeners.get('scroll')(); flush();
  assert.equal(saved.get('tuvima.timeline:/read/books?browse=timeline'), '1995', 'leaving-page scroll must not replace the opened item year');
  disconnect(root);
  assert.equal(listeners.size, 0);
  assert.equal(rootListeners.size, 0);
  assert.equal(disposed, 1);
  assert.doesNotThrow(() => disconnect(root));
});
