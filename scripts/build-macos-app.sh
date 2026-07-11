#!/usr/bin/env bash
# Convenience wrapper: publish OTA self-contained for this Mac's architecture. The BundleMacApp MSBuild
# target (in OpenTabletArtist.csproj) then assembles dist/OpenTabletArtist.app automatically.
#
# Equivalent to just running:  dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release \
#                                  -r osx-<arch> --self-contained
#
# The .app is unsigned (fine to run locally; signing/notarization is the deferred V2 milestone). For the pen
# to work, start OTD.app's daemon first (`open -a OpenTabletDriver`) so OTA connects to a permission-granted
# daemon — then `open dist/OpenTabletArtist.app`.
set -euo pipefail

cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:=$HOME/.dotnet}"; export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

RID="osx-$([ "$(uname -m)" = "arm64" ] && echo arm64 || echo x64)"
dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r "$RID" --self-contained true -p:PublishSingleFile=false

echo "==> Done → dist/OpenTabletArtist.app"
echo "    Run:  open -a OpenTabletDriver && open dist/OpenTabletArtist.app"
