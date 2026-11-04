# Run the API locally (Development mode)

One command starts Postgres 16 and Azurite in Docker and the API on `0.0.0.0:5080`:

```bash
scripts/run-local-api.sh      # idempotent; log in .local/api.log, pid in .local/api.pid
python3 tools/seed_local.py   # 2 artists, 5 published stories, replies, a follow (needs Pillow); idempotent
scripts/stop-local-api.sh     # stops the API and removes the containers (--keep keeps them)
```

Containers: `vela-e2e-pg` (host port 55432) and `vela-e2e-azurite` (host port 10000). Override with
`VELA_E2E_PG_PORT`, `VELA_E2E_AZURITE_PORT`, `VELA_E2E_API_PORT`. Toolchains: `scripts/with-env.sh <cmd>` puts
dotnet, flutter, JDK 17 and the Android SDK on PATH (see `scripts/dev-env.sh`, `scripts/android-env.sh`).

What the script sets (all Development-only, nothing secret):

| Setting | Why |
|---|---|
| `ASPNETCORE_ENVIRONMENT=Development` | fake SSO provider (`idToken` = `fake:<subject>:<email>`), console email sender, fake Content Safety, fake NCMEC |
| `Database__MigrateOnStartup=true` | creates the schema on first start |
| `Abuse__RequireAttestation__Android=false` (iOS is off by default), `Abuse__SignupsPerIpPerDay=100000` | no Play Integrity / App Attest; tests create many accounts from one IP (default limit is 5/day) |
| `Storage__EnsurePublicRead=true` | makes `works`/`avatars` public-read in Azurite as the Azure infra does (otherwise images 403) |
| `Storage__PublicBaseUrl=http://127.0.0.1:10000/...` | the emulator reaches it through `adb reverse tcp:10000 tcp:10000` |
| `Auth__UserStateCacheSeconds=1` | a ban written straight to the DB is seen within a second (default 20 s) |

Email sign-in codes are printed to the API log (`[dev email] sign-in code: 123456`); the address is never printed,
so the newest code is the one for the latest request. `tools/e2e_host_helper.py` (port 5090) serves that code to
on-device tests and can suspend an account (what an admin ban does).

The emulator reaches the host at `http://10.0.2.2:5080`. Build the app with
`--dart-define=API_BASE_URL=http://10.0.2.2:5080 --dart-define=VELA_FAKE_SSO=true`.
