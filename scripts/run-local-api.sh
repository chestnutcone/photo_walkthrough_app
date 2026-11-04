#!/usr/bin/env bash
# Starts (idempotently) Postgres + Azurite in Docker and the Vela API in Development mode on 0.0.0.0:5080.
# State: .local/api.pid, log: .local/api.log. See docs/dev/run-local.md.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck disable=SC1091
. "$ROOT/scripts/dev-env.sh"
STATE="$ROOT/.local"; mkdir -p "$STATE"
PG=vela-e2e-pg; AZ=vela-e2e-azurite
PG_PORT="${VELA_E2E_PG_PORT:-55432}"; AZ_PORT="${VELA_E2E_AZURITE_PORT:-10000}"; API_PORT="${VELA_E2E_API_PORT:-5080}"

ensure_container() { # name, docker-run-args...
  local name="$1"; shift
  if [ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null || echo none)" = true ]; then return; fi
  docker rm -f "$name" >/dev/null 2>&1 || true
  docker run -d --name "$name" "$@" >/dev/null
}
ensure_container "$PG" -e POSTGRES_DB=vela -e POSTGRES_USER=vela -e POSTGRES_PASSWORD=vela_dev_only -p "$PG_PORT:5432" postgres:16
ensure_container "$AZ" -p "$AZ_PORT:10000" mcr.microsoft.com/azure-storage/azurite \
  azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck --loose

for _ in $(seq 1 60); do docker exec "$PG" pg_isready -U vela -d vela >/dev/null 2>&1 && break; sleep 1; done

if [ -f "$STATE/api.pid" ] && kill -0 "$(cat "$STATE/api.pid")" 2>/dev/null && curl -fs "http://127.0.0.1:$API_PORT/health" >/dev/null; then
  echo "API already running (pid $(cat "$STATE/api.pid"))"; exit 0
fi

export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Default="Host=127.0.0.1;Port=$PG_PORT;Database=vela;Username=vela;Password=vela_dev_only"
export Database__MigrateOnStartup=true
export Abuse__RequireAttestation__Android=false
export Abuse__SignupsPerIpPerDay=100000   # e2e creates many accounts from one IP
# Image URLs handed to the emulator point at 127.0.0.1:$AZ_PORT; the emulator reaches it via `adb reverse tcp:$AZ_PORT tcp:$AZ_PORT`.
export Storage__PublicBaseUrl="http://127.0.0.1:$AZ_PORT/devstoreaccount1/"
export Storage__ConnectionString="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:$AZ_PORT/devstoreaccount1;"
export Storage__EnsurePublicRead=true   # works/avatars public-read, like the Azure infra
export Auth__ReviewMode=false
export Auth__UserStateCacheSeconds=1   # a ban set directly in the DB is seen within a second

cd "$ROOT/api/src/Vela.Api"
dotnet build -c Debug --nologo -v q >"$STATE/api-build.log" 2>&1 || { cat "$STATE/api-build.log"; exit 1; }
nohup dotnet run --no-build --no-launch-profile --urls "http://0.0.0.0:$API_PORT" >"$STATE/api.log" 2>&1 &
echo $! >"$STATE/api.pid"
for _ in $(seq 1 90); do
  if curl -fs "http://127.0.0.1:$API_PORT/health" >/dev/null; then echo "API up on :$API_PORT (pid $(cat "$STATE/api.pid"))"; exit 0; fi
  kill -0 "$(cat "$STATE/api.pid")" 2>/dev/null || { tail -30 "$STATE/api.log"; exit 1; }
  sleep 1
done
tail -30 "$STATE/api.log"; exit 1
