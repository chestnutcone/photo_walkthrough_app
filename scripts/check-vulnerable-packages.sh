#!/usr/bin/env bash
# Fails when any NuGet package (direct or transitive) of the API solution has a known High or Critical advisory.
# Low and Moderate advisories are listed but do not fail the build. Also fails when the advisory data could not be
# fetched (NU1900), because "no output" would otherwise read as "no vulnerabilities".
#
# Usage:  scripts/check-vulnerable-packages.sh [solution]        (default api/Vela.sln)
#         scripts/check-vulnerable-packages.sh --self-test       (checks the parser against sample output; needs no network)
# Env:    VULN_JSON=<file>  parse this `dotnet list package --format json` output instead of running dotnet.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FAIL_LEVELS="High Critical"

parse() { # $1 = json file; prints findings, exits 1 when a failing severity is present
  python3 - "$1" "$FAIL_LEVELS" <<'PY'
import json, sys
path, fail_levels = sys.argv[1], sys.argv[2].split()
with open(path) as f:
    doc = json.load(f)
failing, other = [], []

def walk(node, project=None, framework=None, kind=None):
    if isinstance(node, dict):
        if "vulnerabilities" in node and "id" in node:
            for v in node["vulnerabilities"]:
                row = f'{project or "?"} [{framework or "?"}] {node["id"]} {node.get("resolvedVersion", "?")} ' \
                      f'severity={v.get("severity", "?")} {v.get("advisoryurl", "")}'
                (failing if v.get("severity") in fail_levels else other).append(row)
        for key, value in node.items():
            walk(value,
                 node.get("path", project) if key == "projects" or "path" in node else project,
                 node.get("framework", framework), key)
    elif isinstance(node, list):
        for item in node:
            walk(item, item.get("path", project) if isinstance(item, dict) else project, framework, kind)

walk(doc)
for row in other:
    print("note (below failing severity):", row)
for row in failing:
    print("VULNERABLE:", row)
if failing:
    print(f"{len(failing)} package(s) with {'/'.join(fail_levels)} advisories.", file=sys.stderr)
    sys.exit(1)
print("No High or Critical vulnerable packages.")
PY
}

self_test() {
  local dir; dir="$(mktemp -d)"; trap 'rm -rf "$dir"' RETURN
  cat >"$dir/clean.json" <<'J'
{"version":1,"projects":[{"path":"/a/Api.csproj"}]}
J
  cat >"$dir/moderate.json" <<'J'
{"version":1,"projects":[{"path":"/a/Api.csproj","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"Foo","requestedVersion":"1.0.0","resolvedVersion":"1.0.0","vulnerabilities":[{"severity":"Moderate","advisoryurl":"https://github.com/advisories/GHSA-x"}]}]}]}]}
J
  cat >"$dir/high-transitive.json" <<'J'
{"version":1,"projects":[{"path":"/a/Api.csproj","frameworks":[{"framework":"net10.0","transitivePackages":[{"id":"Bar","resolvedVersion":"2.0.0","vulnerabilities":[{"severity":"High","advisoryurl":"https://github.com/advisories/GHSA-y"}]}]}]}]}
J
  cat >"$dir/critical.json" <<'J'
{"version":1,"projects":[{"path":"/a/Api.csproj","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"Baz","resolvedVersion":"3.0.0","vulnerabilities":[{"severity":"Low","advisoryurl":"u"},{"severity":"Critical","advisoryurl":"https://github.com/advisories/GHSA-z"}]}]}]}]}
J
  parse "$dir/clean.json" >/dev/null || { echo "self-test: clean output must pass" >&2; return 1; }
  parse "$dir/moderate.json" >/dev/null || { echo "self-test: Moderate must not fail" >&2; return 1; }
  if parse "$dir/high-transitive.json" >/dev/null 2>&1; then echo "self-test: transitive High must fail" >&2; return 1; fi
  if parse "$dir/critical.json" >/dev/null 2>&1; then echo "self-test: Critical must fail" >&2; return 1; fi
  echo "self-test passed"
}

if [ "${1:-}" = "--self-test" ]; then self_test; exit $?; fi

SOLUTION="${1:-$ROOT/api/Vela.sln}"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT

if [ -n "${VULN_JSON:-}" ]; then
  cp "$VULN_JSON" "$tmp/out.json"
else
  # Restore first so the advisory check sees the real graph; --format json avoids parsing the human table.
  dotnet restore "$SOLUTION" >/dev/null
  dotnet list "$SOLUTION" package --vulnerable --include-transitive --format json >"$tmp/out.json" 2>"$tmp/err.log" || {
    cat "$tmp/err.log" >&2; echo "FAIL: dotnet list package failed" >&2; exit 2; }
  if grep -q "NU1900" "$tmp/err.log" "$tmp/out.json" 2>/dev/null; then
    cat "$tmp/err.log" >&2; echo "FAIL: vulnerability data could not be fetched (NU1900); refusing to report a clean result" >&2; exit 2
  fi
fi
parse "$tmp/out.json"
