# Vela — Design

Status: draft for approval. Date: 2026-10-22.

## 1. Intent

Vela is a mobile app (iOS + Android) for viewing photographs, and other visual art, together with the story behind them. An artist uploads a photo, taps to drop numbered pins ("chapters") on details, and writes a short note for each. Viewers either **walk through** the pins in order (Story mode, camera moves to each detail) or **explore** them freely (Explore mode). Each pin has its own reply thread. Artists have portfolio pages with their works, followers, and contact/commission info.

The visual and interaction spec already exists: `design_handoff_vela_app/README.md` plus the interactive prototype `design_handoff_vela_app/Vela App.dc.html` (its `class Component` script is the clearest spec of state and behaviour). This document does **not** restate that spec. It records what is decided on top of it: architecture, additions, and changes. Where this document and the handoff README disagree, this document wins.

## 2. Goals, non-goals, anti-goals

Goals
- Native-feeling iOS and Android app from one Flutter codebase, with the story viewer's zoom/pan/spotlight experience matching the prototype.
- Easy authoring: pick a photo, tap to pin, write a note, publish.
- Low running cost on the owner's existing Azure subscription (target about 15-25 USD/month at small scale; estimate, to be checked against current pricing).
- GDPR-compliant: user data can be fully deleted on request.

Non-goals (this version)
- Direct messaging / inbox. Contact is via the artist's contact form, delivered by email.
- Push notifications. Notifications are an in-app list fetched over REST; updates do not need to be timely.
- Paid tiers and in-app purchases. Only a free tier exists.
- Password-based login, account merging or linking across sign-in methods.
- A public marketing site or SEO-indexed portfolio pages.

Anti-goals (a failure even if it technically works)
- Storing passwords.
- Exposing an artist's login email publicly.
- Quota or auth logic enforced only in the client.
- Looking like a generic template; the prototype's visual identity is the bar.

## 3. Platforms and stack (decided)

| Area | Decision |
|---|---|
| Client | Flutter, iOS + Android. |
| Web | Flutter web, **read-only story viewer** for share links only. |
| Backend | C# / .NET minimal API on Azure Container Apps (scale to zero). |
| Database | Azure Database for PostgreSQL Flexible Server, smallest burstable size, always on. |
| Images | Azure Blob Storage; Cloudflare free CDN in front. |
| Email | Azure Communication Services Email, using the owner's existing sending domain. |
| Moderation | Azure AI Content Safety (free tier) plus report and block. |
| Logging | OpenTelemetry export to a Log Analytics workspace (Kusto/KQL), daily cap sized to stay within the ~5 GB/month free allowance, sampling on. |
| CI/CD | GitHub Actions. |
| Contract | OpenAPI spec is the source of truth; Dart client generated from it. |

Not used: Firebase (no auth, no push), Redis, a search engine, websockets, a separate feed service.

Decided by the owner: Flutter; own auth (no Firebase); SSO plus email sign-in; Log Analytics at ~5 GB; no push for now; no DMs; Azure subscription available. Choices marked "my call" below are the designer's defaults and open to change.

## 4. Architecture

```
Flutter app (iOS/Android)      Flutter web viewer (read-only,
        |                       Azure Static Web Apps, free)
        |  REST (OpenAPI-generated Dart client)
        v
API: C# / .NET minimal API, Azure Container Apps
   |            |               |               |
   v            v               v               v
PostgreSQL   Blob Storage    ACS Email      AI Content Safety
             + WebP sizes
             behind Cloudflare CDN
```

The API also serves a small HTML page per story link carrying preview tags (title, cover image) that then opens the app (universal links / app links) or the Flutter web viewer. (my call)

## 5. Authentication and accounts

- Sign-in methods: **Sign in with Apple, Google, and email one-time code**. No passwords anywhere.
- SSO flow: the app obtains an ID token via `sign_in_with_apple` / `google_sign_in`; the API verifies it against the provider's public keys, finds or creates the user, and issues its own short-lived access token (JWT) and a rotating refresh token stored hashed in Postgres.
- Email flow: user enters email, API sends a 6-digit code via ACS. Codes are stored hashed, expire after 10 minutes, allow 5 attempts, and are rate-limited per email and per IP (my call on numbers). Success issues the same tokens as SSO.
- **No account linking or merging.** Each sign-in identity (provider + subject, or email) is its own account. Two different methods are two accounts. (Owner decision.)
- Data model: `users`, `identities` (provider, subject/email, unique per pair), `refresh_tokens`, `tiers`.
- App Store review needs a demo account with a fixed code (my call: a configured allow-listed email that accepts a fixed code in non-production-like review mode; must not exist for real users).
- Browsing (feed, story viewer, portfolios) works as a guest. Following, replying, publishing, and sending inquiries require sign-in.
- Required by app stores: in-app account deletion; terms acceptance at sign-up; report and block for user content.

### Account deletion (GDPR)
Deleting an account **deletes all data for that user**, not just the profile:
- User row, identities, refresh tokens, profile fields, avatar.
- All their works and chapters, and all image variants in Blob Storage (and CDN purge).
- All replies they wrote (on their own and others' works), their follows (in both directions), notifications about or to them, reports they filed, and inquiries they sent or received.
- For Apple sign-in, revoke the Apple token as Apple requires.
- Deletion runs as a server-side job that is idempotent and verifiable. The user is signed out immediately and the account is unusable straight away; hard deletion completes within a bounded time (target: within 30 days including backups expiring; propose immediate hard delete in the primary database and rely on backup retention expiry). Logs must not contain personal content; log retention is capped.
- Also needed for GDPR (my call, scoped to be small): a data export endpoint, and a privacy policy and terms link in app and web. Confirm with the owner whether export is required at launch.

## 6. Tiers and limits

- A `tiers` table holds each tier's limits; each user has a `tier_id`. One tier exists: **Freemium**, `max_works = 20`.
- The limit counts **published stories** (one photo plus its pins). Deleting a story frees a slot. (my call)
- Enforced server-side: the upload endpoint checks quota before issuing an upload URL, inside a transaction so concurrent uploads cannot exceed the limit. The app only displays the limit.
- Other limits, placeholders to tune (my call): about 40 pins per story, maximum upload file size, rate limits on replies, inquiries, and sign-in codes.
- Paid tiers later would require platform in-app purchases (e.g. RevenueCat). Out of scope now; the table design should not block it.

## 7. Features

Everything in the handoff README applies unless changed here.

### Changes and additions to the handoff
- **Per-pin zoom choice (my default):** when creating a pin the author chooses "zoom in" (with a zoom level, default 2.4) or "stay put". "Stay put" shows the spotlight ring and note without moving the camera. In Explore mode viewers can always zoom out.
- **Notifications:** a `notifications` table is written transactionally with the event (reply on your pin, new follower, new story from someone you follow). `GET /notifications` is cursor-paginated; an unread-count endpoint feeds a badge. The app refreshes on launch, resume, and pull-to-refresh. Replies are grouped at read time ("3 new replies on <title>"). Tapping an item deep-links to the pin. A later push channel would deliver the same rows without changing the table.
- **Contact email:** the contact form sends through the API; the artist's login email is never exposed. A separate optional public contact email can be shown on the About tab. Inquiries are saved in the database first, then emailed via ACS.
- **Feed ranking:** Discover is a simple placeholder ordering (as in the prototype), keyset-paginated. Following shows followed artists' works first.

### Onboarding
- Guest browsing is allowed (see section 5).
- First launch: short welcome, then a built-in demo story, skippable.
- After first sign-in: choose display name and handle, optional avatar, accept terms, pick 3 or more artists to follow to seed Following. With no follows, Following falls back to Discover.
- No notification-permission prompt.

### Edit profile
- Public fields: name, handle, avatar, bio (280 chars), city, discipline, "open for commissions" toggle, website, social handle, studio, reply-time note, services (title, description, price), exhibitions.
- Avatar: optional uploaded photo (same image pipeline, square crop); coloured initial circle remains the fallback.
- Handle: 3-20 characters, lowercase, unique, changeable at most once every 30 days (my call).
- Account section: linked sign-in methods (display only), sign out, delete account.

### Search
- PostgreSQL full-text search plus trigram fuzzy matching; no separate service.
- Two result tabs: Artists (name, handle, city, discipline) and Stories (title, intro, chapter titles).
- Entry: search icon in the feed header. Starts at 2 characters with debounce, paginated, hides blocked or removed content. Empty state: recent searches (stored on device) and suggested artists.

### Moderation
- Report button on stories and replies; block user. Azure AI Content Safety scans replies and uploaded images and auto-blocks unsafe ones, with every block queued for admin review and restore (see section 14). User-reported content is hidden pending review.

## 8. Data model (outline)

Artist and Work shapes are in the handoff README. Additional or changed entities: `users`, `identities`, `refresh_tokens`, `tiers`, `profiles` (public fields above), `follows`, `works`, `chapters` (x, y, zoom nullable meaning "stay put", title, body, meta), `replies`, `notifications`, `inquiries`, `reports`, `blocks`, `email_codes`. Pin coordinates are 0-1 fractions so they survive image resizing. Image variants: thumb, feed, full, as WebP, generated after upload.

## 9. Images

The app uploads straight to Blob Storage using a short-lived upload URL issued by the API after the quota check. A function or background step generates variants and handles EXIF rotation. Clients receive CDN URLs. Deleting a work or account deletes all variants.

## 10. Quality and testing

TDD with 80%+ coverage on the API and shared Flutter logic. Unit tests for the camera/zoom math and quota logic; integration tests for the API against a real Postgres; end-to-end tests for the critical flows (sign in, publish a story, walk through it, reply, delete account).

## 11. Left to the builder

Project and folder structure, state-management and routing packages in Flutter, exact schema and indexes, migration tool, OpenAPI tooling, CDN cache rules, function versus background-service choice for image variants, IaC approach (Bicep or Terraform), the report-review path, and the exact rate-limit numbers.

## 12. Calls made during review (designer's calls, open to change)

- **Undesigned screens** (edit profile, search, onboarding, notifications list, sign-in sheet, terms, empty/error/offline states): the builder designs them following the prototype's tokens, typography, and component style. No external designer is assumed.
- **Web viewer scope:** the same Flutter story-viewer code, including full zoom/pan/spotlight, for a single story reached by share link. Replies are visible read-only. Portfolios, feed, and search are not on web. Sign-in is not offered on web; it shows an "Open in app" prompt. Public read endpoints for a story, its chapters, and replies must work without auth. Universal links / app links are in scope (association files hosted on the owner's domain).
- **Two accounts per person:** accepted. No "already has an account" warning. Onboarding and the linked-methods screen state plainly that each sign-in method is its own account.
- **Drafts and quota:** picking a photo creates a server-side draft work and its upload. A draft counts toward the 20-slot quota. Unpublished drafts expire after 24 hours; a cleanup job deletes the draft and its blobs. Quota is checked in a transaction when the draft is created.
- **"Stay put" pins** (`zoom` is null): the camera animates to the unzoomed view (scale 1, translate 0,0) and the spotlight ring moves to the pin's screen position. This is deterministic and keeps the pin visible. Tests encode this rule.
- **Moderation states (see section 14):** a story is `draft`, `pending`, `published`, `blocked`, or `removed`. Scanning runs as a background step after publish is requested. A flagged image (medium severity or above) makes the story `blocked`; a flagged reply is hidden; each creates an admin-queue item. An admin can restore or confirm. User-reported content is hidden pending review.
- **Blocks:** mutual in effect. Blocking removes follows in both directions and hides each user's stories, replies, and notifications from the other, in feed, search, replies, and notifications.

## 13. Owner decisions on open questions

- **Data export is in scope for launch.** An authenticated user can request an export of all their data (profile, works and chapters, replies, follows, notifications, inquiries they sent) as a downloadable archive (JSON plus their original images). Delivery mechanism is the builder's call (suggest: background job, then a short-lived download link sent by email). Rate-limited to one request per day (my call).
- **Deletion:** hard delete in the primary database and blob storage promptly after the request, with backups allowed to expire on their normal schedule, within 30 days. Accepted by the owner. A privacy advisor should confirm before public launch.
- **Release scope:** iOS, Android, and the web story viewer ship together.
- **Naming:** "Vela" is a **code name only**. It appears to be taken as a public name, so a final product name is to be chosen later. User-facing strings, bundle identifiers, domains, and store listings must come from a single configurable place so the rename is cheap.
- **Minimum age: adults only (18+).** Age confirmation at sign-up alongside terms acceptance.
- **Legal documents required before public launch:** privacy policy, terms of service, and a content/copyright policy with a takedown process. Users must warrant they own what they upload. The builder drafts placeholders and in-app/web links; the owner arranges final text and legal review.
- **Open launch items (do not block building):** who reviews reports and how fast; seeding launch content (invited artists, rights-cleared demo story); final product name and icon.
- **Alt text (my call, included):** optional description field per story and per pin, used by screen readers.
- **Repo:** git repository at `/home/oliver/vela`, remote `git@github.com:chestnutcone/photo_walkthrough_app.git`. Layout: `app/` (Flutter), `api/` (.NET), `infra/` (Bicep), `web/` (Flutter web viewer build target and share-page assets). Flutter state management Riverpod, routing `go_router` (my calls). Local dev via Docker (Postgres) and Azurite; Azure budget alert at about twice expected monthly cost; Postgres built-in point-in-time restore for backups.
- **Accounts and deployment are deferred.** The owner has no Apple Developer account and may have a Google one. Development proceeds without them:
  - Auth providers sit behind an interface. Development and tests use a fake provider; real Apple and Google verification is implemented and unit-tested against recorded/test tokens but cannot be exercised end to end until the accounts exist.
  - Sign in with Apple on a physical device, TestFlight, and App Store submission are blocked until the Apple Developer account exists. This is a release dependency, not a build dependency, and the plan must list it as such.
  - Universal link / app link association files and the share-link domain are configured at deployment. The assumption (not confirmed) is that the owner's existing email-sending domain, or a subdomain of it, hosts them.
  - Infrastructure is defined as code so deployment to Azure can happen later without rework.

## 14. Additions after plan review (owner-requested)

- **Target market: United States.** EU-specific duties (DSA) are out of scope. US duties apply instead: reporting apparent child sexual abuse material to NCMEC's CyberTipline and preserving the evidence (18 U.S.C. 2258A); a DMCA copyright takedown and counter-notice process with a registered DMCA agent; CCPA/CPRA deletion rights (already covered by the full-delete design). Azure region is a US region (my call: East US 2). Legal text and obligations must be confirmed by counsel.
- **Illegal-content flagging and user removal.** Users can report a story, reply, or user with a reason that includes "Illegal content". Admins review a queue and can remove content, hide replies, and **suspend a user**. Suspension revokes all tokens at once, removes their public content, blocks sign-in (`account_suspended`), and blocks re-signup with the same sign-in identity (stored as a hash). Every admin action is written to an audit log.
- **Evidence hold (legal exception to deletion).** When an admin marks content as illegal, a copy of the evidence is stored in a restricted container for at least one year. Account deletion removes everything else but must not purge held evidence. The privacy policy must state this. An admin, after review, triggers the NCMEC report (human in the loop); the submission code sits behind an interface and is faked until the owner has an NCMEC reporting account.
- **Submissions.** (a) Users submit reports, including illegal-content reports. (b) Rights-holders submit DMCA takedown notices through a public form, and owners can submit a counter-notice. (c) NCMEC CyberTipline submissions by an admin. (d) App Store and Google Play submissions are tracked as release dependencies.
- **Admin tool, manual review first.** An admin-only flagged-content monitor: counts per status, age of the oldest unreviewed item, a queue filtered by status, reason and source (`user_report`, `auto_flag`, `dmca`), thumbnails, the owner's history, and inline actions. Automatic blocking and image recognition are a later design, added only if manual review volume becomes too high; the `source` field lets them feed the same queue. Minimal server-rendered admin pages inside the API at `/admin`, signed in through the same email-code flow but only for allow-listed admin emails, with a cookie session and CSRF protection.
- **Content Safety auto-blocks, admins can revert (owner decision).** Azure Content Safety scans uploads and replies. Severity at medium or above blocks the story or hides the reply immediately and adds an `auto_flag` item to the queue. An admin can restore it (it is then never auto-blocked again) or confirm the removal. If the scan service is unavailable after retries, content is published and a `scan_unavailable` item is queued (designer's call: fail-open). Stronger image recognition (including known-CSAM hash matching, subject to counsel's advice) is designed later.
- **Report limit.** Each account may file at most 10 reports per UTC day (configurable) to limit abuse of the report button.
- **Ban and reinstate.** Admins can ban an account from the queue or from a user lookup page (handle, name, or id), with a required reason, and can reinstate it. Banning cannot target oneself or another admin.
- **Crash and error reporting.** Sentry for the Flutter app and the .NET API (free tier to start), with personal data scrubbed before events leave the device or server. Logs and events must not contain emails or reply/inquiry text.
- **Spam and bot protection.** Per-endpoint rate limits (per IP, per email, per user), Cloudflare in front of the API (WAF and bot protection), device attestation on sign-in, email-code and upload-start endpoints (Apple App Attest, Google Play Integrity) once the developer accounts exist, a cap on new accounts per IP per day, a disposable-email blocklist, and request size limits.
- **Security review.** A written threat model, automated dependency, secret and code scanning in CI, a security-reviewer pass over auth, uploads, admin, and deletion before launch, and a checklist based on OWASP ASVS (API) and MASVS (app). An external penetration test is recommended before public launch.
