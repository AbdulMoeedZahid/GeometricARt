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
    /// VERIFIER (Play Mode): the Undo button end to end. A virtual Touchscreen drives the real BoardInputRouter into the
    /// real EditorSession (ScreenSnapMetric over a fake, always-Placed IBoardSpace whose board covers the WHOLE screen, so
    /// the Undo button sits over the board and only the router's UI check can keep its touches off the board), with the
    /// real ToolBar and an EventSystem.
    /// No UI input module runs here, so the click a released touch would produce is dispatched by the test to the click
    /// handler of the topmost uGUI raycast hit at the press point (asserted to be the Undo button), right after the touch
    /// is released; the touch itself still goes through the router.
    ///   (a) Draw a line by touch, tap Undo: the line is gone, one BoardChanged(History), the press opened no gesture.
    ///   (b) Three strokes, three taps: empty board, dimmed button, toasts newest first; a 4th tap: "Nothing to undo".
    ///   (c) Finger 1 mid-drag, finger 2 taps Undo: the open line never commits, the previous stroke is undone.
    ///   (d) Select + Move by touch, tap Undo: exact old coordinates, selection and tool kept.
    ///   (e) A press that starts on Undo (dimmed and bright) and drags across the board opens no gesture.
    ///   (f) While the layer sidebar's opacity popup is open, its blocker covers the Undo button.
    /// </summary>
    public sealed class VerifierUndoPlayTests
    {
        sealed class FakeBoardSpace : IBoardSpace
        {
            public double OX, OY, W = 800, H = 800;
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

        const float DisabledAlpha = 0.4f;

        Touchscreen _touch;
        FakeBoardSpace _space;
        EditorSession _session;
        BoardInputRouter _router;
        GameObject _go, _uiGo, _esGo, _sideGo;
        EventSystem _es;
        InputSettings _savedSettings, _settings;
        readonly List<string> _toasts = new List<string>();
        readonly List<BoardChangeKind> _boards = new List<BoardChangeKind>();

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
            _go = new GameObject("VerifierUndoRouter");
            _router = _go.AddComponent<BoardInputRouter>();
            _toasts.Clear();
            _boards.Clear();
            _session.Toast += t => _toasts.Add(t);
            _session.BoardChanged += k => _boards.Add(k);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            if (_uiGo != null) UnityEngine.Object.DestroyImmediate(_uiGo);
            if (_sideGo != null) UnityEngine.Object.DestroyImmediate(_sideGo);
            if (_esGo != null) UnityEngine.Object.DestroyImmediate(_esGo);
            if (_touch != null) InputSystem.RemoveDevice(_touch);
            if (_savedSettings != null) InputSystem.settings = _savedSettings;
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            ServiceHub.Clear();
        }

        // ---------- helpers ----------

        static IEnumerator Frame()
        {
            System.Threading.Thread.Sleep(16);
            yield return null;
        }

        void Touch(int id, InputTouchPhase phase, Vector2 p) =>
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = id, phase = phase, position = p, pressure = 1f });

        Vector2 Px(double x, double y) => new Vector2((float)(_space.OX + x * _space.W), (float)(_space.OY + y * _space.H));

        /// <summary>The board covers the whole screen; ToolBar, EventSystem; a few frames so Start has run everywhere.</summary>
        IEnumerator MakeUi()
        {
            _space.OX = 0;
            _space.OY = 0;
            _space.W = Mathf.Max(1, Screen.width);
            _space.H = Mathf.Max(1, Screen.height);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            _uiGo = new GameObject("VerifierToolBar");
            _uiGo.AddComponent<ToolBar>();
            yield return Frame();
            yield return Frame();
            Canvas.ForceUpdateCanvases();
            yield return Frame();
            Assert.IsTrue(UndoGo.activeInHierarchy, "precondition: Undo shown while Placed");
        }

        GameObject Child(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        GameObject UndoGo => Child(_uiGo, "Undo");
        float Alpha => UndoGo.GetComponent<CanvasGroup>().alpha;

        Rect UndoRect()
        {
            var c = new Vector3[4];
            ((RectTransform)UndoGo.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        List<RaycastResult> RaycastAt(Vector2 at)
        {
            var hits = new List<RaycastResult>();
            _es.RaycastAll(new PointerEventData(_es) { position = at }, hits);
            return hits;
        }

        /// <summary>The topmost uGUI hit at the Undo centre must be the Undo button (else the batch run has no raycasts).</summary>
        GameObject UndoHandlerAtCentre(out Vector2 at)
        {
            at = UndoRect().center;
            var hits = RaycastAt(at);
            Debug.Log($"[Verifier] Undo probe at {at} on {Screen.width}x{Screen.height}: {hits.Count} hits");
            if (hits.Count == 0) Assert.Inconclusive($"uGUI raycasts hit nothing in batch Play Mode (screen {Screen.width}x{Screen.height})");
            GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.AreSame(UndoGo, handler, $"the topmost UI at the Undo centre is the Undo button, got {hits[0].gameObject.name}");
            Assert.IsTrue(_space.TryScreenToBoard(new Vec2(at.x, at.y), out _), "precondition: the Undo button is over the board");
            return handler;
        }

        /// <summary>
        /// Press and release finger <paramref name="id"/> on the Undo button (no board gesture may open), then the click the
        /// UI module would send on release.
        /// </summary>
        IEnumerator TapUndo(int id = 1)
        {
            GameObject handler = UndoHandlerAtCentre(out Vector2 at);
            bool alone = id == 1;
            Touch(id, InputTouchPhase.Began, at);
            yield return Frame();
            if (alone) Assert.IsFalse(_router.GestureOpen || _session.GestureActive, "a press on Undo opens no board gesture");
            Touch(id, InputTouchPhase.Stationary, at);
            yield return Frame();
            if (alone) Assert.IsFalse(_router.GestureOpen || _session.GestureActive, "still no gesture while held");
            Touch(id, InputTouchPhase.Ended, at);
            yield return Frame();
            ExecuteEvents.Execute(handler, new PointerEventData(_es) { position = at, button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
            yield return Frame();
        }

        IEnumerator Move(int id, Vector2 a, Vector2 b, int frames = 12, int rest = 20)
        {
            for (int i = 1; i <= frames; i++)
            {
                Touch(id, InputTouchPhase.Moved, Vector2.Lerp(a, b, i / (float)frames));
                yield return Frame();
            }
            for (int i = 0; i < rest; i++) { Touch(id, InputTouchPhase.Stationary, b); yield return Frame(); }
        }

        IEnumerator Drag(Vector2 a, Vector2 b)
        {
            Touch(1, InputTouchPhase.Began, a);
            yield return Frame();
            yield return Move(1, a, b);
            Touch(1, InputTouchPhase.Ended, b);
            yield return Frame();
            yield return Frame();
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

        Shape FindShape(EntityId id)
        {
            foreach (var l in _session.Board.Layers)
                foreach (var s in l.Shapes)
                    if (s.Id == id) return s;
            return null;
        }

        // ---------- tests ----------

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
        public IEnumerator A_TapUndoRemovesTheLineWithOneHistoryBoardChanged()
        {
            yield return MakeUi();
            _session.SetSnapEnabled(false);
            Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "dimmed before any edit");
            yield return Drag(Px(0.3, 0.45), Px(0.6, 0.55));
            Assert.AreEqual(1, ShapeCount, "precondition: the touch drew a line");
            Assert.AreEqual(1f, Alpha, 1e-4f, "bright after the stroke");

            _boards.Clear();
            _toasts.Clear();
            yield return TapUndo();
            Assert.AreEqual(0, ShapeCount, "the line is gone");
            Assert.AreEqual(1, _boards.Count, $"exactly one BoardChanged, got {_boards.Count}");
            Assert.IsTrue((_boards[0] & BoardChangeKind.History) != 0, $"it carries History, got {_boards[0]}");
            CollectionAssert.AreEqual(new[] { "Undid: Line" }, _toasts);
            Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "dimmed again");
            Assert.IsFalse(_router.GestureOpen || _session.GestureActive, "no gesture after the release");
        }

        [UnityTest]
        public IEnumerator B_ThreeStrokesThreeTapsThenNothingToUndo()
        {
            yield return MakeUi();
            _session.SetSnapEnabled(false);
            yield return Drag(Px(0.25, 0.4), Px(0.55, 0.45));
            _session.SetTool(ToolId.Circle);
            yield return Drag(Px(0.5, 0.6), Px(0.6, 0.6));
            _session.SetTool(ToolId.Line);
            yield return Drag(Px(0.3, 0.7), Px(0.6, 0.72));
            Assert.AreEqual(3, ShapeCount, "precondition: three strokes");
            Assert.AreEqual(3, _session.UndoCount, "three steps");

            _toasts.Clear();
            for (int i = 0; i < 3; i++) yield return TapUndo();
            Assert.AreEqual(0, ShapeCount, "the board is empty");
            CollectionAssert.AreEqual(new[] { "Undid: Line", "Undid: Circle", "Undid: Line" }, _toasts, "newest first");
            Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "dimmed");
            Assert.AreEqual(1, _session.Board.Layers.Count, "one layer");

            _toasts.Clear();
            _boards.Clear();
            yield return TapUndo();
            CollectionAssert.AreEqual(new[] { "Nothing to undo" }, _toasts, "4th tap");
            Assert.AreEqual(0, _boards.Count, "nothing changed");
            Assert.AreEqual(0, ShapeCount, "nothing drawn where the tap landed");
        }

        [UnityTest]
        public IEnumerator C_SecondFingerTapsUndoWhileTheFirstDrags()
        {
            yield return MakeUi();
            _session.SetSnapEnabled(false);
            yield return Drag(Px(0.25, 0.4), Px(0.55, 0.45));   // the previous stroke
            Assert.AreEqual(1, ShapeCount, "precondition: previous stroke");
            _boards.Clear();
            _toasts.Clear();

            Vector2 a = Px(0.3, 0.6), b = Px(0.5, 0.65);
            Touch(1, InputTouchPhase.Began, a);
            yield return Frame();
            yield return Move(1, a, b, 8, 4);
            Assert.IsTrue(_session.GestureActive && _session.Preview.Count > 0, "precondition: a line ghost under finger 1");

            yield return TapUndo(2);
            Assert.IsFalse(_session.GestureActive, "the open line was cancelled");
            Assert.AreEqual(0, _session.Preview.Count, "no ghost");
            Assert.AreEqual(0, ShapeCount, "the previous stroke is undone");
            CollectionAssert.AreEqual(new[] { "Undid: Line" }, _toasts);

            yield return Move(1, b, Px(0.6, 0.7), 6, 4);
            Touch(1, InputTouchPhase.Ended, Px(0.6, 0.7));
            yield return Frame();
            yield return Frame();
            Assert.AreEqual(0, ShapeCount, "lifting finger 1 commits nothing");
            Assert.AreEqual(0, _session.UndoCount, "no step");
            Assert.AreEqual(1, _session.RedoCount, "the undone stroke is on the redo stack");
            Assert.AreEqual(1, _boards.Count, $"only the Undo's BoardChanged, got {_boards.Count}");
            Assert.IsTrue((_boards[0] & BoardChangeKind.History) != 0);
        }

        [UnityTest]
        public IEnumerator D_SelectMoveByTouchThenUndoRestoresExactly()
        {
            yield return MakeUi();
            Layer layer = _session.ActiveLayer;
            var line = new LineShape(_session.Board.NewId(), new Vec2(0.35, 0.45), new Vec2(0.5, 0.5), StrokeStyle.Default);
            layer.Shapes.Add(line);
            EntityId id = line.Id;
            Vec2 a0 = line.A, b0 = line.B;

            _session.SetTool(ToolId.SelectMove);
            yield return Drag(Px(0.3, 0.4), Px(0.56, 0.56));
            Assert.IsTrue(_session.Selection.Ids.Count == 1 && _session.Selection.Ids.Contains(id), "box-selected by touch");
            _session.SetTool(ToolId.Move);
            Assert.AreEqual(ToolId.Move, _session.ActiveTool);
            yield return Drag(Px(0.42, 0.62), Px(0.52, 0.7));
            var moved = (LineShape)FindShape(id);
            Assert.IsFalse(moved.A == a0, "precondition: the touch moved the line");
            Assert.AreEqual(1, _session.UndoCount, "one Move step");

            _toasts.Clear();
            yield return TapUndo();
            var back = (LineShape)FindShape(id);
            Assert.IsTrue(back.A == a0 && back.B == b0, $"exact old coordinates, got {back.A.X:R},{back.A.Y:R} {back.B.X:R},{back.B.Y:R}");
            Assert.IsTrue(_session.Selection.Ids.Count == 1 && _session.Selection.Ids.Contains(id), "still selected");
            Assert.AreEqual(ToolId.Move, _session.ActiveTool, "still Move");
            CollectionAssert.AreEqual(new[] { "Undid: Move" }, _toasts);
        }

        [UnityTest]
        public IEnumerator E_PressStartingOnUndoNeverReachesTheBoard()
        {
            yield return MakeUi();
            _session.SetSnapEnabled(false);
            foreach (bool bright in new[] { false, true })
            {
                if (bright)
                {
                    yield return Drag(Px(0.25, 0.4), Px(0.55, 0.45));
                    Assert.AreEqual(1f, Alpha, 1e-4f, "precondition: bright");
                }
                else Assert.AreEqual(DisabledAlpha, Alpha, 1e-4f, "precondition: dimmed");
                string what = bright ? "bright" : "dimmed";
                UndoHandlerAtCentre(out Vector2 at);
                int shapes = ShapeCount, steps = _session.UndoCount;
                _boards.Clear();

                Touch(1, InputTouchPhase.Began, at);
                yield return Frame();
                Assert.IsFalse(_session.GestureActive, $"{what}: a press on Undo opens no board gesture");
                Vector2 to = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);
                for (int i = 1; i <= 8; i++)
                {
                    Touch(1, InputTouchPhase.Moved, Vector2.Lerp(at, to, i / 8f));
                    yield return Frame();
                    Assert.IsFalse(_session.GestureActive, $"{what}: still no gesture after move {i}");
                }
                Touch(1, InputTouchPhase.Ended, to);
                yield return Frame();
                yield return Frame();
                Assert.AreEqual(shapes, ShapeCount, $"{what}: nothing drawn");
                Assert.AreEqual(steps, _session.UndoCount, $"{what}: no step");
                Assert.AreEqual(0, _boards.Count, $"{what}: no BoardChanged");
            }
        }

        [UnityTest]
        public IEnumerator F_OpacityPopupBlockerCoversTheUndoButton()
        {
            yield return MakeUi();
            UndoHandlerAtCentre(out Vector2 at);
            _sideGo = new GameObject("VerifierSidebar");
            _sideGo.AddComponent<LayerSidebar>();
            yield return Frame();
            yield return Frame();
            GameObject blocker = Child(_sideGo, "PopupBlocker");
            Assert.IsFalse(blocker.activeInHierarchy, "precondition: no popup");
            Assert.AreSame(UndoGo, ExecuteEvents.GetEventHandler<IPointerClickHandler>(RaycastAt(at)[0].gameObject),
                "without the popup, Undo is on top");

            blocker.SetActive(true);   // what opening the opacity popup does (with the popup itself)
            Canvas.ForceUpdateCanvases();
            yield return Frame();
            var hits = RaycastAt(at);
            Assert.Greater(hits.Count, 0);
            Assert.AreSame(blocker, hits[0].gameObject, $"the popup blocker is on top of Undo, got {hits[0].gameObject.name}");
            Assert.AreNotSame(UndoGo, ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), "a tap there cannot reach Undo");
        }
    }
}
