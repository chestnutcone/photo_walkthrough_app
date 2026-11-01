#!/usr/bin/env bash
# Stops the local API and removes the Postgres/Azurite containers (data is discarded; pass --keep to keep them).
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -f "$ROOT/.local/api.pid" ]; then
  pid="$(cat "$ROOT/.local/api.pid")"
  pkill -P "$pid" 2>/dev/null || true
  kill "$pid" 2>/dev/null || true
  rm -f "$ROOT/.local/api.pid"
fi
if [ "${1:-}" != "--keep" ]; then docker rm -f vela-e2e-pg vela-e2e-azurite >/dev/null 2>&1 || true; fi
echo stopped
