# Vela (code name) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Vela mobile app (iOS, Android, plus a read-only web story viewer) and its Azure-hosted API: annotated photo stories with pins, replies, portfolios, own SSO/email auth, quotas, search, GDPR delete and export.

**Architecture:** Contract-first. `contract/openapi.yaml` is the single source of truth; the .NET API implements it and a Dart client is generated from it. The Flutter app (one codebase, mobile + web viewer target) talks only to that client. Infra is Bicep. Tracks A (API), B (app), C (infra) run in parallel after Foundation.

**Tech Stack:** .NET LTS (10.x) minimal API, EF Core + Npgsql, PostgreSQL 16, Testcontainers, Azure Blob/ACS Email/AI Content Safety, Azurite, Flutter stable (Riverpod, go_router, `sign_in_with_apple`, `google_sign_in`, `flutter_secure_storage`), openapi-generator (dart-dio), Bicep, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-22-vela-design.md` (plus the visual spec `design_handoff_vela_app/README.md` and prototype `Vela App.dc.html`). Executors read both; where they disagree the design doc wins.

## Global Constraints

- "Vela" is a code name: all user-facing strings, bundle ids, domains come from one place (`api/src/Vela.Api/Branding/BrandingOptions.cs`, `app/lib/core/branding.dart`).
- No passwords anywhere. Sign-in = Apple, Google, or email one-time code. No account linking or merging; each identity (provider + subject) is its own user.
- Email code: 6 digits, stored hashed, expires 10 minutes, max 5 attempts, rate-limited per email and per IP.
- Freemium tier: `max_works = 20`; counts statuses `draft`, `pending`, `published` (not `blocked` or `removed`); deleting frees a slot; checked in a transaction. Drafts expire after 24 hours. Max 40 pins per story. Data export rate limit: one per day.
- Adults only: users confirm 18+ and accept terms at onboarding.
- Handle: 3-20 chars, lowercase `a-z0-9_`, unique, changeable at most once per 30 days. Bio max 280 chars.
- Pins: `x`,`y` in [0,1]; `zoom` nullable, null = "stay put"; default zoom 2.4 when set.
- Camera: transition 900 ms, `cubic-bezier(.2,.7,.1,1)`; math exactly as in handoff README (section 2). Stay put = scale 1, tx 0, ty 0, spotlight at the pin's screen position.
- Moderation: Content Safety severity >= 4 **auto-blocks** (story status `blocked`, reply `Hidden` with `HiddenBy = 'auto'`) and always adds an `auto_flag` item to the admin queue, where an admin can restore or confirm. If the scan is unavailable after retries the content is published and a `scan_unavailable` item is queued (fail-open, designer's call). User reports hide the reported reply/work pending review. Only an admin sets a story to `removed` or restores a `blocked` one; restored content is never auto-blocked again. Story status: `draft`, `pending`, `published`, `blocked`, `removed`. Stronger image recognition (e.g. known-CSAM hash matching) is a later design.
- Reports: each account may file at most `Reports:MaxPerUserPerDay` (default 10, configurable) per UTC day.
- Blocks are mutual in effect and remove follows both ways.
- API envelope for every response: `{ "success": bool, "data": T|null, "error": {"code": string, "message": string}|null, "page": {"nextCursor": string|null}|null }`. Lists use cursor (keyset) pagination.
- Account deletion deletes all user data (rows, blobs, CDN purge); hard delete in primary DB, backups expire within 30 days. Logs contain no personal content.
- Test-first, 80%+ coverage on API and Flutter logic; immutable data models; files under 800 lines, functions under 50.
- No Firebase, Redis, search engine, websockets, or push. Notifications are DB rows fetched over REST.
- Market is the United States; Azure region is a US region (default `eastus2`, parameterized).
- Suspended users: all tokens revoked immediately, sign-in returns 403 `account_suspended`, re-signup with the same identity refused (store only a hash of provider + subject).
- Illegal-content evidence is held for at least 1 year in a restricted container; account deletion (A13) must not purge held evidence. Every admin action is written to an audit log.
- Sentry is the crash/error reporter (app and API); events and logs must contain no email addresses and no reply, inquiry, or bio text.
- Rate limits are enforced server-side per policy (A15) and the API client IP is trusted from `CF-Connecting-IP` only when the request comes from Cloudflare.

## Review Focus

1. **Two simultaneous draft creations at 19 stories** — exactly one succeeds, the other gets `quota_exceeded` (A7).
2. **Pins at the extreme edges** (x=0, y=1) and photos that are not 3:4 — spotlight stays on screen and the camera clamps correctly (B3, B4).
3. **Hostile text** — HTML, very long, empty, emoji and right-to-left text in titles, replies, bios and inquiries is stored safely and HTML-escaped on the share-preview page (A8, A9, A12).
4. **EXIF-rotated phone photos** — pins land on the orientation the author saw (A7, B9).
5. **Replayed or brute-forced credentials** — a reused email code, a sixth attempt, and a reused rotated refresh token all fail; reuse revokes the token family (A2, A4).

---

## File Structure

```
contract/openapi.yaml                      source of truth
api/Vela.sln
api/src/Vela.Api/                          Program.cs, Branding/, Common/ (envelope, errors, cursor)
  Data/ (VelaDbContext, Entities/, Migrations/)
  Auth/ (TokenService, Identity/, EmailCodes/)  Profiles/  Works/  Social/ (Follows, Replies, Blocks, Reports)
  Feed/  Notifications/  Inquiries/  Search/  Images/  Moderation/  Share/  Account/ (Deletion, Export)
api/tests/Vela.Api.Tests/                  Testcontainers Postgres fixture, fakes
app/                                       Flutter (lib/core, lib/features/*, test/, integration_test/)
app/packages/vela_api_client/              generated, never hand-edited
infra/main.bicep, infra/modules/*.bicep
docker-compose.yml                         postgres + azurite
.github/workflows/{api,app,infra}.yml
```

## Dependencies and parallelism

T1 -> T2 -> then in parallel: Track A (A1 first; A2-A6 after A1; A7-A13 as listed; A14 needs A2, A4, A6, A8, A9, A13; A15 and A16 only need T1/A1 and can start early but attach to endpoints after the endpoints exist), Track B (B1-B3 need only T1/T2; screens use a mock server from the generated client until the matching API task lands; B14 after B2, B4, B6), Track C. A13 is last of the original API tasks, A14 modifies it. B13 and B14 last in B. S1 runs last, after every other task.

---

## Foundation

### Task T1: Repo scaffold

**Files:** Create `.gitignore`, `docker-compose.yml`, `api/Vela.sln`, `api/src/Vela.Api/Program.cs`, `api/tests/Vela.Api.Tests/SmokeTests.cs`, `app/` (via `flutter create --org dev.vela --project-name vela_app app`), `app/test/smoke_test.dart`, `.github/workflows/{api,app}.yml`, `README.md`.

**Interfaces:** Produces: `GET /health` returns envelope with `data: {"status":"ok"}`; `Common/ApiEnvelope.cs` with `record ApiEnvelope<T>(bool Success, T? Data, ApiError? Error, PageInfo? Page)`, `record ApiError(string Code, string Message)`, `record PageInfo(string? NextCursor)`, and `static class Results { IResult Ok<T>(T data, string? nextCursor = null); IResult Fail(int status, string code, string message); }`.

- [ ] **Step 1: Write failing tests** — `SmokeTests.Health_returns_ok_envelope` (WebApplicationFactory GET /health: 200, `success == true`, `data.status == "ok"`); `ApiEnvelopeTests.Fail_returns_error_envelope_with_status` ; Flutter `smoke_test.dart` pumping `VelaApp` finds a `MaterialApp`.
- [ ] **Step 2: Run** `dotnet test api/Vela.sln` and `cd app && flutter test` — expect FAIL (types missing).
- [ ] **Step 3: Implement** the scaffold, envelope types, health endpoint; `docker-compose.yml` with `postgres:16` (port 5432, db `vela`) and Azurite; CI workflows run build + tests; README documents `docker compose up -d`, `dotnet test`, `flutter test`.
- [ ] **Step 4: Run** both test commands — expect PASS; `docker compose up -d && docker compose ps` shows both healthy.
- [ ] **Step 5: Commit** `chore: scaffold repo, api, flutter app, compose, ci`.

### Task T2: OpenAPI contract and generated client

**Files:** Create `contract/openapi.yaml`, `app/packages/vela_api_client/` (generated), `scripts/gen-client.sh`, `api/tests/Vela.Api.Tests/ContractTests.cs`.

**Interfaces:** Produces the full operation list below (operationId in parentheses). All paths are under `/v1`. Bearer JWT auth except where marked public.
- Auth (public): `POST /auth/sso` (ssoSignIn: provider, idToken, nonce) -> TokenPair; `POST /auth/email/request` (emailRequest); `POST /auth/email/verify` (emailVerify) -> TokenPair; `POST /auth/refresh` (refresh) -> TokenPair; `POST /auth/logout` (logout).
- Me: `GET /me` (getMe); `POST /me/onboarding` (completeOnboarding: displayName, handle, ageConfirmed, termsAccepted, followArtistIds); `PATCH /me/profile` (updateProfile); `POST /me/avatar-upload` (avatarUpload); `DELETE /me` (deleteAccount); `POST /me/export` (requestExport); `GET /me/export/{id}` (getExport).
- Artists: `GET /artists/{handle}` (getArtist, public); `GET /artists/{handle}/works` (listArtistWorks, public); `PUT /artists/{id}/follow` (follow); `DELETE /artists/{id}/follow` (unfollow); `GET /artists/{id}/followers` (listFollowers); `GET /artists/suggested` (suggestedArtists, public); `POST /artists/{id}/inquiries` (sendInquiry).
- Works: `GET /feed` (getFeed: tab=following|discover, cursor); `POST /works` (createDraft) -> {workId, uploadUrl}; `GET /works/{id}` (getWork, public); `PUT /works/{id}` (updateWork); `POST /works/{id}/publish` (publishWork); `DELETE /works/{id}` (deleteWork).
- Replies: `GET /works/{id}/chapters/{index}/replies` (listReplies, public); `POST` same path (addReply); `DELETE /replies/{id}` (deleteReply).
- Notifications: `GET /notifications` (listNotifications); `GET /notifications/unread-count` (unreadCount); `POST /notifications/read` (markRead).
- Search (public): `GET /search` (search: q, type=artists|stories, cursor).
- Safety: `POST /reports` (report: targetType, targetId, reason); `PUT /blocks/{userId}` (block); `DELETE /blocks/{userId}` (unblock).
- Non-API (not in contract): `GET /s/{workId}` share page, `/.well-known/*`, `/legal/*`, `/health`.

- [ ] **Step 1: Write failing test** — `ContractTests.Every_operationId_has_a_mapped_endpoint` loads `contract/openapi.yaml` and asserts each operationId exists as an endpoint name registered in the API (`.WithName(operationId)`); also `Contract_is_valid_openapi_3`.
- [ ] **Step 2: Run** `dotnet test api/Vela.sln --filter FullyQualifiedName~ContractTests` — expect FAIL.
- [ ] **Step 3: Implement** the YAML (schemas: `TokenPair`, `Profile`, `Work`, `Chapter{x,y,zoom?,title,body,meta?,altText?}`, `Reply`, `Notification`, `Inquiry`, `Page`, envelope wrapper) and `scripts/gen-client.sh` (openapi-generator `dart-dio` into `app/packages/vela_api_client`). Operations whose handlers do not exist yet are registered as `501 not_implemented` stubs named by operationId so the test passes; each Track A task replaces its stubs.
- [ ] **Step 4: Run** the contract test (PASS) and `./scripts/gen-client.sh && cd app && flutter pub get && flutter analyze` (no errors).
- [ ] **Step 5: Commit** `feat: openapi contract, stub endpoints, generated dart client`.

---

## Track A: API

### Task A1: Schema and migrations

**Files:** Create `api/src/Vela.Api/Data/VelaDbContext.cs`, `Data/Entities/*.cs`, `Data/Migrations/*`, `api/tests/Vela.Api.Tests/Support/PostgresFixture.cs`, `Data/SchemaTests.cs`.

**Interfaces:** Produces `class VelaDbContext : DbContext` with `DbSet`s: `Users, Identities, RefreshTokens, EmailCodes, Tiers, Profiles, Follows, Works, Chapters, Replies, Notifications, Inquiries, Reports, Blocks, ExportJobs`. Key columns: `Users(Id, TierId, CreatedAt, DeletedAt?, AgeConfirmedAt?, TermsAcceptedAt?)`; `Identities(Id, UserId, Provider, Subject, Email?)` unique (Provider, Subject); `Tiers(Id, Name, MaxWorks)` seeded `Freemium/20`; `Profiles(UserId, Handle unique, HandleChangedAt?, DisplayName, Bio, City, Discipline, Availability, AvatarBlob?, Website, Social, Studio, ReplyTime, PublicEmail?, Services jsonb, Shows jsonb)`; `Works(Id, ArtistId, Title, Intro, Status, ImageBlob, Width, Height, AltText?, CreatedAt, PublishedAt?, ExpiresAt?)`; `Chapters(WorkId, Index, X, Y, Zoom?, Title, Body, Meta?, AltText?)`; `Replies(Id, WorkId, ChapterIndex, AuthorId, Text, Hidden, CreatedAt)`; search columns/indexes added in A11. Every user-owned table has a non-null FK to `Users` with `OnDelete(Cascade)` except where noted in A13.
- Postgres fixture: `PostgresFixture : IAsyncLifetime` (Testcontainers) exposing `string ConnectionString` and `VelaDbContext NewContext()`.

- [ ] **Step 1: Write failing tests** — `Migrations_apply_to_empty_database`; `Freemium_tier_is_seeded_with_max_works_20`; `Identity_provider_subject_is_unique`; `Handle_is_unique_case_insensitively`.
- [ ] **Step 2: Run** `dotnet test api/Vela.sln --filter FullyQualifiedName~SchemaTests` — expect FAIL.
- [ ] **Step 3: Implement** entities, DbContext config (snake_case naming, citext handle or lowercase check), initial migration.
- [ ] **Step 4: Run** — expect PASS.
- [ ] **Step 5: Commit** `feat(api): database schema and migrations`.

### Task A2: Access and refresh tokens

**Files:** Create `Auth/TokenService.cs`, `Auth/ITokenService.cs`, `Auth/AuthEndpoints.cs` (refresh, logout), tests `Auth/TokenServiceTests.cs`.

**Interfaces:** Consumes `VelaDbContext` (A1). Produces `record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt)`; `interface ITokenService { Task<TokenPair> IssueAsync(Guid userId); Task<TokenPair> RefreshAsync(string refreshToken); Task RevokeAllAsync(Guid userId); }`. Access JWT 15 min; refresh token 30 days, random 32 bytes, stored SHA-256 hashed with a `FamilyId`; refresh rotates. JWT bearer auth configured in `Program.cs`, `ICurrentUser { Guid? UserId }`.

- [ ] **Step 1: Write failing tests** — `Issued_access_token_validates_and_carries_user_id`; `Refresh_returns_new_pair_and_invalidates_old_refresh_token`; `Reusing_a_rotated_refresh_token_fails_and_revokes_the_whole_family` (Review Focus 5); `Expired_refresh_token_fails`; `Logout_revokes_the_refresh_token`; `Deleted_user_cannot_refresh`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~TokenServiceTests` — expect FAIL.
- [ ] **Step 3: Implement** `TokenService`, wire `/auth/refresh` and `/auth/logout` replacing stubs.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): jwt access and rotating refresh tokens`.

### Task A3: SSO sign-in (Apple, Google, fake)

**Files:** Create `Auth/Identity/IIdentityVerifier.cs`, `AppleIdentityVerifier.cs`, `GoogleIdentityVerifier.cs`, `FakeIdentityVerifier.cs`, `AccountResolver.cs`, tests `Auth/Identity/*Tests.cs`.

**Interfaces:** Produces `record VerifiedIdentity(string Provider, string Subject, string? Email, bool EmailVerified)`; `interface IIdentityVerifier { string Provider { get; } Task<VerifiedIdentity> VerifyAsync(string idToken, string? nonce); }`; `interface IAccountResolver { Task<(Guid UserId, bool IsNew)> SignInAsync(VerifiedIdentity identity); }`. `POST /auth/sso` picks the verifier by `provider` ("apple"|"google"|"fake"; "fake" registered only in Development/Test), resolves, then `ITokenService.IssueAsync`. New users get the Freemium tier and no profile until onboarding.

- [ ] **Step 1: Write failing tests** — `Apple_verifier_accepts_token_signed_by_apple_jwks_with_correct_audience_and_nonce` (test JWKS and recorded token fixtures); `Apple_verifier_rejects_wrong_audience_expired_and_bad_nonce`; same pair for Google; `Resolver_creates_user_on_first_sign_in_and_reuses_it_on_second`; `Same_email_via_different_providers_creates_two_users` (no linking); `Fake_provider_is_not_registered_in_production_environment`; endpoint test `Sso_sign_in_returns_token_pair`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~Identity` — expect FAIL.
- [ ] **Step 3: Implement** verifiers (JWKS cached, issuer/audience from config `Auth:Apple:ClientId`, `Auth:Google:ClientIds`), resolver, endpoint.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): sso sign-in with apple, google, and fake provider`.

### Task A4: Email one-time-code sign-in

**Files:** Create `Auth/EmailCodes/EmailCodeService.cs`, `Common/IEmailSender.cs`, `Common/AcsEmailSender.cs`, `Common/FakeEmailSender.cs`, `Common/RateLimiting.cs`, tests `Auth/EmailCodes/EmailCodeServiceTests.cs`.

**Interfaces:** Consumes `IAccountResolver` (A3), `ITokenService` (A2). Produces `interface IEmailSender { Task SendAsync(string to, string subject, string textBody); }`; `interface IEmailCodeService { Task RequestAsync(string email, string ip); Task<TokenPair> VerifyAsync(string email, string code, string ip); }`. Email normalized lower-case/trimmed; identity = (`"email"`, normalized email). A configured review allow-list email accepts a fixed code only when `Auth:ReviewMode` is on (default off).

- [ ] **Step 1: Write failing tests** — `Request_sends_a_six_digit_code_and_stores_only_its_hash`; `Correct_code_signs_in_and_creates_user`; `Code_expires_after_10_minutes`; `Sixth_attempt_fails_even_with_correct_code` (Review Focus 5); `Used_code_cannot_be_replayed`; `New_request_invalidates_previous_code`; `Rate_limit_blocks_requests_per_email_and_per_ip`; `Review_mode_off_rejects_fixed_code`; `Response_does_not_reveal_whether_email_has_an_account`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~EmailCode` — expect FAIL.
- [ ] **Step 3: Implement** with SHA-256 + server pepper from config, constant-time compare; ACS sender uses the configured sending domain.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): email one-time-code sign-in`.

### Task A5: Profiles, onboarding, handles

**Files:** Create `Profiles/ProfileService.cs`, `Profiles/ProfileEndpoints.cs`, `Profiles/HandleRules.cs`, tests `Profiles/*Tests.cs`.

**Interfaces:** Produces `static class HandleRules { bool IsValid(string handle); string Normalize(string handle); }`; `interface IProfileService { Task<MeDto> GetMeAsync(Guid userId); Task CompleteOnboardingAsync(Guid userId, OnboardingRequest req); Task UpdateAsync(Guid userId, UpdateProfileRequest req); Task<ArtistDto?> GetByHandleAsync(string handle, Guid? viewerId); }` Onboarding requires `AgeConfirmed` and `TermsAccepted` true and applies `followArtistIds`. `GET /me` includes `onboardingComplete`, `tier {name,maxWorks}`, `usedWorks`.

- [ ] **Step 1: Write failing tests** — `Handle_must_be_3_to_20_lowercase_alnum_underscore`; `Handle_change_is_refused_within_30_days`; `Handle_taken_returns_handle_taken_error`; `Onboarding_without_age_or_terms_is_refused`; `Onboarding_follows_chosen_artists`; `Bio_over_280_chars_is_refused`; `Public_artist_response_never_contains_login_email`; `Update_profile_only_changes_own_profile`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~Profile` — expect FAIL.
- [ ] **Step 3: Implement** and replace stubs for `getMe, completeOnboarding, updateProfile, getArtist, suggestedArtists`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): profiles, onboarding, handle rules`.

### Task A6: Blocks, reports, visibility

**Files:** Create `Social/BlockService.cs`, `Social/ReportService.cs`, `Social/IVisibility.cs`, `Social/SafetyEndpoints.cs`, tests `Social/SafetyTests.cs`.

**Interfaces:** Produces `interface IVisibility { Task<IReadOnlySet<Guid>> HiddenUserIdsAsync(Guid? viewerId); }` returning users the viewer blocked plus users who blocked the viewer; every list query in A8-A11 must exclude these ids. `interface IBlockService { Task BlockAsync(Guid userId, Guid targetId); Task UnblockAsync(...); }` — blocking deletes follows in both directions. `ReportService.CreateAsync(Guid reporterId, string targetType /*work|reply|user*/, Guid targetId, string reason)` and `ReportService.CreateAutoFlagAsync(string targetType, Guid targetId, string category, int severity)`; `Reports` rows carry `Source` (`user_report | auto_flag | dmca`), `Reason`, `Status` (`pending | reviewed | actioned | dismissed`), `TargetType`, `TargetId`, `ReporterId?` (null for `auto_flag`); auto-flag rows record the automatic action already taken (`blocked` or `hidden`) in `Reason`; a user may file at most `Reports:MaxPerUserPerDay` (default 10) reports per UTC day, beyond which `report` returns 429 `report_limit_reached`; a reported reply or work is hidden pending review once reported.

- [ ] **Step 1: Write failing tests** — `Block_removes_follows_in_both_directions`; `Hidden_ids_include_blockers_and_blocked`; `Cannot_block_self`; `Reported_reply_is_hidden`; `Duplicate_report_by_same_user_is_idempotent`; `Auto_flag_has_no_reporter_and_source_auto_flag`; `Eleventh_report_in_a_day_is_429_report_limit_reached`; `Report_limit_is_per_account_and_resets_next_utc_day`; `Report_limit_is_configurable`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~SafetyTests` — expect FAIL.
- [ ] **Step 3: Implement** and replace stubs for `report, block, unblock`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): blocks, reports, visibility`.

### Task A7: Quota, drafts, uploads, image variants

**Files:** Create `Works/QuotaService.cs`, `Images/IBlobStore.cs`, `Images/AzureBlobStore.cs`, `Images/VariantGenerator.cs`, `Images/DraftCleanupJob.cs`, tests `Works/QuotaTests.cs`, `Images/VariantTests.cs`.

**Interfaces:** Consumes A1. Produces `interface IBlobStore { Uri CreateUploadUrl(string blobName, TimeSpan ttl); Task WriteAsync(string name, Stream data, string contentType); Task<Stream> OpenReadAsync(string name); Task DeletePrefixAsync(string prefix); Uri PublicUrl(string name); }`; `class QuotaExceededException(int limit)`; `interface IQuotaService { Task<Work> CreateDraftAsync(Guid userId); }` (transactional, `SERIALIZABLE` or row lock on user, sets `ExpiresAt = now + 24h`); `interface IVariantGenerator { Task<ImageVariants> GenerateAsync(string sourceBlob); }` producing `thumb`, `feed`, `full` WebP and `Width/Height` of the **orientation-corrected** image. `POST /works` returns `{workId, uploadUrl}`; quota failure -> 409 `quota_exceeded` with `limit`.

- [ ] **Step 1: Write failing tests** — `Twenty_first_draft_is_refused_with_quota_exceeded`; `Two_concurrent_drafts_at_19_yield_exactly_one_success` (Review Focus 1, real Postgres, `Task.WhenAll`); `Deleting_a_work_frees_a_slot`; `Blocked_and_removed_works_do_not_count`; `Expired_drafts_are_deleted_with_their_blobs_by_the_cleanup_job`; `Variants_are_generated_in_three_sizes_as_webp`; `Exif_rotated_portrait_photo_reports_corrected_width_and_height` (Review Focus 4, fixture JPEG with orientation 6); `Oversized_or_non_image_upload_is_rejected`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Quota|FullyQualifiedName~Variant"` — expect FAIL.
- [ ] **Step 3: Implement** (ImageSharp or NetVips, builder's call; check the licence suits the project), Azurite-backed blob store tests, hosted service for cleanup; replace `createDraft` stub.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): quota-enforced drafts, uploads, image variants`.

### Task A8: Works, chapters, publish, moderation

**Files:** Create `Works/WorkService.cs`, `Works/WorkEndpoints.cs`, `Moderation/IContentSafety.cs`, `Moderation/AzureContentSafety.cs`, `Moderation/FakeContentSafety.cs`, `Moderation/PublishPipeline.cs`, tests `Works/WorkTests.cs`.

**Interfaces:** Consumes A6, A7. Produces `interface IContentSafety { Task<int> SeverityAsync(string text); Task<int> SeverityAsync(Stream image); }`; `interface IWorkService { Task UpdateAsync(Guid userId, Guid workId, UpdateWorkRequest req); Task RequestPublishAsync(Guid userId, Guid workId); Task DeleteAsync(Guid userId, Guid workId); Task<WorkDto?> GetAsync(Guid workId, Guid? viewerId); }`. `UpdateWorkRequest` carries title, intro, altText, and `IReadOnlyList<ChapterDto>` (x, y, zoom?, title, body, meta?, altText?). Publish sets `pending`, runs the Content Safety scan in a background step (retry up to 3 times over 10 minutes), then sets `published`, or `blocked` when severity >= 4 (queue item via `ReportService.CreateAutoFlagAsync`, A6, for admin review and possible restore). If the scan is still unavailable after retries the work is published (fail-open, designer's call) and a `scan_unavailable` auto-flag is queued. Only an admin (A14) sets `removed` or restores a `blocked` work; admin-restored content carries `ModerationOverride = true` and is not auto-blocked again. Only the owner can edit; published works are readable by anyone (public endpoint), others' drafts are not.

- [ ] **Step 1: Write failing tests** — `Update_rejects_more_than_40_chapters`; `Update_rejects_x_or_y_outside_0_to_1`; `Zoom_null_is_stored_as_stay_put_and_returned_as_null`; `Publish_with_zero_chapters_is_refused`; `Flagged_image_severity_4_sets_status_blocked_and_creates_queue_item`; `Flagged_text_severity_4_sets_status_blocked_and_creates_queue_item`; `Blocked_work_is_visible_only_to_its_owner_and_admins`; `Content_safety_outage_retries_then_publishes_with_scan_unavailable_flag`; `Restored_work_is_not_blocked_again_on_republish`; `Clean_work_becomes_published_after_pipeline_runs`; `Non_owner_cannot_update_or_delete`; `Draft_is_not_visible_to_other_users`; `Hostile_text_is_stored_verbatim_and_returned_unescaped_as_json` (Review Focus 3, escaping is the renderer's job); `Deleting_removes_blobs`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WorkTests` — expect FAIL.
- [ ] **Step 3: Implement** and replace stubs for `updateWork, publishWork, deleteWork, getWork`; creates a `notifications` row for followers' feeds is NOT done here (A10 hooks `IWorkPublished`).
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): works, chapters, publish with moderation`.

### Task A9: Follows, feed, replies

**Files:** Create `Social/FollowService.cs`, `Feed/FeedService.cs`, `Social/ReplyService.cs`, `Social/SocialEndpoints.cs`, `Common/Cursor.cs`, tests `Social/FollowTests.cs`, `Feed/FeedTests.cs`, `Social/ReplyTests.cs`.

**Interfaces:** Consumes A6 `IVisibility`, A8. Produces `static class Cursor { string Encode(DateTimeOffset at, Guid id); (DateTimeOffset At, Guid Id) Decode(string cursor); }`; `IFeedService.GetAsync(Guid? viewerId, FeedTab tab, string? cursor, int limit)` (following = followed artists' published works first by recency; discover = reverse order placeholder; both exclude hidden users and non-published works; guests get discover only); `IReplyService.AddAsync(Guid userId, Guid workId, int chapterIndex, string text)` (1-1000 chars after trim, Content Safety severity >= 4 sets `Hidden` with `HiddenBy = 'auto'` and adds an `auto_flag` queue item); `ListAsync` excludes hidden replies and hidden users; reply counts per chapter in `WorkDto`. Raises domain events `IReplyAdded`, `IFollowed` consumed by A10.

- [ ] **Step 1: Write failing tests** — `Follow_is_idempotent_and_cannot_follow_self`; `Following_feed_lists_followed_artists_first`; `Guest_following_tab_returns_discover`; `Feed_excludes_drafts_auto_blocked_removed_and_blocked_users`; `Feed_pagination_has_no_duplicates_or_gaps_with_equal_timestamps`; `Reply_empty_or_over_1000_chars_is_refused`; `Reply_flagged_by_content_safety_is_hidden_and_queued_for_review`; `Reply_reported_by_a_user_is_hidden_from_others_but_visible_to_author`; `Reply_on_nonexistent_chapter_is_404`; `Emoji_and_rtl_text_round_trips`; `Author_can_delete_own_reply_and_work_owner_can_delete_any_on_their_work`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Follow|FullyQualifiedName~Feed|FullyQualifiedName~Reply"` — expect FAIL.
- [ ] **Step 3: Implement** and replace stubs for `getFeed, follow, unfollow, listFollowers, listArtistWorks, listReplies, addReply, deleteReply`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): follows, feed, replies`.

### Task A10: Notifications and inquiries

**Files:** Create `Notifications/NotificationService.cs`, `Notifications/NotificationEndpoints.cs`, `Inquiries/InquiryService.cs`, tests `Notifications/NotificationTests.cs`, `Inquiries/InquiryTests.cs`.

**Interfaces:** Consumes A9 events, A8 publish event `IWorkPublished`, `IEmailSender` (A4). Produces `NotificationType { Reply, Follower, NewStory }`; rows written in the same transaction as the event; `INotificationService { Task<(IReadOnlyList<NotificationDto>, string? Next)> ListAsync(Guid userId, string? cursor); Task<int> UnreadCountAsync(Guid userId); Task MarkReadAsync(Guid userId, IReadOnlyList<Guid>? ids); }` where list groups consecutive unread replies for the same work chapter at read time ("3 new replies on <title>"); `IInquiryService.SendAsync(Guid? senderId, Guid artistId, InquiryRequest req)` where `InquiryRequest(string Type, string Email, string Message, Guid? WorkId)`; saved first, then emailed to the artist's login email via `IEmailSender`, which is never returned to the sender.

- [ ] **Step 1: Write failing tests** — `Reply_creates_notification_for_work_owner_but_not_for_self_reply`; `New_story_notifies_followers_not_blocked_users`; `Unread_count_matches_and_mark_read_clears_it`; `Consecutive_replies_on_same_chapter_group_into_one_item`; `Notification_not_created_if_transaction_rolls_back`; `Inquiry_is_saved_before_email_is_sent_and_survives_email_failure`; `Inquiry_requires_valid_email_with_at_sign_and_non_empty_message`; `Inquiry_rate_limited_per_sender_and_ip`; `Inquiry_email_body_does_not_contain_html_injection_unescaped` (Review Focus 3); `Artist_login_email_never_appears_in_any_response`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Notification|FullyQualifiedName~Inquiry"` — expect FAIL.
- [ ] **Step 3: Implement** and replace stubs for `listNotifications, unreadCount, markRead, sendInquiry`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): notifications and inquiries`.

### Task A11: Search

**Files:** Create `Search/SearchService.cs`, migration `AddSearchIndexes`, tests `Search/SearchTests.cs`.

**Interfaces:** Consumes A6, A8. Produces `ISearchService.SearchAsync(Guid? viewerId, string q, SearchType type, string? cursor, int limit)`; Postgres `tsvector` plus `pg_trgm` GIN indexes on handle, display name, city, discipline (artists) and title, intro, chapter titles (stories). Queries under 2 characters return 400 `query_too_short`. Only published works from non-hidden users.

- [ ] **Step 1: Write failing tests** — `Finds_artist_by_handle_name_city_and_discipline`; `Typo_tolerant_match_via_trigram`; `Finds_story_by_chapter_title`; `Excludes_drafts_auto_blocked_removed_and_blocked_users`; `Single_character_query_is_400`; `Query_with_sql_wildcards_and_quotes_is_safe`; `Pagination_is_stable`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~SearchTests` — expect FAIL.
- [ ] **Step 3: Implement** and replace `search` stub.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): postgres full-text and fuzzy search`.

### Task A12: Share pages, well-known files, legal pages

**Files:** Create `Share/SharePageEndpoint.cs`, `Share/WellKnownEndpoints.cs`, `Legal/LegalEndpoints.cs`, `Legal/*.md` (privacy, terms, content/copyright placeholders), tests `Share/ShareTests.cs`.

**Interfaces:** Consumes A8. `GET /s/{workId}` returns HTML with Open Graph/Twitter tags (title, intro, cover `feed` variant URL) for **published** works only (404 otherwise), then opens the app via universal link or loads the web viewer at `Web:ViewerUrl`. `GET /.well-known/apple-app-site-association` and `/.well-known/assetlinks.json` are generated from config (`Links:AppleTeamId`, `Links:BundleId`, `Links:AndroidPackage`, `Links:Sha256Fingerprints`). `/legal/{privacy|terms|content}` render markdown placeholders marked "DRAFT, pending legal review".

- [ ] **Step 1: Write failing tests** — `Share_page_has_og_title_description_and_image`; `Share_page_html_escapes_script_in_title_and_intro` (Review Focus 3); `Share_page_for_draft_auto_blocked_or_removed_is_404`; `Well_known_files_reflect_configuration`; `Legal_pages_render_and_are_marked_draft`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ShareTests` — expect FAIL.
- [ ] **Step 3: Implement** with explicit HTML encoding (`WebUtility.HtmlEncode`), no string-concatenated attributes.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): share preview, app-link files, legal placeholders`.

### Task A13: Account deletion and data export

**Files:** Create `Account/AccountDeletionService.cs`, `Account/DeletionJob.cs`, `Account/ExportService.cs`, `Account/AccountEndpoints.cs`, tests `Account/DeletionTests.cs`, `Account/ExportTests.cs`.

**Interfaces:** Consumes all tables and `IBlobStore`. Produces `IAccountDeletionService { Task RequestAsync(Guid userId); }` (sets `DeletedAt`, revokes all tokens immediately, enqueues job) and `DeletionJob.RunAsync(Guid userId)` (idempotent hard delete: user, identities, tokens, profile, works, chapters, all their replies on any work, follows both directions, notifications to or about them, reports they filed, blocks, inquiries sent or received, blobs under `works/{id}/` and `avatars/{userId}/`, CDN purge call via `ICdnPurger`; for Apple identities calls `IAppleTokenRevoker`); `IExportService { Task<Guid> RequestAsync(Guid userId); Task<ExportStatus> GetAsync(Guid userId, Guid exportId); }` builds a zip (JSON for profile, works, chapters, replies, follows, notifications, inquiries sent + original images), stores it in blob for 7 days, emails a short-lived download link; one request per day.

- [ ] **Step 1: Write failing tests** — `Deleted_user_cannot_sign_in_or_refresh_immediately`; `After_job_runs_no_table_has_any_row_referencing_the_user` (reflect over all FKs to `Users` and over `Replies.AuthorId`/follows/notifications actor columns; fail if a new table is added without coverage); `Replies_by_deleted_user_on_other_works_are_removed_and_counts_update`; `Blobs_for_works_and_avatar_are_deleted`; `Job_is_idempotent_when_run_twice`; `Export_contains_profile_works_replies_follows_and_images`; `Export_second_request_same_day_is_refused`; `Export_download_link_expires`; `Other_users_data_is_not_in_export`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Deletion|FullyQualifiedName~Export"` — expect FAIL.
- [ ] **Step 3: Implement** and replace `deleteAccount, requestExport, getExport` stubs.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): gdpr account deletion and data export`.

### Task A14: Admin moderation, suspension, evidence hold, DMCA

**Files:** Create `Admin/AdminAuth.cs`, `Admin/ModerationService.cs`, `Admin/Pages/*.cshtml` (queue, report detail, user detail, audit log), `Admin/EvidenceService.cs`, `Admin/INcmecReporter.cs`, `Admin/FakeNcmecReporter.cs`, `Legal/DmcaService.cs`, migration `AddModerationTables`, tests `Admin/*Tests.cs`, `Legal/DmcaTests.cs`. Modify `Account/DeletionJob.cs` (A13) and `contract/openapi.yaml`.

**Interfaces:** Consumes A6 reports, A8 works, A9 replies, A2 tokens, A4 email, `IBlobStore`. New tables: `AdminActions(Id, AdminId, Action, TargetType, TargetId, Reason, At)`, `BannedIdentities(Provider, SubjectHash)`, `EvidenceHolds(Id, UserId, WorkId?, ReplyId?, BlobPrefix, Reason, HoldUntil, NcmecReportId?)`, `DmcaNotices(Id, WorkId, ClaimantName, ClaimantEmail, Statement, SwornAt, Status, CounterNoticeText?)`; `Users.SuspendedAt`, `Users.IsAdmin`. Report reasons enum: `illegal_content, copyright, harassment, spam, other`. Produces `IModerationService { Task<Page<ReportItem>> QueueAsync(ReportStatus status, string? cursor); Task RemoveWorkAsync(Guid adminId, Guid workId, string reason); Task HideReplyAsync(Guid adminId, Guid replyId, string reason); Task DismissReportAsync(Guid adminId, Guid reportId); Task RestoreAsync(Guid adminId, Guid reportId, string reason); /* auto-blocked work -> published, auto-hidden reply -> visible, sets ModerationOverride, closes the item */ Task SuspendUserAsync(Guid adminId, Guid userId, string reason); /* shown in the admin UI as "Ban account"; reason required */ Task ReinstateUserAsync(Guid adminId, Guid userId, string reason); /* clears SuspendedAt and the banned-identity hashes */ Task<Page<AdminUserItem>> FindUsersAsync(string q, string? cursor); /* admin lookup page /admin/users by handle, display name or id, so an account can be banned without a report */ Task MarkIllegalAsync(Guid adminId, Guid reportId); }` and `INcmecReporter { Task<string> SubmitAsync(EvidenceHold hold); }`. `MarkIllegalAsync` creates the `EvidenceHold` (copies content and blobs to the restricted `evidence` container, `HoldUntil = now + 1 year`) and suspends the owner; `NcmecReporter` is invoked only by an explicit admin action after review. Admin login: email code (A4) restricted to `Admin:Emails`, cookie session 8 h, antiforgery on every POST. Admin dashboard (manual review first; automation is a later design): `/admin` home shows counts per status (pending, reviewed, actioned), the age of the oldest unreviewed item, and a queue sorted oldest first with filters by status, reason, and source. `Reports.Source` is `user_report | auto_flag | dmca`, so a later auto-block or image-recognition step can add items to the same queue without schema changes. Each row shows a thumbnail (admin-only short-lived URL, 5 minutes), reason, reporter note, the owner's prior reports and removals, and inline actions (restore, dismiss, hide reply, remove work, ban user, mark illegal). The page refreshes itself every 60 seconds (no websockets) and paginates with a cursor. Contract additions: `POST /legal/dmca-notices` (submitDmcaNotice, public, rate-limited), `POST /works/{id}/counter-notice` (submitCounterNotice, owner only); regenerate the Dart client with `scripts/gen-client.sh`.

- [ ] **Step 1: Write failing tests** — `Non_admin_gets_403_on_every_admin_route`; `Admin_login_refuses_emails_not_on_allow_list`; `Suspend_revokes_all_tokens_immediately_and_sign_in_returns_account_suspended`; `Suspend_removes_public_works_and_replies_from_feed_search_and_share_page`; `Suspended_identity_cannot_sign_up_again_with_same_provider_subject` (hash stored, raw subject not); `Mark_illegal_copies_content_to_evidence_container_and_sets_hold_until_one_year`; `Account_deletion_deletes_public_data_but_keeps_held_evidence` (modifies A13); `Held_evidence_is_never_returned_by_any_public_or_export_endpoint`; `Ncmec_submission_only_runs_on_explicit_admin_action_and_records_report_id`; `Admin_can_ban_a_user_from_the_lookup_page_without_a_report`; `Ban_requires_a_reason`; `Admin_cannot_ban_themselves_or_another_admin`; `Reinstate_restores_sign_in_and_removes_the_identity_ban`; `Every_admin_action_writes_an_audit_row`; `Dashboard_shows_counts_per_status_and_age_of_oldest_pending_item`; `Queue_is_sorted_oldest_first_and_filters_by_status_reason_and_source`; `Reviewed_items_leave_the_pending_queue`; `Thumbnail_urls_are_admin_only_and_expire_within_5_minutes`; `Auto_flag_source_items_appear_in_the_same_queue`; `Restore_republishes_an_auto_blocked_work_and_closes_the_item`; `Restore_unhides_an_auto_hidden_reply`; `Confirming_an_auto_block_sets_removed`; `Restore_requires_a_reason_and_writes_an_audit_row`; `Queue_pagination_has_no_duplicates_or_gaps`; `Dmca_notice_requires_claimant_name_email_and_sworn_statement`; `Dmca_takedown_removes_work_and_emails_owner_with_counter_notice_instructions`; `Counter_notice_only_by_work_owner`; `Admin_pages_html_escape_user_supplied_text` (Review Focus 3); `Post_without_antiforgery_token_is_rejected`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Admin|FullyQualifiedName~Dmca"` — expect FAIL.
- [ ] **Step 3: Implement** services, Razor Pages, evidence copy, suspension and deletion-job exception; replace the `report` handler's reason validation; add the two new contract operations.
- [ ] **Step 4: Run** — PASS, plus `dotnet test api/Vela.sln` (full suite still green, including A13).
- [ ] **Step 5: Commit** `feat(api): admin moderation, suspension, evidence hold, dmca`.

### Task A15: Abuse and bot protection

**Files:** Create `Abuse/RateLimitPolicies.cs`, `Abuse/ClientIpResolver.cs`, `Abuse/IAttestationVerifier.cs`, `Abuse/PlayIntegrityVerifier.cs`, `Abuse/AppAttestVerifier.cs`, `Abuse/FakeAttestationVerifier.cs`, `Abuse/SignupVelocity.cs`, `Abuse/DisposableEmailList.cs`, tests `Abuse/*Tests.cs`.

**Interfaces:** Produces ASP.NET Core rate-limiter policies: `auth-email` (5/hour per email, 20/hour per IP), `anon-read` (120/min per IP), `write` (60/min per user), `upload` (20/hour per user), `public-form` (5/hour per IP); 429 uses the envelope with `error.code = "rate_limited"` and a `Retry-After` header. `IClientIpResolver { IPAddress Resolve(HttpContext ctx); }` trusts `CF-Connecting-IP` only when the connecting address is in the configured Cloudflare ranges. `IAttestationVerifier { Task<bool> VerifyAsync(string platform /*ios|android*/, string token, string action); }` read from the `X-Attestation` and `X-Platform` headers; required on `/auth/email/request`, `/auth/sso`, `POST /works` when `Abuse:RequireAttestation` is true (default false in Development). `SignupVelocity.AllowAsync(IPAddress ip)` allows at most 5 new accounts per IP per day. Request body limit 1 MB for JSON endpoints; JSON max depth 32.

- [ ] **Step 1: Write failing tests** — `Sixth_email_code_request_for_same_email_in_an_hour_is_429_with_retry_after`; `Twenty_first_request_from_same_ip_is_429`; `Spoofed_cf_connecting_ip_is_ignored_when_not_from_cloudflare`; `Valid_cf_connecting_ip_from_cloudflare_range_is_used`; `Missing_attestation_is_rejected_when_required_and_allowed_when_not`; `Invalid_attestation_token_is_rejected`; `Sixth_new_account_from_same_ip_in_a_day_is_refused`; `Disposable_email_domain_is_refused_at_email_request`; `Oversized_json_body_is_413`; `Deeply_nested_json_is_400`; `Rate_limited_response_uses_envelope`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~Abuse` — expect FAIL.
- [ ] **Step 3: Implement** policies and attach by endpoint group; verifiers use Google Play Integrity decode and Apple App Attest validation behind the interface, tested with recorded tokens; real verification is exercised after the developer accounts exist.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): rate limits, attestation, signup velocity, request limits`.

### Task A16: Telemetry and crash reporting (API)

**Files:** Create `Telemetry/TelemetryExtensions.cs`, `Telemetry/PiiScrubber.cs`, tests `Telemetry/TelemetryTests.cs`.

**Interfaces:** `services.AddVelaTelemetry(IConfiguration)`: OpenTelemetry traces/metrics/logs exported to the Log Analytics workspace (sampling ratio from config, default 0.1) and Sentry .NET (DSN from config; disabled when empty). `static class PiiScrubber { string Scrub(string input); }` redacts email addresses; request bodies are never logged; Sentry `BeforeSend` strips user email, IP, and any request body.

- [ ] **Step 1: Write failing tests** — `Scrubber_redacts_email_addresses`; `Sentry_events_contain_no_email_ip_or_request_body`; `Reply_and_inquiry_text_never_appear_in_logs` (capture log sink while posting a reply and an inquiry); `Telemetry_is_disabled_without_configuration_and_app_still_starts`; `Unhandled_exception_returns_envelope_without_stack_trace`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~Telemetry` — expect FAIL.
- [ ] **Step 3: Implement** and add a global exception handler that returns `{success:false, error:{code:"internal_error"}}`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(api): telemetry and sentry with pii scrubbing`.

---

## Track B: Flutter app

### Task B1: Theme, fonts, branding

**Files:** Create `app/lib/core/theme/tokens.dart`, `app/lib/core/theme/app_theme.dart`, `app/lib/core/branding.dart`, bundle fonts Instrument Serif, Geist, Geist Mono under `app/assets/fonts/`, tests `app/test/core/theme_test.dart`.

**Interfaces:** Produces `abstract final class VelaColors { static const background = Color(0xFF0F0E0D); ink = Color(0xFFF2EEE6); secondary = Color(0xFFD6D1C8); muted = Color(0xFFA39D93); faint = Color(0xFF8A847B); accent = Color(0xFFE5AE63); sheet = Color(0xFF161513); }`, `VelaText` styles (serif 46/40/34/32/28/26/24/18; Geist 15/14/13/12/11; mono 12/11/10), `ThemeData buildTheme()`, `abstract final class Branding { static const appName = 'Vela'; }`.

- [ ] **Step 1: Write failing tests** — `colors_match_handoff_tokens`; `theme_uses_background_and_ink`; `all_text_styles_use_bundled_font_families`.
- [ ] **Step 2: Run** `cd app && flutter test test/core/theme_test.dart` — FAIL.
- [ ] **Step 3: Implement** tokens, theme, font declarations in `pubspec.yaml`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): design tokens, theme, branding`.

### Task B2: API wiring, session, router, guest gating

**Files:** Create `app/lib/core/api/api_providers.dart`, `app/lib/core/auth/session_store.dart`, `app/lib/core/auth/auth_controller.dart`, `app/lib/core/router.dart`, `app/lib/core/require_sign_in.dart`, tests `app/test/core/*_test.dart`.

**Interfaces:** Consumes generated `vela_api_client`. Produces Riverpod providers `apiProvider`, `sessionProvider` (`AsyncNotifier<Session?>`), `class Session { final String accessToken; final String refreshToken; final DateTime accessExpiresAt; }` stored via `flutter_secure_storage`; a Dio interceptor that attaches the bearer token, refreshes once on 401, then signs out; `Future<bool> requireSignIn(BuildContext, WidgetRef)` that returns true when signed in, else opens the sign-in sheet (B8) and returns whether sign-in succeeded; routes `/` feed, `/story/:id`, `/artist/:handle`, `/create`, `/search`, `/notifications`, `/settings`, `/onboarding`.

- [ ] **Step 1: Write failing tests** — `interceptor_attaches_bearer_token`; `on_401_refreshes_once_and_retries`; `refresh_failure_clears_session`; `concurrent_401s_trigger_a_single_refresh`; `require_sign_in_returns_true_when_session_exists`; `guest_can_open_feed_and_story_routes`; `session_survives_restart`.
- [ ] **Step 2: Run** `flutter test test/core` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): api client wiring, session, router, guest gating`.

### Task B3: Camera math (pure)

**Files:** Create `app/lib/features/story/camera_math.dart`, test `app/test/features/story/camera_math_test.dart`.

**Interfaces:** Produces `@immutable class CameraState { final double scale; final double tx; final double ty; final Offset spotlight; }` and `CameraState computeCamera({required double x, required double y, required double? zoom, required double zoomMultiplier, required Size viewport})`. Zoomed: `s = zoom * zoomMultiplier; tx = clamp(W/2 - s*x*W, W - s*W, 0); ty = clamp(0.32*H - s*y*H, H - s*H, 0); spotlight = (tx + s*x*W, ty + s*y*H)`. Stay put (`zoom == null`): scale 1, tx 0, ty 0, spotlight (x*W, y*H). `const CameraState.reset()` = scale 1, tx 0, ty 0, spotlight unused. Constants `cameraDuration = Duration(milliseconds: 900)`, `cameraCurve = Cubic(.2, .7, .1, 1)`.

- [ ] **Step 1: Write failing tests** — `centre_pin_zoom_2_4_on_390x844`: x=.5,y=.5,zoom 2.4,mult 1 gives scale 2.4, tx -273, ty -742.72, spotlight (195, 270.08); `pin_at_top_left_clamps_tx_ty_to_zero`; `pin_at_bottom_right_clamps_to_w_minus_sw_and_h_minus_sh`; `zoom_multiplier_scales_s` ; `stay_put_returns_identity_and_spotlight_at_pin` (x=.5,y=.5 gives scale 1, tx 0, ty 0, spotlight (195, 422)); `spotlight_stays_inside_viewport_for_every_x_y_in_a_grid_of_0_to_1_step_0_1_with_zoom_1_to_4` (Review Focus 2); `non_3_4_viewports_work` (e.g. 360x640 and 430x932).
- [ ] **Step 2: Run** `flutter test test/features/story/camera_math_test.dart` — FAIL.
- [ ] **Step 3: Implement** as pure functions with no Flutter widgets.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): camera math with stay-put rule`.

### Task B4: Story viewer

**Files:** Create `app/lib/features/story/story_screen.dart`, `story_controller.dart`, `widgets/{spotlight_ring,pin_layer,progress_segments,bottom_card,story_top_bar}.dart`, tests `app/test/features/story/*_test.dart`.

**Interfaces:** Consumes B3, B2, generated `getWork`. Produces `StoryScreen({required String workId})`; `StoryState { int step; /* 0 intro, 1..n chapters, n+1 end */ StoryMode mode; int exploreSel; bool threadOpen; }` with immutable `copyWith`; `StoryController` methods `next()`, `prev()`, `jump(int)`, `setMode(StoryMode)`, `selectPin(int)`, `zoomOut()`, `toggleAutoplay()`. Layout and states exactly per handoff README section 2 (intro, chapter, end, explore idle cards; tap zones left 30% prev / right 70% next on y 110-510; autoplay 6 s paused while replies open; pins counter-scaled 1/s). The photo transform is animated with the B3 duration and curve; stay-put chapters animate to the identity transform. Haptic on chapter change. `StoryScreen` is also the web viewer body (B13) with `readOnly: true`.

- [ ] **Step 1: Write failing tests** — controller: `next_from_intro_goes_to_chapter_1`, `prev_at_chapter_1_goes_to_intro`, `next_past_last_goes_to_end`, `mode_toggle_resets_to_intro_and_unzoomed`, `explore_pin_tap_selects_and_zoom_out_clears`; widget: `spotlight_ring_positioned_at_camera_spotlight_point`, `stay_put_chapter_shows_unzoomed_photo_with_ring_on_pin`, `pins_hidden_during_chapters_visible_on_intro_and_explore`, `tapping_progress_segment_jumps`, `end_card_shows_follow_and_next_story_and_print_inquiry_buttons`, `autoplay_advances_every_6s_and_pauses_when_replies_open` (fake async), `edge_pins_x0_y1_render_inside_screen` (Review Focus 2), `alt_text_is_exposed_as_semantics_label`.
- [ ] **Step 2: Run** `flutter test test/features/story` — FAIL.
- [ ] **Step 3: Implement.** Compare against the prototype in a browser for visual parity.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): story viewer`.

### Task B5: Feed

**Files:** Create `app/lib/features/feed/{feed_screen,feed_controller,post_card}.dart`, tests `app/test/features/feed/*`.

**Interfaces:** Consumes `getFeed`, `follow`, `unfollow`, B2 `requireSignIn`. `FeedScreen` with Following/Discover tabs (Following hidden for guests), infinite scroll via `nextCursor`, pull-to-refresh, search icon (-> `/search`), notifications bell with unread badge (-> `/notifications`), empty, error and offline states.

- [ ] **Step 1: Write failing tests** — `renders_cards_with_title_meta_and_pin_dots`; `follow_pill_for_guest_prompts_sign_in`; `follow_toggles_optimistically_and_rolls_back_on_error_with_visible_message`; `scrolling_to_end_loads_next_page_once`; `pull_to_refresh_reloads`; `empty_following_shows_discover_hint`; `unread_badge_shows_count`.
- [ ] **Step 2: Run** `flutter test test/features/feed` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): feed`.

### Task B6: Replies sheet

**Files:** Create `app/lib/features/replies/{replies_sheet,replies_controller}.dart`, tests.

**Interfaces:** Consumes `listReplies`, `addReply`, `deleteReply`, `report`. `showRepliesSheet(BuildContext, {required String workId, required int chapterIndex, required String chapterTitle})`.

- [ ] **Step 1: Write failing tests** — `lists_replies_and_empty_state`; `send_disabled_for_blank_text`; `send_prompts_sign_in_for_guest`; `send_appends_reply_and_clears_input`; `failed_send_keeps_text_and_shows_error`; `own_reply_can_be_deleted_others_can_be_reported`; `text_over_1000_chars_is_blocked_in_input`; `rtl_and_emoji_render`.
- [ ] **Step 2: Run** `flutter test test/features/replies` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): replies sheet`.

### Task B7: Portfolio, followers, contact

**Files:** Create `app/lib/features/portfolio/{portfolio_screen,works_tab,about_tab,followers_sheet,contact_sheet}.dart`, tests.

**Interfaces:** Consumes `getArtist`, `listArtistWorks`, `listFollowers`, `sendInquiry`. Per handoff section 4. Contact sheet: types Commission, Buy a print, Licensing, Collaboration, Just saying hi; Send disabled until email contains "@" and message non-empty; success state. Own profile shows Edit profile and New story; empty-state for own works shows quota usage ("n of 20 stories"). Public contact email shown only if present; login email is never shown.

- [ ] **Step 1: Write failing tests** — `works_grid_and_featured_story_render`; `stats_and_followers_sheet_open`; `contact_send_disabled_until_valid`; `contact_prefills_work_and_type_for_print_inquiry`; `contact_success_state_shows_first_name`; `own_profile_shows_edit_and_new_story`; `copy_email_row_copies_public_email_only`; `blocked_or_missing_artist_shows_not_found`.
- [ ] **Step 2: Run** `flutter test test/features/portfolio` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): portfolio, followers, contact`.

### Task B8: Sign-in and onboarding

**Files:** Create `app/lib/features/auth/{sign_in_sheet,email_code_screen,sso_buttons}.dart`, `app/lib/features/onboarding/{welcome_screen,demo_story,profile_setup_screen,follow_suggestions_screen}.dart`, tests.

**Interfaces:** Consumes `ssoSignIn`, `emailRequest`, `emailVerify`, `completeOnboarding`, `suggestedArtists`. `Future<bool> showSignInSheet(BuildContext)`; buttons for Apple (iOS and web hidden), Google, email. Abstraction `abstract interface class SsoClient { Future<String> idToken(String provider, String nonce); }` with real implementation and a fake used in tests. Onboarding: welcome, skippable demo story (bundled asset story JSON + image, rights-cleared placeholder), after first sign-in a profile setup (display name, handle with live validation, optional avatar, 18+ checkbox, terms checkbox with links), follow 3+ suggested artists. A plain note states that each sign-in method is its own account. No notification-permission prompt.

- [ ] **Step 1: Write failing tests** — `google_button_calls_sso_client_then_api`; `email_flow_request_then_six_digit_entry_then_session`; `wrong_code_shows_error_and_sixth_attempt_message`; `onboarding_submit_disabled_without_age_and_terms`; `handle_shows_taken_error`; `follow_step_requires_three_selections_but_is_skippable_with_discover_fallback`; `demo_story_plays_without_network`; `cancel_returns_false_from_sign_in_sheet`.
- [ ] **Step 2: Run** `flutter test test/features/auth test/features/onboarding` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): sign-in and onboarding`.

### Task B9: Create story

**Files:** Create `app/lib/features/create/{create_screen,create_controller,pin_editor,photo_picker}.dart`, tests.

**Interfaces:** Consumes `createDraft`, direct upload to `uploadUrl`, `updateWork`, `publishWork`. `CreateState { phase pick|annotate|publishing; String? workId; String title; String altText; List<PinDraft> pins; PinDraft? ghost; }` where `PinDraft(double x, double y, double? zoom, String title, String body, String? meta, String? altText)`. Picking a photo creates the server draft (quota error shows "You have used 20 of 20 stories. Delete one to add more."). Per pin: "Zoom in" (default, zoom 2.4) or "Stay put" (zoom null). Max 40 pins. Orientation-corrected image is shown, and pin fractions are computed against the displayed image box. Publish polls the work status until `published` or `blocked`. `blocked` shows "This photo can't be published (content policy). It has been sent for review."; a story an admin later removes shows "Removed by moderators" to its owner. Chapters play in the order added.

- [ ] **Step 1: Write failing tests** — `tap_places_ghost_pin_at_fraction`; `add_chapter_requires_title_and_appends_in_order`; `stay_put_pin_serializes_zoom_null_and_zoom_in_serializes_2_4`; `forty_first_pin_is_refused`; `quota_exceeded_shows_message_and_blocks_upload`; `exif_rotated_image_pins_map_to_displayed_orientation` (Review Focus 4); `publish_disabled_until_one_chapter`; `publish_polls_until_published`; `blocked_status_shows_policy_message_and_review_note`; `removed_work_shows_removed_by_moderators_message`; `back_with_unsaved_pins_asks_to_discard`.
- [ ] **Step 2: Run** `flutter test test/features/create` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): create story`.

### Task B10: Edit profile and settings

**Files:** Create `app/lib/features/profile_edit/{edit_profile_screen,services_editor,shows_editor}.dart`, `app/lib/features/settings/{settings_screen,delete_account_flow,export_flow}.dart`, tests.

**Interfaces:** Consumes `updateProfile`, `avatarUpload`, `deleteAccount`, `requestExport`, `getExport`, `logout`. Fields per spec section 7. Settings: linked sign-in method (display only), sign out, export data (shows status, refuses a second request same day with the server message), delete account (type-to-confirm; on success clears session and local data and returns to feed as guest), links to privacy, terms, content policy.

- [ ] **Step 1: Write failing tests** — `bio_counter_blocks_over_280`; `handle_field_validates_and_shows_30_day_lock_message`; `services_can_be_added_edited_removed`; `avatar_upload_then_save_updates_profile`; `delete_requires_typed_confirmation_and_clears_session`; `export_shows_pending_then_download_link`; `sign_out_clears_session`.
- [ ] **Step 2: Run** `flutter test test/features/profile_edit test/features/settings` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): edit profile and settings`.

### Task B11: Search

**Files:** Create `app/lib/features/search/{search_screen,search_controller,recent_searches}.dart`, tests.

**Interfaces:** Consumes `search`, `suggestedArtists`. Artists/Stories tabs, 300 ms debounce, minimum 2 characters, pagination, recent searches in local storage (max 10, deduplicated), suggested artists in the empty state.

- [ ] **Step 1: Write failing tests** — `no_request_below_two_characters`; `debounce_collapses_rapid_typing_to_one_request`; `stale_response_for_old_query_is_ignored`; `recent_searches_dedupe_and_cap_at_10`; `tabs_switch_result_type`; `empty_state_shows_suggested_artists`; `no_results_message`.
- [ ] **Step 2: Run** `flutter test test/features/search` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): search`.

### Task B12: Notifications list

**Files:** Create `app/lib/features/notifications/{notifications_screen,notifications_controller}.dart`, tests.

**Interfaces:** Consumes `listNotifications`, `unreadCount`, `markRead`. Refreshes on launch, app resume (`WidgetsBindingObserver`), and pull-to-refresh; unread badge provider `unreadCountProvider`; tapping an item marks it read and deep-links to the story chapter (reply sheet open) or the artist.

- [ ] **Step 1: Write failing tests** — `refreshes_on_resume`; `tap_marks_read_and_navigates_to_chapter_replies`; `grouped_reply_item_shows_count`; `empty_state`; `guest_sees_sign_in_prompt`; `badge_updates_after_mark_read`.
- [ ] **Step 2: Run** `flutter test test/features/notifications` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): notifications list`.

### Task B13: Web viewer and deep links

**Files:** Create `app/lib/main_web.dart`, `app/lib/features/web/web_viewer_app.dart`, `web/` assets, `app/ios` associated-domains entitlement, `app/android` intent filters, tests `app/test/features/web/*`.

**Interfaces:** Consumes B4 `StoryScreen(readOnly: true)`, `getWork`, `listReplies`. `flutter build web -t lib/main_web.dart` renders only `/s/:workId`; replies read-only; "Open in app" banner; no sign-in. Mobile app handles `https://{domain}/s/{workId}` via universal links/app links and routes to `/story/:id`. The domain comes from config `LINKS_DOMAIN` (dart-define).

- [ ] **Step 1: Write failing tests** — `web_viewer_renders_story_without_session`; `replies_are_visible_but_no_composer`; `open_in_app_banner_present`; `unknown_or_unpublished_work_shows_not_found`; `deep_link_s_workid_routes_to_story` (router test).
- [ ] **Step 2: Run** `flutter test test/features/web` — FAIL.
- [ ] **Step 3: Implement**; `flutter build web -t lib/main_web.dart` must succeed.
- [ ] **Step 4: Run** tests (PASS) and the web build (exit 0).
- [ ] **Step 5: Commit** `feat(app): web story viewer and deep links`.

### Task B14: Reporting UI, suspended state, crash reporting, attestation

**Files:** Create `app/lib/features/report/report_sheet.dart`, `app/lib/core/crash_reporting.dart`, `app/lib/core/attestation/attestation_client.dart`, `app/lib/features/auth/suspended_screen.dart`, tests.

**Interfaces:** Consumes `report`, `submitDmcaNotice` (link only), A14/A15 error codes. `showReportSheet(BuildContext, {required String targetType, required String targetId})` with reasons Illegal content, Copyright, Harassment, Spam, Other (+ optional note); also reachable from story menu, reply long-press, and artist profile, and requires sign-in. `abstract interface class AttestationClient { Future<String?> token(String action); }` with Play Integrity and App Attest implementations (return null until the accounts are configured) and a fake; the Dio interceptor adds `X-Attestation` and `X-Platform` on `/auth/email/request`, `/auth/sso`, `POST /works`. `initCrashReporting()` uses `sentry_flutter` with `beforeSend` removing user, email, request bodies, and breadcrumbs containing text input; disabled when the DSN dart-define is empty. API error `account_suspended` clears the session and shows `SuspendedScreen` with a support contact; `rate_limited` shows a "try again in N seconds" message from `Retry-After`.

- [ ] **Step 1: Write failing tests** — `report_requires_sign_in_and_sends_reason_and_target`; `illegal_content_reason_is_listed_first_after_copyright`; `success_shows_thanks_and_hides_item_locally`; `account_suspended_error_clears_session_and_shows_suspended_screen`; `rate_limited_error_shows_retry_after_seconds`; `attestation_header_added_only_on_protected_endpoints`; `crash_event_before_send_strips_email_and_text_inputs`; `crash_reporting_disabled_without_dsn`.
- [ ] **Step 2: Run** `flutter test test/features/report test/core` — expect FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(app): reporting ui, suspended state, crash reporting, attestation`.

---

## Track C: Infra

### Task C1: Bicep and deployment workflow

**Files:** Create `infra/main.bicep`, `infra/modules/{containerapp,postgres,storage,acs-email,loganalytics,staticweb,budget}.bicep`, `infra/parameters.dev.json`, `.github/workflows/infra.yml`.

**Interfaces:** Produces parameters `environment`, `location`, `postgresAdminLogin`, `monthlyBudget`; outputs `apiUrl`, `storageAccountName`, `webViewerUrl`. Contents: Container Apps environment (scale to zero, min 0 replicas), PostgreSQL Flexible Server (burstable, smallest SKU), Storage account with `works` and `avatars` containers and CORS for direct upload, ACS Email resource linked to the owner's sending domain (domain name as parameter), Log Analytics workspace with `dailyQuotaGb` set to 0.15, Static Web App (free), Azure budget alert at 2x `monthlyBudget`. Secrets via Key Vault references or Container Apps secrets, never in files.

- [ ] **Step 1: Write failing check** — CI step `az bicep build --file infra/main.bicep` and `az deployment sub what-if` placeholder script `scripts/validate-infra.sh` that exits non-zero if build fails or if any parameter file contains a string matching `password|secret|key` with a literal value.
- [ ] **Step 2: Run** `./scripts/validate-infra.sh` — expect FAIL (files missing).
- [ ] **Step 3: Implement** modules.
- [ ] **Step 4: Run** `./scripts/validate-infra.sh` — PASS (build only; actual deployment is deferred until accounts exist).
- [ ] **Step 5: Commit** `feat(infra): bicep modules and validation`.

### Task S1: Security review gate

**Files:** Create `docs/security/threat-model.md`, `docs/security/asvs-masvs-checklist.md`, `.github/workflows/security.yml`, `api/tests/Vela.Api.Tests/Security/AuthorizationMatrixTests.cs`. Runs after A1-A16, B1-B14 and C1.

**Interfaces:** Consumes everything. CI job runs on every PR: `dotnet list api/Vela.sln package --vulnerable --include-transitive` (fail on any High/Critical), `flutter pub outdated` report, gitleaks secret scan, CodeQL for C#, and `dart analyze --fatal-infos`. The authorization matrix test reads `contract/openapi.yaml` security requirements and checks every operation.

- [ ] **Step 1: Write failing tests** — `Every_non_public_operation_returns_401_without_token`; `Every_owner_only_operation_returns_403_or_404_for_another_user`; `Every_admin_route_returns_403_for_normal_users`; `Public_endpoints_never_return_login_email_or_internal_ids_beyond_whitelist`; `Upload_urls_are_scoped_to_one_blob_and_expire_within_15_minutes`; `Cors_allows_only_configured_web_viewer_origin`; `Security_headers_present_on_html_pages` (CSP, X-Content-Type-Options, frame-ancestors); `No_secret_values_in_repo_config_files`.
- [ ] **Step 2: Run** `dotnet test api/Vela.sln --filter FullyQualifiedName~Security` — expect FAIL.
- [ ] **Step 3: Implement** fixes the tests uncover; write the threat model (assets: accounts, tokens, photos, evidence, admin; actors: anonymous, user, admin, insider; STRIDE per component) and fill the OWASP ASVS L2 (API) and MASVS (app) checklists with status per item. Then run the `security-reviewer` agent over `Auth/`, `Images/`, `Admin/`, `Account/`, `Abuse/`, record findings in `docs/security/review-findings.md`, fix CRITICAL and HIGH, and re-run.
- [ ] **Step 4: Run** full API and app test suites plus the security workflow locally (`gitleaks detect`, vulnerable-package check) — all PASS, no open CRITICAL/HIGH findings.
- [ ] **Step 5: Commit** `chore: security review, threat model, ci scanning`.

---

## Release dependencies (not build tasks)

- Apple Developer account (needed for Sign in with Apple on device, TestFlight, App Store). Owner action; lead time.
- Google Play account and Google OAuth client IDs. Owner action.
- Share-link domain (assumed: the owner's email-sending domain or subdomain) and the association files hosted there.
- Final privacy policy, terms, content/copyright policy with legal review; named person for reports; final product name and icon; launch content and rights-cleared demo story; privacy review of the 30-day backup window.
- Real Azure deployment and end-to-end test with real Apple and Google tokens.
- NCMEC CyberTipline reporting account (electronic service provider registration) before launch; counsel to confirm the obligations and the evidence-hold period.
- DMCA designated agent registered with the US Copyright Office; agent contact published on `/legal/dmca`.
- Cloudflare account with the API domain proxied, WAF and bot protection rules enabled, and the Cloudflare IP ranges set in API config.
- Sentry project (app and API DSNs) created; Apple App Attest and Google Play Integrity enabled once the developer accounts exist.
- App Store privacy nutrition label and Google Play Data safety form completed (data collected: email, photos, user content, diagnostics).
- App Store and Google Play submissions: screenshots, descriptions, review demo account (A4 review mode), UGC moderation contact. External penetration test recommended before public launch.
- Named person(s) to review the admin queue, with a response-time target for illegal-content reports.

## Self-Review

- **Spec coverage:** auth (A2-A4), tiers/quota (A7), onboarding/edit profile (A5, B8, B10), search (A11, B11), notifications (A10, B12), inquiries (A10, B7), moderation/blocks/reports (A6, A8, A9), images/EXIF (A7, B9), per-pin zoom and stay-put (B3, B4, B9), share/web/deep links (A12, B13), deletion/export (A13, B10), alt text (A8, B4, B9), branding/code name (B1, Global), infra/logging/budget (C1), legal placeholders (A12), release dependencies (listed). Report review, suspension, evidence hold, NCMEC and DMCA (A14, B14), abuse protection (A15, B14), crash reporting (A16, B14), security review (S1), US market (Global Constraints, Release dependencies).
- **Type consistency:** `TokenPair`, `ITokenService`, `IIdentityVerifier`, `IAccountResolver`, `IEmailSender`, `IBlobStore`, `IContentSafety`, `IVisibility`, `IQuotaService`, `CameraState`/`computeCamera` are defined once and consumed under the same names.
- **Known gaps:** real NCMEC, Play Integrity, App Attest, Apple and Google sign-in behaviour cannot be verified end to end until the owner's accounts exist; the code is tested against fakes and recorded tokens. Cloudflare configuration is manual (outside Bicep). Counsel must confirm the legal obligations.
