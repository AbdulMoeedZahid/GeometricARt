using System;
using System.Collections.Generic;
using System.Reflection;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER (Extend action, written from the spec before the builder's code). For each selected LineShape (circles and
    /// polygons in the selection are ignored) each end extends independently along the line's direction to the FIRST
    /// drawn thing in the way (any other line, circle or polygon edge on a visible layer, other selected shapes included);
    /// the new end is the exact intersection; nothing in the way = unchanged (the board edge and grid/snap guides are not
    /// boundaries); hits at or behind the end are ignored (an end on a line goes on to the next one); collinear boundaries
    /// are ignored; hidden layers neither extend nor bound. One "Extend" step with one BoardChanged(Shapes); no move = no
    /// step + "Nothing in the way to extend to"; else "Extended N ends". Selection, id, style and layer are kept.
    /// API GUESS: a public parameterless EditorSession method whose name contains "Extend" (ExtendSelection), called by
    /// reflection so this file compiles whatever it is named.
    /// </summary>
    public static class VerifierExtendChecks
    {
        const string Label = "Extend", NothingToast = "Nothing in the way to extend to";

        static Vec2 V(double x, double y) => new Vec2(x, y);
        static string P(Vec2 v) => $"({v.X:R}, {v.Y:R})";
        static int s_id = 7000;

        static MethodInfo ExtendMethod()
        {
            foreach (var m in typeof(EditorSession).GetMethods(BindingFlags.Instance | BindingFlags.Public))
                if (m.Name.Contains("Extend") && m.GetParameters().Length == 0 && m.ReturnType == typeof(void)) return m;
            throw new Exception("no public parameterless EditorSession.*Extend* method (API guess: ExtendSelection())");
        }

        sealed class Run
        {
            public EditorSession S;
            public Layer L;
            public readonly List<string> Toasts = new List<string>();
            public int Boards;
            public BoardChangeKind Kind;
            public void Extend()
            {
                Toasts.Clear();
                Boards = 0;
                ExtendMethod().Invoke(S, null);
            }
        }

        static LineShape Ln(Layer l, double ax, double ay, double bx, double by)
        {
            var s = new LineShape(new EntityId(++s_id), V(ax, ay), V(bx, by), new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted));
            l.Shapes.Add(s);
            return s;
        }

        /// <summary>A session on an empty 2x2 board; <paramref name="build"/> adds shapes; the given shapes are selected.</summary>
        static Run Make(Action<Board, Layer> build, Func<Layer, Shape[]> select)
        {
            var b = GoldenBoards.Empty();
            var layer = b.Layers[0];
            build(b, layer);
            var r = new Run { S = new EditorSession(b), L = layer };
            r.S.Toast += m => r.Toasts.Add(m);
            r.S.BoardChanged += k => { r.Boards++; r.Kind = k; };
            r.S.SetTool(ToolId.SelectMove);
            var ids = new List<EntityId>();
            foreach (var sh in select(layer)) ids.Add(sh.Id);
            r.S.SetSelection(ids);
            return r;
        }

        static void ExpectStep(Run r, int ends, string what)
        {
            Check.That(r.S.UndoCount == 1 && r.S.NextUndoLabel == Label, $"{what}: one \"Extend\" step, got {r.S.UndoCount} {r.S.NextUndoLabel}");
            Check.That(r.Boards == 1 && r.Kind == BoardChangeKind.Shapes, $"{what}: one BoardChanged(Shapes), got {r.Boards} {r.Kind}");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0].StartsWith("Extended " + ends + " end"), $"{what}: toast 'Extended {ends} end(s)', got {string.Join(";", r.Toasts)}");
        }

        static void ExpectNothing(Run r, string what)
        {
            Check.That(r.S.UndoCount == 0, $"{what}: no step, got {r.S.UndoCount}");
            Check.That(r.Boards == 0, $"{what}: no BoardChanged, got {r.Boards}");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == NothingToast, $"{what}: '{NothingToast}', got {string.Join(";", r.Toasts)}");
        }

        static void Near(Vec2 got, Vec2 exp, string what, double eps = 1e-12) =>
            Check.That(Vec2.Distance(got, exp) <= eps, $"{what}: expected {P(exp)}, got {P(got)}");

        static double DistToLine(Vec2 p, Vec2 a, Vec2 b) =>
            Math.Abs((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X)) / Vec2.Distance(a, b);

        // ================= sides =================

        public static void V_BothSidesExtendToTheWallsExactly()
        {
            LineShape line = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.4, 0.5, 0.6, 0.5);
                Ln(l, 0.2, 0.1, 0.2, 0.9);   // left wall x = 0.2
                Ln(l, 0.85, 0.1, 0.85, 0.9); // right wall x = 0.85
            }, l => new Shape[] { line });
            var id = line.Id;
            var style = line.Style;
            r.Extend();
            Near(line.A, V(0.2, 0.5), "A to the left wall");
            Near(line.B, V(0.85, 0.5), "B to the right wall");
            ExpectStep(r, 2, "both sides");
            Check.That(line.Id == id && line.Style.Equals(style) && r.L.Shapes.Contains(line), "same id, style and layer (edited in place)");
            Check.That(r.S.SelectionCount == 1 && r.S.Selection.Ids.Contains(id) && r.S.ActiveTool == ToolId.SelectMove, "selection and tool kept");
        }

        public static void V_OneSideOnly()
        {
            LineShape line = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.4, 0.5, 0.6, 0.5);
                Ln(l, 0.85, 0.1, 0.85, 0.9);   // only on the B side
            }, l => new Shape[] { line });
            r.Extend();
            Check.That(line.A == V(0.4, 0.5), $"nothing on the A side (the board edge is no boundary): unchanged, got {P(line.A)}");
            Near(line.B, V(0.85, 0.5), "B to the wall");
            ExpectStep(r, 1, "one side");
        }

        public static void V_NothingInTheWay()
        {
            LineShape line = null;
            var r = Make((b, l) =>
            {
                b.GridN = 4;                                   // grid lines and grid dots are not boundaries
                line = Ln(l, 0.4, 0.55, 0.6, 0.55);
                l.SnapPoints.Add(new SnapPoint(b.NewId(), V(0.8, 0.55)));   // a user point on the extension: not a boundary
                Ln(l, 0.3, 0.8, 0.7, 0.9);                     // a line nowhere near the extension
            }, l => new Shape[] { line });
            r.Extend();
            Check.That(line.A == V(0.4, 0.55) && line.B == V(0.6, 0.55), "unchanged");
            ExpectNothing(r, "nothing in the way");
        }

        public static void V_EmptyOrNonLineSelectionExtendsNothing()
        {
            var r = Make((b, l) =>
            {
                l.Shapes.Add(new CircleShape(new EntityId(++s_id), V(0.5, 0.5), 0.1, StrokeStyle.Default));
                l.Shapes.Add(new PolygonShape(new EntityId(++s_id), V(0.2, 0.2), 5, 0.05, 0, StrokeStyle.Default));
                Ln(l, 0.1, 0.9, 0.9, 0.9);
            }, l => new Shape[] { l.Shapes[0], l.Shapes[1] });
            var c = (CircleShape)r.L.Shapes[0];
            r.Extend();
            Check.That(c.Radius == 0.1 && c.Center == V(0.5, 0.5), "a selected circle is ignored (never changed)");
            ExpectNothing(r, "circle + polygon only");
        }

        // ================= which boundary =================

        public static void V_NearestOfSeveralWins()
        {
            LineShape line = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.3, 0.5, 0.4, 0.5);
                Ln(l, 0.9, 0.1, 0.9, 0.9);   // farther (added first)
                Ln(l, 0.7, 0.1, 0.7, 0.9);   // nearest
                Ln(l, 0.8, 0.1, 0.8, 0.9);
                Ln(l, 0.65, 0.6, 0.75, 0.7); // misses the extension (above it)
            }, l => new Shape[] { line });
            r.Extend();
            Near(line.B, V(0.7, 0.5), "the FIRST thing in the way, not the first in the list");
        }

        public static void V_CircleBoundaries()
        {
            LineShape outside = null, inside = null;
            var r = Make((b, l) =>
            {
                l.Shapes.Add(new CircleShape(new EntityId(++s_id), V(0.6, 0.5), 0.15, StrokeStyle.Default));
                outside = Ln(l, 0.1, 0.5, 0.2, 0.5);    // B extends into the circle: stops at its near side x = 0.45
                inside = Ln(l, 0.55, 0.6, 0.6, 0.6);    // inside the circle: both ends stop on the circle
            }, l => new Shape[] { outside, inside });
            r.Extend();
            Check.That(outside.A == V(0.1, 0.5), "nothing to the left: unchanged");
            Near(outside.B, V(0.45, 0.5), "the circle's near side");
            double dx = Math.Sqrt(0.15 * 0.15 - 0.1 * 0.1);
            Near(inside.A, V(0.6 - dx, 0.6), "inside: A to the circle");
            Near(inside.B, V(0.6 + dx, 0.6), "inside: B to the circle");
            ExpectStep(r, 3, "circles");
        }

        public static void V_PolygonEdgeBoundary()
        {
            LineShape line = null;
            PolygonShape poly = null;
            var r = Make((b, l) =>
            {
                poly = new PolygonShape(new EntityId(++s_id), V(0.7, 0.5), 6, 0.12, 0.2, StrokeStyle.Default);
                l.Shapes.Add(poly);
                line = Ln(l, 0.2, 0.47, 0.3, 0.48);    // slightly sloped, aimed at the hexagon
            }, l => new Shape[] { line });
            r.Extend();
            // Expected: the nearest crossing of the ray with the six edges (vertices as SnapIndex.PolygonVertex).
            Vec2 a = V(0.2, 0.47), dir = V(0.1, 0.01);
            double best = double.MaxValue;
            for (int k = 0; k < 6; k++)
            {
                Vec2 p0 = SnapIndex.PolygonVertex(poly, k), p1 = SnapIndex.PolygonVertex(poly, (k + 1) % 6);
                if (Intersect.LineSegment(a, dir, p0, p1, out Vec2 x) != 1) continue;
                double t = ((x.X - a.X) * dir.X + (x.Y - a.Y) * dir.Y);
                if (t > 0.011 && t < best) best = t;
            }
            Check.That(best < double.MaxValue, "precondition: the ray meets the hexagon");
            Vec2 exp = V(a.X + dir.X * best / 0.0101, a.Y + dir.Y * best / 0.0101);
            Near(line.B, exp, "the polygon's near edge", 1e-9);
            Check.That(DistToLine(line.B, V(0.2, 0.47), V(0.3, 0.48)) < 1e-12, "the new end is still on the line's own line");
            Check.That(line.A == V(0.2, 0.47), "nothing on the A side");
        }

        public static void V_ExactIntersectionInGeneralPosition()
        {
            LineShape line = null, wall = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.13, 0.27, 0.31, 0.41);
                wall = Ln(l, 0.62, 0.11, 0.57, 0.93);
            }, l => new Shape[] { line });
            r.Extend();
            Check.That(DistToLine(line.B, V(0.13, 0.27), V(0.31, 0.41)) < 1e-12, $"on its own line, got {P(line.B)}");
            Check.That(DistToLine(line.B, wall.A, wall.B) < 1e-12, "on the wall");
            Check.That(Intersect.SegmentSegment(line.A, line.B, wall.A, wall.B, out Vec2 x) >= 1 && Vec2.Distance(x, line.B) < 1e-12,
                "Intersect agrees: the new end is the crossing");
            // The new end must be a snap candidate the next stroke can hit exactly (an Intersection or an Endpoint).
            bool candidate = false;
            foreach (var c in r.S.SnapCandidates) if (c.Position == line.B) candidate = true;
            Check.That(candidate, "the extended end is exactly a snap candidate (==)");
        }

        // ================= next-beyond, collinear, hidden =================

        public static void V_EndOnALineGoesToTheNextBeyond()
        {
            LineShape line = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.3, 0.5, 0.5, 0.5);
                Ln(l, 0.5, 0.2, 0.5, 0.8);     // B is ON this line
                Ln(l, 0.75, 0.2, 0.75, 0.8);   // next beyond
                Ln(l, 0.3, 0.2, 0.3, 0.8);     // A is on this one; nothing beyond on the left
            }, l => new Shape[] { line });
            r.Extend();
            Near(line.B, V(0.75, 0.5), "B passes its own wall to the next");
            Check.That(line.A == V(0.3, 0.5), "A on a wall with nothing beyond: unchanged");
            ExpectStep(r, 1, "next beyond");
        }

        public static void V_EndOnACircleGoesToItsFarSide()
        {
            LineShape line = null;
            var r = Make((b, l) =>
            {
                l.Shapes.Add(new CircleShape(new EntityId(++s_id), V(0.6, 0.5), 0.15, StrokeStyle.Default));
                line = Ln(l, 0.3, 0.5, 0.45, 0.5);   // B on the circle's near side
            }, l => new Shape[] { line });
            r.Extend();
            Near(line.B, V(0.75, 0.5), "on the near side: on to the far side");
        }

        public static void V_CollinearBoundariesAreIgnored()
        {
            LineShape line = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.2, 0.5, 0.4, 0.5);
                Ln(l, 0.5, 0.5, 0.6, 0.5);     // collinear segment ahead
                Ln(l, 0.4, 0.5, 0.45, 0.5);    // collinear, touching B
                Ln(l, 0.8, 0.2, 0.8, 0.8);     // the real boundary
            }, l => new Shape[] { line });
            r.Extend();
            Near(line.B, V(0.8, 0.5), "collinear pieces are not boundaries");
            // Sloped: rounding makes the collinear piece's cross product tiny but not zero.
            LineShape s2 = null;
            var r2 = Make((b, l) =>
            {
                s2 = Ln(l, 0.1, 0.2, 0.3, 0.5);
                Ln(l, 0.5, 0.8, 0.6, 0.95);   // on the same line (slope 1.5), ahead of B
                Ln(l, 0.0, 0.98, 1.0, 0.98);  // the real boundary
            }, l => new Shape[] { s2 });
            r2.Extend();
            Near(s2.B, V(0.1 + 0.78 / 1.5, 0.98), "sloped collinear piece ignored: on to the real boundary", 1e-9);
        }

        public static void V_HiddenLayersNeitherBoundNorExtend()
        {
            LineShape line = null, hiddenLine = null;
            Layer hidden = null;
            var b0 = GoldenBoards.Empty();
            line = Ln(b0.Layers[0], 0.3, 0.5, 0.4, 0.5);
            Ln(b0.Layers[0], 0.9, 0.2, 0.9, 0.8);                       // visible boundary, farther
            hidden = new Layer(b0.NewId(), "hidden");
            b0.Layers.Add(hidden);
            Ln(hidden, 0.6, 0.2, 0.6, 0.8);                             // nearer, but hidden
            hiddenLine = Ln(hidden, 0.1, 0.3, 0.2, 0.3);
            var s = new EditorSession(b0);
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { line.Id, hiddenLine.Id });
            s.SetLayerVisible(hidden.Id, false);
            var steps = s.UndoCount;
            ExtendMethod().Invoke(s, null);
            Near(line.B, V(0.9, 0.5), "the hidden wall is no boundary");
            Check.That(hiddenLine.A == V(0.1, 0.3) && hiddenLine.B == V(0.2, 0.3), "a line on a hidden layer is never extended");
            Check.That(s.UndoCount == steps + 1, "one step");
        }

        // ================= multi-selection, undo =================

        public static void V_MultiSelectionUsesSelectedShapesAsBoundaries()
        {
            // H crosses the board; V is selected too and lies across H's B-side path in its ORIGINAL span, so the
            // order in which the two are extended cannot matter.
            LineShape h = null, v = null;
            var r = Make((b, l) =>
            {
                h = Ln(l, 0.2, 0.5, 0.3, 0.5);
                v = Ln(l, 0.6, 0.3, 0.6, 0.7);
                Ln(l, 0.1, 0.1, 0.9, 0.1);     // floor
                Ln(l, 0.1, 0.9, 0.9, 0.9);     // ceiling
            }, l => new Shape[] { h, v });
            r.Extend();
            Near(h.B, V(0.6, 0.5), "H stops at the selected V");
            Check.That(h.A == V(0.2, 0.5), "nothing left of H");
            Near(v.A, V(0.6, 0.1), "V down to the floor");
            Near(v.B, V(0.6, 0.9), "V up to the ceiling");
            ExpectStep(r, 3, "multi");
            Check.That(r.S.SelectionCount == 2, "both stay selected");
        }

        public static void V_UndoRedoAreExact()
        {
            LineShape a = null, b2 = null;
            var r = Make((b, l) =>
            {
                a = Ln(l, 0.13, 0.27, 0.31, 0.41);
                b2 = Ln(l, 0.7, 0.2, 0.72, 0.3);
                Ln(l, 0.62, 0.11, 0.57, 0.93);
                Ln(l, 0.05, 0.95, 0.95, 0.95);
                l.Shapes.Add(new CircleShape(new EntityId(++s_id), V(0.3, 0.2), 0.15, StrokeStyle.Default));
            }, l => new Shape[] { a, b2 });
            Vec2 a0 = a.A, a1 = a.B, b0 = b2.A, b1 = b2.B;
            r.Extend();
            var layerId = r.L.Id;
            Shape Find(EntityId id) { foreach (var sh in r.S.Board.FindLayer(layerId).Shapes) if (sh.Id == id) return sh; return null; }
            var ea = (LineShape)Find(a.Id);
            var eb = (LineShape)Find(b2.Id);
            Vec2 xa0 = ea.A, xa1 = ea.B, xb0 = eb.A, xb1 = eb.B;
            Check.That(!(xa0 == a0 && xa1 == a1), "precondition: something moved");
            r.S.Undo();
            var ua = (LineShape)Find(a.Id);
            var ub = (LineShape)Find(b2.Id);
            Check.That(ua.A == a0 && ua.B == a1 && ub.A == b0 && ub.B == b1, "Undo restores every end bit for bit");
            Check.That(r.S.SelectionCount == 2, "selection restored (Select tool)");
            r.S.Redo();
            var ra = (LineShape)Find(a.Id);
            var rb = (LineShape)Find(b2.Id);
            Check.That(ra.A == xa0 && ra.B == xa1 && rb.A == xb0 && rb.B == xb1, "Redo re-applies bit for bit");
            Check.That(r.S.NextUndoLabel == Label, "label Extend");
        }

        public static void V_AxisAlignedWallGivesABitExactEnd()
        {
            // "The new endpoint is the exact intersection point": against an axis-aligned wall the exact point is
            // representable, so it should come out bit for bit (a later snap or Extend then sees it ON the wall).
            LineShape line = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.4, 0.5, 0.6, 0.5);
                Ln(l, 0.85, 0.1, 0.85, 0.9);
                Ln(l, 0.1, 0.3, 0.9, 0.3);
                Ln(l, 0.1, 0.1, 0.1, 0.9);
            }, l => new Shape[] { line });
            r.Extend();
            Check.That(line.B == V(0.85, 0.5) && line.A == V(0.1, 0.5), $"bit-exact on axis-aligned walls, got {P(line.A)} {P(line.B)}");
        }

        public static void V_ExtendingTwiceIsANoOp()
        {
            LineShape line = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.4, 0.5, 0.6, 0.5);
                Ln(l, 0.2, 0.1, 0.2, 0.9);
                Ln(l, 0.85, 0.1, 0.85, 0.9);
            }, l => new Shape[] { line });
            r.Extend();
            r.Extend();
            // Both ends now sit ON their walls (within rounding); "at or behind" is ignored, so each looks beyond: nothing there.
            Vec2 a1 = line.A, b1 = line.B;
            Near(a1, V(0.2, 0.5), "precondition: on the left wall");
            r.Extend();
            Check.That(line.A == a1 && line.B == b1, "a third Extend: still bit-identical");
            Check.That(r.S.UndoCount == 1, "no second step");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == NothingToast, "nothing-in-the-way toast");
        }

        public static void V_ExtendDuringAnOpenMoveDragDoesNotCorrupt()
        {
            // Not stated: the action bar is UI, so on device a drag is cancelled first. Engine-only: extending during an
            // open Move gesture must end that gesture first (no ghost left, the late Up commits nothing extra).
            LineShape line = null;
            var r = Make((b, l) =>
            {
                line = Ln(l, 0.4, 0.5, 0.6, 0.5);
                Ln(l, 0.85, 0.1, 0.85, 0.9);
            }, l => new Shape[] { line });
            r.S.SetTool(ToolId.Move);
            r.S.Pointer(new PointerEvent(PointerPhase.Down, V(0.6, 0.5), true, 1));
            r.S.Pointer(new PointerEvent(PointerPhase.Move, V(0.6, 0.6), true, 1.1));
            r.Extend();
            Check.That(!r.S.GestureActive, "the open Move drag was ended");
            r.S.Pointer(new PointerEvent(PointerPhase.Up, V(0.6, 0.6), true, 1.2));
            Check.That(line.A.Y == 0.5 && line.B.Y == 0.5, $"the line was not moved by the cancelled drag, got {P(line.A)} {P(line.B)}");
        }
    }
}
