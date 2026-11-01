#!/bin/zsh
# Packs build/Linework.app into build/Linework-<version>.dmg with an Applications shortcut and a note.
# Notarization, once there's a Developer ID, goes after hdiutil here.
set -euo pipefail
cd "${0:A:h}/.."

scripts/build-app.sh > /dev/null
VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" Resources/Info.plist)
DMG="build/Linework-$VERSION.dmg"
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

cp -R build/Linework.app "$STAGE/"
ln -s /Applications "$STAGE/Applications"
cp "Resources/DMG/How to open Linework.txt" "$STAGE/"
rm -f "$DMG"
hdiutil create -quiet -volname "Linework" -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$DMG"
if [[ -n "${LINEWORK_SIGN_IDENTITY:-}" ]]; then
    codesign --force --sign "$LINEWORK_SIGN_IDENTITY" "$DMG"
fi
echo "$PWD/$DMG"
