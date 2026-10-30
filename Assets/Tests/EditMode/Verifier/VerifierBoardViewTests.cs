using System.Diagnostics;
using System.Reflection;
using GeoART.BoardRender;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using EntityId = GeoART.Contracts.EntityId;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// VERIFIER: BoardView builds meshes for the engine's lines/circles (solid and dotted) without hanging, honours
    /// hidden layers and opacity, and the new stroke widths are thinner than the pre-work ones.
    /// Edit Mode does not run Awake/LateUpdate for plain MonoBehaviours, so they are invoked by reflection.
    /// </summary>
    public sealed class VerifierBoardViewTests
    {
        GameObject _go;
        BoardView _view;
        EditorSession _session;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession();
            _go = new GameObject("VerifierBoardView");
            _view = _go.AddComponent<BoardView>();
            Call(_view, "Awake");
            _view.Bind(_session);
        }

        [TearDown]
        public void TearDown()
        {
            // BoardView.OnDestroy uses Object.Destroy (play mode only); free its meshes/materials directly instead.
            if (_go != null)
            {
                foreach (var mf in _go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
                foreach (var mr in _go.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial != null) Object.DestroyImmediate(mr.sharedMaterial);
                Object.DestroyImmediate(_go);
            }
            ServiceHub.Clear();
        }

        static void Call(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        Mesh PartMesh(string part)
        {
            Transform t = _go.transform.Find(part);
            Assert.NotNull(t, $"BoardView child '{part}' missing");
            var mf = t.GetComponent<MeshFilter>();
            Assert.NotNull(mf);
            return mf.sharedMesh;
        }

        Mesh Render()
        {
            var sw = Stopwatch.StartNew();
            _view.MarkAllDirty();
            Call(_view, "LateUpdate");
            Assert.Less(sw.ElapsedMilliseconds, 2000, "BoardView rebuild took > 2 s (pattern loop?)");
            return PartMesh("Shapes");
        }

        void AddLine(Vec2 a, Vec2 b, StrokeStyle style) =>
            _session.ActiveLayer.Shapes.Add(new LineShape(_session.Board.NewId(), a, b, style));

        void AddCircle(Vec2 c, double r, StrokeStyle style) =>
            _session.ActiveLayer.Shapes.Add(new CircleShape(_session.Board.NewId(), c, r, style));

        [Test]
        public void EmptyBoardHasNoShapeVertices()
        {
            Assert.AreEqual(0, Render().vertexCount);
        }

        [Test]
        public void SolidLineBuildsThinRibbon()
        {
            AddLine(new Vec2(0.1, 0.5), new Vec2(0.9, 0.5), StrokeStyle.Default);
            Mesh m = Render();
            Assert.Greater(m.vertexCount, 0, "line has vertices");
            // Horizontal line along local X; ribbon thickness is the Z extent. Pre-work Medium was 3.2 mm at 0.40 m.
            float side = _view.SideMeters;
            float thickness = m.bounds.size.z;
            float length = m.bounds.size.x;
            Assert.Greater(length, 0.75f * side, "line spans the board");
            Assert.LessOrEqual(thickness, 0.0026f * side / 0.40f + 1e-5f,
                $"Medium line should be roughly half the old 3.2 mm (got {thickness * 1000f:0.###} mm incl. any AA fringe)");
        }

        [Test]
        public void WidthsAreOrderedThinMediumThick()
        {
            float[] t = new float[3];
            LineWidth[] widths = { LineWidth.Thin, LineWidth.Medium, LineWidth.Thick };
            for (int i = 0; i < 3; i++)
            {
                _session.ActiveLayer.Shapes.Clear();
                AddLine(new Vec2(0.1, 0.5), new Vec2(0.9, 0.5), StrokeStyle.Default.WithWidth(widths[i]));
                t[i] = Render().bounds.size.z;
            }
            Assert.Less(t[0], t[1], "thin < medium");
            Assert.Less(t[1], t[2], "medium < thick");
            Assert.LessOrEqual(t[2], 0.0055f * _view.SideMeters / 0.40f, "thick no wider than the old thick");
        }

        [Test]
        public void CircleBuildsClosedRing()
        {
            AddCircle(new Vec2(0.5, 0.5), 0.3, StrokeStyle.Default);
            Mesh m = Render();
            Assert.Greater(m.vertexCount, 0);
            float d = 0.6f * _view.SideMeters;
            Assert.AreEqual(d, m.bounds.size.x, 0.01f * _view.SideMeters, "ring diameter x");
            Assert.AreEqual(d, m.bounds.size.z, 0.01f * _view.SideMeters, "ring diameter z");
        }

        [Test]
        public void TinyAndHugeCirclesDoNotHang()
        {
            AddCircle(new Vec2(0.5, 0.5), 0.011, StrokeStyle.Default);
            AddCircle(new Vec2(0.5, 0.5), 0.011, StrokeStyle.Default.WithDash(LineStyle.Dotted));
            AddCircle(new Vec2(0.5, 0.5), 5.0, StrokeStyle.Default);
            AddCircle(new Vec2(0.5, 0.5), 5.0, StrokeStyle.Default.WithDash(LineStyle.Dotted));
            Mesh m = Render();
            Assert.Greater(m.vertexCount, 0);
            Assert.Less(m.vertexCount, 400000, "segment count stays bounded");
        }

        [Test]
        public void DottedLineAndCircleBuildDashes()
        {
            AddLine(new Vec2(0.1, 0.5), new Vec2(0.9, 0.5), StrokeStyle.Default);
            int solid = Render().vertexCount;
            _session.ActiveLayer.Shapes.Clear();
            AddLine(new Vec2(0.1, 0.5), new Vec2(0.9, 0.5), StrokeStyle.Default.WithDash(LineStyle.Dotted));
            int dotted = Render().vertexCount;
            Assert.Greater(dotted, solid * 4, $"dotted line is made of many dashes (solid {solid}, dotted {dotted})");
            AddCircle(new Vec2(0.5, 0.5), 0.3, StrokeStyle.Default.WithDash(LineStyle.Dotted));
            Assert.Greater(Render().vertexCount, dotted, "dotted circle adds dashes");
        }

        [Test]
        public void DashedSegmentTerminatesForAwkwardLengths()
        {
            // Regression guard for the old float-accumulation freeze (8 mm / 6 mm at t = 0.022).
            // Reflection so a signature change in the builders' file cannot break compilation of this test assembly.
            var mb = new BoardMeshBuilder();
            MethodInfo m = typeof(BoardMeshBuilder).GetMethod("AddDashedSegment");
            Assert.NotNull(m, "BoardMeshBuilder.AddDashedSegment exists");
            Assert.AreEqual(7, m.GetParameters().Length, "AddDashedSegment(a, b, width, color, dash, gap, ref phase)");
            var sw = Stopwatch.StartNew();
            var black = new Color32(0, 0, 0, 255);
            for (int i = 1; i < 2000; i++)
            {
                object[] args = { Vector3.zero, new Vector3(0.0001f * i, 0f, 0f), 0.001f, black, 0.008f, 0.006f, 0.022f * (i % 7) };
                m.Invoke(mb, args);
            }
            m.Invoke(mb, new object[] { Vector3.zero, new Vector3(1e6f, 0f, 0f), 0.001f, black, 1e-9f, 1e-9f, 0f });
            Assert.Less(sw.ElapsedMilliseconds, 2000, "dashed segments terminate");
        }

        [Test]
        public void HiddenLayerDrawsNothingAndOpacityScalesAlpha()
        {
            AddLine(new Vec2(0.1, 0.5), new Vec2(0.9, 0.5), StrokeStyle.Default);
            EntityId id = _session.ActiveLayer.Id;
            Assert.Greater(Render().vertexCount, 0);

            _session.SetLayerVisible(id, false);
            Call(_view, "LateUpdate");
            Assert.AreEqual(0, PartMesh("Shapes").vertexCount, "hidden layer not drawn (event-driven rebuild)");

            _session.SetLayerVisible(id, true);
            _session.SetLayerOpacity(id, 0.5);
            Call(_view, "LateUpdate");
            Mesh m = PartMesh("Shapes");
            Assert.Greater(m.vertexCount, 0);
            Color32[] colors = m.colors32;
            Assert.Greater(colors.Length, 0, "vertex colours present");
            int maxA = 0;
            foreach (var c in colors) maxA = Mathf.Max(maxA, c.a);
            Assert.AreEqual(128, maxA, 3, "50% opacity halves the stroke alpha");
        }

        [Test]
        public void PreviewGhostDrawnInOverlay()
        {
            Call(_view, "LateUpdate");
            int before = PartMesh("Overlay").vertexCount;
            _session.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.2, 0.2), true, 1.0));
            _session.Pointer(new PointerEvent(PointerPhase.Move, new Vec2(0.7, 0.6), true, 1.1));
            Call(_view, "LateUpdate");
            Assert.Greater(_session.Preview.Count, 0, "engine has a ghost");
            Assert.Greater(PartMesh("Overlay").vertexCount, before, "ghost drawn in the overlay");
            _session.Pointer(new PointerEvent(PointerPhase.Up, new Vec2(0.7, 0.6), true, 1.2));
            Call(_view, "LateUpdate");
            Assert.Greater(PartMesh("Shapes").vertexCount, 0, "committed line drawn");
        }
    }
}
