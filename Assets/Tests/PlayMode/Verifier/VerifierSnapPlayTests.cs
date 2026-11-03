using System;
using System.Collections;
using GeoART.App;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace GeoART.Tests.Verifier.PlayMode
{
    /// <summary>
    /// VERIFIER (Play Mode, U4): snapping end to end. A virtual Touchscreen drives the real BoardInputRouter (filtering,
    /// UI exclusion, real frames and time) into the real EditorSession, which measures tolerances with the App's real
    /// ScreenSnapMetric over a fake IBoardSpace (board = screen square [100, 900] px). Checks that the committed
    /// coordinates are EXACTLY the candidates': a drag ending near a shape intersection, a drag ending near a grid
    /// intersection (square and hex), tap-to-connect, and the H/V lock with alignment snap.
    /// </summary>
    public sealed class VerifierSnapPlayTests
    {
        sealed class FakeBoardSpace : IBoardSpace
        {
            public PlacementState State => PlacementState.Placed;
#pragma warning disable 67
            public event Action<PlacementState> StateChanged;
#pragma warning restore 67
            public bool IsPlaced => true;
            public void PlaceAtReticle() { }
            public void Reset() { }

            public bool TryScreenToBoard(Vec2 px, out Vec2 board)
            {
                board = new Vec2((px.X - 100) / 800.0, (px.Y - 100) / 800.0);
                return board.X >= 0 && board.X <= 1 && board.Y >= 0 && board.Y <= 1;
            }

            public bool TryBoardToScreen(Vec2 b, out Vec2 px)
            {
                px = new Vec2(100 + b.X * 800, 100 + b.Y * 800);
                return true;
            }
        }

        Touchscreen _touch;
        FakeBoardSpace _space;
        EditorSession _session;
        GameObject _go;
        InputSettings _savedSettings, _settings;

        [SetUp]
        public void SetUp()
        {
            ServiceHub.Clear();
            _touch = InputSystem.AddDevice<Touchscreen>();
            _space = new FakeBoardSpace();
            _session = new EditorSession { SnapMetric = new ScreenSnapMetric(_space) };
            ServiceHub.Register<IBoardSpace>(_space);
            ServiceHub.Register<IEditorSession>(_session);
            _savedSettings = InputSystem.settings;
            _settings = ScriptableObject.CreateInstance<InputSettings>();
            _settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = _settings;
            _go = new GameObject("VerifierSnapRouter");
            _go.AddComponent<BoardInputRouter>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            if (_touch != null) InputSystem.RemoveDevice(_touch);
            if (_savedSettings != null) InputSystem.settings = _savedSettings;
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            ServiceHub.Clear();
        }

        // ---------- input helpers (same pacing as VerifierInputRouterPlayTests) ----------

        static IEnumerator Frame()
        {
            System.Threading.Thread.Sleep(16);
            yield return null;
        }

        void Touch(InputTouchPhase phase, Vector2 p) =>
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = 1, phase = phase, position = p, pressure = 1f });

        static Vector2 Px(Vec2 board) => new Vector2(100f + (float)board.X * 800f, 100f + (float)board.Y * 800f);

        /// <summary>Drag from a to b (screen px), rest so the router's filter settles on b, release.</summary>
        IEnumerator Drag(Vector2 a, Vector2 b)
        {
            Touch(InputTouchPhase.Began, a);
            yield return Frame();
            for (int i = 1; i <= 16; i++)
            {
                Touch(InputTouchPhase.Moved, Vector2.Lerp(a, b, i / 16f));
                yield return Frame();
            }
            for (int i = 0; i < 30; i++) { Touch(InputTouchPhase.Stationary, b); yield return Frame(); }
            Touch(InputTouchPhase.Ended, b);
            yield return Frame();
        }

        IEnumerator Tap(Vector2 p)
        {
            Touch(InputTouchPhase.Began, p);
            yield return Frame();
            Touch(InputTouchPhase.Stationary, p);
            yield return Frame();
            Touch(InputTouchPhase.Ended, p);
            yield return Frame();
        }

        /// <summary>An offset well inside the snap tolerance (40% of it), so the release point is clearly not exact.</summary>
        Vector2 Off(double sx, double sy)
        {
            double d = _session.SnapMetric.TolerancePx * 0.4;
            return new Vector2((float)(sx * d), (float)(sy * d));
        }

        LineShape LastLine()
        {
            var shapes = _session.ActiveLayer.Shapes;
            Assert.Greater(shapes.Count, 0, "something was committed");
            var line = shapes[shapes.Count - 1] as LineShape;
            Assert.NotNull(line, "last shape is a line");
            return line;
        }

        SnapCandidate FindCandidate(SnapKind kind, Func<Vec2, bool> where)
        {
            foreach (var c in _session.SnapCandidates)
                if (c.Kind == kind && where(c.Position)) return c;
            Assert.Fail($"no {kind} candidate found");
            return default;
        }

        static void AssertExact(Vec2 expected, Vec2 actual, string what) =>
            Assert.IsTrue(expected == actual,
                $"{what}: expected exactly ({expected.X:R}, {expected.Y:R}), got ({actual.X:R}, {actual.Y:R})");

        // ---------- tests ----------

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            for (int i = 0; i < 5; i++) yield return Frame();
            yield return Tap(new Vector2(500, 500));
        }

        [UnityTest]
        public IEnumerator DragEndSnapsToShapeIntersection()
        {
            yield return Frame();
            _session.SetSnapEnabled(false);   // two raw crossing lines first
            yield return Drag(Px(new Vec2(0.12, 0.18)), Px(new Vec2(0.86, 0.62)));
            yield return Drag(Px(new Vec2(0.15, 0.71)), Px(new Vec2(0.66, 0.08)));
            Assert.AreEqual(2, _session.ActiveLayer.Shapes.Count, "two lines drawn");
            _session.SetSnapEnabled(true);
            var l0 = (LineShape)_session.ActiveLayer.Shapes[0];
            var l1 = (LineShape)_session.ActiveLayer.Shapes[1];
            Assert.AreEqual(1, Intersect.SegmentSegment(l0.A, l0.B, l1.A, l1.B, out Vec2 x), "they cross");
            var cand = FindCandidate(SnapKind.Intersection, p => Vec2.Distance(p, x) < 1e-9);

            yield return Drag(Px(new Vec2(0.9, 0.95)), Px(cand.Position) + Off(0.8, -0.6));
            var line = LastLine();
            AssertExact(cand.Position, line.B, "line end == the intersection candidate");
            Assert.AreEqual(3, _session.ActiveLayer.Shapes.Count);
        }

        [UnityTest]
        public IEnumerator DragEndSnapsToGridIntersectionSquareAndHex()
        {
            yield return Frame();
            yield return Drag(Px(new Vec2(0.13, 0.62)), Px(new Vec2(0.5, 0.5)) + Off(0.7, 0.7));
            AssertExact(new Vec2(0.5, 0.5), LastLine().B, "square 2x2: end == grid corner (0.5, 0.5)");

            _session.SetGridKind(GridKind.Hex);
            yield return Frame();
            // A hex corner well inside the board, from TileGrid itself (the only source of grid geometry).
            var corners = new System.Collections.Generic.List<SnapCandidate>();
            TileGrid.SnapPoints(GridKind.Hex, _session.Board.GridN, corners);
            Vec2 target = default;
            double best = double.MaxValue;
            foreach (var c in corners)
                if (c.Kind == SnapKind.CellCorner && Vec2.Distance(c.Position, new Vec2(0.5, 0.5)) < best)
                {
                    best = Vec2.Distance(c.Position, new Vec2(0.5, 0.5));
                    target = c.Position;
                }
            yield return Drag(Px(new Vec2(0.1, 0.1)), Px(target) + Off(-0.6, 0.8));
            AssertExact(target, LastLine().B, "hex: end == the TileGrid corner");
        }

        [UnityTest]
        public IEnumerator TapTapConnectsTwoCandidates()
        {
            yield return Frame();
            Vec2 a = new Vec2(0.25, 0.25), b = new Vec2(0.75, 0.75);   // 2x2 tile centres
            yield return Tap(Px(a) + Off(0.5, -0.5));
            Assert.AreEqual(0, _session.ActiveLayer.Shapes.Count, "first tap draws nothing");
            Assert.IsTrue(_session.PendingConnectStart.HasValue, "first tap sets the pending start");
            AssertExact(a, _session.PendingConnectStart.Value, "pending start == candidate");
            yield return Tap(Px(b) + Off(-0.5, 0.5));
            Assert.AreEqual(1, _session.ActiveLayer.Shapes.Count, "second tap connects");
            var line = LastLine();
            AssertExact(a, line.A, "A");
            AssertExact(b, line.B, "B");
            Assert.IsFalse(_session.PendingConnectStart.HasValue, "pending cleared");
        }

        [UnityTest]
        public IEnumerator OrthoLockDrawsExactHorizontalWithAlignmentSnap()
        {
            yield return Frame();
            _session.SetOrthoLock(true);
            // Diagonal drag, mostly horizontal, ending near the tile centre column x = 0.75.
            yield return Drag(Px(new Vec2(0.25, 0.25)) + Off(0.4, 0.4), Px(new Vec2(0.75, 0.33)) + Off(0.5, 0));
            var line = LastLine();
            AssertExact(new Vec2(0.25, 0.25), line.A, "start snapped to the tile centre");
            Assert.IsTrue(line.A.Y == line.B.Y, $"exactly horizontal: {line.A.Y:R} vs {line.B.Y:R}");
            AssertExact(new Vec2(0.75, 0.25), line.B, "end aligned to x = 0.75 on the axis");

            // Mostly vertical: a vertical line.
            yield return Drag(Px(new Vec2(0.25, 0.25)) + Off(-0.3, 0.3), Px(new Vec2(0.31, 0.83)));
            var v = LastLine();
            Assert.IsTrue(v.A.X == v.B.X, $"exactly vertical: {v.A.X:R} vs {v.B.X:R}");
        }
    }
}
