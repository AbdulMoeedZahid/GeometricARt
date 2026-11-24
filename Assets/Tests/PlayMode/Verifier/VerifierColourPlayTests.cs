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
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;
using EntityId = GeoART.Contracts.EntityId;

namespace GeoART.Tests.Verifier.PlayMode
{
    /// <summary>
    /// VERIFIER (Colour palette in the tool column) through the REAL input path (virtual Touchscreen, EventSystem with
    /// InputSystemUIInputModule, ToolBar + ColorBar + LayerSidebar on one GameObject as SceneBuilder makes them, the
    /// BoardInputRouter). Complements the builder's ColourPalettePlayTests: the closing touch on ANOTHER canvas (the layer
    /// sidebar) and on another tool button only closes, empty grid slots neither pick nor close, the Board chip stays open
    /// on a tap and on a slide-release, and a release on the Colour button after its press keeps the palette open.
    /// </summary>
    public sealed class VerifierColourPlayTests
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
            public bool TryBoardToScreen(Vec2 b, out Vec2 px) { px = new Vec2(b.X * W, b.Y * H); return true; }
        }

        Touchscreen _touch;
        EditorSession _session;
        GameObject _esGo, _uiGo, _routerGo;
        InputSettings _savedSettings, _settings;
        BoardGuideColor _guides;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _touch = InputSystem.AddDevice<Touchscreen>();
            _session = new EditorSession();
            ServiceHub.Register<IBoardSpace>(new FullScreenBoard());
            ServiceHub.Register<IEditorSession>(_session);
            _savedSettings = InputSystem.settings;
            _settings = ScriptableObject.CreateInstance<InputSettings>();
            _settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = _settings;
            _guides = BoardAppearance.Guides;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { _routerGo, _uiGo, _esGo })
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (_touch != null) InputSystem.RemoveDevice(_touch);
            if (_savedSettings != null) InputSystem.settings = _savedSettings;
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            BoardAppearance.SetGuides(_guides);
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
            _uiGo = new GameObject("VerifierApp");
            _uiGo.AddComponent<ToolBar>();
            _uiGo.AddComponent<LayerSidebar>();
            _uiGo.AddComponent<ColorBar>();
            _routerGo = new GameObject("VerifierRouter");
            _routerGo.AddComponent<BoardInputRouter>();
            for (int i = 0; i < 4; i++) yield return Frame();
            Canvas.ForceUpdateCanvases();
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

        ToolHoldMenu Palette => Child("Colour").GetComponent<ToolHoldMenu>();
        Vector2 EmptyBoard => new Vector2(Screen.width * 0.45f, Screen.height * 0.3f);

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

        IEnumerator OpenPalette()
        {
            Vector2 p = Center(Child("Colour"));
            Touch(InputTouchPhase.Began, p);
            yield return Frame();
            Assert.IsTrue(Palette.IsOpen, "the PRESS opens the palette");
            Touch(InputTouchPhase.Ended, p);
            yield return Frame();
            yield return Frame();
            Assert.IsTrue(Palette.IsOpen, "the release on the Colour button keeps it open (no toggle back)");
        }

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            yield return MakeUi();
            yield return Tap(EmptyBoard);
        }

        [UnityTest]
        public IEnumerator ClosingTouchOnTheLayerSidebarOnlyCloses()
        {
            yield return MakeUi();
            int layers = _session.Board.Layers.Count;
            EntityId active = _session.Board.ActiveLayerId;
            yield return OpenPalette();
            yield return Tap(Center(Child("Add layer (+)")));
            Assert.IsFalse(Palette.IsOpen, "a touch on the layer sidebar closes the palette");
            Assert.AreEqual(layers, _session.Board.Layers.Count, "and does NOT also press the sidebar's + (no layer added)");
            Assert.AreEqual(active, _session.Board.ActiveLayerId);
        }

        [UnityTest]
        public IEnumerator ClosingTouchOnAnotherToolButtonOnlyCloses()
        {
            yield return MakeUi();
            yield return OpenPalette();
            yield return Tap(Center(Child("Circle")));
            Assert.IsFalse(Palette.IsOpen, "closed");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "Circle was not pressed");
        }

        [UnityTest]
        public IEnumerator EmptyGridSlotsNeitherPickNorClose()
        {
            yield return MakeUi();
            var before = _session.Style;
            yield return OpenPalette();
            // Row 2 holds Custom, Wheel…, Board: the slot right of Board is empty (two slots without Fill).
            GameObject board = Child("Colour menu: Board"), wheel = Child("Colour menu: Wheel…");
            Vector2 step = Center(board) - Center(wheel);
            Vector2 empty = Center(board) + step;
            yield return Tap(empty);
            Assert.IsTrue(Palette.IsOpen, "a tap in an empty slot does not close the palette");
            Assert.IsTrue(_session.Style.Equals(before), "and picks nothing");
            yield return Tap(empty + step);
            Assert.IsTrue(Palette.IsOpen, "the last empty slot too");
            // Slide-release onto an empty slot: no pick, still open.
            Vector2 colour = Center(Child("Colour"));
            yield return Tap(EmptyBoard);                       // close
            Touch(InputTouchPhase.Began, colour);
            yield return Frame();
            for (int i = 1; i <= 8; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(colour, empty, i / 8f)); yield return Frame(); }
            Touch(InputTouchPhase.Ended, empty);
            yield return Frame();
            yield return Frame();
            Assert.IsTrue(Palette.IsOpen && _session.Style.Equals(before), "slide-release on an empty slot: open, nothing picked");
        }

        [UnityTest]
        public IEnumerator BoardChipTogglesAndKeepsThePaletteOpen()
        {
            yield return MakeUi();
            var start = BoardAppearance.Guides;
            yield return OpenPalette();
            yield return Tap(Center(Child("Colour menu: Board")));
            Assert.AreNotEqual(start, BoardAppearance.Guides, "tap: guides toggled");
            Assert.IsTrue(Palette.IsOpen, "and the palette stays open");
            yield return Tap(Center(Child("Colour menu: Board")));
            Assert.AreEqual(start, BoardAppearance.Guides, "tap again: back");
            yield return Tap(EmptyBoard);
            Assert.IsFalse(Palette.IsOpen);
            // Press-slide-release onto Board.
            Vector2 colour = Center(Child("Colour"));
            Touch(InputTouchPhase.Began, colour);
            yield return Frame();
            Vector2 boardChip = Center(Child("Colour menu: Board"));
            for (int i = 1; i <= 8; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(colour, boardChip, i / 8f)); yield return Frame(); }
            Touch(InputTouchPhase.Ended, boardChip);
            yield return Frame();
            yield return Frame();
            Assert.AreNotEqual(start, BoardAppearance.Guides, "slide-release on Board toggles");
            Assert.IsTrue(Palette.IsOpen, "and keeps the palette open");
            yield return Tap(Center(Child("Colour menu: Red")));
            Assert.IsTrue(_session.Style.Color.Equals(GeoART.Contracts.Palette.Red), "a colour after Board still picks");
            Assert.IsFalse(Palette.IsOpen, "and closes");
        }

        [UnityTest]
        public IEnumerator LineHoldMenuStillWorksNextToThePalette()
        {
            // The shared ToolHoldMenu changes (grid chips, clamping, open-on-press) must leave the Line menu as it was.
            yield return MakeUi();
            var line = Child("Line");
            Vector2 p = Center(line);
            Touch(InputTouchPhase.Began, p);
            yield return Frame();
            Assert.IsFalse(line.GetComponent<ToolHoldMenu>().IsOpen, "Line does not open on the press");
            float until = Time.unscaledTime + 1.5f;
            while (!line.GetComponent<ToolHoldMenu>().IsOpen && Time.unscaledTime < until) { Touch(InputTouchPhase.Stationary, p); yield return Frame(); }
            Assert.IsTrue(line.GetComponent<ToolHoldMenu>().IsOpen, "the hold opens it");
            Vector2 dotted = Center(Child("Line menu: Dotted"));
            for (int i = 1; i <= 6; i++) { Touch(InputTouchPhase.Moved, Vector2.Lerp(p, dotted, i / 6f)); yield return Frame(); }
            Touch(InputTouchPhase.Ended, dotted);
            yield return Frame();
            yield return Frame();
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "slide-release picks Dotted");
            Assert.IsFalse(Palette.IsOpen, "the palette never opened");
        }
    }
}
