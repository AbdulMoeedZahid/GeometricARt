using System;
using System.Reflection;
using GeoART.App;
using GeoART.BoardRender;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// VERIFIER: colour options. The ToolBar's Colour palette with the ColorBar behind it (5 shape colours for new shapes,
    /// board guide lines Black/White toggle, hidden until placed, no bottom pill any more) and BoardView's guide-line
    /// colour (dashed grid and outline only, no fill: Black is the original look, White is light).
    /// </summary>
    public sealed class VerifierColorTests
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

        static readonly Color32 BlackGrid = new Color(0.2f, 0.2f, 0.25f, 0.85f);     // original dividerColor
        static readonly Color32 WhiteGrid = new Color(0.93f, 0.93f, 0.95f, 0.85f);
        static readonly Color32 Sheet = new Color(1f, 1f, 1f, 0.32f);               // flat scene's translucent sheet

        EditorSession _session;
        Space _space;
        GameObject _go, _esGo, _boardGo;
        EventSystem _es;

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
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            if (_esGo != null) { EsEnable(_es, false); UnityEngine.Object.DestroyImmediate(_esGo); }
            if (_boardGo != null)
            {
                foreach (var mf in _boardGo.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) UnityEngine.Object.DestroyImmediate(mf.sharedMesh);
                foreach (var mr in _boardGo.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial != null) UnityEngine.Object.DestroyImmediate(mr.sharedMaterial);
                UnityEngine.Object.DestroyImmediate(_boardGo);
            }
            BoardAppearance.ResetForNewSession();
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

        static void SetField(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(f, $"{target.GetType().Name}.{field} not found");
            f.SetValue(target, value);
        }

        /// <summary>The ToolBar (Colour button and palette) and the ColorBar (colour logic, wheel) on one GameObject.</summary>
        ColorBar AddColorBar()
        {
            var tb = _go.AddComponent<ToolBar>();
            var c = _go.AddComponent<ColorBar>();
            Call(tb, "Awake");
            Call(c, "Awake");
            Call(tb, "Start");
            Call(c, "Start");
            return c;
        }

        GameObject Find(string name)
        {
            foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        void Click(GameObject go) =>
            ExecuteEvents.Execute(go, new PointerEventData(_es) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);

        /// <summary>Opens the palette with a click on Colour, then clicks the chip.</summary>
        void Pick(string chip)
        {
            Click(Find("Colour"));
            Click(Find("Colour menu: " + chip));
        }

        Color ChipBg(string chip) => Find("Colour menu: " + chip).GetComponent<Image>().color;

        // ---------- Colour button and palette ----------

        [Test]
        public void Palette_HiddenUntilPlaced_AndBlocksTouches()
        {
            AddColorBar();
            var canvas = Find("Colour").GetComponentInParent<Canvas>(true);
            Assert.IsFalse(canvas.enabled, "hidden before placement");
            _space.PlaceAtReticle();
            Assert.IsTrue(canvas.enabled, "shown once placed");
            foreach (var name in new[] { "Colour", "Colour menu", "Colour menu: Black", "Colour menu: White", "Colour menu: Red",
                         "Colour menu: Green", "Colour menu: Blue", "Colour menu: Custom", "Colour menu: Wheel…", "Colour menu: Board" })
                Assert.IsTrue(Find(name).GetComponent<Graphic>().raycastTarget, $"{name} blocks drawing under it");
            _space.Reset();
            Assert.IsFalse(canvas.enabled, "hidden again after re-place");
        }

        [Test]
        public void Palette_NoBottomPillAnyMore()
        {
            AddColorBar();
            _space.PlaceAtReticle();
            foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                Assert.AreNotEqual("Pill", t.name, "the bottom-left colour pill is gone (nothing sits under the action bar)");
            var canvases = _go.GetComponentsInChildren<Canvas>(true);
            foreach (var c in canvases)
                Assert.IsTrue(c.name == "ToolBar" || c.name == "HoldMenus" || c.name == "ColorWheel",
                    $"only the ToolBar (with its open-menus canvas) and the colour wheel canvases ({c.name})");
        }

        [Test]
        public void Palette_SwatchSetsColorForNewShapes_KeepsWidthAndDash()
        {
            AddColorBar();
            _space.PlaceAtReticle();
            _session.Style = new StrokeStyle(Palette.Black, LineWidth.Thick, LineStyle.Dotted);
            Click(Find("Colour"));
            Assert.AreNotEqual(ChipBg("Black"), ChipBg("Red"), "black highlighted by default");

            Click(Find("Colour menu: Red"));
            Assert.AreEqual(Palette.Red, _session.Style.Color);
            Assert.AreEqual(LineWidth.Thick, _session.Style.Width, "width kept");
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "dash kept");
            Click(Find("Colour"));
            Assert.AreNotEqual(ChipBg("Red"), ChipBg("Black"), "red highlighted");
            Assert.AreEqual(ChipBg("Black"), ChipBg("Green"), "black no longer highlighted");
            Click(Find("Colour"));   // a second click closes it

            foreach (var (name, color) in new[] { ("White", Palette.White), ("Green", Palette.Green), ("Blue", Palette.Blue), ("Black", Palette.Black) })
            {
                Pick(name);
                Assert.AreEqual(color, _session.Style.Color, name);
            }
        }

        [Test]
        public void Palette_ExistingShapesKeepTheirColor()
        {
            AddColorBar();
            _session.ActiveLayer.Shapes.Add(new LineShape(_session.Board.NewId(), new Vec2(0.1, 0.1), new Vec2(0.9, 0.9), StrokeStyle.Default));
            Pick("Blue");
            Assert.AreEqual(Palette.Blue, _session.Style.Color);
            Assert.AreEqual(Palette.Black, _session.ActiveLayer.Shapes[0].Style.Color);
        }

        [Test]
        public void Palette_BoardChipTogglesBlackAndWhite_AndStaysOpen()
        {
            AddColorBar();
            _space.PlaceAtReticle();
            Assert.AreEqual(BoardGuideColor.Black, BoardAppearance.Guides, "black (original look) by default");
            var menu = Find("Colour").GetComponent<ToolHoldMenu>();
            RawImage icon = Find("Colour menu: Board").transform.Find("Icon").GetComponent<RawImage>();
            Assert.Less(icon.color.r, 0.2f, "the Board icon shows black guides");

            Click(Find("Colour"));
            Click(Find("Colour menu: Board"));
            Assert.AreEqual(BoardGuideColor.White, BoardAppearance.Guides);
            Assert.Greater(icon.color.r, 0.8f, "the Board icon shows white guides");
            Assert.IsTrue(menu.IsOpen, "a toggle leaves the palette open");
            Click(Find("Colour menu: Board"));
            Assert.AreEqual(BoardGuideColor.Black, BoardAppearance.Guides);
            Assert.Less(icon.color.r, 0.2f);
            BoardAppearance.SetGuides(BoardGuideColor.White);   // set elsewhere: the icon follows
            Assert.Greater(icon.color.r, 0.8f);
        }

        // ---------- BoardView guide-line colour ----------

        BoardView NewBoardView(bool showBase)
        {
            _boardGo = new GameObject("Board");
            var view = _boardGo.AddComponent<BoardView>();
            SetField(view, "showBase", showBase);
            Call(view, "Awake");
            view.Bind(_session);
            return view;
        }

        Color32[] BaseColors(BoardView view)
        {
            view.MarkAllDirty();
            Call(view, "LateUpdate");
            return _boardGo.transform.Find("Base").GetComponent<MeshFilter>().sharedMesh.colors32;
        }

        static bool Has(Color32[] colors, Color32 c)
        {
            foreach (var x in colors)
                if (x.r == c.r && x.g == c.g && x.b == c.b && x.a == c.a) return true;
            return false;
        }

        static bool HasOpaque(Color32[] colors)
        {
            foreach (var x in colors)
                if (x.a == 255) return true;
            return false;
        }

        [Test]
        public void Guides_BlackByDefault_IsTheOriginalDashedLook_InAR()
        {
            var view = NewBoardView(showBase: false);   // AR prefab
            Color32[] colors = BaseColors(view);
            Assert.Greater(colors.Length, 0, "dashed grid and outline drawn");
            Assert.IsTrue(Has(colors, BlackGrid), "original grid colour");
            Assert.IsFalse(HasOpaque(colors), "nothing filled: no opaque board over the surface");
        }

        [Test]
        public void Guides_White_RecoloursGridOnly()
        {
            var view = NewBoardView(showBase: false);
            int vertsBlack = BaseColors(view).Length;
            BoardAppearance.SetGuides(BoardGuideColor.White);
            Color32[] colors = BaseColors(view);
            Assert.IsTrue(Has(colors, WhiteGrid), "white grid");
            Assert.IsFalse(Has(colors, BlackGrid), "black grid gone");
            Assert.AreEqual(vertsBlack, colors.Length, "same dashed geometry, only the colour changes");
            Assert.IsFalse(HasOpaque(colors), "still nothing filled");
        }

        [Test]
        public void Guides_FlatScene_KeepsTranslucentSheet()
        {
            var view = NewBoardView(showBase: true);
            Assert.IsTrue(Has(BaseColors(view), Sheet), "sheet as before");
            BoardAppearance.SetGuides(BoardGuideColor.White);
            Assert.IsTrue(Has(BaseColors(view), Sheet), "sheet unchanged with white guides");
        }
    }
}
