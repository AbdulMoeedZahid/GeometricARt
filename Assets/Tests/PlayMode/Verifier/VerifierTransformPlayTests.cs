using System;
using System.Collections;
using System.Collections.Generic;
using GeoART.App;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using EntityId = GeoART.Contracts.EntityId;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace GeoART.Tests.Verifier.PlayMode
{
    /// <summary>
    /// VERIFIER (Play Mode): Move, Scale and Duplicate end to end. A virtual Touchscreen drives the real BoardInputRouter
    /// (filter, real frames and time) into the real EditorSession with the App's ScreenSnapMetric over a fake IBoardSpace
    /// (board = screen square [100, 900] px unless a test remaps it).
    ///   (a) Move: a grabbed line end released near a grid corner lands on it exactly; one BoardChanged.
    ///   (b) Scale: an outward drag grows a circle about its centre; released near a dot, the radius is the distance.
    ///   (c) Duplicate: a drag places a copy, the tool becomes Move, and the next drag moves only the copy.
    ///   (d) A second finger mid-Move restores the geometry exactly.
    ///   (e) A touch that starts on an action-bar button never reaches the board.
    /// </summary>
    public sealed class VerifierTransformPlayTests
    {
        /// <summary>Linear screen -> board map: board = (px - Origin) / Size per axis. Default [100, 900] px square.</summary>
        sealed class FakeBoardSpace : IBoardSpace
        {
            public double OX = 100, OY = 100, W = 800, H = 800;
            public PlacementState State => PlacementState.Placed;
#pragma warning disable 67
            public event Action<PlacementState> StateChanged;
#pragma warning restore 67
            public bool IsPlaced => true;
            public void PlaceAtReticle() { }
            public void Reset() { }

            public bool TryScreenToBoard(Vec2 px, out Vec2 board)
            {
                board = new Vec2((px.X - OX) / W, (px.Y - OY) / H);
                return board.X >= 0 && board.X <= 1 && board.Y >= 0 && board.Y <= 1;
            }

            public bool TryBoardToScreen(Vec2 b, out Vec2 px)
            {
                px = new Vec2(OX + b.X * W, OY + b.Y * H);
                return true;
            }
        }

        Touchscreen _touch;
        FakeBoardSpace _space;
        EditorSession _session;
        GameObject _go, _uiGo, _esGo;
        InputSettings _savedSettings, _settings;
        readonly List<string> _toasts = new List<string>();
        int _boards;
        LineShape _line;
        CircleShape _circle;
        LineShape _far;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _touch = InputSystem.AddDevice<Touchscreen>();
            _space = new FakeBoardSpace();
            _session = new EditorSession { SnapMetric = new ScreenSnapMetric(_space) };
            ServiceHub.Register<IBoardSpace>(_space);
            ServiceHub.Register<IEditorSession>(_session);
            _savedSettings = InputSystem.settings;
            _settings = ScriptableObject.CreateInstance<InputSettings>();
            _settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = _settings;
            _go = new GameObject("VerifierTransformRouter");
            _go.AddComponent<BoardInputRouter>();

            Board b = _session.Board;
            Layer layer = _session.ActiveLayer;
            _line = new LineShape(b.NewId(), new Vec2(0.2, 0.2), new Vec2(0.35, 0.3), StrokeStyle.Default);
            _circle = new CircleShape(b.NewId(), new Vec2(0.3, 0.6), 0.08, StrokeStyle.Default);
            _far = new LineShape(b.NewId(), new Vec2(0.6, 0.6), new Vec2(0.9, 0.9), StrokeStyle.Default);
            layer.Shapes.Add(_line);
            layer.Shapes.Add(_circle);
            layer.Shapes.Add(_far);
            _toasts.Clear();
            _boards = 0;
            _session.Toast += t => _toasts.Add(t);
            _session.BoardChanged += k => _boards++;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            if (_uiGo != null) UnityEngine.Object.DestroyImmediate(_uiGo);
            if (_esGo != null) UnityEngine.Object.DestroyImmediate(_esGo);
            if (_touch != null) InputSystem.RemoveDevice(_touch);
            if (_savedSettings != null) InputSystem.settings = _savedSettings;
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            ServiceHub.Clear();
        }

        static IEnumerator Frame()
        {
            System.Threading.Thread.Sleep(16);
            yield return null;
        }

        void Touch(int id, InputTouchPhase phase, Vector2 p) =>
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = id, phase = phase, position = p, pressure = 1f });

        static Vector2 Px(double x, double y) => new Vector2(100f + (float)x * 800f, 100f + (float)y * 800f);
        static Vector2 Px(Vec2 b) => Px(b.X, b.Y);
        static Vec2 V(double x, double y) => new Vec2(x, y);

        IEnumerator Move(int id, Vector2 a, Vector2 b, int frames = 16, int rest = 30)
        {
            for (int i = 1; i <= frames; i++)
            {
                Touch(id, InputTouchPhase.Moved, Vector2.Lerp(a, b, i / (float)frames));
                yield return Frame();
            }
            for (int i = 0; i < rest; i++) { Touch(id, InputTouchPhase.Stationary, b); yield return Frame(); }
        }

        /// <summary>Drag a -> b, rest so the router's filter settles on b, release.</summary>
        IEnumerator Drag(Vector2 a, Vector2 b)
        {
            Touch(1, InputTouchPhase.Began, a);
            yield return Frame();
            yield return Move(1, a, b);
            Touch(1, InputTouchPhase.Ended, b);
            yield return Frame();
        }

        void Select(ToolId tool, params Shape[] shapes)
        {
            _session.SetTool(ToolId.SelectMove);
            var ids = new List<EntityId>();
            foreach (var s in shapes) ids.Add(s.Id);
            _session.SetSelection(ids);
            _session.SetTool(tool);
            Assert.AreEqual(tool, _session.ActiveTool, $"precondition: {tool} with a selection");
        }

        void AssertSelected(string what, params Shape[] shapes)
        {
            var want = new HashSet<EntityId>();
            foreach (var s in shapes) want.Add(s.Id);
            Assert.IsTrue(_session.Selection.Ids.SetEquals(want), $"{what}: {_session.Selection.Ids.Count} selected, expected {want.Count}");
        }

        static void AssertSame(Shape now, Shape before, string what)
        {
            switch (now)
            {
                case LineShape l:
                    var ol = (LineShape)before;
                    Assert.IsTrue(l.A == ol.A && l.B == ol.B, $"{what}: line {ol.A}-{ol.B} became {l.A}-{l.B}");
                    break;
                case CircleShape c:
                    var oc = (CircleShape)before;
                    Assert.IsTrue(c.Center == oc.Center && c.Radius == oc.Radius, $"{what}: circle {oc.Center} r {oc.Radius} became {c.Center} r {c.Radius}");
                    break;
            }
        }

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            for (int i = 0; i < 5; i++) yield return Frame();
            Touch(1, InputTouchPhase.Began, new Vector2(500, 500));
            yield return Frame();
            Touch(1, InputTouchPhase.Ended, new Vector2(500, 500));
            yield return Frame();
        }

        [UnityTest]
        public IEnumerator A_MoveLandsGrabbedEndExactlyOnGridCorner()
        {
            yield return Frame();
            _session.SetTool(ToolId.SelectMove);
            yield return Drag(Px(0.1, 0.1), Px(0.4, 0.4));
            AssertSelected("box-selected", _line);
            _session.SetTool(ToolId.Move);
            Assert.AreEqual(ToolId.Move, _session.ActiveTool);
            var circleBefore = _circle.Clone();
            var farBefore = _far.Clone();
            _boards = 0;

            // Grab 0.014 from the end B (0.35, 0.3); release with the end about 0.007 from the corner (0.5, 0.5).
            Vec2 f0 = V(0.36, 0.31);
            Vec2 f = V(f0.X + (0.5 - 0.35) + 0.006, f0.Y + (0.5 - 0.3) - 0.004);
            Touch(1, InputTouchPhase.Began, Px(f0));
            yield return Frame();
            yield return Move(1, Px(f0), Px(f));
            Assert.Greater(_session.Preview.Count, 0, "orange ghost while dragging");
            Assert.IsTrue(_session.Cursor.HasValue, "cursor at the reference");
            Assert.AreEqual(0.5, _session.Cursor.Value.X, 1e-9, "the reference snapped to the corner (x)");
            Assert.AreEqual(0.5, _session.Cursor.Value.Y, 1e-9, "(y)");
            Assert.AreEqual(0, _boards, "model untouched before release");
            Touch(1, InputTouchPhase.Ended, Px(f));
            yield return Frame();

            Assert.IsTrue(_line.B == new Vec2(0.5, 0.5), $"the grabbed end lands EXACTLY on the corner, got {_line.B.X:R}, {_line.B.Y:R}");
            Assert.AreEqual(0.35, _line.A.X, 1e-12, "the other end moved by the same t");
            Assert.AreEqual(0.4, _line.A.Y, 1e-12);
            Assert.AreEqual(1, _boards, "exactly one BoardChanged");
            AssertSame(_circle, circleBefore, "unselected circle");
            AssertSame(_far, farBefore, "unselected line");
            Assert.AreEqual(0, _session.Preview.Count, "ghost removed");
            AssertSelected("selection kept", _line);
        }

        [UnityTest]
        public IEnumerator B_ScaleGrowsCircleAboutCentreAndSnapsRadiusToDot()
        {
            yield return Frame();
            Select(ToolId.Scale, _circle);
            Assert.IsTrue(_session.TryGetSelectionBounds(out BoardRect bb));
            Vec2 p = V(0.5 * (bb.MinX + bb.MaxX), 0.5 * (bb.MinY + bb.MaxY));
            Vec2 dot = V(0.5, 0.5);   // grid corner, 0.2236 from the centre
            double dist = Vec2.Distance(dot, p);
            Vec2 d = V((dot.X - p.X) / dist, (dot.Y - p.Y) / dist);
            Vec2 f0 = V(p.X + 0.1 * d.X, p.Y + 0.1 * d.Y);   // 0.1 from the pivot toward the dot: the handle is the ring point there
            double reach = 0.1 * (dist + 0.004) / _circle.Radius;   // finger distance that puts the raw handle 0.004 past the dot
            Vec2 f = V(p.X + reach * d.X, p.Y + reach * d.Y);
            _boards = 0;
            _toasts.Clear();

            Touch(1, InputTouchPhase.Began, Px(f0));
            yield return Frame();
            yield return Move(1, Px(f0), Px(f));
            Assert.Greater(_session.Preview.Count, 0, "orange ghost while scaling");
            Assert.AreEqual(0.08, _circle.Radius, 0.0, "model untouched before release");
            Touch(1, InputTouchPhase.Ended, Px(f));
            yield return Frame();

            Assert.AreEqual(dist, _circle.Radius, 1e-12, "radius snapped to the pivot-dot distance");
            Assert.AreEqual(0.3, _circle.Center.X, 1e-12, "grew about its own centre (x)");
            Assert.AreEqual(0.6, _circle.Center.Y, 1e-12, "(y)");
            Assert.AreEqual(1, _boards, "one BoardChanged");
            Assert.AreEqual(1, _toasts.Count, $"one toast, got [{string.Join(" | ", _toasts)}]");
            StringAssert.StartsWith("Scaled to ", _toasts[0]);
            StringAssert.EndsWith("%", _toasts[0]);
        }

        [UnityTest]
        public IEnumerator C_DuplicatePlacesCopyThenMoveMovesOnlyTheCopy()
        {
            yield return Frame();
            _session.SetSnapEnabled(false);   // placement by the finger alone
            Select(ToolId.Duplicate, _line);
            var lineBefore = _line.Clone();
            Layer layer = _session.ActiveLayer;
            int count = layer.Shapes.Count;
            _boards = 0;
            _toasts.Clear();

            yield return Drag(Px(0.6, 0.15), Px(0.7, 0.25));
            Assert.AreEqual(count + 1, layer.Shapes.Count, "one copy added to the original's layer");
            var copy = layer.Shapes[layer.Shapes.Count - 1] as LineShape;
            Assert.NotNull(copy, "the copy is a line");
            Assert.AreNotEqual(_line.Id, copy.Id, "new id");
            Assert.AreEqual(_line.A.X + 0.1, copy.A.X, 0.005, "copy placed by the drag (x)");
            Assert.AreEqual(_line.A.Y + 0.1, copy.A.Y, 0.005, "(y)");
            Assert.AreEqual(copy.B.X - copy.A.X, 0.15, 1e-9, "same shape");
            AssertSame(_line, lineBefore, "original untouched");
            AssertSelected("the copy is the selection", copy);
            Assert.AreEqual(ToolId.Move, _session.ActiveTool, "the tool switched to Move");
            Assert.AreEqual(1, _boards, "one BoardChanged");
            CollectionAssert.Contains(_toasts, EditorSession.DuplicatedToast(1));

            var copyBefore = copy.Clone();
            yield return Drag(Px(0.8, 0.1), Px(0.85, 0.2));
            Assert.AreEqual(count + 1, layer.Shapes.Count, "Move adds nothing");
            Assert.AreEqual(((LineShape)copyBefore).A.X + 0.05, copy.A.X, 0.005, "the copy moved (x)");
            Assert.AreEqual(((LineShape)copyBefore).A.Y + 0.1, copy.A.Y, 0.005, "(y)");
            AssertSame(_line, lineBefore, "the original stays where it was");
            Assert.AreEqual(2, _boards, "one BoardChanged per commit");
        }

        [UnityTest]
        public IEnumerator D_SecondFingerMidMoveRestoresGeometryExactly()
        {
            yield return Frame();
            Select(ToolId.Move, _line, _circle);
            var lineBefore = _line.Clone();
            var circleBefore = _circle.Clone();
            _boards = 0;
            _toasts.Clear();

            Vector2 a = Px(0.7, 0.2), b = Px(0.8, 0.3);
            Touch(1, InputTouchPhase.Began, a);
            yield return Frame();
            yield return Move(1, a, b, 16, 10);
            Assert.GreaterOrEqual(_session.Preview.Count, 2, "ghosts of both shapes while dragging");

            Touch(2, InputTouchPhase.Began, Px(0.9, 0.9));
            yield return Frame();
            AssertSame(_line, lineBefore, "second finger: line bit-identical");
            AssertSame(_circle, circleBefore, "second finger: circle bit-identical");
            Assert.AreEqual(0, _session.Preview.Count, "ghosts removed");
            Assert.IsFalse(_session.Cursor.HasValue, "cursor cleared");
            Assert.IsFalse(_session.GestureActive);

            Touch(2, InputTouchPhase.Ended, Px(0.9, 0.9));
            yield return Frame();
            yield return Move(1, b, Px(0.85, 0.35), 4, 2);
            Touch(1, InputTouchPhase.Ended, Px(0.85, 0.35));
            yield return Frame();
            AssertSame(_line, lineBefore, "lifting the first finger changes nothing");
            AssertSame(_circle, circleBefore, "lifting the first finger changes nothing");
            Assert.AreEqual(0, _boards, "no BoardChanged");
            Assert.AreEqual(3, _session.ActiveLayer.Shapes.Count, "nothing added");
            AssertSelected("selection kept", _line, _circle);
        }

        [UnityTest]
        public IEnumerator E_TouchStartingOnActionBarButtonNeverReachesTheBoard()
        {
            // The board covers the whole screen here, so the bar button is over the board: only the UI check can stop it.
            _space.OX = 0; _space.OY = 0;
            _space.W = Mathf.Max(1, Screen.width); _space.H = Mathf.Max(1, Screen.height);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _uiGo = new GameObject("VerifierToolBar");
            _uiGo.AddComponent<ToolBar>();
            yield return Frame();   // ToolBar.Start, router Start
            Select(ToolId.Move, _line);
            yield return Frame();
            Canvas.ForceUpdateCanvases();
            yield return Frame();

            GameObject button = null;
            foreach (var t in _uiGo.GetComponentsInChildren<Transform>(true))
                if (t.name == "Move") button = t.gameObject;
            Assert.NotNull(button, "action bar Move button");
            Assert.IsTrue(button.activeInHierarchy, "the bar is shown (placed, selection, Move)");
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            Vector2 centre = (corners[0] + corners[2]) * 0.5f;
            Assert.IsTrue(_space.TryScreenToBoard(new Vec2(centre.x, centre.y), out _), $"precondition: button centre {centre} is over the board");

            var es = _esGo.GetComponent<EventSystem>();
            var hits = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = centre }, hits);
            Debug.Log($"[Verifier] action bar probe at {centre} on {Screen.width}x{Screen.height}: {hits.Count} hits");
            if (hits.Count == 0) Assert.Inconclusive($"uGUI raycasts hit nothing in batch Play Mode (screen {Screen.width}x{Screen.height})");

            var lineBefore = _line.Clone();
            _boards = 0;
            Touch(1, InputTouchPhase.Began, centre);
            yield return Frame();
            Assert.IsFalse(_session.GestureActive, "a press on the bar button opens no board gesture (no Down reached the session)");
            Vector2 to = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);
            for (int i = 1; i <= 8; i++)
            {
                Touch(1, InputTouchPhase.Moved, Vector2.Lerp(centre, to, i / 8f));
                yield return Frame();
                Assert.IsFalse(_session.GestureActive, $"still no gesture after move {i}");
            }
            Touch(1, InputTouchPhase.Ended, to);
            yield return Frame();
            AssertSame(_line, lineBefore, "nothing moved");
            Assert.AreEqual(0, _boards, "no BoardChanged");
            Assert.AreEqual(3, _session.ActiveLayer.Shapes.Count, "nothing added");
        }
    }
}
