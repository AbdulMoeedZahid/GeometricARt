using System;
using System.Collections;
using GeoART.App;
using GeoART.App.Save;
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
    /// The closing touch of every open menu on the OTHER UI canvases, through the real input path (virtual Touchscreen,
    /// InputSystemUIInputModule, ToolBar + ColorBar + LayerSidebar + DesignsPanel on one GameObject, BoardInputRouter): with
    /// the Colour palette or the Line, Circle or Snap hold menu open, a tap on the layer sidebar's + or on the Designs
    /// button only closes the menu. It neither adds a layer nor opens the Designs panel (the menus' canvas sits above the
    /// sidebar and the Designs button since this fix), and nothing is drawn.
    /// </summary>
    public sealed class MenuOverlayPlayTests
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

        static readonly string[] Menus = { "Colour", "Line", "Circle", "Snap" };

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            BoardAppearance.ResetForNewSession();
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
            _uiGo = new GameObject("OverlayApp");
            _uiGo.AddComponent<ToolBar>();
            _uiGo.AddComponent<ColorBar>();
            _uiGo.AddComponent<LayerSidebar>();
            _uiGo.AddComponent<DesignsPanel>();
            _routerGo = new GameObject("OverlayRouter");
            _routerGo.AddComponent<BoardInputRouter>();
            for (int i = 0; i < 4; i++) yield return Frame();
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(Child("DesignsButton").activeInHierarchy, "precondition: the Designs button is shown (board placed)");
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

        /// <summary>Opens <paramref name="button"/>'s menu: Colour on the press, the others after the hold; then releases.</summary>
        IEnumerator Open(string button)
        {
            GameObject go = Child(button);
            var menu = go.GetComponent<ToolHoldMenu>();
            Vector2 p = Center(go);
            Touch(InputTouchPhase.Began, p);
            yield return Frame();
            float until = Time.unscaledTime + 1.5f;
            while (!menu.IsOpen && Time.unscaledTime < until) { Touch(InputTouchPhase.Stationary, p); yield return Frame(); }
            Touch(InputTouchPhase.Ended, p);
            yield return Frame();
            yield return Frame();
            Assert.IsTrue(menu.IsOpen, $"{button}: the menu is open after the release ({Screen.width}x{Screen.height})");
        }

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            yield return MakeUi();
            yield return Tap(new Vector2(Screen.width * 0.45f, Screen.height * 0.3f));
        }

        [UnityTest]
        public IEnumerator ATapOnTheLayerSidebarOnlyClosesEveryMenu()
        {
            yield return MakeUi();
            int layers = _session.Board.Layers.Count;
            var active = _session.Board.ActiveLayerId;
            foreach (var button in Menus)
            {
                yield return Open(button);
                yield return Tap(Center(Child("Add layer (+)")));
                Assert.IsFalse(Child(button).GetComponent<ToolHoldMenu>().IsOpen, $"{button}: the tap closed the menu");
                Assert.AreEqual(layers, _session.Board.Layers.Count, $"{button}: and did not add a layer");
                Assert.AreEqual(active, _session.Board.ActiveLayerId, $"{button}: nor change the active layer");
            }
            Assert.AreEqual(0, _session.ActiveLayer.Shapes.Count, "nothing drawn");
            yield return Tap(Center(Child("Add layer (+)")));
            Assert.AreEqual(layers + 1, _session.Board.Layers.Count, "control: with no menu open the same tap adds a layer");
        }

        [UnityTest]
        public IEnumerator ATapOnTheDesignsButtonOnlyClosesEveryMenu()
        {
            yield return MakeUi();
            var designs = _uiGo.GetComponent<DesignsPanel>();
            foreach (var button in Menus)
            {
                yield return Open(button);
                yield return Tap(Center(Child("DesignsButton")));
                Assert.IsFalse(Child(button).GetComponent<ToolHoldMenu>().IsOpen, $"{button}: the tap closed the menu");
                Assert.IsFalse(designs.IsOpen, $"{button}: and did not open the Designs panel");
            }
            Assert.AreEqual(0, _session.ActiveLayer.Shapes.Count, "nothing drawn");
            yield return Tap(Center(Child("DesignsButton")));
            Assert.IsTrue(designs.IsOpen, "control: with no menu open the same tap opens the Designs panel");
            designs.Close();
        }
    }
}
