# Vela threat model

Status: written for the S1 security review gate (branch `build/mvp`). It describes the system as built, not as hoped.
Anything marked **owner** or **device** cannot be settled from the repository; see `review-findings.md` for the open list and
`asvs-masvs-checklist.md` for the per-requirement status. "Vela" is a code name; nothing here depends on the final name.

Scope: Flutter app (iOS, Android), Flutter web story viewer, .NET API on Azure Container Apps, PostgreSQL, Blob Storage behind
a CDN, Azure Communication Services (ACS) email, Azure AI Content Safety, Cloudflare, Sentry and Log Analytics, GitHub Actions CI/CD.
Out of scope: Apple, Google, Azure and Cloudflare platform security themselves; the owner's laptop and accounts (listed as an
insider/owner risk below).

## 1. What we protect (assets)

| Asset | Where it lives | Why it matters | Worst case |
|---|---|---|---|
| Accounts | `users`, `identities`, `profiles` (Postgres) | Sign-in identity (Apple/Google subject, or login email) and the public profile | Account takeover; impersonation of an artist |
| Session tokens | 15-minute JWT access token, 30-day rotating refresh token (stored as SHA-256 hash in `refresh_tokens`); on device in the OS secure store | Bearer of either acts as the user | Silent long-lived access to one account |
| Login email addresses | `identities.email`, `email_codes`, `inquiries.sender_email` (the *sender's* address) | PII. The artist's login email is never returned by any endpoint | Mass email leak; doxxing of an artist |
| Original photographs | Private `originals` container (EXIF incl. GPS kept) | Exact location of where a photographer lives or was; unreleased work | Stalking; theft of full-resolution work |
| Public variants | `works` and `avatars` containers via CDN (re-encoded WebP, no metadata) | Public by design | Hotlinking, scraping (accepted) |
| Evidence for illegal content | Private `evidence` container + `evidence_holds` | Legal hold of at least one year (18 U.S.C. 2258A); the images are CSAM-suspect | Destruction (legal exposure) or leak (severe harm to victims) |
| Data exports | Private `exports` container, short-lived read URL | Everything a user has stored | Leak of one user's complete data |
| Admin capability | `/admin` pages, `users.is_admin`, allow-list in config | Can remove content, ban users, view evidence, file NCMEC reports | Wrongful bans; evidence exposure; abuse of takedown |
| Moderation record | `reports`, `admin_actions`, `banned_identities` (hash only), `dmca_notices` | Audit trail; legal notices carry claimant PII | Tampering hides abuse; bans stop applying |
| Secrets | Key Vault references and Container Apps secrets: JWT signing key, email-code pepper, ban pepper, ACS key, DB password, storage key, Cloudflare token, Sentry DSN | Compromise breaks everything above | Forge any token; read all data |
| Availability and budget | Scale-to-zero Container Apps, daily Log Analytics cap, Azure budget alert | A solo owner pays the bill | Bill shock via abuse; outage |

## 2. Who might attack (actors)

| Actor | Capability | Trust |
|---|---|---|
| Anonymous visitor | Reads public endpoints, share pages and CDN; can call sign-in and the public DMCA form | None |
| Bot / scraper | Anonymous at scale, rotating IPs | None; rate limits and Cloudflare are the controls |
| Signed-in user | Own account, own works; reads public data; writes replies, reports, follows, inquiries | Authenticated, not trusted with other users' data |
| Malicious user | As above, plus crafted uploads and text, many accounts | Treated as hostile in every validator |
| Banned / suspended user | Holds old tokens, tries re-sign-up with the same or a new identity | Tokens rejected at once; same identity refused (HMAC hash) |
| Uploader escaping review | Deletes the account while content is under review, then signs up again with the same identity | Hard delete deferred up to 30 days; owner decision 2026-11-03: the identity hash is blocked (403 `signup_blocked`) until the review closes, at most 30 days (`signup_blocks`) |
| Rights-holder / claimant | Public DMCA form; may be a fraudster using it to remove rivals' work or to mail-bomb an address | Notice is stored, never auto-actioned |
| Admin | Allow-listed email, cookie session, can act on content and users | Trusted but audited; compromised-admin is a modelled threat |
| Insider / the owner | Cloud and repository access, secrets | Trusted; one person means no separation of duties (residual risk) |
| Cloudflare, Azure, GitHub, Sentry, Apple, Google | Sit in the data path or issue identity | Trusted platforms; their failure modes are listed per component |
| Network attacker | On public Wi-Fi, can intercept or tamper | Defeated by TLS; pinning is not used (see MASVS-NETWORK) |
| Device thief / malware | Local access to the phone | Out of reach beyond the OS secure store |

## 3. Components and trust boundaries

```
 [Flutter app] --TLS--> [Cloudflare WAF/CDN] --TLS--> [Container Apps ingress] --> [API (.NET)] --> [PostgreSQL]
 [Web viewer]  --TLS--/                                                                  |--> [Blob: originals, evidence, exports (private)]
 [Browser: share page /s/{id}, /legal/*, /admin] ----------------------------------------|--> [Blob: works, avatars (public via CDN)]
 [Flutter app] --PUT, SAS URL--> [Blob (originals)]                                        |--> [ACS Email]   [Content Safety]   [NCMEC (seam, fake)]
                                                                                           |--> [Sentry]      [Log Analytics]
 [GitHub Actions] --OIDC--> [Azure what-if/deploy]  (manual dispatch only)
```

Trust boundaries (each is crossed by untrusted input and is where validation lives):

1. **Internet to Cloudflare to API.** Everything arriving is hostile. Controls: rate limits, 1 MB body cap, attestation, CORS, security headers.
2. **App to API.** The app is not trusted (a modified client is possible). Controls: server-side validation of every field and ownership check on every id (`tests/.../Security/AuthorizationMatrixTests.cs`).
3. **Client to Blob (direct upload).** The API issues a single-blob, write-only SAS valid for 15 minutes at most; the blob is then untrusted data that the API decodes defensively (`Images/VariantGenerator.cs`).
4. **API to Postgres.** Parameterised EF Core queries only; raw SQL is limited to advisory locks and `FOR UPDATE` with interpolated (parameterised) values.
5. **API to third parties** (ACS, Content Safety, Apple/Google JWKS, Cloudflare purge, NCMEC seam). Outbound only; responses are never trusted for authorization.
6. **Browser to `/admin`.** A separate cookie authentication scheme, separate policy, CSRF tokens, default-deny page convention.
7. **CI to Azure.** OIDC federated identity, no stored secret; deploy needs a manual dispatch and a protected environment.

## 4. STRIDE per component

Mitigation pointers name the code, then the test that pins it. "Residual" points to section 6.

### 4.1 Flutter app (iOS, Android)

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | Attacker replays or forges an attestation to pass as the real app | Play Integrity verified server-side and required on Android requests in production (`Abuse/PlayIntegrityVerifier.cs`, `AttestationMiddleware.cs`), fail-closed; App Attest code exists (`AppAttestVerifier.cs`) but is off at launch (owner decision 2026-11-03) | R-3: no server nonce store, token replayable for 5-10 minutes; iOS requests are unattested |
| S/E | Bot claims `X-Platform: ios` (or drops/garbles the header) to skip Play Integrity | Only exactly one `X-Platform: ios` selects the iOS rule; anything else gets the Android rule; a token is verified only for the declared platform; every request passing without a verified token is held to the unattested limits (5 email codes/h and 10 sign-ins/h per address, 10 drafts/day per account) on top of the normal limits (`PlatformAttestationTests`) | R-3: an iOS claim is unprovable until App Attest ships, so a bot gets the iOS budget |
| S | Phishing a user into a fake sign-in | Native Apple/Google sheets; email code only (no passwords exist) | none |
| T | Modified client sends crafted requests | Server revalidates everything; contract fixed in `contract/openapi.yaml` | none |
| R | User denies an action | Server-side timestamps, audit log for admin actions | users' own actions are not audited (privacy by design) |
| I | Tokens read from device storage | `flutter_secure_storage` (Keychain, Keystore-backed) in `lib/core/auth/session_store.dart`; Android backup disabled (`AndroidManifest.xml`, test `test/security/platform_config_test.dart`) | rooted/jailbroken devices; needs device check (R-9) |
| I | Crash reports or logs leak PII | Sentry scrubbers on both sides (`lib/core/crash/pii_text.dart`, `Telemetry/SentryScrubber.cs`, `PiiScrubber.cs`) | R-10: scrubbing verified from SDK source and tests, not from a real event |
| I | Photo GPS leaves the phone in the public file | Public variants are re-encoded (no metadata); originals are in a private container; client strips metadata before upload | the private original still holds GPS by design (needed for export and evidence) |
| D | Hostile deep link opens the app on a bad route | `go_router` routes validate ids; custom scheme only carries a story id | none |
| E | A second app claims the `vela://` scheme | Scheme carries only a public story id; https app links are the verified path | negligible |

### 4.2 Web story viewer (Flutter web, Static Web App)

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | Another origin calls the API pretending to be the viewer | CORS allows exactly `Web:ViewerUrl`, reads only, no credentials, never a wildcard (`Common/CorsSetup.cs`, test `HeadersAndCorsTests`) | none |
| T | Content injection on the viewer | Text rendered as Flutter text (no HTML); CSP in `app/web/README.md` (`'wasm-unsafe-eval'` only) | CSP is set by the Static Web App config at deploy: device/owner check (R-8) |
| I | Referrer leaks story ids | `Referrer-Policy: strict-origin-when-cross-origin` documented for the host | owner to apply at deploy |
| D | Scraping | Read endpoints sit under `anon-read` limiter and Cloudflare | R-5: public content is public |

### 4.3 API (.NET on Azure Container Apps)

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | Forged, expired, wrong-audience, `alg:none` bearer token | `Program.cs` JWT validation: HS256 only, issuer, audience, zero clock skew, `exp` required, `nbf` honoured; 401 carries `WWW-Authenticate`; `ForgedTokens` in `AuthorizationMatrixTests` | none |
| S | Forged Apple/Google identity token | `Auth/Identity/OidcIdentityVerifier.cs` (issuer, audience, nonce, signature via JWKS with cool-down); `OidcVerifierTests` | Apple token revocation on deletion not implemented (owner item) |
| S | Email-code guessing / enumeration | 6 digits, hashed (HMAC with pepper), 10 minutes, 5 attempts, per-email and per-IP limits (`EmailCodeService.cs`); identical responses for known and unknown addresses | R-12: small response-time difference for unknown addresses |
| S | Refresh token theft and replay | Rotation, reuse detection revokes the family, claim and revoke serialised per family (`TokenService.cs`, `HardeningTests`) | stolen token used before the owner refreshes (30-day window, detected on the next refresh) |
| T | Mass assignment | Hand-parsed request bodies; privileged fields never read from clients (`MassAssignmentTests`) | none |
| T | SQL injection | EF Core parameters; the full-text search builds a `tsquery` from sanitised terms (`Search/SearchQuery.cs`) | none known |
| R | Admin denies an action | `admin_actions` audit rows for every moderation action, admin sign-in and sign-out | R-6: table not DB-immutable |
| I | Another user's data via ids (IDOR) | Ownership enforced in each service; every id-in-path operation tested against a second user (`AuthorizationMatrixTests`) | none known |
| I | Errors leak internals | Exception middleware returns a fixed envelope, logs the type name only (`Telemetry/ExceptionHandling.cs`; `PublicExposureTests`) | none |
| I | Login email, subject, hashes in public JSON | DTOs are explicit; scan test over all public and cross-user responses (`PublicExposureTests`) | none |
| D | Request floods, huge bodies, deep JSON | Global limiter per policy and per user/IP (/64 for IPv6), 1 MB body cap, 32-level JSON depth (`Abuse/RateLimitPolicies.cs`, `RequestLimitsMiddleware.cs`, `WriteEndpointLimitTests`) | R-4: limiter state is per instance |
| D | Image decompression bombs, huge dimensions, parallel decodes | Header-only check (60 MP) before any pixel allocation; JPEG/WebP decoded at reduced size (the smallest supported scale whose long edge is at least 2400 px); a 120 MB decode-memory budget counting the output bitmap(s) and, for progressive or multi-scan JPEG, libjpeg's full-resolution coefficient buffer read from the SOF header (binds PNG at about 30 MP and progressive JPEG at about 25 MP 4:2:0); a process-wide `ImageDecodeGate` (`Images:MaxConcurrentDecodes`, default 2, bounded wait, then 503 `busy`); a failing upload is deleted so it cannot be replayed; publish holds no row lock while decoding (S1b-1, `Images/DecodeLimitTests.cs`, `Works/PublishBoundsTests.cs`) | measured peak RSS with two concurrent worst-case decodes: 404 MB per replica (R-11) |
| E | Normal user reaches admin | Separate cookie scheme and policy; default-deny page convention; allow-list rechecked on every request (`AdminRouteMatrixTests`, `AdminAuthTests`) | none |
| E | Suspended or deleted user keeps working | Per-request state check with a 20-second cache, evicted at once on suspension; refresh refused (`UserStateCache.cs`; `UserStateTests`, matrix) | up to 20 s on other instances |

### 4.4 PostgreSQL (Flexible Server)

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S/T | Stolen DB password | Container Apps secret; server firewall; TLS required | R-7: app connects as the server admin; "allow Azure services" firewall rule (C1 findings) |
| T | Tampering with the audit log | Application-level only | R-6 |
| I | Backups hold deleted data | Documented: Postgres point-in-time restore keeps 7 days, blob soft delete 7 days; the privacy policy Retention section states both, the up-to-30-day review deferral, and the records kept after deletion (evidence, DMCA notices 3 years after closing via `DmcaRetentionJob`, moderation audit trail, ban hashes) | counsel to confirm the periods |
| I | Raw email and IP in `email_codes`, `inquiries` | Codes are hashed; rows pruned by `Auth/AuthDataRetentionJob.cs` (email codes, verify attempts, signup events) | inquiry and notification retention is an owner decision (R-13) |
| D | Connection exhaustion | Scale-to-zero small API, pooled connections | owner to size at launch |

### 4.5 Blob storage and CDN

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | Anyone writes to a blob | SAS is create+write only, one blob, https only, at most 15 minutes (`Images/AzureBlobStore.cs`, `UploadUrlTests`) | a holder can overwrite that one blob until it expires, and cannot be size-capped (lifecycle rule in C1) |
| T | Replacing a public variant | Public containers are written only by the API identity | none |
| I | Originals or evidence exposed | Containers `originals`, `exports`, `evidence` are private and never CDN-fronted; a startup guard refuses a mis-set access level (`ContainerAccessGuard`, `EvidenceContainerTests`) | CDN rules to confirm at deploy |
| I | SAS URL leaks (logs, referrers, screenshots) | 15-minute life, single blob, write-only for uploads; read URLs only for exports; `sig` is scrubbed from logs and Sentry (`PiiScrubber.cs`, tests) | see abuse case A-7 |
| D | Storage cost abuse | 20-slot quota, 24-hour draft expiry and cleanup job, upload limiter 20/hour | orphan blobs after hard delete (lifecycle rule) |
| T | Malware uploaded as a "photo" (to exploit a decoder, the admin's browser, or whoever downloads an export) | S2-1: Defender for Storage scans every write (prod); before decoding, the API waits for the verdict on the server-owned sealed copy; `Malicious` blocks the work and queues `auto:malware:7` in the same transaction; no admin page (queue, report detail for any item on that work, evidence detail) shows an image of a work that ever had a malware flag, and an evidence hold does not copy its original (the sealed copy stays private); Restore is refused while the verdict is malicious, missing or unreadable for a flagged work; public files are re-encoded WebP only (`Moderation/MalwareCheck.cs`, `Moderation/MalwareFlags.cs`, `MalwareScanTests`, `MalwareAdminPageTests`, `MalwareRestoreTests`) | R-18: fail-open after the timeout or past the monthly cap; the publish sweep re-checks; a verdict arriving after the sweep has published is not acted on (no flag, so admin pages can show that image) |
| S | Content-type confusion (HTML or script declared as an image, served from our origin) | S2-4: the bytes must be the JPEG, PNG or WebP the PUT declared (`Images/ImageSniffer.cs`); public containers accept only image types; every stored blob gets `inline` (public images) or `attachment` (private) disposition; sealed copies are `application/octet-stream` | `nosniff` is added by a Cloudflare transform rule (blob storage cannot store it); raw blob-endpoint URLs rely on type + disposition |

### 4.6 ACS email

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | Using Vela to mail third parties | Codes only to the requested address, rate limited per email and IP; DMCA acknowledgement capped at 3 per address per day (`DmcaService.cs`, `HardeningTests`); inquiry body is plain text, fixed subject | a determined attacker can still send a few mails per address per hour |
| S | Phishing an artist through the contact form (S1b-20) | S2-3: fixed template (`Inquiries/InquiryEmail.cs`) opening with an "UNVERIFIED USER MESSAGE" banner; web addresses, schemes and domain dots in user text are disarmed (`https[:]//evil[.]example`); no Reply-To or other header carries user text; the sender's address is shown in the body, labelled unverified (`InquiryEmailTests`) | look-alike text and a lying sender address remain readable; the banner is the control |
| T | Header injection | Fixed subjects; addresses validated; no user text in subjects | none |
| I | Email contents in logs | Logs carry reason codes only (`EmailCodeServiceTests` log assertions) | none |
| D | Sender reputation damage | Disposable-domain list, signup velocity per IP | owner to monitor bounce rate |

### 4.7 Azure AI Content Safety

| | Threat | Mitigation | Residual |
|---|---|---|---|
| T | Evading the scan | Scan is one layer; user reports and admin review sit behind it | R-1: no hash matching for known CSAM (designed for later, counsel) |
| D | Scanner outage | Fail-open by owner decision: content publishes and a `scan_unavailable` item is queued | R-1 |
| I | Sending user content to a third party | Disclosed in the privacy policy (owner); no email or account data is sent, only text or image bytes | counsel |

### 4.8 Cloudflare (WAF, bot protection, CDN)

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | Direct-to-origin traffic spoofs `CF-Connecting-IP` | The header is believed only when the TCP peer is in the configured Cloudflare ranges; behind the Container Apps ingress `Abuse:TrustedProxyHops` selects the right `X-Forwarded-For` entry (`Abuse/ClientIpResolver.cs`, `ClientIpBehindIngressTests`) | Authenticated Origin Pulls not yet configured (owner, C1) |
| D | Layer 7 floods | Cloudflare rules (owner configures), plus API limiters | owner |
| I | API token for purge | Secret in Key Vault; purge failures are retried by the deletion/takedown reconcilers | none |

### 4.9 Sentry and Log Analytics

| | Threat | Mitigation | Residual |
|---|---|---|---|
| I | PII or secrets in events and logs | Server and app scrubbers (emails, JWTs, `sig=`, reply/inquiry/bio text); console logs written with reason codes only; Log Analytics daily cap | R-10 |
| T | Log injection | Structured logging with parameters, no user text in messages | none |
| D | Log volume cost | `dailyQuotaGb = 0.15` in `infra/modules/loganalytics.bicep` | none |

### 4.10 CI/CD (GitHub Actions)

| | Threat | Mitigation | Residual |
|---|---|---|---|
| T | Malicious or compromised action | Third-party actions pinned by commit SHA; dependabot proposes bumps (`.github/workflows/*.yml`, `.github/dependabot.yml`) | the pinned actions still run with the job token |
| E | Workflow token abuse | `permissions: contents: read` at top level; `security-events: write` only on the CodeQL job; OIDC `id-token: write` only on the manual Azure job | none |
| I | Secrets in the repository | gitleaks (history and tree) in CI, the same patterns as `RepoSecretScanTests`; `.gitignore` for key material | R-14: GitHub Advanced Security needed for CodeQL results on a private repo |
| T | Vulnerable dependency | `scripts/check-vulnerable-packages.sh` fails on High/Critical; `flutter pub outdated` report; Dependabot | transitive Dart advisories are not scanned (no `dart` equivalent of `dotnet list --vulnerable`) |

## 5. Abuse cases

| ID | Abuse | Controls | Tests and code | Residual |
|---|---|---|---|---|
| A-1 | **Bots and fake accounts** | Attestation on sign-in, email-code and upload start; signup velocity per IP/day; disposable-email list; email-code limits; Cloudflare | `Abuse/*Tests.cs`, `SignupVelocity.cs` | Android attested (Play Integrity) at launch; iOS and anything claiming iOS only rate-limited (R-3) |
| A-2 | **Scrapers** | `anon-read` 120/min/IP; `user-read` 600/min/user; Cloudflare | `RateLimitTests`, `HardeningTests` | R-5, R-4 |
| A-3 | **CSAM upload** | Content Safety auto-block (severity 4+), user reports, admin queue; evidence hold in a restricted container; admin-triggered NCMEC report behind an interface; suspension plus banned-identity hash | `Admin/Evidence*Tests.cs`, `NcmecTests.cs`, `BanTests.cs` | R-1: fail-open scan; no hash matching; NCMEC reporter is a stub (owner needs an ESP account) |
| A-4 | **DMCA abuse** (false claims; mail-bombing the claimant address) | Notice stored, never auto-actioned; admin decides; counter-notice flow with the 10-day wait; ack mails capped per address; per-IP form limit | `Legal/*Tests.cs`, `HardeningTests` | wording and periods need counsel |
| A-5 | **Report brigading** (silencing an artist) | 10 reports per user per day; S2-5 and owner decision 2026-11-03: an `illegal_content` report hides at once only when the reporter's account is at least 24 h old (younger: queued and counted as a pending reporter, no hide), any report once 3 distinct accounts have pending reports or the reporter's account is older than 7 days (per-target advisory lock, so parallel reports cannot slip under; reporters suspended or deleted since no longer count); an admin restores; restored content is never auto-blocked again; duplicates collapse | `Social/ReportThresholdTests.cs`, `SafetyTests.cs`, `ModerationActionTests.cs` | R-2: a false `illegal_content` report from an account at least a day old, 3 fresh accounts, or one account older than 7 days can still hide a story until review |
| A-6 | **Account enumeration** | Same response for known and unknown emails and for allow-listed and other admin addresses; handle lookups are public by design | `EmailCodeEndpointTests`, `AdminAuthTests` | R-12 (timing) |
| A-7 | **SAS URL leakage** | One blob, write-only (read-only for exports), https, at most 15 minutes; scrubbed from telemetry | `UploadUrlTests`, `TelemetryTests` | a leaked write URL lets the holder replace that one pending upload |
| A-8 | **Token theft** | Short access token, rotating refresh token with family revocation, secure storage on device, suspension revokes everything | `TokenServiceTests`, `HardeningTests` | device compromise |
| A-9 | **Doxxing through photo metadata** | Public variants carry no metadata; client strips; originals private | `VariantTests` | by design the owner can export the original |
| A-10 | **Hostile text** (HTML, RTL overrides, zalgo, zero-width, huge, control characters) | S2-2: one sanitizer for names, bios, titles, pin notes, replies, inquiries and report notes (`Common/TextSanitizer.cs`, mirrored in `app/lib/core/text/text_sanitizer.dart`, shared vectors `contract/text-sanitizer-vectors.json`): NFC, control and invisible characters stripped (ZWJ/ZWNJ kept only in emoji sequences and non-Latin scripts), at most 4 stacked marks, spaces collapsed; limits counted in code points of the cleaned text, identically on the client; bidi overrides and separators still refused; HTML-escaped on every page; no script on any server page; CSP | `TextSanitizerTests`, `text_sanitizer_test.dart`, `ShareEscapingTests`, `ReplyTests`, `HeadersAndCorsTests`, `HardeningTests` | R-15 |
| A-11 | **Admin phishing / session theft** | Email-code sign-in for allow-listed addresses only, 8-hour absolute session, one live session per admin (sign-out and re-sign-in end copies of the cookie), cookie `HttpOnly`, `SameSite=Strict`, `Secure`, path `/admin`; per-request recheck | `AdminAuthTests`, `HardeningTests` | R-6 (single factor) |
| A-12 | **Cost attacks** (storage, email, scans) | Quotas, draft expiry, limiters, Azure budget alert at twice expected cost | `QuotaTests`, `infra/modules/budget.bicep` | owner watches the alert |
| A-13 | **Evading review by deleting the account** (S1b-2) | Deletion of content under review refused (409 `under_review`); account hard delete deferred while reviews are open (at most 30 days); owner decision 2026-11-03: each sign-in identity's HMAC hash goes into `signup_blocks` (hash, created, expires only) and sign-in or sign-up with it answers 403 `signup_blocked` until the hard delete (review closed or 30-day cap) removes the rows; the sign-in check also honours `expires_at` and the hourly auth retention job deletes lapsed rows; an admin can still ban during the window (permanent `banned_identities`) | `Account/SignupBlockTests.cs`, `DeletionDeferralTests.cs`, `DeletedAccountBanTests.cs` | a new identity (other email, other Apple/Google account) is not linked; after 30 days an unreviewed, unbanned person can sign up again |

## 6. Residual risks (accepted or owned)

| ID | Risk | Rating | Disposition |
|---|---|---|---|
| R-1 | Content Safety fails open and has no known-CSAM hash matching; the NCMEC reporter is unconfigured outside Development | High (legal) | Owner decision recorded in the spec; counsel and an ESP account are launch dependencies |
| R-2 | Report brigading: hiding needs an `illegal_content` report from an account at least 24 h old (`Reports:IllegalContentInstantHideMinAgeHours`), 3 distinct pending reporters, or one reporter older than 7 days. The reason is chosen by the reporter, so an attacker who keeps an account for a day can still hide any story at once with `illegal_content`; one account older than 7 days hides with any reason | Medium | Accepted (owner decision 2026-11-03, section 7): the 24 h wait removes the throwaway-account attack (I-5) while an established reporter still takes real CSAM down at once; a fresh account's report is queued for the admin and counts towards the 3-reporter threshold; an admin restores false hides |
| R-3 | Attestation at launch is Android-only (owner decision 2026-11-03): `Abuse:RequireAttestation:Android=true`, `:Ios=false`. The server cannot prove a request is from iOS, so a script that sends `X-Platform: ios` passes without attestation (the downgrade path); it is held to the tighter unattested limits per address and account. Play Integrity has no server nonce store (token replayable for its max age, request hash = SHA-256(action)); App Attest creates a key per call | Medium | Accepted for launch: the unattested limits, signup velocity, email-code limits, quotas and Cloudflare bound the cost; turning on App Attest (`:Ios=true`, after the owner's per-install design, ledger B14/A15) closes the downgrade path |
| R-4 | Rate limiters are in memory per instance; an attacker spreads requests across replicas | Low | Accepted at scale-to-zero size; move to Cloudflare rules or a shared store if replicas grow |
| R-5 | Public content can be scraped | Low | Accepted; it is public by design |
| R-6 | The audit table is not DB-immutable (the app's database role can update it); a single email code is the only admin factor; one person is the whole trust base | Medium | Admin sign-in/out are audited and sign-out ends the session everywhere. At deploy give the app's DB role insert/select only on `admin_actions`, and put Cloudflare Access in front of `/admin` (owner) |

| R-7 | The app connects to Postgres as the server admin; "allow Azure services" firewall rule | Medium | C1 follow-up for the owner at first deploy |
| R-8 | Web viewer CSP and headers are applied by the host at deploy | Low | Owner/device check |
| R-9 | Rooted or jailbroken devices can read app storage | Low | Accepted; MASVS-RESILIENCE is intentionally not pursued |
| R-10 | Telemetry scrubbing verified by unit tests, not on a real event | Low | Device/owner check with a real DSN |
| R-11 | Peak memory when decoding large uploads | Low | Corrected at S1b-1: the earlier "semaphore" did not exist. Now: header check, reduced-size decode, a 120 MB decode budget that also counts the progressive-JPEG coefficient buffer, and `ImageDecodeGate` (default 2 concurrent decodes per replica). Review round 1 measured the earlier 160 MB claim as wrong for progressive JPEG (about 390 MB per decode, about 750 MB for two). Measured now (whole-process peak RSS, 8 parallel requests, gate 2): PNG 5400x5400 404 MB, progressive 4:4:4 3900x3900 362 MB, progressive 4:2:0 5000x5000 300 MB, baseline 8000x6000 camera JPEG 285 MB, avatar path at most 349 MB; with gate 1 at most 303 MB. Revisit with load data |
| R-12 | Sign-in response time differs slightly for unknown versus known addresses | Low | Accepted; responses and codes are identical, rate limits bound probing |
| R-13 | No retention period yet for inquiries, notifications and reports | Medium (privacy) | Owner/counsel decision |
| R-14 | CodeQL needs GitHub Advanced Security on a private repository | Low | Owner decision; the job is in place |
| R-15 | Text that merely *looks* misleading (look-alike letters, emoji sequences) cannot be filtered; bidirectional overrides and line separators are refused | Low | Accepted; the override characters are refused (`Common/UnsafeText.cs`) |
| R-16 | Data Protection keys (antiforgery, admin cookie) are stored unencrypted in blob storage | Medium | Wrap with Key Vault (C1 follow-up) |
| R-17 | An external penetration test has not been done | n/a | Recommended by the spec before public launch |
| R-18 | Malware scanning fails open: no verdict within 10 s, a scan that finished without a verdict (`Error`, `Not scanned`: no wait) (or past the 10 GB monthly cap, or Defender off outside prod) publishes without one; Defender scans only what it supports (no image-decoder exploit detection is promised) | Medium | Accepted (mirrors the Content Safety fail-open decision); the publish sweep re-checks the tag; public files are always re-encoded; watch the `No malware verdict` warning |

## 7. Owner decisions (S2 review, decided 2026-11-03)

- **R-2 / I-5: instant hide on `illegal_content` (decided: option 3).** Trade-off: hiding at once keeps real illegal content (CSAM
  above all) off the app until an admin looks, but a fresh account could pick `illegal_content` and hide any story at once.
  The owner chose to require an account age of 24 hours (`Reports:IllegalContentInstantHideMinAgeHours`, default 24, "at least",
  so exactly 24 h hides) before an `illegal_content` report hides anything by itself. A younger account's report is stored, shown in
  the admin queue, and counts as a pending reporter for the 3-distinct-reporters rule; it hides nothing on its own. The other
  options considered (per-day instant-hide cap for fresh accounts, hide only after an admin has seen it, a dismissed report costing
  the reporter instant hide) were not implemented. Immediate hiding for established accounts is kept.
