#!/usr/bin/env bash
# airplane.sh on|off: toggles airplane mode on the emulator (the emulator keeps adb, loses the data network).
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
if [ "$1" = on ]; then
  "$ROOT/scripts/e2e/adb.sh" shell cmd connectivity airplane-mode enable
else
  "$ROOT/scripts/e2e/adb.sh" shell cmd connectivity airplane-mode disable
fi
