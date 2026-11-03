#!/usr/bin/env bash
# Build Geometric ARt for an Android phone (ARCore): Unity -> APK -> adb install -> launch.
# Usage:  tools/android_build.sh [--no-install] [--sandbox] [--skip-unity]
#   --no-install  only build the APK (also what happens when no phone shows up in `adb devices`)
#   --skip-unity  reuse the last built APK (e.g. to install it on another phone)
# Works over SSH: the APK is signed with Unity's debug key, so no keychain is involved.
# See docs/BUILD_ANDROID.md for one-time setup and every environment variable.
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

PACKAGE="edu.usf.cap4111.geometricart"                      # Android application id (GeoART/Setup/3)
APK="${GEOART_ANDROID_OUT:-$HOME/GeoARtBuilds/android/GeometricARt.apk}"   # outside the repo and without spaces
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"
BUILDS="$(dirname "$(dirname "$APK")")"
LOG_DIR="$BUILDS/logs"
mkdir -p "$(dirname "$APK")" "$LOG_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"

if [ "$SKIP_UNITY" = 1 ]; then
  [ -f "$APK" ] || { echo "No APK at $APK; run without --skip-unity." >&2; exit 1; }
  echo "==> [1/3] Unity: skipped, reusing $APK"
else
[ -x "$UNITY" ] || { echo "Unity $VERSION not found at $UNITY (set UNITY=...)" >&2; exit 1; }
if pgrep -f "Unity.app/Contents/MacOS/Unity.*-projectpath.*$(basename "$ROOT")" >/dev/null 2>&1; then
  echo "Close the Unity Editor for this project first (batch mode cannot open a project that is already open)." >&2; exit 1
fi

echo "==> [1/3] Unity: build APK ($SCENE, Unity $VERSION)"
GEOART_ANDROID_OUT="$APK" GEOART_SCENE="$SCENE" \
  "$UNITY" -batchmode -nographics -projectPath "$ROOT" -buildTarget Android \
  -executeMethod GeoART.EditorTools.AndroidBuild.BuildBatch -logFile "$LOG_DIR/unity-android-$STAMP.log" \
  || { echo "Unity build failed; see $LOG_DIR/unity-android-$STAMP.log" >&2; tail -40 "$LOG_DIR/unity-android-$STAMP.log" >&2; exit 1; }
fi
echo "    APK: $APK ($(du -h "$APK" | cut -f1))"

COMMIT="$(git -C "$ROOT" rev-parse HEAD)"
DIRTY="$(git -C "$ROOT" status --porcelain | grep -q . && echo yes || echo no)"
DEVICE_LINE="not installed"

if [ "$INSTALL" = 1 ]; then
  # adb: $ADB, else the one on PATH, else the SDK that Unity's Android module installs.
  ADB="${ADB:-$(command -v adb || echo "$(dirname "$UNITY")/../../../PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb")}"
  DEVICE="${GEOART_DEVICE:-}"
  if [ -x "$ADB" ] && [ -z "$DEVICE" ]; then
    DEVICE="$("$ADB" devices | awk 'NR > 1 && $2 == "device" { print $1; exit }')"
  fi
  if [ ! -x "$ADB" ]; then
    echo "==> [2/3] Install: skipped, adb not found (set ADB=...)"
  elif [ -z "$DEVICE" ]; then
    echo "==> [2/3] Install: skipped, no phone in 'adb devices' (USB debugging on, prompt accepted?); or set GEOART_DEVICE"
  else
    echo "==> [2/3] Install on $DEVICE"
    if ! OUTPUT="$("$ADB" -s "$DEVICE" install -r "$APK" 2>&1)"; then
      echo "$OUTPUT" >&2
      if echo "$OUTPUT" | grep -q INSTALL_FAILED_UPDATE_INCOMPATIBLE; then
        echo "The installed app was signed by another machine's debug key: $ADB -s $DEVICE uninstall $PACKAGE, then rerun with --skip-unity." >&2
      fi
      exit 1
    fi
    echo "==> [3/3] Launch"
    "$ADB" -s "$DEVICE" shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1 \
      || echo "    (launch failed; open the app on the phone)"
    MODEL="$("$ADB" -s "$DEVICE" shell getprop ro.product.model | tr -d '\r')"
    RELEASE="$("$ADB" -s "$DEVICE" shell getprop ro.build.version.release | tr -d '\r')"
    DEVICE_LINE="$MODEL (Android $RELEASE, $DEVICE)"
  fi
fi

RECORD="$BUILDS/build-record.txt"
{
  echo "---- $(date '+%Y-%m-%d %H:%M %Z') android"
  echo "commit: $COMMIT (dirty: $DIRTY)  scene: $SCENE"
  echo "apk: $APK  package: $PACKAGE  dev: ${GEOART_DEV:-0}"
  echo "unity: $VERSION"
  echo "device: $DEVICE_LINE"
} >> "$RECORD"
echo "Done. Build record appended to $RECORD"
