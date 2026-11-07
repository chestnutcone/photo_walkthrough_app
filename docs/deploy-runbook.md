# First deployment runbook (production)

An ordered, start-to-finish guide for putting Vela live for the first time. The facts live in `docs/deploy.md`; this file
puts them in sequence with a check after each step.

**Status of this document: not run end to end.** No real Azure, Cloudflare, Apple or Google deployment has been done. Steps
marked **[untested]** have never been executed against the real service. Expect to correct some of them the first time.
If a command here disagrees with `docs/deploy.md`, `infra/main.bicep` or the scripts, trust the code and fix this file.

"Vela" is a code name; every `vela.example` value below is a placeholder you replace.

## 0. Gate: do not open sign-ups until these are true

- [ ] **Counsel has reviewed the legal texts** (release blocker): `api/src/Vela.Api/Legal/{privacy,terms,content,dmca}.md` and the
      behaviours listed first in `docs/owner-todos.html`. Until then the texts are drafts.
- [ ] The DMCA agent is registered with the US Copyright Office (the API refuses to start in Production without one).
- [ ] You can answer who reads the moderation queue and the NCMEC process (reports stay queued until you submit manually).

You can deploy the infrastructure before counsel finishes, but keep sign-ups closed (no app in the stores, `/admin` only).

## 1. Accounts and tools (about a day, mostly waiting for approvals)

| Need | Notes |
|---|---|
| Azure subscription | Region `eastus2`. `az login` on your machine. |
| Cloudflare account and a domain on it | Zone must exist before step 5. |
| Apple Developer account | Sign in with Apple, Associated Domains. Needs a Mac for any iOS build. |
| Google Play Console and a Google Cloud project | Play Integrity, Google sign-in client IDs. |
| Sentry project | DSN for app and API. |
| Docker, `az` CLI, `openssl`, Flutter 3.47.7 | Docker is needed for the API image (CI never builds it). |

Check:
```bash
az account show --query '{sub:id,tenant:tenantId}'
docker version --format '{{.Server.Version}}'
```

## 2. Choose names and fill the parameters

1. Copy the values you need into `infra/parameters.prod.json`, replacing **every** `REPLACE_ME-...`. Domains, admin emails, DMCA
   agent, Cloudflare zone id and image host, Content Safety endpoint, Apple and Google ids, Android SHA-256 fingerprints.
2. Never put a secret in the file; the validator fails if you do.
3. `cloudflareIpv4Ranges`: refresh from <https://www.cloudflare.com/ips-v4> now (prod with the app deployed and an empty list
   is refused).
4. `budgetStartDate` is a fixed first-of-month date. Never change it after the first deployment.

Check (offline, no Azure needed):
```bash
scripts/validate-infra.sh --deploy infra/parameters.prod.json
```
It must print no `FAIL`. It fails while any placeholder, DMCA agent, admin allow-list, Cloudflare zone/image host or
Content Safety endpoint is empty.

## 3. Azure access and the resource group

Follow `docs/deploy.md` "One-time owner steps" item 1 exactly. In short:
```bash
az group create -n rg-vela-prod -l eastus2
```
Then create the app registration with a federated credential for the repo (environment `azure-infra`, main branch only), give it
Contributor on the resource group plus the scoped role-assignment permission, and set the repo variables
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. Add required reviewers to the `azure-infra` environment.
**[untested]** The role-assignment condition in particular.

You may skip the workflow and deploy from your own shell (steps 4 and 8 do). The workflow's `parameters-file` input
defaults to the **dev** file: for production set it to `parameters.prod.json`. **[untested]**

## 4. Pass 1: everything except the app

Preview first, then deploy. The database password is generated here and never written to a file:
```bash
az deployment sub what-if -l eastus2 -f infra/main.bicep -p infra/parameters.prod.json \
  -p postgresAdminPassword="placeholder-for-preview-only"
az deployment sub create -l eastus2 -f infra/main.bicep -p infra/parameters.prod.json \
  -p postgresAdminPassword="$(openssl rand -base64 32)"
```
Save the password in your password manager: afterwards only the Key Vault connection string holds it. Later deploys omit it.
**[untested]** `what-if` has never been run; read its output for anything that would delete or replace a resource.

Check:
```bash
az resource list -g rg-vela-prod -o table
```
You should see the Container Apps environment, Postgres, storage account, Key Vault, Log Analytics, Static Web App, ACS Email and
the budget. The API app does not exist yet (that is pass 2).

## 5. Key Vault secrets

Give yourself `Key Vault Secrets Officer` on the vault, then add the secrets infra did not create
(`postgres-connection-string`, `storage-connection-string`, `acs-connection-string`, `appinsights-connection-string` already exist):

```bash
KV=<vault name from the deployment output>
az keyvault secret set --vault-name $KV --name jwt-signing-key       --value "$(openssl rand -base64 48)"
az keyvault secret set --vault-name $KV --name email-code-pepper     --value "$(openssl rand -base64 32)"
az keyvault secret set --vault-name $KV --name ban-pepper            --value "$(openssl rand -base64 32)"
az keyvault secret set --vault-name $KV --name content-safety-key    --value "<from the Content Safety resource>"
az keyvault secret set --vault-name $KV --name cloudflare-api-token  --value "<Zone > Cache Purge only>"
az keyvault secret set --vault-name $KV --name sentry-dsn            --value "<dsn>"
az keyvault secret set --vault-name $KV --name play-integrity-decryption-key   --value "<base64 AES-256 from Play Console>"
az keyvault secret set --vault-name $KV --name play-integrity-verification-key --value "<base64 EC key, SubjectPublicKeyInfo>"
```
**Never rotate `ban-pepper`**: it voids every ban. `review-code` is only needed while `reviewMode` is on (store review only).

Check: `az keyvault secret list --vault-name $KV --query '[].name' -o tsv` lists all of the above.

## 6. Email domain and Content Safety

1. **ACS email:** add the DNS records from the deployment output `emailDomainVerification` at your DNS host, wait for
   **Verified** in the portal, then redeploy with `linkEmailDomain=true`. **[untested]**
2. **Content Safety:** create an Azure AI Content Safety resource (F0 tier). Its endpoint goes in the parameters (step 2), its key
   in Key Vault (step 5). **[untested]**

Check: request a sign-in code to your own address after pass 2 (step 9); the mail must arrive.

## 7. API image

```bash
docker build -f api/Dockerfile -t ghcr.io/<owner>/vela-api:<tag> .
docker push ghcr.io/<owner>/vela-api:<tag>
```
A public GHCR package needs no credentials; a private one needs `registryUsername` plus the `registry-password` secret.
**[untested]** CI never builds this image, so the first build may fail: fix it before anything else.

Check: `docker run --rm ghcr.io/<owner>/vela-api:<tag> --help` starts (it will refuse to run without settings, which is fine).

## 8. Pass 2: deploy the app

```bash
az deployment sub create -l eastus2 -f infra/main.bicep -p infra/parameters.prod.json \
  -p deployApp=true -p apiImage=ghcr.io/<owner>/vela-api:<tag>
```
The startup probe allows about 10 minutes because migrations run first. The output `apiUrl` is the app's own hostname.

If the app does not become healthy, read its log. The API refuses to start in Production, and **names every missing key**, when:
the DMCA agent, Cloudflare zone/image host/token, Play Integrity values, admin settings, or a private storage container
setting is wrong; also when the viewer host equals the branding domain.
```bash
az containerapp logs show -g rg-vela-prod -n ca-vela-prod-api --tail 100
```

Check:
```bash
curl -s https://<apiUrl>/health        # {"status":"ok"}
```

## 9. Cloudflare

Follow `docs/deploy.md` "Cloudflare" in order. The parts people miss:

1. SSL/TLS **Full (strict)**.
2. `api.<domain>`: proxied record to the app FQDN. Create a **Cloudflare Origin CA certificate**, upload it with
   `az containerapp env certificate upload`, bind with `az containerapp hostname bind` (managed certificates cannot renew behind the proxy).
3. `img.<domain>`: proxied CNAME to `<storage>.blob.core.windows.net`, an Origin Rule rewriting the Host header to the blob host,
   a cache rule (edge TTL 1 month), and a Transform Rule adding `X-Content-Type-Options: nosniff`.
4. **Cloudflare Access in front of `/admin`.**
5. WAF or bot-fight rate rules on `/v1/auth/*` and `POST /v1/works` (the only control across addresses for iOS clients, see `docs/security/threat-model.md` R-3).
6. SPA fallback for `/s/*` on the viewer host, and the CSP from `app/web/README.md`.

**[untested]** all of this. In particular the Origin Rule and the forwarded-for shape: the API trusts `CF-Connecting-IP` only when
the connecting address is a Cloudflare edge (`Abuse__TrustedProxyHops=1`).

Check:
```bash
curl -s https://api.<domain>/health
curl -sI https://img.<domain>/<a public works blob> | grep -i 'x-content-type-options\|cf-cache-status'
curl -sI https://api.<domain>/admin           # must be intercepted by Cloudflare Access
```
Then tighten the origin: put `az containerapp show -g rg-vela-prod -n ca-vela-prod-api --query properties.outboundIpAddresses`
into `appOutboundIps`, set `allowAzureServices=false`, redeploy. Re-check after any environment change.

## 10. Web viewer

```bash
cd app
flutter build web -t lib/main_web.dart --release --no-web-resources-cdn \
  --dart-define=API_BASE_URL=https://api.<domain> --dart-define=LINKS_DOMAIN=<links domain>
../scripts/check-web-viewer-origins.sh build/web
```
(Use the exact command and defines in `app/web/README.md`; this repeats the CI shape.) Deploy `build/web` to the Static Web App
with `Azure/static-web-apps-deploy`; `az staticwebapp secrets list` gives the deployment token. **[untested]**

Check: open `https://<viewer host>/s/<a published story id>` after step 11.

## 11. Smoke test against production

Use a throwaway account. Do these before any store submission:

1. Sign in with an email code (mail arrives; code works once; five wrong codes lock).
2. Complete onboarding, accept the terms, confirm 18+.
3. Create a story, add a pin, upload a photo, publish. Open the story's share link on the web viewer.
4. Report your own story from a second account; confirm it lands in `/admin`. Confirm a report from a younger-than-24-hour account queues instead of hiding (illegal content).
5. Delete the account: the export and deletion workers run in the background, so this proves a replica is alive.
6. Confirm the image host serves the photo, and that a deleted story's image stops being served after the cache purge.
7. In Application Insights check the `Usage` table and that `vela.abuse.unattested_limited` is near zero.

An automated version of the app side already passes on the emulator (25/25, `docs/review/e2e-report.md`), but it ran
against a local API, not these services.

## 12. Mobile builds

Follow `docs/deploy.md` "App release builds" and "App review mode and attestation".

**Android (the first store target)**
1. Create the upload key (`keytool`, see deploy.md), write the git-ignored `app/android/key.properties`, enrol in Play App Signing.
2. Put the **app signing key** SHA-256 from Play Console into `androidSha256Fingerprints`, redeploy, and publish `assetlinks.json`.
3. In Play Console link a Cloud project, enable the Play Integrity API, note its **project number**, choose your own response
   encryption keys and store them (step 5).
4. Build:
   ```bash
   flutter build appbundle --release \
     -PlinksDomain=<links domain> \
     --dart-define=LINKS_DOMAIN=<links domain> --dart-define=API_BASE_URL=https://api.<domain> \
     --dart-define=PLAY_CLOUD_PROJECT_NUMBER=<number> --dart-define=SENTRY_DSN=<dsn> \
     --dart-define=PLAY_STORE_URL=<url> --dart-define=APP_STORE_URL=<url>
   ```
   The build fails on a missing key, a placeholder, or mismatched links values, which is intended.
5. Upload to an internal test track and install on a **real phone**. Play Integrity cannot be tested on an emulator
   (the emulator test only covers the error path). **[untested]**

**iOS (needs a Mac; attestation deferred)**
1. Replace `applinks:vela.example` in `app/ios/Runner/Runner.entitlements`; enable Sign in with Apple and Associated Domains on the App ID; regenerate the profile.
2. `scripts/check-release-config.sh` with the same values, then `flutter build ipa`.
3. Leave `requireAttestationIos: false` and build without `APP_ATTEST_ENABLED`. **[untested]** nothing iOS has been built or run.

**Store review:** set `reviewMode=true` with `reviewEmails` and the `review-code` secret only for the review window, give the
reviewers that account, then turn it off and redeploy.

## 13. Before opening sign-ups: final checklist

Run through the "Production checklist" in `docs/deploy.md` (it is the authoritative list), and also confirm:

- [ ] Section 0 gate is complete (counsel sign-off recorded).
- [ ] `reviewMode: false`, `requireAttestationAndroid: true`, `minReplicas` at least 1, `malwareScanning: true`.
- [ ] `adminEmails` set, and the first admin has signed in at `/admin` through Access.
- [ ] Budget alert emails reach you; an alert exists on Postgres storage percent.
- [ ] Sentry receives a test error from the API and from the app.

## 14. Rollback and the first week

**Bad API release:** roll the app back to the previous image:
```bash
az containerapp update -g rg-vela-prod -n ca-vela-prod-api --image ghcr.io/<owner>/vela-api:<previous-tag>
```
Migrations are additive (never one the previous revision cannot run against), so rolling the image back is safe. If a migration
itself is the problem, restore from the 7-day Postgres point-in-time backup into a new server and repoint the connection string.
**[untested]** the restore.

**Bad infrastructure change:** redeploy the previous template version; always read `what-if` first.

**First week, daily:**
- `/admin` moderation queue and pending illegal-content reports (NCMEC is manual).
- Log Analytics daily cap (0.15 GB): once hit, further records are dropped for the day. Check the `Usage` table.
- The metric `vela.abuse.unattested_limited`; raise the permit settings if real users hit it.
- Budget and the Defender scan cap (about 11.50 USD a month at most).
- Sentry for crashes.

**Known gaps to schedule** (see `docs/deploy.md` "Known gaps"): authenticated origin pulls, orphan `originals` sweep,
Apple token revocation on deletion, App Attest, unencrypted Data Protection keys, app connects as the DB admin.
