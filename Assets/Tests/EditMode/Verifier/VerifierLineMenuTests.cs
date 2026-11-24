using System;
using System.Collections.Generic;
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
    /// VERIFIER (ui-line-plan 2, P1): one Line button with a hold-to-pick mode menu, driven through the real ToolBar with
    /// EventSystem pointer events and a fake clock. Edit Mode does not tick MonoBehaviours, so Awake/Start/Update are
    /// invoked by reflection. The hold-menu component is found by type name ("*HoldMenu*") and its injectable clock as
    /// any Func&lt;float&gt; field or property on it (plan 2.3), so this file compiles whatever the component's exact shape.
    /// Chips are found by their label text ("Solid", "Dotted"); the blocker by a name containing "Blocker".
    /// </summary>
    public sealed class VerifierLineMenuTests
    {
        const float HoldSeconds = 0.45f;   // plan 2.1, restated
        const float SlopRefPx = 8f;

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
        ToolBar _bar;
        float _now = 100f;
        readonly List<string> _toasts = new List<string>();

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession();
            _session.Toast += m => _toasts.Add(m);
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
            _go = new GameObject("App");
            _bar = _go.AddComponent<ToolBar>();
            Call(_bar, "Awake");
            Call(_bar, "Start");
            _space.PlaceAtReticle();
            Canvas.ForceUpdateCanvases();
            foreach (var m in Menus()) SetClock(m);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            if (_esGo != null) { EsEnable(_esGo.GetComponent<EventSystem>(), false); UnityEngine.Object.DestroyImmediate(_esGo); }
            ServiceHub.Clear();
            _toasts.Clear();
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

        static void CallIfAny(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            m?.Invoke(target, null);
        }

        GameObject Find(string name)
        {
            foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        /// <summary>Every hold-menu component under the ToolBar (Line and Circle at least).</summary>
        List<MonoBehaviour> Menus()
        {
            var l = new List<MonoBehaviour>();
            foreach (var mb in _go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name.Contains("HoldMenu")) l.Add(mb);
            return l;
        }

        MonoBehaviour MenuOn(GameObject button)
        {
            foreach (var mb in button.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name.Contains("HoldMenu")) return mb;
            Assert.Fail($"no *HoldMenu* component on {button.name} (plan 2.3: added next to the Button)");
            return null;
        }

        /// <summary>Points every Func&lt;float&gt; field/property (instance or static) of the menu at the fake clock.</summary>
        void SetClock(MonoBehaviour menu)
        {
            Func<float> clock = () => _now;
            bool set = false;
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            for (var t = menu.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var f in t.GetFields(all))
                    if (f.FieldType == typeof(Func<float>) && !f.IsInitOnly) { f.SetValue(f.IsStatic ? null : menu, clock); set = true; }
                foreach (var p in t.GetProperties(all))
                    if (p.PropertyType == typeof(Func<float>) && p.CanWrite)
                    {
                        p.SetValue(p.GetSetMethod(true).IsStatic ? null : menu, clock);
                        set = true;
                    }
            }
            Assert.IsTrue(set, $"{menu.GetType().Name}: no writable Func<float> clock (plan 2.3: injectable clock)");
        }

        void Tick()
        {
            foreach (var m in Menus()) { CallIfAny(m, "Update"); CallIfAny(m, "LateUpdate"); }
            CallIfAny(_bar, "Update");
            Canvas.ForceUpdateCanvases();
        }

        void Advance(float seconds)
        {
            _now += seconds;
            Tick();
        }

        static Vector2 Center(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;
        }

        PointerEventData Ped(Vector2 pos) =>
            new PointerEventData(_es) { position = pos, pressPosition = pos, pointerId = 0, button = PointerEventData.InputButton.Left };

        PointerEventData Down(GameObject go)
        {
            var e = Ped(Center(go));
            e.pointerPress = go;
            e.rawPointerPress = go;
            e.pointerPressRaycast = new RaycastResult { gameObject = go, screenPosition = e.position };
            ExecuteEvents.Execute(go, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, e, ExecuteEvents.initializePotentialDrag);
            return e;
        }

        void DragTo(GameObject pressed, PointerEventData e, Vector2 pos, int steps = 4)
        {
            Vector2 start = e.position;
            if (!e.dragging)
            {
                e.dragging = true;
                e.pointerDrag = pressed;
                ExecuteEvents.Execute(pressed, e, ExecuteEvents.beginDragHandler);
            }
            for (int i = 1; i <= steps; i++)
            {
                e.position = Vector2.Lerp(start, pos, (float)i / steps);
                e.delta = (pos - start) / steps;
                ExecuteEvents.Execute(pressed, e, ExecuteEvents.dragHandler);
            }
        }

        /// <summary>Release; then the click the EventSystem would send if the finger is still over the pressed button.</summary>
        void Up(GameObject pressed, PointerEventData e)
        {
            ExecuteEvents.Execute(pressed, e, ExecuteEvents.pointerUpHandler);
            if (e.dragging) ExecuteEvents.Execute(pressed, e, ExecuteEvents.endDragHandler);
            if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)pressed.transform, e.position, null))
                ExecuteEvents.Execute(pressed, e, ExecuteEvents.pointerClickHandler);
        }

        void Click(GameObject go)
        {
            var e = Down(go);
            Up(go, e);
        }

        /// <summary>
        /// An active chip with this label (the nearest ancestor of the label with a raycast-target Graphic), or null. No
        /// column button is labelled "Solid" or "Dotted" after P1 (the Dotted button is gone, Bands reads Off/Double/Weave).
        /// </summary>
        GameObject Chip(string label)
        {
            foreach (var t in _go.GetComponentsInChildren<Text>(false))
            {
                if (!t.gameObject.activeInHierarchy || !string.Equals(t.text, label, StringComparison.OrdinalIgnoreCase)) continue;
                for (var p = t.transform.parent; p != null; p = p.parent)
                {
                    var g = p.GetComponent<Graphic>();
                    if (g != null && g.raycastTarget && p.GetComponent<Text>() == null) return p.gameObject;
                }
            }
            return null;
        }

        bool MenuOpen => Chip("Dotted") != null && Chip("Solid") != null;

        GameObject Blocker()
        {
            foreach (var t in _go.GetComponentsInChildren<Transform>(false))
                if (t.gameObject.activeInHierarchy && t.name.IndexOf("Blocker", StringComparison.OrdinalIgnoreCase) >= 0
                    && t.GetComponent<Graphic>() != null && t.GetComponent<Graphic>().raycastTarget
                    && t.name != "Column" && t.parent != null && t.parent.name != "Column")
                    return t.gameObject;
            return null;
        }

        /// <summary>Reference px -> screen px, as the hold slop measures it (CanvasScaler formula for the screen).</summary>
        static float Scale => ScreenSnapMetric.ScaleFactor(Screen.width, Screen.height);

        /// <summary>Hold the button past the hold time without moving; returns the still-open pointer.</summary>
        PointerEventData Hold(GameObject button)
        {
            var e = Down(button);
            Advance(HoldSeconds * 0.5f);
            Assert.IsFalse(MenuOpen, "not open before 0.45 s");
            Advance(HoldSeconds * 0.5f + 0.05f);
            return e;
        }

        Texture IconOf(string button) => Find(button).transform.Find("Icon").GetComponent<RawImage>().texture;

        // ---------- the column ----------

        [Test]
        public void ColumnHasNineButtonsWithBandsInTheOldDottedSlot()
        {
            string[] names = { "Line", "Circle", "Select", "Bands", "Medium", "Square", "2x2", "Snap", "HV" };
            var column = Find("Column").transform;
            var direct = new List<string>();
            foreach (Transform c in column) if (c.GetComponent<Button>() != null && c.gameObject.activeSelf) direct.Add(c.name);
            Assert.AreEqual(9, direct.Count, "9 buttons directly under Column: " + string.Join(",", direct));
            var canvas = _go.GetComponentInChildren<Canvas>(true);
            Vector3[] cc = new Vector3[4], bc = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(cc);
            float prevTop = float.MaxValue;
            foreach (var n in names)
            {
                ((RectTransform)Find(n).transform).GetWorldCorners(bc);
                Assert.GreaterOrEqual(bc[0].y, cc[0].y, $"{n} above the bottom edge");
                Assert.LessOrEqual(bc[2].y, cc[2].y, $"{n} below the top edge");
                Assert.Less(bc[2].y, prevTop, $"{n} below the previous button (order)");
                prevTop = bc[0].y;
            }
            foreach (var t in column.GetComponentsInChildren<Transform>(true))
                Assert.AreNotEqual("Dotted", t.name, "the Dotted button is gone (its slot is Bands)");
            Assert.AreEqual(ToolBar.ColumnHeight, 9 * 52f + 5 * 8f + 3 * 14f, 0.01f, "ColumnHeight unchanged (550)");
        }

        [Test]
        public void BandsButtonShowsOffForThePlainLayer()
        {
            var label = Find("Bands").GetComponentInChildren<Text>();
            Assert.AreEqual("Off", label.text, "Bands label shows the active layer's state (plan 3.3)");
        }

        [Test]
        public void LineAndCircleCarryAHoldMenuOthersDoNot()
        {
            MenuOn(Find("Line"));
            MenuOn(Find("Circle"));
            foreach (var n in new[] { "Select", "Medium", "Square", "2x2", "Snap", "HV" })
                foreach (var mb in Find(n).GetComponents<MonoBehaviour>())
                    Assert.IsFalse(mb.GetType().Name.Contains("HoldMenu"), $"{n} has no hold menu in P1");
            Assert.NotNull(Find("Line").GetComponent<Button>(), "the uGUI Button stays (view tests click through it)");
        }

        // ---------- tap ----------

        [Test]
        public void TapSelectsLineWithoutOpeningTheMenu()
        {
            _session.SetTool(ToolId.Circle);
            var e = Down(Find("Line"));
            Advance(0.1f);
            Up(Find("Line"), e);
            Advance(1f);
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "tap selects Line");
            Assert.IsFalse(MenuOpen, "a tap never opens the menu");
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "mode unchanged");
            Assert.IsNull(Blocker(), "no blocker left behind to eat the next board touch");
        }

        [Test]
        public void TappingTheActiveLineAgainDoesNotOpenTheMenu()
        {
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);
            Click(Find("Line"));
            Advance(1f);
            Click(Find("Line"));
            Advance(1f);
            Assert.IsFalse(MenuOpen, "re-tapping the active tool does not open the picker (plan 2.1)");
            Assert.IsNull(Blocker());
        }

        // ---------- hold ----------

        [Test]
        public void HoldOpensChipsLeftOfTheButtonAndSelectsLine()
        {
            _session.SetTool(ToolId.Circle);
            var e = Hold(Find("Line"));
            Assert.IsTrue(MenuOpen, "at 0.45 s the Solid/Dotted chips open");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "the hold selects the Line tool");
            var btn = new Vector3[4];
            ((RectTransform)Find("Line").transform).GetWorldCorners(btn);
            float cy = (btn[0].y + btn[2].y) / 2;
            foreach (var label in new[] { "Solid", "Dotted" })
            {
                var c = new Vector3[4];
                ((RectTransform)Chip(label).transform).GetWorldCorners(c);
                Assert.LessOrEqual(c[2].x, btn[0].x + 0.5f, $"{label} chip pops out to the LEFT of the button");
                Assert.GreaterOrEqual(c[0].x, 0f, $"{label} chip is on screen");
                Assert.That(cy, Is.InRange(c[0].y, c[2].y), $"{label} chip row is vertically centred on the button");
            }
            Up(Find("Line"), e);
        }

        [Test]
        public void ClickAfterAHoldIsSuppressedAndTheRowStaysOpen()
        {
            var e = Hold(Find("Line"));
            Assert.IsTrue(MenuOpen);
            Up(Find("Line"), e);   // released on the button itself: pointerUp + pointerClick
            Advance(0.1f);
            Assert.IsTrue(MenuOpen, "release away from a chip leaves the row open (plan 2.1)");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "nothing picked");
            Assert.NotNull(Blocker(), "a transparent full-screen blocker sits under the open chips");
        }

        [Test]
        public void SlideOntoDottedAndReleasePicksIt()
        {
            var line = Find("Line");
            var e = Hold(line);
            DragTo(line, e, Center(Chip("Dotted")));
            Up(line, e);
            Advance(0.1f);
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "slide-release on Dotted sets Style.Dash");
            Assert.IsTrue(_session.DottedLine, "DottedLine stays the shared view of Style.Dash");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);
            Assert.IsFalse(MenuOpen, "picking closes the row");
            Assert.IsNull(Blocker(), "and its blocker");
        }

        [Test]
        public void SlideBackToSolidPicksSolid()
        {
            _session.Style = _session.Style.WithDash(LineStyle.Dotted);
            var line = Find("Line");
            var e = Hold(line);
            DragTo(line, e, Center(Chip("Dotted")));
            DragTo(line, e, Center(Chip("Solid")));
            Up(line, e);
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "the chip under the finger at release wins");
            Assert.IsFalse(MenuOpen);
        }

        [Test]
        public void ReleaseThenTapAChipPicksIt()
        {
            var line = Find("Line");
            var e = Hold(line);
            Up(line, e);
            Assert.IsTrue(MenuOpen, "precondition: still open after release");
            Click(Chip("Dotted"));
            Advance(0.1f);
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "tapping a chip picks it");
            Assert.IsFalse(MenuOpen, "and closes the row");
            Assert.IsNull(Blocker());
        }

        [Test]
        public void TapOnTheBlockerClosesWithoutPicking()
        {
            var line = Find("Line");
            var e = Hold(line);
            Up(line, e);
            var blocker = Blocker();
            Assert.NotNull(blocker, "precondition: blocker present");
            var b = Ped(new Vector2(20f, 20f));
            b.pointerPress = blocker;
            ExecuteEvents.Execute(blocker, b, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(blocker, b, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(blocker, b, ExecuteEvents.pointerClickHandler);
            Advance(0.1f);
            Assert.IsFalse(MenuOpen, "tap outside (on the blocker) closes the row");
            Assert.IsNull(Blocker(), "the blocker goes with it");
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash, "nothing picked");
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);
        }

        [Test]
        public void MovingBeforeTheHoldCancelsIt()
        {
            _session.SetTool(ToolId.Circle);
            var line = Find("Line");
            var e = Down(line);
            Advance(0.1f);
            DragTo(line, e, e.position + new Vector2(-3f * SlopRefPx * Scale, 0f));   // 24 ref px, beyond the 8 px slop
            Advance(1f);
            Assert.IsFalse(MenuOpen, "moving more than 8 ref px before 0.45 s cancels the hold");
            Assert.IsNull(Blocker());
            Up(line, e);
            Assert.AreEqual(LineStyle.Solid, _session.Style.Dash);
        }

        [Test]
        public void SmallJitterDoesNotCancelTheHold()
        {
            var line = Find("Line");
            var e = Down(line);
            Advance(0.1f);
            DragTo(line, e, e.position + new Vector2(0.5f * SlopRefPx * Scale, 0.3f * SlopRefPx * Scale));   // ~4.7 ref px
            Advance(0.4f);
            Assert.IsTrue(MenuOpen, "jitter inside the 8 px slop still opens the menu");
            Up(line, e);
        }

        [Test]
        public void CircleHoldSharesTheMode()
        {
            var circle = Find("Circle");
            var e = Hold(circle);
            Assert.IsTrue(MenuOpen, "Circle has the same picker");
            Assert.AreEqual(ToolId.Circle, _session.ActiveTool, "holding Circle selects Circle");
            DragTo(circle, e, Center(Chip("Dotted")));
            Up(circle, e);
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "dashed circles: the mode is the shared Style.Dash");
            Assert.AreEqual(ToolId.Circle, _session.ActiveTool);
        }

        [Test]
        public void HoldWhileAnotherMenuIsOpenNeverLeavesTwoRows()
        {
            var line = Find("Line");
            var e = Hold(line);
            Up(line, e);
            // The blocker covers the screen: a second press goes to it, not to Circle. Close, then open Circle's row.
            var blocker = Blocker();
            Assert.NotNull(blocker);
            var b = Ped(Center(Find("Circle")));
            ExecuteEvents.Execute(blocker, b, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(blocker, b, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(blocker, b, ExecuteEvents.pointerClickHandler);
            Advance(0.1f);
            Assert.IsFalse(MenuOpen, "precondition: the blocker press closed the Line row");
            var e2 = Hold(Find("Circle"));
            int dotted = 0;
            foreach (var t in _go.GetComponentsInChildren<Text>(false))
                if (t.gameObject.activeInHierarchy && t.text == "Dotted") dotted++;
            Assert.AreEqual(1, dotted, "exactly one chip row is open");
            Up(Find("Circle"), e2);
        }

        [Test]
        public void ChipRowFollowsAShrunkColumn()
        {
            // Short / landscape screens shrink the column (ApplySafeArea: localScale = fit, MinFit 0.4). The row must
            // open at the button's on-screen scale, vertically centred on it, RowGap * fit to its left, and still pick.
            foreach (float fit in new[] { 0.4f, 0.7f })
            {
                var column = (RectTransform)Find("Column").transform;
                column.localScale = new Vector3(fit, fit, 1f);
                Canvas.ForceUpdateCanvases();
                var line = Find("Line");
                var btn = new Vector3[4];
                ((RectTransform)line.transform).GetWorldCorners(btn);
                float bh = btn[1].y - btn[0].y, cy = (btn[0].y + btn[1].y) / 2;
                var e = Hold(line);
                Assert.IsTrue(MenuOpen, $"fit {fit}: open");
                float canvasScale = _go.GetComponentInChildren<Canvas>(true).transform.lossyScale.y;
                float rightmost = float.MinValue;
                foreach (var label in new[] { "Solid", "Dotted" })
                {
                    var c = new Vector3[4];
                    ((RectTransform)Chip(label).transform).GetWorldCorners(c);
                    Assert.AreEqual(bh, c[1].y - c[0].y, 0.5f, $"fit {fit}: {label} chip as tall as the shrunk button");
                    Assert.AreEqual(cy, (c[0].y + c[1].y) / 2, 0.5f, $"fit {fit}: {label} chip centred on the button");
                    Assert.GreaterOrEqual(c[0].x, 0f, $"fit {fit}: {label} on screen");
                    rightmost = Mathf.Max(rightmost, c[2].x);
                }
                Assert.AreEqual(btn[0].x - 10f * fit * canvasScale, rightmost, 1f, $"fit {fit}: RowGap * fit left of the button");
                DragTo(line, e, Center(Chip("Dotted")));
                Up(line, e);
                Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, $"fit {fit}: slide-release still picks");
                _session.Style = _session.Style.WithDash(LineStyle.Solid);
                Advance(0.1f);
            }
        }

        // ---------- icons ----------

        [Test]
        public void IconsFollowTheModeWhereverItChanges()
        {
            Texture lineSolid = IconOf("Line"), circleSolid = IconOf("Circle");
            _session.ToggleDottedLine();   // changed elsewhere (the engine's own toggle)
            Assert.AreNotSame(lineSolid, IconOf("Line"), "Line icon shows the dashed diagonal");
            Assert.AreNotSame(circleSolid, IconOf("Circle"), "Circle icon shows the dashed ring");
            Texture lineDotted = IconOf("Line");
            _session.Style = _session.Style.WithDash(LineStyle.Solid);
            Assert.AreSame(lineSolid, IconOf("Line"), "back to the solid diagonal (textures reused, not rebuilt)");
            Assert.AreSame(circleSolid, IconOf("Circle"), "back to the solid ring");
            _session.Style = _session.Style.WithDash(LineStyle.Dotted);
            Assert.AreSame(lineDotted, IconOf("Line"), "dashed texture reused");
        }

        [Test]
        public void ColourAndWidthChangesDoNotTouchTheMode()
        {
            _session.Style = _session.Style.WithDash(LineStyle.Dotted);
            Click(Find("Medium"));
            _session.Style = _session.Style.WithColor(Palette.Red);
            Assert.AreEqual(LineStyle.Dotted, _session.Style.Dash, "width/colour keep the line mode");
        }
    }
}
