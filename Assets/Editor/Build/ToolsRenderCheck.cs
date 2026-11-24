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
    ///   1. compiles every pass of GeoART/BoardUnlit for Metal/iOS and for Vulkan + GLES3/Android, so shader errors show
    ///      up without a full build;
    ///   2. renders a test board (lines/circles/dotted at every width plus a live ghost; snapping off) and a snapping
    ///      board (grid and shape candidate dots, a pending tap-to-connect ring and a live hover ring; on the sheet and on
    ///      a dark table) to PNGs from phone-like viewpoints at 45 cm and 90 cm, with and without MSAA;
    ///   3. renders an open Select marquee (orange dotted box, cyan halo on the enclosed shapes) on the sheet and on a
    ///      dark table (select-*.png, select-dark-*.png);
    ///   4. renders open Move and Scale drags of a selection (cyan halos, dashed selection frame and pivot marker on the
    ///      originals, orange ghosts, cursor, hover ring when snapped): xform-*.png, xform-dark-*.png, xform-scale-*.png;
    ///   5. renders the test board after two Undos (undo-*.png): the base design without the open green ghost, the blue
    ///      dotted circle and the tiny circle, so BoardView redraws from a swapped-in board.
    ///   6. renders banded layers through BoardView (Layer.Bands on the top layer of GoldenBoards.StarOnFullBoard and
    ///      Tiled2x2, thin black outlines): bands-* (Double, structure shown), bands-stripped-*, interlace-* and
    ///      interlace-inner-* (8 mm), the tiled bands-tiled-* / interlace-tiled-*, and interlace with the structure line
    ///      shown (cut under every over band): interlace-structure-*, interlace-inner-structure-* (10 mm), for
    ///      comparison with the book (p22-23); 45 cm, 90 cm and straight down.
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
            CompileShader(ShaderCompilerPlatform.Metal, BuildTarget.iOS);
            CompileShader(ShaderCompilerPlatform.Vulkan, BuildTarget.Android);
            CompileShader(ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
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

        static void CompileShader(ShaderCompilerPlatform platform, BuildTarget target)
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
                        var info = sub.GetPass(p).CompileVariant(stage, Array.Empty<string>(), platform, target);
                        compiled++;
                        if (!info.Success)
                            foreach (var m in info.Messages) errors.AppendLine($"{stage}: {m.message} (line {m.line})");
                    }
            }
            if (errors.Length > 0) throw new InvalidOperationException($"BoardUnlit {platform} compile failed:\n" + errors);
            Debug.Log($"[GeoART] BoardUnlit compiled for {platform}/{target}: {compiled} stages OK.");
        }

        static void RenderBoard(string outDir)
        {
            // 1. Stroke anti-aliasing board (snapping off, so the raw test geometry is drawn as written).
            RenderScenario(outDir, "", DrawTestDesign, showBase: true, dark: false);
            // 2. Snapping on the sheet, and on a dark table with the base hidden (as in AR).
            //    A drag drops a pending tap-to-connect start, so the sheet board shows the hover ring of an open drag and the
            //    dark board shows the ring of a pending start.
            RenderScenario(outDir, "snap-", s => DrawSnapDesign(s, openDrag: true), showBase: true, dark: false);
            RenderScenario(outDir, "snap-dark-", s => DrawSnapDesign(s, openDrag: false), showBase: false, dark: true);
            // 3. Select: a live marquee over part of the design, so the PNG shows the box and the highlighted shapes.
            RenderScenario(outDir, "select-", DrawSelectDesign, showBase: true, dark: false);
            RenderScenario(outDir, "select-dark-", DrawSelectDesign, showBase: false, dark: true);
            // 4. Move and Scale: an open drag (no Up), so the ghosts move while the frame and pivot stay on the originals.
            RenderScenario(outDir, "xform-", s => DrawTransformDesign(s, ToolId.Move), showBase: true, dark: false);
            RenderScenario(outDir, "xform-dark-", s => DrawTransformDesign(s, ToolId.Move), showBase: false, dark: true);
            RenderScenario(outDir, "xform-scale-", s => DrawTransformDesign(s, ToolId.Scale), showBase: true, dark: false);
            // 5. Undo: the first Undo cancels the open green ghost and removes the last stroke (blue dotted circle), the
            //    second removes the tiny circle.
            RenderScenario(outDir, "undo-", s => { DrawTestDesign(s); s.Undo(); s.Undo(); }, showBase: true, dark: false);
            // 6. Bands, drawn from the engine geometry directly.
            RenderBands(outDir, "bands-", GoldenBoards.StarOnFullBoard, 0.015, false, false, true);
            RenderBands(outDir, "bands-stripped-", GoldenBoards.StarOnFullBoard, 0.015, false, false, false);
            RenderBands(outDir, "interlace-", GoldenBoards.StarOnFullBoard, 0.015, true, false, false);
            RenderBands(outDir, "interlace-inner-", GoldenBoards.StarOnFullBoard, 0.02, true, true, false);
            RenderBands(outDir, "bands-tiled-", GoldenBoards.Tiled2x2, 0.015, false, false, true);
            RenderBands(outDir, "interlace-tiled-", GoldenBoards.Tiled2x2, 0.02, true, true, false);
            // Interlace with the structure line shown: the under band's structure line is cut at every crossing.
            RenderBands(outDir, "interlace-structure-", GoldenBoards.StarOnFullBoard, 0.015, true, false, true);
            RenderBands(outDir, "interlace-inner-structure-", GoldenBoards.StarOnFullBoard, 0.025, true, true, true);
        }

        /// <summary>
        /// The board's top layer only (others hidden) with bands on (BoardView draws them through its BandCache):
        /// Double / Interlace, inner lines, structure shown or hidden. Logs the engine's safety numbers for the layer.
        /// </summary>
        static void RenderBands(string outDir, string prefix, Func<Board> make, double width, bool interlace, bool inner,
            bool structure)
        {
            Board shown = make();
            Layer layer = shown.Layers[shown.Layers.Count - 1];
            for (int i = 0; i < shown.Layers.Count - 1; i++) shown.Layers[i].Visible = false;
            layer.Bands = new LayerBands
            {
                On = true, Mode = interlace ? BandMode.Interlace : BandMode.Double, Width = width, HideStructure = !structure,
                InnerLines = inner, OutlineColor = Palette.Black, OutlineWidth = LineWidth.Thin,
            };
            var outline = BandOutline.Build(BandGraph.Build(layer.Shapes), BandOutlineOptions.ForBoard(layer.Bands, BoardGeometry.DefaultSideMeters));
            Debug.Log($"[GeoART] Bands {prefix}: {outline.Runs.Count} runs, {outline.InnerRuns.Count} inner, " +
                      $"{outline.Bridges.Count / 2} bridges, max safe {outline.MaxSafeWidth:0.####}, collapsed {outline.Collapsed}, " +
                      $"self-intersections {outline.SelfIntersections}, conflicts {outline.WeaveConflicts}, structure cuts {outline.StructureCuts.Count}");
            // Snapping off: no candidate dots on top of the crossings, like the book's drawings.
            RenderScenario(outDir, prefix, s => { s.LoadBoard(shown); s.SetSnapEnabled(false); }, showBase: true, dark: false,
                topView: true);
        }

        static void RenderScenario(string outDir, string prefix, Action<EditorSession> draw, bool showBase, bool dark,
            bool topView = false)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var boardGo = new GameObject("RenderCheckBoard");
            var camGo = new GameObject("RenderCheckCamera", typeof(Camera));
            try
            {
                var view = boardGo.AddComponent<BoardView>();
                var so = new SerializedObject(view);
                so.FindProperty("material").objectReferenceValue = material;
                so.FindProperty("showBase").boolValue = showBase;
                so.ApplyModifiedPropertiesWithoutUndo();
                Invoke(view, "Awake");

                var session = new EditorSession();
                draw(session);
                view.Bind(session);
                Invoke(view, "LateUpdate");

                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = dark ? new Color(0.12f, 0.11f, 0.1f)       // dark table
                                           : new Color(0.78f, 0.74f, 0.68f);     // wooden-table-ish
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 10f;
                cam.fieldOfView = 60f;

                Shoot(cam, new Vector3(0f, 0.36f, -0.3f), outDir, prefix + "near45cm");
                Shoot(cam, new Vector3(0f, 0.6f, -0.7f), outDir, prefix + "far90cm");
                if (topView)
                {
                    Shoot(cam, new Vector3(0f, 0.8f, -0.0001f), outDir, prefix + "top80cm");   // straight down
                    Shoot(cam, new Vector3(0f, 0.3f, -0.0001f), outDir, prefix + "top30cm");   // close-up of the centre
                }
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

        static void Drag(EditorSession s, double ax, double ay, double bx, double by)
        {
            s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(ax, ay), true, 0));
            s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(bx, by), true, 0.1));
            s.Pointer(new PointerEvent(PointerPhase.Up, new Vec2(bx, by), true, 0.2));
        }

        /// <summary>Every width solid and dotted, near-horizontal lines (worst case for aliasing), circles of several
        /// sizes, and an open Circle gesture so the ghost is drawn too. Snapping is off: this board checks strokes.</summary>
        static void DrawTestDesign(EditorSession s)
        {
            s.SetSnapEnabled(false);
            var widths = new[] { LineWidth.Thin, LineWidth.Medium, LineWidth.Thick };
            for (int w = 0; w < 3; w++)
            {
                s.Style = StrokeStyle.Default.WithWidth(widths[w]);
                Drag(s, 0.05, 0.92 - w * 0.05, 0.95, 0.93 - w * 0.05);              // nearly horizontal
                s.Style = s.Style.WithDash(LineStyle.Dotted);
                Drag(s, 0.05, 0.75 - w * 0.05, 0.95, 0.75 - w * 0.05);
                s.Style = s.Style.WithDash(LineStyle.Solid).WithColor(Palette.Red);
                Drag(s, 0.05 + w * 0.04, 0.05, 0.3 + w * 0.04, 0.55);                // steep
            }
            s.Style = StrokeStyle.Default;
            s.SetTool(ToolId.Circle);
            Drag(s, 0.5, 0.5, 0.95, 0.5);      // big circle
            Drag(s, 0.62, 0.3, 0.7, 0.3);
            Drag(s, 0.62, 0.3, 0.635, 0.3);    // tiny
            s.Style = StrokeStyle.Default.WithColor(Palette.Blue).WithDash(LineStyle.Dotted);
            Drag(s, 0.5, 0.5, 0.5, 0.82);
            s.Style = StrokeStyle.Default.WithColor(Palette.Green).WithWidth(LineWidth.Thick);
            s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.3, 0.3), true, 0));
            s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.42, 0.36), true, 0.1));   // ghost stays open
        }

        /// <summary>
        /// Snapping on (default), 2x2 square grid: a circle snapped to the board centre with its radius on an edge
        /// midpoint, a line from a cell centre to a top grid corner and one across the board, so grid intersections,
        /// shape intersections, endpoints and circle divisions all show. Then either an open Line drag snapped to a
        /// line/circle crossing (ghost + hover ring) or a pending tap-to-connect start (ring).
        /// </summary>
        static void DrawSnapDesign(EditorSession s, bool openDrag)
        {
            s.SetTool(ToolId.Circle);
            Drag(s, 0.503, 0.498, 0.505, 0.747);           // centre (0.5, 0.5), r = 0.25
            s.SetTool(ToolId.Line);
            Drag(s, 0.253, 0.247, 0.497, 0.996);           // (0.25, 0.25) -> (0.5, 1)
            Drag(s, 0.003, 0.502, 0.996, 0.249);           // (0, 0.5) -> (1, 0.25)
            if (openDrag)
            {
                s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.502, 0.253), true, 2));   // drag from (0.5, 0.25) ...
                s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.293, 0.364), true, 2.1)); // ... near a crossing
            }
            else
            {
                s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.748, 0.752), true, 1));   // tap on (0.75, 0.75)
                s.Pointer(new PointerEvent(PointerPhase.Up, new Vec2(0.748, 0.752), true, 1.05));
            }
        }

        /// <summary>
        /// Two layers: a black line, a dotted blue line and a red circle at the bottom; a thick green line and a big circle
        /// on top. Then the Select tool with an open marquee (Down and Moves, no Up) that encloses the black line, the
        /// dotted line and the red circle but only part of the big circle, which must stay unhighlighted.
        /// </summary>
        static void DrawSelectDesign(EditorSession s)
        {
            s.SetSnapEnabled(false);
            Drag(s, 0.12, 0.15, 0.42, 0.38);                                       // black line (in)
            s.Style = StrokeStyle.Default.WithColor(Palette.Blue).WithDash(LineStyle.Dotted);
            Drag(s, 0.12, 0.45, 0.45, 0.45);                                       // dotted blue line (in)
            s.SetTool(ToolId.Circle);
            s.Style = StrokeStyle.Default.WithColor(Palette.Red);
            Drag(s, 0.3, 0.62, 0.38, 0.62);                                        // red circle r 0.08 (in)
            s.AddLayer();
            s.Style = StrokeStyle.Default;
            Drag(s, 0.72, 0.6, 0.92, 0.6);                                         // big circle r 0.2 (partly in: out)
            s.SetTool(ToolId.Line);
            s.Style = StrokeStyle.Default.WithColor(Palette.Green).WithWidth(LineWidth.Thick);
            Drag(s, 0.62, 0.12, 0.9, 0.2);                                         // thick green line (outside: out)
            s.SetTool(ToolId.SelectMove);
            s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.06, 0.08), true, 3));
            s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.3, 0.4), true, 3.1));
            s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.58, 0.76), true, 3.2));   // box stays open
        }

        /// <summary>
        /// A black line and a red circle (selected by a marquee) and a blue line (not selected), drawn with snapping off,
        /// then snapping on and an open drag in <paramref name="tool"/>. Move grabs the black line's upper end and drags
        /// it onto the grid corner (0.5, 0.5) (hover ring); Scale drags outward from right of the pivot (about 1.5x).
        /// </summary>
        static void DrawTransformDesign(EditorSession s, ToolId tool)
        {
            s.SetSnapEnabled(false);
            Drag(s, 0.15, 0.2, 0.35, 0.35);                                        // black line (selected)
            s.Style = StrokeStyle.Default.WithColor(Palette.Blue);
            Drag(s, 0.6, 0.15, 0.85, 0.4);                                         // blue line (not selected)
            s.SetTool(ToolId.Circle);
            s.Style = StrokeStyle.Default.WithColor(Palette.Red);
            Drag(s, 0.3, 0.6, 0.38, 0.6);                                          // red circle r 0.08 (selected)
            s.Style = StrokeStyle.Default;
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.05, 0.1, 0.45, 0.75);
            s.SetSnapEnabled(true);
            s.SetTool(tool);
            if (tool == ToolId.Scale)
            {
                s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.4, 0.44), true, 5));
                s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.44, 0.44), true, 5.1));
                s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.4675, 0.44), true, 5.2));   // stays open
            }
            else
            {
                s.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.36, 0.355), true, 5));   // near (0.35, 0.35)
                s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.42, 0.43), true, 5.1));
                s.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.507, 0.512), true, 5.2)); // end on (0.5, 0.5)
            }
        }

        static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(target, null);
    }
}
