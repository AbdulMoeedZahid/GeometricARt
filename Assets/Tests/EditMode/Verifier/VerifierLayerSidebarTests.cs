using System.Collections.Generic;
using System.Reflection;
using GeoART.App;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EntityId = GeoART.Contracts.EntityId;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// VERIFIER: drives the real LayerSidebar with EventSystem pointer events (ExecuteEvents) against the real
    /// EditorSession: tap selects, double tap toggles visibility, hold opens the opacity popup (slider live), vertical
    /// drag reorders, + adds on top, - deletes the bottom. Edit Mode does not tick MonoBehaviours, so Awake/Start/Update
    /// are invoked by reflection; elapsed time is simulated by ageing the sidebar's own press/tap timestamps
    /// (Time.unscaledTime does not advance inside a synchronous test).
    /// </summary>
    public sealed class VerifierLayerSidebarTests
    {
        EditorSession _session;
        GameObject _esGo, _go;
        LayerSidebar _bar;
        EventSystem _es;
        readonly List<string> _toasts = new List<string>();

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession();
            _session.AddLayer();
            _session.AddLayer();   // 3 layers, top active
            _session.Toast += m => _toasts.Add(m);
            ServiceHub.Register<IEditorSession>(_session);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
            _go = new GameObject("App");
            _bar = _go.AddComponent<LayerSidebar>();
            Call(_bar, "Awake");
            Call(_bar, "Start");
        }

        /// <summary>Edit Mode does not run EventSystem.OnEnable/OnDisable, which (un)register it as EventSystem.current.</summary>
        static void EsEnable(EventSystem es, bool on)
        {
            if (es == null) return;
            var m = typeof(EventSystem).GetMethod(on ? "OnEnable" : "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(es, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_esGo != null) { EsEnable(_esGo.GetComponent<EventSystem>(), false); Object.DestroyImmediate(_esGo); }
            ServiceHub.Clear();
            _toasts.Clear();
        }

        // ---------- helpers ----------

        static object Call(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m, $"{target.GetType().Name}.{method} not found");
            return m.Invoke(target, null);
        }

        FieldInfo Field(string name)
        {
            var f = typeof(LayerSidebar).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f, $"LayerSidebar.{name} not found (test needs it to simulate time / read layout)");
            return f;
        }

        float GetF(string name) => (float)Field(name).GetValue(_bar);

        /// <summary>Simulates <paramref name="seconds"/> passing for the press timer and the double-tap window.</summary>
        void Age(float seconds)
        {
            Field("_pressTime").SetValue(_bar, GetF("_pressTime") - seconds);
            Field("_lastTapUp").SetValue(_bar, GetF("_lastTapUp") - seconds);
            Call(_bar, "Update");
        }

        /// <summary>Active dash rows, top of the sidebar first.</summary>
        List<GameObject> Rows()
        {
            Call(_bar, "SnapRows");
            var rows = new List<GameObject>();
            foreach (var rt in _go.GetComponentsInChildren<RectTransform>(false))
                if (rt.name.StartsWith("Layer row")) rows.Add(rt.gameObject);
            rows.Sort((a, b) => ((RectTransform)b.transform).anchoredPosition.y
                .CompareTo(((RectTransform)a.transform).anchoredPosition.y));
            return rows;
        }

        GameObject Find(string contains)
        {
            foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains(contains)) return t.gameObject;
            Assert.Fail($"no child named *{contains}*");
            return null;
        }

        PointerEventData Ped(Vector2 pos, int id = 0) =>
            new PointerEventData(_es) { position = pos, pressPosition = pos, pointerId = id, button = PointerEventData.InputButton.Left };

        static Vector2 Center(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            Vector3[] c = new Vector3[4];
            rt.GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;
        }

        PointerEventData Down(GameObject go, int id = 0)
        {
            var e = Ped(Center(go), id);
            e.pointerPress = go;
            ExecuteEvents.Execute(go, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, e, ExecuteEvents.initializePotentialDrag);
            return e;
        }

        void Up(GameObject go, PointerEventData e) => ExecuteEvents.Execute(go, e, ExecuteEvents.pointerUpHandler);

        void Tap(GameObject go) { var e = Down(go); Up(go, e); }

        void DragBy(GameObject go, PointerEventData e, float dyPixels, int steps = 6)
        {
            Vector2 start = e.position;
            ExecuteEvents.Execute(go, e, ExecuteEvents.beginDragHandler);
            for (int i = 1; i <= steps; i++)
            {
                e.position = start + new Vector2(0f, dyPixels * i / steps);
                ExecuteEvents.Execute(go, e, ExecuteEvents.dragHandler);
                Call(_bar, "Update");
            }
        }

        float RowPixels => GetF("_rowH") * GetF("_scale");

        void Click(GameObject button) =>
            ExecuteEvents.Execute(button, Ped(Center(button)), ExecuteEvents.pointerClickHandler);

        Layer L(int index) => _session.Board.Layers[index];

        // ---------- structure ----------

        [Test]
        public void OneDashPerLayerTopLayerOnTop()
        {
            var rows = Rows();
            Assert.AreEqual(3, rows.Count, "one dash row per layer");
            Tap(rows[0]);
            Assert.AreEqual(L(2).Id, _session.Board.ActiveLayerId, "visually top dash = Layers[Count-1] (top)");
            Tap(Rows()[2]);
            Assert.AreEqual(L(0).Id, _session.Board.ActiveLayerId, "visually bottom dash = Layers[0] (bottom)");
        }

        [Test]
        public void SidebarIsMiddleLeftWithPlusTopMinusBottom()
        {
            var canvas = _go.GetComponentInChildren<Canvas>();
            Assert.AreEqual(110, canvas.sortingOrder, "sortingOrder 110");
            Assert.NotNull(canvas.GetComponent<GraphicRaycaster>(), "sidebar canvas has a GraphicRaycaster");
            Canvas.ForceUpdateCanvases();
            var cr = (RectTransform)canvas.transform;
            Vector3[] cc = new Vector3[4];
            cr.GetWorldCorners(cc);
            float cw = cc[2].x - cc[0].x, ch = cc[2].y - cc[0].y;
            Vector2 plus = Center(Find("Add layer")), minus = Center(Find("Delete bottom")), row = Center(Rows()[1]);
            Assert.Less((row.x - cc[0].x) / cw, 0.2f, "sidebar hugs the left edge");
            float vy = (row.y - cc[0].y) / ch;
            Assert.That(vy, Is.InRange(0.35f, 0.65f), "sidebar vertically centred");
            Assert.Greater(plus.y, row.y, "+ above the dashes");
            Assert.Less(minus.y, row.y, "- below the dashes");
            foreach (var r in Rows())
                Assert.IsTrue(r.GetComponent<Graphic>() != null && r.GetComponent<Graphic>().raycastTarget, "dash rows are raycast targets");
            Assert.IsTrue(Find("Add layer").GetComponent<Graphic>().raycastTarget);
            Assert.IsTrue(Find("Delete bottom").GetComponent<Graphic>().raycastTarget);
        }

        // ---------- tap / double tap ----------

        [Test]
        public void TapSelectsImmediately()
        {
            Tap(Rows()[2]);
            Assert.AreEqual(L(0).Id, _session.Board.ActiveLayerId, "selected on the tap itself, no double-tap wait");
            Assert.IsTrue(L(0).Visible, "single tap does not hide");
        }

        [Test]
        public void DoubleTapTogglesVisibility()
        {
            GameObject mid = Rows()[1];
            EntityId id = L(1).Id;
            Tap(mid);
            Age(0.1f);
            Tap(mid);
            Assert.IsFalse(_session.Board.FindLayer(id).Visible, "double tap hides");
            Assert.AreEqual(id, _session.Board.ActiveLayerId, "double-tapped layer is selected");
            Age(1f);
            Tap(mid);
            Age(0.1f);
            Tap(mid);
            Assert.IsTrue(_session.Board.FindLayer(id).Visible, "double tap again shows");
        }

        [Test]
        public void SlowSecondTapDoesNotToggle()
        {
            GameObject mid = Rows()[1];
            Tap(mid);
            Age(0.5f);
            Tap(mid);
            Assert.IsTrue(L(1).Visible, "taps 0.5 s apart are two taps, not a double tap");
        }

        [Test]
        public void DoubleTapOnDifferentDashesDoesNotToggle()
        {
            var rows = Rows();
            Tap(rows[0]);
            Tap(rows[1]);
            foreach (var l in _session.Board.Layers) Assert.IsTrue(l.Visible, "taps on two different dashes never toggle");
            Assert.AreEqual(L(1).Id, _session.Board.ActiveLayerId);
        }

        [Test]
        public void HiddenStateShownOnDash()
        {
            _session.SetLayerVisible(L(0).Id, false);   // from outside: UI must follow BoardChanged
            GameObject bottom = Rows()[2];
            Transform hole = null;
            foreach (var t in bottom.GetComponentsInChildren<Transform>(true)) if (t.name == "Hole") hole = t;
            Assert.NotNull(hole, "dash has a hidden-state marker");
            Assert.IsTrue(hole.gameObject.activeSelf, "hidden layer's dash shows the hidden marker");
        }

        // ---------- hold / opacity ----------

        [Test]
        public void HoldOpensOpacityPopupAndSliderIsLive()
        {
            GameObject bottom = Rows()[2];
            EntityId id = L(0).Id;
            var e = Down(bottom);
            Age(0.2f);
            Assert.IsFalse(Find("OpacityPopup").activeSelf, "no popup before the hold time");
            Age(0.3f);
            Assert.IsTrue(Find("OpacityPopup").activeSelf, "popup after ~0.45 s hold");
            Assert.AreEqual(id, _session.Board.ActiveLayerId, "hold selects the layer");
            Up(bottom, e);
            Assert.IsTrue(Find("OpacityPopup").activeSelf, "popup stays open after release");
            Assert.IsTrue(_session.Board.FindLayer(id).Visible, "hold + release does not toggle visibility");

            var slider = Find("OpacityPopup").GetComponentInChildren<Slider>(true);
            Assert.NotNull(slider);
            Assert.AreEqual(0f, slider.minValue); Assert.AreEqual(1f, slider.maxValue);
            Assert.AreEqual(1f, slider.value, 1e-4, "slider starts at the layer's opacity");
            slider.value = 0.3f;
            Assert.AreEqual(0.3, _session.Board.FindLayer(id).Opacity, 1e-4, "slider applied live");
            slider.value = 0f;
            Assert.AreEqual(0.0, _session.Board.FindLayer(id).Opacity, 1e-4, "0%");
            Assert.IsTrue(Find("OpacityPopup").activeSelf, "popup still open while sliding");
            bool percentShown = false;
            foreach (var t in Find("OpacityPopup").GetComponentsInChildren<Text>(true)) if (t.text == "0%") percentShown = true;
            Assert.IsTrue(percentShown, "percentage label follows the slider");

            GameObject blocker = Find("PopupBlocker");
            var be = Down(blocker);
            Up(blocker, be);
            Assert.IsFalse(Find("OpacityPopup").activeSelf, "touch outside closes the popup");
        }

        [Test]
        public void PopupSyncsToHeldLayerOpacity()
        {
            _session.SetLayerOpacity(L(1).Id, 0.25);
            var row = Rows()[1];
            var e = Down(row);
            Age(0.6f);
            Up(row, e);
            var slider = Find("OpacityPopup").GetComponentInChildren<Slider>(true);
            Assert.AreEqual(0.25f, slider.value, 1e-3, "slider shows the held layer's current opacity");
        }

        // ---------- drag reorder ----------

        [Test]
        public void DragUpReordersOnRelease()
        {
            GameObject bottom = Rows()[2];
            EntityId id = L(0).Id;
            var e = Down(bottom);
            DragBy(bottom, e, 2f * RowPixels + 2f);
            Assert.AreEqual(id, L(0).Id, "order unchanged until release");
            Up(bottom, e);
            Assert.AreEqual(id, L(2).Id, "bottom layer dragged two slots up is now the top layer");
            Assert.AreEqual(3, _session.Board.Layers.Count);
            foreach (var l in _session.Board.Layers) Assert.IsTrue(l.Visible, "drag never toggles visibility");
            Assert.IsFalse(Find("OpacityPopup").activeSelf, "drag never opens the popup");
        }

        [Test]
        public void DragOneSlotDown()
        {
            GameObject top = Rows()[0];
            EntityId id = L(2).Id;
            var e = Down(top);
            DragBy(top, e, -1f * RowPixels - 2f);
            Up(top, e);
            Assert.AreEqual(id, L(1).Id, "top layer dragged one slot down is now in the middle");
        }

        [Test]
        public void DragFarBeyondEndsClamps()
        {
            GameObject top = Rows()[0];
            EntityId id = L(2).Id;
            var e = Down(top);
            DragBy(top, e, -50f * RowPixels);
            Up(top, e);
            Assert.AreEqual(id, L(0).Id, "dragged far below -> bottom");
        }

        [Test]
        public void SmallJitterIsStillATap()
        {
            GameObject bottom = Rows()[2];
            var e = Down(bottom);
            DragBy(bottom, e, 0.1f * RowPixels, 2);   // a few pixels of finger jitter
            Up(bottom, e);
            Assert.AreEqual(L(0).Id, _session.Board.ActiveLayerId, "jittery tap still selects");
        }

        [Test]
        public void DragThenHoldTimeDoesNotOpenPopup()
        {
            GameObject bottom = Rows()[2];
            var e = Down(bottom);
            DragBy(bottom, e, 1f * RowPixels + 2f);
            Age(1f);
            Assert.IsFalse(Find("OpacityPopup").activeSelf, "a drag in progress never turns into a hold");
            Up(bottom, e);
        }

        [Test]
        public void SecondPointerIgnoredWhilePressed()
        {
            var rows = Rows();
            var e = Down(rows[2], 0);
            var e2 = Down(rows[0], 1);
            Up(rows[0], e2);
            Up(rows[2], e);
            Assert.AreEqual(L(0).Id, _session.Board.ActiveLayerId, "first finger's tap wins, second ignored");
            // and the sidebar is not stuck afterwards
            Tap(Rows()[1]);
            Assert.AreEqual(L(1).Id, _session.Board.ActiveLayerId, "sidebar still responds");
        }

        [Test]
        public void LostPointerUpDoesNotWedgeSidebar()
        {
            var rows = Rows();
            Down(rows[2], 5);                      // its PointerUp never arrives
            var e = Down(rows[0], 5);              // same pointer id again: the old press was stale
            Up(rows[0], e);
            Assert.AreEqual(L(2).Id, _session.Board.ActiveLayerId, "new press with the same pointer id replaces the lost one");

            Down(Rows()[2], 7);                    // lost again
            var f = Field("_pressActivity");
            f.SetValue(_bar, (float)f.GetValue(_bar) - 2f);   // 2 s of silence
            var g = Down(Rows()[1], 8);
            Up(Rows()[1], g);
            Assert.AreEqual(L(1).Id, _session.Board.ActiveLayerId, "a different pointer after a quiet spell also recovers");
        }

        // ---------- + / - ----------

        [Test]
        public void PlusAddsLayerOnTop()
        {
            Click(Find("Add layer"));
            Assert.AreEqual(4, _session.Board.Layers.Count);
            Assert.AreEqual(L(3).Id, _session.Board.ActiveLayerId, "new top layer is active");
            Assert.AreEqual(4, Rows().Count, "a dash appears for it");
            Tap(Rows()[0]);
            Assert.AreEqual(L(3).Id, _session.Board.ActiveLayerId, "new dash is at the top");
        }

        [Test]
        public void PlusAtCapToasts()
        {
            for (int i = 0; i < 20; i++) Click(Find("Add layer"));
            Assert.AreEqual(12, _session.Board.Layers.Count);
            Assert.AreEqual(12, Rows().Count);
            Assert.Greater(_toasts.Count, 0, "toast at the cap");
        }

        [Test]
        public void MinusDeletesBottomLayer()
        {
            EntityId bottom = L(0).Id, mid = L(1).Id;
            Click(Find("Delete bottom"));
            Assert.AreEqual(2, _session.Board.Layers.Count);
            Assert.IsNull(_session.Board.FindLayer(bottom), "bottom-most layer deleted");
            Assert.AreEqual(mid, L(0).Id);
            Assert.AreEqual(2, Rows().Count, "its dash is gone");
            Click(Find("Delete bottom"));
            Click(Find("Delete bottom"));
            Assert.AreEqual(1, _session.Board.Layers.Count, "never deletes the last layer");
            Assert.Greater(_toasts.Count, 0, "toast when refusing");
            Assert.AreEqual(1, Rows().Count);
        }

        [Test]
        public void ExternalChangesRefreshRows()
        {
            _session.AddLayer();
            Assert.AreEqual(4, Rows().Count, "AddLayer from elsewhere adds a dash");
            _session.LoadBoard(GoldenBoards.Empty());
            Assert.AreEqual(_session.Board.Layers.Count, Rows().Count, "LoadBoard resyncs the dashes");
            Tap(Rows()[0]);
            Assert.AreEqual(_session.Board.Layers[_session.Board.Layers.Count - 1].Id, _session.Board.ActiveLayerId);
        }

        [Test]
        public void DeleteWhilePressedDoesNotWedge()
        {
            GameObject bottom = Rows()[2];
            var e = Down(bottom);
            _session.DeleteBottomLayer();   // the pressed dash's layer disappears
            Up(bottom, e);
            Tap(Rows()[0]);
            Assert.AreEqual(L(1).Id, _session.Board.ActiveLayerId, "sidebar still works after its pressed layer was deleted");
        }
    }
}
