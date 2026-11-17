#!/usr/bin/env bash
# Gera executáveis autocontidos em ./release (padrão: Windows x64).
#   ./scripts/publish.sh [runtime]
set -euo pipefail
runtime="${1:-win-x64}"
cd "$(dirname "$0")/.."
common=(-c Release -r "$runtime" --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true)
dotnet publish src/MesaCerta.Desktop "${common[@]}" -o release/desktop
dotnet publish src/MesaCerta.Server "${common[@]}" -o release/server
cp .env.example release/server/.env.example
echo "Executáveis em release/desktop e release/server"
