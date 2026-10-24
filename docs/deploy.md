# Deploying Vela (Azure + Cloudflare)

Templates: `infra/main.bicep` (+ `infra/modules`). Check locally with `scripts/validate-infra.sh` (no Azure login needed).
"Vela" is a code name: domains and ids below are placeholders from `infra/parameters.dev.json`.

## What gets created (resource group `rg-vela-<env>`)

| Resource | Cost note |
|---|---|
| Container Apps environment + API app | Consumption, `minReplicas` 0 (scale to zero), max 2 |
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
- Scale to zero means the API's background workers (draft cleanup, account deletion, export, take-down reconcile, evidence
  retention) only run while a replica is up. They catch up on the next request. Set `minReplicas` to 1 (about a few dollars a
  month at 0.5 vCPU) once real users arrive or if take-downs must finish promptly.
- Postgres firewall allows "Azure services" (Container Apps has no fixed egress without a VNet). Protection is the password + TLS.

## One-time owner steps

1. **Azure**: subscription, `az login`. Create an app registration with a federated credential for this repo
   (environment `azure-infra`), give it Contributor and User Access Administrator on the subscription (role assignments are in
   the template). Set repo variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; add required reviewers to
   the `azure-infra` environment.
2. **Parameters**: edit `infra/parameters.dev.json` (domains, emails, budget). Never put a secret in it; the validator fails if
   you do. Add `cloudflareIpv4Ranges` from <https://www.cloudflare.com/ips-v4> once Cloudflare is in front (blocks direct-to-origin access).
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
5. Create the API token and zone id for purging (`cloudflare-api-token`, `cloudflareZoneId`). Purge by prefix is used for
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

- NCMEC reporting is unconfigured in production (needs an ESP account); illegal-content reports stay queued.
- Orphan `originals` blobs from SAS URLs used after deletion have no sweep yet.
- Data Protection keys are stored unencrypted in the private `dataprotection` container (account key protects them); add a Key Vault
  key wrap if that is not acceptable.
- Apple sign-in token revocation on account deletion is not implemented.
