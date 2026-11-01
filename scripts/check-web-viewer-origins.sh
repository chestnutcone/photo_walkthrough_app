#!/usr/bin/env bash
# Fails when the built web viewer (default app/build/web) can load a script
# from a third-party origin. The viewer is for anonymous visitors: it must
# contact only its own origin, the API and the image CDN (final review I-2).
#
#   flutter build web -t lib/main_web.dart --release --no-web-resources-cdn ...
#   scripts/check-web-viewer-origins.sh [build/web dir]
set -euo pipefail
dir="${1:-$(dirname "$0")/../app/build/web}"
[[ -f "$dir/main.dart.js" ]] || { echo "no main.dart.js in $dir (build the viewer first)" >&2; exit 2; }

fail=0
# Script/SDK hosts that a plugin could inject (Google Identity Services,
# Google APIs loader, Sign in with Apple JS, Sentry browser SDK, tag managers,
# public JS CDNs).
forbidden='accounts\.google\.com|apis\.google\.com|appleid\.cdn-apple\.com|sentry-cdn\.com|googletagmanager\.com|google-analytics\.com|cdn\.jsdelivr\.net|unpkg\.com|cdnjs\.cloudflare\.com'
if hits=$(grep -rlE "$forbidden" "$dir" --include='*.js' --include='*.html' --include='*.json'); then
  echo "FAIL: third-party script origin in the web viewer build:" >&2
  grep -roE "$forbidden" "$dir" --include='*.js' --include='*.html' --include='*.json' | sort | uniq -c >&2
  fail=1
fi
# index.html loads only same-origin scripts.
if grep -qiE '<script[^>]+src="(https?:)?//' "$dir/index.html"; then
  echo "FAIL: index.html loads a script from another origin" >&2
  fail=1
fi
# CanvasKit must come from the build itself, not www.gstatic.com.
if ! grep -q '"useLocalCanvasKit":true' "$dir/flutter_bootstrap.js"; then
  echo "FAIL: build without --no-web-resources-cdn (CanvasKit would load from gstatic.com)" >&2
  fail=1
fi
[[ $fail -eq 0 ]] && echo "OK: no third-party script origin in $dir"
exit $fail
