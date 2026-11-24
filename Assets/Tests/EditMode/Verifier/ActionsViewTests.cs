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
    /// Optional extras P7 and P8: the action bar's Delete button and the Redo button next to Undo. Edit Mode does not
    /// tick MonoBehaviours, so Awake/Start are invoked by reflection.
    /// </summary>
    public sealed class ActionsViewTests
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
            _session = new EditorSession();
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(_es, null);
            _appGo = new GameObject("App");
            var tb = _appGo.AddComponent<ToolBar>();
            Call(tb, "Awake");
            Call(tb, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            if (_appGo != null) Object.DestroyImmediate(_appGo);
            if (_esGo != null)
            {
                typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(_es, null);
                Object.DestroyImmediate(_esGo);
            }
            ServiceHub.Clear();
        }

        static void Call(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, null);

        GameObject Find(string name)
        {
            foreach (var t in _appGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            Assert.Fail($"no child named {name}");
            return null;
        }

        void Click(string name) =>
            ExecuteEvents.Execute(Find(name), new PointerEventData(_es) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);

        static Rect WorldRect(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        LineShape AddLine()
        {
            var l = new LineShape(_session.Board.NewId(), new Vec2(0.2, 0.2), new Vec2(0.4, 0.4), StrokeStyle.Default);
            _session.ActiveLayer.Shapes.Add(l);
            return l;
        }

        [Test]
        public void DeleteButtonRemovesTheSelection()
        {
            _space.PlaceAtReticle();
            var line = AddLine();
            _session.SetTool(ToolId.SelectMove);
            _session.SetSelection(new[] { line.Id });
            Assert.IsTrue(Find("Delete").activeInHierarchy, "Delete is in the action bar");
            Click("Delete");
            Assert.AreEqual(0, _session.ActiveLayer.Shapes.Count, "deleted");
            Assert.AreEqual("Delete", _session.NextUndoLabel);
            Assert.IsFalse(Find("Move").transform.parent.gameObject.activeSelf, "the bar hides with the selection");
        }

        [Test]
        public void RedoSitsRightOfUndoAndRedoes()
        {
            var redo = Find("Redo");
            Assert.IsFalse(redo.activeInHierarchy && redo.GetComponentInParent<Canvas>().enabled, "hidden until placed");
            _space.PlaceAtReticle();
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(redo.activeSelf, "shown once placed");
            Assert.Less(redo.GetComponent<CanvasGroup>().alpha, 1f, "dimmed: nothing to redo");
            Rect u = WorldRect(Find("Undo")), r = WorldRect(redo);
            Assert.Greater(r.xMin, u.xMax, "right of Undo, no overlap");
            Assert.AreEqual(u.yMax, r.yMax, 0.5f, "on the same row");
            Assert.AreEqual(u.width, r.width, 0.5f, "same size");
            Assert.AreNotSame(Find("Undo").transform.Find("Icon").GetComponent<RawImage>().texture,
                redo.transform.Find("Icon").GetComponent<RawImage>().texture, "its own glyph");

            _session.Pointer(new PointerEvent(PointerPhase.Down, new Vec2(0.2, 0.2), true, 0));
            _session.Pointer(new PointerEvent(PointerPhase.Up, new Vec2(0.5, 0.6), true, 0));
            Assert.AreEqual(1, _session.ActiveLayer.Shapes.Count, "precondition: a line");
            Click("Undo");
            Assert.AreEqual(0, _session.ActiveLayer.Shapes.Count);
            Assert.AreEqual(1f, redo.GetComponent<CanvasGroup>().alpha, "Redo lights up");
            Click("Redo");
            Assert.AreEqual(1, _session.ActiveLayer.Shapes.Count, "Redo brings the line back");
            Assert.Less(redo.GetComponent<CanvasGroup>().alpha, 1f, "dimmed again");
        }
    }
}
