#!/usr/bin/env bash
# run-test.sh <test file> : runs one integration test on the emulator, turning E2E_SHOT markers into screenshots.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
"$ROOT/scripts/with-env.sh" adb reverse tcp:10000 tcp:10000 >/dev/null 2>&1
# No ANR / crash dialogs over the app on a loaded emulator (the app's own crashes still fail the test).
"$ROOT/scripts/with-env.sh" adb shell settings put global hide_error_dialogs 1 >/dev/null 2>&1
cd "$ROOT/app"
"$ROOT/scripts/with-env.sh" flutter test "$1" -d "${ANDROID_SERIAL:-emulator-5554}" \
  --dart-define=API_BASE_URL=http://10.0.2.2:5080 --dart-define=VELA_FAKE_SSO=true "${@:2}" 2>&1 |
while IFS= read -r line; do
  case "$line" in
    *E2E_SHOT*)
      name="${line##*E2E_SHOT }"; name="${name//$'\r'/}"
      python3 "$ROOT/scripts/e2e/shot.py" "$ROOT/docs/review/screenshots/$name.png" 2>&1 | tail -1
      echo "shot: $name" ;;
    *) echo "$line" ;;
  esac
done | grep -v "^\[ *[+=]" 
exit "${PIPESTATUS[0]}"
