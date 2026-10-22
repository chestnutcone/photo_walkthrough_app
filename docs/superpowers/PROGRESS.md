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

## How to resume
1. `cd /home/oliver/vela && git status && git log --oneline | head`
2. Find the first unchecked task below; read its section in the plan.
3. If a task is marked IN PROGRESS, inspect `git diff` for partial work, decide whether to continue or reset that task's files.

## Milestones (commit and push when reached)
- [x] M0 docs committed and pushed (spec, plan, progress) — commit 310df02
- [ ] M1 Foundation done (T1, T2)
- [ ] M2 API core done (A1-A6)
- [ ] M3 API features done (A7-A13)
- [ ] M4 Admin and safety done (A14-A16)
- [ ] M5 App core done (B1-B5)
- [ ] M6 App features done (B6-B14)
- [ ] M7 Infra and security done (C1, S1)

## Task checklist (mark `[x]` when merged and tests pass; `[~]` when in progress)
- [x] T1 Repo scaffold (a8dca9b)
- [ ] T2 OpenAPI contract and generated client
- [ ] A1 Schema and migrations
- [ ] A2 Access and refresh tokens
- [ ] A3 SSO sign-in
- [ ] A4 Email one-time code
- [ ] A5 Profiles, onboarding, handles
- [ ] A6 Blocks, reports, visibility (report limit, auto-flag)
- [ ] A7 Quota, drafts, uploads, image variants
- [ ] A8 Works, chapters, publish, moderation (auto-block)
- [ ] A9 Follows, feed, replies
- [ ] A10 Notifications and inquiries
- [ ] A11 Search
- [ ] A12 Share pages, well-known, legal
- [ ] A13 Account deletion and export
- [ ] A14 Admin moderation, ban/restore, evidence hold, DMCA
- [ ] A15 Abuse and bot protection
- [ ] A16 Telemetry and crash reporting (API)
- [ ] B1 Theme, fonts, branding
- [ ] B2 API wiring, session, router, guest gating
- [ ] B3 Camera math
- [ ] B4 Story viewer
- [ ] B5 Feed
- [ ] B6 Replies sheet
- [ ] B7 Portfolio, followers, contact
- [ ] B8 Sign-in and onboarding
- [ ] B9 Create story
- [ ] B10 Edit profile and settings
- [ ] B11 Search
- [ ] B12 Notifications list
- [ ] B13 Web viewer and deep links
- [ ] B14 Reporting UI, suspended state, crash reporting, attestation
- [ ] C1 Bicep and deployment workflow
- [ ] S1 Security review gate

## Log (newest first; one line per event: date, task, result, commit)
- 2026-10-22 Design approved, plan written, progress file created. No code yet.
