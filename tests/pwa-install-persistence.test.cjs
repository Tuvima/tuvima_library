// Run with node --test tests/pwa-install-persistence.test.cjs.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/MediaEngine.Web/wwwroot/app.js', 'utf8');
const start = source.indexOf('window.tuvimaPwa = (function () {');
const bridge = source.slice(start, source.indexOf('})();', start) + 5);

function boot(storage) {
    const events = {};
    const window = { navigator: { userAgent: 'Desktop' }, matchMedia: () => ({ matches: false }), addEventListener: (name, fn) => events[name] = fn };
    vm.runInNewContext(bridge, { window, navigator: window.navigator, localStorage: {
        getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value)
    }, console });
    return { api: window.tuvimaPwa, events };
}

test('dismissal survives reload and later install events while Overview remains available', () => {
    const storage = new Map();
    let { api, events } = boot(storage);
    const ref = { invokeMethodAsync: () => Promise.resolve() };
    events.beforeinstallprompt({ preventDefault() {} });
    assert.equal(api.register(ref, 'banner', false), 'available');
    assert.equal(api.dismiss(), 'dismissed');
    ({ api, events } = boot(storage));
    assert.equal(api.register(ref, 'banner', false), 'dismissed');
    events.beforeinstallprompt({ preventDefault() {} });
    assert.equal(api.register(ref, 'banner', false), 'dismissed');
    assert.equal(api.register(ref, 'overview', true), 'available');
    api.unregister('overview');
    assert.equal(api.register(ref, 'banner', false), 'dismissed');
});

test('a dismissed browser install dialog persists the same banner preference', async () => {
    const storage = new Map();
    const { api, events } = boot(storage);
    events.beforeinstallprompt({ preventDefault() {}, prompt: async () => {}, userChoice: Promise.resolve({ outcome: 'dismissed' }) });
    assert.equal(await api.install(false), 'dismissed');
    assert.equal(boot(storage).api.register({ invokeMethodAsync() {} }, 'banner', false), 'dismissed');
});
