#!/usr/bin/env bash
#
# Regenerate build/macos/Luma.icns from build/macos/icon.svg.
# Only needed when the brand mark changes; the resulting .icns is committed so
# build-macos.sh works without rsvg-convert installed.
#
# Requires: rsvg-convert (brew install librsvg) and iconutil (Xcode CLT).
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/build/macos"
SVG="$DIR/icon.svg"
ICONSET="$(mktemp -d)/Luma.iconset"
mkdir -p "$ICONSET"

render() { rsvg-convert -w "$1" -h "$1" "$SVG" -o "$ICONSET/$2"; }
render 16   icon_16x16.png
render 32   icon_16x16@2x.png
render 32   icon_32x32.png
render 64   icon_32x32@2x.png
render 128  icon_128x128.png
render 256  icon_128x128@2x.png
render 256  icon_256x256.png
render 512  icon_256x256@2x.png
render 512  icon_512x512.png
render 1024 icon_512x512@2x.png

iconutil -c icns "$ICONSET" -o "$DIR/Luma.icns"
echo "Wrote $DIR/Luma.icns"
