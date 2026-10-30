using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER: Line and Circle tools written from the spec (down/drag/up, ghost preview, min size ~0.01, commit to the
    /// ACTIVE layer, refuse on a hidden layer with a toast, default tool Line, Cancel and LoadBoard abort the gesture).
    /// </summary>
    public static class VerifierToolChecks
    {
        static double s_T;
        static PointerEvent Ev(PointerPhase p, double x, double y, bool on = true) =>
            new PointerEvent(p, new Vec2(x, y), on, s_T += 0.016);

        static void Drag(EditorSession s, double ax, double ay, double bx, double by, int steps = 8)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * t, ay + (by - ay) * t));
            }
            s.Pointer(Ev(PointerPhase.Up, bx, by));
        }

        static int ShapeCount(EditorSession s)
        {
            int n = 0;
            foreach (var l in s.Board.Layers) n += l.Shapes.Count;
            return n;
        }

        public static void V_DefaultToolIsLine()
        {
            var s = new EditorSession();
            Check.That(s.ActiveTool == ToolId.Line, $"default tool Line, got {s.ActiveTool}");
        }

        public static void V_SetToolSwitchesAndRaisesState()
        {
            var s = new EditorSession();
            int state = 0;
            s.StateChanged += () => state++;
            s.SetTool(ToolId.Circle);
            Check.That(s.ActiveTool == ToolId.Circle && state > 0, "Circle selected with StateChanged");
            s.SetTool(ToolId.Line);
            Check.That(s.ActiveTool == ToolId.Line, "back to Line");
        }

        public static void V_LineCommitsOnUp()
        {
            var s = new EditorSession();
            int board = 0;
            s.BoardChanged += k => { if ((k & BoardChangeKind.Shapes) != 0 || k != BoardChangeKind.None) board++; };
            s.Pointer(Ev(PointerPhase.Down, 0.2, 0.3));
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.5));
            Check.That(s.ActiveLayer.Shapes.Count == 0, "nothing committed while dragging");
            Check.That(s.Preview.Count >= 1, "ghost while dragging");
            LineShape ghost = null;
            foreach (var g in s.Preview) if (g is LineShape gl) ghost = gl;
            Check.That(ghost != null, "ghost is a LineShape");
            Check.Near(ghost.A.X, 0.2, "ghost A.x", 1e-6); Check.Near(ghost.A.Y, 0.3, "ghost A.y", 1e-6);
            Check.Near(ghost.B.X, 0.5, "ghost B.x", 1e-6); Check.Near(ghost.B.Y, 0.5, "ghost B.y", 1e-6);
            s.Pointer(Ev(PointerPhase.Move, 0.7, 0.6));
            s.Pointer(Ev(PointerPhase.Up, 0.7, 0.6));
            Check.That(s.ActiveLayer.Shapes.Count == 1, "one line committed");
            var line = s.ActiveLayer.Shapes[0] as LineShape;
            Check.That(line != null, "committed a LineShape");
            Check.Near(line.A.X, 0.2, "A.x", 1e-6); Check.Near(line.A.Y, 0.3, "A.y", 1e-6);
            Check.Near(line.B.X, 0.7, "B.x", 1e-6); Check.Near(line.B.Y, 0.6, "B.y", 1e-6);
            Check.That(!line.Id.IsNone, "shape id set");
            Check.That(s.Preview.Count == 0, "ghost cleared after commit");
            Check.That(board > 0, "BoardChanged raised on commit");
        }

        public static void V_LineUsesSessionStyle()
        {
            var s = new EditorSession();
            var style = new StrokeStyle(Palette.Red, LineWidth.Thin, LineStyle.Dotted);
            s.Style = style;
            Drag(s, 0.1, 0.1, 0.6, 0.1);
            Check.That(s.ActiveLayer.Shapes.Count == 1 && s.ActiveLayer.Shapes[0].Style.Equals(style), "committed with session style (dotted kept)");
        }

        public static void V_TooShortLineNotCommitted()
        {
            var s = new EditorSession();
            Drag(s, 0.5, 0.5, 0.503, 0.5);
            Check.That(ShapeCount(s) == 0, "0.003 long line rejected");
            s.Pointer(Ev(PointerPhase.Down, 0.5, 0.5));
            s.Pointer(Ev(PointerPhase.Up, 0.5, 0.5));
            Check.That(ShapeCount(s) == 0, "tap commits nothing");
            Check.That(s.Preview.Count == 0, "no ghost left after a rejected gesture");
        }

        public static void V_CircleCommitsOnUp()
        {
            var s = new EditorSession();
            s.SetTool(ToolId.Circle);
            s.Pointer(Ev(PointerPhase.Down, 0.5, 0.5));
            s.Pointer(Ev(PointerPhase.Move, 0.6, 0.5));
            CircleShape ghost = null;
            foreach (var g in s.Preview) if (g is CircleShape gc) ghost = gc;
            Check.That(ghost != null, "circle ghost while dragging");
            Check.Near(ghost.Radius, 0.1, "ghost radius", 1e-6);
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.75));
            s.Pointer(Ev(PointerPhase.Up, 0.5, 0.75));
            Check.That(s.ActiveLayer.Shapes.Count == 1, "one circle committed");
            var c = s.ActiveLayer.Shapes[0] as CircleShape;
            Check.That(c != null, "CircleShape");
            Check.Near(c.Center.X, 0.5, "centre x", 1e-6); Check.Near(c.Center.Y, 0.5, "centre y", 1e-6);
            Check.Near(c.Radius, 0.25, "radius = |up - centre|", 1e-6);
            Check.That(s.Preview.Count == 0, "ghost cleared");
        }

        public static void V_TinyCircleNotCommitted()
        {
            var s = new EditorSession();
            s.SetTool(ToolId.Circle);
            Drag(s, 0.5, 0.5, 0.504, 0.5);
            Check.That(ShapeCount(s) == 0, "radius 0.004 rejected");
        }

        public static void V_CommitsToActiveNotTopLayer()
        {
            var s = new EditorSession();
            EntityId bottom = s.Board.Layers[0].Id;
            s.AddLayer();
            s.AddLayer();
            s.SelectLayer(bottom);
            Drag(s, 0.1, 0.1, 0.9, 0.9);
            Check.That(s.Board.Layers[0].Shapes.Count == 1, "line went to the selected bottom layer");
            Check.That(s.Board.Layers[2].Shapes.Count == 0, "top layer untouched");
        }

        public static void V_HiddenLayerRefusesWithToast()
        {
            var s = new EditorSession();
            var toasts = new List<string>();
            s.Toast += m => toasts.Add(m);
            s.SetLayerVisible(s.ActiveLayer.Id, false);
            Drag(s, 0.1, 0.1, 0.9, 0.9);
            Check.That(ShapeCount(s) == 0, "nothing committed on a hidden layer");
            Check.That(toasts.Count >= 1, "toast explains why");
            Check.That(s.Preview.Count == 0, "no ghost stuck");
            s.SetTool(ToolId.Circle);
            Drag(s, 0.5, 0.5, 0.7, 0.5);
            Check.That(ShapeCount(s) == 0, "circle refused too");
            s.SetLayerVisible(s.ActiveLayer.Id, true);
            Drag(s, 0.5, 0.5, 0.7, 0.5);
            Check.That(ShapeCount(s) == 1, "works again once visible");
        }

        public static void V_CancelAbortsGesture()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.8, 0.8));
            s.Pointer(Ev(PointerPhase.Cancel, 0.8, 0.8, false));
            Check.That(s.Preview.Count == 0, "ghost cleared on Cancel");
            s.Pointer(Ev(PointerPhase.Up, 0.8, 0.8));
            Check.That(ShapeCount(s) == 0, "Up after Cancel commits nothing");
        }

        public static void V_UpWithoutDownIgnored()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Move, 0.3, 0.3));
            s.Pointer(Ev(PointerPhase.Up, 0.9, 0.9));
            Check.That(ShapeCount(s) == 0 && s.Preview.Count == 0, "stray Move/Up does nothing");
        }

        public static void V_DoubleUpCommitsOnce()
        {
            var s = new EditorSession();
            Drag(s, 0.1, 0.1, 0.6, 0.6);
            s.Pointer(Ev(PointerPhase.Up, 0.6, 0.6));
            Check.That(ShapeCount(s) == 1, "second Up does not commit again");
        }

        public static void V_LoadBoardAbortsGesture()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.8, 0.8));
            s.LoadBoard(GoldenBoards.Empty());
            Check.That(s.Preview.Count == 0, "ghost cleared by LoadBoard");
            s.Pointer(Ev(PointerPhase.Up, 0.8, 0.8));
            Check.That(ShapeCount(s) == 0, "gesture from the old board does not commit");
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.8, 0.8));
            s.ClearBoard();
            s.Pointer(Ev(PointerPhase.Up, 0.8, 0.8));
            Check.That(ShapeCount(s) == 0, "ClearBoard aborts too");
        }

        public static void V_ToolSwitchMidGestureNoCrash()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.5));
            s.SetTool(ToolId.Circle);
            s.Pointer(Ev(PointerPhase.Move, 0.6, 0.6));
            s.Pointer(Ev(PointerPhase.Up, 0.6, 0.6));
            Check.That(ShapeCount(s) <= 1, "at most one shape");
            Check.That(s.Preview.Count == 0, "no ghost stuck after switching tools mid-gesture");
        }

        public static void V_NonFinitePositionsIgnored()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, double.NaN, 0.5));
            s.Pointer(Ev(PointerPhase.Up, 0.9, 0.9));
            Check.That(ShapeCount(s) == 0, "NaN down starts nothing");
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, double.PositiveInfinity, 0.5));
            s.Pointer(Ev(PointerPhase.Up, 0.6, 0.1));
            foreach (var l in s.Board.Layers)
                foreach (var sh in l.Shapes)
                    if (sh is LineShape ln) Check.That(ln.A.IsFinite && ln.B.IsFinite, "no non-finite geometry committed");
        }

        public static void V_ShapeIdsUnique()
        {
            var s = new EditorSession();
            for (int i = 0; i < 5; i++) Drag(s, 0.1, 0.1 + i * 0.1, 0.8, 0.1 + i * 0.1);
            s.AddLayer();
            s.SetTool(ToolId.Circle);
            for (int i = 0; i < 3; i++) Drag(s, 0.5, 0.5, 0.6 + i * 0.05, 0.5);
            var ids = new HashSet<int>();
            foreach (var l in s.Board.Layers)
            {
                Check.That(ids.Add(l.Id.Value), "layer id unique");
                foreach (var sh in l.Shapes) Check.That(ids.Add(sh.Id.Value), "shape id unique");
            }
            Check.That(ShapeCount(s) == 8, $"8 shapes committed, got {ShapeCount(s)}");
        }

        public static void V_CursorFollowsPointer()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.3, 0.4));
            Check.That(s.Cursor.HasValue, "cursor shown while touching");
            s.Pointer(Ev(PointerPhase.Cancel, 0.3, 0.4, false));
            Check.That(!s.Cursor.HasValue, "cursor hidden on Cancel");
        }

        public static void V_UpOffBoardDoesNotCommitGarbage()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.2, 0.2));
            s.Pointer(Ev(PointerPhase.Move, 0.6, 0.6));
            s.Pointer(Ev(PointerPhase.Up, 0, 0, false));   // router could not project the release point
            foreach (var l in s.Board.Layers)
                foreach (var sh in l.Shapes)
                    if (sh is LineShape ln)
                        Check.That(!(ln.B.X == 0 && ln.B.Y == 0), "off-board Up must not commit to the (0,0) placeholder");
            Check.That(s.Preview.Count == 0, "no ghost stuck after off-board Up");
        }
    }
}
