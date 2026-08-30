/*
 * Tuvima Library is installable and reconnect-capable, not an offline media
 * client. This worker deliberately leaves requests on the network so the UI
 * never presents stale catalogue, identity, or playback state as current.
 */
self.addEventListener('install', function () {
    self.skipWaiting();
});

self.addEventListener('activate', function (event) {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', function (event) {
    event.respondWith(fetch(event.request));
});
