#!/usr/bin/env bash
# Build a local, double-clickable OpenTabletArtist.app for macOS — NO signing/notarization (those are only
# needed to *distribute* to other machines; a locally-built .app isn't quarantined, so Gatekeeper allows it).
#
# This is a dev convenience, not the shipping packaging pipeline (that's the deferred V2 milestone).
# Output: dist/OpenTabletArtist.app  →  drag to /Applications, or `open dist/OpenTabletArtist.app`.
#
# The pen needs a running OTD daemon with Input Monitoring — easiest is `open -a OpenTabletDriver` first,
# so OTA connects to OTD.app's already-granted daemon (the card will read "External daemon", which is fine).
set -euo pipefail

cd "$(dirname "$0")/.."
: "${DOTNET_ROOT:=$HOME/.dotnet}"; export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

RID="osx-$([ "$(uname -m)" = "arm64" ] && echo arm64 || echo x64)"
APP="dist/OpenTabletArtist.app"
PUB="dist/.publish-$RID"
VERSION="$(grep -m1 -oE '<Version>[^<]+' OpenTabletArtist/OpenTabletArtist.csproj | sed 's/<Version>//' || true)"
VERSION="${VERSION:-0.0.0}"

echo "==> Publishing self-contained ($RID)…"
rm -rf "$PUB"
dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r "$RID" \
  --self-contained true -p:PublishSingleFile=false -o "$PUB"

echo "==> Assembling $APP…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/OpenTabletArtist"

echo "==> Building icon…"
ICONSET="$(mktemp -d)/OpenTabletArtist.iconset"; mkdir -p "$ICONSET"
SRC="OpenTabletArtist/Assets/appicon.png"
for s in 16 32 128 256 512; do
  sips -z $s $s        "$SRC" --out "$ICONSET/icon_${s}x${s}.png"    >/dev/null
  sips -z $((s*2)) $((s*2)) "$SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>OpenTabletArtist</string>
  <key>CFBundleDisplayName</key><string>OpenTabletArtist</string>
  <key>CFBundleIdentifier</key><string>com.thesevenpens.opentabletartist</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleExecutable</key><string>OpenTabletArtist</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.graphics-design</string>
</dict>
</plist>
PLIST

echo "==> Done: $APP"
echo "    Run:  open $APP     (start 'open -a OpenTabletDriver' first so the pen works)"
