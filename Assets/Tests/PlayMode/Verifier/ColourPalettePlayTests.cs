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
    /// The Colour palette through the REAL input path (as VerifierLineMenuPlayTests does for the Line menu): a virtual
    /// Touchscreen, an EventSystem with InputSystemUIInputModule, the real ToolBar, ColorBar and BoardInputRouter, real
    /// frames. A tap on Colour opens the palette (the press opens it, the release leaves it open) and a tap on a chip picks;
    /// press-slide-release picks; with a selection a pick restyles it and keeps the tool, the selection and the action bar;
    /// and the first touch on the board with the palette open only closes it: nothing is drawn (the P1 fix of the hold
    /// menus' blocker, reused). The board covers the whole screen.
    /// </summary>
    public sealed class ColourPalettePlayTests
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
            BoardAppearance.ResetForNewSession();
            _touch = InputSystem.AddDevice<Touchscreen>();
            _session = new EditorSession();
            _session.SetSnapEnabled(false);
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
            BoardAppearance.ResetForNewSession();
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
            _uiGo = new GameObject("PaletteToolBar");
            _uiGo.AddComponent<ToolBar>();
            _uiGo.AddComponent<ColorBar>();
            _routerGo = new GameObject("PaletteRouter");
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

        static Rect WorldRect(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        static Vector2 Center(GameObject go) => WorldRect(go).center;

        GameObject Colour => Child("Colour");
        ToolHoldMenu Menu => Colour.GetComponent<ToolHoldMenu>();
        int ShapeCount => _session.ActiveLayer.Shapes.Count;

        /// <summary>A point on the board that no tool bar element (column, palette, action bar, Undo, Redo) covers.</summary>
        Vector2 FreeBoardPoint()
        {
            var taken = new System.Collections.Generic.List<Rect>();
            foreach (var n in new[] { "Column", "Colour menu", "Undo", "Redo" }) taken.Add(WorldRect(Child(n)));
            taken.Add(WorldRect(Child("Move").transform.parent.gameObject));
            foreach (var f in new[] { new Vector2(0.3f, 0.5f), new Vector2(0.3f, 0.85f), new Vector2(0.3f, 0.3f), new Vector2(0.15f, 0.6f) })
            {
                var p = new Vector2(Screen.width * f.x, Screen.height * f.y);
                bool free = true;
                foreach (var r in taken)
                    if (r.Contains(p) || r.Contains(p + new Vector2(Screen.width * 0.1f, 0f))) free = false;
                if (free) return p;
            }
            Assert.Inconclusive($"no free board point on {Screen.width}x{Screen.height}");
            return default;
        }

        IEnumerator Rest(Vector2 p, int frames)
        {
            for (int i = 0; i < frames; i++) { Touch(InputTouchPhase.Stationary, p); yield return Frame(); }
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

        IEnumerator Drag(Vector2 a, Vector2 b)
        {
            Touch(InputTouchPhase.Began, a);
            yield return Frame();
            for (int i = 1; i <= 10; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(a, b, i / 10f)); yield return Frame(); }
            yield return Rest(b, 5);
            Touch(InputTouchPhase.Ended, b);
            yield return Frame();
            yield return Frame();
        }

        void CountGesture()
        {
            if (_session.GestureActive) _downs++;
        }

        // ---------- tests ----------

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            yield return MakeUi();
            yield return Tap(new Vector2(Screen.width * 0.3f, Screen.height * 0.5f));
        }

        [UnityTest]
        public IEnumerator TapOpensThenATapOnAChipPicks()
        {
            yield return MakeUi();
            yield return Tap(Center(Colour));
            Assert.IsTrue(Menu.IsOpen, $"a real tap on Colour opens the palette ({Screen.width}x{Screen.height})");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "the tool is not switched");
            yield return Tap(Center(Child("Colour menu: Red")));
            Assert.AreEqual(Palette.Red, _session.Style.Color, "a real tap on the Red chip picks it");
            Assert.IsFalse(Menu.IsOpen, "and closes the palette");
            Assert.AreEqual(0, ShapeCount, "nothing drawn");
        }

        [UnityTest]
        public IEnumerator PressSlideReleasePicks()
        {
            yield return MakeUi();
            Vector2 from = Center(Colour);
            Touch(InputTouchPhase.Began, from);
            yield return Frame();
            yield return Frame();
            Assert.IsTrue(Menu.IsOpen, "the press opens the palette");
            Vector2 to = Center(Child("Colour menu: Blue"));
            for (int i = 1; i <= 8; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(from, to, i / 8f)); yield return Frame(); }
            yield return Rest(to, 3);
            Assert.AreNotEqual(Child("Colour menu: Green").GetComponent<Image>().color, Child("Colour menu: Blue").GetComponent<Image>().color,
                "the chip under the finger is highlighted");
            Touch(InputTouchPhase.Ended, to);
            yield return Frame();
            yield return Frame();
            Assert.AreEqual(Palette.Blue, _session.Style.Color, "slide-release on Blue picks it");
            Assert.IsFalse(Menu.IsOpen, "and closes");
            Assert.AreEqual(0, ShapeCount, "nothing drawn");
        }

        [UnityTest]
        public IEnumerator WithASelectionAPickRestylesItAndKeepsToolSelectionAndBar()
        {
            yield return MakeUi();
            var line = new LineShape(_session.Board.NewId(), new Vec2(0.1, 0.1), new Vec2(0.2, 0.2), StrokeStyle.Default);
            _session.ActiveLayer.Shapes.Add(line);
            _session.SetTool(ToolId.SelectMove);
            _session.SetSelection(new[] { line.Id });
            yield return Frame();
            Assert.IsTrue(Child("Move").activeInHierarchy, "precondition: the action bar is shown");
            yield return Tap(Center(Colour));
            Assert.IsTrue(Menu.IsOpen, "open");
            Assert.AreEqual(1, _session.SelectionCount, "opening keeps the selection");
            yield return Tap(Center(Child("Colour menu: Green")));
            Assert.AreEqual(Palette.Green, line.Style.Color, "the selected line is restyled");
            Assert.AreEqual("Style", _session.NextUndoLabel, "one Style step");
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, "tool kept");
            Assert.AreEqual(1, _session.SelectionCount, "selection kept");
            Assert.IsTrue(Child("Move").activeInHierarchy, "the action bar stays");
        }

        [UnityTest]
        public IEnumerator FirstBoardTouchWithThePaletteOpenOnlyClosesIt()
        {
            yield return MakeUi();
            yield return Tap(Center(Colour));
            Assert.IsTrue(Menu.IsOpen, "precondition: open");
            Vector2 a = FreeBoardPoint(), b = a + new Vector2(Screen.width * 0.1f, 0f);
            _session.PreviewChanged += CountGesture;
            yield return Drag(a, b);   // a full drag: the blocker must swallow all of it, not just the Down
            _session.PreviewChanged -= CountGesture;
            Assert.IsFalse(Menu.IsOpen, "the touch closed the palette");
            Assert.AreEqual(0, ShapeCount, "and drew nothing: the closing touch never reaches the board");
            Assert.AreEqual(0, _downs, "no board gesture was even started (no ghost)");
            Assert.AreEqual(Palette.Black, _session.Style.Color, "and picked nothing");
            yield return Drag(a, b);
            Assert.AreEqual(1, ShapeCount, "control: with the palette closed the same drag draws a line");
        }

        [UnityTest]
        public IEnumerator SecondTapOnColourOnlyClosesIt()
        {
            yield return MakeUi();
            yield return Tap(Center(Colour));
            Assert.IsTrue(Menu.IsOpen, "precondition: open");
            yield return Tap(Center(Colour));
            Assert.IsFalse(Menu.IsOpen, "a second tap on Colour (on the blocker over it) closes the palette");
            Assert.AreEqual(0, ShapeCount);
        }
    }
}
