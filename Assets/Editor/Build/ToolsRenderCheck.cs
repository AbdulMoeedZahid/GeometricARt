using System;
using System.IO;
using System.Reflection;
using System.Text;
using GeoART.BoardRender;
using GeoART.Contracts;
using GeoART.Engine;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace GeoART.EditorTools
{
    /// <summary>
    /// TOOLS builder's rendering check (no device needed):
    ///   1. compiles every pass of GeoART/BoardUnlit for Metal/iOS, so shader errors show up without a full build;
    ///   2. renders a test board (lines/circles/dotted at every width plus a live ghost) to PNGs from phone-like
    ///      viewpoints at 45 cm and 90 cm, with and without MSAA, for a visual check of the stroke anti-aliasing.
    /// Batch (needs graphics, so no -nographics):
    ///   -executeMethod GeoART.EditorTools.ToolsRenderCheck.RunBatch   (PNGs go to $GEOART_RENDER_OUT or Logs/GeoARtRenderCheck)
    /// </summary>
    public static class ToolsRenderCheck
    {
        const string ShaderPath = "Assets/GeoART/BoardRender/Shaders/BoardUnlit.shader";
        const string MaterialPath = "Assets/GeoART/BoardRender/Materials/BoardLines.mat";

        public static void RunBatch() => ProjectSetup.RunBatch(Run);

        [MenuItem("GeoART/Checks/Board rendering check")]
        public static void Run()
        {
            CompileShaderForMetal();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[GeoART] Render check skipped: no graphics device (-nographics).");
                return;
            }
            string outDir = Environment.GetEnvironmentVariable("GEOART_RENDER_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("Logs/GeoARtRenderCheck");
            Directory.CreateDirectory(outDir);
            RenderBoard(outDir);
        }

        static void CompileShaderForMetal()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new InvalidOperationException($"Missing {ShaderPath}");
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("BoardUnlit has import errors");
            ShaderData data = ShaderUtil.GetShaderData(shader);
            int compiled = 0;
            var errors = new StringBuilder();
            for (int s = 0; s < data.SubshaderCount; s++)
            {
                ShaderData.Subshader sub = data.GetSubshader(s);
                for (int p = 0; p < sub.PassCount; p++)
                    foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                    {
                        var info = sub.GetPass(p).CompileVariant(stage, Array.Empty<string>(),
                            ShaderCompilerPlatform.Metal, BuildTarget.iOS);
                        compiled++;
                        if (!info.Success)
                            foreach (var m in info.Messages) errors.AppendLine($"{stage}: {m.message} (line {m.line})");
                    }
            }
            if (errors.Length > 0) throw new InvalidOperationException("BoardUnlit Metal compile failed:\n" + errors);
            Debug.Log($"[GeoART] BoardUnlit compiled for Metal/iOS: {compiled} stages OK.");
        }

        static void RenderBoard(string outDir)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var boardGo = new GameObject("RenderCheckBoard");
            var camGo = new GameObject("RenderCheckCamera", typeof(Camera));
            try
            {
                var view = boardGo.AddComponent<BoardView>();
                var so = new SerializedObject(view);
                so.FindProperty("material").objectReferenceValue = material;
                so.FindProperty("showBase").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                Invoke(view, "Awake");

                var session = new EditorSession();
                DrawTestDesign(session);
                view.Bind(session);
                Invoke(view, "LateUpdate");

                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.78f, 0.74f, 0.68f);   // wooden-table-ish
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 10f;
                cam.fieldOfView = 60f;

                Shoot(cam, new Vector3(0f, 0.36f, -0.3f), outDir, "near45cm");
                Shoot(cam, new Vector3(0f, 0.6f, -0.7f), outDir, "far90cm");
                Invoke(view, "OnDestroy");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        static void Shoot(Camera cam, Vector3 position, string outDir, string name)
        {
            cam.transform.position = position;
            cam.transform.LookAt(Vector3.zero);
            foreach (int msaa in new[] { 1, 4 })
            {
                var rt = new RenderTexture(1290 / 2, 2796 / 2, 24) { antiAliasing = msaa };
                var prev = RenderTexture.active;
                try
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    string path = Path.Combine(outDir, $"board-{name}-msaa{msaa}.png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    Debug.Log($"[GeoART] Render check wrote {path}");
                }
                finally
                {
                    cam.targetTexture = null;
                    RenderTexture.active = prev;
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }

        /// <summary>Every width solid and dotted, near-horizontal lines (worst case for aliasing), circles of several
        /// sizes, and an open Circle gesture so the ghost is drawn too.</summary>
        static void DrawTestDesign(EditorSession s)
        {
            void Drag(double ax, double ay, double bx, double by)
            {
                s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(ax, ay), true, 0));
                s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(bx, by), true, 0.1));
                s.Pointer(new PointerEvent(PointerPhase.Up, new Vec2(bx, by), true, 0.2));
            }
            var widths = new[] { LineWidth.Thin, LineWidth.Medium, LineWidth.Thick };
            for (int w = 0; w < 3; w++)
            {
                s.Style = StrokeStyle.Default.WithWidth(widths[w]);
                Drag(0.05, 0.92 - w * 0.05, 0.95, 0.93 - w * 0.05);              // nearly horizontal
                s.Style = s.Style.WithDash(LineStyle.Dotted);
                Drag(0.05, 0.75 - w * 0.05, 0.95, 0.75 - w * 0.05);
                s.Style = s.Style.WithDash(LineStyle.Solid).WithColor(Palette.Red);
                Drag(0.05 + w * 0.04, 0.05, 0.3 + w * 0.04, 0.55);                // steep
            }
            s.Style = StrokeStyle.Default;
            s.SetTool(ToolId.Circle);
            Drag(0.5, 0.5, 0.95, 0.5);      // big circle
            Drag(0.62, 0.3, 0.7, 0.3);
            Drag(0.62, 0.3, 0.635, 0.3);    // tiny
            s.Style = StrokeStyle.Default.WithColor(Palette.Blue).WithDash(LineStyle.Dotted);
            Drag(0.5, 0.5, 0.5, 0.82);
            s.Style = StrokeStyle.Default.WithColor(Palette.Green).WithWidth(LineWidth.Thick);
            s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.3, 0.3), true, 0));
            s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.42, 0.36), true, 0.1));   // ghost stays open
        }

        static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(target, null);
    }
}
