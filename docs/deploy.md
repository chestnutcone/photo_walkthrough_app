# Deploying Vela (Azure + Cloudflare)

Templates: `infra/main.bicep` (+ `infra/modules`). Check locally with `scripts/validate-infra.sh` (no Azure login needed).
"Vela" is a code name: domains and ids below are placeholders from `infra/parameters.dev.json`.

## What gets created (resource group `rg-vela-<env>`)

| Resource | Cost note |
|---|---|
| Container Apps environment + API app | Consumption, max 2; `minReplicas` defaults to 1 in prod and 0 elsewhere |
| PostgreSQL Flexible Server | Burstable `Standard_B1ms`, 32 GB, 7-day point-in-time restore, TLS 1.2+, `pg_trgm` allow-listed |
| Storage account (LRS) | `works`, `avatars` public read (via Cloudflare); `originals`, `exports`, `evidence`, `dataprotection` private |
| Key Vault | holds every secret; read by a user-assigned identity |
| Log Analytics (+ Application Insights) | daily cap 0.15 GB (about 4.5 GB a month) |
| Static Web App (Free) | web story viewer |
| ACS Email | sending domain is a parameter |
| Budget | alert at 2x `monthlyBudget` (plus a forecast notice at 1x) |

Decisions worth knowing:

- One storage account. The API uses one connection string for all containers, so the account allows container-level public
  access; only `works` and `avatars` opt in and the template pins the rest to `None` on every deploy. Splitting into a second
  public account would allow `allowBlobPublicAccess=false` on the private one but costs an API change for no saving.
- Lifecycle rule deletes `exports` after 7 days. Nothing else is aged out: blob names in `originals` do not show whether an
  upload was published, so orphan originals (SAS issued, then account deleted) need an app-level sweep (known gap, see below).
  `evidence` is never touched.
- The API's background workers (draft cleanup, account deletion, export, take-down reconcile, evidence retention) only run
  while a replica is up, so prod keeps one replica (`minReplicas` default -1 = 1 in prod, 0 otherwise). Dev may scale to zero;
  its workers catch up on the next request.
- Startup check: outside Development and Test the API reads the access level of `originals`, `exports`, `evidence` and
  `dataprotection` and refuses to start if any is not private, or if `works`/`avatars` use Container level (listing). This is the
  runtime guard for the one-account design (the account must allow public blob access). A storage outage only logs a warning.
- Postgres firewall allows "Azure services" at launch (reachable from any Azure tenant; protection is a long random password +
  TLS). Hardening without a VNet: after pass 2 put the app's `az containerapp show --query properties.outboundIpAddresses` into
  `appOutboundIps` and set `allowAzureServices=false` (re-check after any environment change). Follow-up: a VNet-integrated
  Container Apps environment with a delegated Postgres subnet.
- Least privilege gap: the app connects as the server admin and `MigrateOnStartup` runs DDL as that account. Follow-up: a
  separate runtime role with DML only, with migrations run by a bundle under the admin account.
- Postgres storage auto-grows (never goes read-only when full); set an Azure Monitor alert on storage percent.

## One-time owner steps

1. **Azure**: subscription, `az login`. Create an app registration with a federated credential for this repo
   (environment `azure-infra`, restrict it to the main branch). Do not grant subscription-wide User Access Administrator.
   Create the resource group once yourself (`az group create -n rg-vela-<env> -l eastus2`), then give the app Contributor on that
   group plus a role-assignment permission scoped to the same group (`Role Based Access Control Administrator`, with a condition
   limiting it to the Key Vault Secrets User, Storage Blob Data Contributor and Storage Blob Delegator roles). The template's
   subscription-scope deployment still needs rights to write the resource group itself: run pass 1 yourself, or add
   `Microsoft.Resources/subscriptions/resourceGroups/write` through a small custom role. Set repo variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; add required reviewers to
   the `azure-infra` environment.
2. **Parameters**: edit `infra/parameters.dev.json` (domains, emails, budget). Never put a secret in it; the validator fails if
   you do. Set `cloudflareIpv4Ranges` from <https://www.cloudflare.com/ips-v4> **before pass 2** in prod: `validate-infra.sh` fails
   prod with `deployApp=true` and an empty list, which would leave the origin open. Refresh the list whenever Cloudflare changes
   it (compare `curl -s https://www.cloudflare.com/ips-v4` with the parameter quarterly and before each release); a stale list
   blocks new Cloudflare ranges and causes an outage. `budgetStartDate` is a fixed first-of-month date; never change it after
   the first deployment.
3. **Pass 1** (everything except the app), from your shell so the DB password never touches a file or the workflow:
   `az deployment sub create -l eastus2 -f infra/main.bicep -p infra/parameters.dev.json -p postgresAdminPassword="$(openssl rand -base64 32)"`
   (keep that password somewhere safe: only the connection string in Key Vault holds it). Later deploys omit it; it is kept.
4. **Key Vault secrets** (you need `Key Vault Secrets Officer` on the vault). Infra already wrote `postgres-connection-string`,
   `storage-connection-string`, `acs-connection-string`, `appinsights-connection-string`. You add:
   `jwt-signing-key` (32+ bytes random), `email-code-pepper` (32+ chars), `ban-pepper` (32+ chars; rotating it voids bans),
   `content-safety-key`, `cloudflare-api-token` (permission: Zone > Cache Purge only), `sentry-dsn`,
   `play-integrity-decryption-key`, `play-integrity-verification-key` (from Play Console; needed when `playIntegrityPackageName` is set),
   `review-code` (only while `reviewMode` is on), `registry-password` (only for a private registry).
   Example: `az keyvault secret set --vault-name <kv> --name jwt-signing-key --value "$(openssl rand -base64 48)"`.
5. **ACS email domain**: add the DNS records shown by `emailDomainVerification` (deployment output) at your DNS host, wait for
   Verified in the portal, then redeploy with `linkEmailDomain=true`.
6. **Content Safety**: create an Azure AI Content Safety resource (F0 tier), pass `contentSafetyEndpoint`, store its key above.
7. **Store and identity values** (parameters): `appleClientId`, `googleClientIds`, `appleTeamId`, `androidSha256Fingerprints`,
   `appAttestAppId` (`TEAMID.bundle.id`), `playIntegrityPackageName`, `dmcaAgent*` (needs legal review), `adminEmails`
   (comma-separated allow-list; first admin signs in at `/admin` with an email code), Sentry DSN (secret above).
   Also set the app's dart-defines (`LINKS_DOMAIN`, `SENTRY_DSN`, `PLAY_CLOUD_PROJECT_NUMBER`, `APP_ATTEST_ENABLED=true`, store URLs).
8. **Container image**: `docker build -f api/Dockerfile -t ghcr.io/<owner>/vela-api:<tag> .` then `docker push`. Public GHCR package
   needs no credentials; a private one needs `registryUsername` + `registry-password`.
9. **Pass 2**: redeploy with `deployApp=true`, `apiImage=ghcr.io/<owner>/vela-api:<tag>`. Output `apiUrl` is the app FQDN.
10. **Later releases**: push a new image, then
    `az containerapp update -g rg-vela-<env> -n ca-vela-<env>-api --image ghcr.io/<owner>/vela-api:<new-tag>`.
11. **Web viewer**: build `app` for web (see `app/web/README.md`), deploy to the Static Web App
    (`az staticwebapp secrets list` gives the deployment token; use `Azure/static-web-apps-deploy`). SPA fallback for `/s/*` and the CSP are in that README.

## Database migrations

The startup probe allows about 10 minutes (120 x 5 s) because migrations run before the server listens. For a release with a
long migration (large index or backfill), build a migration bundle (`dotnet ef migrations bundle`), run it as a separate step
under the admin account, then deploy the new revision.

The container sets `Database__MigrateOnStartup=true`: each starting replica runs `Database.Migrate()`. EF Core takes a
database-wide lock, so replicas starting together cannot corrupt the schema; the loser waits, then finds nothing to apply.
Rules for migrations so this stays safe: additive first (add column/table, deploy code, drop later), no long locks, and never
a migration the previous revision cannot run against (revisions overlap briefly). To migrate out of band instead, set the
setting to `false` and run an `dotnet ef migrations bundle` from a machine allowed through the Postgres firewall.

## Cloudflare

1. Add the zone; set SSL/TLS to Full (strict).
2. `api.<domain>` (or the apex): proxied record to the Container App FQDN. Container Apps managed certificates cannot renew behind
   the proxy, so create a Cloudflare Origin CA certificate, upload it with `az containerapp env certificate upload`, and bind the
   hostname with `az containerapp hostname bind`. Share pages (`/s/{id}`), `/.well-known`, `/admin` and `/v1` all use this host
   (`Branding:Domain`).
3. `img.<domain>`: proxied CNAME to `<storage>.blob.core.windows.net` plus an Origin Rule that rewrites the Host header to the
   blob host; set `imageHost=img.<domain>` so `Storage:PublicBaseUrl` points at it. Cache rule: cache everything, edge TTL 1 month
   (purge-on-delete is wired). Only `works` and `avatars` are anonymously readable.
4. Viewer host (`viewerHost` or the SWA default hostname) must differ from `brandingDomain`; the API refuses to start otherwise.
5. `imageHost` must be set (and be on the zone) when `cloudflareZoneId` is: purge URLs outside the zone are skipped and logged by
   count rather than failing deletions forever. Prefix purge is tried first; if Cloudflare rejects it (4xx, plan or token), the
   known variant files (thumb/feed/full, avatar-256) are purged instead. Create the API token and zone id for purging (`cloudflare-api-token`, `cloudflareZoneId`). Purge by prefix is used for
   deleted users and works; if the API logs purge failures, check the token's zone scope.
6. Client IPs: the ingress sets `X-Forwarded-For`; `Abuse__TrustedProxyHops=1` makes the API treat its last entry as the
   connecting address and trust `CF-Connecting-IP` only if that address is a Cloudflare edge. `cloudflareIpv4Ranges` restricts
   ingress to Cloudflare so the check cannot be bypassed by calling the origin directly.

## App review mode and attestation

- `reviewMode=true` plus `reviewEmails` and secret `review-code` accepts a fixed code for those accounts. Turn it off after review.
- `requireAttestation` defaults to true and must be true at launch. Set false only for first bring-up (before store apps exist);
  with it true the API refuses to start unless both Play Integrity and App Attest settings are present. Decide the App Attest
  per-install design before enabling it in production (see the progress ledger).

## Known gaps (owner or later work)

- Origin protection follow-up: the IP allow-list lets any Cloudflare tenant reach the origin. Add Authenticated Origin Pulls
  (mTLS) or a secret-header Origin Rule that the API checks.
- GitHub Actions are pinned to commit SHAs (tag in a trailing comment); Dependabot proposes updates weekly.
- NCMEC reporting is unconfigured in production (needs an ESP account); illegal-content reports stay queued.
- Orphan `originals` blobs from SAS URLs used after deletion have no sweep yet.
- Data Protection keys are stored unencrypted in the private `dataprotection` container (account key protects them); add a Key Vault
  key wrap if that is not acceptable.
- Apple sign-in token revocation on account deletion is not implemented.

## Security settings added at the S1 review gate

See `docs/security/` for the threat model, checklists and findings.

- `Admin__BanPepper` (32+ characters) is required outside Development and Test **even when `Admin__Enabled=false`**: bans recorded
  earlier are matched with it. Never change it without re-hashing the ban table.
- `Security__Hsts` (default on outside Development and Test) sends `Strict-Transport-Security: max-age=31536000; includeSubDomains`
  on every response. `preload` is deliberately not sent; submit to the preload list only as a conscious decision.
- `Admin__LoginMinimumMs` (default 600) pads every admin sign-in answer so response time does not reveal allow-listed addresses.
- CORS: only `Web__ViewerUrl`'s origin may read the public endpoints from a browser (GET/HEAD/OPTIONS, no credentials). If the viewer
  moves, change that setting; nothing else needs touching.
- Rate limits: `Abuse__RateLimits__user-read__Permit` (signed-in reads, default 600/minute) joins `anon-read`, `write`, `upload`,
  `auth-email` and `public-form`; `/admin` is limited too.
- Give the application's database role `INSERT` and `SELECT` only on `admin_actions` so the audit trail cannot be edited by the app.
- Put Cloudflare Access (or equivalent) in front of `/admin`: the pages are single-factor (an emailed code to an allow-listed address).
- Android release builds: provide a real signing keystore (the Gradle config still signs with the debug key) and build with
  `--obfuscate --split-debug-info`.


## Settings and behaviour added by the S1b fixes

- `Images__MaxConcurrentDecodes` (default 2) and `Images__DecodeWaitSeconds` (default 5): image decodes allowed at once per
  replica, and how long a request waits for a slot before it answers 503 `busy` with `Retry-After`. Each decode can use up to
  160 MB of native memory, so with the 1 GiB container keep the limit at 2 unless the memory is raised.
- Migrations `PublishClaim`, `SealedOriginal`, `DeletionDeferral` and `DmcaClaimantMailbox` add nullable columns only; they apply
  with `Database__MigrateOnStartup` like the others.
- **Sealed originals (S1b-3).** Works published from now on read only `originals/sealed/{workId}/{random}`; the row keeps its
  SHA-256. Rows published before this change have `sealed_original_blob = NULL` and fall back to `originals/works/{id}`. Backfill
  (one-off, only needed if such rows exist in a real environment): for each non-draft work with a NULL sealed name, copy
  `originals/works/{id}` to a new `originals/sealed/{id}/{guid}` name, record the name and the lower-case hex SHA-256 of the copied
  bytes, then delete the old blob. Pre-launch there are none, so nothing has to be run.
- **Deferred account deletion (S1b-2).** `DELETE /v1/me` signs out and hides the account at once as before. When the account has
  works pending or blocked, open reports against its content, DMCA notices or evidence holds, the hard delete waits until those
  are resolved, at most 30 days after the request (`users.deletion_deferred_until`; the sweep re-checks hourly) and the response
  says `deferred_review` with `completesBy`. Counsel to confirm the 30-day figure against the GDPR erasure window.
- **Blob soft delete (S1b-15).** Deleted blobs and containers are retained for 7 days. Bytes removed by account deletion or a
  take-down are therefore recoverable by an operator (never served) for up to 7 days before Azure purges them; the privacy policy
  states this. An operator restoring a soft-deleted blob of a deleted account must delete it again.
