# S1b: independent security review findings

Branch `build/mvp` at `bfd750b` (S1a = `d0a4d86..d012256`). Reviewer: `security-reviewer` agent, 2026-10-24. Scope: the whole API
(`api/src/Vela.Api`), the Flutter app's security surface, `infra/` and `.github/workflows`. I changed no source code. I read the
code, scrutinised the S1a diff, and wrote one proof of concept outside the repo (scratchpad console app that calls the real
`VariantGenerator`). Earlier conclusions in `review-findings.md`, `threat-model.md` and the ledger were checked against the
code, not assumed. Where one of them is wrong, the finding says so.

Severity scale: **CRITICAL** = remote compromise of accounts/data with no preconditions. **HIGH** = one ordinary account can take
the service down or defeat the moderation/evidence controls. **MEDIUM** = a real abuse path with limits or preconditions.
**LOW** = hardening or narrow impact. **INFO** = checked and sound, or a note.

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| HIGH | 3 |
| MEDIUM | 6 |
| LOW | 11 |
| INFO | see section 5 |

---

## 1. HIGH

### S1b-1 (HIGH): Any signed-in user can exhaust API memory with in-request decodes of a 251 KB image (the "semaphore" does not exist)

**Where**
- `Profiles/ProfileService.cs:60` (`PATCH /v1/me/profile` with `"avatarUploaded": true`) → `Images/VariantGenerator.cs:30` `GenerateAvatarAsync`, inside the HTTP request.
- `Works/WorkService.cs:37-48` (`POST /v1/works/{id}/publish`) → `VariantGenerator.GenerateAsync`, inside the request **and inside a DB transaction holding `FOR UPDATE` on the work row** (`LockOwnedAsync`).
- `Images/VariantGenerator.cs:79-82`: the only guard is `width*height <= 60,000,000`; then a full RGBA8888 bitmap (~240 MB native) is allocated. With an EXIF rotation, `ImageOrientation.Apply` allocates a second one. `ReadSourceAsync` also holds up to 2 × 25 MB managed copies.
- There is **no concurrency limit anywhere**: `grep -ri semaphore api/src` finds only the JWKS gate. `threat-model.md` line 107 ("Decode limits and a semaphore") and R-11 ("Concurrency semaphore") are false, so F-A7-4 and R-11 were accepted on a premise that does not hold.
- Production container: `infra/modules/containerapp.bicep:171`, `cpu 0.5, memory 1Gi`, max 2 replicas.

**Attack (normal user, one account).** Upload a 7745×7745 single-colour PNG with its last 64 bytes cut off (251 KB) through the
avatar SAS URL. Then send N parallel `PATCH /v1/me/profile {"avatarUploaded":true}`. Each call decodes the whole image, then
fails as `corrupt`. That failure path deliberately **keeps the source blob** (`VariantGenerator.cs:43`: the source is deleted only
on success), so the attack repeats for free. The only limiter is `write` at 60/min per user, a fixed window with no queue and no
concurrency cap, so 60 requests can be in flight at once. The publish path works the same way on a draft whose upload fails to
decode, and each of those requests also pins a pooled Npgsql connection and a row lock while it decodes and uploads.

**Proof (measured).** A scratch console app (outside the repo) calls the real `VariantGenerator.GenerateAvatarAsync` with an
in-memory `IBlobStore` that serves the truncated PNG:

```
concurrency=1 results=corrupt                      elapsed=436ms peakRSS(VmHWM)=271 MB  source still present=True
concurrency=2 results=corrupt,corrupt              elapsed=294ms peakRSS(VmHWM)=493 MB  source still present=True
concurrency=4 results=corrupt x4                   elapsed=488ms peakRSS(VmHWM)=943 MB  source still present=True
concurrency=8 results=corrupt x8                   elapsed=698ms peakRSS(VmHWM)=1848 MB source still present=True
```

About 230 MB of resident memory per concurrent request, from a 251 KB upload. Five parallel requests exceed the 1 GiB container
limit and get it OOM-killed. Both replicas can be kept down from one account, and every user's in-flight request is lost.

**Fix**
1. Take decoding out of the request path. Avatar: accept the PATCH, queue the work, and return the avatar asynchronously. Publish: mark the work `pending` and let `PublishPipeline` (already single-threaded per instance) generate the variants. At the least, put a process-wide `SemaphoreSlim(1..2)` around `Decode`, using `WaitAsync` with a short timeout and returning 503 or `invalid_image` when the wait expires.
2. Decode at reduced size. Use `SKCodec.GetScaledDimensions` / `SKImageInfo` with a sample size chosen so the decoded long edge is ≤ 2400 px (the largest variant). Skia's JPEG and WebP decoders can downscale while decoding, so a 60 MP source then costs about 25 MB.
3. Lower `MaxPixels` to what the product needs (for example 40 MP), and count the rotation copy in the budget.
4. Delete (or quarantine) the source after any `InvalidImageException`, so a failing upload cannot be replayed.
5. Do not hold the `FOR UPDATE` transaction across decode and blob I/O in `RequestPublishAsync`.
6. Correct `threat-model.md` (line 107 and R-11) and add a test that runs N concurrent publishes or avatar generations and checks that peak concurrency stays within the cap.

### S1b-2 (HIGH): Uploaders can destroy flagged content and its moderation record before an admin acts, then sign up again with the same identity

**Where**
- `Works/WorkService.cs:64-72` `DeleteAsync`: deletes the variants, the **private original** and the row for **any status**, including `blocked` (auto-blocked by Content Safety), `pending`, and report-hidden works. There is no check for a pending report or a hold.
- `Social/ReplyService.cs:75-81` `DeleteAsync`: the author can delete a reply that was auto-hidden as `unsafe_text`.
- `Account/AccountEndpoints.cs:23` + `Account/DeletionWorker.cs:17,54`: `DELETE /v1/me` is followed by a hard delete **within about a minute**, with no grace period. `Account/DeletionJob.cs:103` deletes every report about the user's works and replies (`EvidenceHoldGuard.Deletable` is the identity function, `EvidenceHoldGuard.cs:12`). Identities are deleted and **no `BannedIdentities` row is written**, so `AccountResolver.SignInAsync` lets the same Apple, Google or email identity create a fresh account at once.
- Only a completed admin `MarkIllegal` (evidence hold + ban) protects content. Everything before that point can be destroyed by its owner.

**Attack (malicious artist).** Upload illegal imagery and publish it. The scanner auto-blocks it (severity ≥ 4) and queues an
`auto_flag`, or another user reports it as `illegal_content`. The uploader sees the work is blocked or hidden (`GET /v1/works/{id}`
shows them `status`) and calls `DELETE /v1/works/{id}` or `DELETE /v1/me`. The original, the variants, the chapters and (for
account deletion) the reports are gone before anyone reviews them. The person then signs in again with the same Apple ID.
For an 18+ UGC service with a documented evidence-hold and NCMEC process (threat-model A-3), this defeats the process at its
weakest point: the queue between detection and review. Once a provider has actual knowledge, 18 U.S.C. §2258A obliges it to
report and preserve, so counsel should judge exposure. Technically, the evidence is unrecoverable: blob soft delete is off, see S1b-15.

**Fix**
- `WorkService.DeleteAsync`: if the work is `blocked`, `removed`, `ReportHidden`, or has any pending report, do **not** hard-delete. Hide it from the owner (return 200 to the owner, so nothing is revealed) and keep the row and the original until an admin resolves the item. The same rule applies to replies with `Hidden && HiddenBy is Auto or Report`, or with pending reports.
- `DeletionJob`: before phase A, if the user has blocked or removed works, auto-flags or `illegal_content` reports pending, or any report on their content still pending, either skip them (retry later and surface them in the admin queue as "deletion requested") or create an automatic preservation hold that copies the original, variants and snapshot into `evidence/`, as `CaptureAsync` does, before deleting anything. Do not delete pending reports.
- Write `BannedIdentities` rows (or a "cooling-off" hash) for accounts deleted while they had pending `illegal_content` items or auto-blocks, so re-signup goes to review.
- Consider a deletion grace period of 7 to 30 days (the account is already invisible from `DeletedAt`).
- Add tests: delete a blocked work, delete an auto-hidden reply, and delete an account with a pending `illegal_content` report. In each, the original, the reports and a ban must survive.

### S1b-3 (HIGH): The upload SAS still allows writes after publish: the original can be swapped, so admins review, hold and restore a different image from the one the scanner and the public saw

**Where**
- `Images/AzureBlobStore.cs:38`: the draft upload URL is `Create | Write` on `originals/works/{id}` for 15 minutes, with no stored access policy, so it cannot be revoked. `Write` allows overwriting an existing blob.
- `Works/WorkService.cs:48`: the public variants are generated from the original once, at publish. `PublishPipeline.cs:151` scans `feed.webp` (the variant).
- The admin queue and report page show **only the original** (`Admin/Pages/Index.cshtml.cs:39`, `Report.cshtml.cs:19`), never the public variants.
- `EvidenceService.cs:105` copies the original at hold time. For an auto-blocked work the variants were already deleted by `TakeDownAsync`, so the hold contains only the original.
- `WorkVariantLifecycle.RestoreAsync` (`Admin/WorkVariantLifecycle.cs`) regenerates public variants from the original and sets `ModerationOverride = true` (the work is never scanned again).

**Attacks (malicious artist, all within 15 minutes of `createDraft`; publishing takes seconds).**
1. *Hide from review.* Publish image A, which is public. Then `PUT` benign image B to the still-valid SAS URL. Anyone who reports A sends the admin to a page that shows B, and the admin dismisses. A stays public.
2. *Destroy evidence.* Publish illegal A. The scan blocks it and the variants are deleted. Overwrite the original with B. "Mark illegal" now holds B: the evidence copy is not the content that was uploaded.
3. *Unscanned restore.* If an admin restores a blocked work while the URL is still live, the original can be swapped between review and click. The swapped image is published with `ModerationOverride = true`.

**Fix**
- At publish, inside `RequestPublishAsync`, copy (server-side) `originals/works/{id}` to a new, server-owned name such as `originals/works/{id}/sealed`, generate the variants **from that copy**, and use the sealed name for admin views, evidence, export and restore. The SAS URL is bound to the old blob name, so it can no longer reach the content that matters. Delete the unsealed blob afterwards.
- Alternatively, issue the SAS against a stored access policy per draft and delete the policy at publish.
- On the admin report page, show the public variant (`feed.webp`, when it exists) next to the original, and record a SHA-256 of the sealed original and of each variant in the work row and in the evidence snapshot.
- Test: after publish, a `PUT` with the original upload URL must not change what the admin page, the evidence hold or the restore read.

---

## 2. MEDIUM

### S1b-4 (MEDIUM): Unscanned images are public at a predictable CDN URL from the moment of publish

`WorkService.RequestPublishAsync` (`Works/WorkService.cs:48`) writes `works/{id}/thumb|feed|full.webp` into the **public** `works`
container (`infra/modules/storage.bicep:62`, `publicAccess: 'Blob'`, CDN-fronted). It does this while the work is still `pending`;
the scan runs later in `PublishPipeline` (5 s minimum, 30 s sweep, and up to about 9 minutes when the scanner errors, after which
the work is published anyway, fail-open). The URL is fully determined by the work id, which the uploader knows. So any image
is publicly served and edge-cacheable for that window, with no visibility check, whatever the scan later decides. If
`RequestPublishAsync`'s `SaveChanges` fails after generation, the variants stay public forever (orphans, noted F-A8-2).

*Fix:* generate variants into a private staging prefix (for example `originals/works/{id}/variants/`). Have the pipeline copy them
to `works/` only after a clean scan or an admin override. Generate them in the pipeline rather than the request, which also
helps S1b-1.

### S1b-5 (MEDIUM): Per-address limits in the database compare the full IPv6 address; the S1-8 DMCA mail-bomb fix is bypassable

The in-memory limiter buckets IPv6 by /64 (`Abuse/IpKey.cs`), but every DB-backed per-address limit stores and compares
`IPAddress.ToString()`:
- email code request 20/h: `Auth/EmailCodes/EmailCodeEndpoints.cs:51` → `EmailCodeService.cs:132`
- email code verify 30/h: `EmailCodeEndpoints.cs:69` → `EmailCodeService.cs:119`
- admin login: `Admin/Pages/Login.cshtml.cs:20,27`
- DMCA 5/day: `Legal/DmcaService.cs:50`
- inquiries 30/day: `Inquiries/InquiryService.cs:68`

Any host with a /64 (every residential IPv6 line, every cloud VM) gets 2^64 fresh "addresses", so these limits disappear. The
limit that is left is the global `write` limiter, 60/min per /64, per instance.

Consequence for the DMCA form (`POST /v1/legal/dmca-notices`, anonymous, no attestation, and **no `RequirePublicFormLimit`**,
`Legal/DmcaEndpoints.cs:16`): at about 86,000 notices a day per /64 per instance, every one with an acknowledgement email from the
service's own sending domain. `AckAllowedAsync` (`DmcaService.cs:78-79`) caps mails per *exact* lower-cased address, so
`victim+1@gmail.com`, `victim+2@…` and `v.ictim@gmail.com` each get 3 more. The S1-8 mail-bomb relay therefore still works against
one inbox, and complaints against the ACS sender domain would also hurt delivery of sign-in codes. The `LOWER(claimant_email)`
predicate has no supporting index, so each submission scans the whole `dmca_notices` table.

*Fix:* key every DB limit on `IpKey.Of(ip)` (store the /64 key, or both). Add `.RequirePublicFormLimit()` to the DMCA endpoint. Cap
acknowledgements per normalised mailbox (strip `+tag`; strip dots for gmail/googlemail) and per day globally, add an index on
`lower(claimant_email)`, and consider sending no acknowledgement at all until a notice is reviewed. Add a test that sends from
two addresses in one /64 and expects the shared limit.

### S1b-6 (MEDIUM): Email-code sign-in can be blocked for chosen victims, and the admin login page locks out every admin

- Verify (`/v1/auth/email/verify`) needs no attestation, and wrong guesses consume the victim's live code (`EmailCodeService.cs:78-81`, 5 attempts per code, then the code is dead). Combined with S1b-5's IPv6 rotation, an attacker can send one wrong guess per second for a target email, and every code that person requests dies within 5 seconds. It is a targeted, sustained sign-in denial for email-code users.
- The admin login page (`Admin/AdminLogin.cs:23-26`) lets anyone, with no attestation, trigger `codes.RequestAsync` for an allow-listed address. Five requests per hour exhaust that address's per-email request budget, so the admin's own requests are silently refused (`RateLimitedException` is swallowed). Wrong guesses through the public verify endpoint kill whatever code did arrive. An attacker who guesses an admin address (such as `admin@…` or the DMCA agent address) can keep moderators out during an incident. The 600 ms timing floor hides whether the address is allow-listed, but the attacker does not need to know.
- Horizontal brute force: 6 digits, 5 guesses per code, 5 codes per email per hour gives about 2.5×10⁻⁵ per hour per account. Across 10,000 targeted addresses, that is roughly 6 takeovers a day for an attacker with residential proxies. Code requests need attestation in production, but see S1b-8: tokens are replayable.

*Fix:* count failed verifies per email across codes (for example 10 per 24 h, with exponential back-off), not only per code. Put verify behind the `auth-email` limiter keyed by `IpKey`. Do not let a wrong guess from IP X burn a code that was requested from IP Y (bind the attempt counter to the requesting /64, or require a request token returned by `/request`). For admins, add a second factor (passkey/WebAuthn or TOTP) or an IP allow-list for `/admin`, and do not let unauthenticated callers spend an admin address's request budget (give the admin page its own budget). Alert on repeated admin code requests.

### S1b-7 (MEDIUM): Upload size is unbounded, originals are never aged out, and the export worker copies them to local disk

The SAS URL (`AzureBlobStore.cs:38`) cannot cap size. `Write` allows Put Blob (up to 5000 MiB) and Put Block/Put Block List
(far larger). `Create|Write` also lets the holder start a server-side copy from any URL, which moves data without using the
attacker's bandwidth. F-A7-3 was accepted with "a storage lifecycle rule must" cap it. The only lifecycle rule in
`infra/modules/storage.bicep` is for `exports/`, and the triage says originals are "deliberately never aged out". A failed avatar
leaves `originals/avatars/{userId}` in place forever (overwritten on the next try only). `ExportArchiveBuilder.BuildAsync` streams
**every** work original, drafts included, into a temp file on the container's ephemeral disk (`Account/ExportArchiveBuilder.cs`),
so one oversized original fills the replica's disk and fails the export worker.

*Fix:* issue upload URLs only for a client-declared size, and check `Content-Length` with a `GetProperties` call before any read,
publish or export. Reject anything over 25 MB and delete it. Add lifecycle rules: `originals/avatars/` after 1 day; `originals/works/`
blobs without a row (or tag blobs `state=draft` and age those out after 2 days). In `ExportArchiveBuilder`, skip or cap any blob
over the limit.

### S1b-8 (MEDIUM): Attestation is the only bot gate on sign-in, and a captured token is replayable without limit for 5 to 10 minutes

`AppAttestVerifier` accepts any fresh `"{action}|{unix}"` challenge the client chose (`Abuse/AppAttestVerifier.cs:199-205`), and
`PlayIntegrityVerifier` binds only `requestHash = SHA-256(action)` (`PlayIntegrityVerifier.cs:92`). Neither records a token or key
as used. One genuine app on one real device, behind a MITM proxy (the app has no pinning), yields a token that a script can
replay for every `sso` / `email_request` / `create_work` call, from any IP, for 5 minutes (iOS) or 10 minutes (Android), then mint
another. This is tracked as F-A15-1/2 and F-B14-1 (Owner). It is listed here because S1b-5 and S1b-6 rely on attestation as the
remaining control, and so does the threat model's signup-abuse story. *Fix:* issue a server nonce (`/v1/attest/challenge`, single
use, 2-minute TTL, kept in the DB), bind it into the challenge / `requestHash`, and mark it used on success. For iOS, move to
attest-once-per-install with assertions and counters.

### S1b-9 (MEDIUM): Evidence holds are not tamper-evident and can be deleted by the app's own credentials

`EvidenceSnapshotBuilder` (`Admin/EvidenceSnapshotBuilder.cs`) records no content hash of the copied blobs. The evidence container
is a normal container in an account with `allowSharedKeyAccess: true`, `deleteRetentionPolicy: { enabled: false }`
(`infra/modules/storage.bicep`), and no immutability policy. The API's account key (in Key Vault and in the app's environment)
can overwrite or delete held evidence, and so can anyone who obtains it (insider, leaked connection string, RCE). The
`admin_actions` table is still mutable by the app role (F-A14a-3 Open). *Fix:* store SHA-256 for the original and each variant in
the snapshot and show it on the evidence page. Enable blob soft delete and versioning. Put a time-based immutability policy (WORM,
365 days, unlocked for now) on the `evidence` container. Use a separate identity with write-only (no delete) rights for evidence
writes.

---

## 3. LOW

| ID | Finding | Where | Fix |
|---|---|---|---|
| S1b-10 | **An admin's mobile API token carries admin read rights.** `WorkReader.FindViewableAsync` and `BuildAsync` give any bearer token whose user has `IsAdmin` access to every work (drafts, blocked, removed, evidence-held text and chapters) and its replies. The token is the app's 30-day refresh session: no allow-list re-check per request, no session stamp, no 8-hour limit. | `Works/WorkReader.cs:22,41,61` | Remove the admin branch from the public API (admins review in `/admin`). If it is needed, require the admin cookie, not the bearer token. |
| S1b-11 | **No guard against review mode overlapping the admin allow-list.** An address in both `Auth:ReviewEmails` and `Admin:Emails` signs in to `/admin` with the fixed review code shared with Apple reviewers (`AdminLogin.VerifyCoreAsync` → `codes.VerifyAsync` → `ReviewCodeAcceptedAsync`). | `Auth/EmailCodes/EmailCodeSettings.cs`, `Admin/AdminSettings.cs` | Refuse to start when the two sets intersect, and have `AdminLoginService` never accept the review path. |
| S1b-12 | **The Sentry header drop list names the wrong attestation header.** It drops `X-Vela-Attestation`, but the header is `X-Attestation`. Replayable attestation tokens (S1b-8) reach Sentry with unhandled exceptions on the `sso`, `email_request` and `create_work` routes. `PiiScrubber`'s JWT pattern only partly masks a 5-part JWE and does not match App Attest tokens. | `Telemetry/SentryScrubber.cs:14`, `Abuse/AttestationMiddleware.cs:17` | Use `AttestationMiddleware.TokenHeader` in the set, and add a test that sends the real header. |
| S1b-13 | **Private API responses carry no `Cache-Control`.** `/v1/me`, `/v1/me/export/{id}` (contains a 15-minute SAS download URL), `/v1/notifications`, and owner views of drafts. Safe with Cloudflare's defaults; one "cache everything" rule on the API host would serve one user's data to another. | `Common/SecurityHeaders.cs` | Send `Cache-Control: no-store` on every `/v1/*` response sent to an authenticated caller, or on all of them, with explicit public caching only where intended. |
| S1b-14 | **SSO replay.** The nonce is chosen by the client and never recorded server-side, and `jti` is not tracked. A captured Google `id_token` plus request body is a valid sign-in for the token's lifetime (about 1 h); for Apple, about 10 min. No `azp` check for Google. | `Auth/Identity/OidcIdentityVerifier.cs:62-64` | Issue server nonces (same store as S1b-8), keep used `jti`s until `exp`, and check Google `azp` against the configured client ids. |
| S1b-15 | **Blob soft delete is disabled on the whole account**, so S1b-2's deletions, a bad sweep or a compromised key are unrecoverable. | `infra/modules/storage.bicep` (`deleteRetentionPolicy: false`) | Enable blob and container soft delete (7 to 14 days), and blob versioning for `originals` and `evidence`. |
| S1b-16 | **Ban evasion and unverified service mail.** Email bans hash the lower-cased address only, so `a+1@gmail.com` and `a.b@gmail.com` evade them, and so does signup velocity. `Identity.Email` is stored from Google even when `email_verified` is false, then used by `LoginEmail.ForUserAsync` for inquiry alerts and export notices. | `Admin/BanList.cs:10`, `Auth/Identity/AccountResolver.cs:29`, `Auth/LoginEmail.cs` | Canonicalise known providers before hashing (strip `+tag`, gmail dots). Store `Email` only when `EmailVerified`. |
| S1b-17 | **The Android release build is signed with the debug key** (template TODO). Debug keystores are not secret: if the release ships like this, or `Links:Sha256Fingerprints` is set to the debug SHA-256, any developer's debug-signed APK can claim the app links or update a sideloaded install. Play Integrity's `certificateSha256Digest` allow-list would also be meaningless. | `app/android/app/build.gradle.kts:40-42` | Release-blocker: a real upload key via `key.properties` (not committed). Fail the release build when the debug config is used. |
| S1b-18 | **The custom scheme reaches every route.** Flutter deep linking hands `vela://open/<path>` to go_router, so any page or app can open `/settings`, `/profile/edit` or `/create` in a signed-in session. The `vela` scheme can be claimed by other Android apps. No destructive action happens without a confirmation dialog, so the impact is UI spoofing or nuisance. | `app/lib/core/router.dart`, `AndroidManifest.xml` | In `redirect`, allow only `/s/:id` and `/story/:id` for links coming from the `vela` scheme (or a `RouteInformationProvider` filter). |
| S1b-19 | **The uploader controls the original's `Content-Type`.** Through the SAS URL (`x-ms-blob-content-type`), the uploader can make the original `text/html`. It is served from the blob origin when an admin opens the thumbnail URL directly, and it travels into exports. The blob origin holds no Vela session, so the impact is low. | `Images/AzureBlobStore.cs:33-39` | Set `ContentType = application/octet-stream` and `ContentDisposition = attachment` on read SAS URLs (`BlobSasBuilder.ContentType` / `ContentDisposition`), or re-set properties when sealing (S1b-3). |
| S1b-20 | **Inquiry emails relay attacker text.** The artist receives the sender's message verbatim and an unverified "Reply to" address, from Vela's own sender domain. Limited to 10 per sender per day, but a convincing phishing channel against artists. | `Inquiries/InquiryService.cs:105-109` | Add a fixed footer ("Vela did not verify this address; never share your sign-in code"), and consider verified reply addresses or relayed replies. |

A single user report hides a work at once (`Social/ReportService.cs:51` → `HideAsync`). `threat-model.md` R-2 says "a coordinated
group", but one account (10 reports a day) is enough. I have not listed this as a separate finding because it is an accepted product decision; R-2 should be reworded.

---

## 4. Regressions or bypasses in S1a (`d0a4d86..d012256`)

- **S1-8 (DMCA mail-bomb)**: bypassable, see S1b-5 (plus-addressing, IPv6 rotation, no `public-form` policy on the endpoint).
- **S1-3 (refresh race)**: correct. I traced the remaining interleavings. A suspension can commit between the claim and the insert, and then the new token survives `RevokeAllAsync`, because its row is not in that statement's snapshot. That token is useless: the next refresh re-reads `SuspendedAt` and revokes the family, and `OnTokenValidated` refuses its access token after eviction or 20 s. No change needed.
- **S1-6 (`/admin` rate limited)**: correct. Admin traffic is classified as anonymous (the cookie is not the default scheme) and keyed per IP, which is right.
- **S1-10 (session stamp)**: correct. The stamp is checked on every request in `AdminSessionValidator`. Signing in elsewhere ends the old session, as intended.
- **S1-14 (login timing floor)**: correct, but see S1b-6: equal timing does not stop the lockout.
- **S1-1 (CORS)**: correct: exact origin match, reads only, no credentials, nothing for `/admin`.
- **S1-2 / S1-16 headers and `nbf`**: correct.
- **F-A7-4 / R-11 / threat-model line 107**: the "semaphore" claim is false (S1b-1). **F-A7-3**: accepted on a lifecycle rule that does not exist (S1b-7).

---

## 5. INFO: checked and found sound

- **JWT**: HS256 only (`ValidAlgorithms`), key ≥ 32 bytes and required at startup, issuer, audience and lifetime enforced with zero skew. `kid`/`jku`/`x5u` are never read. The user's state is checked on every request (`OnTokenValidated` + `UserStateCache`). Refresh tokens are 256-bit random, stored as SHA-256, rotated, with family revocation on reuse. The revoke-on-suspend and revoke-on-delete paths call `RevokeAllAsync` and evict the cache.
- **OIDC**: RS256 only, `kid` required, keys only from the hard-coded Apple/Google JWKS URIs (no attacker key URL), issuer and audience checked (empty audience fails closed), nonce compared in constant time. The JWKS cache has a per-URI gate and a cool-down.
- **Email codes**: CSPRNG 6 digits, HMAC with pepper, constant-time compare, single use via an atomic consume, attempts counted before compare. Responses do not reveal whether an account exists. Review mode has an advisory-locked failure counter.
- **Admin**: separate cookie scheme with `HttpOnly`, `Secure` (prod), `SameSite=Strict`, path `/admin`, 8 h absolute, no sliding. The allow-list, `IsAdmin`, suspension, deletion and stamp are all re-checked on every request. Default-deny `AuthorizeFolder("/")`; Razor Pages antiforgery on every POST. CSP `default-src 'none'`, no script, style nonce, `form-action 'self'`, `frame-ancestors 'none'`, `no-store`, `no-referrer`. All user text is Razor-encoded (no `Html.Raw` anywhere). Targets come from the stored report, not the form. Ban targets cannot be self or an allow-listed admin. Every action is audited in the same transaction. No open redirects (fixed paths only, no return URL).
- **Injection**: no `FromSqlRaw`/`ExecuteSqlRaw`/`SqlQueryRaw`. All raw SQL is interpolated (parameterised). LIKE input is escaped in search and admin user search, and trigram functions take parameters.
- **IDOR**: owner checks on update, publish, delete of works (`artist_id` in the locked query), replies, notifications (`user_id` scoped), exports (`user_id` scoped), counter-notices (owner and status under `FOR UPDATE`). Report targets must be visible to the reporter. Hidden, unpublished and blocked content answers 404 exactly like missing content.
- **Cursors**: unsigned but strictly parsed (segment 0/1, ticks bounded, GUID N format, rank ≤ 3). Tampering only moves a keyset position inside the caller's own visible set. Limits are clamped to 50 (admin 100).
- **Share and legal pages**: every dynamic value goes through `HtmlText.E` (control and separator stripping, then HTML encoding), images must be https, CSP has a nonce and no script, no redirects. Markdown comes only from embedded resources with `DisableHtml()`. Well-known files are built from configuration.
- **Images**: magic-byte sniff must agree with Skia's detected format; JPEG/PNG/WebP only; pixel cap applied before allocation; variants are re-encoded (no EXIF, GPS or ICC); originals, exports and evidence are private and refused by `PublicUrl`; the startup `ContainerAccessGuard` checks container access levels. Blob names are built only from GUIDs, and `Split` refuses `..`, `\`, `?` and `#`. No user-supplied URL is ever fetched server-side (no SSRF): website fields are stored, never dereferenced.
- **Parsers**: CBOR depth, item count and length bounds checked with overflow-safe arithmetic; JWE/JWS in Play Integrity verified with fixed algorithms; RFC 3394 unwrap with constant-time ICV; the request body is capped at 1 MB with a JSON depth limit of 32. Regexes have timeouts in `PiiScrubber`, and the DMCA story-link regex is anchored on input of at most 500 characters. No XML or `BinaryFormatter`. The export ZIP has fixed entry names (no zip-slip, because nothing is ever unzipped).
- **Client IP**: `CF-Connecting-IP` is trusted only from Cloudflare ranges, and `X-Forwarded-For` is read only at the configured hop from the right. Values like `1.2` are refused. Behind Container Apps ingress with hops=1 this resolves correctly, provided origin access is limited to Cloudflare (see F-C1-5: Authenticated Origin Pulls are still Owner). `AllowedHosts: *` is harmless because no code reads `Host`.
- **Telemetry**: API logs record exception type names only; OTel log records are scrubbed and drop exception objects; Sentry drops request bodies, cookies, Authorization and IP headers (except S1b-12). The app's crash scrubber is fail-closed, drops `ui.*` breadcrumbs and query strings, and nulls the user.
- **CI**: actions pinned by SHA, `permissions: contents: read`, OIDC (no stored Azure secret), dispatch inputs passed via `env` (no expression injection into `run`), gitleaks over full history with a checksum-verified binary. My own history grep for keys, peppers and tokens found only the documented dev-only values (`appsettings.Development.json`, Azurite's public key, test fixtures).
- **App**: tokens are kept in `flutter_secure_storage` and `Session.toString()` never prints them; the bearer token goes only to the API Dio, with uploads on a separate client; release builds refuse a non-https API base URL; `allowBackup=false`, `usesCleartextTraffic=false`; no `badCertificateCallback`, no WebView, `url_launcher` https only; `debugPrint` logs only `runtimeType`. Certificate pinning is absent, which is an accepted platform norm, but it makes S1b-8 cheap.

## 6. Not verified (needs a deployment or a device)

Container Apps' actual X-Forwarded-For hop count. Whether Sentry .NET includes request headers when `SendDefaultPii=false` (S1b-12;
inferred from the scrubber handling them). Cloudflare cache rules on the API host (S1b-13). OOM behaviour on Container Apps itself:
the PoC measured RSS on WSL, and the 1 GiB limit comes from the Bicep file. iOS keychain accessibility, and token survival across
uninstall and reinstall.

---

## 7. Disposition (S1b-fix, 2026-10-24)

Every finding has a status. Fixed items name the commit subject and the pinning tests; details in `review-findings.md` section 5.

| ID | Severity | Status | Note |
|---|---|---|---|
| S1b-1 | HIGH | Fixed | Header-only pixel check, reduced-size JPEG/WebP decode, 160 MB decode budget, `ImageDecodeGate` (default 2, then 503 `busy`), failed uploads deleted, publish holds no row lock while decoding. `Images/DecodeLimitTests.cs`, `Works/PublishBoundsTests.cs`. R-11 and F-A7-4 corrected |
| S1b-2 | HIGH | Fixed in part | 409 `under_review` for work and reply deletion under review; account hard delete deferred up to 30 days. `Admin/UnderReviewDeleteTests.cs`, `Account/DeletionDeferralTests.cs`. **Open - owner decision:** ban or cooling-off on re-signup after deleting during review; identity reuse unchanged |
| S1b-3 | HIGH | Fixed | Sealed original + SHA-256 at publish, used by variants, scan check, admin pages, evidence, restore, export and deletion; upload deleted. `Admin/SealedOriginalTests.cs` |
| S1b-4 | MEDIUM | Open - deferred | Random variant names conflict with the fixed-name CDN purge fallback (`CloudflareCdnPurger` expands a prefix into thumb/feed/full), the take-down purge list and the evidence copy, and would not hide the URL from the uploader, who reads it from the owner view of the work. The real fix is the reviewer's private staging prefix copied to `works/` after a clean scan; next task |
| S1b-5 | MEDIUM | Fixed | `IpKey.Of` for every DB per-address limit; `public-form` on the DMCA form; acknowledgements capped per normalised mailbox (indexed column). `Abuse/Ipv6DatabaseLimitTests.cs`. Not done: a global daily acknowledgement cap (Accepted: per-mailbox and per-/64 caps bound it) |
| S1b-6 | MEDIUM | Open - owner decision | Email-code lockout and admin sign-in hardening (per-email failure budget, admin second factor or IP allow-list) belong with the attestation/nonce design |
| S1b-7 | MEDIUM | Open - deferred | Upload size check before read, lifecycle rules for stale originals, export size cap: next task (needs a size-declaring upload API) |
| S1b-8 | MEDIUM | Open - owner decision | Server nonces for attestation; decision before enabling attestation (F-A15-1/2, F-B14-1) |
| S1b-9 | MEDIUM | Open - deferred | Evidence hashes in the snapshot, WORM policy, separate write-only identity. Blob soft delete now on (S1b-15); the sealed original's SHA-256 is on the work row |
| S1b-10 | LOW | Open - deferred | Remove the admin branch from the public API reader |
| S1b-11 | LOW | Open - deferred | Refuse overlapping review and admin addresses at startup |
| S1b-12 | LOW | Fixed | Sentry drops `X-Attestation` (API); the Flutter scrubber already drops all headers and now has a test. `TelemetryTests.Sentry_drops_the_real_attestation_header_s1b_12`, `app/test/core/crash_reporting_test.dart` |
| S1b-13 | LOW | Fixed | `no-store` on authenticated and non-GET `/v1` responses, `Vary: Authorization` on all `/v1`. `Security/CacheControlTests.cs` |
| S1b-14 | LOW | Open - owner decision | SSO server nonce and `jti` store, with S1b-8 |
| S1b-15 | LOW | Fixed | 7-day blob and container soft delete (`infra/modules/storage.bicep`); GDPR interplay in `docs/deploy.md` and the privacy text. Versioning not enabled (would keep overwritten originals): Open with S1b-9 |
| S1b-16 | LOW | Open - deferred | Canonicalise emails for bans and signup velocity (the DMCA `Mailbox` helper can be reused); store `Email` only when verified |
| S1b-17 | LOW | Open - owner decision | Release signing key (release blocker, needs the owner's upload key) |
| S1b-18 | LOW | Open - deferred | Restrict routes reachable from the `vela` scheme |
| S1b-19 | LOW | Fixed in part | Sealed originals are always stored as `application/octet-stream` (S1b-3). Read SAS content-type/disposition override: Open - deferred |
| S1b-20 | LOW | Accepted for now | Footer and verified reply addresses are product wording; Owner to approve text |
