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

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// The Colour button in the tool column and its palette (ToolBar.Colour.cs, a ToolHoldMenu with OpenOnPress), driven
    /// with EventSystem pointer events against the real EditorSession and ColorBar: the press opens the palette without
    /// switching tool or clearing the selection; tap-then-tap and slide-release pick; a pick restyles the selection (one
    /// "Style" step, selection and action bar kept) and closes; the blocker closes on release only; Board toggles the guides
    /// and stays open; Wheel… opens the wheel; the button's icon follows the colour; and the layout (at the batch screen
    /// and by the spec formula on the target phones) keeps the palette on screen, clear of the column, the action bar and
    /// the layer sidebar. Edit Mode does not tick MonoBehaviours, so Awake/Start are invoked by reflection.
    /// </summary>
    public sealed class ColourPaletteViewTests
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

        // Spec constants (reference px), restated so the layout test does not read them from the code under test.
        const float ButtonW = 56f, ButtonH = 52f, Spacing = 8f, GroupGap = 14f, EdgeMargin = 8f, RowGap = 10f;
        const float PlacementRowTop = 0.11f, CoachTextBottom = 0.80f, MinFit = 0.4f;
        const float PaletteW = 5 * 48f + 4 * 4f, PaletteH = 2 * 52f + 4f;   // 2 rows of 5 chips, 48 x 52, spacing 4
        const float BarWidth = 308f, BarH = 52f, SidebarRight = 6f + 50f, SidebarGap = 4f;

        EditorSession _session;
        Space _space;
        GameObject _go, _esGo;
        EventSystem _es;
        ColorBar _bar;
        LineShape _line, _other;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            BoardAppearance.ResetForNewSession();
            _session = new EditorSession();
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
            _go = new GameObject("App");
            var tb = _go.AddComponent<ToolBar>();
            _bar = _go.AddComponent<ColorBar>();
            Call(tb, "Awake");
            Call(_bar, "Awake");
            Call(tb, "Start");
            Call(_bar, "Start");
            _space.PlaceAtReticle();
            Canvas.ForceUpdateCanvases();

            _line = new LineShape(_session.Board.NewId(), new Vec2(0.2, 0.2), new Vec2(0.4, 0.4), StrokeStyle.Default);
            _other = new LineShape(_session.Board.NewId(), new Vec2(0.6, 0.2), new Vec2(0.8, 0.4), StrokeStyle.Default);
            _session.ActiveLayer.Shapes.Add(_line);
            _session.ActiveLayer.Shapes.Add(_other);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                // OnDestroy (Object.Destroy) does not run in Edit Mode: free the ToolBar's textures directly.
                var tb = _go.GetComponent<ToolBar>();
                if (typeof(ToolBar).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(tb) is Texture2D[] table)
                    foreach (var t in table) if (t != null) Object.DestroyImmediate(t);
                foreach (var name in new[] { "_spectrumRing", "_spectrumDisc" })
                    if (typeof(ToolBar).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(tb) is Texture2D t)
                        Object.DestroyImmediate(t);
                Object.DestroyImmediate(_go);
            }
            if (_esGo != null) { EsEnable(_es, false); Object.DestroyImmediate(_esGo); }
            BoardAppearance.ResetForNewSession();
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

        GameObject Colour => Find("Colour");
        GameObject Chip(string label) => Find("Colour menu: " + label);
        ToolHoldMenu Menu => Colour.GetComponent<ToolHoldMenu>();
        GameObject Blocker => Find("Colour menu blocker");
        bool BarShown => Find("Move").activeInHierarchy;

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

        /// <summary>A real-looking tap on Colour: press (opens), release on the button, and the click that may follow.</summary>
        void TapColour()
        {
            var e = Down(Colour);
            Up(Colour, e);
            Click(Colour);
        }

        void SelectLine()
        {
            _session.SetTool(ToolId.SelectMove);
            _session.SetSelection(new[] { _line.Id });
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(BarShown, "precondition: the action bar shows the selection");
        }

        // ---------- opening ----------

        [Test]
        public void ThePressOpensThePaletteWithoutSwitchingToolOrSelection()
        {
            SelectLine();
            int opened = 0;
            Menu.Opened += () => opened++;
            var e = Down(Colour);
            Assert.IsTrue(Menu.IsOpen, "open on the press itself (no hold)");
            Assert.IsTrue(Blocker.activeSelf, "with its blocker");
            Assert.AreEqual(1, opened, "Opened raised once");
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, "the tool is not switched");
            Assert.AreEqual(1, _session.SelectionCount, "the selection is kept");
            Up(Colour, e);
            Click(Colour);   // the release after the press is not a tap that would close it again
            Assert.IsTrue(Menu.IsOpen, "released on the button: the palette stays open for a tap");
            Assert.IsTrue(BarShown, "the action bar stays");
            Assert.AreEqual(Palette.Black, _session.Style.Color, "nothing picked");
        }

        [Test]
        public void PaletteHasTheColoursCustomWheelAndBoardInTwoRowsWithTheCurrentHighlighted()
        {
            TapColour();
            Canvas.ForceUpdateCanvases();
            string[] row1 = BoardAppearance.ShapeColorNames;
            string[] row2 = { "Custom", "Wheel…", "Board" };
            Rect first = WorldRect(Chip(row1[0]));
            for (int i = 0; i < row1.Length; i++)
            {
                Rect r = WorldRect(Chip(row1[i]));
                Assert.AreEqual(first.center.y, r.center.y, 0.5f, $"{row1[i]} in the first row");
                if (i > 0) Assert.Greater(r.xMin, WorldRect(Chip(row1[i - 1])).xMax, $"{row1[i]} right of {row1[i - 1]}");
            }
            for (int i = 0; i < row2.Length; i++)
            {
                Rect r = WorldRect(Chip(row2[i]));
                Assert.Less(r.yMax, first.yMin, $"{row2[i]} in the second row, below the colours");
                Assert.AreEqual(WorldRect(Chip(row1[i])).center.x, r.center.x, 0.5f, $"{row2[i]} in column {i + 1}");
            }
            Color black = Chip("Black").GetComponent<Image>().color, red = Chip("Red").GetComponent<Image>().color;
            Assert.AreNotEqual(black, red, "the current colour (Black) is highlighted");
            Assert.AreEqual(red, Chip("Board").GetComponent<Image>().color, "Board is never highlighted");
            for (int i = 0; i < row1.Length; i++)
            {
                var swatch = Chip(row1[i]).transform.Find("Icon/Swatch").GetComponent<RawImage>();
                Assert.AreEqual((Color)ColorBarColor(BoardAppearance.ShapeColors[i]), swatch.color, $"{row1[i]} chip shows its colour");
            }
        }

        // ---------- picking ----------

        [Test]
        public void TapThenTapPicksRestylesTheSelectionAndCloses()
        {
            SelectLine();
            int steps = _session.UndoCount;
            TapColour();
            Click(Chip("Red"));
            Assert.AreEqual(Palette.Red, _session.Style.Color, "Red is the current colour");
            Assert.AreEqual(Palette.Red, _line.Style.Color, "the selected line is restyled");
            Assert.AreEqual(Palette.Black, _other.Style.Color, "the other line keeps its colour");
            Assert.AreEqual(steps + 1, _session.UndoCount, "one undo step");
            Assert.AreEqual("Style", _session.NextUndoLabel);
            Assert.IsFalse(Menu.IsOpen, "the pick closes the palette");
            Assert.IsFalse(Blocker.activeSelf);
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool, "tool kept");
            Assert.AreEqual(1, _session.SelectionCount, "selection kept");
            Assert.IsTrue(BarShown, "the action bar stays");
        }

        [Test]
        public void PressSlideReleasePicks()
        {
            SelectLine();
            var e = Down(Colour);
            Canvas.ForceUpdateCanvases();
            GameObject green = Chip("Green");
            Color before = green.GetComponent<Image>().color;
            DragTo(Colour, e, Center(Chip("Blue")));
            DragTo(Colour, e, Center(green));
            Assert.AreNotEqual(before, green.GetComponent<Image>().color, "the chip under the finger is highlighted");
            Up(Colour, e);
            Click(Colour);
            Assert.AreEqual(Palette.Green, _session.Style.Color, "slide-release picks Green");
            Assert.AreEqual(Palette.Green, _line.Style.Color, "and restyles the selection");
            Assert.IsFalse(Menu.IsOpen, "and closes");
            Assert.AreEqual(1, _session.SelectionCount);
        }

        [Test]
        public void SlideOffTheChipsThenReleaseLeavesItOpen()
        {
            var e = Down(Colour);
            DragTo(Colour, e, Center(Chip("Red")));
            DragTo(Colour, e, Center(Colour) + new Vector2(0f, 400f));
            Up(Colour, e);
            Assert.IsTrue(Menu.IsOpen, "still open");
            Assert.AreEqual(Palette.Black, _session.Style.Color, "nothing picked");
        }

        [Test]
        public void BlockerClosesOnReleaseWithoutPicking()
        {
            SelectLine();
            TapColour();
            var b = Blocker;
            Assert.IsTrue(b.GetComponent<Graphic>().raycastTarget, "the blocker catches touches");
            Assert.AreEqual(0f, b.GetComponent<Graphic>().color.a, 1e-6f, "and is invisible");
            Canvas.ForceUpdateCanvases();
            Rect blocker = WorldRect(b), canvas = WorldRect(Colour.GetComponentInParent<Canvas>().gameObject);
            Assert.IsTrue(blocker.Contains(canvas.min + Vector2.one) && blocker.Contains(canvas.max - Vector2.one), "full screen");
            Assert.AreEqual(b.transform.parent.childCount - 2, b.transform.GetSiblingIndex(), "right under the palette grid");
            var e = new PointerEventData(_es) { position = Vector2.zero, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(b, e, ExecuteEvents.pointerDownHandler);
            Assert.IsTrue(Menu.IsOpen && b.activeSelf, "still up while the finger is down: the whole touch stays on UI");
            ExecuteEvents.Execute(b, e, ExecuteEvents.pointerUpHandler);
            Assert.IsFalse(Menu.IsOpen, "closes on release");
            Assert.IsFalse(b.activeSelf);
            Assert.AreEqual(Palette.Black, _session.Style.Color, "nothing picked");
            Assert.AreEqual(1, _session.SelectionCount, "the selection is kept");
        }

        [Test]
        public void BoardChipTogglesTheGuidesAndStaysOpen()
        {
            TapColour();
            Click(Chip("Board"));
            Assert.AreEqual(BoardGuideColor.White, BoardAppearance.Guides);
            Assert.IsTrue(Menu.IsOpen, "a toggle leaves the palette open");
            Assert.AreEqual(Palette.Black, _session.Style.Color, "the colour is not touched");
            Menu.Close();

            var e = Down(Colour);   // press-slide-release onto Board toggles too
            DragTo(Colour, e, Center(Chip("Board")));
            Up(Colour, e);
            Assert.AreEqual(BoardGuideColor.Black, BoardAppearance.Guides, "slide-release toggles back");
            Assert.IsTrue(Menu.IsOpen, "and stays open");
            Click(Chip("Red"));
            Assert.AreEqual(Palette.Red, _session.Style.Color, "a colour after the toggle still picks");
            Assert.IsFalse(Menu.IsOpen, "and closes");
        }

        [Test]
        public void WheelChipClosesThePaletteAndOpensTheWheelClearOfTheColumn()
        {
            SelectLine();
            TapColour();
            Click(Chip("Wheel…"));
            Assert.IsFalse(Menu.IsOpen, "the palette closes");
            Assert.IsTrue(_bar.WheelOpen, "the wheel opens");
            Assert.AreEqual(1, _session.SelectionCount, "selection kept");
            Canvas.ForceUpdateCanvases();
            Rect wheel = WorldRect(Find("ColorWheelPopup")), column = WorldRect(Find("Column"));
            Assert.That(wheel.xMin >= -0.5f && wheel.yMin >= -0.5f && wheel.xMax <= Screen.width + 0.5f && wheel.yMax <= Screen.height + 0.5f,
                $"wheel {wheel} on the {Screen.width}x{Screen.height} screen");
            Assert.IsFalse(wheel.Overlaps(column), $"wheel {wheel} clear of the column {column}");
            _bar.PickOnWheel(new Vector2(0.6f, 0.3f));
            RgbaColor picked = _bar.WheelColor;
            Click(Find("Use colour"));
            Assert.AreEqual(picked, _line.Style.Color, "Use colour restyles the selection");
            Assert.AreEqual(picked, _bar.CustomColor, "and becomes the custom colour");
            TapColour();
            Assert.AreNotEqual(Chip("Custom").GetComponent<Image>().color, Chip("Black").GetComponent<Image>().color,
                "the Custom chip is the highlighted one");
            Assert.AreEqual((Color)ColorBarColor(picked), Chip("Custom").transform.Find("Icon/Swatch").GetComponent<RawImage>().color,
                "and shows the custom colour");
        }

        static Color32 ColorBarColor(RgbaColor c) => new Color32(c.R, c.G, c.B, c.A);

        // ---------- the button ----------

        [Test]
        public void TheButtonShowsTheCurrentColourAndARainbowRingForTheCustomOne()
        {
            Assert.NotNull(Colour.transform.Find("HoldMark"), "the menu corner mark");
            Assert.AreEqual("Colour", Colour.GetComponentInChildren<Text>().text);
            RawImage ring = Colour.transform.Find("Icon").GetComponent<RawImage>();
            RawImage swatch = Colour.transform.Find("Icon/Swatch").GetComponent<RawImage>();
            Texture plain = ring.texture;
            Assert.AreEqual((Color)ColorBarColor(Palette.Black), swatch.color, "Black by default");
            _session.Style = _session.Style.WithColor(Palette.Red);   // set elsewhere
            Assert.AreEqual((Color)ColorBarColor(Palette.Red), swatch.color, "follows Style.Color");
            Assert.AreSame(plain, ring.texture, "a palette colour: the plain outline ring");

            _bar.OpenWheel();
            _bar.PickOnWheel(new Vector2(0f, 0.8f));
            RgbaColor custom = _bar.WheelColor;
            _bar.UseWheelColor();
            Assert.AreEqual((Color)ColorBarColor(custom), swatch.color, "the custom colour");
            Assert.AreNotSame(plain, ring.texture, "in the rainbow ring");
            Assert.AreSame(Chip("Custom").transform.Find("Icon").GetComponent<RawImage>().texture, ring.texture,
                "the same ring as the Custom chip");
            TapColour();
            Click(Chip("Blue"));
            Assert.AreSame(plain, ring.texture, "back to the plain ring");
        }

        [Test]
        public void OneMenuAtATimeAndARelayoutCloses()
        {
            float now = 10f;
            foreach (var m in _go.GetComponentsInChildren<ToolHoldMenu>(true)) m.Clock = () => now;
            TapColour();
            var line = Find("Line");
            var e = Down(line);   // a second finger holds Line
            now += ToolHoldMenu.HoldSeconds + 0.05f;
            Call(line.GetComponent<ToolHoldMenu>(), "Update");
            Up(line, e);
            Assert.IsTrue(line.GetComponent<ToolHoldMenu>().IsOpen, "precondition: the Line menu opened");
            Assert.IsFalse(Menu.IsOpen, "opening another menu closes the palette");
            line.GetComponent<ToolHoldMenu>().Close();

            TapColour();
            Call(_go.GetComponent<ToolBar>(), "ApplySafeArea");
            Assert.IsFalse(Menu.IsOpen, "a re-layout closes it");
            TapColour();
            _space.Reset();
            Assert.IsFalse(Menu.IsOpen, "hiding the tool bar closes it");
        }

        // ---------- layout ----------

        [Test]
        public void LayoutAtTheBatchScreenIsOnScreenAndClearOfTheColumnAndTheActionBar()
        {
            SelectLine();
            Call(_go.GetComponent<ToolBar>(), "LateUpdate");
            Canvas.ForceUpdateCanvases();
            TapColour();
            Canvas.ForceUpdateCanvases();
            Rect canvas = WorldRect(Colour.GetComponentInParent<Canvas>().gameObject);
            Rect palette = WorldRect(Find("Colour menu")), column = WorldRect(Find("Column")), button = WorldRect(Colour);
            Rect bar = WorldRect(Find("Move").transform.parent.gameObject);
            Debug.Log($"[Palette] screen {Screen.width}x{Screen.height} canvas {canvas}: palette {palette} column {column} bar {bar}");
            Assert.That(palette.xMin >= canvas.xMin - 0.5f && palette.xMax <= canvas.xMax + 0.5f && palette.yMin >= canvas.yMin - 0.5f
                        && palette.yMax <= canvas.yMax + 0.5f, $"palette {palette} inside the canvas {canvas}");
            Assert.IsFalse(palette.Overlaps(column), $"palette {palette} clear of the column {column}");
            Assert.Less(palette.xMax, button.xMin, "left of the Colour button");
            Assert.IsFalse(palette.Overlaps(bar), $"palette {palette} clear of the action bar {bar}");
            if (palette.height < canvas.height * 0.5f && button.center.y - palette.height * 0.5f > canvas.yMin && button.center.y + palette.height * 0.5f < canvas.yMax)
                Assert.AreEqual(button.center.y, palette.center.y, 1f, "vertically centred on the Colour button");
            Assert.AreEqual(column.width / ButtonW, palette.height / PaletteH, 0.01f, "at the column's scale");
            foreach (var n in new[] { "Undo", "Redo" })
                Assert.IsFalse(palette.Overlaps(WorldRect(Find(n))), $"clear of {n}");
        }

        [Test]
        public void OpenMenusHaveTheirOwnCanvasAboveTheSidebarAndDesignsButtonBelowTheWheel()
        {
            GameObject menus = Find("HoldMenus");
            var canvas = menus.GetComponent<Canvas>();
            Assert.NotNull(canvas, "the open menus are on a canvas of their own");
            Assert.IsTrue(canvas.overrideSorting, "with its own sorting order");
            Assert.AreEqual(ToolBar.HoldMenusSortingOrder, canvas.sortingOrder);
            Assert.NotNull(menus.GetComponent<GraphicRaycaster>(), "and its own raycaster, so its blocker is hit first");
            Assert.Greater(canvas.sortingOrder, 110, "above the layer sidebar (110)");
            Assert.Greater(canvas.sortingOrder, 106, "above the Designs button (106)");
            Assert.Greater(canvas.sortingOrder, Colour.GetComponentInParent<Canvas>().sortingOrder, "above the column");
            Assert.Less(canvas.sortingOrder, Find("ColorWheel").GetComponent<Canvas>().sortingOrder, "below the colour wheel");
            Assert.Less(canvas.sortingOrder, 120, "below the toasts");
            foreach (var n in new[] { "Colour menu", "Colour menu blocker", "Line menu", "Line menu blocker", "Circle menu",
                         "Circle menu blocker", "Snap menu", "Snap menu blocker" })
                Assert.AreSame(menus.transform, Find(n).transform.parent, $"{n} is on the menus canvas");
        }

        [Test]
        public void ALeftLimitShrinksThePaletteToStayRightOfIt()
        {
            TapColour();
            Canvas.ForceUpdateCanvases();
            Rect natural = WorldRect(Find("Colour menu"));
            Menu.Close();
            float limit = natural.xMin + natural.width * 0.2f;   // as if the sidebar reached 20% into the palette
            Menu.LeftLimit = () => limit;
            TapColour();
            Canvas.ForceUpdateCanvases();
            Rect shrunk = WorldRect(Find("Colour menu"));
            Assert.GreaterOrEqual(shrunk.xMin, limit - 0.5f, "the left edge stays right of the limit");
            Assert.AreEqual(natural.xMax, shrunk.xMax, 0.5f, "the right edge stays next to the button");
            Assert.AreEqual(0.8f, shrunk.width / natural.width, 0.01f, "shrunk to fit");
            Assert.AreEqual(Center(Colour).y, shrunk.center.y, 1f, "still centred on the button");
            Menu.Close();
            Menu.LeftLimit = () => natural.xMin + natural.width * 0.9f;   // far too narrow: not below MinLimitScale
            TapColour();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(ToolHoldMenu.MinLimitScale, WorldRect(Find("Colour menu")).width / natural.width, 0.01f,
                "never below MinLimitScale");
            Menu.Close();
            Menu.LeftLimit = () => float.NaN;
            TapColour();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(natural.width, WorldRect(Find("Colour menu")).width, 0.5f, "NaN: no limit");
        }

        [Test]
        public void ThePaletteStaysRightOfARealLayerSidebar()
        {
            var sidebar = _go.AddComponent<LayerSidebar>();
            Call(sidebar, "Awake");
            Call(sidebar, "Start");
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(sidebar.TryGetPillWorldRect(out Rect pill), "the pill is shown (board placed)");
            TapColour();
            Canvas.ForceUpdateCanvases();
            Rect palette = WorldRect(Find("Colour menu"));
            Assert.GreaterOrEqual(palette.xMin, pill.xMax, $"palette {palette} right of the sidebar pill {pill}");
        }

        /// <summary>
        /// The layout by the spec formula (ToolBar.ApplySafeArea, PlaceActionBar, ToolHoldMenu.Open) in screen px for a
        /// w x h screen: the column, the Colour button's centre, the palette and the action bar.
        /// </summary>
        static void SpecLayout(float w, float h, Rect safe, out float fit, out Rect column, out Rect palette, out Rect bar)
        {
            float scale = ScreenSnapMetric.ScaleFactor(w, h);
            float margin = EdgeMargin * scale;
            int buttons = ToolBar.FillEnabled ? 10 : 9;
            float columnH = buttons * ButtonH + (buttons - 4) * Spacing + 3 * GroupGap;
            Assert.AreEqual(columnH, ToolBar.ColumnHeight, 1e-3f, "precondition: the column height the spec assumes");
            float bottom = safe.yMin + PlacementRowTop * h + margin;
            float top = Mathf.Min(safe.yMax, CoachTextBottom * h) - margin;
            fit = Mathf.Clamp((top - bottom) / (columnH * scale), MinFit, 1f);
            float half = columnH * scale * fit * 0.5f;
            float centre = Mathf.Clamp((safe.yMin + safe.yMax) * 0.5f, bottom + half, Mathf.Max(bottom + half, top - half));
            float s = scale * fit;
            column = Rect.MinMaxRect(safe.xMax - margin - ButtonW * s, centre - half, safe.xMax - margin, centre + half);
            // Colour is the third button of the second group (sixth, or seventh with Fill).
            int before = ToolBar.FillEnabled ? 6 : 5;   // buttons above it
            float fromTop = before * ButtonH + (before - 1) * Spacing + GroupGap + ButtonH * 0.5f;
            float colourY = column.yMax - fromTop * s;
            float right = column.xMin - RowGap * s;
            // Kept right of the layer sidebar's pill: shrunk when the space is narrower (21:9), not below 0.6 of the fit.
            float limit = safe.xMin + (SidebarRight + SidebarGap) * scale;
            float ps = Mathf.Max(s * ToolHoldMenu.MinLimitScale, Mathf.Min(s, (right - limit) / PaletteW));
            palette = Rect.MinMaxRect(right - PaletteW * ps, colourY - PaletteH * ps * 0.5f, right, colourY + PaletteH * ps * 0.5f);

            float barLeft = safe.xMin + margin, barRight = safe.xMax - (EdgeMargin + ButtonW * fit + EdgeMargin) * scale;
            float barFit = Mathf.Clamp((barRight - barLeft) / (BarWidth * scale), MinFit, 1f);
            float barBottom = safe.yMin + PlacementRowTop * h + margin, mid = (barLeft + barRight) * 0.5f;
            bar = Rect.MinMaxRect(mid - BarWidth * scale * barFit * 0.5f, barBottom, mid + BarWidth * scale * barFit * 0.5f,
                barBottom + BarH * scale * barFit);
        }

        [Test]
        public void SpecLayoutFitsThe390x844ReferenceIPhonesAndNarrowAndroids()
        {
            var phones = new[]
            {
                ("390x844 reference", 390f, 844f, 0f, 0f, 1f),
                ("iPhone 14 (1170x2532)", 1170f, 2532f, 34f, 47f, 0.968f),
                ("iPhone 14 Pro Max (1290x2796)", 1290f, 2796f, 31f, 54f, 0.975f),
                ("Pixel 7 (1080x2400)", 1080f, 2400f, 0f, 0f, -1f),
                ("21:9 Android (1080x2520)", 1080f, 2520f, 0f, 0f, -1f),
            };
            foreach (var (name, w, h, insetBottom, insetTop, wantFit) in phones)
            {
                float s = ScreenSnapMetric.ScaleFactor(w, h);
                var safe = Rect.MinMaxRect(0f, insetBottom * s, w, h - insetTop * s);
                SpecLayout(w, h, safe, out float fit, out Rect column, out Rect palette, out Rect bar);
                if (!ToolBar.FillEnabled && wantFit > 0f) Assert.AreEqual(wantFit, fit, 0.006f, $"{name}: column fit (minimal shrink)");
                Assert.GreaterOrEqual(fit, 0.95f, $"{name}: the column shrinks only a little");
                Assert.GreaterOrEqual(column.yMin, safe.yMin + PlacementRowTop * h, $"{name}: column above the placement row");
                Assert.LessOrEqual(column.yMax, CoachTextBottom * h, $"{name}: column below the coach text");
                Assert.GreaterOrEqual(palette.xMin, (SidebarRight + SidebarGap) * s - 0.01f, $"{name}: palette {palette} right of the layer sidebar");
                Assert.GreaterOrEqual(palette.height / (PaletteH * s), 0.9f, $"{name}: palette shrunk at most a little");
                Assert.IsTrue(palette.yMin > safe.yMin && palette.yMax < safe.yMax, $"{name}: palette inside the safe area");
                Assert.Less(palette.xMax, column.xMin, $"{name}: palette left of the column");
                Assert.Greater(palette.yMin, bar.yMax, $"{name}: palette {palette} above the action bar {bar}");
                Assert.Less(bar.xMax, column.xMin, $"{name}: the action bar still clears the column");
                if (wantFit > 0f) Assert.AreEqual(1f, bar.width / (BarWidth * s), 0.01f, $"{name}: the action bar is not shrunk");
                // The Undo/Redo row: top-left, two buttons; the palette stays below it.
                float undoBottom = safe.yMax - (EdgeMargin + ButtonH * fit) * s;
                Assert.Less(palette.yMax, undoBottom, $"{name}: palette below Undo/Redo");
            }
        }
    }
}
