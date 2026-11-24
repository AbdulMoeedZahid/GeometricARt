using System;
using System.Reflection;
using GeoART.App;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// The Line / Circle hold menu (ToolHoldMenu on the ToolBar's Line and Circle buttons) driven with EventSystem pointer
    /// events against the real EditorSession: a tap selects the tool without a menu; a hold (fake clock) selects the tool,
    /// opens the chip row and swallows the release's click; slide-release and release-then-tap pick a mode (Style.Dash);
    /// the blocker closes; moving before the hold cancels it; the icons follow Style.Dash wherever it is set; the Bands
    /// slot shows "Off". Edit Mode does not tick MonoBehaviours, so Awake/Start/Update are invoked by reflection.
    /// </summary>
    public sealed class ToolHoldMenuViewTests
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
        GameObject _go, _esGo;
        EventSystem _es;
        float _now = 100f;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession();
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
            _go = new GameObject("App");
            var bar = _go.AddComponent<ToolBar>();
            Call(bar, "Awake");
            Call(bar, "Start");
            _space.PlaceAtReticle();
            Canvas.ForceUpdateCanvases();
            foreach (var m in _go.GetComponentsInChildren<ToolHoldMenu>(true)) m.Clock = () => _now;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            if (_esGo != null) { EsEnable(_esGo.GetComponent<EventSystem>(), false); UnityEngine.Object.DestroyImmediate(_esGo); }
            ServiceHub.Clear();
        }

        // ---------- helpers ----------

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
            foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        ToolHoldMenu Menu(string button) => Find(button).GetComponent<ToolHoldMenu>();

        static Rect WorldRect(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        static Vector2 Center(GameObject go) => WorldRect(go).center;

        PointerEventData Down(GameObject go)
        {
            var p = Center(go);
            var e = new PointerEventData(_es) { position = p, pressPosition = p, pointerId = 0, button = PointerEventData.InputButton.Left };
            e.pointerPress = go;
            ExecuteEvents.Execute(go, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, e, ExecuteEvents.initializePotentialDrag);
            return e;
        }

        static void DragTo(GameObject pressed, PointerEventData e, Vector2 pos)
        {
            e.position = pos;
            ExecuteEvents.Execute(pressed, e, ExecuteEvents.dragHandler);
        }

        static void Up(GameObject pressed, PointerEventData e) => ExecuteEvents.Execute(pressed, e, ExecuteEvents.pointerUpHandler);

        void Click(GameObject go) =>
            ExecuteEvents.Execute(go, new PointerEventData(_es) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);

        /// <summary>Advances the fake clock and ticks every hold menu.</summary>
        void Advance(float seconds)
        {
            _now += seconds;
            foreach (var m in _go.GetComponentsInChildren<ToolHoldMenu>(true)) Call(m, "Update");
        }

        /// <summary>Press <paramref name="button"/> and hold past the threshold; returns the still-down event.</summary>
        PointerEventData Hold(string button)
        {
            var e = Down(Find(button));
            Advance(ToolHoldMenu.HoldSeconds + 0.05f);
            Assert.IsTrue(Menu(button).IsOpen, $"{button}: the hold opened the menu");
            return e;
        }

        void TapBlocker(string button)
        {
            var b = Find(button + " menu blocker");
            var e = new PointerEventData(_es) { position = Vector2.zero, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(b, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(b, e, ExecuteEvents.pointerUpHandler);
        }

        Texture Icon(string button) => Find(button).transform.Find("Icon").GetComponent<RawImage>().texture;

        // ---------- tests ----------

        [Test]
        public void TapSelectsLineWithoutOpeningTheMenu()
        {
            _session.SetTool(ToolId.Circle);
            var line = Find("Line");
            var e = Down(line);
            Advance(0.1f);
            Up(line, e);
            Click(line);
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "a tap selects Line");
            Assert.IsFalse(Menu("Line").IsOpen, "no menu on a tap");
            Assert.IsFalse(Find("Line menu").activeSelf);
            Assert.IsFalse(Find("Line menu blocker").activeSelf);
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "mode unchanged");
        }

        [Test]
        public void HoldChargesThenOpensSelectsToolAndSwallowsTheClick()
        {
            _session.SetTool(ToolId.SelectMove);
            int opened = 0;
            Menu("Line").Opened += () => opened++;
            var line = Find("Line");
            var e = Down(line);
            Advance(ToolHoldMenu.HoldSeconds * 0.5f);
            Assert.IsFalse(Menu("Line").IsOpen, "not before the hold time");
            Assert.Greater(Find("Line").transform.Find("Charge").GetComponent<Image>().color.a, 0.05f, "the button charges");
            Advance(ToolHoldMenu.HoldSeconds * 0.5f + 0.01f);
            Assert.IsTrue(Menu("Line").IsOpen, "open at the hold time");
            Assert.AreEqual(1, opened, "Opened raised once");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "the hold selects Line");
            Assert.IsTrue(Find("Line menu blocker").activeSelf, "blocker up");

            _session.SetTool(ToolId.Circle);   // so a click that slipped through would be visible
            Up(line, e);                        // released on the button, not on a chip
            Click(line);
            Assert.AreEqual(ToolId.Circle, _session.ActiveTool, "the release of a hold is not a tap");
            Assert.IsTrue(Menu("Line").IsOpen, "released off the chips: the row stays open");
            Advance(1f);
            Assert.AreEqual(1, opened, "no second open while it stays open");

            TapBlocker("Line");
            Assert.IsFalse(Menu("Line").IsOpen, "the blocker closes it");

            var e2 = Down(line);   // the next tap works again
            Up(line, e2);
            Click(line);
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "a later tap is a tap");
        }

        [Test]
        public void SlideOntoDottedAndReleasePicksIt()
        {
            Texture lineSolid = Icon("Line"), circleSolid = Icon("Circle");
            var line = Find("Line");
            var e = Hold("Line");
            var dotted = Find("Line menu: Dotted");
            var solid = Find("Line menu: Solid");
            Color before = dotted.GetComponent<Image>().color;
            DragTo(line, e, Center(dotted));
            Assert.AreNotEqual(before, dotted.GetComponent<Image>().color, "the chip under the finger is highlighted");
            Assert.AreNotEqual(dotted.GetComponent<Image>().color, solid.GetComponent<Image>().color);
            Up(line, e);
            Click(line);
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "slide-release picks Dotted");
            Assert.IsTrue(_session.DottedLine);
            Assert.IsFalse(Menu("Line").IsOpen, "picking closes the row");
            Assert.IsFalse(Find("Line menu blocker").activeSelf);
            Assert.AreNotSame(lineSolid, Icon("Line"), "Line icon shows the dashed mode");
            Assert.AreNotSame(circleSolid, Icon("Circle"), "Circle shares the mode, so its icon changes too");
            Assert.AreSame(dotted.transform.Find("Icon").GetComponent<RawImage>().texture, Icon("Line"),
                "the Line icon is the Dotted chip's glyph");
        }

        [Test]
        public void SlideOffTheChipsThenReleaseLeavesTheRowOpen()
        {
            var line = Find("Line");
            var e = Hold("Line");
            DragTo(line, e, Center(Find("Line menu: Dotted")));
            DragTo(line, e, Center(line) + new Vector2(0f, 300f));   // slid away again
            Up(line, e);
            Assert.IsTrue(Menu("Line").IsOpen, "still open");
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "nothing picked");
        }

        [Test]
        public void ReleaseThenTapAChipPicksIt()
        {
            var line = Find("Line");
            var e = Hold("Line");
            Up(line, e);
            Assert.IsTrue(Menu("Line").IsOpen);
            Click(Find("Line menu: Dotted"));
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash);
            Assert.IsFalse(Menu("Line").IsOpen);

            e = Hold("Line");
            Assert.AreNotEqual(Find("Line menu: Solid").GetComponent<Image>().color,
                Find("Line menu: Dotted").GetComponent<Image>().color, "the current mode's chip is highlighted");
            Up(line, e);
            Click(Find("Line menu: Solid"));
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "and back to Solid");
        }

        [Test]
        public void BlockerClosesWithoutPicking()
        {
            var line = Find("Line");
            var e = Hold("Line");
            Up(line, e);
            var b = Find("Line menu blocker");
            Assert.IsTrue(b.GetComponent<Graphic>().raycastTarget, "the blocker catches touches");
            Assert.AreEqual(0f, b.GetComponent<Graphic>().color.a, 1e-6f, "and is invisible");
            var e2 = new PointerEventData(_es) { position = Vector2.zero, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(b, e2, ExecuteEvents.pointerDownHandler);
            Assert.IsTrue(Menu("Line").IsOpen && b.activeSelf,
                "still up while the finger is down: the whole touch stays on UI (BoardInputRouter checks at touch-begin)");
            ExecuteEvents.Execute(b, e2, ExecuteEvents.pointerUpHandler);
            Assert.IsFalse(Menu("Line").IsOpen, "closes on release");
            Assert.IsFalse(b.activeSelf, "blocker gone");
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "nothing picked");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);
        }

        [Test]
        public void MovingBeforeTheHoldCancelsIt()
        {
            _session.SetTool(ToolId.Circle);
            var line = Find("Line");
            var e = Down(line);
            float scale = ScreenSnapMetric.ScaleFactor(Screen.width, Screen.height);
            DragTo(line, e, Center(line) + new Vector2(0f, 20f * scale));   // 20 reference px > the 8 px slop
            Advance(ToolHoldMenu.HoldSeconds * 3f);
            Assert.IsFalse(Menu("Line").IsOpen, "a moving finger is not a hold");
            Assert.AreEqual(0f, Find("Line").transform.Find("Charge").GetComponent<Image>().color.a, 1e-6f, "charge cleared");
            Up(line, e);
            Click(line);
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "the release can still be a tap");
        }

        [Test]
        public void SmallJitterStillHolds()
        {
            var line = Find("Line");
            var e = Down(line);
            float scale = ScreenSnapMetric.ScaleFactor(Screen.width, Screen.height);
            DragTo(line, e, Center(line) + new Vector2(3f * scale, 0f));
            Advance(ToolHoldMenu.HoldSeconds + 0.05f);
            Assert.IsTrue(Menu("Line").IsOpen, "3 reference px of jitter is inside the slop");
        }

        [Test]
        public void IconsFollowTheModeSetElsewhere()
        {
            Texture lineSolid = Icon("Line"), circleSolid = Icon("Circle");
            _session.ToggleDottedLine();
            Texture lineDotted = Icon("Line"), circleDotted = Icon("Circle");
            Assert.AreNotSame(lineSolid, lineDotted, "Line icon follows Style.Dash");
            Assert.AreNotSame(circleSolid, circleDotted, "Circle icon follows Style.Dash");
            Assert.AreNotSame(lineDotted, circleDotted);
            _session.Style = _session.Style.WithDash(LineStyle.Solid);
            Assert.AreSame(lineSolid, Icon("Line"));
            Assert.AreSame(circleSolid, Icon("Circle"));
        }

        [Test]
        public void CircleHoldSelectsCircleAndPicksTheSharedMode()
        {
            var circle = Find("Circle");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);
            var e = Hold("Circle");
            Assert.AreEqual(ToolId.Circle, _session.ActiveTool, "the hold selects Circle");
            DragTo(circle, e, Center(Find("Circle menu: Dotted")));
            Up(circle, e);
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "Circle's menu sets the same shared mode");
            Assert.AreSame(Find("Circle menu: Dotted").transform.Find("Icon").GetComponent<RawImage>().texture, Icon("Circle"),
                "Circle icon is the dashed ring");
            Assert.IsFalse(Menu("Line").IsOpen, "only one menu at a time");
        }

        [Test]
        public void RowOpensLeftOfTheButtonOnTopOfEverything()
        {
            var line = Find("Line");
            Hold("Line");
            Canvas.ForceUpdateCanvases();
            Rect button = WorldRect(line), row = WorldRect(Find("Line menu"));
            Assert.Less(row.xMax, button.xMin, "the row pops out to the left");
            Assert.AreEqual(button.center.y, row.center.y, 1f, "vertically centred on the button");
            Assert.AreEqual(button.height, row.height, 1f, "same scale as the column");
            var canvas = _go.GetComponentInChildren<Canvas>(true);
            Rect cr = WorldRect(canvas.gameObject);
            Assert.GreaterOrEqual(row.xMin, cr.xMin, "inside the screen");
            var menus = Find("HoldMenus").transform;
            Assert.AreSame(canvas.transform, menus.parent);
            Assert.AreEqual(canvas.transform.childCount - 1, menus.GetSiblingIndex(), "menus draw over the column, bar and Undo");
            Rect blocker = WorldRect(Find("Line menu blocker"));
            Assert.IsTrue(blocker.Contains(button.center) && blocker.Contains(cr.min + Vector2.one) && blocker.Contains(cr.max - Vector2.one),
                "the blocker covers the whole screen");
        }

        [Test]
        public void OpeningOneMenuClosesTheOther()
        {
            var line = Find("Line");
            var circle = Find("Circle");
            var e1 = Down(line);
            var e2 = Down(circle);
            Advance(ToolHoldMenu.HoldSeconds + 0.05f);   // both holds fire in the same tick (two fingers)
            Assert.AreNotEqual(Menu("Line").IsOpen, Menu("Circle").IsOpen, "only one row is open");
            Assert.IsFalse(Find("Line menu blocker").activeSelf && Find("Circle menu blocker").activeSelf, "and one blocker");
            Up(line, e1);
            Up(circle, e2);
        }

        [Test]
        public void ReLayoutClosesTheMenu()
        {
            Hold("Line");
            Call(_go.GetComponent<ToolBar>(), "ApplySafeArea");   // safe area / rotation change
            Assert.IsFalse(Menu("Line").IsOpen, "a row placed for the old layout is closed");
        }

        [Test]
        public void HidingTheToolBarClosesTheMenu()
        {
            Hold("Line");
            _space.Reset();
            Assert.IsFalse(Menu("Line").IsOpen, "a hidden bar keeps no menu open");
            _space.PlaceAtReticle();
            Assert.IsFalse(Menu("Line").IsOpen, "and it does not come back");
        }

        [Test]
        public void HoldMarksAndTheBandsButton()
        {
            Assert.NotNull(Find("Line").transform.Find("HoldMark"), "Line has a hold menu mark");
            Assert.NotNull(Find("Circle").transform.Find("HoldMark"), "Circle has a hold menu mark");
            Assert.NotNull(Find("Bands").transform.Find("HoldMark"), "Bands has a hold mark (layer popup)");
            Assert.IsNull(Find("Select").transform.Find("HoldMark"), "Select has no menu");
            var bands = Find("Bands");
            Assert.AreEqual("Off", bands.GetComponentInChildren<Text>().text, "Bands starts Off");
            Assert.IsNull(bands.GetComponent<CanvasGroup>(), "no longer dimmed");
            Click(bands);
            Assert.IsTrue(_session.ActiveLayer.Bands.On, "a tap turns the active layer's bands on");
            Assert.AreEqual("Double", bands.GetComponentInChildren<Text>().text);
            Click(bands);
            Assert.IsFalse(_session.ActiveLayer.Bands.On, "and off again");
            Assert.AreEqual("Off", bands.GetComponentInChildren<Text>().text);
        }
    }
}
