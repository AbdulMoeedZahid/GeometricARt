using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.ARKit;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace GeoART.EditorTools
{
    /// <summary>
    /// One-click, re-runnable iOS AR configuration (P2 4.1 steps 1-3). Menu: GeoART/Setup/1.
    /// Batch: Unity -batchmode -projectPath . -executeMethod GeoART.EditorTools.ProjectSetup.ConfigureBatch
    /// Signing (team ID) is deliberately NOT stored here; tools/ios_build.sh passes it to xcodebuild.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ProductName = "Geometric ARt";
        public const string DefaultBundleId = "edu.usf.cap4111.geometricart";
        const string ARKitLoaderType = "UnityEngine.XR.ARKit.ARKitLoader";

        [MenuItem("GeoART/Setup/1. Configure iOS AR Project", priority = 1)]
        public static void Configure()
        {
            ConfigurePlayer();
            ConfigureXRManagement();
            ConfigureARKit();
            ConfigureRenderers();
            AssetDatabase.SaveAssets();
            Debug.Log("[GeoART] iOS AR project configured: player settings, ARKit loader (iOS), ARKit required, AR background on URP renderers.");
        }

        public static void ConfigureBatch() => RunBatch(Configure);

        /// <summary>Runs an action from -executeMethod and exits with 0/1 so scripts can detect failure.</summary>
        internal static void RunBatch(Action action)
        {
            try
            {
                action();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "GeoART Team";
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, DefaultBundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.cameraUsageDescription =
                "Geometric ARt uses the camera to place your drawing board on a table. Camera images stay on your device.";
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;   // sketches are portrait
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        static void ConfigureXRManagement()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                EnsureFolder("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.iOS))
                perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.iOS);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.iOS))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.iOS);

            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.iOS);
            general.InitManagerOnStart = true;
            var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.iOS);
            if (!XRPackageMetadataStore.IsLoaderAssigned(ARKitLoaderType, BuildTargetGroup.iOS) &&
                !XRPackageMetadataStore.AssignLoader(manager, ARKitLoaderType, BuildTargetGroup.iOS))
                throw new InvalidOperationException("Could not assign the ARKit loader for iOS in XR Plug-in Management.");

            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);
            EnsureIOSDefine(ARKitLoaderDefine);
        }

        // ARKit's LoaderEnabledCheck writes this define when the GUI Editor sees the loader enabled, but it returns early
        // in batch mode, and its batch-mode recovery in OnPreprocessBuild only compiles when the define already exists.
        // Without it a batch-only project exports an Xcode project with no ARKit native plugin (black screen, no camera
        // prompt). Face tracking stays off, so UNITY_XR_ARKIT_FACE_TRACKING_ENABLED is deliberately not added.
        const string ARKitLoaderDefine = "UNITY_XR_ARKIT_LOADER_ENABLED";

        /// <summary>Adds an iOS scripting define if missing, the same way ARKit's LoaderEnabledCheck.AddDefine does.</summary>
        static void EnsureIOSDefine(string define)
        {
            string current = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS) ?? string.Empty;
            var defines = current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim())
                .Where(d => d.Length > 0).ToList();
            if (defines.Contains(define)) return;
            defines.Add(define);
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS, string.Join(";", defines));
            Debug.Log($"[GeoART] Added iOS scripting define {define}.");
        }

        static void ConfigureARKit()
        {
            var settings = ARKitSettings.GetOrCreateSettings();
            settings.requirement = ARKitSettings.Requirement.Required;
            settings.faceTracking = false;
            EditorUtility.SetDirty(settings);
        }

        /// <summary>Adds ARBackgroundRendererFeature to every URP renderer, so the camera feed shows at any quality level.</summary>
        static void ConfigureRenderers()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null || data.rendererFeatures.Any(f => f is ARBackgroundRendererFeature)) continue;

                var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
                feature.name = "ARBackgroundRendererFeature";
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.SaveAssets();
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

                var so = new SerializedObject(data);
                var features = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                Debug.Log($"[GeoART] Added ARBackgroundRendererFeature to {path}");
            }
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
