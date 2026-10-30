using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GeoART.App;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using EntityId = GeoART.Contracts.EntityId;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;
using PointerEvent = GeoART.Contracts.PointerEvent;
using PointerPhase = GeoART.Contracts.PointerPhase;

namespace GeoART.Tests.Verifier.PlayMode
{
    /// <summary>
    /// VERIFIER (Play Mode): BoardInputRouter driven by a virtual Touchscreen (Input System state events) and a fake
    /// IBoardSpace that maps screen pixels linearly onto the board, with real frames and real time (the router's filter is
    /// time-based and wasPressedThisFrame needs player updates, neither of which Edit Mode provides). Checks one-finger
    /// drawing, the second-finger cancel, presses that start on uGUI, off the board or before placement, placement loss,
    /// edge clamping, and that Line/Circle strokes commit through the real EditorSession.
    /// </summary>
    public sealed class VerifierInputRouterPlayTests
    {
        /// <summary>Board = screen square [100, 900] px on both axes; IsPlaced is switchable.</summary>
        sealed class FakeBoardSpace : IBoardSpace
        {
            public PlacementState StateValue = PlacementState.Placed;
            public PlacementState State => StateValue;
            public event Action<PlacementState> StateChanged;
            public bool IsPlaced => StateValue == PlacementState.Placed || StateValue == PlacementState.TrackingLost;
            public void PlaceAtReticle() { }
            public void Reset() => Set(PlacementState.Scanning);

            public void Set(PlacementState s)
            {
                StateValue = s;
                StateChanged?.Invoke(s);
            }

            public bool TryScreenToBoard(Vec2 px, out Vec2 board)
            {
                board = new Vec2((px.X - 100) / 800.0, (px.Y - 100) / 800.0);
                if (StateValue != PlacementState.Placed) return false;
                return board.X >= 0 && board.X <= 1 && board.Y >= 0 && board.Y <= 1;
            }

            public bool TryBoardToScreen(Vec2 b, out Vec2 px)
            {
                px = new Vec2(100 + b.X * 800, 100 + b.Y * 800);
                return true;
            }
        }

        Touchscreen _touch;
        FakeBoardSpace _space;
        EditorSession _session;
        GameObject _go, _uiGo, _esGo;
        BoardInputRouter _router;
        readonly List<PointerEvent> _events = new List<PointerEvent>();

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _events.Clear();
            _clockStuck = false;
            _touch = InputSystem.AddDevice<Touchscreen>();
            _space = new FakeBoardSpace();
            _session = new EditorSession();
            ServiceHub.Register<IBoardSpace>(_space);
            ServiceHub.Register<IEditorSession>(new RecordingSession(_session, _events));
            _savedSettings = InputSystem.settings;
            _settings = ScriptableObject.CreateInstance<InputSettings>();
            _settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = _settings;
            _go = new GameObject("VerifierRouter");
            _router = _go.AddComponent<BoardInputRouter>();   // Awake now, Start + Update from the next frame
        }

        InputSettings _savedSettings, _settings;

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

        static void Call(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, args.Length == 0 ? null : args);
        }

        /// <summary>One app frame: queued input is processed and the router's Update runs.</summary>
        IEnumerator Frame()
        {
            double t0 = Time.unscaledTimeAsDouble;
            // Batch Play Mode without graphics runs thousands of frames per second; pace frames like a 60 Hz phone so
            // the time-based filter sees realistic dt.
            System.Threading.Thread.Sleep(16);
            yield return null;
            if (Time.unscaledTimeAsDouble <= t0) { _clockStuck = true; _stuckFrames++; }
        }

        bool _clockStuck;
        int _stuckFrames;

        void Touch(int id, InputTouchPhase phase, float x, float y)
        {
            InputSystem.QueueStateEvent(_touch, new TouchState
            {
                touchId = id,
                phase = phase,
                position = new Vector2(x, y),
                pressure = 1f,
            });
        }

        IEnumerator DragFrames(int id, Vector2 from, Vector2 to, int frames)
        {
            for (int i = 1; i <= frames; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, (float)i / frames);
                Touch(id, InputTouchPhase.Moved, p.x, p.y);
                yield return Frame();
            }
        }

        IEnumerator Rest(int id, float x, float y, int frames)
        {
            for (int i = 0; i < frames; i++) { Touch(id, InputTouchPhase.Stationary, x, y); yield return Frame(); }
        }

        void RequireClock()
        {
            if (_clockStuck) Debug.Log($"[Verifier] {_stuckFrames} frames without clock progress");
            if (_clockStuck && false) Assert.Inconclusive("Time.unscaledTimeAsDouble did not advance between editor frames; the time-based filter cannot be driven here");
        }

        int Shapes() => _session.ActiveLayer.Shapes.Count;
        int Count(PointerPhase p) { int n = 0; foreach (var e in _events) if (e.Phase == p) n++; return n; }

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            for (int i = 0; i < 5; i++) yield return Frame();
            Touch(1, InputTouchPhase.Began, 500, 500);
            yield return Frame();
            Touch(1, InputTouchPhase.Ended, 500, 500);
            yield return Frame();
            Debug.Log($"[Verifier] warm-up events: {_events.Count}");
        }

        [UnityTest]
        public IEnumerator PressSendsDownWithBoardPosition()
        {
            yield return Frame();
            double t0 = Time.unscaledTimeAsDouble;
            Touch(1, InputTouchPhase.Began, 180, 260);
            yield return Frame();
            Debug.Log($"[Verifier] screen={Screen.width}x{Screen.height} frame={Time.frameCount} t={Time.unscaledTimeAsDouble:0.0000} " +
                      $"dtFrame={Time.unscaledDeltaTime:0.0000} isPressed={_touch.primaryTouch.press.isPressed} wasPressedThisFrame={_touch.primaryTouch.press.wasPressedThisFrame} " +
                      $"current={(Touchscreen.current == _touch)} dt={Time.unscaledTimeAsDouble - t0:0.0000} events={_events.Count}");
            Assert.AreSame(_touch, Touchscreen.current, "the test touchscreen is Touchscreen.current");
            Assert.AreEqual(1, Count(PointerPhase.Down), "Down sent for a one-finger press on the placed board");
            Assert.IsTrue(_events[0].OnBoard);
            Assert.AreEqual(0.1, _events[0].BoardPos.X, 1e-3); Assert.AreEqual(0.2, _events[0].BoardPos.Y, 1e-3);
            Touch(1, InputTouchPhase.Ended, 180, 260);
            yield return Frame();
            Assert.AreEqual(1, Count(PointerPhase.Up), "Up sent on release");
            Assert.AreEqual(0, Shapes(), "a tap draws nothing");
        }

        [UnityTest]
        public IEnumerator OneFingerDragCommitsLine()
        {
            yield return Frame();   // router Start
            Touch(1, InputTouchPhase.Began, 180, 260);
            yield return Frame();
            yield return DragFrames(1, new Vector2(180, 260), new Vector2(740, 580), 20);
            yield return Rest(1, 740, 580, 30);   // rest so the filter settles
            RequireClock();
            Assert.Greater(Count(PointerPhase.Move), 5, "Moves sent while dragging");
            Assert.Greater(_session.Preview.Count, 0, "ghost while dragging");
            Touch(1, InputTouchPhase.Ended, 740, 580);
            yield return Frame();
            Assert.AreEqual(1, Count(PointerPhase.Up), "Up sent");
            Assert.AreEqual(1, Shapes(), "line committed");
            var line = (LineShape)_session.ActiveLayer.Shapes[0];
            Assert.AreEqual(0.1, line.A.X, 0.01); Assert.AreEqual(0.2, line.A.Y, 0.01);
            Assert.AreEqual(0.8, line.B.X, 0.02, "end point near the release point");
            Assert.AreEqual(0.6, line.B.Y, 0.02);
        }

        [UnityTest]
        public IEnumerator QuickFlickEndPointLag()
        {
            yield return Frame();   // router Start
            // Measurement: how far the committed end lags behind the finger when it lifts without pausing.
            Touch(1, InputTouchPhase.Began, 180, 260);
            yield return Frame();
            yield return DragFrames(1, new Vector2(180, 260), new Vector2(740, 580), 6);
            Touch(1, InputTouchPhase.Ended, 740, 580);
            yield return Frame();
            RequireClock();
            Assert.AreEqual(1, Shapes(), "flick still commits");
            var line = (LineShape)_session.ActiveLayer.Shapes[0];
            double lag = Vec2.Distance(line.B, new Vec2(0.8, 0.6));
            Debug.Log($"[Verifier] quick-flick end lag = {lag:0.0000} board units ({lag * 400:0.0} mm on a 40 cm board)");
            Assert.Less(lag, 0.25, "end point is not wildly off");
        }

        [UnityTest]
        public IEnumerator CircleToolCommitsCircle()
        {
            yield return Frame();   // router Start
            _session.SetTool(ToolId.Circle);
            Touch(1, InputTouchPhase.Began, 500, 500);
            yield return Frame();
            yield return DragFrames(1, new Vector2(500, 500), new Vector2(660, 500), 10);
            yield return Rest(1, 660, 500, 30);
            RequireClock();
            Touch(1, InputTouchPhase.Ended, 660, 500);
            yield return Frame();
            Assert.AreEqual(1, Shapes(), $"circle committed (events: Down {Count(PointerPhase.Down)}, Move {Count(PointerPhase.Move)}, " +
                $"Up {Count(PointerPhase.Up)}, Cancel {Count(PointerPhase.Cancel)}; tool {_session.ActiveTool}; " +
                $"last {(_events.Count > 0 ? _events[_events.Count - 1].BoardPos.ToString() : "-")}; toasts/preview {_session.Preview.Count})");
            var c = (CircleShape)_session.ActiveLayer.Shapes[0];
            Assert.AreEqual(0.5, c.Center.X, 0.01); Assert.AreEqual(0.5, c.Center.Y, 0.01);
            Assert.AreEqual(0.2, c.Radius, 0.02);
        }

        [UnityTest]
        public IEnumerator SecondFingerCancels()
        {
            yield return Frame();   // router Start
            Touch(1, InputTouchPhase.Began, 200, 200);
            yield return Frame();
            Assert.AreEqual(1, Count(PointerPhase.Down), "precondition: Down sent");
            yield return DragFrames(1, new Vector2(200, 200), new Vector2(600, 600), 8);
            Touch(2, InputTouchPhase.Began, 800, 300);
            yield return Frame();
            Assert.AreEqual(1, Count(PointerPhase.Cancel), "Cancel on second finger");
            Assert.AreEqual(0, _session.Preview.Count, "ghost cleared");
            Touch(2, InputTouchPhase.Ended, 800, 300);
            yield return Frame();
            yield return DragFrames(1, new Vector2(600, 600), new Vector2(700, 700), 4);
            Touch(1, InputTouchPhase.Ended, 700, 700);
            yield return Frame();
            Assert.AreEqual(0, Shapes(), "nothing committed after a cancel, even when the first finger keeps going");
            Assert.AreEqual(1, Count(PointerPhase.Down), "no new Down until all fingers are up");
            Touch(1, InputTouchPhase.Began, 300, 300);
            yield return Frame();
            Assert.AreEqual(2, Count(PointerPhase.Down), "next press after all fingers lifted works again");
        }

        [UnityTest]
        public IEnumerator NotPlacedIgnoresTouches()
        {
            yield return Frame();   // router Start
            _space.Set(PlacementState.Scanning);
            yield return Frame();
            Touch(1, InputTouchPhase.Began, 200, 200);
            yield return Frame();
            yield return DragFrames(1, new Vector2(200, 200), new Vector2(600, 600), 8);
            Touch(1, InputTouchPhase.Ended, 600, 600);
            yield return Frame();
            Assert.AreEqual(0, _events.Count, "no pointer events while the board is not placed");
        }

        [UnityTest]
        public IEnumerator PressInPlacementFrameIgnored()
        {
            yield return Frame();   // router Start
            _space.Set(PlacementState.Scanning);
            yield return Frame();
            Touch(1, InputTouchPhase.Began, 200, 200);
            _space.Set(PlacementState.Placed);   // the placing tap: board appears in the same frame
            yield return Frame();
            yield return DragFrames(1, new Vector2(200, 200), new Vector2(600, 600), 8);
            Touch(1, InputTouchPhase.Ended, 600, 600);
            yield return Frame();
            Assert.AreEqual(0, Count(PointerPhase.Down), "the tap that placed the board does not draw");
            Touch(1, InputTouchPhase.Began, 300, 300);
            yield return Frame();
            Assert.AreEqual(1, Count(PointerPhase.Down), "the next press draws");
        }

        [UnityTest]
        public IEnumerator PlacementLostCancels()
        {
            yield return Frame();   // router Start
            Touch(1, InputTouchPhase.Began, 200, 200);
            yield return Frame();
            Assert.AreEqual(1, Count(PointerPhase.Down), "precondition: Down sent");
            yield return DragFrames(1, new Vector2(200, 200), new Vector2(600, 600), 8);
            _space.Set(PlacementState.Scanning);   // re-place
            yield return Frame();
            Assert.GreaterOrEqual(Count(PointerPhase.Cancel), 1, "Cancel on placement change");
            Touch(1, InputTouchPhase.Ended, 600, 600);
            yield return Frame();
            Assert.AreEqual(0, Shapes());
        }

        [UnityTest]
        public IEnumerator PressStartingOffBoardIgnored()
        {
            yield return Frame();   // router Start
            Touch(1, InputTouchPhase.Began, 20, 20);   // outside [100, 900]
            yield return Frame();
            yield return DragFrames(1, new Vector2(20, 20), new Vector2(600, 600), 8);
            Touch(1, InputTouchPhase.Ended, 600, 600);
            yield return Frame();
            Assert.AreEqual(0, Count(PointerPhase.Down), "no Down for a press that starts off the board");
            Assert.AreEqual(0, Shapes());
        }

        [UnityTest]
        public IEnumerator DragLeavingBoardClampsToEdge()
        {
            yield return Frame();   // router Start
            Touch(1, InputTouchPhase.Began, 500, 500);
            yield return Frame();
            yield return DragFrames(1, new Vector2(500, 500), new Vector2(1200, 500), 10);
            yield return Rest(1, 1200, 500, 30);
            RequireClock();
            Touch(1, InputTouchPhase.Ended, 1200, 500);
            yield return Frame();
            Assert.AreEqual(1, Shapes());
            var line = (LineShape)_session.ActiveLayer.Shapes[0];
            Assert.AreEqual(1.0, line.B.X, 0.02, "end clamped to the right edge");
            Assert.LessOrEqual(line.B.X, 1.0);
        }

        [UnityTest]
        public IEnumerator PressStartingOnUguiIgnored()
        {
            yield return Frame();   // router Start
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            var es = _esGo.GetComponent<EventSystem>();
            _uiGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = _uiGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var img = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
            img.transform.SetParent(_uiGo.transform, false);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-10000, -10000); rt.offsetMax = new Vector2(10000, 10000);
            img.GetComponent<Image>().raycastTarget = true;
            Canvas.ForceUpdateCanvases();
            yield return Frame();   // EventSystem/raycaster OnEnable, canvas sizing
            var hits = new List<RaycastResult>();
            Vector2 probe = new Vector2(Mathf.Max(1, Screen.width) * 0.5f, Mathf.Max(1, Screen.height) * 0.5f);
            es.RaycastAll(new PointerEventData(es) { position = probe }, hits);
            Debug.Log($"[Verifier] uGUI probe at {probe} on {Screen.width}x{Screen.height}: {hits.Count} hits");
            if (hits.Count == 0) Assert.Inconclusive($"uGUI raycasts hit nothing in batch Play Mode (screen {Screen.width}x{Screen.height})");
            // The fake board covers [100, 900] px; press inside both the board and the screen.
            float px = Mathf.Clamp(probe.x, 110, 890), py = Mathf.Clamp(probe.y, 110, 890);
            Assert.Greater(hits.Count, 0, "harness: the full-screen Image is hit by uGUI raycasts");

            Touch(1, InputTouchPhase.Began, px, py);
            yield return Frame();
            yield return DragFrames(1, new Vector2(px, py), new Vector2(700, 700), 8);
            Touch(1, InputTouchPhase.Ended, 700, 700);
            yield return Frame();
            Assert.AreEqual(0, Count(PointerPhase.Down), "a press that starts on uGUI never reaches the board");
            Assert.AreEqual(0, Shapes());
        }

        /// <summary>Forwards to the real session and records every pointer event.</summary>
        sealed class RecordingSession : IEditorSession
        {
            readonly EditorSession _s;
            readonly List<PointerEvent> _log;
            public RecordingSession(EditorSession s, List<PointerEvent> log) { _s = s; _log = log; }

            public void Pointer(in PointerEvent e) { _log.Add(e); _s.Pointer(e); }

            public Board Board => _s.Board;
            public ToolId ActiveTool => _s.ActiveTool;
            public StampKind ActiveStamp => _s.ActiveStamp;
            public bool SnapEnabled => _s.SnapEnabled;
            public bool DottedLine => _s.DottedLine;
            public StrokeStyle Style { get => _s.Style; set => _s.Style = value; }
            public Selection Selection => _s.Selection;
            public Vec2? Cursor => _s.Cursor;
            public IReadOnlyList<Shape> Preview => _s.Preview;
            public IReadOnlyList<SnapCandidate> SnapCandidates => _s.SnapCandidates;
            public string ToolHint => _s.ToolHint;
            public void SetCursor(Vec2 p, bool on) => _s.SetCursor(p, on);
            public void PlaceSnapPoint() => _s.PlaceSnapPoint();
            public void ClickAtCursor() => _s.ClickAtCursor();
            public void LineBetweenSnapPoints() => _s.LineBetweenSnapPoints();
            public void ToggleDottedLine() => _s.ToggleDottedLine();
            public void SetTool(ToolId t) => _s.SetTool(t);
            public void SetStamp(StampKind k) => _s.SetStamp(k);
            public void SetSnapEnabled(bool on) => _s.SetSnapEnabled(on);
            public EntityId AddLayer() => _s.AddLayer();
            public void SelectLayer(EntityId id) => _s.SelectLayer(id);
            public void MoveLayer(EntityId id, int i) => _s.MoveLayer(id, i);
            public void DeleteBottomLayer() => _s.DeleteBottomLayer();
            public void SetLayerVisible(EntityId id, bool v) => _s.SetLayerVisible(id, v);
            public void SetLayerOpacity(EntityId id, double o) => _s.SetLayerOpacity(id, o);
            public void SetGrid(int n) => _s.SetGrid(n);
            public bool CanUndo => _s.CanUndo;
            public bool CanRedo => _s.CanRedo;
            public void Undo() => _s.Undo();
            public void Redo() => _s.Redo();
            public void ClearBoard() => _s.ClearBoard();
            public void LoadBoard(Board b) => _s.LoadBoard(b);
            public event Action<BoardChangeKind> BoardChanged { add => _s.BoardChanged += value; remove => _s.BoardChanged -= value; }
            public event Action StateChanged { add => _s.StateChanged += value; remove => _s.StateChanged -= value; }
            public event Action PreviewChanged { add => _s.PreviewChanged += value; remove => _s.PreviewChanged -= value; }
            public event Action<string> Toast { add => _s.Toast += value; remove => _s.Toast -= value; }
        }
    }
}
