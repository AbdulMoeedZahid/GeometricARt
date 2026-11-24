using System;
using System.Collections;
using GeoART.App;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace GeoART.Tests.Verifier.PlayMode
{
    /// <summary>
    /// VERIFIER (Play Mode, ui-line-plan 2.1, P1): the Line hold menu through the REAL input path: a virtual Touchscreen,
    /// an EventSystem with InputSystemUIInputModule (as SceneBuilder makes it), the real ToolBar and BoardInputRouter, real
    /// frames and Time.unscaledTime. Checks that a tap clicks, a hold's release does not click, slide drags reach the
    /// chips, release-then-tap picks, the first touch on the board with the row open only closes it (nothing drawn), and
    /// moving before the hold cancels it. The board covers the whole screen.
    /// </summary>
    public sealed class VerifierLineMenuPlayTests
    {
        sealed class FullScreenBoard : IBoardSpace
        {
            public PlacementState State => PlacementState.Placed;
#pragma warning disable 67
            public event Action<PlacementState> StateChanged;
#pragma warning restore 67
            public bool IsPlaced => true;
            public void PlaceAtReticle() { }
            public void Reset() { }
            static float W => Mathf.Max(1, Screen.width);
            static float H => Mathf.Max(1, Screen.height);

            public bool TryScreenToBoard(Vec2 px, out Vec2 board)
            {
                board = new Vec2(px.X / W, px.Y / H);
                return board.X >= 0 && board.X <= 1 && board.Y >= 0 && board.Y <= 1;
            }

            public bool TryBoardToScreen(Vec2 b, out Vec2 px)
            {
                px = new Vec2(b.X * W, b.Y * H);
                return true;
            }
        }

        Touchscreen _touch;
        EditorSession _session;
        GameObject _esGo, _uiGo, _routerGo;
        InputSettings _savedSettings, _settings;
        int _downs;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _touch = InputSystem.AddDevice<Touchscreen>();
            _session = new EditorSession();
            _session.Pointer(new PointerEvent(PointerPhase.Cancel, default, false, 0));
            ServiceHub.Register<IBoardSpace>(new FullScreenBoard());
            ServiceHub.Register<IEditorSession>(_session);
            _savedSettings = InputSystem.settings;
            _settings = ScriptableObject.CreateInstance<InputSettings>();
            _settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = _settings;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { _routerGo, _uiGo, _esGo })
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
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

        void Touch(InputTouchPhase phase, Vector2 p) =>
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = 1, phase = phase, position = p, pressure = 1f });

        IEnumerator MakeUi()
        {
            _esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var module = _esGo.GetComponent<InputSystemUIInputModule>();
            if (module.actionsAsset == null) module.AssignDefaultActions();
            _uiGo = new GameObject("VerifierToolBar");
            _uiGo.AddComponent<ToolBar>();
            _routerGo = new GameObject("VerifierRouter");
            _routerGo.AddComponent<BoardInputRouter>();
            for (int i = 0; i < 4; i++) yield return Frame();
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(_uiGo.GetComponentInChildren<Canvas>(true).enabled, "precondition: toolbar shown (board placed)");
            _downs = 0;
        }

        GameObject Child(string name)
        {
            foreach (var t in _uiGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        static Vector2 Center(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;
        }

        ToolHoldMenu LineMenu => Child("Line").GetComponent<ToolHoldMenu>();
        int ShapeCount => _session.ActiveLayer.Shapes.Count;
        Vector2 EmptyBoard => new Vector2(Screen.width * 0.3f, Screen.height * 0.5f);

        IEnumerator Rest(Vector2 p, int frames)
        {
            for (int i = 0; i < frames; i++) { Touch(InputTouchPhase.Stationary, p); yield return Frame(); }
        }

        /// <summary>Press on the button and keep the finger still until the row opens (at most 1.5 s of real time).</summary>
        IEnumerator HoldOpen(GameObject button)
        {
            Vector2 p = Center(button);
            Touch(InputTouchPhase.Began, p);
            yield return Frame();
            float until = Time.unscaledTime + 1.5f;
            var menu = button.GetComponent<ToolHoldMenu>();
            while (!menu.IsOpen && Time.unscaledTime < until) { Touch(InputTouchPhase.Stationary, p); yield return Frame(); }
            Assert.IsTrue(menu.IsOpen, $"the hold opened the row through the input module ({Screen.width}x{Screen.height})");
        }

        IEnumerator Tap(Vector2 p)
        {
            Touch(InputTouchPhase.Began, p);
            yield return Frame();
            Touch(InputTouchPhase.Stationary, p);
            yield return Frame();
            Touch(InputTouchPhase.Ended, p);
            yield return Frame();
            yield return Frame();
        }

        // ---------- tests ----------

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            yield return MakeUi();
            yield return Tap(EmptyBoard);
        }

        [UnityTest]
        public IEnumerator TapOnLineClicksThroughTheInputModule()
        {
            yield return MakeUi();
            _session.SetTool(ToolId.Circle);
            yield return Tap(Center(Child("Line")));
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "a real tap on Line clicks (input module path works)");
            Assert.IsFalse(LineMenu.IsOpen, "a tap never opens the row");
            Assert.AreEqual(0, ShapeCount, "a toolbar touch never reaches the board");
        }

        [UnityTest]
        public IEnumerator HoldReleaseOnTheButtonDoesNotClick()
        {
            yield return MakeUi();
            var line = Child("Line");
            yield return HoldOpen(line);
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "the hold selected Line");
            _session.SetTool(ToolId.Circle);          // a click on release would switch back to Line: make it observable
            Touch(InputTouchPhase.Ended, Center(line));
            yield return Frame();
            yield return Frame();
            Assert.AreEqual(ToolId.Circle, _session.ActiveTool, "the release that ends a hold is not a click");
            Assert.IsTrue(LineMenu.IsOpen, "released off the chips: the row stays open");
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "nothing picked");
        }

        [UnityTest]
        public IEnumerator SlideDragsReachTheChips()
        {
            yield return MakeUi();
            var line = Child("Line");
            yield return HoldOpen(line);
            var dotted = Child("Line menu: Dotted");
            Vector2 from = Center(line), to = Center(dotted);
            for (int i = 1; i <= 8; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(from, to, i / 8f)); yield return Frame(); }
            yield return Rest(to, 3);
            Assert.AreNotEqual(Child("Line menu: Solid").GetComponent<Image>().color, dotted.GetComponent<Image>().color,
                "the chip under the finger is highlighted");
            Touch(InputTouchPhase.Ended, to);
            yield return Frame();
            yield return Frame();
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "slide-release on the Dotted chip picks it");
            Assert.IsFalse(LineMenu.IsOpen, "and closes the row");
            Assert.AreEqual(0, ShapeCount, "nothing drawn");
        }

        [UnityTest]
        public IEnumerator ReleaseThenTapAChipPicksIt()
        {
            yield return MakeUi();
            _session.Style = _session.Style.WithDash(LineStyle.Dotted);
            var line = Child("Line");
            yield return HoldOpen(line);
            Touch(InputTouchPhase.Ended, Center(line));
            yield return Frame();
            yield return Frame();
            Assert.IsTrue(LineMenu.IsOpen, "precondition: open after release");
            yield return Tap(Center(Child("Line menu: Solid")));
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "a real tap on the Solid chip picks it");
            Assert.IsFalse(LineMenu.IsOpen);
        }

        [UnityTest]
        public IEnumerator FirstBoardTouchWithTheRowOpenOnlyClosesIt()
        {
            yield return MakeUi();
            var line = Child("Line");
            yield return HoldOpen(line);
            Touch(InputTouchPhase.Ended, Center(line));
            yield return Frame();
            yield return Frame();
            Assert.IsTrue(LineMenu.IsOpen, "precondition: open");
            _session.PreviewChanged += CountGesture;
            // A full drag on the empty board: the blocker must swallow all of it, not just the Down.
            Vector2 a = EmptyBoard, b = EmptyBoard + new Vector2(0f, Screen.height * 0.2f);
            Touch(InputTouchPhase.Began, a);
            yield return Frame();
            for (int i = 1; i <= 10; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(a, b, i / 10f)); yield return Frame(); }
            yield return Rest(b, 5);
            Touch(InputTouchPhase.Ended, b);
            yield return Frame();
            yield return Frame();
            _session.PreviewChanged -= CountGesture;
            Assert.IsFalse(LineMenu.IsOpen, "the touch closed the row");
            Assert.AreEqual(0, ShapeCount, "and drew nothing: the closing touch never reaches the board");
            Assert.AreEqual(0, _downs, "no board gesture was even started (no ghost)");
            // Control: the next drag draws.
            Touch(InputTouchPhase.Began, a);
            yield return Frame();
            for (int i = 1; i <= 10; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(a, b, i / 10f)); yield return Frame(); }
            yield return Rest(b, 5);
            Touch(InputTouchPhase.Ended, b);
            yield return Frame();
            yield return Frame();
            Assert.AreEqual(1, ShapeCount, "control: with the row closed the same drag draws a line");
        }

        void CountGesture()
        {
            if (_session.GestureActive) _downs++;
        }

        [UnityTest]
        public IEnumerator FirstTouchOnAnotherToolButtonOnlyCloses()
        {
            yield return MakeUi();
            var line = Child("Line");
            yield return HoldOpen(line);
            Touch(InputTouchPhase.Ended, Center(line));
            yield return Frame();
            yield return Frame();
            yield return Tap(Center(Child("Select")));
            Assert.IsFalse(LineMenu.IsOpen, "closed");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "by design the closing tap does not also press Select");
        }

        [UnityTest]
        public IEnumerator MovingBeforeTheHoldCancelsIt()
        {
            yield return MakeUi();
            var line = Child("Line");
            Vector2 p = Center(line);
            float scale = ScreenSnapMetric.ScaleFactor(Screen.width, Screen.height);
            Touch(InputTouchPhase.Began, p);
            yield return Frame();
            Vector2 q = p + new Vector2(-20f * scale, 0f);   // 20 ref px, past the 8 px slop, still on the 56 px button
            for (int i = 1; i <= 3; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(p, q, i / 3f)); yield return Frame(); }
            float until = Time.unscaledTime + 0.8f;
            while (Time.unscaledTime < until) { Touch(InputTouchPhase.Stationary, q); yield return Frame(); }
            Assert.IsFalse(LineMenu.IsOpen, "moving past the slop before 0.45 s cancels the hold");
            Touch(InputTouchPhase.Ended, q);
            yield return Frame();
            yield return Frame();
            Assert.AreEqual(0, ShapeCount);
        }
    }
}
