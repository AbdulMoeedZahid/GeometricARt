using System;
using System.Collections.Generic;
using System.Reflection;
using GeoART.App;
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
    /// VERIFIER: the Undo button (p3-undo-briefs B1.11, C3.2), written from the spec. ToolBar with a real EditorSession
    /// and a fake IBoardSpace whose State is settable:
    ///   structure (a Button named "Undo", a direct child of the ToolBar canvas, not under "Column", raycast target, 28x28
    ///   icon, 11 pt "Undo" label, CanvasGroup, Idle background, same sprite as the column), the glyph table (16 slots,
    ///   rounded sprite at 15), visibility (only in Placed, in every tool; hidden in Scanning, PlaneFound, TrackingLost,
    ///   PickingCorners), the dimmed state (alpha 0.4 / 1, refreshed on BoardChanged without StateChanged, still
    ///   interactable and blocking raycasts), the click (Undo, or "Nothing to undo"), layout (B1.11 formula at the batch
    ///   screen, clear of the column, the action bar, the sidebar band and the toast; the formula for 390x844 and iPhone 14
    ///   Pro Max), the uGUI footprint, and the LayerSidebar opacity popup session as one undo step.
    /// Edit Mode does not run Awake/Start/LateUpdate for plain MonoBehaviours, so they are invoked by reflection.
    /// </summary>
    public sealed class VerifierUndoViewTests
    {
        /// <summary>IsPlaced is true in Placed and TrackingLost, as ARBoardSpace's.</summary>
        sealed class Space : IBoardSpace
        {
            PlacementState _state = PlacementState.Scanning;

            public PlacementState State
            {
                get => _state;
                set
                {
                    if (_state == value) return;
                    _state = value;
                    StateChanged?.Invoke(value);
                }
            }

            public event Action<PlacementState> StateChanged;
            public bool IsPlaced => _state == PlacementState.Placed || _state == PlacementState.TrackingLost;
            public void PlaceAtReticle() => State = PlacementState.Placed;
            public void Reset() => State = PlacementState.Scanning;
            public bool TryScreenToBoard(Vec2 px, out Vec2 b) { b = default; return false; }
            public bool TryBoardToScreen(Vec2 b, out Vec2 px) { px = default; return false; }
        }

        // Spec constants (ToolBar, B1.11, A4), restated so the tests do not read them from the code under test.
        const float ButtonW = 56f, ButtonH = 52f, EdgeMargin = 8f, DisabledAlpha = 0.4f;
        const float PlacementRowTop = 0.11f, CoachTextBottom = 0.80f, MinFit = 0.4f, ColumnHeight = 550f;
        const float SidebarTopFraction = 0.73f, ToastCentreFromTop = 0.245f;
        const string NothingToUndo = "Nothing to undo";

        static readonly string[] ColumnOrder = { "Line", "Circle", "Select", "Bands", "Medium", "Square", "2x2", "Snap", "HV" };

        EditorSession _session;
        Space _space;
        GameObject _appGo, _esGo, _sideGo;
        EventSystem _es;
        Canvas _canvas;
        double _t;
        readonly List<string> _toasts = new List<string>();
        readonly List<BoardChangeKind> _boards = new List<BoardChangeKind>();
        int _states;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession();
            _session.SetSnapEnabled(false);
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
            _toasts.Clear();
            _boards.Clear();
            _states = 0;
            _session.Toast += t => _toasts.Add(t);
            _session.BoardChanged += k => _boards.Add(k);
            _session.StateChanged += () => _states++;
        }

        [TearDown]
        public void TearDown()
        {
            if (_appGo != null)
            {
                // ToolBar.OnDestroy uses Object.Destroy (play mode only): free the glyph textures and sprite directly.
                var tb = _appGo.GetComponent<ToolBar>();
                var f = typeof(ToolBar).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic);
                if (tb != null && f != null && f.GetValue(tb) is Texture2D[] table)
                    foreach (var t in table) if (t != null) Object.DestroyImmediate(t);
                Object.DestroyImmediate(_appGo);
            }
            if (_sideGo != null) Object.DestroyImmediate(_sideGo);
            if (_esGo != null) { EsEnable(_es, false); Object.DestroyImmediate(_esGo); }
            ServiceHub.Clear();
        }

        // ---------- helpers ----------

        static object Call(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m, $"{target.GetType().Name}.{method} not found");
            return m.Invoke(target, null);
        }

        /// <summary>Edit Mode does not run EventSystem.OnEnable/OnDisable, which (un)register it as EventSystem.current.</summary>
        static void EsEnable(EventSystem es, bool on)
        {
            if (es == null) return;
            var m = typeof(EventSystem).GetMethod(on ? "OnEnable" : "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(es, null);
        }

        void MakeEventSystem()
        {
            if (_esGo != null) return;
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
        }

        void MakeToolBar(bool place)
        {
            MakeEventSystem();
            _appGo = new GameObject("App");
            var tb = _appGo.AddComponent<ToolBar>();
            Call(tb, "Awake");
            Call(tb, "Start");
            _canvas = _appGo.GetComponentInChildren<Canvas>(true);
            if (place) _space.State = PlacementState.Placed;
        }

        GameObject Find(string name)
        {
            foreach (var t in _appGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        GameObject UndoGo => Find("Undo");
        CanvasGroup Group => UndoGo.GetComponent<CanvasGroup>();
        float Alpha => Group.alpha;
        bool UndoVisible => _canvas.enabled && UndoGo.activeInHierarchy;

        void Click(GameObject go) =>
            ExecuteEvents.Execute(go, new PointerEventData(_es) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);

        void Ptr(PointerPhase phase, double x, double y) =>
            _session.Pointer(new PointerEvent(phase, new Vec2(x, y), true, _t += 0.016));

        /// <summary>Commits a line through the session's pointer path (BoardChanged only, no StateChanged).</summary>
        void DrawLine(double ax, double ay, double bx, double by)
        {
            Ptr(PointerPhase.Down, ax, ay);
            for (int i = 1; i <= 6; i++) Ptr(PointerPhase.Move, ax + (bx - ax) * i / 6, ay + (by - ay) * i / 6);
            Ptr(PointerPhase.Up, bx, by);
        }

        int ShapeCount
        {
            get
            {
                int n = 0;
                foreach (var l in _session.Board.Layers) n += l.Shapes.Count;
                return n;
            }
        }

        static Rect WorldRect(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        Rect CanvasRect()
        {
            var c = new Vector3[4];
            ((RectTransform)_canvas.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        void SelectLine(LineShape line)
        {
            _session.SetTool(ToolId.SelectMove);
            _session.SetSelection(new[] { line.Id });
            Assert.AreEqual(1, _session.SelectionCount, "precondition: selected");
        }

        LineShape AddLine(double ax, double ay, double bx, double by)
        {
            var l = new LineShape(_session.Board.NewId(), new Vec2(ax, ay), new Vec2(bx, by), StrokeStyle.Default);
            _session.ActiveLayer.Shapes.Add(l);
            return l;
        }

        // ---------- structure ----------

        [Test]
        public void UndoIsADirectCanvasChildButtonWithIconLabelAndCanvasGroup()
        {
            MakeToolBar(place: true);
            int named = 0;
            foreach (var t in _appGo.GetComponentsInChildren<Transform>(true)) if (t.name == "Undo") named++;
            Assert.AreEqual(1, named, "exactly one GameObject named Undo");
            GameObject go = UndoGo;
            Assert.AreSame(_canvas.transform, go.transform.parent, "Undo is a direct child of the ToolBar canvas");
            Assert.IsFalse(go.transform.IsChildOf(Find("Column").transform), "not under Column");
            Assert.IsFalse(go.transform.IsChildOf(Find("Move").transform.parent), "not under the action bar");
            Assert.NotNull(go.GetComponent<Button>(), "a Button");
            var bg = go.GetComponent<Image>();
            Assert.NotNull(bg, "a background Image");
            Assert.IsTrue(bg.raycastTarget, "the background is a raycast target");
            Assert.AreSame(Find("Line").GetComponent<Image>().sprite, bg.sprite, "the column's rounded sprite");
            Assert.AreEqual(Find("Circle").GetComponent<Image>().color, bg.color, "Idle background (Line is the active tool, Circle idle)");
            Assert.NotNull(go.GetComponent<CanvasGroup>(), "a CanvasGroup for the dimmed look");

            var icon = go.transform.Find("Icon");
            Assert.NotNull(icon, "an Icon");
            Assert.AreEqual(new Vector2(28f, 28f), ((RectTransform)icon).sizeDelta, "icon 28x28");
            var tex = icon.GetComponent<RawImage>().texture;
            Assert.NotNull(tex, "icon texture");
            foreach (var n in ColumnOrder)
                Assert.AreNotSame(Find(n).transform.Find("Icon").GetComponent<RawImage>().texture, tex, $"Undo glyph differs from {n}'s");
            foreach (var n in new[] { "Move", "Scale", "Duplicate", "Deselect" })
                Assert.AreNotSame(Find(n).transform.Find("Icon").GetComponent<RawImage>().texture, tex, $"Undo glyph differs from {n}'s");
            Text label = null;
            foreach (var t in go.GetComponentsInChildren<Text>(true)) label = t;
            Assert.NotNull(label, "a label");
            Assert.AreEqual("Undo", label.text);
            Assert.AreEqual(11, label.fontSize, "11 pt label");

            var rt = (RectTransform)go.transform;
            Assert.AreEqual(new Vector2(0f, 1f), rt.pivot, "pivot top-left (0, 1)");
            Assert.AreEqual(ButtonW, rt.rect.width, 0.01f, "56 wide");
            Assert.AreEqual(ButtonH, rt.rect.height, 0.01f, "52 tall");
            Transform column = Find("Column").transform;
            Assert.AreEqual(column.localScale.x, rt.localScale.x, 1e-4f, "scaled with the column fit");
            Assert.AreEqual(rt.localScale.x, rt.localScale.y, 1e-4f, "uniform");
        }

        [Test]
        public void GlyphTableHasSixteenDistinctSlotsWithTheRoundedSpriteAt15()
        {
            MakeToolBar(place: true);
            var f = typeof(ToolBar).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f, "ToolBar._textures");
            var table = (Texture2D[])f.GetValue(_appGo.GetComponent<ToolBar>());
            Assert.AreEqual(16, table.Length, "GlyphCount 15 + the rounded sprite");
            var distinct = new HashSet<Texture2D>();
            foreach (var t in table)
            {
                Assert.NotNull(t, "every slot filled (a missing GlyphCount bump overwrites one)");
                distinct.Add(t);
            }
            Assert.AreEqual(16, distinct.Count, "all distinct");
            Assert.AreSame(Find("Line").GetComponent<Image>().sprite.texture, table[15], "rounded sprite at index 15");
            CollectionAssert.Contains(table, UndoGo.transform.Find("Icon").GetComponent<RawImage>().texture,
                "the Undo glyph is in the table (freed on destroy)");
        }

        // ---------- visibility ----------

        [Test]
        public void UndoShownOnlyWhilePlacedInEveryTool()
        {
            MakeToolBar(place: false);
            Assert.IsFalse(UndoGo.activeInHierarchy && _canvas.enabled, "Scanning before placement: hidden");
            _space.State = PlacementState.PlaneFound;
            Assert.IsFalse(UndoVisible, "PlaneFound: hidden");
            _space.State = PlacementState.PickingCorners;
            Assert.IsFalse(UndoVisible, "PickingCorners: hidden");
            _space.State = PlacementState.Placed;
            Assert.IsTrue(UndoVisible, "Placed: shown");

            _space.State = PlacementState.TrackingLost;
            Assert.IsTrue(_canvas.enabled, "precondition: the tool column stays visible in TrackingLost (IsPlaced)");
            Assert.IsFalse(UndoGo.activeInHierarchy, "TrackingLost: Undo hidden (the coach line may draw there)");
            _space.State = PlacementState.Placed;
            Assert.IsTrue(UndoVisible, "Placed again: shown");

            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            foreach (var tool in new[] { ToolId.Line, ToolId.Circle, ToolId.SelectMove })
            {
                _session.SetTool(tool);
                Assert.IsTrue(UndoVisible, $"{tool}: shown");
            }
            SelectLine(line);
            foreach (var tool in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
            {
                _session.SetTool(tool);
                Assert.AreEqual(tool, _session.ActiveTool);
                Assert.IsTrue(UndoVisible, $"{tool}: shown");
            }

            // Re-placement keeps the history: hidden while re-placing, bright again once placed.
            _session.SetTool(ToolId.Line);
            DrawLine(0.1, 0.8, 0.6, 0.8);
            Assert.IsTrue(_session.CanUndo);
            _space.Reset();
            Assert.IsFalse(UndoVisible, "re-placing: hidden");
            _space.State = PlacementState.PlaneFound;
            Assert.IsFalse(UndoVisible, "PlaneFound while re-placing: hidden");
            _space.State = PlacementState.Placed;
            Assert.IsTrue(UndoVisible && _session.CanUndo, "placed again: shown, history kept");
            Assert.AreEqual(1f, Alpha, 1e-4f, "and bright");
        }

        // ---------- dimmed state ----------

        [Test]
        public void UndoDimmedWhenNothingToUndoAndRefreshedOnBoardChanged()
        {
            MakeToolBar(place: true);
            Color idle = UndoGo.GetComponent<Image>().color;
            Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "nothing to undo at start: alpha 0.4");
            Assert.IsTrue(Group.interactable && Group.blocksRaycasts, "dimmed but still interactable and blocking raycasts");
            Assert.IsTrue(UndoGo.GetComponent<Button>().interactable, "the Button stays interactable");

            _states = 0;
            _boards.Clear();
            DrawLine(0.2, 0.3, 0.7, 0.3);
            Assert.AreEqual(1, _boards.Count, "precondition: the commit raised one BoardChanged");
            Assert.AreEqual(0, _states, "precondition: and no StateChanged");
            Assert.AreEqual(1f, Alpha, 1e-4f, "after the first stroke: full alpha (refreshed on BoardChanged)");
            Assert.IsTrue(Group.interactable && Group.blocksRaycasts);
            Assert.AreEqual(idle, UndoGo.GetComponent<Image>().color, "background stays Idle (an action, never Accent)");

            _session.Undo();
            Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "after Undo: dimmed again");
            _session.Redo();
            Assert.AreEqual(1f, Alpha, 1e-4f, "after Redo: bright");
            _session.LoadBoard(GoldenBoards.Empty());
            Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "after LoadBoard: dimmed");
            _session.ClearBoard();
            Assert.AreEqual(1f, Alpha, 1e-4f, "Clear board is undoable: bright");
            _session.Undo();
            _session.SetGrid(3);
            Assert.AreEqual(1f, Alpha, 1e-4f, "Tiles: bright");
            _session.Undo();
            _session.SetLayerOpacity(_session.Board.Layers[0].Id, 0.5);
            Assert.AreEqual(1f, Alpha, 1e-4f, "Layer opacity: bright");
            _session.Undo();
            Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "everything undone: dimmed");

            // Edits made while hidden show up when the button reappears.
            _space.Reset();
            DrawLine(0.2, 0.5, 0.7, 0.5);
            _space.State = PlacementState.Placed;
            Assert.AreEqual(1f, Alpha, 1e-4f, "placed again after an edit: bright");
            foreach (var tool in new[] { ToolId.Circle, ToolId.SelectMove })
            {
                _session.SetTool(tool);
                Assert.AreEqual(idle, UndoGo.GetComponent<Image>().color, $"{tool}: background still Idle");
            }
        }

        // ---------- click ----------

        [Test]
        public void UndoClickRevertsTheLastEditAndToastsWhenEmpty()
        {
            MakeToolBar(place: true);
            DrawLine(0.2, 0.3, 0.7, 0.3);
            Assert.AreEqual(1, ShapeCount, "precondition: a line");
            _toasts.Clear();
            _boards.Clear();
            Click(UndoGo);
            Assert.AreEqual(0, ShapeCount, "the click undid the line");
            Assert.AreEqual(1, _boards.Count, "one BoardChanged");
            Assert.AreEqual(EditorSession.HistoryChangeKind, _boards[0], "with the History kind");
            CollectionAssert.AreEqual(new[] { "Undid: Line" }, _toasts);
            Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "dimmed");

            _toasts.Clear();
            _boards.Clear();
            Click(UndoGo);   // dimmed: still clickable, feedback instead of a dead button
            CollectionAssert.AreEqual(new[] { NothingToUndo }, _toasts, "a tap on the dimmed button toasts");
            Assert.AreEqual(0, _boards.Count, "and changes nothing");

            // Every action type through the button: the selection tools keep the button.
            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            SelectLine(line);
            _session.SetTool(ToolId.Move);
            Ptr(PointerPhase.Down, 0.6, 0.6);
            for (int i = 1; i <= 6; i++) Ptr(PointerPhase.Move, 0.6 + 0.01 * i, 0.6);
            Ptr(PointerPhase.Up, 0.66, 0.6);
            Assert.AreNotEqual(0.2, ((LineShape)_session.Board.Layers[0].Shapes[0]).A.X, "precondition: moved");
            Click(UndoGo);
            var back = (LineShape)_session.Board.Layers[0].Shapes[0];
            Assert.IsTrue(back.A == new Vec2(0.2, 0.2) && back.B == new Vec2(0.4, 0.4), "Move undone exactly through the button");
            Assert.AreEqual(ToolId.Move, _session.ActiveTool, "tool kept");
            Assert.IsTrue(_session.Selection.Ids.Contains(line.Id), "selection kept");
        }

        // ---------- layout ----------

        /// <summary>The column per ToolBar.ApplySafeArea (screen px): fit, top edge, left edge.</summary>
        static void SpecColumn(float w, float h, Rect safe, out float scale, out float fit, out float top, out float left)
        {
            scale = ScreenSnapMetric.ScaleFactor(w, h);
            float margin = EdgeMargin * scale;
            float bottom = safe.yMin + PlacementRowTop * h + margin;
            float bandTop = Mathf.Min(safe.yMax, CoachTextBottom * h) - margin;
            fit = Mathf.Clamp((bandTop - bottom) / (ColumnHeight * scale), MinFit, 1f);
            float half = ColumnHeight * scale * fit * 0.5f;
            float centre = Mathf.Clamp((safe.yMin + safe.yMax) * 0.5f, bottom + half, Mathf.Max(bottom + half, bandTop - half));
            top = centre + half;
            left = safe.xMax - margin - ButtonW * scale * fit;
        }

        /// <summary>The Undo button per B1.11 (screen px): pivot (0, 1) at the safe area's top-left, inset 8 ref px.</summary>
        static Rect SpecUndo(Rect safe, float scale, float fit)
        {
            float left = safe.xMin + EdgeMargin * scale, top = safe.yMax - EdgeMargin * scale;
            return Rect.MinMaxRect(left, top - ButtonH * scale * fit, left + ButtonW * scale * fit, top);
        }

        [Test]
        public void UndoLayoutAtTheBatchScreenClearsColumnBarSidebarAndToast()
        {
            MakeToolBar(place: true);
            var line = AddLine(0.2, 0.2, 0.4, 0.4);
            SelectLine(line);   // the action bar shows too
            Call(_appGo.GetComponent<ToolBar>(), "LateUpdate");   // re-applies the safe area if the screen changed
            Canvas.ForceUpdateCanvases();
            Rect canvas = CanvasRect();
            float sw = Screen.width, sh = Screen.height;
            if (Mathf.Abs(canvas.width - sw) > 1f || Mathf.Abs(canvas.height - sh) > 1f)
                Assert.Ignore($"overlay canvas {canvas.width}x{canvas.height} is not screen-sized ({sw}x{sh}) in this Edit Mode run");

            Rect safe = Screen.safeArea;
            SpecColumn(sw, sh, safe, out float scale, out float fit, out float colTop, out float colLeft);
            Rect want = SpecUndo(safe, scale, fit);
            Rect u = WorldRect(UndoGo);
            Rect column = WorldRect(Find("Column"));
            Rect bar = WorldRect(Find("Move").transform.parent.gameObject);
            Debug.Log($"[Verifier] screen {sw}x{sh} safe {safe} scale {scale} fit {fit}: undo want {want} got {u}; column {column}; bar {bar}");

            Assert.AreEqual(fit, Find("Column").transform.localScale.x, 1e-3f, "precondition: column fit per ApplySafeArea");
            Assert.AreEqual(want.xMin, u.xMin, 1.5f, "left edge 8 ref px inside the safe area");
            Assert.AreEqual(want.yMax, u.yMax, 1.5f, "top edge 8 ref px below the safe area's top");
            Assert.AreEqual(want.width, u.width, 1.0f, "56 * scale * fit wide");
            Assert.AreEqual(want.height, u.height, 1.0f, "52 * scale * fit tall");

            Assert.GreaterOrEqual(u.xMin, canvas.xMin, "inside the canvas (left)");
            Assert.LessOrEqual(u.yMax, canvas.yMax, "inside the canvas (top)");
            Assert.GreaterOrEqual(u.xMin, safe.xMin, "inside the safe area (left)");
            Assert.LessOrEqual(u.yMax, safe.yMax, "inside the safe area (top)");
            Assert.Less(u.xMax, column.xMin, "left of the tool column");
            Assert.Greater(u.yMin, column.yMax, "above the tool column's top");
            Assert.AreEqual(colTop, column.yMax, 1.5f, "precondition: column top per the formula");
            Assert.Greater(u.yMin, canvas.yMin + SidebarTopFraction * canvas.height, "above the layer sidebar's highest reach (73%)");
            Assert.Greater(u.yMin, canvas.yMin + (1f - ToastCentreFromTop) * canvas.height, "above the toast centre (24.5% from the top)");
            Assert.IsTrue(UndoGo.activeInHierarchy && Find("Move").activeInHierarchy, "precondition: Undo and the bar both shown");
            Assert.IsFalse(u.Overlaps(bar), "no overlap with the action bar");
            foreach (var n in ColumnOrder) Assert.IsFalse(u.Overlaps(WorldRect(Find(n))), $"no overlap with {n}");
        }

        [Test]
        public void SpecUndoLayoutFor390x844AndIPhone14ProMax()
        {
            // 390x844 reference, no insets: x 8..64, y 784..836 from the bottom.
            var safe = new Rect(0, 0, 390, 844);
            SpecColumn(390f, 844f, safe, out float scale, out float fit, out float colTop, out _);
            Assert.AreEqual(1f, scale, 1e-4f);
            Assert.AreEqual(1f, fit, 1e-4f, "390x844: column not shrunk");
            Rect u = SpecUndo(safe, scale, fit);
            Assert.AreEqual(8f, u.xMin, 0.01f, "390x844: x 8..64");
            Assert.AreEqual(64f, u.xMax, 0.01f);
            Assert.AreEqual(784f, u.yMin, 0.01f, "390x844: y 784..836");
            Assert.AreEqual(836f, u.yMax, 0.01f);
            Assert.Greater(u.yMin, colTop, "above the column");
            Assert.Greater(u.yMin, SidebarTopFraction * 844f, "above the sidebar band");
            Assert.Greater(u.yMin, (1f - ToastCentreFromTop) * 844f, "above the toast centre");
            Assert.Greater(u.yMin, (1f - 0.25f) * 844f, "above the measurements box (21..25% from the top)");

            // iPhone 14 Pro Max portrait: 1290x2796 px, safe insets about 54 ref px top and 31 bottom.
            float w = 1290f, h = 2796f;
            float s = ScreenSnapMetric.ScaleFactor(w, h);
            safe = Rect.MinMaxRect(0f, 31f * s, w, h - 54f * s);
            SpecColumn(w, h, safe, out scale, out fit, out colTop, out float colLeft);
            Assert.AreEqual(0.975f, fit, 0.01f, "column fit about 0.975");
            u = SpecUndo(safe, scale, fit);
            Assert.AreEqual(8f, u.xMin / s, 0.5f, "14 Pro Max: x about 8..62.6 ref");
            Assert.AreEqual(62.6f, u.xMax / s, 0.5f);
            Assert.AreEqual(732.5f, u.yMin / s, 1.0f, "14 Pro Max: y about 732.5..783.2 ref from the bottom");
            Assert.AreEqual(783.2f, u.yMax / s, 1.0f);
            Assert.AreEqual(667.8f, colTop / s, 1.0f, "column top about 667.8 ref");
            Assert.Greater(u.yMin - colTop, 60f * s, "about 65 ref above the column");
            Assert.Less(u.xMax, colLeft, "left of the column");
            Assert.Greater(u.yMin, SidebarTopFraction * h, "above the sidebar band");
            Assert.Greater(u.yMin, (1f - ToastCentreFromTop) * h, "above the toast centre");
            Assert.Greater(u.yMin, (1f - 0.25f) * h, "above the measurements box");
            Assert.LessOrEqual(u.yMax, safe.yMax - 7.9f * s, "below the Dynamic Island / status bar inset");
            float fromTopMin = (h - u.yMax) / h, fromTopMax = (h - u.yMin) / h;
            Assert.AreEqual(0.073f, fromTopMin, 0.004f, "about 7.3% from the top");
            Assert.AreEqual(0.133f, fromTopMax, 0.004f, "to about 13.3% from the top");
        }

        // ---------- footprint ----------

        [Test]
        public void UndoFootprintIsARaycastTargetDimmedOrNot()
        {
            MakeToolBar(place: true);
            Canvas.ForceUpdateCanvases();
            var raycaster = _canvas.GetComponent<GraphicRaycaster>();
            Assert.NotNull(raycaster, "ToolBar canvas has a GraphicRaycaster");
            var img = UndoGo.GetComponent<Image>();
            foreach (bool bright in new[] { false, true })
            {
                if (bright) DrawLine(0.2, 0.3, 0.7, 0.3);
                string what = bright ? "bright" : "dimmed";
                Assert.AreEqual(bright ? 1f : DisabledAlpha, Alpha, 1e-4f, "precondition: alpha");
                Rect u = WorldRect(UndoGo);
                var probes = new[] { u.center, new Vector2(u.xMin + 2f, u.yMax - 2f), new Vector2(u.xMax - 2f, u.yMin + 2f) };

                // What GraphicRaycaster tests per graphic: a raycast target whose rect contains the point and whose
                // raycast filters (the CanvasGroup's blocksRaycasts among them) accept it. One rounded Image, no gaps.
                Assert.IsTrue(img.raycastTarget && img.isActiveAndEnabled, $"{what}: Undo's Image is an active raycast target");
                foreach (var p in probes)
                {
                    Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(img.rectTransform, p, null), $"{what}: {p} inside Undo");
                    Assert.IsTrue(img.Raycast(p, null), $"{what}: {p} passes Undo's raycast filters (CanvasGroup blocks raycasts)");
                }

                // The full GraphicRaycaster too, when this Edit Mode run assigns graphic depths (batch runs often do not).
                var hits = new List<RaycastResult>();
                raycaster.Raycast(new PointerEventData(_es) { position = u.center }, hits);
                Debug.Log($"[Verifier] {what} Undo footprint: GraphicRaycaster returned {hits.Count} hits at {u.center}");
                if (hits.Count > 0)
                {
                    GameObject top = hits[0].gameObject;
                    Assert.IsTrue(top.transform.IsChildOf(UndoGo.transform), $"{what}: the topmost hit is Undo, got {top.name}");
                    Assert.AreSame(UndoGo, ExecuteEvents.GetEventHandler<IPointerClickHandler>(top), "a tap there clicks Undo");
                }
            }

            // Negative control: the filter check is real (a CanvasGroup that stopped blocking would let touches through).
            Group.blocksRaycasts = false;
            Assert.IsFalse(img.Raycast(WorldRect(UndoGo).center, null), "control: with blocksRaycasts off the filter rejects");
            Group.blocksRaycasts = true;
        }

        // ---------- the LayerSidebar opacity popup: one step per session ----------

        LayerSidebar _bar;

        void MakeSidebar()
        {
            MakeEventSystem();
            _sideGo = new GameObject("Sidebar");
            _bar = _sideGo.AddComponent<LayerSidebar>();
            Call(_bar, "Awake");
            Call(_bar, "Start");
        }

        FieldInfo SideField(string name)
        {
            var f = typeof(LayerSidebar).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f, $"LayerSidebar.{name} not found (test needs it to simulate time)");
            return f;
        }

        /// <summary>Simulates <paramref name="seconds"/> passing for the press timer and the double-tap window.</summary>
        void Age(float seconds)
        {
            foreach (var n in new[] { "_pressTime", "_lastTapUp" })
            {
                var f = SideField(n);
                f.SetValue(_bar, (float)f.GetValue(_bar) - seconds);
            }
            Call(_bar, "Update");
        }

        /// <summary>Active dash rows, top of the sidebar first.</summary>
        List<GameObject> Rows()
        {
            Call(_bar, "SnapRows");
            var rows = new List<GameObject>();
            foreach (var rt in _sideGo.GetComponentsInChildren<RectTransform>(false))
                if (rt.name.StartsWith("Layer row")) rows.Add(rt.gameObject);
            rows.Sort((a, b) => ((RectTransform)b.transform).anchoredPosition.y.CompareTo(((RectTransform)a.transform).anchoredPosition.y));
            return rows;
        }

        GameObject SideFind(string contains)
        {
            foreach (var t in _sideGo.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains(contains)) return t.gameObject;
            Assert.Fail($"no sidebar child named *{contains}*");
            return null;
        }

        static Vector2 Center(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;
        }

        PointerEventData Press(GameObject go)
        {
            Vector2 p = Center(go);
            var e = new PointerEventData(_es) { position = p, pressPosition = p, pointerId = 0, button = PointerEventData.InputButton.Left };
            e.pointerPress = go;
            ExecuteEvents.Execute(go, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, e, ExecuteEvents.initializePotentialDrag);
            return e;
        }

        void Release(GameObject go, PointerEventData e) => ExecuteEvents.Execute(go, e, ExecuteEvents.pointerUpHandler);

        void TapRow(GameObject go) => Release(go, Press(go));

        void Hold(GameObject row)
        {
            var e = Press(row);
            Age(0.6f);
            Release(row, e);
            Assert.IsTrue(SideFind("OpacityPopup").activeSelf, "precondition: the hold opened the popup");
        }

        void ClosePopup()
        {
            GameObject blocker = SideFind("PopupBlocker");
            TapRow(blocker);
            Assert.IsFalse(SideFind("OpacityPopup").activeSelf, "precondition: popup closed");
        }

        [Test]
        public void OpacityPopupSessionIsOneUndoStepAndRowTapsAreNone()
        {
            _session.AddLayer();
            _session.AddLayer();   // 3 layers, two steps
            MakeSidebar();
            int u0 = _session.UndoCount;
            Assert.AreEqual(2, u0, "precondition: two Add layer steps");
            Layer mid = _session.Board.Layers[1];
            EntityId id = mid.Id;

            // Taps on rows select layers: never a step.
            var rows = Rows();
            TapRow(rows[2]);
            Age(1f);
            TapRow(rows[0]);
            Age(1f);
            Assert.AreEqual(u0, _session.UndoCount, "row taps (SelectLayer) are not undo steps");

            // First popup session: a hold, then many slider values: one step.
            Hold(Rows()[1]);
            Assert.AreEqual(id, _session.Board.ActiveLayerId, "the hold selected the middle layer");
            var slider = SideFind("OpacityPopup").GetComponentInChildren<Slider>(true);
            foreach (float v in new[] { 0.9f, 0.8f, 0.6f, 0.45f, 0.3f })
                slider.value = v;
            Assert.AreEqual(u0 + 1, _session.UndoCount, "one popup session with five slider values: one step");
            Assert.AreEqual("Layer opacity", _session.NextUndoLabel);
            double afterFirst = _session.Board.FindLayer(id).Opacity;
            ClosePopup();

            // Second session on the same layer: a new step.
            Age(1f);
            Hold(Rows()[1]);
            slider = SideFind("OpacityPopup").GetComponentInChildren<Slider>(true);
            slider.value = 0.2f;
            slider.value = 0.1f;
            Assert.AreEqual(u0 + 2, _session.UndoCount, "a second hold starts a new step");
            ClosePopup();

            _session.Undo();
            Assert.AreEqual(afterFirst, _session.Board.FindLayer(id).Opacity, "first Undo: the value the first session ended on");
            _session.Undo();
            Assert.AreEqual(1.0, _session.Board.FindLayer(id).Opacity, "second Undo: the original opacity");

            // A double tap hides: a step, undone by Undo.
            Age(1f);
            GameObject row = Rows()[1];
            TapRow(row);
            Age(0.1f);
            TapRow(row);
            Assert.IsFalse(_session.Board.FindLayer(id).Visible, "precondition: the double tap hid the layer");
            Assert.AreEqual("Hide layer", _session.NextUndoLabel, "Hide layer is a step");
            _session.Undo();
            Assert.IsTrue(_session.Board.FindLayer(id).Visible, "Undo shows it again");
            Assert.IsTrue(Rows().Count == 3, "the sidebar follows the restored board (3 rows)");
        }
    }
}
