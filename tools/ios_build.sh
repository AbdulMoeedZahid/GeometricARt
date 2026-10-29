#!/usr/bin/env bash
# Build Geometric ARt for a physical iPhone: Unity -> Xcode project -> signed .app -> install -> launch.
# Usage:  GEOART_TEAM_ID=XXXXXXXXXX tools/ios_build.sh [--no-install] [--sandbox] [--skip-unity]
#   --skip-unity  reuse the last exported Xcode project (e.g. after only signing failed)
# Run it from Terminal on the Mac itself: code signing needs the unlocked login keychain, which SSH sessions lack.
# See docs/BUILD_IOS.md for one-time setup and every environment variable.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
INSTALL=1
SKIP_UNITY=0
SCENE="${GEOART_SCENE:-Assets/Scenes/Main.unity}"
for arg in "$@"; do
  case "$arg" in
    --no-install) INSTALL=0 ;;
    --sandbox) SCENE="Assets/Scenes/Sandbox/Sandbox_A_AR.unity" ;;
    --skip-unity) SKIP_UNITY=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

TEAM="${GEOART_TEAM_ID:?Set GEOART_TEAM_ID to your Apple Team ID (docs/BUILD_IOS.md, step 3)}"
BUNDLE="${GEOART_BUNDLE_ID:-edu.usf.cap4111.geometricart}"
OUT="${GEOART_IOS_OUT:-$HOME/GeoARtBuilds/ios}"          # outside the repo and without spaces
BUILD_NUMBER="${GEOART_BUILD_NUMBER:-$(date +%y%m%d%H%M)}"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"
LOG_DIR="$(dirname "$OUT")/logs"
mkdir -p "$OUT" "$LOG_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"

if [ "$SKIP_UNITY" = 1 ]; then
  [ -d "$OUT/Unity-iPhone.xcodeproj" ] || { echo "No exported project in $OUT; run without --skip-unity." >&2; exit 1; }
  echo "==> [1/4] Unity: skipped, reusing $OUT"
else
[ -x "$UNITY" ] || { echo "Unity $VERSION not found at $UNITY (set UNITY=...)" >&2; exit 1; }
if pgrep -f "Unity.app/Contents/MacOS/Unity.*-projectpath.*$(basename "$ROOT")" >/dev/null 2>&1; then
  echo "Close the Unity Editor for this project first (batch mode cannot open a project that is already open)." >&2; exit 1
fi

echo "==> [1/4] Unity: export Xcode project ($SCENE, Unity $VERSION)"
GEOART_IOS_OUT="$OUT" GEOART_SCENE="$SCENE" GEOART_BUNDLE_ID="$BUNDLE" \
  "$UNITY" -batchmode -nographics -projectPath "$ROOT" -buildTarget iOS \
  -executeMethod GeoART.EditorTools.IOSBuild.BuildBatch -logFile "$LOG_DIR/unity-$STAMP.log" \
  || { echo "Unity export failed; see $LOG_DIR/unity-$STAMP.log" >&2; tail -40 "$LOG_DIR/unity-$STAMP.log" >&2; exit 1; }
fi

echo "==> [2/4] Xcode: build and sign (team $TEAM, bundle $BUNDLE, build $BUILD_NUMBER)"
xcodebuild -project "$OUT/Unity-iPhone.xcodeproj" -scheme Unity-iPhone -configuration Release \
  -destination "generic/platform=iOS" -derivedDataPath "$OUT/DerivedData" -allowProvisioningUpdates \
  DEVELOPMENT_TEAM="$TEAM" CODE_SIGN_STYLE=Automatic CURRENT_PROJECT_VERSION="$BUILD_NUMBER" \
  build > "$LOG_DIR/xcode-$STAMP.log" 2>&1 \
  || { echo "xcodebuild failed; see $LOG_DIR/xcode-$STAMP.log" >&2; grep -E "error:|errSec" "$LOG_DIR/xcode-$STAMP.log" | head -20 >&2
      if grep -q errSecInternalComponent "$LOG_DIR/xcode-$STAMP.log"; then
        echo "Signing could not use the keychain (typical over SSH). Run this script from Terminal on the Mac, with --skip-unity." >&2; fi
      exit 1; }

APP="$(find "$OUT/DerivedData/Build/Products/Release-iphoneos" -maxdepth 1 -name "*.app" | head -1)"
[ -n "$APP" ] || { echo "No .app produced" >&2; exit 1; }
echo "    built $APP"

COMMIT="$(git -C "$ROOT" rev-parse HEAD)"
DIRTY="$(git -C "$ROOT" status --porcelain | grep -q . && echo yes || echo no)"
XCODE="$(xcodebuild -version | tr '\n' ' ')"
DEVICE_LINE="not installed"

if [ "$INSTALL" = 1 ]; then
  echo "==> [3/4] Install on iPhone"
  DEVICE="${GEOART_DEVICE:-}"
  if [ -z "$DEVICE" ]; then
    JSON="$(mktemp)"
    xcrun devicectl list devices --json-output "$JSON" >/dev/null 2>&1 || true
    DEVICE="$(python3 - "$JSON" <<'EOF'
import json, sys
try:
    devices = json.load(open(sys.argv[1]))["result"]["devices"]
except Exception:
    sys.exit(0)
for d in devices:
    hw, conn = d.get("hardwareProperties", {}), d.get("connectionProperties", {})
    if hw.get("platform") == "iOS" and hw.get("reality") == "physical" and conn.get("tunnelState") == "connected":
        print(d["identifier"]); break
EOF
)"
    rm -f "$JSON"
  fi
  [ -n "$DEVICE" ] || { echo "No connected iPhone. Plug it in, unlock it, trust this Mac, enable Developer Mode; or set GEOART_DEVICE." >&2; exit 1; }
  xcrun devicectl device install app --device "$DEVICE" "$APP"
  echo "==> [4/4] Launch"
  xcrun devicectl device process launch --device "$DEVICE" "$BUNDLE" || echo "    (launch failed; open the app from the Home Screen. First run may need: Settings > General > VPN & Device Management > trust developer)"
  DEVICE_LINE="$(xcrun devicectl device info details --device "$DEVICE" 2>/dev/null | grep -E "marketingName|osVersionNumber" | tr -s ' ' | tr '\n' ' ' || echo "$DEVICE")"
fi

RECORD="$(dirname "$OUT")/build-record.txt"
{
  echo "---- $(date '+%Y-%m-%d %H:%M %Z')"
  echo "commit: $COMMIT (dirty: $DIRTY)  scene: $SCENE"
  echo "build: $BUILD_NUMBER  bundle: $BUNDLE  team: $TEAM"
  echo "unity: $VERSION  xcode: $XCODE"
  echo "device: $DEVICE_LINE"
} >> "$RECORD"
echo "Done. Build record appended to $RECORD"
