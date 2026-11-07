# Web story viewer check (Playwright)

Date: 2026-11-06 (E2E-FINAL re-run on the final code; first run 2026-10-31). Chromium 156 headless (Playwright cache), viewer built from this branch with
`flutter build web -t lib/main_web.dart --profile --no-web-resources-cdn --dart-define=API_BASE_URL=http://127.0.0.1:5080`
and served with an SPA fallback on `http://127.0.0.1:8090`; the local API ([run-local.md](../dev/run-local.md)) with the seeded
story "Salt flats" (4 chapters). Script: `tools/web_viewer_check.js` (needs `playwright-core`). Screenshots:
[`screenshots/web-viewer/`](screenshots/web-viewer/).

Local set-up notes: a `--release` build refuses an `http://` API (by design, `config.dart`), so the local check uses a
`--profile` build. Azurite sends no CORS headers by default, so CanvasKit could not fetch the photos until CORS was added
with `az storage cors add --services b --methods GET HEAD OPTIONS --origins http://127.0.0.1:8090 ...` (in Azure the image
CDN must send `Access-Control-Allow-Origin`, as `app/web/README.md` already says).

## Results

| Check | Desktop 1280 x 800 | Phone 390 x 844 |
|---|---|---|
| Shared link `/s/{id}` loads the story | pass (5.3 s headless, software WebGL; 5.7 s on 10-31) | pass (5.7 s) |
| API share page `GET /s/{id}` (the link people share) | `<title>Salt flats · Vela</title>`, Open Graph title/description/url; "View story" points at `Web:ViewerUrl` (`https://vela.example` locally, so not followed) | same |
| Page title | was always "Vela"; **fixed**: "Salt flats · Vela" (ab4c0e7, test `page_title_names_the_story`) | same |
| Keyboard: chapters | was impossible (Begin, tap zones and pins are gesture detectors); **fixed**: Arrow Right / Left step through (519804c). Verified: Right x2 = chapter 2, Left = chapter 1 (`*-03`, `*-04`) | same |
| Keyboard: Tab | Tab cycles the page body, Flutter's "Enable accessibility" placeholder and the Flutter view. No story control shows a focus ring (`*-02-tab-focus` is identical to the intro) | same |
| Focus visibility | **gap** (see below) | same |
| Screen-reader tree (after "Enable accessibility") | photo alt text, "Go to chapter n of 4" x4, artist, Story, Explore, Previous / Next chapter, live region "Chapter 1 of 4, Centre", Reply, Open in app | same |
| Console errors / failed requests | none (only Chromium software-WebGL warnings and one Flutter debug line) | none |
| Third-party script origins | none: the GSI client is no longer in the build (`scripts/check-web-viewer-origins.sh` OK on the profile build; FINALAPP) | same |

## Gaps (not fixed)

Re-checked 2026-11-06: the focus-visibility and desktop-layout gaps are unchanged; the GSI gap is fixed (struck below).

| Sev | Gap | Why not fixed / proposed fix |
|---|---|---|
| Major (web, keyboard) | No visible focus on the viewer's own controls: Begin, Story/Explore, chapter segments, pins and Reply are gesture detectors with semantics. They work with screen readers and Switch Access, and Arrow keys now step chapters, but Tab never lands on them and no focus ring is drawn | Needs each custom control rebuilt as a focusable button with a designed focus ring (story chrome, card, pins). Too large for this task; listed as Later |
| ~~Minor (privacy / CSP)~~ fixed (FINALAPP) | The viewer loaded `https://accounts.google.com/gsi/client` on every page view: `google_sign_in`'s web plugin is registered in the shared web build although the viewer never signs in. Under the CSP in `app/web/README.md` (`script-src 'self'`) it is blocked and logs a CSP error | Build the viewer without the sign-in plugins (separate pubspec / package for the viewer, or a conditional plugin registrant). Later |
| Minor (layout, desktop) | At 1280 x 800 the bottom card spans the full width and the spotlight of chapter 2 sits under the card (`desktop-03`). The handoff designs only the 390 x 844 phone frame | Cap the card width (about 560 pt) and keep the camera target above the card on landscape viewports. Owner/design decision for a desktop layout |
