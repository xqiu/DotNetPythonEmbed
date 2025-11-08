#!/usr/bin/env bash
set -euo pipefail

if [[ $# -gt 1 ]]; then
  echo "Usage: $0 [version]" >&2
  exit 1
fi

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/.." && pwd)
PROJECT="$REPO_ROOT/src/DotNetPythonEmbed/DotNetPythonEmbed.csproj"
OUTPUT_DIR="$REPO_ROOT/artifacts"
VERSION_ARG=${1:-}

if [[ -z "${NUGET_API_KEY:-}" ]]; then
  echo "NUGET_API_KEY environment variable must be set" >&2
  exit 1
fi

PACK_ARGS=("dotnet" "pack" "$PROJECT" "--configuration" "Release" "--output" "$OUTPUT_DIR" "--no-build")
if [[ -n "$VERSION_ARG" ]]; then
  PACK_ARGS+=("/p:PackageVersion=$VERSION_ARG")
fi

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

if ! dotnet restore "$REPO_ROOT/DotNetPythonEmbed.sln"; then
  echo "dotnet restore failed" >&2
  exit 1
fi

dotnet build "$REPO_ROOT/DotNetPythonEmbed.sln" --configuration Release --no-restore
"${PACK_ARGS[@]}"

PACKAGE_PATH=$(find "$OUTPUT_DIR" -maxdepth 1 -name 'DotNetPythonEmbed*.nupkg' | sort | tail -n 1)
if [[ -z "$PACKAGE_PATH" ]]; then
  echo "Failed to locate packed nupkg in $OUTPUT_DIR" >&2
  exit 1
fi

dotnet nuget push "$PACKAGE_PATH" \
  --source "https://api.nuget.org/v3/index.json" \
  --api-key "$NUGET_API_KEY" \
  --skip-duplicate

echo "Published $PACKAGE_PATH to NuGet"
