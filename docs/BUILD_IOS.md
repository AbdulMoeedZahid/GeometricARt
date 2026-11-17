# Running Geometric ARt on an iPhone (ARKit)

Android has its own page: [BUILD_ANDROID.md](BUILD_ANDROID.md). Both build from the same project; neither build changes
the other platform's settings.

## One-time setup
1. Mac with Xcode and Unity **6000.5.10f1** with **iOS Build Support** (Unity Hub > Installs > Add modules).
2. Open the project once, then **GeoART > Setup > 1. Configure iOS AR Project** (re-runnable): bundle id
   `edu.usf.cap4111.geometricart`, IL2CPP, iPhone only, iOS 15+, portrait, camera usage text, ARKit loader for iOS,
   ARKit Required, the `UNITY_XR_ARKIT_LOADER_ENABLED` define (batch builds need it, or the app shows a black screen),
   AR background feature on the URP renderers. **GeoART > Setup > 2** builds the scenes and prefabs.
3. Your Apple **Team ID**: Xcode > Settings > Accounts > your team, or developer.apple.com > Membership. It is passed
   to the script as `GEOART_TEAM_ID` and never stored in the project. A Personal Team profile expires seven days
   after it is issued; rebuild and reinstall then.
4. iPhone: plug in, unlock, trust the Mac, Settings > Privacy & Security > **Developer Mode** on.

## Every time
Close the Unity Editor for this project, then in **Terminal on the Mac** (signing needs the unlocked login keychain,
which SSH sessions lack):
```
GEOART_TEAM_ID=XXXXXXXXXX tools/ios_build.sh               # export, sign, install, launch
GEOART_TEAM_ID=XXXXXXXXXX tools/ios_build.sh --no-install  # stop after the signed .app
GEOART_TEAM_ID=XXXXXXXXXX tools/ios_build.sh --sandbox     # Sandbox_A_AR scene instead of Main
GEOART_TEAM_ID=XXXXXXXXXX tools/ios_build.sh --skip-unity  # re-sign the last export (e.g. after an SSH export)
```
Output: `~/GeoARtBuilds/ios` (`GEOART_IOS_OUT`); logs: `~/GeoARtBuilds/logs`; each run appends to
`~/GeoARtBuilds/build-record.txt`. Other env vars: `GEOART_BUNDLE_ID` (this build only), `GEOART_SCENE`,
`GEOART_BUILD_NUMBER`, `GEOART_DEVICE` (devicectl id), `GEOART_DEV=1`, `UNITY`.

The Unity step alone (works over SSH): `-buildTarget iOS -executeMethod GeoART.EditorTools.IOSBuild.BuildBatch`; from
the Editor: **GeoART > Build > Export iOS Xcode Project**. Compile check without an export:
`-executeMethod GeoART.EditorTools.PlayerScriptCheck.CompileIOSBatch`.

## If something goes wrong
- **`errSecInternalComponent`**: signing ran over SSH. Rerun from Terminal on the Mac with `--skip-unity`.
- **Black screen, no camera prompt**: the export has no ARKit plugin. Rerun Setup 1 (it restores the define above).
- **Launch fails the first time**: Settings > General > VPN & Device Management > trust the developer.
- **ProjectSettings `preloadedAssets` changed after a build**: see the same entry in BUILD_ANDROID.md; never commit it.
