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
using Object = UnityEngine.Object;
using EntityId = GeoART.Contracts.EntityId;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// VERIFIER (Extend action, view side, written before the builder's code). API GUESS: the bar button's GameObject is
    /// named "Extend" (CreateBarButton names buttons by their label) and sits with Move/Scale/Duplicate/Delete/Deselect
    /// under one bar parent; ToolBar.ActionBarWidth stays public. Checks: six bar buttons inside the screen and left of
    /// the tool column; a click extends, keeps the selection and the bar, one step; by the layout formulas the bar's
    /// buttons stay at least 44 ref px wide on the 14 Pro Max, 14, 13 mini and SE.
    /// </summary>
    public sealed class VerifierExtendViewTests
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
        LineShape _line;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            var b = GoldenBoards.Empty();
            var l = b.Layers[0];
            _line = new LineShape(b.NewId(), new Vec2(0.4, 0.5), new Vec2(0.6, 0.5), StrokeStyle.Default);
            l.Shapes.Add(_line);
            l.Shapes.Add(new LineShape(b.NewId(), new Vec2(0.2, 0.1), new Vec2(0.2, 0.9), StrokeStyle.Default));
            l.Shapes.Add(new LineShape(b.NewId(), new Vec2(0.85, 0.1), new Vec2(0.85, 0.9), StrokeStyle.Default));
            _session = new EditorSession(b);
            _space = new Space();
            ServiceHub.Register<IEditorSession>(_session);
            ServiceHub.Register<IBoardSpace>(_space);
            _esGo = new GameObject("EventSystem", typeof(EventSystem));
            _es = _esGo.GetComponent<EventSystem>();
            EsEnable(_es, true);
            _appGo = new GameObject("App");
            var tb = _appGo.AddComponent<ToolBar>();
            Call(tb, "Awake");
            Call(tb, "Start");
            _space.PlaceAtReticle();
            _session.SetTool(ToolId.SelectMove);
            _session.SetSelection(new[] { _line.Id });
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

        void Click(GameObject go) =>
            ExecuteEvents.Execute(go, new PointerEventData(_es) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);

        static Rect ScreenRect(GameObject go)
        {
            var c = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        [Test]
        public void SixBarButtonsIncludingExtendFitLeftOfTheColumn()
        {
            var extend = Find("Extend");
            Assert.IsTrue(extend.activeInHierarchy, "Extend shows with a selection in Select");
            var bar = extend.transform.parent;
            var names = new List<string>();
            foreach (Transform t in bar) if (t.GetComponent<Button>() != null && t.gameObject.activeSelf) names.Add(t.name);
            Assert.AreEqual(6, names.Count, "6 bar buttons: " + string.Join(",", names));
            foreach (var n in new[] { "Move", "Scale", "Duplicate", "Delete", "Deselect", "Extend" })
                CollectionAssert.Contains(names, n, $"{n} in the bar");
            Rect column = ScreenRect(Find("Column"));
            var canvas = _appGo.GetComponentInChildren<Canvas>(true);
            Rect screen = ScreenRect(canvas.gameObject);
            Rect prev = default;
            bool first = true;
            foreach (Transform t in bar)
            {
                if (t.GetComponent<Button>() == null) continue;
                Rect r = ScreenRect(t.gameObject);
                Assert.LessOrEqual(r.xMax, column.xMin + 0.5f, $"{t.name} left of the tool column");
                Assert.GreaterOrEqual(r.xMin, screen.xMin - 0.5f, $"{t.name} on screen");
                if (!first) Assert.IsFalse(r.Overlaps(prev), $"{t.name} does not overlap its neighbour");
                prev = r;
                first = false;
            }
        }

        [Test]
        public void ExtendClickExtendsInOneStepAndTheBarStays()
        {
            int steps = _session.UndoCount;
            Click(Find("Extend"));
            Assert.IsTrue(Vec2.Distance(_line.A, new Vec2(0.2, 0.5)) < 1e-12 && Vec2.Distance(_line.B, new Vec2(0.85, 0.5)) < 1e-12,
                $"extended to both walls, got {_line.A} {_line.B}");
            Assert.AreEqual(steps + 1, _session.UndoCount, "one step");
            Assert.AreEqual("Extend", _session.NextUndoLabel);
            Assert.AreEqual(1, _session.SelectionCount, "selection kept");
            Assert.IsTrue(Find("Extend").activeInHierarchy && Find("Move").activeInHierarchy, "the bar stays");
            Click(Find("Extend"));
            Assert.AreEqual(steps + 1, _session.UndoCount, "nothing more in the way: no step");
        }

        [Test]
        public void BarLabelsFitTheirButtons()
        {
            Canvas.ForceUpdateCanvases();
            var bar = Find("Extend").transform.parent;
            foreach (Transform t in bar)
            {
                if (t.GetComponent<Button>() == null) continue;
                var label = t.GetComponentInChildren<Text>();
                if (label == null) continue;
                float w = ((RectTransform)label.transform).rect.width;
                var settings = label.GetGenerationSettings(new Vector2(float.MaxValue, float.MaxValue));
                float needed = label.cachedTextGeneratorForLayout.GetPreferredWidth(label.text, settings) / label.pixelsPerUnit;
                Assert.LessOrEqual(needed, w + 0.5f, $"'{label.text}' needs {needed:F1} ref px in a {w:F1} px label (clipped or wrapped)");
            }
        }

        [Test]
        public void BarButtonsStayAtLeast44RefPxWideOnPhones()
        {
            // ToolBar.PlaceActionBar: span = safe width - EdgeMargin - (EdgeMargin + 56 x columnFit + EdgeMargin), the
            // bar shrinks to fit. The 8-button column has fit 1 on these phones (490 ref px tall).
            var phones = new (string name, float w, float h)[]
            {
                ("14 Pro Max", 1290, 2796), ("14", 1170, 2532), ("13 mini", 1080, 2340), ("SE", 750, 1334),
            };
            foreach (var p in phones)
            {
                float scale = ScreenSnapMetric.ScaleFactor(p.w, p.h);
                float refW = p.w / scale;
                float span = refW - 8f - (8f + 56f + 8f);
                float fit = Mathf.Min(1f, span / ToolBar.ActionBarWidth);
                float perButton = ToolBar.ActionBarWidth * fit / 6f;   // includes its share of the spacing: lenient
                Assert.GreaterOrEqual(perButton, 44f, $"{p.name}: ~{perButton:F1} ref px per bar button (bar {ToolBar.ActionBarWidth}, fit {fit:F2})");
            }
        }
    }
}
