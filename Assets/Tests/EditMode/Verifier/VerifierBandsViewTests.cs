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

        // ---------- P5: interlace, flip, inner lines ----------

        sealed class Corners : ICornerPlacement
        {
            public double Side = 1.2;
            public PlacementMode Mode => PlacementMode.Corners;
            public void SetMode(PlacementMode mode) { }
            public int RequiredCorners => 4;
            public int CornerCount => 4;
            public bool CanAddCorner => false;
            public bool CanConfirm => false;
            public CornerIssue Issue => CornerIssue.None;
            public int IssueCorner => -1;
            public double LiveEdgeMeters => double.NaN;
            public double SurfaceWidthMeters => Side;
            public double SurfaceHeightMeters => Side;
            public double BoardSideMeters => Side;
            public bool IsVerticalSurface => false;
            public bool TryAddCorner() => false;
            public void UndoCorner() { }
            public void ClearCorners() { }
            public bool ConfirmCorners() => false;
#pragma warning disable 67
            public event Action CornersChanged;
#pragma warning restore 67
        }

        Text Note => Find("BandNote").GetComponent<Text>();

        void InnerBumpCase(double side)
        {
            LayerBands b = On();
            b.Width = 0.008;
            _session.SetLayerBands(Star.Id, b);
            int steps = _session.UndoCount;
            OpenPopup(Star.Id);
            Click(Find("Inner lines"));
            double scale = Math.Max(0.4, Math.Min(2.0, side / 0.40));
            double min = 0.010 * scale / side;   // 10 mm times the stroke scale (P5)
            Assert.IsTrue(Star.Bands.InnerLines, $"side {side}: inner lines on");
            Assert.AreEqual(min, Star.Bands.Width, 1e-12, $"side {side}: width raised to 10 mm x stroke scale");
            Assert.AreEqual(steps + 1, _session.UndoCount, $"side {side}: toggle + bump = ONE step");
            Assert.AreEqual("Bands", _session.NextUndoLabel);
            var o = BandOutline.Build(BandGraph.Build(Star.Shapes), BandOutlineOptions.ForBoard(Star.Bands, side));
            Assert.IsTrue(o.InnerShown, $"side {side}: after the bump the inner lines are actually drawn");
            Click(Find("Inner lines"));
            Assert.IsFalse(Star.Bands.InnerLines, "off again");
            Assert.AreEqual(min, Star.Bands.Width, 1e-12, "turning them off keeps the width");
            _session.Undo();
            _session.Undo();
            Assert.IsFalse(Star.Bands.InnerLines, $"side {side}: Undo of the toggle");
            Assert.AreEqual(0.008, Star.Bands.Width, 1e-12, $"side {side}: the same Undo restores the width");
        }

        [Test]
        public void InnerLinesBumpTheWidthInTheSameStepOnTheDefaultBoard() => InnerBumpCase(0.40);

        [Test]
        public void InnerLinesBumpScalesOnALargeCornerPlacedBoard()
        {
            ServiceHub.Register<ICornerPlacement>(new Corners { Side = 1.2 });
            InnerBumpCase(1.2);
            double w = Star.Bands.Width;   // after the two Undos: 0.008 board units = 9.6 mm on the 1.2 m board
            StringAssert.StartsWith((w * 1200).ToString("0.0"), Find("WidthValue").GetComponent<Text>().text, "mm of the 1.2 m board");
        }

        [Test]
        public void FlipIsOnlyForInterlaceAndIsOneStep()
        {
            _session.SetLayerBands(Star.Id, On());
            OpenPopup(Star.Id);
            Assert.IsFalse(Find("Flip weave").GetComponent<Button>().interactable, "Double: Flip weave dimmed");
            Click(Find("Bands Interlace"));
            Assert.IsTrue(Find("Flip weave").GetComponent<Button>().interactable, "Interlace: Flip weave enabled");
            Assert.AreEqual("Weave", BandsLabel);
            int steps = _session.UndoCount;
            Click(Find("Flip weave"));
            Assert.IsTrue(Star.Bands.FlipWeave);
            Assert.AreEqual(steps + 1, _session.UndoCount, "one step");
            _session.Undo();
            Assert.IsFalse(Star.Bands.FlipWeave, "Undo un-flips");
        }

        [Test]
        public void NotesFollowTheirPriority()
        {
            // A triangle crossed once by a line that starts inside it: one crossing that can't alternate.
            // Scaled by 0.3 about the centre so MaxSafeWidth (0.03) is inside the slider range.
            Vec2 S(double x, double y) => new Vec2(0.5 + (x - 0.5) * 0.3, 0.5 + (y - 0.5) * 0.3);
            Star.Shapes.Clear();
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), S(0.3, 0.3), S(0.7, 0.3), StrokeStyle.Default));
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), S(0.7, 0.3), S(0.5, 0.7), StrokeStyle.Default));
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), S(0.5, 0.7), S(0.3, 0.3), StrokeStyle.Default));
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), S(0.45, 0.4), S(0.45, 0.9), StrokeStyle.Default));
            _session.LoadBoard(_session.Board);
            var g = BandGraph.Build(Star.Shapes);
            Assert.AreEqual(1, g.WeaveConflicts, "precondition: one conflict");
            double safe = BandOutline.Build(g, BandOutlineOptions.ForBoard(LayerBands.Default, 0.40)).MaxSafeWidth;
            Assert.That(safe > 0.012 && safe < 0.045, $"precondition: MaxSafeWidth {safe} inside the slider range");

            LayerBands b = On(BandMode.Interlace);
            b.Width = 0.006;
            _session.SetLayerBands(Star.Id, b);
            OpenPopup(Star.Id);
            Assert.AreEqual(LayerSidebar.ConflictsNote(1), Note.text, "narrow interlace: the conflict note");
            b.Width = safe * 0.9;
            _session.SetLayerBands(Star.Id, b);
            Assert.AreEqual(LayerSidebar.BandsWideNote, Note.text, "amber width outranks the conflicts");
            b.Width = Math.Min(LayerBands.MaxWidth, safe * 1.05);
            _session.SetLayerBands(Star.Id, b);
            Assert.AreEqual(LayerSidebar.BandsTooWideNote, Note.text, "red outranks everything");
            b.Width = 0.006;
            b.Mode = BandMode.Double;
            _session.SetLayerBands(Star.Id, b);
            Assert.AreNotEqual(LayerSidebar.ConflictsNote(1), Note.text, "Double: conflicts are not a concern");
            b.Mode = BandMode.Interlace;
            b.InnerLines = true;               // set directly at a narrow width: no bump
            _session.SetLayerBands(Star.Id, b);
            Assert.AreEqual(LayerSidebar.ConflictsNote(1), Note.text, "amber conflicts outrank the grey inner-lines note");
            b.Mode = BandMode.Double;
            _session.SetLayerBands(Star.Id, b);
            Assert.AreEqual(LayerSidebar.InnerTooNarrowNote, Note.text, "then the grey note");
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
            var bar = _appGo.GetComponent<ColorBar>();
            bar.OpenWheel();
            Canvas.ForceUpdateCanvases();
            GameObject panel = Find("ColorWheelPopup");
            Assert.IsTrue(panel.activeInHierarchy, "the wheel panel is open");
            Rect wheel = ScreenRect(panel);
            Assert.That(wheel.xMin >= 0 && wheel.yMin >= 0 && wheel.xMax <= Screen.width && wheel.yMax <= Screen.height,
                $"wheel panel {wheel} inside the screen");
            Assert.IsFalse(wheel.Overlaps(column), $"wheel panel {wheel} clear of the tool column {column} ({Screen.width}x{Screen.height})");
            bar.CloseWheel();
        }
    }
}
