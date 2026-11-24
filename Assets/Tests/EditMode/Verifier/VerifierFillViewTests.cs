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
    /// VERIFIER (decision 6 / P6 UI): "No fill" takes the Board button's place only while Fill is active, a colour ends it,
    /// leaving Fill resets it and brings Board back; the Snap button keeps its tap (snapping on/off) and its hold opens
    /// the H/V chip (the release of the hold toggles nothing), and the label reads "H/V" while the lock is on.
    /// </summary>
    public sealed class VerifierFillViewTests
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
        float _now = 10f;

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
            var cb = _appGo.AddComponent<ColorBar>();
            foreach (MonoBehaviour c in new MonoBehaviour[] { tb, cb }) Call(c, "Awake");
            foreach (MonoBehaviour c in new MonoBehaviour[] { tb, cb }) Call(c, "Start");
            _space.PlaceAtReticle();
            foreach (var m in _appGo.GetComponentsInChildren<ToolHoldMenu>(true)) m.Clock = () => _now;
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

        static Vector2 Center(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;
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

        /// <summary>The first palette swatch's clickable GameObject (a raycast Graphic with a Button under the pill).</summary>
        GameObject FirstSwatch()
        {
            var pill = Find("Pill").transform;
            foreach (Transform t in pill)
                if (t.GetComponent<Button>() != null && t.name != "Board" && t.name != "No fill" && t.name != "Custom") return t.gameObject;
            Assert.Fail("no palette swatch under the pill");
            return null;
        }

        // ---------- No fill ----------

        [Test]
        public void NoFillTakesBoardsPlaceOnlyInFillAndResets()
        {
            Assert.IsTrue(Find("Board").activeSelf && !Find("No fill").activeSelf, "outside Fill: Board shown, No fill hidden");
            Click(Find("Fill"));
            Assert.AreEqual(ToolId.Fill, _session.ActiveTool, "the Fill button selects Fill");
            Assert.IsTrue(!Find("Board").activeSelf && Find("No fill").activeSelf, "in Fill: No fill in Board's place");
            Click(Find("No fill"));
            Assert.IsTrue(_session.FillErase, "No fill on");
            int steps = _session.UndoCount;
            Click(FirstSwatch());
            Assert.IsFalse(_session.FillErase, "a colour ends No fill");
            Assert.AreEqual(steps, _session.UndoCount, "picking a colour in Fill records nothing");
            Click(Find("No fill"));
            Assert.IsTrue(_session.FillErase);
            Click(Find("Line"));
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);
            Assert.IsFalse(_session.FillErase, "leaving Fill resets No fill");
            Assert.IsTrue(Find("Board").activeSelf && !Find("No fill").activeSelf, "Board is back");
            Click(Find("Fill"));
            Assert.IsFalse(_session.FillErase, "re-entering Fill starts with colours, not No fill");
        }

        [Test]
        public void ColoursDoNotRestyleASelectionWhileInFill()
        {
            // Entering Fill clears the selection anyway (Fill keeps no selection); a colour then only sets the style.
            var star = _session.ActiveLayer;
            _session.SetTool(ToolId.SelectMove);
            _session.SetSelection(new[] { star.Shapes[0].Id });
            Click(Find("Fill"));
            Assert.AreEqual(0, _session.Selection.Ids.Count, "Fill drops the selection");
            var before = star.Shapes[0].Style;
            Click(FirstSwatch());
            Assert.IsTrue(before.Equals(star.Shapes[0].Style), "no restyle in Fill");
        }

        // ---------- Snap hold menu ----------

        [Test]
        public void SnapTapTogglesAndHoldOpensTheHVChip()
        {
            var snap = Find("Snap");
            Text label = snap.GetComponentInChildren<Text>();
            Assert.AreEqual("Snap", label.text);
            Click(snap);
            Assert.IsFalse(_session.SnapEnabled, "tap: snapping off");
            Click(snap);
            Assert.IsTrue(_session.SnapEnabled, "tap: back on");

            var e = Ped(snap);
            ExecuteEvents.Execute(snap, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(snap, e, ExecuteEvents.initializePotentialDrag);
            _now += ToolHoldMenu.HoldSeconds + 0.05f;
            Call(snap.GetComponent<ToolHoldMenu>(), "Update");
            var chip = Find("Snap menu: H/V");
            Assert.IsTrue(chip.activeInHierarchy, "hold: the H/V chip opens");
            ExecuteEvents.Execute(snap, e, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(snap, e, ExecuteEvents.pointerClickHandler);
            Assert.IsTrue(_session.SnapEnabled, "the release of the hold does not toggle snapping");
            Click(chip);
            Assert.IsTrue(_session.OrthoLock, "the chip turns the H/V lock on");
            Assert.AreEqual("H/V", label.text, "label reads H/V while locked");
            Assert.IsTrue(_session.SnapEnabled, "and snapping stays on");
            _session.SetOrthoLock(false);
            Assert.AreEqual("Snap", label.text, "lock off elsewhere: label back to Snap");
        }

        [Test]
        public void ColumnFitsAndHasNoHVButton()
        {
            string[] names = { "Line", "Circle", "Select", "Fill", "Bands", "Medium", "Square", "2x2", "Snap" };
            var column = Find("Column").transform;
            int n = 0;
            foreach (Transform c in column) if (c.GetComponent<Button>() != null) n++;
            Assert.AreEqual(9, n, "9 buttons in the column");
            foreach (var t in column.GetComponentsInChildren<Transform>(true)) Assert.AreNotEqual("HV", t.name, "H/V moved into Snap's menu");
            var canvas = _appGo.GetComponentInChildren<Canvas>(true);
            Vector3[] cc = new Vector3[4], bc = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(cc);
            float prevTop = float.MaxValue;
            foreach (var name in names)
            {
                ((RectTransform)Find(name).transform).GetWorldCorners(bc);
                Assert.GreaterOrEqual(bc[0].y, cc[0].y, $"{name} above the bottom");
                Assert.Less(bc[2].y, prevTop, $"{name} in order");
                prevTop = bc[0].y;
            }
        }
    }
}
