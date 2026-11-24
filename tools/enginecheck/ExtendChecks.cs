using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Extend (action bar): each end of every selected line goes along the line to the first drawn shape in the way
    /// (line, circle, polygon edge, on a visible layer, selected or not), landing exactly on the crossing. Nothing in the
    /// way: the end stays. Not boundaries: the board edge, collinear lines, hidden layers. One "Extend" step, or none and
    /// a toast when nothing moved.
    /// </summary>
    public static class ExtendChecks
    {
        static LineShape Line(EditorSession s, Layer l, double x0, double y0, double x1, double y1)
        {
            var line = new LineShape(s.Board.NewId(), new Vec2(x0, y0), new Vec2(x1, y1), StrokeStyle.Default);
            l.Shapes.Add(line);
            return line;
        }

        static void Select(EditorSession s, params Shape[] shapes)
        {
            var ids = new List<EntityId>();
            foreach (var sh in shapes) ids.Add(sh.Id);
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(ids);
        }

        sealed class Spy
        {
            public readonly List<string> Toasts = new List<string>();
            public int Shapes;
            public string LastToast => Toasts.Count > 0 ? Toasts[Toasts.Count - 1] : null;

            public Spy(EditorSession s)
            {
                s.Toast += t => Toasts.Add(t);
                s.BoardChanged += k => { if ((k & BoardChangeKind.Shapes) != 0) Shapes++; };
            }
        }

        public static void ExtendsBothEndsToCrossingLines()
        {
            var s = new EditorSession();
            var line = Line(s, s.ActiveLayer, 0.4, 0.5, 0.6, 0.5);
            Line(s, s.ActiveLayer, 0.2, 0.1, 0.2, 0.9);
            Line(s, s.ActiveLayer, 0.8, 0.1, 0.8, 0.9);
            Select(s, line);
            var spy = new Spy(s);
            int steps = s.UndoCount;
            EntityId id = line.Id;
            StrokeStyle style = line.Style;
            s.ExtendSelection();
            Check.That(line.A == new Vec2(0.2, 0.5) && line.B == new Vec2(0.8, 0.5), $"both ends on the crossings exactly, got {line.A} {line.B}");
            Check.That(s.UndoCount == steps + 1 && s.NextUndoLabel == "Extend" && spy.Shapes == 1, "one 'Extend' step, one BoardChanged(Shapes)");
            Check.That(spy.LastToast == "Extended 2 ends", "toast " + spy.LastToast);
            Check.That(line.Id.Equals(id) && line.Style.Equals(style) && s.ActiveLayer.Shapes.Contains(line), "same id, style, layer");
            Check.That(s.SelectionCount == 1 && s.Selection.Ids.Contains(id) && s.ActiveTool == ToolId.SelectMove, "selection and tool kept");
            bool snap = false;
            foreach (var c in s.SnapCandidates) if (c.Position == new Vec2(0.8, 0.5)) snap = true;
            Check.That(snap, "the new end is a snap point (normal change path)");
        }

        public static void OneSideOnly_TheOtherEndStays()
        {
            var s = new EditorSession();
            var line = Line(s, s.ActiveLayer, 0.4, 0.5, 0.6, 0.5);
            Line(s, s.ActiveLayer, 0.8, 0.1, 0.8, 0.9);
            Select(s, line);
            var spy = new Spy(s);
            s.ExtendSelection();
            Check.That(line.A == new Vec2(0.4, 0.5), "nothing to the left (the board edge is not a boundary)");
            Check.That(line.B == new Vec2(0.8, 0.5), "right end on the line");
            Check.That(spy.LastToast == "Extended 1 end", "toast " + spy.LastToast);
        }

        public static void NothingInTheWay_NoStepAndAToast()
        {
            var s = new EditorSession();
            var line = Line(s, s.ActiveLayer, 0.4, 0.5, 0.6, 0.5);
            Line(s, s.ActiveLayer, 0.1, 0.8, 0.9, 0.8);       // parallel, never crossed
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.9, 0.5)));   // snap points and the grid are not boundaries
            Select(s, line);
            var spy = new Spy(s);
            int steps = s.UndoCount;
            s.ExtendSelection();
            Check.That(line.A == new Vec2(0.4, 0.5) && line.B == new Vec2(0.6, 0.5), "unchanged");
            Check.That(s.UndoCount == steps && spy.Shapes == 0, "no step, no event");
            Check.That(spy.LastToast == "Nothing in the way to extend to", "toast " + spy.LastToast);
            Check.That(s.SelectionCount == 1, "selection kept");
        }

        public static void StopsAtTheNearestOfSeveral()
        {
            var s = new EditorSession();
            var line = Line(s, s.ActiveLayer, 0.45, 0.5, 0.55, 0.5);
            Line(s, s.ActiveLayer, 0.9, 0.1, 0.9, 0.9);   // added first, further away
            Line(s, s.ActiveLayer, 0.7, 0.1, 0.7, 0.9);
            Line(s, s.ActiveLayer, 0.1, 0.1, 0.1, 0.9);
            Line(s, s.ActiveLayer, 0.3, 0.1, 0.3, 0.9);
            Select(s, line);
            s.ExtendSelection();
            Check.That(line.A == new Vec2(0.3, 0.5) && line.B == new Vec2(0.7, 0.5), $"the nearest on each side, got {line.A} {line.B}");
        }

        public static void CircleAndPolygonEdgesAreBoundaries()
        {
            var s = new EditorSession();
            // A line inside a circle reaches the circle on both sides.
            var c = new CircleShape(s.Board.NewId(), new Vec2(0.5, 0.5), 0.2, StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(c);
            var inside = Line(s, s.ActiveLayer, 0.45, 0.5, 0.55, 0.5);
            Select(s, inside);
            s.ExtendSelection();
            Check.Near(inside.A.X, 0.3, "left on the circle", 1e-12);
            Check.Near(inside.B.X, 0.7, "right on the circle", 1e-12);
            Check.That(inside.A.Y == 0.5 && inside.B.Y == 0.5, "stays horizontal exactly");

            // A line outside a square stamp stops at its near edge; the far side is free.
            var t = new EditorSession();
            var sq = new PolygonShape(t.Board.NewId(), new Vec2(0.5, 0.5), 4, 0.1, System.Math.PI / 4, StrokeStyle.Default);
            t.ActiveLayer.Shapes.Add(sq);
            var outside = Line(t, t.ActiveLayer, 0.1, 0.52, 0.2, 0.52);
            Select(t, outside);
            t.ExtendSelection();
            Vec2 v0 = SnapIndex.PolygonVertex(sq, 1), v1 = SnapIndex.PolygonVertex(sq, 2);   // the left edge
            double x = v0.X;   // vertical up to rounding
            Check.Near(outside.B.X, x, "right end on the square's left edge", 1e-12);
            Check.That(outside.B.Y == 0.52, "same y exactly");
            Check.That(outside.A == new Vec2(0.1, 0.52), "nothing to the left");
            Check.That(System.Math.Min(v0.Y, v1.Y) < 0.52 && System.Math.Max(v0.Y, v1.Y) > 0.52, "precondition: the edge spans y 0.52");
            // A polygon vertex hit exactly lands on that vertex.
            var u = new EditorSession();
            var tri = new PolygonShape(u.Board.NewId(), new Vec2(0.5, 0.5), 4, 0.2, 0, StrokeStyle.Default);   // diamond, vertex 2 at the left
            u.ActiveLayer.Shapes.Add(tri);
            Vec2 left = SnapIndex.PolygonVertex(tri, 2);
            var toVertex = Line(u, u.ActiveLayer, 0.05, left.Y, 0.1, left.Y);
            Select(u, toVertex);
            u.ExtendSelection();
            Check.That(toVertex.B == left, $"lands on the vertex exactly, got {toVertex.B} want {left}");
        }

        public static void AnEndOnALineGoesToTheNextOne()
        {
            var s = new EditorSession();
            var line = Line(s, s.ActiveLayer, 0.3, 0.5, 0.5, 0.5);
            Line(s, s.ActiveLayer, 0.5, 0.1, 0.5, 0.9);   // B already sits on it
            Line(s, s.ActiveLayer, 0.7, 0.1, 0.7, 0.9);
            Line(s, s.ActiveLayer, 0.3, 0.2, 0.3, 0.8);   // A already sits on it, nothing beyond
            Select(s, line);
            var spy = new Spy(s);
            s.ExtendSelection();
            Check.That(line.B == new Vec2(0.7, 0.5), $"B goes past the line it sits on to the next, got {line.B}");
            Check.That(line.A == new Vec2(0.3, 0.5), "A sits on a line with nothing beyond: stays");
            Check.That(spy.LastToast == "Extended 1 end", "toast " + spy.LastToast);

            // Lines meeting at a shared end (a V) do not stop each other there.
            var t = new EditorSession();
            var arm = Line(t, t.ActiveLayer, 0.5, 0.5, 0.6, 0.6);
            Line(t, t.ActiveLayer, 0.5, 0.5, 0.6, 0.4);
            Line(t, t.ActiveLayer, 0.2, 0.0, 0.2, 1.0);
            Select(t, arm);
            t.ExtendSelection();
            Check.Near(arm.A.X, 0.2, "A goes back past the V to the vertical line", 1e-12);
            Check.Near(arm.A.Y, 0.2, "on the diagonal", 1e-12);
        }

        public static void CollinearAndParallelLinesAreIgnored()
        {
            var s = new EditorSession();
            var line = Line(s, s.ActiveLayer, 0.4, 0.5, 0.5, 0.5);
            Line(s, s.ActiveLayer, 0.6, 0.5, 0.7, 0.5);   // collinear, ahead
            Line(s, s.ActiveLayer, 0.1, 0.5, 0.2, 0.5);   // collinear, behind
            Line(s, s.ActiveLayer, 0.45, 0.5, 0.48, 0.5); // collinear, overlapping
            Line(s, s.ActiveLayer, 0.9, 0.2, 0.9, 0.8);   // the real boundary ahead
            Line(s, s.ActiveLayer, 0.05, 0.2, 0.05, 0.8); // the real boundary behind
            Line(s, s.ActiveLayer, 0.95, 0.1, 0.95, 0.9); // parallel to nothing: further than 0.9
            Select(s, line);
            s.ExtendSelection();
            Check.That(line.B == new Vec2(0.9, 0.5), $"collinear lines ahead ignored, got {line.B}");
            Check.That(line.A == new Vec2(0.05, 0.5), $"collinear lines behind and overlapping ignored, got {line.A}");

            // A square stamp's edge collinear with the line is ignored too; its crossing edges count.
            var t = new EditorSession();
            var flat = Line(t, t.ActiveLayer, 0.1, 0.5, 0.2, 0.5);
            var sq = new PolygonShape(t.Board.NewId(), new Vec2(0.5, 0.5), 4, 0.1, 0, StrokeStyle.Default);   // diamond, vertices on y 0.5
            t.ActiveLayer.Shapes.Add(sq);
            Select(t, flat);
            t.ExtendSelection();
            Check.That(flat.B == SnapIndex.PolygonVertex(sq, 2), $"stops on the diamond's left vertex, got {flat.B}");
        }

        public static void HiddenLayersAreIgnored()
        {
            var s = new EditorSession();
            EntityId bottom = s.ActiveLayer.Id;
            var line = Line(s, s.ActiveLayer, 0.4, 0.5, 0.6, 0.5);
            EntityId top = s.AddLayer();
            Layer upper = s.Board.FindLayer(top);
            Line(s, upper, 0.7, 0.1, 0.7, 0.9);           // hidden below: ignored
            Line(s, s.Board.FindLayer(bottom), 0.9, 0.1, 0.9, 0.9);
            var hiddenLine = Line(s, upper, 0.65, 0.4, 0.75, 0.4);
            s.SetLayerVisible(top, false);
            Select(s, line, hiddenLine);
            Check.That(s.SelectionCount == 1, "precondition: a hidden layer's line cannot be selected");
            s.ExtendSelection();
            Check.That(line.B == new Vec2(0.9, 0.5), $"the hidden layer's line is not a boundary, got {line.B}");
            Check.That(hiddenLine.A == new Vec2(0.65, 0.4) && hiddenLine.B == new Vec2(0.75, 0.4), "hidden lines are not extended");
        }

        public static void MultiSelection_UsesTheBoardBeforeTheAction()
        {
            var s = new EditorSession();
            var h = Line(s, s.ActiveLayer, 0.4, 0.5, 0.6, 0.5);
            var v = Line(s, s.ActiveLayer, 0.3, 0.6, 0.3, 0.7);   // selected: it is a boundary for h only where it is now
            var circle = new CircleShape(s.Board.NewId(), new Vec2(0.5, 0.5), 0.05, StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(circle);
            Line(s, s.ActiveLayer, 0.1, 0.3, 0.9, 0.3);
            Line(s, s.ActiveLayer, 0.8, 0.1, 0.8, 0.9);
            Select(s, h, v, circle);
            var spy = new Spy(s);
            int steps = s.UndoCount;
            s.ExtendSelection();
            // h runs through the circle (x 0.45..0.55); both its ends are outside it, so the circle is behind them.
            Check.That(h.B == new Vec2(0.8, 0.5), $"h right end to x 0.8, got {h.B}");
            Check.That(h.A == new Vec2(0.4, 0.5), $"h left: v (y 0.6..0.7, before the action) is not in the way, got {h.A}");
            Check.That(v.A == new Vec2(0.3, 0.3), $"v bottom end on y 0.3, got {v.A}");
            Check.That(v.B == new Vec2(0.3, 0.7), "v top: nothing above");
            Check.That(circle.Center == new Vec2(0.5, 0.5) && circle.Radius == 0.05, "circles are not extended");
            Check.That(s.UndoCount == steps + 1 && spy.Shapes == 1 && spy.LastToast == "Extended 2 ends", "one step for all lines");
            Check.That(s.SelectionCount == 3, "selection kept");
        }

        public static void SelectedLinesAreBoundariesForEachOther()
        {
            var s = new EditorSession();
            var a = Line(s, s.ActiveLayer, 0.2, 0.5, 0.3, 0.5);
            var b = Line(s, s.ActiveLayer, 0.6, 0.4, 0.6, 0.6);
            Select(s, a, b);
            s.ExtendSelection();
            Check.That(a.B == new Vec2(0.6, 0.5), $"a stops on the selected line b, got {a.B}");
            Check.That(b.A == new Vec2(0.6, 0.4) && b.B == new Vec2(0.6, 0.6), "b has nothing in the way");
        }

        public static void UndoAndRedoRestoreExactly()
        {
            var s = new EditorSession();
            var line = Line(s, s.ActiveLayer, 0.41, 0.47, 0.63, 0.52);
            Line(s, s.ActiveLayer, 0.2, 0.05, 0.25, 0.95);
            var c = new CircleShape(s.Board.NewId(), new Vec2(0.75, 0.55), 0.07, StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(c);
            Select(s, line);
            Vec2 a0 = line.A, b0 = line.B;
            s.ExtendSelection();
            Vec2 a1 = FindLine(s, line.Id).A, b1 = FindLine(s, line.Id).B;
            Check.That(a1 != a0 && b1 != b0, "precondition: both ends moved");
            s.Undo();
            var back = FindLine(s, line.Id);
            Check.That(back.A == a0 && back.B == b0, "Undo restores the ends bit for bit");
            Check.That(s.SelectionCount == 1 && s.Selection.Ids.Contains(line.Id), "and the selection");
            s.Redo();
            var again = FindLine(s, line.Id);
            Check.That(again.A == a1 && again.B == b1, "Redo restores the extended ends bit for bit");
            Check.That(s.NextUndoLabel == "Extend", "the step is 'Extend'");
        }

        public static void ExtendingTwiceIsANoOp_SlantedAndCurvedBoundaries()
        {
            var s = new EditorSession();
            var line = Line(s, s.ActiveLayer, 0.43, 0.41, 0.52, 0.47);
            Line(s, s.ActiveLayer, 0.11, 0.93, 0.29, 0.07);   // slanted wall behind A
            var c = new CircleShape(s.Board.NewId(), new Vec2(0.81, 0.62), 0.13, StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(c);                        // B runs into the circle, whose far side lies beyond
            var poly = new PolygonShape(s.Board.NewId(), new Vec2(0.5, 0.5), 7, 0.45, 0.3, StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(poly);                     // a heptagon around everything: the last boundary
            Select(s, line);
            var spy = new Spy(s);
            int guard = 0;
            int before;
            do
            {
                before = s.UndoCount;
                s.ExtendSelection();
            } while (s.UndoCount > before && ++guard < 10);
            Check.That(guard >= 1 && guard < 10, $"repeated Extend settles (ends step over each boundary once), {guard} steps");
            Vec2 a = line.A, b = line.B;
            int steps = s.UndoCount, events = spy.Shapes;
            s.ExtendSelection();
            Check.That(line.A == a && line.B == b && s.UndoCount == steps && spy.Shapes == events, "one more Extend: no-op, no step");
            Check.That(spy.LastToast == "Nothing in the way to extend to", "toast " + spy.LastToast);
        }

        static LineShape FindLine(EditorSession s, EntityId id)
        {
            foreach (var layer in s.Board.Layers)
                foreach (var sh in layer.Shapes)
                    if (sh is LineShape l && l.Id.Equals(id)) return l;
            return null;
        }
    }
}
