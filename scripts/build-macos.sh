#!/usr/bin/env bash
#
# Build Luma as a distributable macOS .app bundle: publish, assemble, code-sign,
# and (optionally) notarize + staple.
#
# Signing identity is auto-detected unless --identity is given:
#   1. a "Developer ID Application" cert  (proper distribution — notarizable)
#   2. otherwise the first available code-signing identity (e.g. a self-signed dev cert)
#   3. otherwise ad-hoc ("-")             (runs locally; macOS re-prompts for permissions)
#
# Notarization only runs with --notarize <profile> AND a Developer ID identity; a
# hardened runtime + entitlements are always applied so the same bundle is notarizable.
#
# Usage:
#   scripts/build-macos.sh [options]
#     --arch <arm64|x64>             default: arm64 (build x64 as a separate artifact for Intel)
#     --config <Debug|Release>       default: Release
#     --identity <name>              codesign identity; default: auto-detect
#     --notarize <keychain-profile>  notarytool profile (see `notarytool store-credentials`)
#     --dmg                          also produce a .dmg next to the .app
#     --version <x.y.z>              CFBundleShortVersionString; default: git describe or 1.0.0
#     --nuget-config <path>          passed to `dotnet` as --configfile
#     --output <dir>                 default: publish
set -euo pipefail

# --- project constants ---------------------------------------------------------
APP_NAME="Luma"
BUNDLE_ID="consulting.cranmore.luma"
PROJECT="src/Luma.App"
MIN_MACOS="11.0"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"
ENTITLEMENTS="$REPO_ROOT/build/macos/Entitlements.plist"

# --- defaults ------------------------------------------------------------------
ARCH="arm64"
CONFIG="Release"
IDENTITY=""
NOTARIZE_PROFILE=""
MAKE_DMG=0
VERSION=""
NUGET_CONFIG=""
OUTPUT="publish"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --arch) ARCH="$2"; shift 2 ;;
    --config) CONFIG="$2"; shift 2 ;;
    --identity) IDENTITY="$2"; shift 2 ;;
    --notarize) NOTARIZE_PROFILE="$2"; shift 2 ;;
    --dmg) MAKE_DMG=1; shift ;;
    --version) VERSION="$2"; shift 2 ;;
    --nuget-config) NUGET_CONFIG="$2"; shift 2 ;;
    --output) OUTPUT="$2"; shift 2 ;;
    -h|--help) sed -n '3,23p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

case "$ARCH" in
  arm64) RID=osx-arm64 ;;
  x64)   RID=osx-x64 ;;
  *) echo "Invalid --arch '$ARCH' (want arm64|x64)" >&2; exit 2 ;;
esac

if [[ -z "$VERSION" ]]; then
  VERSION="$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)"
  VERSION="${VERSION:-1.0.0}"
fi

APP="$OUTPUT/$APP_NAME.app"
CONTENTS="$APP/Contents"
MACOS_DIR="$CONTENTS/MacOS"

log() { printf '\033[1;36m▸ %s\033[0m\n' "$*"; }

# --- 1. publish ----------------------------------------------------------------
publish_args=(-c "$CONFIG" --self-contained -p:UseAppHost=true)
[[ -n "$NUGET_CONFIG" ]] && publish_args+=(--configfile "$NUGET_CONFIG")

log "Publishing $RID ($CONFIG)"
dotnet publish "$PROJECT" -r "$RID" "${publish_args[@]}" -o "$OUTPUT/$RID" >/dev/null

# --- 2. assemble bundle --------------------------------------------------------
log "Assembling $APP (v$VERSION, $ARCH)"
rm -rf "$APP"
mkdir -p "$MACOS_DIR" "$CONTENTS/Resources"
cp -R "$OUTPUT/$RID/." "$MACOS_DIR/"
chmod +x "$MACOS_DIR/$APP_NAME"

cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>               <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>        <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>         <string>$BUNDLE_ID</string>
  <key>CFBundleExecutable</key>         <string>$APP_NAME</string>
  <key>CFBundlePackageType</key>        <string>APPL</string>
  <key>CFBundleShortVersionString</key> <string>$VERSION</string>
  <key>CFBundleVersion</key>            <string>$VERSION</string>
  <key>LSMinimumSystemVersion</key>     <string>$MIN_MACOS</string>
  <key>NSHighResolutionCapable</key>    <true/>
  <key>LSApplicationCategoryType</key>  <string>public.app-category.developer-tools</string>
</dict>
</plist>
PLIST
echo "APPL????" > "$CONTENTS/PkgInfo"

# --- 3. resolve signing identity ----------------------------------------------
if [[ -z "$IDENTITY" ]]; then
  identities="$(security find-identity -v -p codesigning 2>/dev/null || true)"
  IDENTITY="$(printf '%s\n' "$identities" | grep -o '"Developer ID Application[^"]*"' | head -1 | tr -d '"' || true)"
  [[ -z "$IDENTITY" ]] && IDENTITY="$(printf '%s\n' "$identities" | sed -n '1s/.*"\(.*\)".*/\1/p' || true)"
  [[ -z "$IDENTITY" ]] && IDENTITY="-"
fi
if [[ "$IDENTITY" == "-" ]]; then
  log "Signing ad-hoc (no identity found) — runs locally, not distributable"
else
  log "Signing as: $IDENTITY"
fi

# --- 4. codesign inside-out ----------------------------------------------------
# Apple: never sign a bundle with --deep. Sign nested code first, bundle last.
# A secure timestamp needs Apple's network service and only matters for notarized
# distribution, so skip it unless signing with a Developer ID.
TS="--timestamp=none"
[[ "$IDENTITY" == Developer\ ID\ Application* ]] && TS="--timestamp"
sign() { codesign --force $TS --options runtime --entitlements "$ENTITLEMENTS" --sign "$IDENTITY" "$@"; }

# Every file in Contents/MacOS must be signed, not just Mach-O: codesign treats the
# flat managed .NET assemblies (.dll) as code subcomponents and refuses to seal the
# bundle while any is unsigned. Sign deepest paths first, main executable and bundle last.
log "Code-signing ($(find "$MACOS_DIR" -type f | wc -l | tr -d ' ') files)"
find "$MACOS_DIR" -type f ! -path "$MACOS_DIR/$APP_NAME" \
  | awk '{ print gsub(/\//,"/"), $0 }' | sort -rn | cut -d' ' -f2- \
  | while IFS= read -r f; do sign "$f"; done
sign "$MACOS_DIR/$APP_NAME"
sign "$APP"

log "Verifying signature"
codesign --verify --deep --strict --verbose=2 "$APP"

# --- 5. notarize (optional) ----------------------------------------------------
if [[ -n "$NOTARIZE_PROFILE" ]]; then
  if [[ "$IDENTITY" != Developer\ ID\ Application* ]]; then
    echo "⚠︎  --notarize needs a Developer ID Application identity; skipping notarization." >&2
  else
    ZIP="$OUTPUT/$APP_NAME.zip"
    log "Submitting to notary service (profile: $NOTARIZE_PROFILE)"
    ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
    xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARIZE_PROFILE" --wait
    xcrun stapler staple "$APP"
    rm -f "$ZIP"
  fi
fi

# --- 6. dmg (optional) ---------------------------------------------------------
if [[ "$MAKE_DMG" == "1" ]]; then
  DMG="$OUTPUT/$APP_NAME-$VERSION.dmg"
  log "Building $DMG"
  rm -f "$DMG"
  hdiutil create -volname "$APP_NAME" -srcfolder "$APP" -ov -format UDZO "$DMG" >/dev/null
  [[ "$IDENTITY" != "-" ]] && codesign --force --sign "$IDENTITY" "$DMG"
  if [[ -n "$NOTARIZE_PROFILE" && "$IDENTITY" == Developer\ ID\ Application* ]]; then
    xcrun notarytool submit "$DMG" --keychain-profile "$NOTARIZE_PROFILE" --wait
    xcrun stapler staple "$DMG"
  fi
fi

log "Done → $APP"
