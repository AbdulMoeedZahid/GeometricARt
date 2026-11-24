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
    /// VERIFIER (Play Mode, ui-line-plan 4.2 / 7, P3): the 32 px capture radius and the sticky release end to end. A
    /// virtual Touchscreen drives the real BoardInputRouter (One Euro filter, real frames) into the real EditorSession with
    /// the App's ScreenSnapMetric over a fake board space (board = screen square [100, 900] px, 800 px per board unit, so
    /// BoardCap = 1.5 tol / 800 and never binds below 1.5 tol). Offsets are fractions of the live TolerancePx, so the
    /// batch screen size does not matter. Same pacing as VerifierSnapPlayTests.
    /// </summary>
    public sealed class VerifierSnapRadiusPlayTests
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
            _go = new GameObject("VerifierSnapRadiusRouter");
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

        static IEnumerator Frame()
        {
            System.Threading.Thread.Sleep(16);
            yield return null;
        }

        void Touch(InputTouchPhase phase, Vector2 p) =>
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = 1, phase = phase, position = p, pressure = 1f });

        static Vector2 Px(Vec2 board) => new Vector2(100f + (float)board.X * 800f, 100f + (float)board.Y * 800f);

        double Tol => _session.SnapMetric.TolerancePx;

        /// <summary>Unit direction (0.6, 0.8) scaled to <paramref name="fraction"/> of the tolerance, in screen px.</summary>
        Vector2 Off(double fraction) => new Vector2((float)(0.6 * fraction * Tol), (float)(0.8 * fraction * Tol));

        IEnumerator MoveAndRest(Vector2 from, Vector2 to, int steps, int rest)
        {
            for (int i = 1; i <= steps; i++)
            {
                Touch(InputTouchPhase.Moved, Vector2.Lerp(from, to, (float)i / steps));
                yield return Frame();
            }
            for (int i = 0; i < rest; i++) { Touch(InputTouchPhase.Stationary, to); yield return Frame(); }
        }

        LineShape LastLine()
        {
            var shapes = _session.ActiveLayer.Shapes;
            Assert.Greater(shapes.Count, 0, "something was committed");
            var line = shapes[shapes.Count - 1] as LineShape;
            Assert.NotNull(line, "last shape is a line");
            return line;
        }

        static void AssertExact(Vec2 expected, Vec2 actual, string what) =>
            Assert.IsTrue(expected == actual,
                $"{what}: expected exactly ({expected.X:R}, {expected.Y:R}), got ({actual.X:R}, {actual.Y:R})");

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            for (int i = 0; i < 5; i++) yield return Frame();
            Touch(InputTouchPhase.Began, new Vector2(500, 500));
            yield return Frame();
            Touch(InputTouchPhase.Ended, new Vector2(500, 500));
            yield return Frame();
            _session.LoadBoard(null);
        }

        [UnityTest]
        public IEnumerator ReleaseAt28Of32FromADotCommitsExactly()
        {
            yield return Frame();
            Assert.AreEqual(32f, ScreenSnapMetric.ToleranceReferencePx, "precondition: 32 ref px");
            Vec2 dot = new Vec2(0.5, 0.5);   // 2x2 grid corner; the nearest other candidate is 200 px away
            Vector2 start = Px(new Vec2(0.13, 0.62)), end = Px(dot) + Off(28.0 / 32.0);
            Touch(InputTouchPhase.Began, start);
            yield return Frame();
            yield return MoveAndRest(start, end, 16, 30);
            Touch(InputTouchPhase.Ended, end);
            yield return Frame();
            AssertExact(dot, LastLine().B, "released at 28/32 of the radius (outside the old 24): end == the dot");
        }

        [UnityTest]
        public IEnumerator ReleaseAt34Of32StaysRaw()
        {
            yield return Frame();
            Vec2 dot = new Vec2(0.5, 0.5);
            Vector2 end = Px(dot) + Off(34.0 / 32.0);
            // Approach from far away along a path that never comes within the radius: straight in from the far side.
            Vector2 far = Px(dot) + Off(4.0);
            Touch(InputTouchPhase.Began, far);
            yield return Frame();
            yield return MoveAndRest(far, end, 16, 30);
            Touch(InputTouchPhase.Ended, end);
            yield return Frame();
            Assert.IsFalse(LastLine().B == dot, "outside the radius and never snapped: raw end");
        }

        [UnityTest]
        public IEnumerator SnappedDragThatWobbles40PxOnLiftStaysSnapped()
        {
            yield return Frame();
            Vec2 dot = new Vec2(0.75, 0.25);   // 2x2 tile centre
            Vector2 start = Px(new Vec2(0.2, 0.8)), atDot = Px(dot);
            Touch(InputTouchPhase.Began, start);
            yield return Frame();
            yield return MoveAndRest(start, atDot, 16, 20);
            Assert.IsTrue(_session.Cursor.HasValue && _session.Cursor.Value == dot, "precondition: snapped on the dot");
            Vector2 wobble = atDot + Off(40.0 / 32.0);   // 1.25 tol: outside the capture radius, inside the 1.5 tol sticky
            yield return MoveAndRest(atDot, wobble, 3, 30);
            Touch(InputTouchPhase.Ended, wobble);
            yield return Frame();
            AssertExact(dot, LastLine().B, "sticky: a 40 px wobble on lift still commits the dot");
        }

        [UnityTest]
        public IEnumerator SnappedDragMovedPastTheStickyRadiusLetsGo()
        {
            yield return Frame();
            Vec2 dot = new Vec2(0.75, 0.25);
            Vector2 start = Px(new Vec2(0.2, 0.8)), atDot = Px(dot);
            Touch(InputTouchPhase.Began, start);
            yield return Frame();
            yield return MoveAndRest(start, atDot, 16, 20);
            Vector2 away = atDot + Off(56.0 / 32.0);     // 1.75 tol: beyond 48
            yield return MoveAndRest(atDot, away, 6, 30);
            Touch(InputTouchPhase.Ended, away);
            yield return Frame();
            Assert.IsFalse(LastLine().B == dot, "beyond 1.5 tol the end lets go of the dot");
        }
    }
}
