using System;
using System.Collections;
using System.Collections.Generic;
using GeoART.App;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using EntityId = GeoART.Contracts.EntityId;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace GeoART.Tests.Verifier.PlayMode
{
    /// <summary>
    /// VERIFIER (Play Mode): the Select tool end to end. A virtual Touchscreen drives the real BoardInputRouter (filter,
    /// real frames and time) into the real EditorSession with the App's ScreenSnapMetric over a fake IBoardSpace
    /// (board = screen square [100, 900] px). A one-finger box selects the enclosed shapes and toasts the count; a second
    /// finger mid-drag cancels and restores the previous selection.
    /// </summary>
    public sealed class VerifierSelectPlayTests
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
        readonly List<string> _toasts = new List<string>();
        LineShape _line;
        CircleShape _circle;
        LineShape _far;

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
            _go = new GameObject("VerifierSelectRouter");
            _go.AddComponent<BoardInputRouter>();

            Board b = _session.Board;
            Layer layer = _session.ActiveLayer;
            _line = new LineShape(b.NewId(), new Vec2(0.2, 0.2), new Vec2(0.35, 0.3), StrokeStyle.Default);
            _circle = new CircleShape(b.NewId(), new Vec2(0.3, 0.6), 0.08, StrokeStyle.Default);
            _far = new LineShape(b.NewId(), new Vec2(0.6, 0.6), new Vec2(0.9, 0.9), StrokeStyle.Default);
            layer.Shapes.Add(_line);
            layer.Shapes.Add(_circle);
            layer.Shapes.Add(_far);
            _toasts.Clear();
            _session.Toast += t => _toasts.Add(t);
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

        void Touch(int id, InputTouchPhase phase, Vector2 p) =>
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = id, phase = phase, position = p, pressure = 1f });

        static Vector2 Px(double x, double y) => new Vector2(100f + (float)x * 800f, 100f + (float)y * 800f);

        IEnumerator Move(int id, Vector2 a, Vector2 b, int frames = 16, int rest = 30)
        {
            for (int i = 1; i <= frames; i++)
            {
                Touch(id, InputTouchPhase.Moved, Vector2.Lerp(a, b, i / (float)frames));
                yield return Frame();
            }
            for (int i = 0; i < rest; i++) { Touch(id, InputTouchPhase.Stationary, b); yield return Frame(); }
        }

        IEnumerator Drag(Vector2 a, Vector2 b)
        {
            Touch(1, InputTouchPhase.Began, a);
            yield return Frame();
            yield return Move(1, a, b);
            Touch(1, InputTouchPhase.Ended, b);
            yield return Frame();
        }

        void AssertSelected(string what, params Shape[] shapes)
        {
            var want = new HashSet<EntityId>();
            foreach (var s in shapes) want.Add(s.Id);
            Assert.IsTrue(_session.Selection.Ids.SetEquals(want),
                $"{what}: {_session.Selection.Ids.Count} selected, expected {want.Count}");
        }

        [UnityTest, Order(0)]
        public IEnumerator A0_WarmUp()
        {
            for (int i = 0; i < 5; i++) yield return Frame();
            Touch(1, InputTouchPhase.Began, new Vector2(500, 500));
            yield return Frame();
            Touch(1, InputTouchPhase.Ended, new Vector2(500, 500));
            yield return Frame();
        }

        [UnityTest]
        public IEnumerator OneFingerBoxSelectsEnclosedShapes()
        {
            yield return Frame();
            _session.SetTool(ToolId.SelectMove);
            Assert.AreEqual(ToolId.SelectMove, _session.ActiveTool);
            _toasts.Clear();
            int shapes = _session.ActiveLayer.Shapes.Count;

            Vector2 a = Px(0.1, 0.1), b = Px(0.45, 0.75);
            Touch(1, InputTouchPhase.Began, a);
            yield return Frame();
            yield return Move(1, a, b);
            AssertSelected("live, before release", _line, _circle);
            Assert.AreEqual(4, _session.Preview.Count, "the dotted rectangle is drawn");
            Touch(1, InputTouchPhase.Ended, b);
            yield return Frame();

            AssertSelected("after release", _line, _circle);
            CollectionAssert.AreEqual(new[] { "2 shapes selected" }, _toasts);
            Assert.AreEqual(0, _session.Preview.Count, "rectangle removed");
            Assert.AreEqual(shapes, _session.ActiveLayer.Shapes.Count, "nothing drawn");

            yield return Drag(Px(0.5, 0.5), Px(0.97, 0.97));
            AssertSelected("a new box replaces", _far);
        }

        [UnityTest]
        public IEnumerator SecondFingerCancelsAndRestoresSelection()
        {
            yield return Frame();
            _session.SetTool(ToolId.SelectMove);
            yield return Drag(Px(0.1, 0.1), Px(0.4, 0.4));
            AssertSelected("precondition", _line);
            _toasts.Clear();

            Vector2 a = Px(0.15, 0.45), b = Px(0.45, 0.75);
            Touch(1, InputTouchPhase.Began, a);
            yield return Frame();
            yield return Move(1, a, b, 16, 10);
            AssertSelected("live: the circle", _circle);

            Touch(2, InputTouchPhase.Began, Px(0.8, 0.2));
            yield return Frame();
            AssertSelected("second finger: previous selection restored", _line);
            Assert.AreEqual(0, _session.Preview.Count, "rectangle removed");
            Assert.IsFalse(_session.GestureActive);

            Touch(2, InputTouchPhase.Ended, Px(0.8, 0.2));
            yield return Frame();
            yield return Move(1, b, Px(0.6, 0.9), 4, 2);
            Touch(1, InputTouchPhase.Ended, Px(0.6, 0.9));
            yield return Frame();
            AssertSelected("the first finger lifting changes nothing", _line);
            Assert.AreEqual(0, _toasts.Count, "no toast for a cancelled box");
        }
    }
}
