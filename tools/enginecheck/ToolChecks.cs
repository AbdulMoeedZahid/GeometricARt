using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>Line/Circle tools: commit, discard, cancel, layer targeting, board replacement, tool switching.</summary>
    public static class ToolChecks
    {
        static PointerEvent Ev(PointerPhase phase, double x, double y, bool onBoard = true, double t = 0) =>
            new PointerEvent(phase, new Vec2(x, y), onBoard, t);

        static void Drag(EditorSession s, Vec2 a, Vec2 b, int moves = 5)
        {
            s.Pointer(Ev(PointerPhase.Down, a.X, a.Y));
            for (int i = 1; i <= moves; i++)
            {
                double k = (double)i / moves;
                s.Pointer(Ev(PointerPhase.Move, a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k));
            }
            s.Pointer(Ev(PointerPhase.Up, b.X, b.Y));
        }

        static int ShapeCount(EditorSession s)
        {
            int n = 0;
            foreach (var l in s.Board.Layers) n += l.Shapes.Count;
            return n;
        }

        public static void DefaultToolIsLine()
        {
            var s = new EditorSession();
            Check.That(s.ActiveTool == ToolId.Line, "default tool is Line");
            Check.That(!s.SnapEnabled && s.SnapCandidates.Count == 0, "snapping off");
        }

        public static void LineCommitsToActiveLayer()
        {
            var s = new EditorSession();
            var kinds = new List<BoardChangeKind>();
            s.BoardChanged += k => kinds.Add(k);
            int previews = 0;
            s.PreviewChanged += () => previews++;

            s.Pointer(Ev(PointerPhase.Down, 0.2, 0.2));
            Check.That(s.Cursor.HasValue, "cursor shown on down");
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.4));
            Check.That(s.Preview.Count == 1 && s.Preview[0] is LineShape, "line ghost while dragging");
            var ghost = (LineShape)s.Preview[0];
            Check.Near(ghost.B.X, 0.5, "ghost follows pointer");
            Check.That(ShapeCount(s) == 0, "ghost is not in the model");
            s.Pointer(Ev(PointerPhase.Move, 0.8, 0.6));
            s.Pointer(Ev(PointerPhase.Up, 0.8, 0.6));

            var shapes = s.ActiveLayer.Shapes;
            Check.That(shapes.Count == 1 && shapes[0] is LineShape, "one line committed");
            var line = (LineShape)shapes[0];
            Check.Near(line.A.X, 0.2, "A.x"); Check.Near(line.A.Y, 0.2, "A.y");
            Check.Near(line.B.X, 0.8, "B.x"); Check.Near(line.B.Y, 0.6, "B.y");
            Check.That(!line.Id.IsNone, "real id");
            Check.That(line.Style.Equals(s.Style), "current style");
            Check.That(kinds.Count == 1 && kinds[0] == BoardChangeKind.Shapes, "BoardChanged(Shapes) once");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "ghost and cursor cleared");
            Check.That(previews > 0, "PreviewChanged raised");
            Check.That(!s.GestureActive, "gesture closed");
        }

        public static void CircleCommitsWithRadius()
        {
            var s = new EditorSession();
            s.SetTool(ToolId.Circle);
            Check.That(s.ActiveTool == ToolId.Circle, "circle tool");
            s.Pointer(Ev(PointerPhase.Down, 0.5, 0.5));
            s.Pointer(Ev(PointerPhase.Move, 0.7, 0.5));
            Check.That(s.Preview.Count >= 1 && s.Preview[0] is CircleShape, "circle ghost");
            Check.Near(((CircleShape)s.Preview[0]).Radius, 0.2, "ghost radius", 1e-12);
            s.Pointer(Ev(PointerPhase.Up, 0.5, 0.8));
            var c = s.ActiveLayer.Shapes[0] as CircleShape;
            Check.That(c != null, "circle committed");
            Check.Near(c.Center.X, 0.5, "centre x"); Check.Near(c.Center.Y, 0.5, "centre y");
            Check.Near(c.Radius, 0.3, "radius from the Up point", 1e-12);
            Check.That(s.Preview.Count == 0, "ghost cleared");
        }

        public static void TooShortIsDiscarded()
        {
            var s = new EditorSession();
            int changes = 0;
            s.BoardChanged += _ => changes++;
            Drag(s, new Vec2(0.5, 0.5), new Vec2(0.505, 0.505));
            s.SetTool(ToolId.Circle);
            Drag(s, new Vec2(0.5, 0.5), new Vec2(0.5, 0.509));
            Check.That(ShapeCount(s) == 0 && changes == 0, "tiny line/circle discarded");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "ghost cleared after discard");
            Drag(s, new Vec2(0.5, 0.5), new Vec2(0.5, 0.511));
            Check.That(ShapeCount(s) == 1, "just above the minimum commits");
        }

        public static void TapCommitsNothing()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.3, 0.3));
            s.Pointer(Ev(PointerPhase.Up, 0.3, 0.3));
            Check.That(ShapeCount(s) == 0, "a tap draws nothing");
        }

        public static void CancelDiscards()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.9));
            s.Pointer(Ev(PointerPhase.Cancel, 0.9, 0.9));
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue && !s.GestureActive, "cancel clears");
            s.Pointer(Ev(PointerPhase.Up, 0.9, 0.9));
            Check.That(ShapeCount(s) == 0, "Up after Cancel commits nothing");
        }

        public static void DownOffBoardDoesNothing()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 1.4, 0.5, onBoard: false));
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.5));
            s.Pointer(Ev(PointerPhase.Up, 0.2, 0.5));
            Check.That(ShapeCount(s) == 0 && !s.GestureActive, "no gesture from an off-board down");
        }

        public static void OffBoardMoveKeepsLastValidPoint()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.2, 0.2));
            s.Pointer(Ev(PointerPhase.Move, 0.6, 0.2));
            s.Pointer(Ev(PointerPhase.Move, 1.3, 0.2, onBoard: false));
            s.Pointer(Ev(PointerPhase.Up, double.NaN, 0.2, onBoard: false));
            var line = (LineShape)s.ActiveLayer.Shapes[0];
            Check.Near(line.B.X, 0.6, "last valid point kept");
        }

        public static void HiddenLayerRefusesWithToast()
        {
            var s = new EditorSession();
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            s.ActiveLayer.Visible = false;   // direct model edit: independent of the layers builder's commands
            Drag(s, new Vec2(0.1, 0.1), new Vec2(0.9, 0.9));
            Check.That(ShapeCount(s) == 0, "nothing drawn on a hidden layer");
            Check.That(toasts.Count == 1 && toasts[0] == EditorSession.HiddenLayerToast, "hidden-layer toast once");
            Check.That(s.Preview.Count == 0 && !s.GestureActive, "no ghost");
        }

        public static void LayerHiddenMidGestureDiscards()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.9));
            s.ActiveLayer.Visible = false;
            s.Pointer(Ev(PointerPhase.Up, 0.9, 0.9));
            Check.That(ShapeCount(s) == 0, "not committed into a layer hidden mid-gesture");
        }

        public static void CommitsIntoActiveLayerOnly()
        {
            var board = new Board();
            var bottom = new Layer(board.NewId(), "Bottom");
            var top = new Layer(board.NewId(), "Top");
            board.Layers.Add(bottom);
            board.Layers.Add(top);
            board.ActiveLayerId = bottom.Id;
            var s = new EditorSession(board);
            Drag(s, new Vec2(0.1, 0.1), new Vec2(0.4, 0.1));
            Check.That(bottom.Shapes.Count == 1 && top.Shapes.Count == 0, "went into the active (bottom) layer");

            board.ActiveLayerId = top.Id;
            s.SetTool(ToolId.Circle);
            Drag(s, new Vec2(0.5, 0.5), new Vec2(0.6, 0.5));
            Check.That(bottom.Shapes.Count == 1 && top.Shapes.Count == 1 && top.Shapes[0] is CircleShape, "then into top");
            Check.That(bottom.Shapes[0].Id != top.Shapes[0].Id, "unique ids");
        }

        public static void BoardReplacedAbortsGesture()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.9));
            s.ClearBoard();
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue && !s.GestureActive, "gesture aborted on board swap");
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.9));
            s.Pointer(Ev(PointerPhase.Up, 0.5, 0.9));
            Check.That(ShapeCount(s) == 0, "the stale gesture never commits into the new board");
        }

        public static void DottedToggleAppliesToNewShapes()
        {
            var s = new EditorSession();
            int states = 0;
            s.StateChanged += () => states++;
            Check.That(!s.DottedLine, "solid by default");
            s.ToggleDottedLine();
            Check.That(s.DottedLine && states == 1, "toggled on, StateChanged");
            Drag(s, new Vec2(0.1, 0.1), new Vec2(0.5, 0.1));
            Check.That(s.ActiveLayer.Shapes[0].Style.Dash == LineStyle.Dotted, "dotted line committed");
            s.ToggleDottedLine();
            Drag(s, new Vec2(0.1, 0.3), new Vec2(0.5, 0.3));
            Check.That(s.ActiveLayer.Shapes[1].Style.Dash == LineStyle.Solid, "solid again");
        }

        public static void UnsupportedToolToastsAndKeepsCurrent()
        {
            var s = new EditorSession();
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            s.SetTool(ToolId.Circle);
            s.SetTool(ToolId.Stamps);
            Check.That(s.ActiveTool == ToolId.Circle, "kept Circle");
            Check.That(toasts.Count == 1 && toasts[0] == EditorSession.ComingSoonToast, "coming soon toast");
            s.SetTool(ToolId.None);
            Check.That(s.ActiveTool == ToolId.None, "None allowed");
            Drag(s, new Vec2(0.1, 0.1), new Vec2(0.9, 0.9));
            Check.That(ShapeCount(s) == 0, "None draws nothing");
        }

        public static void SetToolRaisesStateAndHintAndCancels()
        {
            var s = new EditorSession();
            int states = 0;
            s.StateChanged += () => states++;
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.9));
            s.SetTool(ToolId.Circle);
            Check.That(states == 1, "exactly one StateChanged");
            Check.That(s.ToolHint.Length > 0, "hint set");
            Check.That(!s.GestureActive && s.Preview.Count == 0, "switching tools cancels the open gesture");
            s.SetTool(ToolId.Circle);
            Check.That(states == 1, "same tool: no event");
        }

        public static void SecondDownWithoutUpRestarts()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.9));
            s.Pointer(Ev(PointerPhase.Down, 0.3, 0.3));   // lost Up
            s.Pointer(Ev(PointerPhase.Up, 0.6, 0.3));
            var shapes = s.ActiveLayer.Shapes;
            Check.That(shapes.Count == 1, "only the second stroke");
            Check.Near(((LineShape)shapes[0]).A.X, 0.3, "starts at the new down");
        }

        // ---------- OneEuroFilter2D (BoardInputRouter smoothing) ----------

        static double Noise(int i) => ((i * 7919) % 13 - 6) / 6.0;   // deterministic pseudo-noise in [-1, 1]

        public static void FilterPassesFirstSampleAndIsDeterministic()
        {
            var f1 = new OneEuroFilter2D(1.2, 5, 1);
            var f2 = new OneEuroFilter2D(1.2, 5, 1);
            var first = f1.Filter(new Vec2(0.3, 0.4), 10.0);
            f2.Filter(new Vec2(0.3, 0.4), 10.0);
            Check.That(first == new Vec2(0.3, 0.4), "first sample passes through");
            for (int i = 1; i < 200; i++)
            {
                var p = new Vec2(0.3 + 0.002 * i, 0.4 + 0.001 * Noise(i));
                Check.That(f1.Filter(p, 10.0 + i / 60.0) == f2.Filter(p, 10.0 + i / 60.0), "deterministic");
            }
            var before = f1.Value;
            Check.That(f1.Filter(new Vec2(0.9, 0.9), 10.0 + 199 / 60.0) == before, "same timestamp is ignored");
        }

        public static void FilterSteadiesJitterAtRest()
        {
            // 2 mm-ish jitter (0.005 board units peak) around a resting finger: the output must move far less.
            var f = new OneEuroFilter2D(1.2, 5, 1);
            var centre = new Vec2(0.5, 0.5);
            double maxDev = 0;
            for (int i = 0; i < 240; i++)
            {
                var p = centre + new Vec2(0.005 * Noise(i), 0.005 * Noise(i + 5));
                var o = f.Filter(p, i / 60.0);
                if (i > 60) maxDev = System.Math.Max(maxDev, Vec2.Distance(o, centre));
            }
            Check.That(maxDev < 0.0025, $"jitter at rest reduced at least 2x (max deviation {maxDev})");
        }

        public static void FilterIsFrameRateIndependentAndConverges()
        {
            // A drag at 0.5 board/s that stops: at 30 and 120 fps the output after 0.6 s of rest is near the end point.
            foreach (int fps in new[] { 30, 60, 120 })
            {
                var f = new OneEuroFilter2D(1.2, 5, 1);
                Vec2 o = default;
                int moveFrames = fps;   // 1 s
                for (int i = 0; i <= moveFrames + fps; i++)
                {
                    double t = (double)i / fps;
                    double x = 0.2 + 0.5 * System.Math.Min(t, 1.0);
                    o = f.Filter(new Vec2(x, 0.5), t);
                    if (i == moveFrames)
                        Check.That(0.7 - o.X < 0.03, $"{fps} fps: lag while dragging under 3% of the board ({0.7 - o.X})");
                }
                Check.That(System.Math.Abs(o.X - 0.7) < 0.002, $"{fps} fps: settles on the end point ({o.X})");
            }
        }
    }
}
