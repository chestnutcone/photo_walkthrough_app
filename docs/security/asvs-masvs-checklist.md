# OWASP ASVS 4.0.3 (Level 2) and MASVS v2 checklist

Status vocabulary: **Met** (implemented and pinned by a test or a file you can open), **Partially** (implemented with a stated
gap), **Not met** (not implemented; a reason follows), **N/A** (the requirement does not apply to this design),
**Needs device / owner** (cannot be settled from the repository: it needs a real device, a deployed environment, an account the
owner has not created, or a decision only the owner or counsel can make).

Paths are relative to the repository root. `Sec/` is `api/tests/Vela.Api.Tests/Security/`. The threat model is
`docs/security/threat-model.md`; open items are numbered in `docs/security/review-findings.md` (F-xx = deferred finding, R-xx =
residual risk). This is a self-assessment by the builder, written honestly so a reviewer or penetration tester can aim at the
weak spots. It is not an attestation.

Scope of ASVS here: the API (`api/`), the admin pages it serves, and the web story viewer. Chapters V1 to V14 are covered; V15+ do
not exist in 4.0.3. The mobile app is assessed against MASVS v2 in part 2.

## Part 1: ASVS 4.0.3 L2

### V1 Architecture, design and threat modelling

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 1.1.1 | Secure SDLC | Met | Plan/review gate per task, `docs/superpowers/`, SDD ledger; CI in `.github/workflows/` |
| 1.1.2 | Threat modelling for every design change | Partially | `threat-model.md` written at S1; per-task reviews considered threats, but it is not re-run per change |
| 1.1.4 | Trust boundaries, components and data flows documented | Met | `threat-model.md` section 3 |
| 1.1.5 | Security architecture of components documented | Met | `threat-model.md` section 4 |
| 1.1.6 | Centralised, simple, vetted security controls | Met | JWT validation in one place (`Program.cs`), one rate-limit module, one `UnsafeText`/validator family |
| 1.1.7 | Secure coding checklists and review | Met | Every task reviewed; this S1 gate |
| 1.2.1 | Unique low-privilege OS accounts per component | Needs owner | Container runs as `$APP_UID` (`api/Dockerfile`); Azure identities in `infra/modules/identity.bicep` still to be checked on a deploy (R-7: app uses the DB admin login) |
| 1.2.2 | Inter-component communication authenticated | Partially | Storage and ACS by key/connection string, Postgres by password; managed identity not used yet |
| 1.2.3 | One vetted authentication mechanism | Met | JWT bearer for the API, one cookie scheme for `/admin`, both created from the same email-code and OIDC flows |
| 1.4.1 | Access control enforced on a trusted layer | Met | Server-side authorization on every endpoint; `Sec/AuthorizationMatrixTests.cs` |
| 1.4.4 | Single well-vetted access control mechanism | Met | `RequireAuthorization()` + per-service ownership checks; admin policy `AdminCookie` |
| 1.4.5 | Attribute/feature-based checks rather than roles in code | Met | Policy and claim based; ownership by user id in queries |
| 1.5.1 | Input and output requirements defined | Met | `contract/openapi.yaml`, validators (`Works/WorkValidator.cs` etc.) |
| 1.5.2 | Untrusted data never deserialised unsafely | Met | `System.Text.Json` only; no polymorphic type names |
| 1.5.3 | Input validation on a trusted service layer | Met | Server validates every field; client validation is convenience only |
| 1.5.4 | Output encoding close to the interpreter | Met | `Share/HtmlText.cs` for HTML; Razor encoding for admin; `Sec/` header tests, `Share/ShareEscapingTests.cs` |
| 1.6.1 | Cryptographic key management policy | Partially | Key roles listed in `threat-model.md` and `docs/deploy.md`; rotation procedure for the JWT key and peppers is not written (owner) |
| 1.6.2 | Keys protected by a vault or equivalent | Partially | Key Vault and Container Apps secrets (`infra/modules/keyvault.bicep`); secrets reach the app as environment variables |
| 1.7.1 | Common log format | Met | Structured logging with reason codes; `Telemetry/` |
| 1.7.2 | Logs transmitted to a central system | Met | Log Analytics (`infra/modules/loganalytics.bicep`), Sentry |
| 1.8.1 | Sensitive data classified and protected | Met | `threat-model.md` section 1 |
| 1.9.1 | Components communicate over encrypted channels | Met | Storage https only (`minimumTlsVersion` TLS1_2), Postgres `require_secure_transport`, ingress `allowInsecure: false` |
| 1.10.1 | Source control with change control | Met | Git, task-per-commit history |
| 1.11.1 | Business logic flows documented | Met | Spec sections 7, 12, 14 |
| 1.11.2 | High-value flows resist race conditions | Met | Quota in a transaction, advisory locks (refresh family, inquiries, DMCA, signup velocity); `Works/QuotaTests.cs`, `Sec/HardeningTests.cs` |
| 1.12.1 | Uploaded files stored outside the web root | Met | Blob Storage; originals in a private container |
| 1.12.2 | Uploaded files served safely | Met | Only re-encoded WebP variants are public; originals never served |
| 1.14.1 | Components of differing trust segregated | Met | Separate containers by trust; admin on a separate scheme |
| 1.14.3 | Build pipeline warns on vulnerable components | Met | `.github/workflows/security.yml` (NuGet advisories fail the build; Dependabot) |
| 1.14.4 | Build pipeline verifies secure deployment | Partially | `scripts/validate-infra.sh` checks templates offline; nothing validated against Azure yet (C1 note) |

### V2 Authentication

No passwords exist anywhere. Sign-in is Apple, Google or an email one-time code. All password requirements (2.1.x, 2.4.x, 2.5.x,
2.6.x, 2.9.x) are N/A and not listed one by one.

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 2.1.x, 2.4.x, 2.6.x, 2.9.x | Password policy, storage, look-up secrets, cryptographic verifiers | N/A | No passwords or look-up secrets in the design |
| 2.2.1 | Anti-automation against credential stuffing and brute force | Met | Per-email 5/hour, per-IP limits, 5 attempts per code, signup velocity: `Auth/EmailCodes/EmailCodeService.cs`, `Abuse/*Tests.cs`, `Auth/EmailCodes/EmailCodeEndpointTests.cs` |
| 2.2.2 | Weak authenticators (email) only as secondary factors | Not met | Email code is a *primary* sign-in route, by product decision (no password, no phone). Apple/Google are offered; the email route is rate limited, hashed, short-lived. Accepted, R-12 |
| 2.2.3 | Secure notification after authentication changes | N/A | No changeable credentials |
| 2.3.1 | Initial/activation codes are random, short-lived, single use | Met | 6 digits from `RandomNumberGenerator`, 10 minutes, consumed on use: `EmailCodeServiceTests.cs` |
| 2.5.4 | No default or shared accounts | Met | No seed accounts; review mode fixed code only for allow-listed review addresses and is logged on boot (`Program.cs`) |
| 2.5.5 | Notify users of reset | N/A | |
| 2.7.1 | No clear-text PSTN/SMS out-of-band | Met | Email only |
| 2.7.2 | Out-of-band code expires within 10 minutes | Met | `EmailCodeService.CodeLifetime`, test in `EmailCodeServiceTests.cs` |
| 2.7.3 | Out-of-band code usable once | Met | `ConsumedAt` claim; replay test |
| 2.7.4 | Verifier and channel are independent and secure | Partially | Email is as secure as the mailbox; transport TLS via ACS |
| 2.8.1 | One-time verifiers have a defined lifetime | Met | As 2.7.2 |
| 2.8.4 | One-time verifier used only once in its window | Met | As 2.7.3; a new request consumes older codes |
| 2.10.1 | No unchanging credentials between services | Partially | Storage and DB keys are static secrets (R-7); rotation is manual |
| 2.10.2 | No default service credentials | Met | `Auth/JwtOptions.cs` and others refuse to start without real secrets outside Development and Test (`Data/StartupConfigTests.cs`) |
| 2.10.4 | Secrets not in source or repositories | Met | `Sec/RepoSecretScanTests.cs`, gitleaks in CI; documented dev-only values allow-listed |
| 2.1 OIDC | Federated tokens verified: signature, issuer, audience, nonce, expiry | Met | `Auth/Identity/OidcIdentityVerifier.cs`, `Auth/Identity/OidcVerifierTests.cs` |

### V3 Session management

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 3.1.1 | Session tokens never in URLs | Met | Bearer header and body only |
| 3.2.1 | New session token on authentication | Met | `TokenService.IssueAsync` per sign-in |
| 3.2.2 | At least 64 bits of entropy | Met | 256-bit refresh token; signed access token |
| 3.2.4 | Generated with approved cryptography | Met | `RandomNumberGenerator`, HMAC-SHA256 |
| 3.3.1 | Logout and expiry invalidate the session | Partially | Refresh family revoked at logout; the 15-minute access token lives on. The admin cookie is *not* invalidated server-side at logout (F-A14a-2) |
| 3.3.2 | Re-authentication after inactivity | Met | Access token 15 min, refresh 30 days rotating; admin cookie 8 h absolute |
| 3.3.4 | Users can view and end active sessions | Not met | No session list UI; sign-out and delete-account revoke everything |
| 3.4.1 to 3.4.3, 3.4.5 | Cookie `Secure`, `HttpOnly`, `SameSite`, narrow path | Met | `Admin/AdminAuth.cs`; `Admin/AdminAuthTests.cs` |
| 3.4.4 | `__Host-` prefix | Not met | The admin cookie is scoped to `/admin` for least reach, which the prefix forbids. Accepted |
| 3.5.2 | Session tokens rather than static API secrets | Met | |
| 3.5.3 | Stateless tokens resist tampering, replay, `alg:none`, key substitution | Met | HS256 pinned, issuer, audience, `exp` required, zero skew, `nbf` honoured; 10 forged-token shapes on every operation: `Sec/AuthorizationMatrixTests.cs` (`ForgedTokens.cs`); refresh reuse detection and race: `Auth/TokenServiceTests.cs`, `Sec/HardeningTests.cs` |
| 3.7.1 | Defences against session exploits | Met | Suspension/deletion check per request (20 s cache, evicted at once): `Admin/UserStateTests.cs` |

### V4 Access control

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 4.1.1 | Access control enforced server side | Met | `Sec/AuthorizationMatrixTests.cs`: 401 without a token on every non-public operation, owner-only operations refused for another user |
| 4.1.2 | Attributes used for access decisions cannot be manipulated by users | Met | `Sec/MassAssignmentTests.cs` (admin flag, tier, quota, handle lock, status, owner) |
| 4.1.3 | Least privilege | Met | Ownership checks in queries; admin separated |
| 4.1.5 | Access controls fail securely | Met | Default-deny Razor convention; unknown admin routes covered by `Sec/AdminRouteMatrixTests.cs` |
| 4.2.1 | No IDOR on data, files, URLs | Met | `Sec/AuthorizationMatrixTests.cs` (drafts, works, replies, notifications, exports, inquiries) |
| 4.2.2 | CSRF protection for cookie-authenticated functions | Met | Antiforgery on all admin POSTs (`Admin/AdminAuthTests.cs`); the JSON API uses bearer tokens and a read-only CORS policy |
| 4.3.1 | Administrative interfaces use multi-factor authentication | Not met | Email code to an allow-listed address is a single factor. Mitigated by the allow-list, 8-hour session and per-request recheck. Recommend Cloudflare Access in front of `/admin` (owner), R-6 |
| 4.3.2 | Directory browsing and metadata files not exposed | Met | No static file serving in the API |

### V5 Validation, sanitisation and encoding

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 5.1.1 | HTTP parameter pollution defences | Met | Single-value binding; extra keys ignored (`Sec/MassAssignmentTests.cs`) |
| 5.1.3 | Positive validation of all input | Met | Per-field validators; handle regex; lengths; enums |
| 5.1.4 | Structured data validated against a schema | Partially | Hand-written validators checked against the contract by tests, not a runtime schema validator |
| 5.1.5 | Redirects only to allow-listed targets | Met | The only redirects are to fixed admin paths |
| 5.2.1 | HTML sanitised | Met | No user HTML is rendered; legal Markdown has HTML disabled (`Legal/LegalDocuments.cs`) |
| 5.2.2 | Unstructured input sanitised | Met | Control and bidi-override characters refused (`Common/UnsafeText.cs`, `Sec/HardeningTests.cs`) |
| 5.2.3 | Output sanitised for mail injection | Met | Fixed subjects, plain text bodies |
| 5.2.4 | No `eval` | Met | |
| 5.2.6 | SSRF protection | Met | The server fetches only fixed JWKS and configured service endpoints; user URLs (website) are stored, never fetched |
| 5.3.1 to 5.3.3 | Context-aware output encoding | Met | `Share/HtmlText.cs`, Razor; `Share/ShareEscapingTests.cs` |
| 5.3.4, 5.3.5 | Parameterised queries | Met | EF Core; raw SQL uses interpolated parameters only |
| 5.3.6 to 5.3.10 | JSON, LDAP, OS, path injection | Met / N/A | No shell, no LDAP; blob names are server-built (`Images/BlobNames.cs`, `Sec/UploadUrlTests.cs`) |
| 5.5.1, 5.5.3 | Safe deserialisation | Met | |

### V6 Stored cryptography

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 6.1.1 to 6.1.3 | Sensitive data encrypted at rest | Needs owner | Azure encrypts Postgres and Storage at rest by default; confirm in the deployed subscription |
| 6.2.1, 6.2.2 | Crypto fails securely; proven algorithms | Met | HMAC-SHA256 peppered hashes for codes and banned identities; SHA-256 token hashes |
| 6.2.8 | Constant-time comparison | Met | `CryptographicOperations.FixedTimeEquals` in `EmailCodeService` |
| 6.3.1 | CSPRNG for security values | Met | `RandomNumberGenerator` for codes, refresh tokens, nonces |
| 6.3.2 | GUIDs from a CSPRNG where they are secrets | Met | Capability ids are not secret; secrets are never GUIDs |
| 6.4.1 | Secrets in a vault | Partially | Key Vault module exists; the app receives them as Container Apps secrets |

### V7 Error handling and logging

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 7.1.1, 7.1.2 | No credentials or sensitive data in logs | Met | Reason-code logging, scrubbers, SAS signatures redacted: `Telemetry/TelemetryTests.cs`, `Admin/AdminPageTests.cs` log assertions |
| 7.1.3 | Security events logged | Partially | Auth failures, rate limits and moderation actions are logged or audited; 401/403 volume is metrics only |
| 7.1.4 | Logs carry time, user, event | Partially | Trace id in 500s; user ids not in every line (privacy) |
| 7.2.1, 7.2.2 | Authentication and access control decisions logged | Partially | Admin actions, sign-in and sign-out are audited in `admin_actions` (S1-10); general 403s and the table's DB-immutability are not (F-A14a-3, R-6) |
| 7.3.1 | Log injection prevented | Met | Parameterised structured logging |
| 7.3.3 | Logs protected | Needs owner | Log Analytics RBAC to be set by the owner |
| 7.4.1 | Generic message on unexpected error | Met | `Telemetry/ExceptionHandling.cs`; `Sec/PublicExposureTests.cs` |
| 7.4.2, 7.4.3 | Exception handling everywhere; last-resort handler | Met | Middleware plus per-endpoint mapping |

### V8 Data protection

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 8.1.1 | Sensitive data not cached on the server tier | Met | No server-side response cache of user data |
| 8.1.5, 8.1.6 | Backups taken and tested | Needs owner | Postgres point-in-time restore (7 days); a restore drill has not been run |
| 8.2.1 | Anti-caching headers for sensitive pages | Partially | Admin pages `no-store`; authenticated JSON responses carry no `Cache-Control` (F-S1-1) |
| 8.2.2, 8.2.3 | Client storage holds no sensitive data; cleared at sign-out | Met | Secure storage for tokens; local-data registry clears searches on sign-out and delete (`app/test/core/local_data_test.dart`) |
| 8.3.1 | Sensitive data not in URLs | Partially | Emails, codes and tokens travel in bodies; SAS URLs and search terms are in URLs by nature |
| 8.3.2 | Users can remove and export their data | Met | `Account/DeletionJob.cs`, `ExportService.cs`; tests in `Account/` |
| 8.3.3 | Plain-language privacy notice and consent | Needs owner | Placeholder legal pages (`Legal/*.md`); counsel to finalise |
| 8.3.4 | All sensitive data identified, with controls | Met | `threat-model.md` section 1 |
| 8.3.5 | Access to sensitive data is audited | Not met | Admin views of evidence are not audited (counsel decision, F-A14b-3) |
| 8.3.8 | Retention classification | Partially | Email codes and attempts pruned daily; inquiries, notifications, reports have no retention period yet (R-13) |

### V9 Communications

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 9.1.1 to 9.1.3 | TLS for all client connections, TLS 1.2+ | Needs owner | Container Apps `allowInsecure: false`; Cloudflare minimum TLS version and "Always use HTTPS" to be set at deploy |
| 9.2.1 to 9.2.3 | TLS between components and to external services | Met | Postgres `require_secure_transport`, storage `supportsHttpsTrafficOnly`, ACS and Content Safety over https |
| HSTS | Strict-Transport-Security | Met | `Common/SecurityHeaders.cs` sets it outside Development and Test; `Sec/HeadersAndCorsTests.cs` (no `preload`, owner's call) |

### V10 Malicious code

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 10.1.1 | Static analysis in use | Met | CodeQL (`security.yml`; needs GitHub Advanced Security for a private repository, R-14), analyzers, `dart analyze --fatal-infos` |
| 10.2.1 | No unauthorised phone-home | Met | Only Sentry (documented, scrubbed) and the services in the threat model |
| 10.2.2 | No unnecessary permissions | Met | Android: INTERNET only; iOS: camera and photos purpose strings only (`app/test/security/platform_config_test.dart`) |
| 10.3.2 | Integrity protection of delivered code | Needs owner | Store signing; Android release is still debug-signed until the owner provides a keystore |
| 10.3.3 | Subdomain takeover protection | Needs owner | DNS hygiene at deploy |

### V11 Business logic

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 11.1.1 | Steps processed in order | Met | Story state machine draft, pending, published, blocked, removed |
| 11.1.2, 11.1.4 | Realistic human time; anti-automation | Met | Limiters, attestation, signup velocity |
| 11.1.3, 11.1.5 | Per-user limits and validation | Met | 20-story quota, 40 pins, 10 reports/day, export once a day, inquiry caps |
| 11.1.6 | TOCTOU and races | Met | Transactions and advisory locks; two simultaneous drafts at 19: `Works/QuotaTests.cs` |
| 11.1.7, 11.1.8 | Monitoring and alerting on anomalies | Partially | Metrics exist (`Telemetry/VelaMetrics.cs`); no alert rules are defined yet (owner) |

### V12 Files and resources

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 12.1.1 | Large files cannot exhaust resources | Partially | 25 MB source cap and 60 megapixel cap checked before decode (`Images/VariantGenerator.cs`); SAS cannot cap size, a lifecycle rule must (R-11) |
| 12.1.2 | Compressed-file bombs | N/A | No archives accepted |
| 12.1.3 | File size quota per user | Met | Quota of 20 works, drafts expire in 24 hours |
| 12.2.1 | File type verified | Met | Content decoded, not trusted by extension; invalid images map to `invalid_image` |
| 12.3.1, 12.3.2 | Server-chosen file names, no traversal | Met | GUID blob names; `Sec/UploadUrlTests.cs` refuses traversal names |
| 12.3.4 | Reflected file download | Met | `nosniff` everywhere; JSON content type |
| 12.4.1 | Untrusted files stored outside the web root | Met | |
| 12.4.2 | Antivirus scan | Not met | Images are decoded and re-encoded (which removes payloads) and scanned by Content Safety; there is no malware scanner |
| 12.5.2 | Uploads never executed | Met | |
| 12.6.1 | SSRF | Met | See 5.2.6 |

### V13 API and web service

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 13.1.1 | Same parsers and encodings across components | Met | |
| 13.1.4 | Authorisation at URI and resource level | Met | `Sec/AuthorizationMatrixTests.cs` |
| 13.1.5 | Unexpected or missing content types rejected | Not met | Endpoints parse the body whatever its `Content-Type`; size and depth are capped. A bearer-token API is not CSRF-exposed. Accepted (F-A8-1) |
| 13.2.1 | RESTful methods match actions | Met | Other methods get 404/405 (`Sec/PublicExposureTests.cs`) |
| 13.2.2, 13.2.5 | JSON schema validation; content-type check | Partially | See 5.1.4, 13.1.5 |
| 13.2.3 | CSRF defence for REST | Met | Bearer tokens; CORS read-only for the viewer origin only |
| 13.2.6 | Request/response headers well-formed | Met | |

### V14 Configuration

| Req | Summary | Status | Evidence |
|---|---|---|---|
| 14.1.1 to 14.1.4 | Repeatable, hardened builds and deployment | Met | Bicep + `validate-infra.sh`, Dockerfile, CI; nothing deployed yet |
| 14.2.1 | Components up to date and free of known vulnerabilities | Met | `scripts/check-vulnerable-packages.sh` (0 High/Critical on this branch), Dependabot, `flutter pub outdated` artifact |
| 14.2.3 | Subresource integrity for CDN assets | N/A | No third-party script on any page; the viewer is built with `--no-web-resources-cdn` |
| 14.2.4 | Components from trusted repositories | Met | nuget.org, pub.dev |
| 14.2.5 | Software bill of materials | Not met | Not generated; `dotnet list package` and `pubspec.lock` are the inventory |
| 14.3.1 | No stack traces or debug output | Met | `Sec/PublicExposureTests.cs` |
| 14.3.2 | Debug modes off in production | Met | Behaviour keyed on the environment name; startup refuses missing secrets outside Development/Test |
| 14.3.3 | No version banners | Met | Kestrel `Server` header disabled (`Program.cs`); the test server has no Kestrel, so this is checked by reading the code |
| 14.4.1 | Safe Content-Type with charset | Met | |
| 14.4.2 | `Content-Disposition: attachment` on API responses | Not met | Not set; JSON responses are not served to browsers as documents. Low value |
| 14.4.3 | Content-Security-Policy | Met | Share, legal and admin pages: `default-src 'none'`, nonce styles, `frame-ancestors 'none'`: `Sec/HeadersAndCorsTests.cs` |
| 14.4.4 | `X-Content-Type-Options: nosniff` on all responses | Met | `Common/SecurityHeaders.cs`; test |
| 14.4.5 | HSTS on all responses | Met | As V9 |
| 14.4.6 | Referrer-Policy | Met | `no-referrer` (admin), `strict-origin-when-cross-origin` (public pages) |
| 14.4.7 | Framing restricted | Met | CSP `frame-ancestors 'none'` and `X-Frame-Options: DENY` |
| 14.5.1 | Only the HTTP methods in use are accepted | Met | |
| 14.5.2 | `Origin` not used for authentication | Met | CORS only |
| 14.5.3 | Strict CORS allow-list | Met | One origin, no wildcard, no credentials, reads only: `Common/CorsSetup.cs`, `Sec/HeadersAndCorsTests.cs` |
| 14.5.4 | Headers set by a proxy are trusted only from it | Met | `CF-Connecting-IP` only from Cloudflare ranges; `Abuse/ClientIpResolverTests.cs`, `ClientIpBehindIngressTests.cs` |

## Part 2: MASVS v2 (Flutter app)

Targets MAS-L1 (standard security) plus the privacy controls. MASVS-RESILIENCE is the optional defence-in-depth profile; it is not
pursued (see its rows). Every row that needs a physical device says so.

| Control | Requirement | Status | Evidence and notes |
|---|---|---|---|
| STORAGE-1 | Sensitive data stored securely | Met; Needs device | Session (access and refresh token) in `flutter_secure_storage` (Keychain, Android Keystore-backed): `app/lib/core/auth/session_store.dart`. Verify on a device that nothing leaks into shared preferences (only recent searches live there) |
| STORAGE-2 | No sensitive data leaks to backups, logs, screenshots, clipboard, keyboard cache | Partially; Needs device | Android cloud backup disabled and cleartext forbidden (`AndroidManifest.xml`, `app/test/security/platform_config_test.dart`); Sentry and logs scrubbed (`app/lib/core/crash/pii_text.dart`). Not done: hiding the app snapshot in the task switcher, secure keyboard for the email field. Needs device |
| CRYPTO-1 | Strong, current cryptography | Met | Only SHA-256 (Apple nonce) via the `crypto` package; transport crypto is the OS's. No home-made crypto |
| CRYPTO-2 | Proper key management | N/A | The app holds no keys of its own; App Attest keys live in the Secure Enclave |
| AUTH-1 | Secure authentication protocols | Met; Needs device | Apple and Google OIDC with a fresh nonce per attempt, verified by the server; no passwords. Sign-in on a real device needs the owner's Apple/Google client ids (`app/README.md`) |
| AUTH-2 | Local authentication | N/A | No biometric or PIN gate |
| AUTH-3 | Step-up for sensitive operations | Not met | Deleting the account asks for confirmation but not re-authentication. The only credential is the session, and deletion has a short recovery-free window. Consider re-auth before launch |
| NETWORK-1 | TLS everywhere, secure configuration | Met; Needs device | No cleartext in the manifest or Info.plist (`platform_config_test.dart`); release builds require an `https://` `API_BASE_URL` and refuse to start otherwise (`app/lib/core/config.dart`, `app/test/core/config_test.dart`) |
| NETWORK-2 | Certificate pinning | Not met | Not implemented, by choice: a code-name product with changing certificates and a Cloudflare front makes pins a self-inflicted outage risk. Revisit if the threat model changes |
| PLATFORM-1 | IPC and deep links handled securely | Partially; Needs device | Only `MainActivity` is exported; the `vela://open/s/{id}` scheme and https app links carry only a public story id and are validated by `go_router`. App-link verification (`assetlinks.json`, AASA) happens at deploy |
| PLATFORM-2 | WebView use secure | N/A | No WebView; legal and store links open the system browser, https only (`url_launcher`) |
| PLATFORM-3 | UI does not leak data | Needs device | No overlay/tap-jacking protection added; needs a check on device |
| CODE-1 | Platform and SDK up to date | Needs owner | Flutter 3.47.7; `minSdk` and iOS deployment target are the Flutter defaults; confirm against store requirements at release |
| CODE-2 | Forced update mechanism | Not met | None; old app versions keep working against the API until the contract breaks |
| CODE-3 | Vulnerable components managed | Partially | `flutter pub outdated` report and Dependabot; there is no Dart advisory scanner, so pub advisories rely on pub.dev notices. Two young single-maintainer packages are called out in the ledger (`app_attest`, `sentry_flutter`) |
| CODE-4 | All untrusted input validated | Met | Typed generated client; server responses parsed defensively (notifications use a tolerant parser); deep links parsed by the router |
| RESILIENCE-1 to 4 | Tamper, root, debugger, obfuscation | N/A by decision | MAS-R is not targeted. The substitute is server-side attestation (App Attest, Play Integrity), which is implemented but disabled until the owner has the developer accounts (R-3). Release builds should use `--obfuscate --split-debug-info` (owner, with the signing keystore) |
| PRIVACY-1 | Minimise data collected | Met | Handle, name, optional bio; no analytics SDK; Sentry events scrubbed of user data |
| PRIVACY-2 | Prevent identification and tracking | Met | Login email never exposed; public photos carry no EXIF or GPS |
| PRIVACY-3 | Transparency about data use | Needs owner | Privacy policy, App Store privacy labels and Play data-safety form are the owner's to complete with counsel |
| PRIVACY-4 | User control over their data | Met | In-app export and account deletion; recent searches cleared on sign-out |
