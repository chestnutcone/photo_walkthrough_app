#!/usr/bin/env bash
# Stops the local API (and the e2e "api-attested" instance, if any) and removes the Postgres/Azurite containers
# (data is discarded; pass --keep to keep them).
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
for f in "$ROOT/.local/api.pid" "$ROOT/.local/api-attested.pid"; do
  [ -f "$f" ] || continue
  pid="$(cat "$f")"
  pkill -P "$pid" 2>/dev/null || true
  kill "$pid" 2>/dev/null || true
  rm -f "$f"
done
if [ "${1:-}" != "--keep" ]; then docker rm -f vela-e2e-pg vela-e2e-azurite >/dev/null 2>&1 || true; fi
echo stopped
