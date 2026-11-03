using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;

namespace GeoART.EditorTools
{
    /// <summary>
    /// Keeps Player Settings > Preloaded Assets free of the entries that package build processors add for one build only:
    /// XR Management's XRGeneralSettings for the target, AR Foundation's camera background shaders (ARKit, ARCore) and the
    /// Input System's project-wide actions and settings. Each package removes its entry in OnPostprocessBuild, which Unity
    /// skips when a build fails, so a failed build leaves them in ProjectSettings.asset; committed, an Android build's
    /// leftovers then ship in iOS builds (9270ba3). The committed list is empty: every build re-adds what its target needs.
    /// <see cref="IOSBuild"/> and <see cref="AndroidBuild"/> build through <see cref="BuildPlayer"/>, which strips before and
    /// after; the Editor also strips once after each domain reload, which covers builds started from the Build Profiles window.
    /// </summary>
    [InitializeOnLoad]
    static class PreloadedAssetsGuard
    {
        static PreloadedAssetsGuard()
        {
            EditorApplication.delayCall += () =>
            {
                if (!BuildPipeline.isBuildingPlayer && Strip()) AssetDatabase.SaveAssets();
            };
        }

        /// <summary>BuildPipeline.BuildPlayer that leaves Preloaded Assets (and so ProjectSettings.asset) as it found them.</summary>
        internal static BuildReport BuildPlayer(BuildPlayerOptions options)
        {
            Strip();
            try
            {
                return BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                Strip();
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>Removes build-only entries (and missing references); returns true if anything was removed.</summary>
        internal static bool Strip()
        {
            var assets = PlayerSettings.GetPreloadedAssets() ?? new Object[0];
            var kept = assets.Where(a => !IsBuildOnly(a)).ToArray();
            if (kept.Length == assets.Length) return false;
            PlayerSettings.SetPreloadedAssets(kept);
            Debug.Log($"[GeoART] Removed {assets.Length - kept.Length} build-only Preloaded Assets left by an earlier build: " +
                      string.Join(", ", assets.Where(IsBuildOnly).Select(a => a == null ? "(missing)" : $"{a.name} ({a.GetType().Name})")));
            return true;
        }

        static bool IsBuildOnly(Object a) =>
            a == null || a is Shader || a is XRGeneralSettings || a is InputActionAsset || a is InputSettings;
    }
}
