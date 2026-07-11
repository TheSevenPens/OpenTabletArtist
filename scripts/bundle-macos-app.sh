#!/usr/bin/env bash
# Assemble a macOS .app bundle from an already-published, self-contained output directory.
# Called by the BundleMacApp MSBuild target after `dotnet publish` for an osx-* RID (also usable standalone).
# NOT signed/notarized — that's only needed to distribute; a locally-built bundle runs fine (V2 = shipping).
#
# Usage: bundle-macos-app.sh <publishDir> <appBundlePath> [version]
set -euo pipefail

PUBLISH_DIR="${1:?publish dir required}"
APP="${2:?app bundle path required}"
VERSION="${3:-0.0.0}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ICON_SRC="$HERE/../OpenTabletArtist/Assets/appicon.png"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH_DIR"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/OpenTabletArtist"

# appicon.png -> a proper multi-size AppIcon.icns
ICONSET="$(mktemp -d)/OpenTabletArtist.iconset"; mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z "$s" "$s"             "$ICON_SRC" --out "$ICONSET/icon_${s}x${s}.png"    >/dev/null
  sips -z "$((s*2))" "$((s*2))" "$ICON_SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
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

echo "Bundled $APP (v${VERSION})"
