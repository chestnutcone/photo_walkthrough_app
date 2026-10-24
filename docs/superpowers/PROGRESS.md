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
- [x] M1 Foundation done (T1, T2)
- [x] M2 API core done (A1-A6)
- [x] M3 API features done (A7-A13)
- [x] M4 Admin and safety done (A14-A16)
- [x] M5 App core done (B1-B5)
- [ ] M6 App features done (B6-B14)
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
- [ ] B14 Reporting UI, suspended state, crash reporting, attestation
- [ ] C1 Bicep and deployment workflow
- [ ] S1 Security review gate

## Log (newest first; one line per event: date, task, result, commit)
- 2026-10-23 M5 reached: B1-B5 complete. 214 app tests pass. Pushed.
- 2026-10-23 M4 reached: A14b, A15, A16 complete (fix commits d1cf76b, b8b648a, 13b2e1d). 926+ API tests pass. Pushed.
- 2026-10-23 A15 complete (4be2c1c, 534a1b2, fix b8b648a). 905 API tests pass. Not yet pushed.
- 2026-10-23 A14 complete: A14b (e093620, bda3613, 07a0fd6, a77af4b, fix d1cf76b). 771 API tests pass. Not yet pushed (M4 pending A15, A16).
- 2026-10-22 M3 reached: A7-A13 complete (e631618, 1f75178, 811af34, 686f382, ff35eb9, 1c5df5e, 850a17f). Pushed.
- 2026-10-22 M2 reached: A1-A6 complete (56fb231, d28b26e, a2277e1, 1fdcd5c, 1d81d0f, ccb8aef). Pushed.
- 2026-10-22 M1 reached: T1 (a8dca9b), T2 (503a45a). Pushed branch build/mvp.
- 2026-10-22 Design approved, plan written, progress file created. No code yet.
