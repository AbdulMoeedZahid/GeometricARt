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
using Object = UnityEngine.Object;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// The Fill tool's view side (decision 6): the Fill button, the colour bar's "No fill" in the Board button's place,
    /// fills drawn first in the layer's Shapes mesh (under its strokes), hidden layers drawing none, and the cost of a
    /// band-width drag on a banded layer with fills. Edit Mode does not tick MonoBehaviours, so Awake/Start/LateUpdate are
    /// invoked by reflection.
    /// </summary>
    public sealed class FillViewTests
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
            var cb = _appGo.AddComponent<ColorBar>();
            Call(tb, "Awake");
            Call(cb, "Awake");
            Call(tb, "Start");
            Call(cb, "Start");
            _space.PlaceAtReticle();
        }

        void MakeView()
        {
            _viewGo = new GameObject("FillView");
            _view = _viewGo.AddComponent<BoardView>();
            Call(_view, "Awake");
            _view.Bind(_session);
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

        void Click(string name) =>
            ExecuteEvents.Execute(Find(name), new PointerEventData(_es) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);

        void Tap(double x, double y)
        {
            _session.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(x, y), true, 0));
            _session.Pointer(new PointerEvent(PointerPhase.Up, new Vec2(x, y), true, 0));
        }

        [Test]
        public void FillButtonAndNoFillInTheColourBar()
        {
            MakeApp();
            if (!ToolBar.FillEnabled)
            {
                // Switched off (docs/FILL.md, known issue): no Fill button, and "No fill" never shows, even if the engine's
                // Fill tool is selected from code.
                foreach (var t in _appGo.GetComponentsInChildren<Transform>(true))
                    Assert.AreNotEqual("Fill", t.name, "no Fill button while Fill is switched off");
                _session.SetTool(ToolId.Fill);
                Assert.IsFalse(Find("No fill").activeSelf, "No fill stays hidden");
                Assert.IsTrue(Find("Board").activeSelf, "the Board button stays");
                return;
            }
            Assert.IsTrue(Find("Board").activeSelf && !Find("No fill").activeSelf, "Board button outside Fill");
            Click("Fill");
            Assert.AreEqual(ToolId.Fill, _session.ActiveTool, "Fill button selects the Fill tool");
            Assert.AreNotEqual(Find("Medium").GetComponent<Image>().color, Find("Fill").GetComponent<Image>().color, "highlighted");
            Assert.IsTrue(Find("No fill").activeSelf && !Find("Board").activeSelf, "No fill takes the Board button's place");
            Click("No fill");
            Assert.IsTrue(_session.FillErase, "No fill: taps clear");
            Click("Red");
            Assert.IsFalse(_session.FillErase, "a colour ends No fill");
            Assert.AreEqual(Palette.Red, _session.Style.Color);
            Click("No fill");
            Click("Line");
            Assert.IsFalse(_session.FillErase, "leaving Fill resets it");
            Assert.IsTrue(Find("Board").activeSelf && !Find("No fill").activeSelf, "Board button back");
        }

        [Test]
        public void FillsDrawUnderTheStrokesAndNotOnHiddenLayers()
        {
            MakeView();
            int plain = ShapeVertices();
            _session.SetTool(ToolId.Fill);
            Tap(0.5, 0.5);   // the star's octagon
            Assert.AreEqual(1, _session.ActiveLayer.Fills.Count, "filled");
            int filled = ShapeVertices();
            Assert.Greater(filled, plain, "fill triangles added");
            Assert.AreEqual(1, _view.Fills.BuildCount, "one fill build");
            var verts = _viewGo.transform.Find("Shapes").GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(verts.vertexCount, 0);
            _session.SetLayerVisible(_session.ActiveLayer.Id, false);
            Assert.Less(ShapeVertices(), plain, "a hidden layer draws neither strokes nor fills");
        }

        [Test]
        public void WidthDragOnABandedLayerWithFillsCost()
        {
            var layer = _session.ActiveLayer;
            layer.Shapes.Add(new CircleShape(_session.Board.NewId(), new Vec2(0.5, 0.5), 0.12, StrokeStyle.Default));
            layer.Shapes.Add(new CircleShape(_session.Board.NewId(), new Vec2(0.5, 0.5), 0.3, StrokeStyle.Default));
            var bands = LayerBands.Default;
            bands.On = true;
            _session.SetLayerBands(layer.Id, bands);
            _session.SetTool(ToolId.Fill);
            Tap(0.5, 0.5);
            Tap(0.5, 0.17);
            MakeView();
            float now = 5f;
            _view.Clock = () => now;
            ShapeVertices();
            int builds = _view.Fills.BuildCount;
            double worst = 0;
            for (int i = 1; i <= 5; i++)
            {
                bands.Width = 0.015 + 0.001 * i;
                _session.SetLayerBands(layer.Id, bands);
                now += 0.016f;   // one frame per slider tick
                var sw = System.Diagnostics.Stopwatch.StartNew();
                ShapeVertices();
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            Debug.Log($"[Fill] banded star + 2 circles with {layer.Fills.Count} fills: width-slider frame worst {worst:0.0} ms, fill rebuilds during the drag {_view.Fills.BuildCount - builds}");
            Assert.LessOrEqual(_view.Fills.BuildCount - builds, 1, "the fill is not rebuilt every frame of the drag");
            Assert.Less(worst, 80.0, "a width-slider frame with fills stays usable in the editor");
            now += FillCache.StaleSeconds + 0.01f;
            int before = _view.Fills.BuildCount;
            ShapeVertices();
            Assert.AreEqual(before + 1, _view.Fills.BuildCount, "after the drag the fill catches up");
            ShapeVertices();
            Assert.AreEqual(before + 1, _view.Fills.BuildCount, "and then stays put");
        }
    }
}
