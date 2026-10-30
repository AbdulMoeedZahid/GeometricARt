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
    /// <summary>VERIFIER: ToolBar (right edge, Line/Circle, highlight, hidden until placed) and ToastView (shows toasts, never blocks touches).</summary>
    public sealed class VerifierToolBarToastTests
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
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            if (_esGo != null) { EsEnable(_esGo.GetComponent<EventSystem>(), false); UnityEngine.Object.DestroyImmediate(_esGo); }
            ServiceHub.Clear();
        }

        static void Call(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        T Add<T>() where T : MonoBehaviour
        {
            var c = _go.AddComponent<T>();
            Call(c, "Awake");
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

        [Test]
        public void ToolBarHiddenUntilPlacedThenSwitchesTools()
        {
            Add<ToolBar>();
            var canvas = _go.GetComponentInChildren<Canvas>(true);
            Assert.IsFalse(canvas.enabled, "hidden while the board is not placed");
            _space.PlaceAtReticle();
            Assert.IsTrue(canvas.enabled, "shown once placed");
            Assert.AreEqual(105, canvas.sortingOrder);

            Assert.AreEqual(ToolId.Line, _session.ActiveTool, "default Line");
            Color lineIdle = Find("Line").GetComponent<Image>().color;
            Color circleIdle = Find("Circle").GetComponent<Image>().color;
            Assert.AreNotEqual(lineIdle, circleIdle, "active tool is highlighted");

            Click(Find("Circle"));
            Assert.AreEqual(ToolId.Circle, _session.ActiveTool, "Circle button selects Circle");
            Assert.AreEqual(lineIdle, Find("Circle").GetComponent<Image>().color, "Circle now highlighted");
            Click(Find("Line"));
            Assert.AreEqual(ToolId.Line, _session.ActiveTool);

            Assert.IsTrue(Find("Line").GetComponent<Graphic>().raycastTarget, "buttons are raycast targets");
            Assert.IsTrue(Find("Circle").GetComponent<Graphic>().raycastTarget);

            _space.Reset();
            Assert.IsFalse(canvas.enabled, "hidden again after re-place");
        }

        [Test]
        public void ToolBarOnRightEdge()
        {
            Add<ToolBar>();
            _space.PlaceAtReticle();
            Canvas.ForceUpdateCanvases();
            var canvas = _go.GetComponentInChildren<Canvas>(true);
            Vector3[] cc = new Vector3[4], bc = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(cc);
            ((RectTransform)Find("Line").transform).GetWorldCorners(bc);
            float cw = cc[2].x - cc[0].x, ch = cc[2].y - cc[0].y;
            Assert.Greater((bc[0].x - cc[0].x) / cw, 0.75f, "tool buttons are on the right quarter");
            float y = ((bc[0].y + bc[2].y) * 0.5f - cc[0].y) / ch;
            Assert.That(y, Is.InRange(0.2f, 0.8f), "vertically clear of the bottom placement row and the top coach text");
        }

        [Test]
        public void ToastViewShowsSessionToastWithoutBlocking()
        {
            Add<ToastView>();
            Assert.IsNull(_go.GetComponentInChildren<GraphicRaycaster>(true), "toast canvas never receives touches");
            _session.DeleteBottomLayer();   // only layer -> engine toasts
            bool shown = false;
            foreach (var t in _go.GetComponentsInChildren<Text>(false))
                if (!string.IsNullOrEmpty(t.text)) shown = true;
            Assert.IsTrue(shown, "toast text visible after a session toast");
            foreach (var g in _go.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(g.raycastTarget, $"{g.name} must not be a raycast target");
        }
    }
}
