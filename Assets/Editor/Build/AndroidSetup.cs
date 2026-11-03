using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.ARCore;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace GeoART.EditorTools
{
    /// <summary>
    /// One-click, re-runnable Android (ARCore) AR configuration, the Android counterpart of <see cref="ProjectSetup"/>.
    /// Only touches Android settings, so the iOS build is unaffected. Menu: GeoART/Setup/3.
    /// Batch: Unity -batchmode -projectPath . -executeMethod GeoART.EditorTools.AndroidSetup.ConfigureBatch
    /// Shared with iOS (already set by Setup 1): product name, portrait, linear colour, the Mobile URP asset and its
    /// ARBackgroundRendererFeature (Android uses the same "Mobile" quality level). Adds ARCommandBufferSupportRendererFeature,
    /// which ARCore needs under Vulkan.
    /// </summary>
    public static class AndroidSetup
    {
        const string ARCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
        const int MinApiLevel = 29;   // Android 10; ARCore refuses "Required" + Vulkan below it, and 29 is a safe floor anyway

        [MenuItem("GeoART/Setup/3. Configure Android AR Project", priority = 3)]
        public static void Configure()
        {
            ConfigurePlayer();
            ConfigureXRManagement();
            ConfigureARCore();
            // Needed if Vulkan is ever re-enabled (without it ARCore throws every frame). It only injects a plugin callback,
            // so it is harmless under OpenGL ES and for ARKit on the shared renderers.
            ProjectSetup.AddRendererFeature<ARCommandBufferSupportRendererFeature>();
            AssetDatabase.SaveAssets();
            Debug.Log("[GeoART] Android AR project configured: IL2CPP ARM64, API " + MinApiLevel +
                      "+, OpenGL ES 3, ARCore loader (Android), ARCore required, AR command buffer feature on URP renderers.");
        }

        public static void ConfigureBatch() => ProjectSetup.RunBatch(Configure);

        static void ConfigurePlayer()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ProjectSetup.DefaultBundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;   // ARCore ships 64-bit only on new devices
            if ((int)PlayerSettings.Android.minSdkVersion < MinApiLevel)
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MinApiLevel;

            // OpenGL ES 3 only. Under Vulkan (the Auto default) the ARCore session tracks but the camera background stays
            // black (Galaxy S24 Ultra, ARCore 6.5, URP 17.5), even with ARCommandBufferSupportRendererFeature added.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        }

        static void ConfigureXRManagement()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
                throw new InvalidOperationException("No XR settings asset. Run GeoART/Setup/1 first.");
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = true;
            var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.IsLoaderAssigned(ARCoreLoaderType, BuildTargetGroup.Android) &&
                !XRPackageMetadataStore.AssignLoader(manager, ARCoreLoaderType, BuildTargetGroup.Android))
                throw new InvalidOperationException("Could not assign the ARCore loader for Android in XR Plug-in Management.");

            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);
        }

        /// <summary>Required: the Play Store only offers the app to ARCore devices and installs Google Play Services for AR.</summary>
        static void ConfigureARCore()
        {
            var settings = ARCoreSettings.GetOrCreateSettings();
            settings.requirement = ARCoreSettings.Requirement.Required;
            settings.depth = ARCoreSettings.Requirement.Optional;
            EditorUtility.SetDirty(settings);
        }
    }
}
