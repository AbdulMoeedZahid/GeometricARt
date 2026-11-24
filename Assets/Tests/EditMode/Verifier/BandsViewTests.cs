using System;
using System.Collections.Generic;
using System.Reflection;
using GeoART.App;
using GeoART.BoardRender;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EntityId = GeoART.Contracts.EntityId;
using Object = UnityEngine.Object;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// P4 view side: BoardView draws banded layers through its BandCache (stripped hides the structure but not circles,
    /// hidden layers cost nothing, a width change never re-planarizes and many changes in a frame rebuild once); the
    /// toolbar Bands button (tap toggles, hold opens the layer popup); the popup's Bands section (mode chips, width
    /// slider as one undo step, structure and outline buttons, amber/red/circle notes); restyle selection from the
    /// colour swatches, the Width button and the Line chips (decision 6); the colour wheel. Edit Mode does not tick
    /// MonoBehaviours, so Awake/Start/Update/LateUpdate are invoked by reflection.
    /// </summary>
    public sealed class BandsViewTests
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
        GameObject _appGo, _esGo, _viewGo;
        EventSystem _es;
        BoardView _view;
        float _now = 50f;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession(GoldenBoards.StarOnFullBoard());
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
        }

        [TearDown]
        public void TearDown()
        {
            if (_appGo != null) Object.DestroyImmediate(_appGo);
            if (_esGo != null) { EsEnable(_esGo.GetComponent<EventSystem>(), false); Object.DestroyImmediate(_esGo); }
            if (_viewGo != null)
            {
                foreach (var mf in _viewGo.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
                foreach (var mr in _viewGo.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial != null) Object.DestroyImmediate(mr.sharedMaterial);
                Object.DestroyImmediate(_viewGo);
            }
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

        void MakeApp()
        {
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
            foreach (var m in _appGo.GetComponentsInChildren<ToolHoldMenu>(true)) m.Clock = () => _now;
        }

        void MakeView()
        {
            _viewGo = new GameObject("BandsView");
            _view = _viewGo.AddComponent<BoardView>();
            Call(_view, "Awake");
            _view.Bind(_session);
            Call(_view, "LateUpdate");
        }

        int ShapeVertices()
        {
            Call(_view, "LateUpdate");
            return _viewGo.transform.Find("Shapes").GetComponent<MeshFilter>().sharedMesh.vertexCount;
        }

        GameObject Find(string name)
        {
            foreach (var t in _appGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        void Click(GameObject go) =>
            ExecuteEvents.Execute(go, new PointerEventData(_es) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);

        void Click(string name) => Click(Find(name));

        Layer Star => _session.ActiveLayer;

        static LayerBands On(double width = LayerBands.DefaultWidth, bool hide = false)
        {
            var b = LayerBands.Default;
            b.On = true;
            b.Width = width;
            b.HideStructure = hide;
            return b;
        }

        // ---------- BoardView ----------

        [Test]
        public void BandedLayerDrawsOutlinesAndStrippedKeepsCircles()
        {
            MakeView();
            int plain = ShapeVertices();
            _session.SetLayerBands(Star.Id, On());
            int banded = ShapeVertices();
            Assert.Greater(banded, plain, "band outlines are drawn on top of the structure");
            Assert.AreEqual(1, _view.Bands.PlanarizeCount, "one planarization");
            _session.SetLayerBands(Star.Id, On(hide: true));
            int stripped = ShapeVertices();
            Assert.Less(stripped, banded, "stripped: the structure lines are gone");
            Assert.Greater(stripped, 0);

            // A circle on the banded layer is still drawn when the structure is hidden.
            Star.Shapes.Add(new CircleShape(_session.Board.NewId(), new Vec2(0.5, 0.5), 0.1, StrokeStyle.Default));
            _view.MarkAllDirty();
            int withCircle = ShapeVertices();
            Assert.Greater(withCircle, stripped, "circles are never banded and never hidden");
        }

        [Test]
        public void HiddenOrUnbandedLayersComputeNothing()
        {
            MakeView();
            _session.SetLayerBands(Star.Id, On());
            ShapeVertices();
            Assert.AreEqual(1, _view.Bands.EntryCount);
            _session.SetLayerVisible(Star.Id, false);
            int planar = _view.Bands.PlanarizeCount, outlines = _view.Bands.OutlineCount;
            ShapeVertices();
            Assert.AreEqual(0, _view.Bands.EntryCount, "a hidden layer's entry is dropped");
            Assert.AreEqual(planar, _view.Bands.PlanarizeCount, "and nothing is planarized");
            Assert.AreEqual(outlines, _view.Bands.OutlineCount);
        }

        [Test]
        public void WidthChangesNeverReplanarizeAndCoalescePerFrame()
        {
            MakeView();
            _session.SetLayerBands(Star.Id, On());
            ShapeVertices();
            int planar = _view.Bands.PlanarizeCount, outlines = _view.Bands.OutlineCount;
            for (int i = 1; i <= 10; i++) _session.SetLayerBands(Star.Id, On(0.015 + 0.001 * i));   // ten slider ticks in one frame
            ShapeVertices();
            Assert.AreEqual(planar, _view.Bands.PlanarizeCount, "a width drag never re-planarizes");
            Assert.AreEqual(outlines + 1, _view.Bands.OutlineCount, "ten changes in one frame: one outline rebuild");
            Assert.AreEqual(1, _session.UndoCount - 1, "and they are one undo step after the On step");

            _session.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.1, 0.9), true, 0));
            _session.Pointer(new PointerEvent(PointerPhase.Up, new Vec2(0.3, 0.95), true, 0));
            ShapeVertices();
            Assert.AreEqual(planar + 1, _view.Bands.PlanarizeCount, "a new stroke re-planarizes once");
            _session.Undo();
            ShapeVertices();
            Assert.AreEqual(planar + 2, _view.Bands.PlanarizeCount, "Undo too");
        }

        [Test]
        public void BandRebuildCostForTheWidthSlider()
        {
            // 200 tile-like lines on one banded layer: the cost of one width-slider frame (outline only) and of a
            // shape edit (planarize + outline), logged for the iPhone estimate (about 2-3x the editor).
            var rng = new System.Random(3);
            for (int i = 0; i < 200; i++)
            {
                int x0 = rng.Next(41), y0 = rng.Next(41);
                int x1 = Math.Max(0, Math.Min(40, x0 + rng.Next(-4, 5))), y1 = Math.Max(0, Math.Min(40, y0 + rng.Next(-4, 5)));
                if (x0 == x1 && y0 == y1) continue;
                Star.Shapes.Add(new LineShape(_session.Board.NewId(), new Vec2(x0 / 40.0, y0 / 40.0), new Vec2(x1 / 40.0, y1 / 40.0), StrokeStyle.Default));
            }
            MakeView();
            _session.SetLayerBands(Star.Id, On());
            ShapeVertices();
            double topology = _view.Bands.LastFrameMs;
            double worst = 0;
            for (int i = 1; i <= 5; i++)
            {
                _session.SetLayerBands(Star.Id, On(0.015 + 0.001 * i));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                ShapeVertices();
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            Debug.Log($"[Bands] 200 lines: planarize+outline {topology:0.0} ms, width-slider frame (outline + mesh) worst {worst:0.0} ms");
            Assert.Less(worst, 40.0, "a width-slider frame stays well inside an editor frame budget");
        }

        // ---------- toolbar Bands button ----------

        [Test]
        public void BandsButtonTapTogglesAndHoldOpensTheLayerPopup()
        {
            MakeApp();
            var bands = Find("Bands");
            Click(bands);
            Assert.IsTrue(Star.Bands.On, "tap: on");
            Assert.AreEqual("Double", bands.GetComponentInChildren<Text>().text);
            _session.Undo();
            Assert.AreEqual("Off", bands.GetComponentInChildren<Text>().text, "label follows Undo");

            var e = new PointerEventData(_es) { position = Vector2.zero, pointerId = 0, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(bands, e, ExecuteEvents.pointerDownHandler);
            _now += ToolHoldMenu.HoldSeconds + 0.05f;
            Call(bands.GetComponent<ToolHoldMenu>(), "Update");
            Assert.IsTrue(Find("OpacityPopup").activeSelf, "hold: the layer popup opens");
            ExecuteEvents.Execute(bands, e, ExecuteEvents.pointerUpHandler);
            Click(bands);
            Assert.IsFalse(Star.Bands.On, "the release of the hold does not toggle");
        }

        // ---------- layer popup ----------

        void OpenPopup()
        {
            MakeApp();
            _appGo.GetComponent<LayerSidebar>().OpenSettings(Star.Id);
            Assert.IsTrue(Find("OpacityPopup").activeSelf, "precondition: popup open");
        }

        Text Note => Find("BandNote").GetComponent<Text>();

        [Test]
        public void PopupModeStructureAndOutlineChips()
        {
            OpenPopup();
            Click("Bands Double");
            Assert.IsTrue(Star.Bands.On && Star.Bands.Mode == BandMode.Double, "Double");
            Assert.IsFalse(Find("Flip weave").GetComponent<Button>().interactable, "Flip weave is dimmed outside Interlace");
            Click("Structure line");
            Assert.IsTrue(Star.Bands.HideStructure, "structure hidden (stripped)");
            _session.Style = _session.Style.WithColor(Palette.Red);
            Click("Outline colour");
            Assert.AreEqual(Palette.Red, Star.Bands.OutlineColor, "outline takes the current colour");
            Click("Bands Off");
            Assert.IsFalse(Star.Bands.On, "Off");
            Assert.IsTrue(Star.Bands.HideStructure, "and remembers the rest");
        }

        [Test]
        public void PopupWidthSliderIsOneUndoStepAndShowsMillimetres()
        {
            OpenPopup();
            Click("Bands Double");
            int steps = _session.UndoCount;
            var slider = Find("BandWidthSlider").GetComponent<Slider>();
            for (int i = 1; i <= 8; i++) slider.value = 0.015f + 0.002f * i;
            Assert.AreEqual(steps + 1, _session.UndoCount, "one slider session = one 'Band width' step");
            Assert.AreEqual("Band width", _session.NextUndoLabel);
            Assert.AreEqual(0.031, Star.Bands.Width, 1e-6);
            Assert.AreEqual("12.4 mm", Find("WidthValue").GetComponent<Text>().text, "mm on the 40 cm board");
            Assert.AreSame(Find("OpacityPopup").GetComponentInChildren<Slider>(true), Find("OpacitySlider").GetComponent<Slider>(),
                "the opacity slider is still the popup's first slider");
        }

        [Test]
        public void PopupWarnsAmberNearTheLimitAndRedBeyondIt()
        {
            Star.Shapes.Clear();
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), new Vec2(0.5, 0), new Vec2(0.5, 1), StrokeStyle.Default));
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), new Vec2(0.52, 0), new Vec2(0.52, 1), StrokeStyle.Default));   // max 0.02
            OpenPopup();
            _session.SetLayerBands(Star.Id, On(0.012));
            Assert.AreEqual("", Note.text, "well inside: no note");
            _session.SetLayerBands(Star.Id, On(0.017));
            Assert.AreEqual(LayerSidebar.BandsWideNote, Note.text, "above 75% of the limit: amber");
            _session.SetLayerBands(Star.Id, On(0.021));
            Assert.AreEqual(LayerSidebar.BandsTooWideNote, Note.text, "beyond it: red");
            Star.Shapes.Add(new CircleShape(_session.Board.NewId(), new Vec2(0.2, 0.5), 0.1, StrokeStyle.Default));
            _session.SetLayerBands(Star.Id, On(0.006));
            _appGo.GetComponent<LayerSidebar>().OpenSettings(Star.Id);
            Assert.AreEqual(LayerSidebar.CirclesNotBandedNote, Note.text, "circles are not banded");
        }

        [Test]
        public void PopupRedWhenOutlinesCrossEvenUnderMaxSafeWidth()
        {
            // A pinched U: two arms joined by one edge whose free ends converge (tester P3 note). MaxSafeWidth stays
            // generous but the outlines cross: the crossing count makes the note red.
            Star.Shapes.Clear();
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), new Vec2(0.3, 0.2), new Vec2(0.7, 0.2), StrokeStyle.Default));
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), new Vec2(0.3, 0.2), new Vec2(0.4975, 0.8), StrokeStyle.Default));
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), new Vec2(0.7, 0.2), new Vec2(0.5025, 0.8), StrokeStyle.Default));
            var o = BandOutline.Build(BandGraph.Build(Star.Shapes), new BandOutlineOptions { Width = 0.02 });
            Assume.That(o.SelfIntersections > 0 && o.MaxSafeWidth > 0.02, "precondition: crossings below MaxSafeWidth");
            OpenPopup();
            _session.SetLayerBands(Star.Id, On(0.02));
            Assert.AreEqual(LayerSidebar.BandsTooWideNote, Note.text, "crossing outlines: red");
        }

        [Test]
        public void PopupInterlaceFlipAndInnerLines()
        {
            OpenPopup();
            Click("Bands Interlace");
            Assert.IsTrue(Star.Bands.On && Star.Bands.Mode == BandMode.Interlace, "Interlace");
            Assert.IsTrue(Find("Flip weave").GetComponent<Button>().interactable, "Flip weave enabled in Interlace");
            Click("Flip weave");
            Assert.IsTrue(Star.Bands.FlipWeave, "flipped");
            int steps = _session.UndoCount;
            Click("Inner lines");
            Assert.IsTrue(Star.Bands.InnerLines, "inner lines on");
            Assert.GreaterOrEqual(Star.Bands.Width, 0.010 / 0.40 - 1e-12, "turning them on raises the band to 10 mm");
            Assert.AreEqual(steps + 1, _session.UndoCount, "toggle + width bump = one 'Bands' step");
            Assert.AreEqual("Bands", _session.NextUndoLabel);
            Click("Inner lines");
            Assert.IsFalse(Star.Bands.InnerLines, "off again");
            Assert.GreaterOrEqual(Star.Bands.Width, 0.010 / 0.40 - 1e-12, "the width stays");
            Click("Bands Double");
            Assert.IsFalse(Find("Flip weave").GetComponent<Button>().interactable, "dimmed again in Double");
            Assert.IsTrue(Star.Bands.FlipWeave, "but remembered");
            Assert.AreEqual("Double", Find("Bands").GetComponentInChildren<Text>().text);
        }

        [Test]
        public void PopupNotesConflictsAndTooNarrowInnerLines()
        {
            // A triangle crossed once by a line that starts inside it: one crossing can't alternate.
            Star.Shapes.Clear();
            foreach (var (a, b) in new[] { (new Vec2(0.3, 0.3), new Vec2(0.7, 0.3)), (new Vec2(0.7, 0.3), new Vec2(0.5, 0.7)),
                         (new Vec2(0.5, 0.7), new Vec2(0.3, 0.3)), (new Vec2(0.45, 0.4), new Vec2(0.45, 0.9)) })
                Star.Shapes.Add(new LineShape(_session.Board.NewId(), a, b, StrokeStyle.Default));
            OpenPopup();
            var woven = On(0.008);
            woven.Mode = BandMode.Interlace;
            _session.SetLayerBands(Star.Id, woven);
            Assert.AreEqual(LayerSidebar.ConflictsNote(1), Note.text, "conflicts are reported in Interlace");
            var flat = On(LayerBands.DefaultWidth);
            flat.InnerLines = true;   // 6 mm without the popup's bump: too narrow for inner lines
            _session.SetLayerBands(Star.Id, flat);
            Assert.AreEqual(LayerSidebar.InnerTooNarrowNote, Note.text);
        }

        [Test]
        public void InterlaceCutsTheUnderStructureLinesInTheView()
        {
            MakeView();
            var b = On();
            b.Mode = BandMode.Interlace;
            _session.SetLayerBands(Star.Id, b);
            int cutVertices = ShapeVertices();
            Assert.IsTrue(_view.Bands.TryGetOutline(Star.Id, out BandOutline o), "outline drawn");
            Assert.AreEqual(8, o.StructureCuts.Count, "one cut per crossing of the star");
            Assert.Greater(o.Bridges.Count, 0, "bridges drawn");
            b.HideStructure = true;
            _session.SetLayerBands(Star.Id, b);
            ShapeVertices();
            Assert.IsTrue(_view.Bands.TryGetOutline(Star.Id, out o) && o.StructureCuts.Count == 0, "stripped: no cuts");
            Assert.Greater(cutVertices, 0);

            // A polygon stamp crossed by a line, woven: drawn in pieces without errors, mitres kept (one polyline per piece).
            Star.Shapes.Add(new PolygonShape(_session.Board.NewId(), new Vec2(0.5, 0.15), 6, 0.08, 0.0, StrokeStyle.Default.WithDash(LineStyle.Dotted)));
            Star.Shapes.Add(new LineShape(_session.Board.NewId(), new Vec2(0.3, 0.15), new Vec2(0.7, 0.15), StrokeStyle.Default));
            b.HideStructure = false;
            _session.SetLayerBands(Star.Id, b);
            _view.Bands.MarkTopologyDirty();   // shapes were added behind the session's back
            _view.MarkAllDirty();
            Assert.DoesNotThrow(() => ShapeVertices());
            Assert.IsTrue(_view.Bands.TryGetOutline(Star.Id, out o));
            Assert.Greater(o.StructureCuts.Count, 8, "the stamp or the line is cut too");
        }

        // ---------- restyle selection ----------

        LineShape SelectFirstLine()
        {
            var line = (LineShape)Star.Shapes[0];
            _session.SetTool(ToolId.SelectMove);
            _session.SetSelection(new[] { line.Id });
            return line;
        }

        [Test]
        public void SwatchAndWidthRestyleTheSelectionOnly()
        {
            MakeApp();
            var other = (LineShape)Star.Shapes[1];
            var line = SelectFirstLine();
            Click(BoardAppearance.ShapeColorNames[2]);   // red
            Assert.AreEqual(Palette.Red, line.Style.Color, "the selected line turns red");
            Assert.AreNotEqual(Palette.Red, other.Style.Color, "others keep their colour");
            Assert.AreEqual(Palette.Red, _session.Style.Color, "and red is the current colour");
            Assert.AreEqual("Style", _session.NextUndoLabel);
            Click("Medium");   // Width: Medium -> Thick
            Assert.AreEqual(LineWidth.Thick, line.Style.Width, "Width restyles the selection");
            Assert.AreEqual(1, _session.SelectionCount, "the selection is kept");

            _session.ClearSelection();
            int steps = _session.UndoCount;
            Click(BoardAppearance.ShapeColorNames[4]);
            Assert.AreEqual(steps, _session.UndoCount, "without a selection a swatch only sets the colour");
        }

        [Test]
        public void HoldingLineWithASelectionRestylesTheDashWithoutSwitchingTool()
        {
            MakeApp();
            var line = SelectFirstLine();
            var lineButton = Find("Line");
            var e = new PointerEventData(_es) { position = Vector2.zero, pointerId = 0, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(lineButton, e, ExecuteEvents.pointerDownHandler);
            _now += ToolHoldMenu.HoldSeconds + 0.05f;
            Call(lineButton.GetComponent<ToolHoldMenu>(), "Update");
            Assert.IsTrue(lineButton.GetComponent<ToolHoldMenu>().IsOpen, "chips open");
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, "the tool is not switched");
            Assert.AreEqual(1, _session.SelectionCount, "the selection is kept");
            ExecuteEvents.Execute(lineButton, e, ExecuteEvents.pointerUpHandler);
            Click("Line menu: Dotted");
            Assert.AreEqual(LineStyle.Dotted, line.Style.Dash, "the chip restyles the selected line");
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "and sets the current mode");
            Assert.AreEqual("Style", _session.NextUndoLabel);
            var tap = new PointerEventData(_es) { position = Vector2.zero, pointerId = 0, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(lineButton, tap, ExecuteEvents.pointerDownHandler);   // a new press ends the hold's click suppression
            ExecuteEvents.Execute(lineButton, tap, ExecuteEvents.pointerUpHandler);
            Click(lineButton);
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "a plain tap still switches tools");
        }

        // ---------- colour wheel ----------

        [Test]
        public void ColourWheelPicksUsesRestylesAndKeepsACustomSwatch()
        {
            MakeApp();
            var bar = _appGo.GetComponent<ColorBar>();
            var line = SelectFirstLine();
            Assert.IsNull(bar.CustomColor, "no custom colour yet");
            Click("Custom");
            Assert.IsTrue(bar.WheelOpen, "with no custom colour the swatch opens the wheel");
            bar.PickOnWheel(new Vector2(0f, 1f));   // hue 0.25 (yellow-green), full saturation
            var slider = Find("BrightnessSlider").GetComponent<Slider>();
            slider.value = 0.5f;
            RgbaColor picked = bar.WheelColor;
            Assert.AreNotEqual(Palette.Black, picked);
            Click("Use colour");
            Assert.IsFalse(bar.WheelOpen, "Use closes the wheel");
            Assert.AreEqual(picked, _session.Style.Color, "the picked colour is current");
            Assert.AreEqual(picked, line.Style.Color, "and restyles the selection");
            Assert.AreEqual(picked, bar.CustomColor, "and stays as the custom swatch");

            Click(BoardAppearance.ShapeColorNames[0]);
            Click("Custom");
            Assert.AreEqual(picked, _session.Style.Color, "a tap on the custom swatch selects it again");
            Assert.IsFalse(bar.WheelOpen, "without opening the wheel");
            Click("Custom");
            Assert.IsTrue(bar.WheelOpen, "when it is already current, a tap opens the wheel");
        }

        [Test]
        public void ColourWheelBlockerClosesOnReleaseWithoutAChange()
        {
            MakeApp();
            var bar = _appGo.GetComponent<ColorBar>();
            RgbaColor before = _session.Style.Color;
            bar.OpenWheel();
            bar.PickOnWheel(new Vector2(1f, 0f));
            var blocker = Find("WheelBlocker");
            var e = new PointerEventData(_es) { position = Vector2.zero, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(blocker, e, ExecuteEvents.pointerDownHandler);
            Assert.IsTrue(bar.WheelOpen && blocker.activeSelf, "still open while the finger is down (the touch stays on UI)");
            ExecuteEvents.Execute(blocker, e, ExecuteEvents.pointerUpHandler);
            Assert.IsFalse(bar.WheelOpen, "closes on release");
            Assert.AreEqual(before, _session.Style.Color, "without a change");
        }

        [Test]
        public void ColourBarStillClearsTheToolColumn()
        {
            Assert.LessOrEqual(ColorBar.EdgeMargin + ColorBar.PillWidth, 390f - 8f - 56f - 8f, "pill ends left of the tool column");
            Assert.AreEqual(6 + 6 * 36 + 8 + 72 + 6, ColorBar.PillWidth, 1e-3f, "five palette swatches, the custom swatch, Board");
        }
    }
}
