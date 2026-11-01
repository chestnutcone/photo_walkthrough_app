#!/usr/bin/env bash
# Source this file: puts the Linux dotnet and flutter toolchains first on PATH.
export DOTNET_ROOT="$HOME/.dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export FLUTTER_SUPPRESS_ANALYTICS=true
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/development/flutter/bin:$PATH"
