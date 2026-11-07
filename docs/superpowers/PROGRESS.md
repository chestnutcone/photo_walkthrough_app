# Vela build progress

Read this first after any interruption. Update it after every task, then commit.

- Spec (approved): `docs/superpowers/specs/2026-10-22-vela-design.md`
- Plan (approved): `docs/superpowers/plans/2026-10-22-vela-build.md`
- Execution: subagent-driven (fresh subagent per task, reviewer after each task). Owner approved.
- Git: repo at `/home/oliver/vela`, branch `main`, remote `origin` = `git@github.com:chestnutcone/photo_walkthrough_app.git`. Commit and push at milestones (end of each track phase, see below).
- Code name only: "Vela". Final product name TBD.

## Latest owner decisions (also in spec section 13-14 and plan Global Constraints)
- Content Safety auto-blocks (story `blocked`, reply hidden) and queues for admin restore/confirm. Fail-open after scan retries.
- Account ban/reinstate in admin; lookup page. Max 10 reports/account/day (configurable).
- US market, East US 2, 18+ only, own auth (Apple/Google/email code), Sentry, Cloudflare, Log Analytics (~5 GB).
- Apple Developer account not yet available: use fakes, deployment deferred.
- 2026-11-03 (built in DECISIONS): (a) an `illegal_content` report hides at once only when the reporter's account is at least
  24 h old (`Reports:IllegalContentInstantHideMinAgeHours`); younger accounts' reports are queued and counted, no hide;
  (b) deleting an account while content is under review blocks the same sign-in identity (403 `signup_blocked`) until the
  review closes, at most 30 days; only the identity hash and dates are stored; bans stay permanent; (c) attestation at launch
  on Android only (Play Integrity, `Abuse:RequireAttestation:Android=true`), iOS App Attest deferred (`:Ios=false`), iOS and
  every other unattested request held to its own per-address/per-account limits (sized for shared addresses, DECISIONS fix). Product name stays Vela (code name).

## Release blockers
- [ ] **Legal review by counsel.** Legal texts are drafts; keep as is until counsel reviews. Scope: `api/src/Vela.Api/Legal/`
  `privacy.md`, `terms.md`, `content.md`, `dmca.md`, and the behaviours counsel must confirm: 30-day deletion deferral, 7-day
  recovery windows (DB backups, storage soft delete), 3-year DMCA retention, ban and audit-trail retention periods, the 24 h
  illegal-content hide rule, the re-signup block, DMCA agent registration, NCMEC process and ESP account, 18+ self-confirmation
  and COPPA, the fail-open scanner stance (Content Safety, malware), app-store privacy disclosures. Owner list:
  `docs/owner-todos.html` ("Release blockers").

## How to resume
1. `cd /home/oliver/vela && git status && git log --oneline | head`
2. Find the first unchecked task below; read its section in the plan.
3. If a task is marked IN PROGRESS, inspect `git diff` for partial work, decide whether to continue or reset that task's files.

## Milestones (commit and push when reached)
- [x] M0 docs committed and pushed (spec, plan, progress) — commit 310df02
- [x] M1 Foundation done (T1, T2)
- [x] M2 API core done (A1-A6)
- [x] M3 API features done (A7-A13)
- [x] M4 Admin and safety done (A14-A16)
- [x] M5 App core done (B1-B5)
- [x] M6 App features done (B6-B14)
- [ ] M7 Infra and security done (C1, S1)

## Task checklist (mark `[x]` when merged and tests pass; `[~]` when in progress)
- [x] T1 Repo scaffold (a8dca9b)
- [x] T2 OpenAPI contract and generated client (503a45a)
- [x] A1 Schema and migrations (56fb231)
- [x] A2 Access and refresh tokens (d28b26e)
- [x] A3 SSO sign-in (a2277e1)
- [x] A4 Email one-time code (1fdcd5c)
- [x] A5 Profiles, onboarding, handles (1d81d0f)
- [x] A6 Blocks, reports, visibility (ccb8aef)
- [x] A7 Quota, drafts, uploads, image variants (e631618)
- [x] A8 Works, chapters, publish, moderation (1f75178)
- [x] A9 Follows, feed, replies (811af34)
- [x] A10 Notifications and inquiries (686f382)
- [x] A11 Search (ff35eb9)
- [x] A12 Share pages, well-known, legal (1c5df5e)
- [x] A13 Account deletion and export (850a17f)
- [x] A14 Admin moderation, ban/restore, evidence hold, DMCA (A14a 398b597; A14b d1cf76b)
- [x] A15 Abuse and bot protection (4be2c1c, 534a1b2, fix b8b648a)
- [x] A16 Telemetry and crash reporting (API) (d89d3ee, f4677dd, 13b2e1d)
- [x] B1 Theme, fonts, branding (a57bd35, fix 6e8751b)
- [x] B2 API wiring, session, router, guest gating (f9e0e28, 63c8c2b + fix)
- [x] B3 Camera math (7a19b28)
- [x] B4 Story viewer (9591cce, 6b111eb, f536591, fix 4f4e3f5)
- [x] B5 Feed (5f1a28c, ce512c0, fix ba260ab)
- [x] B6 Replies sheet (6e6037f, 788e925, 7ac3baa, fix c54f454)
- [x] B7 Portfolio, followers, contact (d2962ae, 4848bf5, fix 90da9cb)
- [x] B8 Sign-in and onboarding (8bedeb2, 4293b11, e229f80, 6c77de7, fix 955e3bb)
- [x] B9 Create story (8d037db, 2971950, fix 44d7a4b)
- [x] B10 Edit profile and settings (1f73962, b11e904, d894c13, fix = HEAD before docs)
- [x] B11 Search (e29ebe7, fix a80d831)
- [x] B12 Notifications list (44aaa28, fix a80d831)
- [x] B13 Web viewer and deep links (5eb563a, fix d0232c4)
- [x] B14 Reporting UI, suspended state, crash reporting, attestation (f55a587, fix eb449e2)
- [x] C1 Bicep and deployment workflow (32cbd7c, fix cd53d6c)
- [ ] S1 Security review gate
- [x] S2 Upload and text hardening, report threshold, E1 a11y fixes (5493cf0..7b13e4e; malware gate, text sanitizer, inquiry mail, content sniff, report threshold, 48pt targets/contrast/portrait/deep-link back, per-install fake SSO); review fix round 399497f..51d7f26 (malware restore/render per work, 10 s wait + 45 s app publish timeout + idempotent retry, mark-cap/tag/blank-name sanitizer fixes, inquiry quoting, threshold ignores suspended reporters, search normalisation, R-2 back to Medium as owner decision)
- [x] GALLERY Screenshots, accessibility audit, emulator e2e, web viewer check (2d65b4a..71c2e27): widget gallery of 64 shots
  ([gallery.md](../review/gallery.md)), audit over every screen ([a11y-audit.md](../review/a11y-audit.md)), e2e 18/18 on the emulator
  ([e2e-report.md](../review/e2e-report.md)), Playwright web viewer pass ([web-viewer-report.md](../review/web-viewer-report.md));
  11 defects fixed test-first (live-region chapter announcements, tap-zone semantics, pin reading order, arrow keys, composer 48 pt,
  Google "G" mark, search prompt, sheet double grabbers, meta chip wrap, not-found debug text, web page title)
- [x] FINALAPP Final review (app) fixes (c91fed6..f14ca3c): every sign-out path runs the sign-out cleaners and clears the image
  cache, account_deleted wipes all, refresh 403 only for account_* codes, Google SDK sign-out (I-1, M-1, M-2); web viewer loads no
  Google GSI script + CI origin check (I-2); 24 MP cap, killable 30 s isolate re-encode, undecodable rotated JPEG refused (I-3, M-7);
  Sign in with Apple entitlement (I-4); Android release refuses the debug key and placeholder values, iOS check script (I-5); delete
  copy (M-8); deep link kept through onboarding (M-4); intl month names (M-9); pin-drop haptic (M-3)
- [x] FINALAPI Final review (API) fixes (61cf8cb..fc8ed7b, [report](../../.superpowers/sdd/2026-10-22-vela-build/task-FINALAPI-report.md) local only): EF/HttpClient
  logs at Warning (I1); accounts pending deletion can be banned, Mark illegal reports the real ban outcome (I3); `parameters.prod.json`,
  validate-infra prod and `--deploy` checks, production checklist (I4); JWKS cache thread-safe (I7); avatars scanned before they go
  public (I6); spans scrubbed before export (I2); DMCA retention job and privacy Retention text (I5 + 7-day backup wording);
  18+/terms gate on inquiry/report/block (M18); Production refuses missing DMCA agent / CDN purge (M9); draft cleanup vs in-flight
  publish (M13); restore over quota defined (M5); take-down reconciler rotation (M4)
- [x] DECISIONS Owner decisions 2026-11-03 (fc03982, 65a013f, 31bab3c + docs 1289614, bb2a84b, 9faf5db, [report](../../.superpowers/sdd/2026-10-22-vela-build/task-DECISIONS-report.md) local only):
  `illegal_content` instant hide only from accounts at least 24 h old; re-signup block `signup_blocked` after deleting during
  review (hash + dates, at most 30 days); per-platform attestation (Android Play Integrity required, iOS deferred with tighter
  unattested limits); release blocker "Legal review by counsel"
- [x] DECISIONS fix round (review-DECISIONS.md; [report](../../.superpowers/sdd/2026-10-22-vela-build/task-DECISIONS-fix-report.md) local only):
  unattested limits sized for shared addresses (email 20/h per address + 5/h per (address, email), sso 60/h per address,
  create_work 10/day per account), charged only after basic validation (sso after the identity token verifies), refusals counted by
  `vela.abuse.unattested_limited`; R-3/deploy.md say the limits bound abuse per address only (I-1, I-2); report min-age validated
  at start-up (M-4); `appAttestAppId` empty in prod params (M-5); validate-infra pins `requireAttestationAndroid=true` (M-6);
  default/sso/create_work limit tests (M-7); deferral and signup blocks in one transaction (M-2)
- [x] E2E-FINAL Final emulator e2e on the final code (8e34f07..HEAD, branch `build/e2e-final`, [report](../../.superpowers/sdd/2026-10-22-vela-build/task-E2EFINAL-report.md) local only):
  25/25 on the emulator incl. new flows L (24 h `illegal_content`), M (`signup_blocked`), N (Android attestation); B/C test updated
  for the follow-at-sign-in behaviour; fresh screenshots; web viewer re-checked; [install-on-device.md](../dev/install-on-device.md)
- [x] CLEANUP Contract and leftovers (77fa745, 77da463, b57a3c0, accfbf2, 18c5700): 501 stubs removed, idempotent follow text, open notification type, contract length limits (F-T2-1, F-A8-4, F-A13-1), B7 website opens https, 'Followed by' excludes viewer, zero build/analyze warnings, consolidated Deferred minors list below

## Review documents
- Widget screenshot gallery: [docs/review/gallery.md](../review/gallery.md)
- Accessibility audit (widget level, every screen): [docs/review/a11y-audit.md](../review/a11y-audit.md); S2 triage: [a11y-triage.md](../review/a11y-triage.md)
- Emulator end-to-end report: [docs/review/e2e-report.md](../review/e2e-report.md)
- Web viewer check: [docs/review/web-viewer-report.md](../review/web-viewer-report.md)

## Deferred minors (single consolidated list, CLEANUP 2026-10-31)

Every deferred minor from the per-task reviews (T1..S2-fix) that is still open, with why it was left. Dispositions: **Accept** (left on purpose,
reason given), **Owner** (needs an owner/counsel decision or account), **Device** (needs a device, a deployment or a real third-party account),
**Later** (real work, too large or risky for a cleanup task). IDs `F-*` / `S1b-*` / `M-*` are detailed in `docs/security/review-findings.md`,
`docs/security/s1b-security-reviewer-findings.md` and `.superpowers/sdd/2026-10-22-vela-build/review-S2.md`. Items closed earlier are not repeated.

Fixed in CLEANUP: dead 501 stub probe + 37 contract `501` entries (F-A13-1); contract length limits (F-T2-1, F-A8-4); follow/unfollow
documented idempotent (B5 note); `Notification.type` open string (B11/B12 note); B7 website row opens https sites; B7 "Followed by" never
names the viewer; unused `levels` test parameter (F-A4-10); all `dotnet build` warnings and all `flutter analyze` issues.

### API
| Task | Item | Disposition | Reason |
|---|---|---|---|
| T2 | Whitespace-only inquiry passes `minLength: 1` in the contract (F-T2-2) | Accept | Server cleans then refuses; the contract now says lengths count the cleaned text |
| A1 | DB CHECKs (handle, bio, status values, hidden consistency), FKs for notification actor/work/reply, Inquiries indexes, cascade-metadata and null-zoom tests (F-A1-1) | Accept | Values are written only by validated code; a migration for no external-attacker gain |
| A2 | 401/framework 400s not all enveloped (F-A2-3); negative bearer tests assert only `IsValid` | Accept | 401 is enveloped; framework 400s leak nothing; the HTTP matrix covers real tokens |
| A3 | Duplicate assert, redundant AppleAud loop, blank-audience test (F-A3-5/6); no failure reason-code logging (F-A3-7) | Accept | Cosmetic / privacy first |
| A4 | Unknown-vs-known email verify timing (F-A4-1, R-12); thin boundary/log tests; request not one transaction; review emails skip DB (F-A4-7); per-IP count-then-insert soft; 30/IP/hour on shared NAT (F-A4-8/9) | Accept / Owner (NAT number) | Identical responses + rate limits; tune with real traffic |
| A5 | Boundary tests, 29d23h handle timing, onboarding double-submit race, constraint-name match (F-A5-2..4); >50 follow ids rejected (F-A5-7) | Accept | Outcome is a clean 400/409 |
| A5 | Follower counts include suspended/deleted users (F-A5-6) | Owner | Product decision |
| A6 | Daily report limit not serialised (F-A6-2); `ChangeTracker.Clear()` in auto-flag catch (F-A6-3); race/boundary tests; undropped `iso_*` test DBs (F-A6-4/6/8); report-hide overrides `HiddenBy` (F-A6-7) | Accept | Throttle only / error path / test hygiene; admin hides are never overridden |
| A7 | Cleanup vs publish race, starvation by 100 failing drafts (F-A7-1/2); SAS cannot cap size (F-A7-3, see S1b-7); partial variants, no Display-P3 to sRGB (F-A7-5/6); `EnsureContainers` per write, fixtures, quota overlap test (F-A7-9..11) | Accept | Publish fails cleanly and retries; colour cosmetic; perf/test breadth |
| A8 | Throwing handler starvation guarded by construction (F-A8-1); `\r` allowed, non-JSON content type not enveloped, orphan variants on failed save (F-A8-2) | Accept | `PublishFailures` recorded; orphan GC is ops |
| A8 | Published stories are not editable (F-A8-5) | Owner | Feature (edit with re-scan) |
| A9 | Cursor replay after follow change; feed returns full works (slim card DTO); retry every 3 min without cap; limit<=0 clamps to 1 (F-A9-1..3, F-A9-5) | Accept | Rare / performance follow-up / tuning. (Guest `isFollowing` and bad `tab` 400 are tested: `ArtistEndpointTests`, `FeedTests`) |
| A10 | Retry-After and 24 h boundary untested; NewStory/Follower rollback untested; per-IP inquiry unserialised (F-A10-3/4/6) | Accept | Test breadth / soft limit |
| A10 | No retention job for inquiries and notifications (F-A10-5, R-13) | Owner | Needs a retention period from counsel |
| A11 | Weak hostile-input test, 6+ terms, word-similarity cannot use GIN; unsigned cursor; test DB not dropped (F-A11-1..3) | Accept | Revisit with real data; cursor reveals nothing new |
| A12 | 404 byte-equality without headers, regex escaping tests (F-A12-2); HEAD on well-known (F-A12-3) | Accept | Cosmetic |
| A12 | Apple Sign in token revocation at deletion (F-A12-5) | Owner | Needs the Apple account; `IAppleTokenRevoker` is a logging no-op |
| A13 | Orphan blobs from SAS URLs used after hard delete (F-A13-3) | Device | Ops lifecycle/orphan scan; bounded by 15-min URL life |
| A13 | `follows.json` lists suspended/deleted followers (F-A13-4); guard blind spots, lease not renewed, archive cleanup (F-A13-5..7) | Accept | Public handles only; overlap safe |
| A14a | Audit table not DB-immutable (F-A14a-3) | Owner (deploy) | Grant INSERT/SELECT only once the non-admin DB role (R-7) exists |
| A14a | Dashboard thumbnails fetch full-size originals (F-A14a-5); dismiss form has no reason (F-A14a-8); state-cache repopulation race, reconciler lock across IO (F-A14a-9/10) | Accept | Admin-only cost / UX / bounded |
| A14b | Audit on evidence view (F-A14b-3); real NCMEC integration; DMCA wording and 10-day period (F-A14b-5/6) | Owner | Counsel / ESP account |
| A14b | Forward-notice stuck state after a crash (F-A14b-4) | Accept | Rare; runbook line is the owner's |
| A15 | Play Integrity server nonce, App Attest counter/replay (F-A15-1/2, S1b-6/8/14) | Owner | Play Integrity is enforced on Android at launch without a nonce store (token replayable for its max age); App Attest stays off until the owner's design (DECISIONS) |
| A15 | In-memory limiters per instance (F-A15-5, R-4) | Accept | Single instance at launch |
| A16 | No stack when Sentry off, console logs unscrubbed (audited), admin 500 as JSON (F-A16-1..3) | Accept | Nothing leaks |
| A16 | Sentry `BeforeSend` throw drops the event (F-A16-4, R-10) | Device | Confirm with a real DSN |
| S1b | S1b-4 unscanned variants at a predictable URL / staging prefix; S1b-7 unbounded upload size; S1b-9 evidence not tamper-evident | Later | Each needs storage-layout or Azure work; listed in `s1b-security-reviewer-findings.md` section 7 |
| S1b | S1b-10/11/16/18 (admin bearer reads, review/admin overlap, email canonicalisation for bans, scheme routes); S1b-19 read-SAS content-type override | Later | Low; not cheap without design changes |
| S1b | Android release key (S1b-17: a release without `key.properties` is now refused, the owner must create the key, see deploy.md "App release builds"); inquiry footer text (S1b-20) | Owner | Legal/product decision; release keystore; wording |
| S2 | Defender malware settings not validated against Azure; tag-read permission needed under managed identity | Device | bicep build/lint only |
| S2-fix | M-3 late verdict after the sweep published is not acted on | Later | Needs a reconciler pass (threat model R-18) |
| S2-fix | M-7 only replies are cleaned client-side (other forms get an opaque 400 for bidi isolates from RTL copy) | Later | Per-field client clean or server message surfacing across several forms |
| S2-fix | M-12 declared type and bytes read in two requests; M-13 read SAS without rsct/rscd pinning and admin fallback to the upload name | Later | Needs an `IBlobStore` read-with-details API / AzureBlobStore change and an Azure check |
| C1 | DP keys unencrypted (R-16); app connects as DB admin, allow-Azure firewall (R-7); Authenticated Origin Pulls; RG-scoped principal; orphan originals after deletion sweep (F-C1-2/3/5) | Owner | First real deploy |
| FINALAPI | M1 refresh rotation has no grace window (lost response or two concurrent refreshes sign the user out) | Later | Needs a successor cache keyed by the old token and a race test; the app retries one refresh (F-B2-2) |
| FINALAPI | M2 client-chosen SSO nonce, id tokens replayable until expiry; M3 per-email code limit lets anyone lock a victim out of email sign-in for an hour | Later / Owner | M2 needs a server nonce endpoint (contract change); M3 needs a product choice (per-IP budget vs live-code holder) |
| FINALAPI | M6 contract does not enumerate error codes; 403 `account_*` and `onboarding_required` (now also on report/block, M18) missing from most operations; no schema-conformance test | Later (contract) | Contract change, out of the API-only round: add `x-error-codes` per operation and a response-schema test |
| FINALAPI | M7 no `ix_reports_target`; M8 search full scan per page | Later | Migration / revisit with real data |
| FINALAPI | M10 Play Integrity request hash = SHA-256(action), replayable for MaxAge | Owner | Part of the attestation design (with F-A15-1/2); the per-IP/per-user limits and the signup cap are the real protection |
| FINALAPI | M11 ex-admin keeps API read access to non-public works until the reconciler/admin visit | Accept | 10-minute window, metadata only |
| FINALAPI | M12 one 7-day-old account hides 10 stories a day (R-2); since DECISIONS also one 24 h-old account with `illegal_content` | Accept | Owner decision 2026-11-03 (threat-model section 7); admins restore |
| FINALAPI | M14 publish holds the row lock across Content Safety calls | Later | Lease column + scan outside the transaction |
| FINALAPI | M15 api.yml: no timeout, no gen-client drift check, no Docker build, no coverage gate; base images not digest-pinned. `infra.yml` does not run `validate-infra.sh --deploy` and defaults to the dev file | Later | `.github/` was outside this round's scope; deploy.md names the prod file and the `--deploy` gate |
| FINALAPI | M16 export includes the sealed original of a malware-flagged work and omits moderation records about the user | Owner | GDPR/CCPA access-scope decision (admin_actions reasons are now stated as retained in privacy.md) |
| FINALAPI | M17 single HMAC key, no `kid`, no key-overlap procedure | Accept | Rotation signs everyone out once; document when a rotation is planned |
| FINALAPI | M5 rest: blocked/removed works keep sealed originals with no count cap | Later | Needs a storage cap or a sweep of old blocked originals (counsel: evidence value) |
| FINALAPI | M9 rest: `Branding:Domain` still falls back to `vela.example` when unset | Accept | Infra always sets it (required `brandingDomain`), and the viewer-host check refuses equal hosts |
| FINALAPI | I6 rest: no admin "clear avatar" action; profile text unscanned | Later / Accept | Avatars are now scanned before publishing (pre-launch none exist unscanned); ban covers a bad one; spec does not require profile text scans |
| DECISIONS | An `X-Platform: ios` claim cannot be proven, so a script claiming iOS skips Play Integrity and gets the unattested budget (R-3); the unattested limiter is in memory per replica (R-4) | Owner / Accept | Closes when App Attest is designed and `Abuse:RequireAttestation:Ios=true`; single replica at launch |
| DECISIONS | Re-signup block lapses up to an hour after the review closes (the deletion sweep rechecks hourly); a different identity (other email, Apple or Google account) is not blocked; rotating `Admin:BanPepper` voids blocks as it voids bans | Accept | Bounded and documented; identities are never linked by email by design |
| DECISIONS fix | M-1: during the deferral the identity rows still exist, so the `signup_blocks` row only changes the error code (`signup_blocked` instead of `account_deleted`); the block is defence in depth | Accept | Keep the block rows meaningful if identities are ever deleted at request time; privacy text describes a hash that does exist |
| DECISIONS fix | M-3: a fresh identity with the same email (email code after deleting an Apple/Google account, or the reverse) is not blocked | Accept / Owner | Identities are never linked by email (threat model A-13 residual) |
| DECISIONS fix | M-8: the email code is consumed before `signup_blocked` is answered | Accept | Same as `account_deleted`/`account_suspended`; nothing leaks before the code verifies |
| DECISIONS fix | The code store's 5 codes/h per email (every platform, any address) lets someone who floods requests for a victim's email delay the victim's code by up to an hour | Accept | Pre-existing mail-bombing guard (A4); the unattested limiter itself is keyed per (address, email) so it adds no cross-address lockout |
| DECISIONS fix | Unattested `create_work` is charged in the middleware before the body is validated (per account, after sign-in) | Accept | A signed-in account only spends its own budget |
| DECISIONS | Only `ssoSignIn` and `emailVerify` list their 403 codes (`SignInForbidden`); other operations still use the generic `Forbidden` (FINALAPI M6) | Later (contract) | Same as M6 |
| C1 | Unused role assignments; hand-maintained settings list; Postgres CA trust and omitted-password behaviour; no one-time warning when `TrustedProxyHops>0` falls back to the TCP peer (F-C1-4) | Accept / Device | The warning needs a logger in `ClientIpResolver` (constructed in many places); check the rest on first deploy |

### App
| Task | Item | Disposition | Reason |
|---|---|---|---|
| B2 | Requests started during a refresh use the stale token (F-B2-2); `api_providers`/`auth_controller` circular import (F-B2-3) | Accept | The 401 triggers one refresh and retry / no runtime effect |
| B4 | `build()`/`_photoLayer` over 50 lines; `_lastSpotlight` assigned in `build`; no ValueKey reset test; next story goes to feed; no autoplay UI | Accept | Refactor risk in the viewer for no behaviour gain |
| B4 | Photo cover-to-contain fallback per work | Owner | Visual check on device |
| B5 | ArtistSummary lacks city and colour; no total reply count on Work; search/bell placement; header scrolls with list | Owner | Contract/design |
| B6 | Report hides a reply locally only; initials avatars; Shift+Enter not widget-testable; an earlier reply shows Report if `/me` fails | Accept | Server state is authoritative on reload |
| B7 | No profile Share pill (no profile share URL); Chapters stat 0 when omitted; followers and inquiries need sign-in; sender email not prefilled | Owner | Contract/product |
| B7 | Dio drops cancelled responses itself (tests use a fake client) | Accept | Test seam only |
| B8 | Review-mode lockout not reset by resend (F-B8-1); text "G" instead of the official mark; handle availability by GET-404 heuristic; no remaining-attempts count | Accept / Owner (Google mark asset) | Server lock is per email; 409 at submit catches hidden handles |
| B8 | Real Apple/Google SDK wrappers untested (F-B8-2) | Device | Needs client ids and a device |
| B9 | No list-drafts endpoint (no resume; a killed app leaves a draft holding a quota slot up to 24 h); system picker instead of in-app grid; no reorder UI; half-clipped edge pins | Owner | Contract/design |
| B10 | `/me` has no sign-in provider; `handle_change_too_soon` has no date; no GET latest export; several profile fields not editable; client https-only website vs server http | Owner | Contract/product |
| B13 | Domain, entitlements, `assetlinks.json`, host CSP, path URL strategy in a real browser (F-B13-x) | Device / Owner | Checklist in the ledger and `app/web/README.md` |
| B14 | App Attest key per call, no server nonce store, young `app_attest` plugin (F-B14-1) | Owner | Decide before enabling attestation |
| B14 | Native crash scrub and a real scrubbed event (F-B14-2); Android release build unverified | Device | Needs a DSN / device |
| B14 | Reported work stays in the loaded feed until refresh (F-B14-3); tests do not go through generated client paths | Accept | Needs a hidden-ids set across providers |
| GALLERY | Web viewer: custom story controls are not Tab-focusable and show no focus ring (Arrow keys now step chapters) | Later | Rebuild the story chrome, card and pins as focusable buttons with a designed focus ring |
| GALLERY | Desktop-width viewer: full-width card, spotlight can sit under the card | Owner | Handoff designs only the phone frame |
| GALLERY | Story top bar puts the artist on its own line at 390 pt and text scale 1.0 (the S2 120 pt rule; handoff shows one row) | Owner | Visual choice; artist stays readable |
| GALLERY | Onboarding welcome names Apple on Android ("Apple, Google and email") while the sheet offers Google and email | Owner | Wording |
| FINALAPP | RTL text direction only per reply (`replies/bidi.dart`); titles, bios and chapter bodies use the ambient LTR direction (M-9 rest) | Later | US launch; needs a per-field direction rule and checks in story, portfolio and create |
| FINALAPP | Web build ~42 MB: every renderer variant (`chromium/`, `skwasm*`, `wimp*`, `webparagraph/`) and `.symbols` deployed (M-5 rest) | Later | A visitor downloads one bundle (0.9 MB gzip JS + 2-3 MB gzip CanvasKit); pruning variants must be checked per browser first. Real numbers in `app/web/README.md` |
| FINALAPP | Web viewer emoji/CJK glyphs not in the bundled fonts render as boxes (engine fallback fonts from fonts.gstatic.com are blocked by the CSP) | Owner | Privacy vs glyph coverage; allow fonts.gstatic.com in the CSP or bundle fallback fonts |
| FINALAPP | Google SDK sign-out runs only when the SSO client was used in this app run (after a restart the SDK may still remember the account) | Accept | google_sign_in throws before initialize(); the next sign-in shows the account chooser |
| FINALAPP | iOS release check is a script (`scripts/check-release-config.sh`), not an Xcode build phase | Device | Editing the Xcode project without a Mac/Xcode to verify is riskier than the script; add a Run Script phase on the first Mac build |
| FINALAPP | API `Legal/privacy.md` "Retention" wording | Owner (counsel) | Fixed in FINALAPI (7-day backups and recovery copies, 30-day deferral, records kept after deletion); counsel to review the whole Retention section and the periods (DMCA 3 years, audit trail and ban hashes indefinite) |
| GALLERY | Contrast measured from rendered pixels flags 11-13 pt labels (anti-aliasing); specified colours pass | Accept | See a11y-audit.md |

## Log (newest first; one line per event: date, task, result, commit)
- 2026-11-06 E2E-FINAL (8e34f07..HEAD, branch `build/e2e-final`): emulator e2e 25/25 on a fresh DB (first run 24/25: B/C test expected the followed artist in onboarding; test updated, no app/API fix needed); flows L, M, N added; a11y 15 findings (unchanged); Flutter 937 pass, 2 skipped, flutter analyze no issues; no API code touched (scripts only), no dotnet test run; web viewer Playwright pass; install-on-device.md verified on the emulator. NOT pushed: no SSH key in this sandbox.
- 2026-11-04 DECISIONS fix round (f03c007..9652db3 + final docs, branch `build/decisions`): API 1506/1506 (was 1481; +25), one full run 24 min; dotnet build 0 warnings; `scripts/validate-infra.sh` OK; Flutter not run (app/contract untouched). Unattested limits sized for shared addresses and charged after validation, metric added, R-3 wording, M-2/M-4/M-5/M-6/M-7. Pushed.
- 2026-11-03 DECISIONS (fc03982..9faf5db + docs, branch `build/decisions`): API 1481/1481 (was 1444), one full run 21 min; dotnet build 0 warnings; `scripts/validate-infra.sh` OK; Flutter 937 pass (was 935), 2 skipped, flutter analyze no issues; `scripts/gen-client.sh` run (doc comments only). Owner decisions (a) 24 h illegal_content rule, (b) `signup_blocked`, (c) Android-only attestation built; release blocker "Legal review by counsel" recorded. Pushed.
- 2026-10-31 FINALAPI (61cf8cb..fc8ed7b + docs): API 1444/1444 (was 1406), one full run 18 min 27 s; dotnet build 0 warnings; `scripts/validate-infra.sh` OK. No app/, contract/ or generated client touched; no flutter run. Pushed.
- 2026-10-31 FINALAPP (c91fed6..f14ca3c + docs): Flutter 935 pass (was 900), 2 skipped; flutter analyze no issues; gallery 114/114; web viewer release build has no third-party script origin (`scripts/check-web-viewer-origins.sh`); Android release refused without key/real domain, debug and allowed local release build. No api/infra/contract touched, no dotnet run. Pushed.
- 2026-10-31 GALLERY (2d65b4a..71c2e27 + docs): Flutter 900 pass (was 887), flutter analyze no issues, gallery 64/64 and audit 50/50 (`--tags gallery --run-skipped`), e2e 18/18 on the emulator, web viewer checked with Playwright. No API code touched. Pushed.
- 2026-10-31 CLEANUP (77fa745..18c5700 + docs): API 1406/1406 (was 1389), Flutter 887 pass (was 878), flutter analyze no issues, dotnet build 0 warnings. Deferred minors consolidated above. Pushed.
- 2026-10-31 S2 review fixes (399497f..51d7f26): API 1389/1389 (was 1335), Flutter 878 pass, flutter analyze only the 6 integration_test infos. Owner decision open: I-5 illegal_content instant hide (threat-model section 7). Pushed.
- 2026-10-31 S2 complete (5493cf0..7b13e4e): API 1335/1335, Flutter 853 pass, flutter analyze clean outside integration_test infos. Pushed.
- 2026-10-23 M6 reached: B6-B14 complete. 773 app tests pass, web build ok. Pushed.
- 2026-10-23 M5 reached: B1-B5 complete. 214 app tests pass. Pushed.
- 2026-10-23 M4 reached: A14b, A15, A16 complete (fix commits d1cf76b, b8b648a, 13b2e1d). 926+ API tests pass. Pushed.
- 2026-10-23 A15 complete (4be2c1c, 534a1b2, fix b8b648a). 905 API tests pass. Not yet pushed.
- 2026-10-23 A14 complete: A14b (e093620, bda3613, 07a0fd6, a77af4b, fix d1cf76b). 771 API tests pass. Not yet pushed (M4 pending A15, A16).
- 2026-10-22 M3 reached: A7-A13 complete (e631618, 1f75178, 811af34, 686f382, ff35eb9, 1c5df5e, 850a17f). Pushed.
- 2026-10-22 M2 reached: A1-A6 complete (56fb231, d28b26e, a2277e1, 1fdcd5c, 1d81d0f, ccb8aef). Pushed.
- 2026-10-22 M1 reached: T1 (a8dca9b), T2 (503a45a). Pushed branch build/mvp.
- 2026-10-22 Design approved, plan written, progress file created. No code yet.
