#!/bin/zsh
# Builds a release binary and wraps it into build/Linework.app.
# Signs with LINEWORK_SIGN_IDENTITY if set, for a Developer ID later on, and ad-hoc otherwise.
set -euo pipefail
cd "${0:A:h}/.."

VERSION=$(sed -n 's/.*current = "\(.*\)".*/\1/p' Sources/LineworkCore/LineworkVersion.swift)
PLIST_VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" Resources/Info.plist)
if [[ "$VERSION" != "$PLIST_VERSION" ]]; then
    echo "Version mismatch: LineworkVersion says $VERSION, Info.plist says $PLIST_VERSION" >&2
    exit 1
fi

swift build -c release --arch arm64
BIN="$(swift build -c release --arch arm64 --show-bin-path)/Linework"
APP="build/Linework.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Linework"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/"
cp -R Resources/Fonts "$APP/Contents/Resources/Fonts"
cp LICENSES/InterTight-OFL.txt "$APP/Contents/Resources/Fonts/OFL.txt"

if [[ -n "${LINEWORK_SIGN_IDENTITY:-}" ]]; then
    codesign --force --options runtime --timestamp --sign "$LINEWORK_SIGN_IDENTITY" "$APP"
else
    codesign --force --sign - "$APP"
fi
echo "$PWD/$APP"
