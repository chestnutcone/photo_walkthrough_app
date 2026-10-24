# Web story viewer

Build (separate entry point, release needs `API_BASE_URL`):

    flutter build web -t lib/main_web.dart --release --no-web-resources-cdn \
      --dart-define=API_BASE_URL=https://api.example.com \
      --dart-define=LINKS_DOMAIN=<links domain> \
      --dart-define=APP_STORE_URL=<optional https store link> \
      --dart-define=PLAY_STORE_URL=<optional https store link>

`--no-web-resources-cdn` bundles CanvasKit instead of loading it from
gstatic.com, so the policy below needs no third-party script origin.

The viewer serves only `/s/{workId}`. Any other path, and every story that is
missing, private, blocked or removed, shows "This story isn't available". It
never signs in and sends no `Authorization` header.

## Routing

The API's share page (`/s/{id}`, preview tags, no script, no redirect) links
its "View story" button to `{Web:ViewerUrl}/s/{id}`. `Web:ViewerUrl` must be
the origin that serves this build, and it must NOT be the host that serves
the API share pages, or the button points at the share page itself. Serve
`index.html` for every `/s/*` path on the viewer origin (SPA fallback).

Universal links / app links are declared for the links domain (the share page
host). Where the app is installed, `https://{links domain}/s/{id}` opens the
app (iOS Runner.entitlements, Android intent filter) and never reaches the
browser.

## Content-Security-Policy the host must send

    default-src 'none';
    script-src 'self' 'wasm-unsafe-eval';
    style-src 'self' 'unsafe-inline';
    img-src 'self' data: blob: https:;
    font-src 'self' data:;
    connect-src 'self' https://<api origin> https://<image CDN origin>;
    worker-src 'self' blob:;
    manifest-src 'self';
    base-uri 'self';
    form-action 'none';
    frame-ancestors 'none';
    object-src 'none'

Notes:

- `'wasm-unsafe-eval'` is required by CanvasKit (WebAssembly). No
  `'unsafe-eval'` and no inline script are needed: `index.html` has none.
- `style-src 'unsafe-inline'` is needed because Flutter's engine sets inline
  styles on its host elements at runtime. `index.html` itself has no inline
  style.
- CanvasKit fetches photos with `fetch`, so the image CDN origin must be in
  `connect-src` and must send `Access-Control-Allow-Origin` for the viewer
  origin.
- Also send `X-Content-Type-Options: nosniff`, `Referrer-Policy:
  strict-origin-when-cross-origin`, HSTS, and `X-Robots-Tag: noindex` (the
  page also carries `<meta name="robots" content="noindex">`).
- Cache `index.html` and `flutter_service_worker.js` with `no-cache`; the
  hashed/versioned assets may be cached long.

The icons in `icons/` and `favicon.png` are Flutter placeholders until the
final name and icon exist.
