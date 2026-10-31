# End-to-end verification report (Android emulator, real local API)

Date: 2026-10-24. Device: Android 35 x86_64 emulator (headless, swiftshader), debug APK `dev.vela.app`,
API in Development mode on `http://10.0.2.2:5080` with Postgres 16 and Azurite in Docker.
Re-run everything with `scripts/run-e2e.sh` ([docs/dev/run-e2e.md](../dev/run-e2e.md)).

## Summary

- The native Android build had never been run. It failed twice (debug manifest merge, minSdk); both fixed. It now builds
  and runs.
- 18 on-device integration tests (6 files under `app/integration_test/`) drive the real app against the real API and pass (`scripts/run-e2e.sh`); the app's 786 unit/widget tests still pass.
- Two Blockers found and fixed: no way to reach Create/Portfolio/Settings (README tab bar missing) and a crash path when a new
  account signs in from the sheet. The same router-redirect race in the delete-account dialog was found by the suite and fixed (Major). Several Majors and Minors found; the small safe ones are fixed, the rest are listed below with a proposed fix.

## Build problems fixed (step 2)

| # | Problem | Fix |
|---|---|---|
| 1 | `processDebugMainManifest`: `usesCleartextTraffic` true (debug manifest) vs false (main) | `tools:replace` in `app/android/app/src/debug/AndroidManifest.xml` |
| 2 | `minSdkVersion 24 cannot be smaller than 26` (sentry_flutter 10) | `minSdk = 26` in `app/android/app/build.gradle.kts` (drops Android 7.x; Play share is small) |
| 3 | Flutter needs `unzip` to fetch Android artifacts | sandbox only (python shim outside the repo); a normal machine is fine |
| 4 | Launcher label was `vela_app`, launch window was white with the Flutter logo | label "Vela"; dark `vela_background` launch window (`res/values/colors.xml`) |

compileSdk 36 / JDK 17 / AGP 9.1 / Kotlin 2.4 / app_attest / jni built without changes.

## Flows verified (screenshots in `docs/review/screenshots/<flow>/`)

| Flow | Test | Result | Evidence |
|---|---|---|---|
| A guest: feed (Discover only), scroll, open story, intro, chapter 1..n via tap zones forward/back, end card, Explore, pin tap, replies sheet, composer asks guest to sign in | `a_guest_test.dart` | pass | `a-guest/01..10` |
| B sign in with Google button (fake SSO) from the feed, onboarding (welcome, name, handle, age, terms, 3 follows), feed | `b_signin_onboarding_test.dart` | pass (after fix, Blocker 2) | `b-signin/01..06` |
| B email code sign-in for a new account (code read from the API log through the host helper), onboarding | same | pass | `b-signin/10..11` |
| C follow during onboarding, Following tab shows the artist, unfollow from card | same | pass | `c-follow/01..02` |
| D post a reply, delete own reply (confirm dialog) | `d_social_test.dart` | pass | `d-replies/01..04` |
| E create: picker override, two pins (zoom-in, stay-put), title, publish, wait for `published`, server shows zoom/stay-put chapters, open it | `e_create_test.dart` | pass | `e-create/01..07` |
| E EXIF orientation 6 JPEG (1600x1000 stored sideways) is stored upright (1000x1600) and shown | same | pass | `e-create-rotated/*` |
| E a new account reaches Create, Portfolio, Settings from the feed alone | same | pass after fix (Blocker 1) | `e-nav/01..03` |
| F edit profile bio/city, shows on portfolio About tab | `f_profile_search_settings_test.dart` | pass | `f-profile/01..04` |
| G search an artist by name, open the portfolio | same | pass | `g-search/01..03` |
| H notifications: reply and follow by a second account (HTTP), unread badge, tap reply notification deep-links to the chapter replies | `d_social_test.dart` | pass | `h-notifications/01..03` |
| I settings: export request, sign out; delete account (type DELETE) wipes the session, token rejected by the API, cold restart stays guest; session persists across restart | `f_profile_search_settings_test.dart` | pass | `i-settings/*`, `i-delete/*`, `i-session/01` |
| J report a story as a signed-in user | `d_social_test.dart` | pass | `j-report/01..03` |
| K suspended account: app shows the suspended screen on the next call, "Continue as a guest" works | same | pass | `k-suspended/01..02` |

Driven with `adb`, not automated in a test (screenshots in `screenshots/links`, `screenshots/gap`):
deep links `vela://open/s/<id>` and `https://vela.example/s/<id>` (cold start, open the story; autoVerify itself cannot be
verified without a real domain), back button from a deep-linked story (leaves the app: expected, no history), font scale 2.0,
a 320x568 screen, landscape rotation, airplane mode (cold start shows "You're offline" with Retry; Retry recovers),
keyboard over the email field (field and Send code stay visible), emulated slow network (inconclusive: the emulator throttle does not apply to host loopback).

Server checks made on the way: with the user-state cache at 1 s, a suspended user's existing token gets `403 account_suspended` on
/me, notifications, replies, follows, drafts, profile, reports and exports (feed and search stay public by design).

## Gaps found

Severity: Blocker (cannot complete a core flow), Major (wrong or inaccessible but a workaround exists), Minor.

### Fixed in this branch

| Sev | Gap | Fix |
|---|---|---|
| Blocker | README section 6 tab bar (Feed, +, Portfolio) was never built. `/create`, `/settings`, `/profile/edit` and the own portfolio were reachable only from a story the user already owns, so a new user could never publish a first story | `features/feed/feed_tab_bar.dart`, wired into `FeedScreen`; widget tests `feed_tab_bar_test.dart`; e2e `e-nav` |
| Blocker | First sign-in from the sheet: the router redirects the new account to `/onboarding` while the sheet is open, then `Navigator.pop(true)` pops the onboarding page (go_router "popped the last page" assertion; blank screen in release) | `sign_in_sheet.dart` pops only while the sheet is the current route; covered by the B/C integration test |
| Major | Delete account: wiping the session makes the router replace the settings page under the dialog, then `Navigator.pop()` popped the feed page (`!_debugLocked` assertion; reproducible in the suite) | pop only while the dialog is the current route (`delete_account_flow.dart`); covered by the I delete integration test |
| Major | Story tap zones (previous/next chapter) had no semantics: TalkBack users could not step through chapters | `Semantics(button, 'Previous chapter' / 'Next chapter')` in `story_screen.dart` |
| Major (dev) | Local Azurite images returned 403 (containers are private; infra makes works/avatars public) | `Storage:EnsurePublicRead` (Development/Test only) set by `scripts/run-local-api.sh` |
| Minor | Launcher label `vela_app`; white launch flash with Flutter logo on a dark-only app | label and dark launch window (see above) |

### Open (not fixed)

S2 update (2026-10-31): fixed since this run: the report threshold (illegal content hides at once, otherwise 3 distinct
reporters or one account older than 7 days), the per-install fake SSO account (random id kept on the install;
`VELA_FAKE_SSO_SUBJECT` pins one), the story top bar at 2.0 / 320 pt, portrait lock, 48 pt targets, `faint` contrast and Back
from a deep-linked story. See [a11y-triage.md](a11y-triage.md). The rows below are kept as found.

| Sev | Gap | Proposed fix |
|---|---|---|
| Major | One report from any account hides the story from everyone until an admin reviews (spec: "User-reported content is hidden pending review"). My own probe hid a seeded story by accident. A throwaway account can take down any artist's story | require N distinct reporters or an account age/behaviour threshold before auto-hide, except `illegal_content`; keep the 10/day limit |
| Major | Fixed fake identity: with `VELA_FAKE_SSO=true` every install signs in as the same `dev-google` account, so onboarding can be exercised once per database | `VELA_FAKE_SSO_SUBJECT` dart-define (tests override `ssoClientProvider` instead) |
| Minor | Story top bar drops the artist name and title at font scale 2.0 and on 320 dp wide screens (avatar only) | ellipsize inside a flexible row instead of hiding |
| Minor | Landscape is allowed: the bottom card covers about half the photo and can hide pins | lock the viewer to portrait, or move the card to a side panel |
| Minor | Tap targets of 44 dp (Back buttons, artist row, pins, "Followed by" row) and 15 dp progress segments, below Android's 48 dp (`docs/review/a11y-violations.txt`) | raise min sizes to 48 |
| Minor | `VelaColors.faint` on `input` is 4.49:1 (just under 4.5). All other text tokens pass AA (`tools/contrast_check.py`). Flutter's pixel-based contrast guideline also flags muted text over photographs and during transitions; not verified one by one | lighten `faint` slightly; re-check the flagged screens |
| Minor | Create form has no intro field (the README prototype has none either, but the viewer shows `intro`) | owner decision |
| Minor | Feed meta shows the date only (no city): `ArtistSummary` lacks city (known contract item) | contract change |
| Minor | Story opened from a deep link: system Back leaves the app instead of going to the feed (the close button goes to the feed) | route Back to `/` when there is no history |
| Minor | Offline mid-session (posting a reply, publishing) and truly slow networks were not exercised on device | manual pass on a real phone |

### Not feasible in this sandbox (what to run on a real device)

Real Google/Apple sign-in, Play Integrity attestation, app-link `autoVerify` against a real domain, TalkBack speech order
(only semantics guidelines and labels were checked), real camera, push, release/obfuscated build, Play store listing checks.

## Prototype comparison

The prototype (`design_handoff_vela_app/Vela App.dc.html`, rendered with Playwright's Chromium) and the app agree on the feed: wordmark,
subline, tabs, artist row, pin dots, serif title, mono meta, "Walk through" pill, and (after the fix) the bottom bar. Deviations:
no city in the artist row (contract), Discover/Following order, extra search and bell icons (not in the README, owner confirmed placement item),
photos with a different aspect ratio are letterboxed on a grey card instead of cropped, no cover "PHOTO · TITLE" caption on the feed photo.

## Accessibility

`g_a11y_test.dart` runs Flutter's tap-target, labelled-target and text-contrast guidelines on 19 screens; the violations are
in `docs/review/a11y-violations.txt` (21 at the first run, mostly 44 dp targets and contrast hints over images). Labels: every
interactive control found had a label except the story tap zones (fixed). Font scale 2.0 and 320 dp layouts do not overflow.
