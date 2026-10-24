# Security review findings

Gate S1 (branch `build/mvp`, base `d0a4d86`). Two parts: **S1a** (this document's author: tests, CI, documents, triage) and
**S1b** (the independent `security-reviewer` agent pass over `Auth/`, `Images/`, `Admin/`, `Account/`, `Abuse/`, filled in by the
controller at the end). Companion documents: `threat-model.md`, `asvs-masvs-checklist.md`.

Disposition vocabulary: **Fixed** (change plus a test), **Accept** (reason given), **Owner** (a decision or account only the owner or
counsel can supply), **Device** (needs a physical device, a deployed environment or a real third-party account), **Open** (not done
here; the next task or the reason is stated), **Closed** (already resolved by a later task; evidence given).

## 1. What S1a found by testing

The tests were written first and run against the unmodified code (RED output kept with the task report). Everything below was
found by them or while reading code to write them. Items marked "real" were observable failures; the rest are hardening.

| ID | Finding | Severity | Disposition |
|---|---|---|---|
| S1-1 | **The API sent no CORS headers at all**, yet the web viewer (own origin, documented in `app/web/README.md`) reads the public endpoints from the browser. Every viewer call would be blocked by the browser. Real | High (feature broken) | Fixed: `Common/CorsSetup.cs`, exactly the configured viewer origin, GET/HEAD/OPTIONS, no credentials, no wildcard, not on `/admin`; `Sec/HeadersAndCorsTests.cs` |
| S1-2 | **No `Strict-Transport-Security` and no `X-Content-Type-Options` on API JSON** (only the HTML pages had nosniff). Real | Medium | Fixed: `Common/SecurityHeaders.cs` on every response, HSTS outside Development/Test (`Security:Hsts` overrides); tests |
| S1-3 | **Refresh-token replay race**: two simultaneous uses of one refresh token could leave the winner's new token live in a session the loser had just revoked (reproduced in 1 of 2 rounds). Real. Ledger A2 | Medium | Fixed: claim, issue and family revoke serialised by a per-family advisory lock in one transaction (`Auth/TokenService.cs`); `Sec/HardeningTests.Racing_replays_...` |
| S1-4 | **Android release build could not reach the network**: the Flutter template declares `INTERNET` only for debug and profile. Also cloud backup was on and cleartext not explicitly forbidden. Real | High (release blocker) | Fixed: `AndroidManifest.xml`; `app/test/security/platform_config_test.dart` (4 RED, 8 GREEN) |
| S1-5 | SAS URLs allowed plain http | Low | Fixed: `Protocol = Https` whenever the account endpoint is https (`Images/AzureBlobStore.cs`); `Sec/UploadUrlTests.cs` (also pins: one blob, `cw` only, 15 minutes, read URLs `r` only) |
| S1-6 | Signed-in reads and every `/admin` request bypassed the rate limiter (ledger A15) | Medium | Fixed: `user-read` 600/min per user; `/admin` limited per address like any page; tests |
| S1-7 | Email domains with look-alike separators or letters (U+3002, Cyrillic, full-width) passed `Normalize` and so could dodge the disposable-domain list (ledger A15) | Medium | Fixed: non-ASCII domains refused (punycode still works); tests |
| S1-8 | The public DMCA form mailed an acknowledgement to any claimant address each time: a mail-bomb relay (ledger A14b) | Medium | Fixed: at most 3 acknowledgements per address per day; notices are still stored; test |
| S1-9 | Ban pepper not required when `Admin:Enabled=false`: earlier bans silently stopped applying, as they also would after a pepper change (ledger A14a) | Medium | Fixed: 32+ character pepper required outside Development/Test whatever the admin switch says (`Admin/AdminSettings.cs`). **Deployment note**: set `Admin__BanPepper` even with the pages off; never rotate it without re-hashing the ban table |
| S1-10 | A copied admin cookie stayed valid for up to 8 hours after sign-out; admin sign-in and sign-out were not audited (ledger A14a) | Medium | Fixed: one live session per admin (`users.admin_session_stamp`, migration `AdminSessionStamp`), sign-in/out written to `admin_actions`; tests |
| S1-11 | Bidirectional override/isolate characters and U+2028/2029 were accepted in replies, inquiries, bios, display names, titles and DMCA text (ledger A10, A12) | Low | Fixed: refused by `Common/UnsafeText.cs`; right-to-left text and the marks U+200E/F still accepted; tests |
| S1-12 | JWKS: a failed fetch started no cool-down (an outage hit the provider on every sign-in), one global lock served both providers, a duplicated `kid` threw (ledger A3) | Medium | Fixed: cool-down on failure, lock per URI, last duplicate wins; 3 tests |
| S1-13 | `email_codes` and `verify_attempts` (raw email, IP) were never deleted (ledger A4) | Medium (privacy) | Fixed: `Auth/AuthDataRetentionJob.cs`, hourly, 24-hour retention; test |
| S1-14 | Admin sign-in took visibly longer for allow-listed addresses (ledger A14a) | Low | Fixed: every answer padded to `Admin:LoginMinimumMs` (default 600 ms); test |
| S1-15 | A failed delete of the raw avatar upload turned a finished avatar into a 500; `Storage:Containers:Originals`/`Exports` could be set to a public container (ledger A7) | Low | Fixed: failure swallowed and logged; startup refuses; 5 unit tests |
| S1-16 | 401 responses lacked `WWW-Authenticate: Bearer` (ledger A5); `nbf` was not checked by the custom lifetime validator (ledger A2); Kestrel sent a `Server` banner | Low | Fixed: header added, `nbf` checked (a not-yet-valid token was already rejected by the handler; the matrix proves it), banner off |
| S1-17 | Content Safety client used SDK defaults (3 retries, 100 s timeout) inside the reply request | Low | Fixed by configuration (2 retries, 10 s): no test, because the SDK offers no seam; the fail-open path is covered by `Social/ReplyTests.cs` |
| S1-18 | `.gitignore` did not exclude key material; CI actions were pinned to tags; the workflows lacked `permissions`; no secret or dependency scanning ran in CI | Medium | Fixed: `.gitignore`, SHA pins (resolved with `git ls-remote`), `permissions: contents: read`, `.github/workflows/security.yml`, `.gitleaks.toml`, `RepoSecretScanTests` |

Checked and found sound (no change needed): every one of the 37 contract operations has an explicit decision in
`Sec/AuthorizationMatrixTests.cs` and obeys it; 10 forged-token shapes (garbage, `alg:none`, wrong key, wrong audience or issuer,
expired, not yet valid, HS512, no expiry) are refused on every non-public operation; owner-only operations return 403/404 to another
user and leave the data untouched; suspended and deleted accounts get 403 `account_suspended`/`account_deleted` on every
non-public operation; every admin route and POST handler refuses anonymous callers, API users and ex-admins; public responses carry
no login email, provider subject, hash or non-whitelisted id; errors leak no stack or path; the 1 MB body cap and the write limiter
cover every write operation; mass-assignment fields are ignored; Apple/Google token verification rejects `alg:none`, HS256-with-public-key
and attacker-supplied key URLs (`Sec/OidcAlgorithmConfusionTests.cs`).

## 2. Findings from the per-task reviews (A1 to C1) and how each ended

Every task was reviewed against its own brief by a fresh reviewer; "round" counts follow the ledger. Only security-relevant items
are summarised.

| Task | Security-relevant finding | Resolution |
|---|---|---|
| A1 schema | Dev DB password fallback in code; missing chapter CHECKs; `Reports.ReporterId` cascade would erase moderation records | Fixed in round 1: startup throws outside Development; CHECKs added; `SetNull` chosen (A13 scrubs the reporter's text) |
| A2 tokens | Real bearer configuration was untested | Fixed: `Auth/BearerValidationTests.cs`; further hardened at S1 (S1-3, S1-16) |
| A3 OIDC | No test that an unconfigured audience fails closed | Fixed: round 1 |
| A4 email codes | Review mode had a fixed code with no brute-force limit; no per-IP verify limit | Fixed: per-email lock (5 failures/hour), per-IP verify limit, startup warning. Accepted: an attacker can lock reviewers out for an hour |
| A5 profiles | Committed Dart client out of date after a contract change | Fixed; standing rule: regenerate with `scripts/gen-client.sh` after every contract change |
| A6 safety | Report target visibility (hidden or unpublished content could be reported by id, confirming it exists); vacuous block test | Fixed: round 1 |
| A7 images | **Originals with GPS in public containers** | Fixed: private `originals` container; avatar original deleted after success. Reviewer and the automated scan agreed |
| A8 publish | `deleteWork` answered 200 vs 404 differently for existing and missing ids (existence leak) | Fixed: round 1 |
| A9, A10, A11, A12 | No Important findings | Deferred items triaged below |
| A13 deletion/export | Concurrent sweeps; mid-transaction rollback; a row lock held across blob/CDN/Apple calls; late-work blob cleanup not resumable | Fixed over two rounds (lease before IO, resumable late-work pass) |
| A14a admin | JWT state check with cache and eviction; `IsAdmin` reconcile with the allow-list; takedown ordering and `TakedownReconciler` | Fixed in round 1 |
| A14b evidence/DMCA | Second counter-notice answered with a bare 429 | Ruling: 409 `counter_notice_exists`; reviewed twice |
| A15 abuse | Attestation default on only outside Development/Test; startup refuses without verifier config | Ruling recorded; remaining items triaged below |
| A16 telemetry | Console logs unscrubbed (audited clean); Sentry `BeforeSend` failure drops the event | Accepted; see F-A16-x |
| B2 to B14 (app) | Token refresh logic, secure storage, deep links, URL launching (https only, store requirement), Sentry scrubbing, attestation timeouts, crash scrubbing | Fixed in their review rounds; device checks listed in section 3 |
| C1 infra | Actions pinned to tags; Data Protection keys unencrypted; app uses the DB admin login; allow-Azure firewall | Tags: fixed at S1 (S1-18). The rest: Owner (R-7, R-16) |

## 3. Deferred and accepted risks: every deferred minor in the ledger, triaged

IDs are `F-<task>-<n>`; the order follows the ledger. "Fixed (S1-n)" points at section 1.

### T1 to A5

| ID | Item | Disposition | Reason / evidence |
|---|---|---|---|
| F-T1-1 | Per-file `Results` alias | Closed | Folded into T2 |
| F-T1-2 | CI: permissions, SHA pins, coverage, path triggers | Fixed (S1-18) | `permissions`, pins, `contract/**` and `scripts/**` triggers; coverage is now collected (`--collect` / `--coverage`); a hard 80% gate is Owner's call |
| F-T1-3 | pubspec description and README boilerplate | Fixed | `app/pubspec.yaml` description; README already project-specific |
| F-T2-1 | Length limits missing from the contract | Open (cleanup task) | Server enforces them (`*Validator.cs`); contract-only change plus regeneration |
| F-T2-2 | Whitespace-only inquiry passes `minLength` | Accept | Server trims and refuses; contract wording only |
| F-T2-3 | Inquiry sender email must stay out of logs | Closed | Telemetry tests and `Inquiries/InquiryTests.cs` assert it |
| F-A1-1 | DB CHECKs for handle, bio, status values, hidden consistency; FKs for notifications; Inquiries indexes; cascade-metadata test; null-zoom tests | Accept | Values are written only by validated code and enum converters; a CHECK adds no defence against an external attacker. Cheap pre-launch hardening if a migration is wanted anyway |
| F-A2-1 | Replay race leaves a live token | Fixed (S1-3) | |
| F-A2-2 | `LifetimeValidator` ignores `notBefore` | Fixed (S1-16) | |
| F-A2-3 | 401 / framework 400s not enveloped | Accept | 401 is enveloped (A5); framework 400s leak nothing (`PublicExposureTests` probes) |
| F-A2-4 | Suspension must call `RevokeAllAsync` | Closed | `Admin/UserStateTests.cs`, `Sec/AuthorizationMatrixTests.A_suspended_accounts_refresh_token_is_refused_...` |
| F-A2-5 | Bearer tests call the handler, not the middleware | Closed | The matrix goes over HTTP with ten forged-token shapes |
| F-A3-1..3 | JWKS cool-down, global gate, duplicate `kid` | Fixed (S1-12) | |
| F-A3-4 | No explicit `alg:none` / HS256-public-key tests | Fixed | `Sec/OidcAlgorithmConfusionTests.cs` |
| F-A3-5, F-A3-6 | Duplicate assertion; blank configured audience | Accept | Cosmetic / theoretical (blank audiences are filtered and fail closed) |
| F-A3-7 | No reason-code logging for verification failures | Accept | Privacy first; a metric can be added if sign-in failures need diagnosing |
| F-A3-8 | Suspended-identity hash is A14 | Closed | `Admin/BanTests.cs` |
| F-A4-1 | Verify timing differs for unknown and known email | Accept (R-12) | Responses are identical and probing is rate limited |
| F-A4-2, F-A4-4 | Thin boundary tests; no log assertion for send failures | Accept | Behaviour is covered; asserts are cosmetic |
| F-A4-3 | No test for `AccountDeletedException` mapping | Accept | Mapping is covered at the token level (`Sec/AuthorizationMatrixTests`) |
| F-A4-5 | A request is not one transaction | Accept | A code row without a sent mail is harmless |
| F-A4-6 | `email_codes` / `verify_attempts` never pruned | Fixed (S1-13) | |
| F-A4-7 | Review emails skip the DB, so timing reveals the allow-list | Accept | Review mode is off after app review and logs a startup warning |
| F-A4-8, F-A4-9 | Per-IP count-then-insert not atomic; 30/IP/hour can lock a shared NAT | Accept / Owner | Soft limit by design; the number is Owner's to tune after real traffic |
| F-A4-10 | Unused `levels` parameter | Accept | Test helper |
| F-A5-1 | `WWW-Authenticate` missing | Fixed (S1-16) | |
| F-A5-2, F-A5-3 | Boundary tests; handle timing at 29d23h | Accept | Cosmetic test gaps |
| F-A5-4 | Onboarding double-submit race; fragile constraint-name match | Accept | Outcome is a 409; fragility only matters on a rename |
| F-A5-5 | Suggested-artists test reads shared DB (flaky) | Open (follow-up task before final review) | Test hygiene, not security |
| F-A5-6 | Follower counts include suspended/deleted users | Owner | Product decision |
| F-A5-7 | More than 50 `followArtistIds` rejected rather than truncated | Accept | Documented behaviour |

### A6 to A12

| ID | Item | Disposition | Reason / evidence |
|---|---|---|---|
| F-A6-1 | Follow/block race (carry to A9) | Closed | A9 rejects follows across a block under a pair lock; `Social/FollowTests.cs` |
| F-A6-2 | Daily report limit not serialised | Accept | Throttle only; a concurrent burst can exceed it by a few |
| F-A6-3 | `ChangeTracker.Clear()` in the auto-flag catch | Accept | Error path only |
| F-A6-4, F-A6-6, F-A6-8 | Untested races, boundaries, undropped test DBs | Accept | Test hygiene |
| F-A6-5 | Report note scrub claimed in a comment | Closed | `Account/DeletionJob.cs` scrubs `Report.Note` |
| F-A6-7 | Report-hide overrides `HiddenBy` for any non-admin hider | Accept | An admin hide is never overridden (`ReportService`); an auto-hidden reply becomes report-hidden, and either way an admin decides |
| F-A7-1 | Cleanup deletes blobs before re-checking the row | Accept | A draft expiring at the instant of publish fails the publish, which the pipeline handles |
| F-A7-2 | Cleanup starvation by 100 failing drafts | Accept | Needs 100 permanently failing deletions |
| F-A7-3 | SAS cannot cap upload size | Accept | 25 MB read cap and 60 MP cap before decode (`Images/VariantGenerator.cs`); storage lifecycle for stale originals is an ops item |
| F-A7-4 | Memory per decode at 60 MP | Accept (R-11) | Semaphore; revisit with load data |
| F-A7-5, F-A7-6 | Partial variant sets; no sRGB conversion | Accept | A failed publish is retried; colour is cosmetic |
| F-A7-7 | SAS start time and https-only | Fixed (S1-5) | Start time not needed |
| F-A7-8 | SAS mixes wall clock and `TimeProvider` | Accept | Azure checks wall-clock time, so wall clock is correct |
| F-A7-9, F-A7-10, F-A7-11 | `EnsureContainers` per write; fixtures; quota overlap test | Accept | Performance and test breadth |
| F-A7-12, F-A7-13 | Originals guard; avatar delete failure | Fixed (S1-15) | |
| F-A7-14 | Carry: CDN must front only works/avatars; lifecycle for stale originals | Device | Deploy-time check (CDN rules). Lifecycle: originals are deliberately never aged out (`infra/modules/storage.bicep`); orphans are removed by the draft job and the deletion sweep |
| F-A8-1 | Throwing handler can starve the publish queue (carry to A9/A10) | Accept | Handlers do not throw by construction; the work row carries `PublishFailures` |
| F-A8-2 | Missing tests; `\r` allowed; non-JSON content type not enveloped; orphan variants on a failed save | Accept | Cosmetic / orphan GC; ASVS 13.1.5 is recorded as Not met |
| F-A8-3 | Content Safety call has no cancellation or timeout | Fixed (S1-17) | |
| F-A8-4 | Chapter fields lack `maxLength` in the contract | Open (cleanup task) | Same as F-T2-1 |
| F-A8-5 | Published stories are not editable | Owner | Feature decision (edit with re-scan) |
| F-A9-1, F-A9-2, F-A9-5 | Cursor replay after follow change; feed returns full works; test gaps | Accept | Rare / performance follow-up |
| F-A9-3 | Failing work retries every 3 minutes with no cap | Accept | `PublishFailures` is recorded; a cap is a tuning choice |
| F-A9-4 | No explicit body limit | Closed | A15 caps every body at 1 MB |
| F-A10-1 | No JSON body cap | Closed | A15 (`Abuse/RequestLimitsMiddleware.cs`) |
| F-A10-2 | Bidi/format control characters | Fixed (S1-11) | |
| F-A10-3, F-A10-4, F-A10-6 | Untested Retry-After, rollbacks; per-IP inquiry unserialised | Accept | |
| F-A10-5 | No retention for inquiries and notifications | Owner (R-13) | Needs a product/counsel period; then one job like `AuthDataRetentionJob` |
| F-A10-7 | Carry: Postgres `azure.extensions` allow-list | Closed | `infra/modules/postgres.bicep` |
| F-A11-1 | Hostile-input search test weak; six-plus terms; word-similarity cannot use GIN | Accept | Performance revisit with data |
| F-A11-2 | Cursor unsigned | Accept | It reveals only a timestamp and an id the caller can already see |
| F-A11-3 | Test DB never dropped; combined mutation check | Accept | |
| F-A12-1 | Share cache 60 s and purge on takedown | Closed | Done in A14a |
| F-A12-2 | 404 byte-equality lacks header comparison; regex escaping tests; markdown script test | Accept | `Sec/HeadersAndCorsTests` asserts no `<script` on legal pages |
| F-A12-3 | No POST/405 tests; well-known lacks nosniff/HEAD | Fixed / Accept | nosniff now global (S1-2); POST/405 probed in `PublicExposureTests`; HEAD on well-known is cosmetic |
| F-A12-4 | Bidi overrides not stripped from titles | Fixed (S1-11) | |
| F-A12-5 | Apple requires revoking Sign in with Apple tokens at account deletion | Owner | Needs the Apple account; `IAppleTokenRevoker` is a logging no-op |

### A13 to A16

| ID | Item | Disposition | Reason / evidence |
|---|---|---|---|
| F-A13-1 | Dead `ContractStubs` test and stale 501 responses in the contract | Open (cleanup task) | |
| F-A13-2 | Deleted user's token works for up to 15 minutes | Closed | A14a state check; `Admin/UserStateTests.cs` |
| F-A13-3 | Orphan blobs from SAS URLs used after hard delete | Device | Ops: periodic orphan scan or lifecycle rule; bounded by the 15-minute URL life |
| F-A13-4 | `follows.json` lists suspended/deleted followers | Accept | Only public handles |
| F-A13-5, F-A13-6, F-A13-7 | Completeness-guard blind spots; lease not renewed; archive cleanup | Accept | Overlap is safe; documented |
| F-A14a-1 | Admin login timing | Fixed (S1-14) | |
| F-A14a-2 | Logout only clears the cookie | Fixed (S1-10) | |
| F-A14a-3 | Admin login/grant not audited; failed actions not logged; audit table not DB-immutable | Fixed in part (S1-10); Open: immutability | Sign-in/out now audited. Immutability: at deploy give the app's database role `INSERT, SELECT` only on `admin_actions` (needs the non-admin DB role from R-7); failed-action logging is Accept |
| F-A14a-4 | Config fail-open: `Admin:Enabled=false` with bans, pepper rotation, `IBanList` optional, no warning | Fixed (S1-9) | `IBanList` is registered unconditionally in `AddAdmin`; `Admin/BanTests` run through the real host |
| F-A14a-5 | Dashboard thumbnails fetch full-size originals every 60 s | Accept | Admin-only cost |
| F-A14a-6 | Default-deny test only inspected `/admin` routes | Fixed | `Sec/AdminRouteMatrixTests.cs` enumerates every endpoint outside the contract |
| F-A14a-7 | Data Protection keys in multi-instance | Closed / Owner | Stored in blob (C1); not encrypted (R-16) |
| F-A14a-8 | Dismiss form lacks a reason field | Accept | UX; the action is audited |
| F-A14a-9, F-A14a-10 | State-cache repopulation race; reconciler row lock across IO | Accept | At most one TTL / twenty rows |
| F-A14a-11 | Client must treat 403 `account_deleted` on repeat delete as success | Closed | `app/lib/features/settings/delete_account_flow.dart` |
| F-A14b-1 | NCMEC idempotency key | Fixed (documented) | `INcmecReporter` contract: `hold.Id` is the key; the fake derives its id from it (`NcmecTests`) |
| F-A14b-2 | Per-email cap on DMCA acks; null-IP limiter case | Fixed (S1-8) / Accept | The resolver always yields an address |
| F-A14b-3 | Audit when an admin views evidence | Owner (counsel) | Retention vs. access-log tension |
| F-A14b-4 | Forward-notice stuck state needs manual repair | Accept | Rare crash window; runbook line in `docs/deploy.md` is Owner's |
| F-A14b-5, F-A14b-6 | Real NCMEC integration; DMCA/owner email wording and 10-day period | Owner | ESP account and counsel |
| F-A15-1 | Play Integrity server nonce | Owner | Left as instructed (decision before enabling attestation) |
| F-A15-2 | App Attest counter and replay cache | Owner | Same decision (B14 note) |
| F-A15-3 | Signed-in reads and `/admin` unlimited | Fixed (S1-6) | |
| F-A15-4 | IPv6 /64 partitioning | Closed | `Abuse/IpKey.cs` already groups by /64 |
| F-A15-5 | In-memory limiters per instance | Accept (R-4) | |
| F-A15-6 | Non-ASCII email domains | Fixed (S1-7) | Refused rather than normalised |
| F-A16-1..3 | No stack when Sentry is off; console logs unscrubbed; admin page 500 returns the JSON envelope | Accept | Console audited clean; envelope leaks nothing |
| F-A16-4 | Sentry `BeforeSend` failure drops the event | Device (R-10) | Verified from SDK source only; confirm with a real DSN |

### B1 to B14 (app) and C1 (infra)

| ID | Item | Disposition | Reason / evidence |
|---|---|---|---|
| F-B2-1 | `FormData` retry cannot resend a consumed body | Closed | B9 builds the body per attempt (`app/test/features/`) |
| F-B2-2 | Requests during a refresh go out with the stale token | Accept | The 401 triggers one refresh and a retry |
| F-B2-3 | `api_providers` / `auth_controller` circular import | Accept | No runtime effect |
| F-B4-x | Long `build()`; spotlight assigned in `build`; photo cover/contain fallback; no autoplay UI | Accept / Owner | Cover/contain is a visual decision (ledger) |
| F-B5-x | Contract: follow idempotency text; artist city and colour; reply total; icon placement | Open (cleanup) / Owner | Not security |
| F-B6-x | Report hides reply locally only; initials avatars; Shift+Enter untestable | Accept | |
| F-B7-x | Website row copies the URL; no profile share; chapters stat; 'Followed by' sign-in; sender email not prefilled | Owner | Product |
| F-B8-1 | Review-mode lockout does not reset on client resend | Accept | Server lock is per email per hour |
| F-B8-2 | Real Apple/Google SDK wrappers untested | Device | Needs the owner's client ids and a device |
| F-B8-3 | Until B14 sends `X-Attestation`, production sign-in returns 403 | Closed | B14 sends it; enabling attestation remains an Owner decision |
| F-B9-1 | A cancel racing a publish is guarded by a client check only | Accept | Server deletion of a pending story is safe (the pipeline finds no row) |
| F-B9-2 | Metadata stripping of upright PNG/WebP is segment cutting | Closed (server side) | The public files are always re-encoded without metadata: `Images/VariantTests.cs` feeds a JPEG with EXIF and a GPS IFD and asserts no `Exif` bytes survive in any variant; a 64-megapixel decompression bomb is refused in the same file. Client stripping is defence in depth only |
| F-B10-x, F-B11-x | `/me` shows no provider; no date on `handle_change_too_soon`; no GET latest export; fields not editable; https-only website | Owner | Contract/product |
| F-B13-x | Domain, entitlements, `assetlinks.json`, CSP at the host, rename checklist | Device / Owner | Checklist in the ledger and `app/web/README.md` |
| F-B14-1 | App Attest creates a key per call; no server nonce store; `app_attest` young | Owner | Decision before enabling attestation in production |
| F-B14-2 | Native crash scrub; a real event has no user/email | Device (R-10) | |
| F-B14-3 | Reported work stays in the loaded feed until refresh | Accept | |
| F-C1-1 | Actions pinned to tags | Fixed (S1-18) | |
| F-C1-2 | Data Protection keys unencrypted | Owner (R-16) | Wrap with Key Vault |
| F-C1-3 | App connects as DB admin; allow-Azure firewall | Owner (R-7) | First deploy |
| F-C1-4 | Unused role assignments; hand-maintained settings list; Postgres CA trust; one-time XFF warning | Accept / Device | Check CA trust and omitted-password behaviour on the first real deploy |
| F-C1-5 | Authenticated Origin Pulls; RG-scoped principal; orphan originals after deletion sweep | Owner | Deploy-time |

## 4. Residual-risk summary

The high residuals are legal and operational, not code: the scan fails open and has no known-CSAM hash matching, the NCMEC
reporter is a stub until the owner has an ESP account, and an external penetration test has not been done. See `threat-model.md`
section 6 for R-1 to R-17.

## 5. S1b: security-reviewer agent findings

_To be filled in by the controller after the `security-reviewer` agent pass over `Auth/`, `Images/`, `Admin/`, `Account/`, `Abuse/`.
For each finding record: ID (S1b-n), severity, location, description, disposition (Fixed with commit, Accept with reason, Owner, Device),
and the test that pins the fix._

| ID | Severity | Location | Finding | Disposition | Evidence |
|---|---|---|---|---|---|
| S1b-1 | | | | | |
