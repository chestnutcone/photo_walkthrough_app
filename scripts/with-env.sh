#!/usr/bin/env bash
# Runs a command with the dotnet/flutter/JDK/Android toolchains on PATH: scripts/with-env.sh flutter build apk
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck disable=SC1091
. "$ROOT/scripts/dev-env.sh"
# shellcheck disable=SC1091
. "$ROOT/scripts/android-env.sh"
# Optional python unzip shim for machines without unzip (kept outside the repo).
[ -d "$HOME/.local/shim" ] && export PATH="$PATH:$HOME/.local/shim"
exec "$@"
