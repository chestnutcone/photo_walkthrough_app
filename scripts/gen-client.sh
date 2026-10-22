#!/usr/bin/env bash
# Regenerates the Dart client from contract/openapi.yaml. Never hand-edit the output.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GENERATOR_IMAGE="openapitools/openapi-generator-cli:v7.14.0"
OUT="app/packages/vela_api_client"

rm -rf "${ROOT:?}/${OUT}"
docker run --rm --user "$(id -u):$(id -g)" -v "${ROOT}:/local" "${GENERATOR_IMAGE}" generate \
  -i /local/contract/openapi.yaml \
  -g dart-dio \
  -o "/local/${OUT}" \
  --additional-properties=pubName=vela_api_client,pubVersion=1.0.0,pubAuthor=Vela,nullSafe=true,serializationLibrary=json_serializable

# The generated toJson uses null-aware elements, which need Dart >= 3.8 (template default is 3.5).
sed -i "s|sdk: '>=3.5.0 <4.0.0'|sdk: '>=3.8.0 <4.0.0'|" "${ROOT}/${OUT}/pubspec.yaml"

# dart-dio emits json_serializable sources; build the *.g.dart files so the package is usable as-is.
(cd "${ROOT}/${OUT}" && dart pub get && dart run build_runner build --delete-conflicting-outputs)
