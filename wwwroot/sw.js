// Service Worker for Tennis Intelligence PWA
const CACHE_NAME = 'tennis-intelligence-v1';
const OFFLINE_URL = '/offline.html';

// Cache essential assets on install
self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache => {
      return cache.addAll([
        '/',
        '/css/site.css',
        '/js/site.js',
        '/lib/bootstrap/dist/css/bootstrap.min.css',
        '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
        '/offline.html'
      ]);
    })
  );
  self.skipWaiting();
});

// Clean old caches on activate
self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys =>
      Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
    )
  );
  self.clients.claim();
});

// Network-first strategy: try network, fall back to cache
self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;

  event.respondWith(
    fetch(event.request)
      .then(response => {
        // Cache successful responses for offline use
        if (response.ok) {
          const clone = response.clone();
          caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
        }
        return response;
      })
      .catch(() => {
        return caches.match(event.request).then(cached => {
          return cached || caches.match(OFFLINE_URL);
        });
      })
  );
});
