# Running Geometric ARt on an Android phone (ARCore)

The app was iPhone-only (ARKit). Android support adds Google's ARCore next to it; the iOS build is unchanged.

## What was added
- `Packages/manifest.json`: `com.unity.xr.arcore` 6.5.0 (same version as AR Foundation and ARKit).
- `Assets/Editor/Build/AndroidSetup.cs`: menu **GeoART > Setup > 3. Configure Android AR Project** (re-runnable). Android
  only: bundle id `edu.usf.cap4111.geometricart`, IL2CPP, ARM64, min API 29 (Android 10; ARCore Required with Vulkan refuses to build below it), ARCore loader enabled for
  Android in XR Plug-in Management, ARCore **Required** (depth optional).
  Also adds `ARCommandBufferSupportRendererFeature` to the URP renderers. Android renders with Vulkan (the project's
  graphics APIs: Vulkan, then OpenGL ES 3 as fallback), and without this feature ARCore throws every frame and the
  **camera feed stays black**. With it, the camera feed, tracking and placement work: **verified on a Galaxy S24 Ultra**
  (Android 16, ARCore 1.45). Harmless for iOS.
- `Assets/Editor/Build/AndroidBuild.cs`: menu **GeoART > Build > Build and Run on Android Phone**. Switches to Android,
  builds `~/GeoARtBuilds/android/GeometricARt.apk` from `Assets/Scenes/Main.unity`, installs it on the USB-connected phone and
  launches it. It applies Setup 3 itself before every build. Batch: `-buildTarget Android -executeMethod GeoART.EditorTools.AndroidBuild.BuildBatch` (build only, no phone needed).
  Env vars: `GEOART_SCENE` (e.g. `Assets/Scenes/Sandbox/Sandbox_A_AR.unity`), `GEOART_ANDROID_OUT`, `GEOART_DEV=1`.
- `tools/android_build.sh`: the same build from the command line (see below).
- Shared with iOS and already in place: portrait, linear colour, the `Mobile` URP asset (Android's default quality
  level) with the AR camera background feature. Runtime code uses only AR Foundation, so no code is platform-specific.

## One-time phone setup
1. The phone must be on Google's ARCore supported devices list. Install **Google Play Services for AR** from the Play Store.
2. Settings > About phone > tap **Build number** 7 times (Developer options), then Developer options > **USB debugging** on.
3. Plug into the Mac, unlock, accept **Allow USB debugging**.

## One-time Mac setup
Unity 6000.5.10f1 needs the **Android Build Support** module with its **Android SDK & NDK Tools** and **OpenJDK**
children: Unity Hub > Installs > 6000.5.10f1 > Add modules, or headless:
`"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless install-modules --version 6000.5.10f1 -m android --childModules`.
Check: `/Applications/Unity/Hub/Editor/6000.5.10f1/PlaybackEngines/AndroidPlayer/` exists. Without it the batch build
fails with "Android Build Support ... is not installed".

## One-time project setup (Unity Editor)
1. Let Unity import the ARCore package (it does so when the Editor gets focus after the manifest change).
2. **GeoART > Setup > 3. Configure Android AR Project**. Console: `[GeoART] Android AR project configured ...`.

## Every time
**GeoART > Build > Build and Run on Android Phone**. The first switch to Android re-imports assets and takes a while.
The app opens on the phone: move it slowly over a textured table until a plane is found, place the board (or tap the
corners), and the dashed grid appears on the surface. Square/Hex and 1x1..4x4 are on the right.
The Editor stays on the Android target afterwards; that does not affect `tools/ios_build.sh`, which passes `-buildTarget iOS`.

## Command line (also over SSH)
Close the Editor for this project first, then from the repo root:
```
tools/android_build.sh               # build the APK, install + launch it if a phone is in `adb devices`
tools/android_build.sh --no-install  # build only
tools/android_build.sh --sandbox     # Sandbox_A_AR scene instead of Main
tools/android_build.sh --skip-unity  # reinstall the last APK
```
APK: `~/GeoARtBuilds/android/GeometricARt.apk` (`GEOART_ANDROID_OUT`); Unity log: `~/GeoARtBuilds/logs/unity-android-*.log`;
each run appends to `~/GeoARtBuilds/build-record.txt`. Other env vars: `GEOART_SCENE`, `GEOART_DEV=1`,
`GEOART_DEVICE` (adb serial), `ADB`, `UNITY`. Signing uses Unity's debug key, so unlike iOS it works over SSH.
Quick compile check without a build: `-buildTarget Android -executeMethod GeoART.EditorTools.PlayerScriptCheck.CompileAndroidBatch`.

## If something goes wrong
- **"No Android device"**: check `adb devices` (Unity's adb is in
  `/Applications/Unity/Hub/Editor/6000.5.10f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/`); re-accept the USB prompt.
- **Black camera / app closes at start**: Google Play Services for AR missing or outdated, or the device is not
  ARCore-certified.
- **Black camera feed**: check that both `Mobile_Renderer.asset` and `PC_Renderer.asset` list
  `ARCommandBufferSupportRendererFeature` (the Console's setup line says "AR command buffer feature"). Note: an
  `adb screencap` may show black even when the camera works on the phone's screen; trust the phone.
- **ProjectSettings `preloadedAssets` changed after a build**: XR Management, ARCore/ARKit and the Input System add
  entries there during a build and only remove them when it succeeds. The GeoART build menus and scripts strip them
  before and after every build (`BuildGuard`), and the Editor strips leftovers after each script reload. If
  the diff still shows up, do not commit it: the committed value is `preloadedAssets: []`.
- **`Mobile_RPAsset.asset` / `PC_RPAsset.asset` changed after a build** (`m_PrefilteringMode*`, `m_Prefilter*`): URP
  recomputes these shader-stripping fields for the target before every build (Android with ARCore keeps one more
  lighting variant than iOS). `BuildGuard` restores them after GeoART builds; a build from the Build Profiles window
  can still leave the change. Do not commit it.
- **`INSTALL_FAILED_UPDATE_INCOMPATIBLE`**: the app on the phone was built on another machine (different debug key).
  `adb uninstall edu.usf.cap4111.geometricart`, then install again.
- **Script edits not picked up** (the Console's setup line does not change): Unity sometimes skips recompiling the
  `GeoART.Editor.Build` assembly. Assets > Refresh, and if that is not enough, restart Unity before building.
- Logs: build with `GEOART_DEV=1` set before launching Unity, then `adb logcat -s Unity`.
