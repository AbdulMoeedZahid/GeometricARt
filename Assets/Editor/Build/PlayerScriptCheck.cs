using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace GeoART.EditorTools
{
    /// <summary>
    /// Compiles the player scripts for iOS (with the iOS scripting defines, e.g. UNITY_IOS and
    /// UNITY_XR_ARKIT_LOADER_ENABLED) without building or exporting a player, so code that only compiles for the device
    /// is checked over SSH in seconds. Compile errors appear in the log as "error CS"; the method fails if any of
    /// A's runtime assemblies is missing from the result.
    /// Batch: -executeMethod GeoART.EditorTools.PlayerScriptCheck.CompileIOSBatch
    /// Environment (optional): GEOART_SCRIPTCHECK_OUT output folder (default Temp/GeoARtScriptCheck).
    /// </summary>
    public static class PlayerScriptCheck
    {
        static readonly string[] s_RequiredAssemblies =
        {
            "GeoART.Contracts.dll", "GeoART.BoardRender.dll", "GeoART.ARBoard.dll", "GeoART.App.dll", "GeoART.Mocks.dll", "GeoART.Engine.dll",
        };

        [MenuItem("GeoART/Build/Check iOS Player Scripts", priority = 21)]
        public static void CompileIOS()
        {
            string outDir = Environment.GetEnvironmentVariable("GEOART_SCRIPTCHECK_OUT");
            if (string.IsNullOrWhiteSpace(outDir)) outDir = "Temp/GeoARtScriptCheck";
            Directory.CreateDirectory(outDir);

            var settings = new ScriptCompilationSettings
            {
                target = BuildTarget.iOS,
                group = BuildTargetGroup.iOS,
                options = ScriptCompilationOptions.None,
            };
            ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(settings, outDir);

            var built = result.assemblies?.ToList() ?? new System.Collections.Generic.List<string>();
            var missing = s_RequiredAssemblies.Where(a => !built.Contains(a)).ToArray();
            if (built.Count == 0 || missing.Length > 0)
                throw new InvalidOperationException(
                    $"iOS player script compilation failed ({built.Count} assemblies; missing: {string.Join(", ", missing)}). " +
                    "Search the log for \"error CS\".");
            Debug.Log($"[GeoART] iOS player scripts compiled: {built.Count} assemblies into {Path.GetFullPath(outDir)} " +
                      $"(GeoART: {string.Join(", ", built.Where(a => a.StartsWith("GeoART")))}).");
        }

        public static void CompileIOSBatch() => ProjectSetup.RunBatch(CompileIOS);
    }
}
