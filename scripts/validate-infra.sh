#!/usr/bin/env bash
# Offline validation of the Bicep templates. Needs no Azure login and deploys nothing.
# Fails (non-zero) when:
#   1. a Bicep build fails, or lint reports an error,
#   2. a parameter file holds a literal value under a password/secret/key-like name,
#   3. a secret-like Bicep parameter is missing @secure(),
#   3b. prod + deployApp has no Cloudflare allow-list, or the budget start date is not fixed,
#   4. a setting listed in infra/required-settings.txt is not wired into infra/modules/containerapp.bicep.
# Usage: scripts/validate-infra.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INFRA="$ROOT/infra"
fail=0
err() { echo "FAIL: $*" >&2; fail=1; }

# Prefer a standalone `bicep`, fall back to `az bicep`.
if command -v bicep >/dev/null 2>&1; then
  bicep_cmd=(bicep)
elif [ -x "$HOME/.azure/bin/bicep" ]; then
  bicep_cmd=("$HOME/.azure/bin/bicep")
elif command -v az >/dev/null 2>&1; then
  bicep_cmd=(az bicep)
else
  echo "FAIL: neither 'bicep' nor 'az' is installed" >&2
  exit 1
fi

if [ ! -f "$INFRA/main.bicep" ]; then
  err "infra/main.bicep is missing"
  exit 1
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# --- 1. build + lint (every template, so a broken module cannot hide behind an unused reference) ---
while IFS= read -r f; do
  rel="${f#"$ROOT"/}"
  if [ "${bicep_cmd[0]}" = "az" ]; then
    build=(az bicep build --file "$f" --outfile "$tmp/out.json")
    lint=(az bicep lint --file "$f")
  else
    build=("${bicep_cmd[@]}" build "$f" --outfile "$tmp/out.json")
    lint=("${bicep_cmd[@]}" lint "$f")
  fi
  if ! "${build[@]}" 2>"$tmp/build.log"; then
    cat "$tmp/build.log" >&2
    err "bicep build failed: $rel"
    continue
  fi
  if ! "${lint[@]}" >"$tmp/lint.log" 2>&1; then
    cat "$tmp/lint.log" >&2
    err "bicep lint reported errors: $rel"
  elif grep -q ' Warning ' "$tmp/lint.log"; then
    cat "$tmp/lint.log" >&2
    err "bicep lint reported warnings: $rel (fix them or configure the rule in infra/bicepconfig.json with a reason)"
  fi
done < <(find "$INFRA" -name '*.bicep' | sort)

# --- 2. parameter files: no literal secret-like values ---
python3 -I - "$INFRA" <<'PY' || fail=1
import glob, json, re, sys
infra = sys.argv[1]
pat = re.compile(r"password|secret|key", re.I)
bad = []
files = sorted(glob.glob(infra + "/parameters*.json"))
if not files:
    print("FAIL: no infra/parameters*.json found", file=sys.stderr); sys.exit(1)
for path in files:
    doc = json.load(open(path))
    for name, spec in (doc.get("parameters") or {}).items():
        if not pat.search(name): continue
        if isinstance(spec, dict) and "reference" in spec:  # Key Vault reference, not a literal
            continue
        value = spec.get("value") if isinstance(spec, dict) else spec
        if isinstance(value, str) and value.strip() != "":
            bad.append(f"{path}: parameter '{name}' has a literal value")
        elif isinstance(value, (dict, list)) and value:
            bad.append(f"{path}: parameter '{name}' has a literal value")
if bad:
    for b in bad: print("FAIL: " + b, file=sys.stderr)
    sys.exit(1)
PY

# --- 3. secret-like Bicep params must be @secure() ---
python3 -I - "$INFRA" <<'PY' || fail=1
import glob, re, sys
pat = re.compile(r"password|secret|key|token|connectionstring|dsn", re.I)
bad = []
for path in sorted(glob.glob(sys.argv[1] + "/**/*.bicep", recursive=True)):
    lines = open(path).read().splitlines()
    for i, line in enumerate(lines):
        m = re.match(r"\s*param\s+(\w+)\s+(\w+)", line)
        if not m or not pat.search(m.group(1)): continue
        if m.group(2) == "bool" or m.group(1).endswith(("Name", "Url", "Uri", "Enabled")): continue
        before = "\n".join(lines[max(0, i - 4):i])
        if "@secure()" not in before:
            bad.append(f"{path}:{i + 1}: param '{m.group(1)}' looks secret but is not @secure()")
if bad:
    for b in bad: print("FAIL: " + b, file=sys.stderr)
    sys.exit(1)
PY

# --- 3b. prod must restrict the origin to Cloudflare once the app is deployed ---
python3 -I - "$INFRA" <<'PY' || fail=1
import glob, json, sys
bad = []
for path in sorted(glob.glob(sys.argv[1] + "/parameters*.json")):
    p = (json.load(open(path)).get("parameters") or {})
    val = lambda k, d=None: (p.get(k) or {}).get("value", d)
    if val("environment") == "prod" and val("deployApp", False) and not val("cloudflareIpv4Ranges"):
        bad.append(f"{path}: prod with deployApp=true needs a non-empty cloudflareIpv4Ranges (origin must only accept Cloudflare)")
    start = val("budgetStartDate")
    if start is None or not __import__("re").fullmatch(r"\d{4}-\d{2}-01", str(start)):
        bad.append(f"{path}: budgetStartDate must be set to the first of a month (yyyy-MM-01)")
if bad:
    for b in bad: print("FAIL: " + b, file=sys.stderr)
    sys.exit(1)
PY

# --- 4. every setting the API needs in Production is wired into the container app ---
list="$INFRA/required-settings.txt"
module="$INFRA/modules/containerapp.bicep"
if [ ! -f "$list" ]; then
  err "infra/required-settings.txt is missing"
elif [ ! -f "$module" ]; then
  err "infra/modules/containerapp.bicep is missing"
else
  while IFS= read -r key; do
    case "$key" in ''|\#*) continue ;; esac
    if ! grep -qF "name: '$key" "$module"; then
      err "required API setting '$key' is not set in infra/modules/containerapp.bicep (env or secret)"
    fi
  done < "$list"
fi

if [ "$fail" -ne 0 ]; then
  echo "validate-infra: FAILED" >&2
  exit 1
fi
echo "validate-infra: OK"
