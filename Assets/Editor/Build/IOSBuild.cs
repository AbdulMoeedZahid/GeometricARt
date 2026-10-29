using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GeoART.EditorTools
{
    /// <summary>
    /// Exports the Xcode project (A-1). Normally run by tools/ios_build.sh, which then signs, installs and launches.
    /// Environment (all optional):
    ///   GEOART_IOS_OUT    output folder (default Builds/iOS)
    ///   GEOART_SCENE      scene to build (default Assets/Scenes/Main.unity; e.g. the Sandbox_A_AR scene)
    ///   GEOART_BUNDLE_ID  bundle identifier for this build only (restored afterwards, so it is never committed)
    ///   GEOART_DEV=1      development build (attach the profiler / see logs)
    /// </summary>
    public static class IOSBuild
    {
        [MenuItem("GeoART/Build/Export iOS Xcode Project", priority = 20)]
        public static void BuildFromMenu()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
                throw new InvalidOperationException("Could not switch the active build target to iOS. Is iOS Build Support installed?");
            Build();
        }

        /// <summary>Pass -buildTarget iOS on the command line so no target switch happens mid-method.</summary>
        public static void BuildBatch() => ProjectSetup.RunBatch(() =>
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                throw new InvalidOperationException("Run with -buildTarget iOS.");
            Build();
        });

        static void Build()
        {
            string outDir = Env("GEOART_IOS_OUT", "Builds/iOS");
            string scene = Env("GEOART_SCENE", SceneBuilder.MainScene);
            string bundleId = Env("GEOART_BUNDLE_ID", null);
            bool development = Env("GEOART_DEV", "0") == "1";

            if (!File.Exists(scene)) throw new FileNotFoundException($"Scene not found: {scene}. Run GeoART/Setup/2 first.");

            string previousBundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            try
            {
                if (!string.IsNullOrEmpty(bundleId))
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, bundleId);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scene },
                    locationPathName = outDir,
                    target = BuildTarget.iOS,
                    targetGroup = BuildTargetGroup.iOS,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                    throw new Exception($"iOS export failed: {summary.result} ({summary.totalErrors} errors). See the Editor log.");
                Debug.Log($"[GeoART] Xcode project exported to {Path.GetFullPath(outDir)} from {scene} in {summary.totalTime}.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(bundleId))
                {
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, previousBundleId);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        static string Env(string name, string fallback)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }
    }
}
