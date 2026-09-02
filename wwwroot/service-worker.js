// Baby Tracker service worker.
//
// This is a Blazor *Server* app: pages are rendered over a live SignalR circuit, so the
// app itself cannot run offline. The worker therefore does two things only:
//   1. makes the app installable and instant to open (static shell assets cached),
//   2. shows a friendly offline page instead of the browser error when the phone is off-network.
// Anything dynamic (_blazor, _framework, POSTs) always goes straight to the network.

const CACHE = 'babytracker-v2';
const SHELL = [
    'app.css',
    'offline.html',
    'manifest.webmanifest',
    'icons/icon-192.png',
    'icons/icon-512.png'
];

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE)
            .then(cache => cache.addAll(SHELL))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    const request = event.request;

    if (request.method !== 'GET') {
        return;
    }

    const url = new URL(request.url);

    if (url.origin !== self.location.origin
        || url.pathname.startsWith('/_blazor')
        || url.pathname.startsWith('/_framework')) {
        return;
    }

    // Pages are always rendered server side; never serve a stale document.
    if (request.mode === 'navigate') {
        event.respondWith(fetch(request).catch(() => caches.match('offline.html')));
        return;
    }

    // Static assets: serve from cache, refresh in the background.
    event.respondWith(
        caches.match(request).then(cached => {
            const network = fetch(request).then(response => {
                if (response.ok) {
                    const copy = response.clone();
                    caches.open(CACHE).then(cache => cache.put(request, copy));
                }
                return response;
            }).catch(() => cached);
            return cached || network;
        })
    );
});
