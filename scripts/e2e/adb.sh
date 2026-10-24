#!/usr/bin/env bash
# adb with the Android toolchain on PATH: scripts/e2e/adb.sh shell input keyevent 4
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
exec "$ROOT/scripts/with-env.sh" adb "$@"
