using System.Reflection;
using GeoART.App;
using GeoART.BoardRender;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// Snap feedback (ui-line-plan 4.3-4.4): BoardView draws the snapped dot as a filled disc and pops the hover ring from
    /// 1.6x to 1x over 120 ms on each new acquisition (fake clock), rebuilding the overlay only while the pop runs;
    /// Haptics ticks at most once per 60 ms; SnapFeedback ticks on SnapAcquired; a hold menu ticks when it opens. Edit Mode
    /// does not tick MonoBehaviours, so Awake/Start/LateUpdate are invoked by reflection.
    /// </summary>
    public sealed class SnapFeedbackViewTests
    {
        GameObject _go;
        BoardView _view;
        EditorSession _session;
        float _now = 10f;
        int _ticks;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _session = new EditorSession();
            _session.SetSnapEnabled(true);
            _session.ActiveLayer.SnapPoints.Add(new SnapPoint(_session.Board.NewId(), new Vec2(0.3, 0.3)));
            _go = new GameObject("SnapFeedbackView");
            _view = _go.AddComponent<BoardView>();
            Call(_view, "Awake");
            _view.Clock = () => _now;
            _view.Bind(_session);
            Haptics.ResetThrottle();
            Haptics.Clock = () => _now;
            Haptics.Sink = () => _ticks++;
        }

        [TearDown]
        public void TearDown()
        {
            Haptics.Sink = null;
            Haptics.Clock = () => Time.unscaledTime;
            Haptics.ResetThrottle();
            if (_go != null)
            {
                foreach (var mf in _go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
                foreach (var mr in _go.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial != null) Object.DestroyImmediate(mr.sharedMaterial);
                Object.DestroyImmediate(_go);
            }
            ServiceHub.Clear();
        }

        static void Call(object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        void Pointer(PointerPhase phase, double x, double y) =>
            _session.Pointer(new PointerEvent(phase, new Vec2(x, y), true, 0));

        Mesh Overlay() => _go.transform.Find("Overlay").GetComponent<MeshFilter>().sharedMesh;

        bool OverlayDirty => (bool)typeof(BoardView).GetField("_overlayDirty", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_view);

        static float Flat(Vector3 v, Vector3 c) => new Vector2(v.x - c.x, v.z - c.z).magnitude;

        /// <summary>
        /// Inner edge of the hover ring around <paramref name="c"/>: the nearest overlay vertex beyond the filled dot
        /// (3 mm) and the ghost line's end (about 1 mm); the cursor ring (9 mm) is always farther than the ring's inner edge.
        /// </summary>
        float RingInner(Vector3 c)
        {
            float min = float.MaxValue;
            foreach (var v in Overlay().vertices)
            {
                float d = Flat(v, c);
                if (d > 0.0035f) min = Mathf.Min(min, d);
            }
            return min;
        }

        Vector3 Local(Vec2 p) => BoardGeometry.BoardToLocal(p, _view.SideMeters, 0f);

        [Test]
        public void SnappedDotIsFilledAndTheRingPopsThenSettles()
        {
            Pointer(PointerPhase.Down, 0.6, 0.6);
            Call(_view, "LateUpdate");
            Pointer(PointerPhase.Move, 0.301, 0.302);   // within the fallback 0.02: snaps onto (0.3, 0.3)
            Assert.AreEqual(new Vec2(0.3, 0.3), _session.Cursor, "precondition: snapped");
            Call(_view, "LateUpdate");
            Vector3 c = Local(new Vec2(0.3, 0.3));
            float popped = RingInner(c);
            bool rim = false;
            foreach (var v in Overlay().vertices)
            {
                float d = Flat(v, c);
                if (d > 0.0025f && d < 0.0032f) rim = true;
            }
            Assert.IsTrue(rim, "a filled 3 mm disc sits on the snapped dot");

            _now += 0.06f;
            Call(_view, "LateUpdate");
            float mid = RingInner(c);
            _now += 0.1f;
            Call(_view, "LateUpdate");
            float settled = RingInner(c);
            float ringRadius = (float)(EditorSession.HoverRingRadius * _view.SideMeters);
            Assert.Greater(popped, ringRadius * 1.4f, "starts about 1.6x");
            Assert.Less(mid, popped, "shrinks");
            Assert.Less(settled, mid, "keeps shrinking");
            Assert.Less(settled, ringRadius * 1.15f, "ends at 1x");

            Assert.IsFalse(OverlayDirty, "rebuilt once more at 1x");
            Call(_view, "LateUpdate");
            Assert.IsFalse(OverlayDirty, "idle: no per-frame rebuild after the pop");

            Pointer(PointerPhase.Move, 0.302, 0.301);   // same dot: no new acquisition, no new pop
            Call(_view, "LateUpdate");
            Assert.Less(RingInner(c), ringRadius * 1.15f, "no pop for the same candidate");
        }

        [Test]
        public void HapticsTickAtMostEvery60Ms()
        {
            Assert.IsTrue(Haptics.Tick(), "first tick plays");
            _now += 0.03f;
            Assert.IsFalse(Haptics.Tick(), "30 ms later: throttled");
            _now += 0.04f;
            Assert.IsTrue(Haptics.Tick(), "70 ms after the first: plays");
            Assert.AreEqual(2, _ticks);
            Haptics.Sink = null;
            _now += 1f;
            Assert.DoesNotThrow(() => Haptics.Tick(), "the editor platform call is a no-op");
        }

        [Test]
        public void SnapFeedbackTicksOnEachNewAcquisition()
        {
            ServiceHub.Register<IEditorSession>(_session);
            var fb = new GameObject("SnapFeedback").AddComponent<SnapFeedback>();
            try
            {
                Call(fb, "Start");
                Pointer(PointerPhase.Down, 0.6, 0.6);
                Pointer(PointerPhase.Move, 0.301, 0.302);
                Assert.AreEqual(1, _ticks, "tick on the snap");
                _now += 0.2f;
                Pointer(PointerPhase.Move, 0.302, 0.301);
                Assert.AreEqual(1, _ticks, "same dot: no second tick");
                Pointer(PointerPhase.Up, 0.302, 0.301);
                Call(fb, "OnDestroy");
                Pointer(PointerPhase.Down, 0.301, 0.301);
                Assert.AreEqual(1, _ticks, "unsubscribed on destroy");
            }
            finally { Object.DestroyImmediate(fb.gameObject); }
        }

        [Test]
        public void EngineInstallerAddsSnapFeedback()
        {
            var go = new GameObject("Installer");
            try
            {
                go.AddComponent<EngineInstaller>();
                Call(go.GetComponent<EngineInstaller>(), "Awake");
                Assert.IsNotNull(go.GetComponent<SnapFeedback>(), "SnapFeedback added at runtime (no scene change)");
            }
            finally
            {
                Call(go.GetComponent<EngineInstaller>(), "OnDestroy");
                Object.DestroyImmediate(go);
            }
        }
    }
}
