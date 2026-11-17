using System;
using System.Collections.Generic;
using System.Reflection;
using GeoART.App;
using GeoART.BoardRender;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EntityId = GeoART.Contracts.EntityId;
using Object = UnityEngine.Object;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// VERIFIER: the Select tool's view side. BoardView's "Selection" part (empty without a selection, drawn live and
    /// after a marquee, cleared by a tool switch, hidden layers skipped, halo under the shape's own stroke, render queues
    /// Base 3000 &lt; Shapes &lt; Selection &lt; Candidates &lt; Overlay 3004), and the ToolBar's Select button (third,
    /// sets SelectMove, orange while active, all nine buttons inside the canvas, every glyph texture distinct).
    /// Edit Mode does not run Awake/Start/LateUpdate for plain MonoBehaviours, so they are invoked by reflection.
    /// </summary>
    public sealed class VerifierSelectViewTests
    {
        sealed class Space : IBoardSpace
        {
            public bool Placed;
            public PlacementState State => Placed ? PlacementState.Placed : PlacementState.Scanning;
            public event Action<PlacementState> StateChanged;
            public bool IsPlaced => Placed;
            public void PlaceAtReticle() { Placed = true; StateChanged?.Invoke(State); }
            public void Reset() { Placed = false; StateChanged?.Invoke(State); }
            public bool TryScreenToBoard(Vec2 px, out Vec2 b) { b = default; return false; }
            public bool TryBoardToScreen(Vec2 b, out Vec2 px) { px = default; return false; }
        }

        static readonly string[] ButtonOrder = { "Line", "Circle", "Select", "Dotted", "Medium", "Square", "2x2", "Snap", "HV" };

        EditorSession _session;
        Space _space;
        GameObject _viewGo, _appGo, _esGo;
        BoardView _view;
        EventSystem _es;
        double _t;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession();
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
        }

        [TearDown]
        public void TearDown()
        {
            if (_viewGo != null)
            {
                // BoardView.OnDestroy uses Object.Destroy (play mode only); free its meshes/materials directly instead.
                foreach (var mf in _viewGo.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
                foreach (var mr in _viewGo.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial != null) Object.DestroyImmediate(mr.sharedMaterial);
                Object.DestroyImmediate(_viewGo);
            }
            if (_appGo != null) Object.DestroyImmediate(_appGo);
            if (_esGo != null) { EsEnable(_es, false); Object.DestroyImmediate(_esGo); }
            ServiceHub.Clear();
        }

        static void Call(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        static void EsEnable(EventSystem es, bool on)
        {
            if (es == null) return;
            var m = typeof(EventSystem).GetMethod(on ? "OnEnable" : "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(es, null);
        }

        // ---------- BoardView helpers ----------

        void MakeView()
        {
            _viewGo = new GameObject("VerifierSelectView");
            _view = _viewGo.AddComponent<BoardView>();
            Call(_view, "Awake");
            _view.Bind(_session);
        }

        Transform Part(string name)
        {
            Transform t = _viewGo.transform.Find(name);
            Assert.NotNull(t, $"BoardView child '{name}' missing");
            return t;
        }

        Mesh PartMesh(string name)
        {
            var mf = Part(name).GetComponent<MeshFilter>();
            Assert.NotNull(mf, $"{name} has a MeshFilter");
            return mf.sharedMesh;
        }

        /// <summary>LateUpdate only: the Selection part must be dirtied by the session's events, not by a forced rebuild.</summary>
        int SelectionVerts()
        {
            Call(_view, "LateUpdate");
            return PartMesh("Selection").vertexCount;
        }

        void Ptr(PointerPhase phase, double x, double y) =>
            _session.Pointer(new PointerEvent(phase, new Vec2(x, y), true, _t += 0.016));

        void Marquee(double ax, double ay, double bx, double by)
        {
            Ptr(PointerPhase.Down, ax, ay);
            for (int i = 1; i <= 6; i++)
                Ptr(PointerPhase.Move, ax + (bx - ax) * i / 6.0, ay + (by - ay) * i / 6.0);
            Ptr(PointerPhase.Up, bx, by);
        }

        LineShape AddLine(Layer layer, Vec2 a, Vec2 b, StrokeStyle style)
        {
            var l = new LineShape(_session.Board.NewId(), a, b, style);
            layer.Shapes.Add(l);
            return l;
        }

        static bool IsCyanHalo(Color32 c) => c.b > 200 && c.g > 120 && c.r < 100;

        // ---------- BoardView tests ----------

        [Test]
        public void SelectionPartSitsBetweenShapesAndCandidates()
        {
            MakeView();
            string[] parts = { "Base", "Shapes", "Selection", "Candidates", "Overlay" };
            var mr = Part("Shapes").GetComponent<MeshRenderer>();
            if (mr.sharedMaterial == null) Assert.Ignore("GeoART/BoardUnlit shader not available: render queues not testable");
            for (int i = 0; i < parts.Length; i++)
            {
                var m = Part(parts[i]).GetComponent<MeshRenderer>().sharedMaterial;
                Assert.NotNull(m, $"{parts[i]} material");
                Assert.AreEqual(3000 + i, m.renderQueue, $"{parts[i]} render queue");
            }
        }

        [Test]
        public void SelectionMeshFollowsTheSelection()
        {
            MakeView();
            AddLine(_session.ActiveLayer, new Vec2(0.2, 0.5), new Vec2(0.4, 0.5), StrokeStyle.Default);
            AddLine(_session.ActiveLayer, new Vec2(0.6, 0.8), new Vec2(0.9, 0.8), StrokeStyle.Default);
            _view.MarkAllDirty();
            Assert.AreEqual(0, SelectionVerts(), "no selection: empty Selection mesh");
            Assert.Greater(PartMesh("Shapes").vertexCount, 0, "the lines themselves are drawn");

            _session.SetTool(ToolId.SelectMove);
            Assert.AreEqual(0, SelectionVerts(), "entering Select selects nothing");

            // Live: Down and Moves, no Up yet.
            Ptr(PointerPhase.Down, 0.1, 0.4);
            Ptr(PointerPhase.Move, 0.3, 0.55);
            Ptr(PointerPhase.Move, 0.45, 0.6);
            Assert.AreEqual(1, _session.Selection.Ids.Count, "precondition: live selection");
            Assert.Greater(SelectionVerts(), 0, "highlight drawn live during the drag");
            Ptr(PointerPhase.Up, 0.45, 0.6);
            int verts = SelectionVerts();
            Assert.Greater(verts, 0, "highlight kept after Up");

            // Only the selected line: everything left of the board centre (x < 0 in local space).
            Mesh m = PartMesh("Selection");
            Assert.Less(m.bounds.max.x, 0f, "only the left line is highlighted");
            float side = _view.SideMeters;
            Assert.AreEqual(0.2f * side, m.bounds.size.x, 0.02f * side, "highlight spans the selected line");

            // Halo first (cyan, wider than the stroke), then the shape's own stroke on top.
            Color32[] colors = m.colors32;
            Assert.Greater(colors.Length, 0);
            Assert.IsTrue(IsCyanHalo(colors[0]), $"first pass is the cyan halo, got {colors[0]}");
            var ink = new Color32(Palette.Black.R, Palette.Black.G, Palette.Black.B, Palette.Black.A);
            Assert.AreEqual(ink, colors[colors.Length - 1], "last pass is the shape's own colour");

            _session.SetTool(ToolId.Line);
            Assert.AreEqual(0, SelectionVerts(), "Line clears the selection and the highlight");
        }

        [Test]
        public void HiddenLayerSelectedShapeIsNotDrawn()
        {
            MakeView();
            AddLine(_session.ActiveLayer, new Vec2(0.2, 0.5), new Vec2(0.8, 0.5), StrokeStyle.Default);
            _session.SetTool(ToolId.SelectMove);
            Marquee(0.1, 0.4, 0.9, 0.6);
            Assert.Greater(SelectionVerts(), 0, "precondition: highlighted");

            // Direct model edit (no event, so the engine has not pruned yet): BoardView itself must skip hidden layers.
            _session.ActiveLayer.Visible = false;
            _view.MarkAllDirty();
            Assert.AreEqual(1, _session.Selection.Ids.Count, "precondition: still selected in the model");
            Assert.AreEqual(0, SelectionVerts(), "hidden layer: nothing highlighted");
            _session.ActiveLayer.Visible = true;
            _view.MarkAllDirty();
            Assert.Greater(SelectionVerts(), 0, "shown again");

            EntityId id = _session.ActiveLayer.Id;
            _session.SetLayerVisible(id, false);
            Assert.AreEqual(0, SelectionVerts(), "hidden through the session: pruned, nothing drawn");
            _session.SetLayerVisible(id, true);
            Assert.AreEqual(0, SelectionVerts(), "not re-selected when shown again");
        }

        [Test]
        public void HaloIsSolidForDottedShapes()
        {
            MakeView();
            Vec2 a = new Vec2(0.2, 0.5), b = new Vec2(0.8, 0.5);
            AddLine(_session.ActiveLayer, a, b, StrokeStyle.Default);
            _view.MarkAllDirty();
            Call(_view, "LateUpdate");
            int solidStroke = PartMesh("Shapes").vertexCount;

            _session.ActiveLayer.Shapes.Clear();
            AddLine(_session.ActiveLayer, a, b, StrokeStyle.Default.WithDash(LineStyle.Dotted));
            _session.SetTool(ToolId.SelectMove);
            Marquee(0.1, 0.4, 0.9, 0.6);
            _view.MarkAllDirty();
            Call(_view, "LateUpdate");
            int dottedStroke = PartMesh("Shapes").vertexCount;
            int selection = PartMesh("Selection").vertexCount;
            Assert.Greater(dottedStroke, solidStroke * 4, "precondition: the dotted line is many dashes");
            Assert.AreEqual(solidStroke, selection - dottedStroke,
                $"halo = one solid ribbon ({solidStroke} verts), then the dotted stroke (selection {selection}, dotted {dottedStroke})");

            Mesh shapes = PartMesh("Shapes"), sel = PartMesh("Selection");
            Assert.Greater(sel.bounds.size.z, shapes.bounds.size.z + 0.001f * _view.SideMeters / 0.40f,
                "the halo is wider than the stroke (horizontal line: thickness is the Z extent)");
            Assert.Greater(sel.bounds.center.y, shapes.bounds.center.y, "lifted above the Shapes part");
            Assert.Less(sel.bounds.max.y, 0.0016f + 1e-5f, "below the Overlay lift");
        }

        // ---------- ToolBar helpers ----------

        void MakeToolBar()
        {
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
            _appGo = new GameObject("App");
            var tb = _appGo.AddComponent<ToolBar>();
            Call(tb, "Awake");
            Call(tb, "Start");
            _space.PlaceAtReticle();
        }

        GameObject Find(string name)
        {
            foreach (var t in _appGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        Color Bg(string name) => Find(name).GetComponent<Image>().color;

        void Click(string name) =>
            ExecuteEvents.Execute(Find(name), new PointerEventData(_es) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);

        // ---------- ToolBar tests ----------

        [Test]
        public void SelectButtonSetsSelectMoveAndIsHighlighted()
        {
            MakeToolBar();
            var toasts = new List<string>();
            _session.Toast += t => toasts.Add(t);
            Color accent = Bg("Line");     // Line is the default tool
            Color idle = Bg("Circle");
            Assert.AreNotEqual(accent, idle, "precondition: active tool highlighted");
            Assert.AreEqual(idle, Bg("Select"), "Select idle at start");
            Assert.IsTrue(Find("Select").GetComponent<Graphic>().raycastTarget, "Select is a raycast target");

            Click("Select");
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, "Select button sets SelectMove");
            Assert.AreEqual(accent, Bg("Select"), "Select highlighted");
            Assert.AreEqual(idle, Bg("Line"), "Line idle");
            Assert.AreEqual(idle, Bg("Circle"), "Circle idle");
            CollectionAssert.Contains(toasts, EditorSession.SelectHintToast, "hint toast on entering Select");

            Click("Line");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);
            Assert.AreEqual(idle, Bg("Select"), "Select idle again");

            _session.SetTool(ToolId.SelectMove);   // from elsewhere: refreshes too
            Assert.AreEqual(accent, Bg("Select"));
            Click("Circle");
            Assert.AreEqual(idle, Bg("Select"));
            Assert.AreEqual(accent, Bg("Circle"));
        }

        [Test]
        public void NineButtonsFitInsideCanvasInOrder()
        {
            MakeToolBar();
            Canvas.ForceUpdateCanvases();
            var canvas = _appGo.GetComponentInChildren<Canvas>(true);
            Vector3[] cc = new Vector3[4], bc = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(cc);
            var buttons = new List<Button>(_appGo.GetComponentsInChildren<Button>(true));
            Assert.AreEqual(9, buttons.Count, "nine buttons");

            float prevBottom = float.MaxValue;
            var tops = new float[ButtonOrder.Length];
            var bottoms = new float[ButtonOrder.Length];
            for (int i = 0; i < ButtonOrder.Length; i++)
            {
                string n = ButtonOrder[i];
                ((RectTransform)Find(n).transform).GetWorldCorners(bc);
                Assert.GreaterOrEqual(bc[0].y, cc[0].y, $"{n} above the bottom edge");
                Assert.LessOrEqual(bc[2].y, cc[2].y, $"{n} below the top edge");
                Assert.LessOrEqual(bc[2].x, cc[2].x, $"{n} inside the right edge");
                Assert.GreaterOrEqual(bc[0].x, cc[0].x, $"{n} inside the left edge");
                Assert.LessOrEqual(bc[2].y, prevBottom + 0.01f, $"{n} is below the previous button, no overlap (column order)");
                tops[i] = bc[2].y;
                bottoms[i] = bc[0].y;
                prevBottom = bc[0].y;
            }
            float inPair = bottoms[1] - tops[2];      // Circle -> Select: same group
            float betweenGroups = bottoms[2] - tops[3];   // Select -> Dotted: next group
            Assert.Greater(betweenGroups, inPair * 1.3f, $"Line, Circle, Select form one group (gaps {inPair} and {betweenGroups})");

            // Unless the column had to shrink to its minimum, it stays between the placement row and the coach text.
            float h = cc[2].y - cc[0].y;
            if (Find("Column").transform.localScale.y > 0.41f)
            {
                Assert.GreaterOrEqual(bottoms[ButtonOrder.Length - 1], cc[0].y + 0.11f * h - 1f, "clear of the placement row");
                Assert.LessOrEqual(tops[0], cc[0].y + 0.80f * h + 1f, "clear of the coach text");
            }
        }

        [Test]
        public void IconsAre28AndEveryGlyphTextureIsDistinct()
        {
            MakeToolBar();
            var tb = _appGo.GetComponent<ToolBar>();
            var icons = new List<Texture>();
            foreach (var n in ButtonOrder)
            {
                var icon = Find(n).transform.Find("Icon");
                Assert.NotNull(icon, $"{n} has an Icon");
                var rt = (RectTransform)icon;
                Assert.AreEqual(new Vector2(28f, 28f), rt.sizeDelta, $"{n} icon 28x28");
                Assert.AreEqual(new Vector2(0f, -3f), rt.anchoredPosition, $"{n} icon at (0, -3)");
                var tex = icon.GetComponent<RawImage>().texture;
                Assert.NotNull(tex, $"{n} icon texture");
                CollectionAssert.DoesNotContain(icons, tex, $"{n} icon texture is its own");
                icons.Add(tex);
            }

            // The rounded button sprite lives after the glyphs in the texture table (index GlyphCount). If GlyphCount was
            // not bumped for the Select glyph, one overwrites the other and a texture is never destroyed.
            var f = typeof(ToolBar).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f, "ToolBar._textures (texture table freed in OnDestroy)");
            var table = new List<Texture2D>((Texture2D[])f.GetValue(tb));
            Texture rounded = Find("Line").GetComponent<Image>().sprite.texture;
            Assert.NotNull(rounded);
            CollectionAssert.DoesNotContain(icons, rounded, "the rounded sprite is not an icon");
            CollectionAssert.Contains(table, rounded, "rounded sprite texture is in the table (freed on destroy)");
            foreach (var t in icons) CollectionAssert.Contains(table, t, $"icon {t.name} is in the table (freed on destroy)");
            var distinct = new HashSet<Texture2D>();
            foreach (var t in table) if (t != null) distinct.Add(t);
            Assert.GreaterOrEqual(distinct.Count, 11, "10 glyphs (incl. Hex) + the rounded sprite, all distinct");

            foreach (var t in table) if (t != null) Object.DestroyImmediate(t);   // OnDestroy does not run in Edit Mode
        }
    }
}
