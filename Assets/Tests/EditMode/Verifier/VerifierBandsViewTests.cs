using System;
using System.Reflection;
using GeoART.App;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using EntityId = GeoART.Contracts.EntityId;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// VERIFIER (ui-line-plan 3.3, 3.10, P4): the Bands button label against the ACTIVE layer through SelectLayer, Undo,
    /// Redo and LoadBoard; the layer popup's undo granularity (one step per popup session's width drag, opacity then
    /// width = two, no phantom step on open or on re-picking the current mode); the popup and the colour wheel inside the
    /// screen and clear of the tool column. Edit Mode: Awake/Start invoked by reflection.
    /// </summary>
    public sealed class VerifierBandsViewTests
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

        EditorSession _session;
        Space _space;
        GameObject _appGo, _esGo;
        EventSystem _es;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession(GoldenBoards.StarOnFullBoard());
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
            _appGo = new GameObject("App");
            var tb = _appGo.AddComponent<ToolBar>();
            var sb = _appGo.AddComponent<LayerSidebar>();
            var cb = _appGo.AddComponent<ColorBar>();
            foreach (MonoBehaviour c in new MonoBehaviour[] { tb, sb, cb }) Call(c, "Awake");
            foreach (MonoBehaviour c in new MonoBehaviour[] { tb, sb, cb }) Call(c, "Start");
            _space.PlaceAtReticle();
            Canvas.ForceUpdateCanvases();
        }

        [TearDown]
        public void TearDown()
        {
            if (_appGo != null) Object.DestroyImmediate(_appGo);
            if (_esGo != null) { EsEnable(_esGo.GetComponent<EventSystem>(), false); Object.DestroyImmediate(_esGo); }
            ServiceHub.Clear();
        }

        static void EsEnable(EventSystem es, bool on)
        {
            if (es == null) return;
            var m = typeof(EventSystem).GetMethod(on ? "OnEnable" : "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(es, null);
        }

        static void Call(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        GameObject Find(string name)
        {
            foreach (var t in _appGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        PointerEventData Ped(GameObject go) =>
            new PointerEventData(_es) { position = Center(go), pointerId = 0, button = PointerEventData.InputButton.Left, pointerPress = go };

        void Click(GameObject go)
        {
            var e = Ped(go);
            ExecuteEvents.Execute(go, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, e, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(go, e, ExecuteEvents.pointerClickHandler);
        }

        static Vector2 Center(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;
        }

        static Rect ScreenRect(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        string BandsLabel => Find("Bands").GetComponentInChildren<Text>().text;
        Layer Star => _session.Board.Layers[2];
        Layer Dotted => _session.Board.Layers[0];

        static LayerBands On(BandMode mode = BandMode.Double)
        {
            LayerBands b = LayerBands.Default;
            b.On = true;
            b.Mode = mode;
            return b;
        }

        void OpenPopup(EntityId id)
        {
            _appGo.GetComponent<LayerSidebar>().OpenSettings(id);
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(Find("OpacityPopup").activeInHierarchy, "precondition: popup open");
        }

        void ClosePopupByBlocker()
        {
            var blocker = Find("PopupBlocker");
            var e = Ped(blocker);
            ExecuteEvents.Execute(blocker, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(blocker, e, ExecuteEvents.pointerUpHandler);
            Assert.IsFalse(Find("OpacityPopup").activeInHierarchy, "precondition: the blocker closed the popup");
        }

        Slider BandSlider => Find("BandWidthSlider").GetComponent<Slider>();
        Slider OpacitySlider => Find("OpacitySlider").GetComponent<Slider>();

        // ---------- the Bands button label ----------

        [Test]
        public void BandsLabelFollowsTheActiveLayerThroughSelectUndoRedoAndLoad()
        {
            Assert.AreEqual(Star.Id, _session.Board.ActiveLayerId, "precondition: the star layer is active");
            Assert.AreEqual("Off", BandsLabel);
            _session.SetLayerBands(Star.Id, On());
            Assert.AreEqual("Double", BandsLabel, "on: Double");
            _session.SelectLayer(Dotted.Id);
            Assert.AreEqual("Off", BandsLabel, "another (unbanded) layer active: Off");
            _session.SetLayerBands(Dotted.Id, On(BandMode.Interlace));
            Assert.AreEqual("Weave", BandsLabel, "Interlace: Weave");
            _session.SelectLayer(Star.Id);
            Assert.AreEqual("Double", BandsLabel, "back to the star layer");
            _session.Undo();                               // undoes the Dotted layer's step; the star stays active
            Assert.AreEqual("Double", BandsLabel, "Undo of the other layer's step: unchanged");
            _session.Undo();                               // undoes the star's bands
            Assert.AreEqual("Off", BandsLabel, "Undo of the active layer's step: Off");
            _session.Redo();
            Assert.AreEqual("Double", BandsLabel, "Redo: Double");
            var loaded = GoldenBoards.Empty();
            loaded.Layers[0].Bands = On(BandMode.Interlace);
            _session.LoadBoard(loaded);
            Assert.AreEqual("Weave", BandsLabel, "LoadBoard: the loaded active layer's state");
            _session.ClearBoard();
            Assert.AreEqual("Off", BandsLabel, "ClearBoard: Off");
            _session.Undo();
            Assert.AreEqual("Weave", BandsLabel, "Undo of Clear: back");
        }

        [Test]
        public void BandsTapIsOneStepEachWayAndActsOnTheActiveLayerOnly()
        {
            _session.SelectLayer(Dotted.Id);
            int steps = _session.UndoCount;
            Click(Find("Bands"));
            Assert.IsTrue(Dotted.Bands.On && !Star.Bands.On, "only the active layer turns on");
            Assert.AreEqual(steps + 1, _session.UndoCount, "one step");
            Assert.AreEqual("Bands", _session.NextUndoLabel);
            Click(Find("Bands"));
            Assert.IsFalse(Dotted.Bands.On);
            Assert.AreEqual(steps + 2, _session.UndoCount, "off is a second step");
        }

        // ---------- popup undo granularity ----------

        [Test]
        public void TwoPopupSessionsAreTwoWidthSteps()
        {
            _session.SetLayerBands(Star.Id, On());
            int steps = _session.UndoCount;
            OpenPopup(Star.Id);
            for (int i = 1; i <= 6; i++) BandSlider.value = 0.015f + 0.002f * i;
            ClosePopupByBlocker();
            OpenPopup(Star.Id);
            for (int i = 1; i <= 6; i++) BandSlider.value = 0.027f + 0.002f * i;
            Assert.AreEqual(steps + 2, _session.UndoCount, "each popup session's drag is its own step");
            _session.Undo();
            Assert.AreEqual(0.027, Star.Bands.Width, 1e-6, "Undo the second drag only");
        }

        [Test]
        public void OpacityThenWidthInOnePopupIsTwoSteps()
        {
            _session.SetLayerBands(Star.Id, On());
            int steps = _session.UndoCount;
            OpenPopup(Star.Id);
            for (int i = 1; i <= 5; i++) OpacitySlider.value = 1f - 0.1f * i;
            for (int i = 1; i <= 5; i++) BandSlider.value = 0.015f + 0.003f * i;
            Assert.AreEqual(steps + 2, _session.UndoCount, "opacity drag, then width drag: 2 steps");
            for (int i = 1; i <= 3; i++) OpacitySlider.value = 0.5f + 0.1f * i;
            Assert.AreEqual(steps + 3, _session.UndoCount, "back to opacity: a third step");
            _session.Undo();
            _session.Undo();
            Assert.AreEqual(0.015, Star.Bands.Width, 1e-9, "two Undos: width restored");
            Assert.AreEqual(0.5, Star.Opacity, 1e-6, "and opacity is the first drag's end");
        }

        [Test]
        public void OpeningThePopupOrRepickingTheModeRecordsNothing()
        {
            LayerBands b = On();
            b.Width = 0.0234567891234567;   // not a float: the slider shows it rounded
            _session.SetLayerBands(Star.Id, b);
            int steps = _session.UndoCount;
            OpenPopup(Star.Id);
            Assert.AreEqual(steps, _session.UndoCount, "open: no step");
            Assert.AreEqual(0.0234567891234567, Star.Bands.Width, "open: the stored double is not rounded through the float slider");
            Click(Find("Bands Double"));
            Assert.AreEqual(steps, _session.UndoCount, "re-picking the current mode: no step");
            ClosePopupByBlocker();
            OpenPopup(Dotted.Id);
            Assert.AreEqual(steps, _session.UndoCount, "opening an unbanded layer's popup: no step");
        }

        // ---------- layout ----------

        [Test]
        public void PopupAndColourWheelAreOnScreenAndClearOfTheToolColumn()
        {
            OpenPopup(Star.Id);
            Rect popup = ScreenRect(Find("OpacityPopup"));
            Rect column = ScreenRect(Find("Column"));
            Assert.That(popup.xMin >= 0 && popup.yMin >= -0.5f && popup.xMax <= Screen.width && popup.yMax <= Screen.height + 0.5f,
                $"popup {popup} inside the {Screen.width}x{Screen.height} screen");
            Assert.IsFalse(popup.Overlaps(column), $"popup {popup} clear of the tool column {column}");
            ClosePopupByBlocker();
            Rect pill = ScreenRect(Find("Board").transform.parent.gameObject);
            Assert.IsFalse(pill.Overlaps(column), $"colour pill {pill} clear of the tool column {column} ({Screen.width}x{Screen.height})");
            var bar = _appGo.GetComponent<ColorBar>();
            bar.OpenWheel();
            Canvas.ForceUpdateCanvases();
            GameObject panel = Find("ColorWheelPopup");
            Assert.IsTrue(panel.activeInHierarchy, "the wheel panel is open");
            Rect wheel = ScreenRect(panel);
            Assert.That(wheel.xMin >= 0 && wheel.yMin >= 0 && wheel.xMax <= Screen.width && wheel.yMax <= Screen.height,
                $"wheel panel {wheel} inside the screen");
            bar.CloseWheel();
        }
    }
}
