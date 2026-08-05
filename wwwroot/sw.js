// Service Worker for Tennis Intelligence PWA
const CACHE_NAME = 'tennis-intelligence-v2';
const SHELL_URL = '/loading.html';
const OFFLINE_URL = '/offline.html';

const SHELL_ASSETS = [
  '/loading.html',
  '/offline.html',
  '/css/site.css',
  '/js/site.js',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/lib/bootstrap/dist/js/bootstrap.bundle.min.js'
];

// Navigation requests (HTML pages) that should show loading shell during cold start
const isNavigationRequest = (request) =>
  request.mode === 'navigate' && request.method === 'GET';

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache => cache.addAll(SHELL_ASSETS))
  );
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys =>
      Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
    )
  );
  self.clients.claim();
});

self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;

  // For page navigations: race network vs a 2-second timeout
  // If server is cold-starting, show cached loading shell immediately
  if (isNavigationRequest(event.request)) {
    event.respondWith(
      new Promise((resolve) => {
        let settled = false;

        // Start the real network fetch
        const networkFetch = fetch(event.request).then(response => {
          if (!settled) {
            settled = true;
            // Cache the fresh page for next offline visit
            if (response.ok) {
              const clone = response.clone();
              caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
            }
            resolve(response);
          }
        }).catch(() => {
          if (!settled) {
            settled = true;
            // Offline: try cached page, then offline page
            caches.match(event.request).then(cached =>
              resolve(cached || caches.match(OFFLINE_URL))
            );
          }
        });

        // After 2 seconds, if network hasn't responded, show loading shell
        setTimeout(() => {
          if (!settled) {
            settled = true;
            caches.match(SHELL_URL).then(shell => {
              if (shell) {
                // Clone the shell and inject a script that auto-reloads when server is ready
                shell.text().then(html => {
                  const refreshScript = `<script>
                    (async function() {
                      while (true) {
                        try {
                          var r = await fetch('/api/health', { cache: 'no-store' });
                          if (r.ok) { location.reload(); return; }
                        } catch {}
                        await new Promise(ok => setTimeout(ok, 1500));
                      }
                    })();
                  </` + `script>`;
                  const enhanced = html.replace('</body>', refreshScript + '</body>');
                  resolve(new Response(enhanced, {
                    headers: { 'Content-Type': 'text/html; charset=utf-8' }
                  }));
                });
              } else {
                // No shell cached, just wait for network
                networkFetch;
              }
            });
          }
        }, 2000);
      })
    );
    return;
  }

  // Static assets: cache-first, fall back to network
  event.respondWith(
    caches.match(event.request).then(cached => {
      if (cached) return cached;
      return fetch(event.request).then(response => {
        if (response.ok) {
          const clone = response.clone();
          caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
        }
        return response;
      });
    })
  );
});
