# Running Geometric ARt on an Android phone (ARCore)

The app was iPhone-only (ARKit). Android support adds Google's ARCore next to it; the iOS build is unchanged.

## What was added
- `Packages/manifest.json`: `com.unity.xr.arcore` 6.5.0 (same version as AR Foundation and ARKit).
- `Assets/Editor/Build/AndroidSetup.cs`: menu **GeoART > Setup > 3. Configure Android AR Project** (re-runnable). Android
  only: bundle id `edu.usf.cap4111.geometricart`, IL2CPP, ARM64, min API 29 (Android 10; ARCore Required with Vulkan refuses to build below it), ARCore loader enabled for
  Android in XR Plug-in Management, ARCore **Required** (depth optional).
  Also adds `ARCommandBufferSupportRendererFeature` to the URP renderers: without it ARCore under Vulkan throws
  every frame and the **camera feed stays black** (seen on a Galaxy S24 Ultra). Harmless for iOS.
  Graphics API is forced to **OpenGL ES 3**: with Vulkan the session tracked but the camera background was still black
  on the S24 Ultra even with that feature. Vulkan can be retried later by changing `AndroidSetup.ConfigurePlayer`.
- `Assets/Editor/Build/AndroidBuild.cs`: menu **GeoART > Build > Build and Run on Android Phone**. Switches to Android,
  builds `Builds/Android/GeometricARt.apk` from `Assets/Scenes/Main.unity`, installs it on the USB-connected phone and
  launches it. It applies Setup 3 itself before every build. Batch: `-buildTarget Android -executeMethod GeoART.EditorTools.AndroidBuild.BuildBatch` (build only).
  Env vars: `GEOART_SCENE` (e.g. `Assets/Scenes/Sandbox/Sandbox_A_AR.unity`), `GEOART_ANDROID_OUT`, `GEOART_DEV=1`.
- Shared with iOS and already in place: portrait, linear colour, the `Mobile` URP asset (Android's default quality
  level) with the AR camera background feature. Runtime code uses only AR Foundation, so no code is platform-specific.

## One-time phone setup
1. The phone must be on Google's ARCore supported devices list. Install **Google Play Services for AR** from the Play Store.
2. Settings > About phone > tap **Build number** 7 times (Developer options), then Developer options > **USB debugging** on.
3. Plug into the Mac, unlock, accept **Allow USB debugging**.

## One-time project setup (Unity Editor)
1. Let Unity import the ARCore package (it does so when the Editor gets focus after the manifest change).
2. **GeoART > Setup > 3. Configure Android AR Project**. Console: `[GeoART] Android AR project configured ...`.

## Every time
**GeoART > Build > Build and Run on Android Phone**. The first switch to Android re-imports assets and takes a while.
The app opens on the phone: move it slowly over a textured table until a plane is found, place the board (or tap the
corners), and the dashed grid appears on the surface. Square/Hex and 1x1..4x4 are on the right.
The Editor stays on the Android target afterwards; that does not affect `tools/ios_build.sh`, which passes `-buildTarget iOS`.

## If something goes wrong
- **"No Android device"**: check `adb devices` (Unity's adb is in
  `/Applications/Unity/Hub/Editor/6000.5.10f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/`); re-accept the USB prompt.
- **Black camera / app closes at start**: Google Play Services for AR missing or outdated, or the device is not
  ARCore-certified.
- **Board strokes missing or pink**: the board shader (`BoardUnlit`) was first verified on Metal; check the Console for
  shader errors for GLES3 and report.
- **Script edits not picked up** (the Console's setup line does not change): Unity sometimes skips recompiling the
  `GeoART.Editor.Build` assembly. Assets > Refresh, and if that is not enough, restart Unity before building.
- Logs: build with `GEOART_DEV=1` set before launching Unity, then `adb logcat -s Unity`.
