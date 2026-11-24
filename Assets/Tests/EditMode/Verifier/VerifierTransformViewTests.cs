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
    /// VERIFIER: the view side of Move / Scale / Duplicate (p3-transform-briefs B1.9, B1.10), written from the spec.
    /// BoardView: the "SelectionFrame" part (queue 3005 after the unchanged 3000..3004; empty without a selection; a cyan
    /// dashed box padded around the union bounds plus the pivot mark centred on it; static while a drag shows the orange
    /// ghosts; hidden layers excluded) and an unchanged Selection part (halo + stroke only).
    /// ToolBar: the contextual action bar (Move | Scale | Duplicate | Deselect): visibility rules, mode toggles and
    /// highlights, Deselect, the column's Select highlight, looks, and layout (one row, inside the canvas, left of the
    /// column, above the placement row, shrink never below MinFit, the spec placement formula at the batch screen, and
    /// the same formula evaluated for 390x844 and iPhone 14 Pro Max portrait).
    /// Edit Mode does not run Awake/Start/LateUpdate for plain MonoBehaviours, so they are invoked by reflection.
    /// </summary>
    public sealed class VerifierTransformViewTests
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

        static readonly string[] BarOrder = { "Move", "Scale", "Duplicate", "Delete", "Deselect" };
        static readonly string[] ColumnOrder = { "Line", "Circle", "Select", "Fill", "Bands", "Medium", "Square", "2x2", "Snap" };

        // Spec constants (ToolBar / BoardView), restated here so the tests do not read them from the code under test.
        const float BarButtonW = 56f, BarButtonH = 52f, BarSpacing = 6f, BarWidth = 304f;   // 5 buttons since Delete (P7)
        const float ButtonW = 56f, EdgeMargin = 8f, PlacementRowTop = 0.11f, MinFit = 0.4f;
        const float SelectionHaloMeters = 0.0018f, FrameStroke = 0.0009f, SelectionLift = 0.0010f;

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
            _session.SetSnapEnabled(false);
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
        }

        [TearDown]
        public void TearDown()
        {
            if (_viewGo != null)
            {
                foreach (var mf in _viewGo.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
                foreach (var mr in _viewGo.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial != null) Object.DestroyImmediate(mr.sharedMaterial);
                Object.DestroyImmediate(_viewGo);
            }
            if (_appGo != null)
            {
                // ToolBar.OnDestroy uses Object.Destroy (play mode only): free the glyph textures and sprite directly.
                var tb = _appGo.GetComponent<ToolBar>();
                var f = typeof(ToolBar).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic);
                if (tb != null && f != null && f.GetValue(tb) is Texture2D[] table)
                    foreach (var t in table) if (t != null) Object.DestroyImmediate(t);
                Object.DestroyImmediate(_appGo);
            }
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

        LineShape AddLine(double ax, double ay, double bx, double by, StrokeStyle? style = null)
        {
            var l = new LineShape(_session.Board.NewId(), new Vec2(ax, ay), new Vec2(bx, by), style ?? StrokeStyle.Default);
            _session.ActiveLayer.Shapes.Add(l);
            return l;
        }

        void SelectShapes(params Shape[] shapes)
        {
            _session.SetTool(ToolId.SelectMove);
            var ids = new List<EntityId>();
            foreach (var s in shapes) ids.Add(s.Id);
            _session.SetSelection(ids);
            Assert.AreEqual(shapes.Length, _session.SelectionCount, "precondition: selected");
        }

        void Ptr(PointerPhase phase, double x, double y) =>
            _session.Pointer(new PointerEvent(phase, new Vec2(x, y), true, _t += 0.016));

        // ---------- BoardView helpers ----------

        void MakeView()
        {
            _viewGo = new GameObject("VerifierTransformView");
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

        /// <summary>LateUpdate only: the parts must be dirtied by the session's events, not by a forced rebuild.</summary>
        Mesh Rebuilt(string name)
        {
            Call(_view, "LateUpdate");
            return PartMesh(name);
        }

        Vector3 Local(double x, double y) => BoardGeometry.BoardToLocal(new Vec2(x, y), _view.SideMeters);

        static bool IsCyan(Color32 c) => c.b > 200 && c.g > 120 && c.r < 100;

        // ---------- BoardView tests ----------

        [Test]
        public void SelectionFrameQueue3005AfterTheUnchangedParts()
        {
            MakeView();
            string[] parts = { "Base", "Shapes", "Selection", "Candidates", "Overlay", "SelectionFrame" };
            if (Part("Shapes").GetComponent<MeshRenderer>().sharedMaterial == null)
                Assert.Ignore("GeoART/BoardUnlit shader not available: render queues not testable");
            for (int i = 0; i < parts.Length; i++)
            {
                var m = Part(parts[i]).GetComponent<MeshRenderer>().sharedMaterial;
                Assert.NotNull(m, $"{parts[i]} material");
                Assert.AreEqual(3000 + i, m.renderQueue, $"{parts[i]} render queue");
            }
        }

        [Test]
        public void FrameEmptyWithoutSelectionAndFramesTheUnionBoundsWithOne()
        {
            MakeView();
            var line = AddLine(0.2, 0.3, 0.6, 0.7);
            AddLine(0.7, 0.1, 0.9, 0.2);
            _view.MarkAllDirty();
            Assert.AreEqual(0, Rebuilt("SelectionFrame").vertexCount, "no selection: empty frame");
            _session.SetTool(ToolId.SelectMove);
            Assert.AreEqual(0, Rebuilt("SelectionFrame").vertexCount, "Select tool alone: empty frame");

            SelectShapes(line);
            Mesh m = Rebuilt("SelectionFrame");
            Assert.Greater(m.vertexCount, 0, "a selection draws the frame and pivot mark");

            float side = _view.SideMeters, scale = BoardView.StrokeScaleFor(side);
            Vector3 c = Local(0.4, 0.5);   // centre of the union bounds = the engine's pivot
            Assert.AreEqual(c.x, m.bounds.center.x, 0.0003f * scale, "frame centred on the pivot (x)");
            Assert.AreEqual(c.z, m.bounds.center.z, 0.0003f * scale, "frame centred on the pivot (z)");
            float expected = 0.4f * side + 2f * SelectionHaloMeters * scale + FrameStroke * scale;
            Assert.AreEqual(expected, m.bounds.size.x, 0.0006f * scale, "box = bounds + halo padding + stroke (x)");
            Assert.AreEqual(expected, m.bounds.size.z, 0.0006f * scale, "box = bounds + halo padding + stroke (z)");
            Assert.AreEqual(SelectionLift, m.bounds.center.y, 1e-5f, "drawn at SelectionLift");
            Assert.Less(m.bounds.size.y, 1e-5f, "flat");
            foreach (var col in m.colors32) Assert.IsTrue(IsCyan(col), $"frame and pivot are cyan, got {col}");

            _session.SetTool(ToolId.Move);
            Assert.Greater(Rebuilt("SelectionFrame").vertexCount, 0, "still drawn in Move");
            _session.SetTool(ToolId.Line);
            Assert.AreEqual(0, Rebuilt("SelectionFrame").vertexCount, "Line clears the selection: frame gone");
        }

        [Test]
        public void PivotMarkSitsAtTheBoundsCentreForAThinSelection()
        {
            MakeView();
            var line = AddLine(0.2, 0.5, 0.8, 0.5);   // zero height: the vertical extent is the pivot mark's, centred on it
            SelectShapes(line);
            Mesh m = Rebuilt("SelectionFrame");
            float scale = BoardView.StrokeScaleFor(_view.SideMeters);
            Vector3 c = Local(0.5, 0.5);
            Assert.AreEqual(c.x, m.bounds.center.x, 0.0003f * scale, "centred on the pivot (x)");
            Assert.AreEqual(c.z, m.bounds.center.z, 0.0003f * scale, "centred on the pivot (z)");
            Assert.GreaterOrEqual(m.bounds.size.z, 2f * 0.0045f * scale, "the pivot ring (r 4.5 mm) is drawn");
            Assert.LessOrEqual(m.bounds.size.z, 2f * 0.0075f * scale, "nothing but the mark sticks out of a flat box");
        }

        [Test]
        public void FrameStaysOnTheOriginalsDuringADragAndFollowsAfterUp()
        {
            MakeView();
            var line = AddLine(0.25, 0.25, 0.5, 0.375);
            SelectShapes(line);
            _session.SetTool(ToolId.Move);
            Mesh m = Rebuilt("SelectionFrame");
            Bounds before = m.bounds;
            Assert.AreEqual(0, Rebuilt("Overlay").vertexCount, "no ghost before the drag");

            Ptr(PointerPhase.Down, 0.75, 0.75);
            Ptr(PointerPhase.Move, 0.78, 0.75);
            Ptr(PointerPhase.Move, 0.8125, 0.75);
            Assert.Greater(Rebuilt("Overlay").vertexCount, 0, "orange ghost and cursor in the Overlay while dragging");
            Bounds during = PartMesh("SelectionFrame").bounds;
            Assert.AreEqual(before.center, during.center, "the frame and pivot stay at the originals during the drag");
            Assert.AreEqual(before.size, during.size);

            Ptr(PointerPhase.Up, 0.8125, 0.75);
            Bounds after = Rebuilt("SelectionFrame").bounds;
            Assert.AreEqual(before.center.x + 0.0625f * _view.SideMeters, after.center.x, 1e-5f, "after Up the frame follows the moved shape");
            Assert.AreEqual(before.center.z, after.center.z, 1e-5f);
            Assert.AreEqual(0, Rebuilt("Overlay").vertexCount, "Overlay empty after Up");
        }

        [Test]
        public void SelectionPartStillHaloPlusStrokeOnly()
        {
            MakeView();
            var line = AddLine(0.2, 0.5, 0.8, 0.5);
            _view.MarkAllDirty();
            int stroke = Rebuilt("Shapes").vertexCount;
            Assert.Greater(stroke, 0);
            SelectShapes(line);
            Assert.AreEqual(2 * stroke, Rebuilt("Selection").vertexCount, "Select: halo + stroke (two solid ribbons)");
            _session.SetTool(ToolId.Move);
            Assert.AreEqual(2 * stroke, Rebuilt("Selection").vertexCount, "Move: the Selection part has no frame or pivot in it");
            _session.SetTool(ToolId.Scale);
            Assert.AreEqual(2 * stroke, Rebuilt("Selection").vertexCount, "Scale: same");
        }

        [Test]
        public void HiddenLayerEmptiesTheFrame()
        {
            MakeView();
            var line = AddLine(0.2, 0.5, 0.8, 0.5);
            SelectShapes(line);
            _session.SetTool(ToolId.Move);
            Assert.Greater(Rebuilt("SelectionFrame").vertexCount, 0, "precondition");

            // Direct model edit (no event, so no prune): bounds are over visible layers only.
            _session.ActiveLayer.Visible = false;
            _view.MarkAllDirty();
            Assert.AreEqual(0, Rebuilt("SelectionFrame").vertexCount, "hidden layer: no frame even with the id still selected");
            _session.ActiveLayer.Visible = true;
            _view.MarkAllDirty();
            Assert.Greater(Rebuilt("SelectionFrame").vertexCount, 0, "shown again");

            EntityId id = _session.ActiveLayer.Id;
            _session.SetLayerVisible(id, false);
            Assert.AreEqual(0, Rebuilt("SelectionFrame").vertexCount, "hidden through the session: pruned, no frame");
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, "and the tool fell back to Select");
            _session.SetLayerVisible(id, true);
            Assert.AreEqual(0, Rebuilt("SelectionFrame").vertexCount, "not re-selected when shown again");
        }

        // ---------- ToolBar helpers ----------

        Canvas _canvas;

        void MakeToolBar(bool place)
        {
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
            _appGo = new GameObject("App");
            var tb = _appGo.AddComponent<ToolBar>();
            Call(tb, "Awake");
            Call(tb, "Start");
            _canvas = _appGo.GetComponentInChildren<Canvas>(true);
            if (place) _space.PlaceAtReticle();
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

        bool BarVisible => _canvas.enabled && BarButtonsActive;

        bool BarButtonsActive
        {
            get
            {
                bool all = true, any = false;
                foreach (var n in BarOrder)
                {
                    bool on = Find(n).activeInHierarchy;
                    all &= on;
                    any |= on;
                }
                Assert.AreEqual(all, any, "the four bar buttons show and hide together");
                return all;
            }
        }

        Transform Bar => Find("Move").transform.parent;

        Rect WorldRect(string name)
        {
            var c = new Vector3[4];
            ((RectTransform)Find(name).transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        Rect CanvasRect()
        {
            var c = new Vector3[4];
            ((RectTransform)_canvas.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        // ---------- ToolBar tests ----------

        [Test]
        public void BarShownOnlyWhenPlacedWithASelectionInASelectionTool()
        {
            MakeToolBar(place: false);
            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            foreach (var n in BarOrder)
                Assert.AreSame(Bar, Find(n).transform.parent, $"{n} is in the bar row");
            Assert.AreNotSame(Find("Line").transform.parent, Bar, "the bar is not part of the column");

            SelectShapes(line);
            Assert.IsFalse(BarVisible, "hidden before placement, even with a selection");
            _space.PlaceAtReticle();
            Assert.IsTrue(BarVisible, "placed + selection + Select: shown");
            _session.ClearSelection();
            Assert.IsFalse(BarVisible, "no selection: hidden");
            _session.SetTool(ToolId.SelectMove);
            Assert.IsFalse(BarVisible, "Select with nothing selected: hidden");
            SelectShapes(line);
            Assert.IsTrue(BarVisible, "selection again: shown");
            foreach (var t in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
            {
                _session.SetTool(t);
                Assert.IsTrue(BarVisible, $"{t}: shown");
            }
            _session.SetTool(ToolId.Line);
            Assert.IsFalse(BarVisible, "Line: hidden");
            _session.SetSelection(new[] { line.Id });   // a selection while drawing (API): still not a selection tool
            Assert.AreEqual(1, _session.SelectionCount, "precondition: selected in Line");
            Assert.IsFalse(BarVisible, "Line with a selection: hidden");
            _session.SetTool(ToolId.Circle);
            Assert.IsFalse(BarVisible, "Circle: hidden");

            SelectShapes(line);
            _session.SetTool(ToolId.Move);
            Assert.IsTrue(BarVisible);
            _space.Reset();
            Assert.IsFalse(BarVisible, "re-place: hidden");
        }

        [Test]
        public void BarModeButtonsToggleAndHighlightDeselectClears()
        {
            MakeToolBar(place: true);
            var toasts = new List<string>();
            _session.Toast += t => toasts.Add(t);
            Color accent = Bg("Line");   // Line is the default tool
            Color idle = Bg("Circle");
            Assert.AreNotEqual(accent, idle, "precondition: active tool highlighted");
            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            SelectShapes(line);
            Assert.IsTrue(BarVisible, "precondition: bar shown");
            foreach (var n in BarOrder)
            {
                Assert.AreEqual(idle, Bg(n), $"{n} idle in Select");
                Assert.IsTrue(Find(n).GetComponent<Graphic>().raycastTarget, $"{n} is a raycast target (board input ignores it)");
            }
            Assert.AreEqual(accent, Bg("Select"), "column Select highlighted in Select");

            var modes = new[] { ("Move", ToolId.Move, EditorSession.MoveHintToast), ("Scale", ToolId.Scale, EditorSession.ScaleHintToast),
                                ("Duplicate", ToolId.Duplicate, EditorSession.DuplicateHintToast) };
            foreach (var (name, tool, hint) in modes)
            {
                toasts.Clear();
                Click(name);
                Assert.AreEqual(tool, _session.ActiveTool, $"{name} button sets {tool}");
                CollectionAssert.Contains(toasts, hint, $"{name}: hint toast");
                Assert.AreEqual(accent, Bg(name), $"{name} filled orange while active");
                foreach (var (other, _, _) in modes)
                    if (other != name) Assert.AreEqual(idle, Bg(other), $"{other} idle while {name} is active");
                Assert.AreEqual(accent, Bg("Select"), $"column Select highlighted in {tool}");
                Assert.AreEqual(idle, Bg("Line"), "Line idle");
                Assert.AreEqual(idle, Bg("Circle"), "Circle idle");
                Assert.AreEqual(1, _session.SelectionCount, $"{name} keeps the selection");

                Click(name);
                Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, $"tapping the active {name} returns to Select");
                Assert.AreEqual(idle, Bg(name), $"{name} idle again");
                Assert.AreEqual(1, _session.SelectionCount, "selection kept");
            }

            Click("Scale");
            Click("Move");
            Assert.AreEqual(ToolId.Move, _session.ActiveTool, "inactive mode button switches directly (Scale -> Move)");
            Click("Select");
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, "column Select from Move");
            Assert.AreEqual(1, _session.SelectionCount, "column Select keeps the selection");

            Click("Duplicate");
            Click("Deselect");
            Assert.AreEqual(0, _session.SelectionCount, "Deselect empties the selection");
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, "and the tool falls back to Select");
            Assert.IsFalse(BarVisible, "and the bar hides");
            Assert.AreEqual(accent, Bg("Select"));
        }

        [Test]
        public void BarButtonsLookLikeTheColumn()
        {
            MakeToolBar(place: true);
            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            SelectShapes(line);
            var tb = _appGo.GetComponent<ToolBar>();
            Sprite rounded = Find("Line").GetComponent<Image>().sprite;
            Assert.NotNull(rounded);
            var icons = new List<Texture>();
            foreach (var n in ColumnOrder) icons.Add(Find(n).transform.Find("Icon").GetComponent<RawImage>().texture);
            foreach (var n in BarOrder)
            {
                var go = Find(n);
                Assert.AreSame(rounded, go.GetComponent<Image>().sprite, $"{n}: the same rounded sprite");
                Assert.IsNotNull(go.GetComponent<Button>(), $"{n} is a Button");
                var icon = go.transform.Find("Icon");
                Assert.NotNull(icon, $"{n} has an Icon");
                Assert.AreEqual(new Vector2(28f, 28f), ((RectTransform)icon).sizeDelta, $"{n} icon 28x28");
                var tex = icon.GetComponent<RawImage>().texture;
                Assert.NotNull(tex, $"{n} icon texture");
                CollectionAssert.DoesNotContain(icons, tex, $"{n} has its own glyph");
                icons.Add(tex);
                var label = go.GetComponentInChildren<Text>(true);
                Assert.NotNull(label, $"{n} label");
                Assert.AreEqual(n, label.text, $"{n}: label text equals the GameObject name");
                Assert.AreEqual(11, label.fontSize, $"{n}: 11 pt label");
                var rt = (RectTransform)go.transform;
                Assert.AreEqual(BarButtonW, rt.rect.width, 0.01f, $"{n} is {BarButtonW} wide");
                Assert.AreEqual(BarButtonH, rt.rect.height, 0.01f, $"{n} is {BarButtonH} tall");
            }

            // GlyphCount 21 (15 + CircleDotted, Bands, HoldMark, Fill, Delete, Redo): the bar glyphs, Undo, plus the rounded sprite at index GlyphCount, all distinct.
            var f = typeof(ToolBar).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f, "ToolBar._textures");
            var table = (Texture2D[])f.GetValue(tb);
            Assert.AreEqual(22, table.Length, "21 glyphs (incl. Undo, Redo, the line-mode glyphs, Fill and Delete) + the rounded sprite");
            var distinct = new HashSet<Texture2D>();
            foreach (var t in table)
            {
                Assert.NotNull(t, "every glyph slot is filled (a missing GlyphCount bump overwrites one)");
                distinct.Add(t);
            }
            Assert.AreEqual(22, distinct.Count, "all distinct");
            Assert.AreSame(rounded.texture, table[21], "the rounded sprite sits at index GlyphCount (21)");
            foreach (var t in icons) CollectionAssert.Contains(table, t, "every icon is in the table (freed on destroy)");
        }

        [Test]
        public void BarIsOneRowInsideTheCanvasLeftOfTheColumnAbovePlacementRow()
        {
            MakeToolBar(place: true);
            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            SelectShapes(line);
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(BarVisible, "precondition: bar shown");
            Rect canvas = CanvasRect();
            float h = canvas.height;

            var r = new Rect[BarOrder.Length];
            for (int i = 0; i < BarOrder.Length; i++)
            {
                r[i] = WorldRect(BarOrder[i]);
                Assert.GreaterOrEqual(r[i].xMin, canvas.xMin - 0.5f, $"{BarOrder[i]} inside the left edge");
                Assert.LessOrEqual(r[i].xMax, canvas.xMax + 0.5f, $"{BarOrder[i]} inside the right edge");
                Assert.GreaterOrEqual(r[i].yMin, canvas.yMin + PlacementRowTop * h - 1f, $"{BarOrder[i]} above the placement row (11%)");
                Assert.LessOrEqual(r[i].yMax, canvas.yMax, $"{BarOrder[i]} inside the top edge");
            }
            float w = r[0].width;
            Assert.Greater(w, 0f);
            for (int i = 1; i < r.Length; i++)
            {
                Assert.AreEqual(r[0].yMin, r[i].yMin, 0.5f, $"{BarOrder[i]} on the same row (bottom)");
                Assert.AreEqual(r[0].yMax, r[i].yMax, 0.5f, $"{BarOrder[i]} on the same row (top)");
                Assert.AreEqual(w, r[i].width, 0.5f, $"{BarOrder[i]} same width");
                float gap = r[i].xMin - r[i - 1].xMax;
                Assert.AreEqual(BarSpacing / BarButtonW, gap / w, 0.02f, $"{BarOrder[i - 1]} -> {BarOrder[i]}: spacing 6 of 56, in order");
            }
            Assert.AreEqual(BarButtonH / BarButtonW, r[0].height / w, 0.02f, "56 x 52 buttons");
            Assert.AreEqual(BarWidth / BarButtonW, (r[4].xMax - r[0].xMin) / w, 0.03f, "the bar is 304 wide");

            float columnLeft = float.MaxValue;
            foreach (var n in ColumnOrder) columnLeft = Mathf.Min(columnLeft, WorldRect(n).xMin);
            Assert.LessOrEqual(r[3].xMax, columnLeft, "the bar ends left of the tool column (never under it)");

            float fit = Bar.localScale.x;
            Assert.GreaterOrEqual(fit, MinFit - 1e-4f, "shrink never below MinFit");
            Assert.LessOrEqual(fit, 1f + 1e-4f, "never enlarged");
            Assert.AreEqual(fit, Bar.localScale.y, 1e-4f, "uniform shrink");
            if (canvas.height > canvas.width)
                Assert.Less(r[0].yMax, canvas.yMin + 0.27f * h, "portrait: below the layer sidebar (27%..73%)");
        }

        [Test]
        public void BarAndColumnFootprintsBlockBoardTouchesInTheGaps()
        {
            MakeToolBar(place: true);
            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            SelectShapes(line);
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(BarVisible, "precondition: bar shown");
            foreach (var (root, names) in new[] { (Bar, BarOrder), (Find("Column").transform, ColumnOrder) })
            {
                var hit = root.GetComponent<Graphic>();
                Assert.NotNull(hit, $"{root.name}: a backing Graphic, so touches between the buttons are UI too");
                Assert.IsTrue(hit.raycastTarget, $"{root.name}: the backing Graphic is a raycast target");
                Assert.AreEqual(0f, hit.color.a, 1e-6f, $"{root.name}: the backing is invisible");
                var c = new Vector3[4];
                ((RectTransform)root).GetWorldCorners(c);
                Rect area = Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
                for (int i = 0; i < names.Length; i++)
                {
                    Rect b = WorldRect(names[i]);
                    Assert.IsTrue(b.xMin >= area.xMin - 0.5f && b.xMax <= area.xMax + 0.5f && b.yMin >= area.yMin - 0.5f
                                  && b.yMax <= area.yMax + 0.5f, $"{root.name}: the backing covers {names[i]}");
                    if (i == 0) continue;
                    Rect a = WorldRect(names[i - 1]);   // the gap between neighbours lies inside the backing
                    Vector2 gap = root == Bar ? new Vector2((a.xMax + b.xMin) * 0.5f, a.center.y)
                                              : new Vector2(a.center.x, (a.yMin + b.yMax) * 0.5f);
                    Assert.IsTrue(area.Contains(gap), $"{root.name}: gap {names[i - 1]}/{names[i]} is covered");
                }
            }
        }

        /// <summary>
        /// The spec placement (B1.10) in screen px for a screen of w x h with the given safe area and column fit: returns
        /// the bar's rect after the shrink (bottom edge on the baseline, centred in the span).
        /// </summary>
        static Rect SpecBarRect(float w, float h, Rect safe, float columnFit, out float fit)
        {
            float scale = ScreenSnapMetric.ScaleFactor(w, h);
            float bottom = safe.yMin + PlacementRowTop * h + EdgeMargin * scale;
            float left = safe.xMin + EdgeMargin * scale;
            float right = safe.xMax - (EdgeMargin + ButtonW * columnFit + EdgeMargin) * scale;
            float span = right - left;
            fit = span < BarWidth * scale ? Mathf.Clamp(span / (BarWidth * scale), MinFit, 1f) : 1f;
            float width = BarWidth * scale * fit, height = BarButtonH * scale * fit;
            float centre = (left + right) * 0.5f;
            return new Rect(centre - width * 0.5f, bottom, width, height);
        }

        [Test]
        public void BarPlacementFollowsTheSpecFormulaAtTheBatchScreen()
        {
            MakeToolBar(place: true);
            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            SelectShapes(line);
            Call(_appGo.GetComponent<ToolBar>(), "LateUpdate");   // re-applies the safe area if the screen changed
            Canvas.ForceUpdateCanvases();
            Rect canvas = CanvasRect();
            float sw = Screen.width, sh = Screen.height;
            if (Mathf.Abs(canvas.width - sw) > 1f || Mathf.Abs(canvas.height - sh) > 1f)
                Assert.Ignore($"overlay canvas {canvas.width}x{canvas.height} is not screen-sized ({sw}x{sh}) in this Edit Mode run");

            float columnFit = Find("Column").transform.localScale.x;
            Rect want = SpecBarRect(sw, sh, Screen.safeArea, columnFit, out float fit);
            Rect a = WorldRect("Move"), d = WorldRect("Deselect");
            Debug.Log($"[Verifier] screen {sw}x{sh} safe {Screen.safeArea} column fit {columnFit}: bar want {want} fit {fit}, " +
                      $"got x {a.xMin}..{d.xMax} y {a.yMin}..{a.yMax} scale {Bar.localScale.x}");
            Assert.AreEqual(fit, Bar.localScale.x, 1e-3f, "bar shrink = clamp(span / 304 scale, MinFit, 1)");
            Assert.AreEqual(want.yMin, a.yMin, 1.5f, "bottom edge on the column's lowest baseline");
            Assert.AreEqual(want.xMin, a.xMin, 1.5f, "left edge (centred in the span left of the column)");
            Assert.AreEqual(want.xMax, d.xMax, 1.5f, "right edge");
            Assert.AreEqual(want.height, a.height, 1.0f, "height 52 * scale * fit");
        }

        [Test]
        public void SpecLayoutFitsThe390x844ReferenceAndIPhone14ProMax()
        {
            // The formula verified against the real bar above, evaluated for the target phones (reference px, scale 1).
            // 390x844 with no insets; column fit 1 (550 px fits between 11% + 8 and 80% - 8).
            Rect bar = SpecBarRect(390f, 844f, new Rect(0, 0, 390, 844), 1f, out float fit);
            Assert.AreEqual(1f, fit, "390x844: no shrink");
            Assert.AreEqual(100.84f, bar.yMin, 0.05f, "390x844: bottom at 11% + 8");
            Assert.AreEqual(11f, bar.xMin, 0.05f, "390x844: x 11..315");
            Assert.AreEqual(315f, bar.xMax, 0.05f);
            Assert.LessOrEqual(bar.xMax, 390f - EdgeMargin - ButtonW, "left of the column");
            Assert.Less(bar.yMax, 0.27f * 844f, "below the sidebar");
            Assert.Less(bar.yMax, (1f - 0.245f) * 844f, "below the toasts");

            // iPhone 14 Pro Max portrait: 1290x2796 px, safe insets about 31 ref px bottom and 54 top.
            float w = 1290f, h = 2796f, s = ScreenSnapMetric.ScaleFactor(w, h);
            var safe = Rect.MinMaxRect(0f, 31f * s, w, h - 54f * s);
            float band = (Mathf.Min(safe.yMax, 0.8f * h) - EdgeMargin * s) - (safe.yMin + PlacementRowTop * h + EdgeMargin * s);
            float columnFit = Mathf.Clamp(band / (550f * s), MinFit, 1f);
            Assert.AreEqual(0.975f, columnFit, 0.01f, "the column fit the brief quotes");
            bar = SpecBarRect(w, h, safe, columnFit, out fit);
            Assert.AreEqual(1f, fit, "14 Pro Max: no shrink");
            Assert.AreEqual(132f, bar.yMin / s, 1.5f, "14 Pro Max: bar 132..184 ref px from the bottom");
            Assert.AreEqual(184f, bar.yMax / s, 1.5f);
            Assert.AreEqual(12f, bar.xMin / s, 1.5f, "14 Pro Max: x about 12..316 ref px");
            Assert.AreEqual(316f, bar.xMax / s, 1.5f);
            float columnLeft = w - (EdgeMargin + ButtonW * columnFit) * s;
            Assert.Less(bar.xMax, columnLeft, "no overlap with the column");
            Assert.Greater(bar.yMin, safe.yMin + PlacementRowTop * h, "above the placement row");
            Assert.Less(bar.yMax, 0.27f * h, "below the sidebar");
        }
    }
}
