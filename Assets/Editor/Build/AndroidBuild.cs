using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GeoART.EditorTools
{
    /// <summary>
    /// Builds an Android APK and, from the menu, installs and launches it on the phone connected over USB (Unity runs adb).
    /// Applies <see cref="AndroidSetup.Configure"/> first (cheap, idempotent), so the build never runs on stale settings. Environment (all optional, same names as <see cref="IOSBuild"/>):
    ///   GEOART_ANDROID_OUT  APK path (default ~/GeoARtBuilds/android/GeometricARt.apk, outside the repo)
    ///   GEOART_SCENE        scene to build (default Assets/Scenes/Main.unity; e.g. the Sandbox_A_AR scene)
    ///   GEOART_DEV=1        development build (logs via adb logcat, profiler)
    /// Batch (builds only, no phone needed; normally run by tools/android_build.sh): Unity -batchmode -nographics
    ///   -projectPath . -buildTarget Android -executeMethod GeoART.EditorTools.AndroidBuild.BuildBatch
    /// Builds through <see cref="BuildGuard"/>, so even a failed build leaves ProjectSettings and the URP assets unchanged.
    /// </summary>
    public static class AndroidBuild
    {
        [MenuItem("GeoART/Build/Build and Run on Android Phone", priority = 30)]
        public static void BuildAndRunFromMenu()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Could not switch the active build target to Android. Is Android Build Support installed?");
            Build(true);
        }

        /// <summary>Pass -buildTarget Android on the command line so no target switch happens mid-method.</summary>
        public static void BuildBatch() => ProjectSetup.RunBatch(() =>
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)
                    ? "Run with -buildTarget Android."
                    : "Android Build Support (with the Android SDK & NDK tools and OpenJDK) is not installed for this Unity version.");
            Build(false);
        });

        static void Build(bool installAndRun)
        {
            string apk = Env("GEOART_ANDROID_OUT", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GeoARtBuilds", "android", "GeometricARt.apk"));
            string scene = Env("GEOART_SCENE", SceneBuilder.MainScene);
            bool development = Env("GEOART_DEV", "0") == "1";
            if (!File.Exists(scene)) throw new FileNotFoundException($"Scene not found: {scene}. Run GeoART/Setup/2 first.");
            Directory.CreateDirectory(Path.GetDirectoryName(apk) ?? ".");
            AndroidSetup.Configure();

            EditorUserBuildSettings.buildAppBundle = false;   // a plain APK installs straight onto the phone
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;   // an APK, not a Gradle project
            var options = BuildOptions.None;
            if (development) options |= BuildOptions.Development;
            if (installAndRun) options |= BuildOptions.AutoRunPlayer;

            BuildReport report = BuildGuard.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = options,
            });
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"Android build failed: {summary.result} ({summary.totalErrors} errors). See the Console.");
            Debug.Log($"[GeoART] APK built at {Path.GetFullPath(apk)} from {scene} in {summary.totalTime}" +
                      (installAndRun ? "; installing and launching on the connected phone." : "."));
        }

        static string Env(string name, string fallback)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }
    }
}
