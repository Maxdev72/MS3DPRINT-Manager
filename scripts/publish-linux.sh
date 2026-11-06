#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p outputs/linux-compatibility
# Include native libraries in the bundle; keep symbols in build directories.
dotnet publish src/MS3DPRINT.Manager.Linux/MS3DPRINT.Manager.Linux.csproj \
  -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
  -o outputs/linux-x64 '-bl:outputs/linux-compatibility/publish-{}.binlog'
chmod +x 'outputs/linux-x64/MS3DPRINT Manager'
cp docs/linux.md outputs/linux-x64/LISEZ-MOI.md
# Select files explicitly: do not package stale symbols or unrelated output files.
tar -czf outputs/MS3DPRINT-Manager-linux-x64.tar.gz -C outputs/linux-x64 'MS3DPRINT Manager' LISEZ-MOI.md
sha256sum outputs/MS3DPRINT-Manager-linux-x64.tar.gz > outputs/MS3DPRINT-Manager-linux-x64.sha256
