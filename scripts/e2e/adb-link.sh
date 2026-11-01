#!/usr/bin/env bash
# adb-link.sh <uri> <out.png> [wait-seconds]: force-stops the app, opens a link in it, waits, screenshots.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ADB="$ROOT/scripts/with-env.sh adb"
$ADB shell am force-stop dev.vela.app
$ADB shell am start -a android.intent.action.VIEW -d "$1" dev.vela.app >/dev/null
sleep "${3:-25}"
python3 "$ROOT/scripts/e2e/shot.py" "$2"
