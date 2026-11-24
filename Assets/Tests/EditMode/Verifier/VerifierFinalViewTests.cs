using System;
using System.Reflection;
using GeoART.App;
using GeoART.App.Save;
using GeoART.BoardRender;
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
    /// VERIFIER (final handover: P7 Delete, P8 Redo, Fill switched off): the Redo button right of Undo, dimmed exactly
    /// when there is nothing to redo; the Delete bar button deletes the selection in one step and the bar hides; Fill is
    /// really gone (no button, "No fill" never shows, even with the Fill tool set in code); a board saved with fills loads,
    /// draws its fills and re-saves them.
    /// </summary>
    public sealed class VerifierFinalViewTests
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
            Canvas.ForceUpdateCanvases();
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

        GameObject Find(string name, bool required = true)
        {
            foreach (var t in _appGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            if (required) Assert.Fail($"no child named {name}");
            return null;
        }

        void Click(GameObject go) =>
            ExecuteEvents.Execute(go, new PointerEventData(_es) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);

        static Rect ScreenRect(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        float Alpha(string name) => Find(name).GetComponent<CanvasGroup>().alpha;

        // ---------- Redo ----------

        [Test]
        public void RedoSitsRightOfUndoAndDimsExactlyWhenNothingToRedo()
        {
            Canvas.ForceUpdateCanvases();
            Rect undo = ScreenRect(Find("Undo")), redo = ScreenRect(Find("Redo"));
            Assert.IsTrue(Find("Redo").activeInHierarchy, "Redo shown while placed");
            Assert.AreEqual(undo.yMin, redo.yMin, 0.5f, "same row as Undo");
            Assert.AreEqual(undo.height, redo.height, 0.5f, "same size");
            Assert.Greater(redo.xMin, undo.xMax, "right of Undo, not overlapping");
            Assert.Less(redo.xMin - undo.xMax, undo.width * 0.5f, "next to it");
            Assert.Less(redo.xMax, ScreenRect(Find("Column")).xMin, "clear of the tool column");
            Assert.Less(Alpha("Redo"), 1f, "nothing to redo: dimmed");
            Assert.IsTrue(Find("Redo").GetComponent<Graphic>().raycastTarget, "still a touch target (toasts when empty)");

            _session.SetGrid(3);
            Assert.Less(Alpha("Redo"), 1f, "an edit: still nothing to redo");
            Assert.AreEqual(1f, Alpha("Undo"));
            Click(Find("Undo"));
            Assert.AreEqual(1f, Alpha("Redo"), "after Undo: Redo bright");
            Click(Find("Redo"));
            Assert.AreEqual(3, _session.Board.GridN, "Redo button re-applies");
            Assert.Less(Alpha("Redo"), 1f, "redone everything: dimmed again");
            Click(Find("Undo"));
            _session.SetGridKind(GridKind.Hex);     // a new edit after Undo
            Assert.Less(Alpha("Redo"), 1f, "a new edit clears Redo: dimmed");
            _space.Reset();
            Assert.IsFalse(Find("Redo").activeInHierarchy, "hidden with the toolbar when not placed");
        }

        // ---------- Delete ----------

        [Test]
        public void DeleteButtonDeletesTheSelectionInOneStepAndTheBarHides()
        {
            var star = _session.ActiveLayer;
            _session.SetTool(ToolId.SelectMove);
            _session.SetSelection(new[] { star.Shapes[0].Id, star.Shapes[5].Id });
            Assert.IsTrue(Find("Delete").activeInHierarchy, "the bar (with Delete) shows with a selection");
            int steps = _session.UndoCount;
            Click(Find("Delete"));
            Assert.AreEqual(6, star.Shapes.Count, "two shapes deleted");
            Assert.AreEqual(steps + 1, _session.UndoCount, "one step");
            Assert.AreEqual("Delete", _session.NextUndoLabel);
            Assert.IsFalse(Find("Delete").activeInHierarchy, "the bar hides with an empty selection");
            var bar = Find("Delete").transform.parent;
            foreach (Transform b in bar)
                if (b.GetComponent<Button>() != null)
                    Assert.LessOrEqual(ScreenRect(b.gameObject).xMax, ScreenRect(Find("Column")).xMin + 0.5f, $"{b.name} left of the column");
        }

        // ---------- Fill is gone ----------

        [Test]
        public void FillIsReallyGone()
        {
            Assert.IsFalse(ToolBar.FillEnabled, "switch off");
            Assert.IsNull(Find("Fill", false), "no Fill button");
            var column = Find("Column").transform;
            int n = 0;
            foreach (Transform c in column) if (c.GetComponent<Button>() != null) n++;
            Assert.AreEqual(8, n, "8 column buttons");
            _session.SetTool(ToolId.Fill);         // reachable only from code: the colour bar must still not offer No fill
            Assert.AreEqual(ToolId.Fill, _session.ActiveTool, "precondition: the engine accepted Fill");
            GameObject noFill = Find("No fill", false);
            Assert.IsTrue(noFill == null || !noFill.activeInHierarchy, "No fill never shows");
            Assert.IsTrue(Find("Board").activeInHierarchy, "the Board button stays");
            _session.SetFillErase(true);
            Assert.IsTrue(noFill == null || !noFill.activeInHierarchy, "not even with erase on");
            Assert.IsTrue(Find("Board").activeInHierarchy);
        }

        // ---------- saved fills ----------

        [Test]
        public void ASavedBoardWithFillsLoadsDrawsAndResaves()
        {
            var b = GoldenBoards.StarOnFullBoard();
            var star = b.FindLayer(b.ActiveLayerId);
            star.Fills.Add(new FillSeed(GoldenBoards.BoardCenter, Palette.Red));   // the centre octagon
            string json = DesignSerializer.ToJson(b, "with fills", DateTime.UtcNow);
            Assert.IsTrue(DesignSerializer.TryParse(json, out DesignFile f, out string err), err);
            Board loaded = DesignSerializer.ToBoard(f.board);
            Assert.AreEqual(1, loaded.FindLayer(loaded.ActiveLayerId).Fills.Count, "the fill loads");

            int Vertices(Board board)
            {
                var s = new EditorSession(board);
                if (_viewGo != null) Object.DestroyImmediate(_viewGo);
                _viewGo = new GameObject("FillView");
                var view = _viewGo.AddComponent<BoardView>();
                Call(view, "Awake");
                view.Bind(s);
                Call(view, "LateUpdate");
                return _viewGo.transform.Find("Shapes").GetComponent<MeshFilter>().sharedMesh.vertexCount;
            }
            int with = Vertices(loaded);
            var plain = DesignSerializer.ToBoard(f.board);
            plain.FindLayer(plain.ActiveLayerId).Fills.Clear();
            int without = Vertices(plain);
            Assert.Greater(with, without, "the fill is drawn (more Shapes vertices) with Fill switched off");

            var session = new EditorSession(loaded);
            session.SetGrid(3);                         // an unrelated edit, then save again
            string again = DesignSerializer.ToJson(session.Board, "with fills", DateTime.UtcNow);
            Assert.IsTrue(DesignSerializer.TryParse(again, out DesignFile f2, out err), err);
            var back = DesignSerializer.ToBoard(f2.board).FindLayer(loaded.ActiveLayerId).Fills;
            Assert.AreEqual(1, back.Count, "re-saved without losing the fill");
            Assert.IsTrue(back[0].Point == GoldenBoards.BoardCenter && back[0].Color.Equals(Palette.Red), "exact seed");
        }
    }
}
