#!/usr/bin/env bash
# End-to-end run: local API (Docker Postgres + Azurite), host helper, emulator, seed data, then every
# integration test under app/integration_test on the emulator. Screenshots land in docs/review/screenshots/<flow>/.
# Usage: scripts/run-e2e.sh [test-file ...]   (default: all *_test.dart). See docs/dev/run-e2e.md.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENVRUN="$ROOT/scripts/with-env.sh"
LOGS="$ROOT/.local/e2e-logs"; mkdir -p "$LOGS"

"$ROOT/scripts/run-local-api.sh" || exit 1

if ! curl -fs http://127.0.0.1:5090/health >/dev/null 2>&1; then
  nohup python3 "$ROOT/tools/e2e_host_helper.py" 5090 >"$ROOT/.local/helper.log" 2>&1 &
  sleep 1
fi
python3 "$ROOT/tools/seed_local.py" || echo "seed skipped/failed (continuing)"

if ! "$ENVRUN" adb devices | grep -q "emulator-5554.*device"; then
  echo "starting emulator (headless)..."
  nohup "$ENVRUN" emulator -avd "${VELA_AVD:-vela_test}" -no-window -no-audio -gpu swiftshader_indirect -no-snapshot >/tmp/emulator.log 2>&1 &
  for _ in $(seq 1 90); do
    [ "$("$ENVRUN" adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] && break
    sleep 5
  done
fi
"$ENVRUN" adb reverse tcp:10000 tcp:10000 >/dev/null   # Azurite image URLs point at 127.0.0.1:10000

if [ $# -gt 0 ]; then files=("$@"); else files=("$ROOT"/app/integration_test/*_test.dart); fi
fail=0
for f in "${files[@]}"; do
  name="$(basename "$f" .dart)"
  echo "=== $name"
  "$ROOT/scripts/e2e/run-test.sh" "$f" 2>&1 | tee "$LOGS/$name.log" | grep -E "All tests passed|Some tests failed|Timed out|A11Y_SUMMARY" || true
  grep -q "All tests passed" "$LOGS/$name.log" || fail=1
done
grep -h "A11Y_FAIL" "$LOGS"/*.log >"$ROOT/docs/review/a11y-violations.txt" 2>/dev/null || true
[ $fail = 0 ] && echo "E2E: all test files passed" || echo "E2E: FAILURES (logs in .local/e2e-logs)"
exit $fail
