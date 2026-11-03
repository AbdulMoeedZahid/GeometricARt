using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

namespace GeoART.EditorTools
{
    /// <summary>
    /// Keeps a build from leaving committed settings changed. <see cref="IOSBuild"/> and <see cref="AndroidBuild"/> build
    /// through <see cref="BuildPlayer"/>.
    ///
    /// Preloaded Assets (ProjectSettings.asset): XR Management's XRGeneralSettings for the target, AR Foundation's camera
    /// background shaders (ARKit, ARCore) and the Input System's project-wide actions and settings are added for one build
    /// only. Each package removes its entry in OnPostprocessBuild, which Unity skips when a build fails, so a failed build
    /// leaves them behind; committed, an Android build's leftovers then ship in iOS builds (9270ba3). The committed list
    /// is empty: every build re-adds what its target needs. Stripped before and after each build, and once after each
    /// domain reload, which covers builds started from the Build Profiles window.
    ///
    /// URP shader prefiltering (the URP assets' m_Prefilter* / m_PrefilteringMode* fields): URP's ShaderBuildPreprocessor
    /// recomputes them for the build target before every build and saves the assets. They differ per platform for a real
    /// reason: on Android with an XR loader (ARCore) URP keeps the additional-lights OFF shader variant, as its runtime
    /// does there (m_PrefilteringModeAdditionalLight 4, SelectPixelAndOff), and strips it on iOS (3, SelectPixel). Since
    /// no build reads the committed values, they are restored after the build instead of committing whichever platform
    /// built last.
    /// </summary>
    [InitializeOnLoad]
    static class BuildGuard
    {
        static BuildGuard()
        {
            EditorApplication.delayCall += () =>
            {
                if (!BuildPipeline.isBuildingPlayer && StripPreloadedAssets()) AssetDatabase.SaveAssets();
            };
        }

        /// <summary>BuildPipeline.BuildPlayer that leaves ProjectSettings.asset and the URP assets as it found them.</summary>
        internal static BuildReport BuildPlayer(BuildPlayerOptions options)
        {
            StripPreloadedAssets();
            var prefiltering = SnapshotPrefiltering();
            try
            {
                return BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                StripPreloadedAssets();
                RestorePrefiltering(prefiltering);
                AssetDatabase.SaveAssets();
            }
        }

        // ---------- Preloaded Assets ----------

        /// <summary>Removes build-only entries (and missing references); returns true if anything was removed.</summary>
        internal static bool StripPreloadedAssets()
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

        // ---------- URP shader prefiltering ----------

        /// <summary>Asset path, then property name -> value (enum value or 0/1), for every URP asset under Assets/.</summary>
        static Dictionary<string, Dictionary<string, int>> SnapshotPrefiltering()
        {
            var snapshot = new Dictionary<string, Dictionary<string, int>>();
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;
                var values = new Dictionary<string, int>();
                var p = new SerializedObject(asset).GetIterator();
                for (bool enter = true; p.Next(enter); enter = false)
                {
                    if (!IsPrefilteringField(p)) continue;
                    values[p.name] = p.propertyType == SerializedPropertyType.Boolean ? (p.boolValue ? 1 : 0) : p.intValue;
                }
                snapshot[path] = values;
            }
            return snapshot;
        }

        static void RestorePrefiltering(Dictionary<string, Dictionary<string, int>> snapshot)
        {
            foreach (var entry in snapshot)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(entry.Key);
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                int changed = 0;
                foreach (var field in entry.Value)
                {
                    var p = so.FindProperty(field.Key);
                    if (p == null) continue;
                    if (p.propertyType == SerializedPropertyType.Boolean)
                    {
                        if (p.boolValue == (field.Value != 0)) continue;
                        p.boolValue = field.Value != 0;
                    }
                    else
                    {
                        if (p.intValue == field.Value) continue;
                        p.intValue = field.Value;
                    }
                    changed++;
                }
                if (changed == 0) continue;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(asset);
                Debug.Log($"[GeoART] Restored {changed} URP shader prefiltering fields in {entry.Key} after the build.");
            }
        }

        static bool IsPrefilteringField(SerializedProperty p) =>
            p.name.StartsWith("m_Prefilter") &&
            (p.propertyType == SerializedPropertyType.Boolean || p.propertyType == SerializedPropertyType.Enum ||
             p.propertyType == SerializedPropertyType.Integer);
    }
}
