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
| S | Attacker replays or forges an attestation to pass as the real app | App Attest and Play Integrity verified server-side (`Abuse/AppAttestVerifier.cs`, `PlayIntegrityVerifier.cs`, `AttestationMiddleware.cs`), fail-closed in production | R-3: no server nonce store, token replayable for 5-10 minutes; App Attest uses a key per call |
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
| D | Image decompression bombs, huge dimensions | Decode limits and a semaphore in `Images/VariantGenerator.cs`; failure maps to `invalid_image` | memory per decode at the maximum accepted size (R-11) |
| E | Normal user reaches admin | Separate cookie scheme and policy; default-deny page convention; allow-list rechecked on every request (`AdminRouteMatrixTests`, `AdminAuthTests`) | none |
| E | Suspended or deleted user keeps working | Per-request state check with a 20-second cache, evicted at once on suspension; refresh refused (`UserStateCache.cs`; `UserStateTests`, matrix) | up to 20 s on other instances |

### 4.4 PostgreSQL (Flexible Server)

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S/T | Stolen DB password | Container Apps secret; server firewall; TLS required | R-7: app connects as the server admin; "allow Azure services" firewall rule (C1 findings) |
| T | Tampering with the audit log | Application-level only | R-6 |
| I | Backups hold deleted data | Documented: backups expire within 30 days (point-in-time restore window); privacy policy to state it | counsel to confirm |
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

### 4.6 ACS email

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | Using Vela to mail third parties | Codes only to the requested address, rate limited per email and IP; DMCA acknowledgement capped at 3 per address per day (`DmcaService.cs`, `HardeningTests`); inquiry body is plain text, fixed subject | a determined attacker can still send a few mails per address per hour |
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
| A-1 | **Bots and fake accounts** | Attestation on sign-in, email-code and upload start; signup velocity per IP/day; disposable-email list; email-code limits; Cloudflare | `Abuse/*Tests.cs`, `SignupVelocity.cs` | attestation disabled until the owner has Apple/Google accounts (R-3) |
| A-2 | **Scrapers** | `anon-read` 120/min/IP; `user-read` 600/min/user; Cloudflare | `RateLimitTests`, `HardeningTests` | R-5, R-4 |
| A-3 | **CSAM upload** | Content Safety auto-block (severity 4+), user reports, admin queue; evidence hold in a restricted container; admin-triggered NCMEC report behind an interface; suspension plus banned-identity hash | `Admin/Evidence*Tests.cs`, `NcmecTests.cs`, `BanTests.cs` | R-1: fail-open scan; no hash matching; NCMEC reporter is a stub (owner needs an ESP account) |
| A-4 | **DMCA abuse** (false claims; mail-bombing the claimant address) | Notice stored, never auto-actioned; admin decides; counter-notice flow with the 10-day wait; ack mails capped per address; per-IP form limit | `Legal/*Tests.cs`, `HardeningTests` | wording and periods need counsel |
| A-5 | **Report brigading** (silencing an artist) | 10 reports per user per day; a report hides the content *pending review*, an admin restores; restored content is never auto-blocked again; duplicates collapse | `Social/SafetyTests.cs`, `ModerationActionTests.cs` | R-2: a coordinated group of real accounts can still hide a story until review |
| A-6 | **Account enumeration** | Same response for known and unknown emails and for allow-listed and other admin addresses; handle lookups are public by design | `EmailCodeEndpointTests`, `AdminAuthTests` | R-12 (timing) |
| A-7 | **SAS URL leakage** | One blob, write-only (read-only for exports), https, at most 15 minutes; scrubbed from telemetry | `UploadUrlTests`, `TelemetryTests` | a leaked write URL lets the holder replace that one pending upload |
| A-8 | **Token theft** | Short access token, rotating refresh token with family revocation, secure storage on device, suspension revokes everything | `TokenServiceTests`, `HardeningTests` | device compromise |
| A-9 | **Doxxing through photo metadata** | Public variants carry no metadata; client strips; originals private | `VariantTests` | by design the owner can export the original |
| A-10 | **Hostile text** (HTML, RTL overrides, huge, control characters) | Validated and stored verbatim, HTML-escaped on every page; no script on any server page; CSP; bidi overrides and separators refused | `ShareEscapingTests`, `ReplyTests`, `HeadersAndCorsTests`, `HardeningTests` | R-15 |
| A-11 | **Admin phishing / session theft** | Email-code sign-in for allow-listed addresses only, 8-hour absolute session, one live session per admin (sign-out and re-sign-in end copies of the cookie), cookie `HttpOnly`, `SameSite=Strict`, `Secure`, path `/admin`; per-request recheck | `AdminAuthTests`, `HardeningTests` | R-6 (single factor) |
| A-12 | **Cost attacks** (storage, email, scans) | Quotas, draft expiry, limiters, Azure budget alert at twice expected cost | `QuotaTests`, `infra/modules/budget.bicep` | owner watches the alert |

## 6. Residual risks (accepted or owned)

| ID | Risk | Rating | Disposition |
|---|---|---|---|
| R-1 | Content Safety fails open and has no known-CSAM hash matching; the NCMEC reporter is unconfigured outside Development | High (legal) | Owner decision recorded in the spec; counsel and an ESP account are launch dependencies |
| R-2 | Report brigading can hide content until an admin reviews it | Medium | Accepted; admin review speed is an owner item |
| R-3 | Attestation is not enforceable until Apple and Google developer accounts exist; no replay store; App Attest creates a key per call | Medium | Owner decision before enabling in production (ledger B14/A15) |
| R-4 | Rate limiters are in memory per instance; an attacker spreads requests across replicas | Low | Accepted at scale-to-zero size; move to Cloudflare rules or a shared store if replicas grow |
| R-5 | Public content can be scraped | Low | Accepted; it is public by design |
| R-6 | The audit table is not DB-immutable (the app's database role can update it); a single email code is the only admin factor; one person is the whole trust base | Medium | Admin sign-in/out are audited and sign-out ends the session everywhere. At deploy give the app's DB role insert/select only on `admin_actions`, and put Cloudflare Access in front of `/admin` (owner) |

| R-7 | The app connects to Postgres as the server admin; "allow Azure services" firewall rule | Medium | C1 follow-up for the owner at first deploy |
| R-8 | Web viewer CSP and headers are applied by the host at deploy | Low | Owner/device check |
| R-9 | Rooted or jailbroken devices can read app storage | Low | Accepted; MASVS-RESILIENCE is intentionally not pursued |
| R-10 | Telemetry scrubbing verified by unit tests, not on a real event | Low | Device/owner check with a real DSN |
| R-11 | Peak memory when decoding a 60-megapixel upload | Low | Concurrency semaphore; revisit with load data |
| R-12 | Sign-in response time differs slightly for unknown versus known addresses | Low | Accepted; responses and codes are identical, rate limits bound probing |
| R-13 | No retention period yet for inquiries, notifications and reports | Medium (privacy) | Owner/counsel decision |
| R-14 | CodeQL needs GitHub Advanced Security on a private repository | Low | Owner decision; the job is in place |
| R-15 | Text that merely *looks* misleading (look-alike letters, emoji sequences) cannot be filtered; bidirectional overrides and line separators are refused | Low | Accepted; the override characters are refused (`Common/UnsafeText.cs`) |
| R-16 | Data Protection keys (antiforgery, admin cookie) are stored unencrypted in blob storage | Medium | Wrap with Key Vault (C1 follow-up) |
| R-17 | An external penetration test has not been done | n/a | Recommended by the spec before public launch |
