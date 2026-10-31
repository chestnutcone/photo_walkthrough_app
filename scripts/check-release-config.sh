#!/usr/bin/env bash
# Pre-release check for the iOS build (Android has the same check built into
# app/android/app/build.gradle.kts and fails assembleRelease/bundleRelease).
# Run before `flutter build ipa` with the values you pass as dart-defines:
#
#   API_BASE_URL=https://api.<domain> LINKS_DOMAIN=<links domain> \
#   APP_STORE_URL=... PLAY_STORE_URL=... scripts/check-release-config.sh
#
# Fails when a value is missing, not https, or still the vela.example
# placeholder, or when Runner.entitlements disagrees with LINKS_DOMAIN.
set -euo pipefail
app="$(cd "$(dirname "$0")/../app" && pwd)"
placeholder='vela.example'
fail=0
bad() { echo "FAIL: $*" >&2; fail=1; }

[[ "${API_BASE_URL:-}" == https://* ]] || bad "API_BASE_URL must be an https:// URL"
[[ "${API_BASE_URL:-}" == *"$placeholder"* ]] && bad "API_BASE_URL holds $placeholder"
[[ -n "${LINKS_DOMAIN:-}" ]] || bad "LINKS_DOMAIN is not set"
[[ "${LINKS_DOMAIN:-}" == *"$placeholder"* ]] && bad "LINKS_DOMAIN holds $placeholder"
for v in APP_STORE_URL PLAY_STORE_URL; do
  val="${!v:-}"
  [[ -z "$val" || "$val" == https://* ]] || bad "$v must be empty or https://"
  [[ "$val" == *"$placeholder"* ]] && bad "$v holds $placeholder"
done

ent="$app/ios/Runner/Runner.entitlements"
grep -q "applinks:$placeholder<" "$ent" && bad "Runner.entitlements still has applinks:$placeholder"
if [[ -n "${LINKS_DOMAIN:-}" ]] && ! grep -q "applinks:${LINKS_DOMAIN}<" "$ent"; then
  bad "Runner.entitlements has no applinks:${LINKS_DOMAIN}"
fi
grep -q 'com.apple.developer.applesignin' "$ent" || bad "Runner.entitlements lacks Sign in with Apple"

[[ $fail -eq 0 ]] && echo "OK: release configuration has no placeholder"
exit $fail
