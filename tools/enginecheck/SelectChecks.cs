using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>Select tool: SelectionGeometry (bounds, enclosure, stroke distance, pick) and the marquee/tap gesture.</summary>
    public static class SelectChecks
    {
        static PointerEvent Ev(PointerPhase phase, double x, double y, bool onBoard = true, double t = 0) =>
            new PointerEvent(phase, new Vec2(x, y), onBoard, t);

        static void Drag(EditorSession s, double ax, double ay, double bx, double by, int moves = 5)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= moves; i++)
            {
                double k = (double)i / moves;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * k, ay + (by - ay) * k));
            }
            s.Pointer(Ev(PointerPhase.Up, bx, by));
        }

        static void Tap(EditorSession s, double x, double y)
        {
            s.Pointer(Ev(PointerPhase.Down, x, y));
            s.Pointer(Ev(PointerPhase.Up, x, y));
        }

        static int ShapeCount(EditorSession s)
        {
            int n = 0;
            foreach (var l in s.Board.Layers) n += l.Shapes.Count;
            return n;
        }

        static readonly StrokeStyle St = StrokeStyle.Default;

        static LineShape AddLine(Board b, Layer l, double ax, double ay, double bx, double by)
        {
            var line = new LineShape(b.NewId(), new Vec2(ax, ay), new Vec2(bx, by), St);
            l.Shapes.Add(line);
            return line;
        }

        static CircleShape AddCircle(Board b, Layer l, double cx, double cy, double r)
        {
            var c = new CircleShape(b.NewId(), new Vec2(cx, cy), r, St);
            l.Shapes.Add(c);
            return c;
        }

        /// <summary>
        /// Two layers. Bottom: line (0.1,0.1)-(0.3,0.3), circle c (0.7,0.2) r 0.1. Top: line (0.1,0.6)-(0.4,0.6), circle
        /// c (0.7,0.7) r 0.15. Top is active. Snapping off so raw positions are used as given.
        /// </summary>
        sealed class Fixture
        {
            public readonly EditorSession S;
            public readonly Layer Bottom, Top;
            public readonly LineShape L1, L2;
            public readonly CircleShape C1, C2;
            public readonly List<string> Toasts = new List<string>();
            public int States, BoardChanges;

            public Fixture()
            {
                var b = new Board();
                Bottom = new Layer(b.NewId(), "Layer 1");
                Top = new Layer(b.NewId(), "Layer 2");
                b.Layers.Add(Bottom);
                b.Layers.Add(Top);
                b.ActiveLayerId = Top.Id;
                L1 = AddLine(b, Bottom, 0.1, 0.1, 0.3, 0.3);
                C1 = AddCircle(b, Bottom, 0.7, 0.2, 0.1);
                L2 = AddLine(b, Top, 0.1, 0.6, 0.4, 0.6);
                C2 = AddCircle(b, Top, 0.7, 0.7, 0.15);
                S = new EditorSession(b);
                S.SetSnapEnabled(false);
                S.SetTool(ToolId.SelectMove);
                S.Toast += t => Toasts.Add(t);
                S.StateChanged += () => States++;
                S.BoardChanged += _ => BoardChanges++;
            }

            public bool Selected(params Shape[] shapes)
            {
                if (S.Selection.Ids.Count != shapes.Length) return false;
                foreach (var sh in shapes)
                    if (!S.Selection.Ids.Contains(sh.Id)) return false;
                return true;
            }

            public string Last => Toasts.Count == 0 ? "<none>" : Toasts[Toasts.Count - 1];
        }

        // ---------- geometry ----------

        public static void RectFromCornersAnyOrder()
        {
            var a = new Vec2(0.7, 0.2);
            var b = new Vec2(0.1, 0.9);
            foreach (var r in new[] { BoardRect.FromCorners(a, b), BoardRect.FromCorners(b, a),
                                      BoardRect.FromCorners(new Vec2(0.1, 0.2), new Vec2(0.7, 0.9)),
                                      BoardRect.FromCorners(new Vec2(0.7, 0.9), new Vec2(0.1, 0.2)) })
            {
                Check.That(r.MinX == 0.1 && r.MinY == 0.2 && r.MaxX == 0.7 && r.MaxY == 0.9, $"normalised {r}");
                Check.Near(r.Width, 0.6, "width", 1e-12);
                Check.Near(r.Height, 0.7, "height", 1e-12);
                Check.That(r.IsFinite, "finite");
            }
            Check.That(!new BoardRect(0, 0, double.NaN, 1).IsFinite, "NaN rect is not finite");
            Check.That(!new BoardRect(0, 0, double.PositiveInfinity, 1).IsFinite, "infinite rect is not finite");
            var u = BoardRect.Union(new BoardRect(0.1, 0.5, 0.2, 0.6), new BoardRect(0.4, 0.2, 0.5, 0.3));
            Check.That(u.MinX == 0.1 && u.MinY == 0.2 && u.MaxX == 0.5 && u.MaxY == 0.6, $"union {u}");
        }

        public static void EnclosureIsWholeShapeAndEdgeInclusive()
        {
            var r = new BoardRect(0.2, 0.2, 0.8, 0.8);
            var inside = new LineShape(new EntityId(1), new Vec2(0.3, 0.3), new Vec2(0.7, 0.6), St);
            var partial = new LineShape(new EntityId(2), new Vec2(0.3, 0.3), new Vec2(0.9, 0.6), St);
            var onEdge = new LineShape(new EntityId(3), new Vec2(0.2, 0.2), new Vec2(0.8, 0.8), St);
            var justOut = new LineShape(new EntityId(4), new Vec2(0.2 - 1e-6, 0.3), new Vec2(0.5, 0.5), St);
            Check.That(SelectionGeometry.IsEnclosed(inside, r), "line inside");
            Check.That(!SelectionGeometry.IsEnclosed(partial, r), "line partly outside");
            Check.That(SelectionGeometry.IsEnclosed(onEdge, r), "endpoints on the corners count (inclusive)");
            Check.That(!SelectionGeometry.IsEnclosed(justOut, r), "1e-6 outside is outside");

            var touching = new CircleShape(new EntityId(5), new Vec2(0.5, 0.5), 0.3, St);   // touches all four sides
            var bulging = new CircleShape(new EntityId(6), new Vec2(0.5, 0.5), 0.3 + 1e-6, St);
            var offCentre = new CircleShape(new EntityId(7), new Vec2(0.7, 0.5), 0.15, St);   // right edge at 0.85
            Check.That(SelectionGeometry.IsEnclosed(touching, r), "circle touching every side is enclosed");
            Check.That(!SelectionGeometry.IsEnclosed(bulging, r), "circle 1e-6 over is not");
            Check.That(!SelectionGeometry.IsEnclosed(offCentre, r), "centre inside is not enough");

            var square = new PolygonShape(new EntityId(8), new Vec2(0.5, 0.5), 4, 0.3, Math.PI / 4, St);   // half-side 0.212
            var diamond = new PolygonShape(new EntityId(9), new Vec2(0.5, 0.5), 4, 0.31, 0, St);           // tips at 0.19/0.81
            Check.That(SelectionGeometry.IsEnclosed(square, r), "polygon whose vertices are all inside");
            Check.That(!SelectionGeometry.IsEnclosed(diamond, r), "polygon with tips outside");
        }

        public static void DegenerateShapesAreNeverEnclosedOrPicked()
        {
            var r = new BoardRect(0, 0, 1, 1);
            var bad = new Shape[]
            {
                null,
                new LineShape(new EntityId(1), new Vec2(double.NaN, 0.5), new Vec2(0.5, 0.5), St),
                new LineShape(new EntityId(2), new Vec2(0.5, 0.5), new Vec2(double.PositiveInfinity, 0.5), St),
                new CircleShape(new EntityId(3), new Vec2(0.5, 0.5), 0, St),
                new CircleShape(new EntityId(4), new Vec2(0.5, 0.5), -0.1, St),
                new CircleShape(new EntityId(5), new Vec2(0.5, 0.5), double.NaN, St),
                new CircleShape(new EntityId(6), new Vec2(0.5, 0.5), double.PositiveInfinity, St),
                new CircleShape(new EntityId(7), new Vec2(double.NaN, 0.5), 0.1, St),
                new PolygonShape(new EntityId(8), new Vec2(0.5, 0.5), 2, 0.1, 0, St),
                new PolygonShape(new EntityId(9), new Vec2(0.5, 0.5), 4, 0, 0, St),
                new PolygonShape(new EntityId(10), new Vec2(0.5, 0.5), 4, 0.1, double.NaN, St),
            };
            foreach (var s in bad)
            {
                Check.That(!SelectionGeometry.TryGetBounds(s, out _), $"no bounds for {s?.Kind.ToString() ?? "null"} {s?.Id}");
                Check.That(!SelectionGeometry.IsEnclosed(s, r), $"never enclosed {s?.Id}");
                Check.That(double.IsPositiveInfinity(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.5), s)),
                    $"infinitely far {s?.Id}");
            }
            var ok = new LineShape(new EntityId(11), new Vec2(0.2, 0.2), new Vec2(0.4, 0.4), St);
            Check.That(!SelectionGeometry.IsEnclosed(ok, new BoardRect(0, 0, double.NaN, 1)), "a NaN rectangle encloses nothing");
            var point = new LineShape(new EntityId(12), new Vec2(0.4, 0.4), new Vec2(0.4, 0.4), St);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.4, 0.43), point), 0.03, "zero-length line: point distance");
        }

        public static void DistanceToStrokeByKind()
        {
            var line = new LineShape(new EntityId(1), new Vec2(0.2, 0.5), new Vec2(0.8, 0.5), St);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.52), line), 0.02, "perpendicular", 1e-12);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.1, 0.5), line), 0.1, "beyond the end: to the end", 1e-12);
            var circle = new CircleShape(new EntityId(2), new Vec2(0.5, 0.5), 0.2, St);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.5), circle), 0.2, "centre is r from the ring", 1e-12);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.75, 0.5), circle), 0.05, "outside the ring", 1e-12);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.69), circle), 0.01, "inside near the ring", 1e-12);
            var square = new PolygonShape(new EntityId(3), new Vec2(0.5, 0.5), 4, Math.Sqrt(2) * 0.1, Math.PI / 4, St);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.5), square), 0.1, "polygon: nearest edge", 1e-9);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.65, 0.5), square), 0.05, "polygon: outside an edge", 1e-9);
        }

        public static void CollectEnclosedUsesVisibleLayersOnly()
        {
            var f = new Fixture();
            var into = new HashSet<EntityId> { new EntityId(999) };
            SelectionGeometry.CollectEnclosed(f.S.Board, new BoardRect(0, 0, 1, 1), into);
            Check.That(into.Count == 4 && !into.Contains(new EntityId(999)), "cleared first, then all four shapes");
            f.Bottom.Visible = false;
            SelectionGeometry.CollectEnclosed(f.S.Board, new BoardRect(0, 0, 1, 1), into);
            Check.That(into.Count == 2 && into.Contains(f.L2.Id) && into.Contains(f.C2.Id), "hidden layer skipped");
            SelectionGeometry.CollectEnclosed(null, new BoardRect(0, 0, 1, 1), into);
            Check.That(into.Count == 0, "null board: empty");
        }

        public static void TryPickPrefersNearestThenTopmost()
        {
            var b = new Board();
            var lo = new Layer(b.NewId(), "lo");
            var hi = new Layer(b.NewId(), "hi");
            b.Layers.Add(lo);
            b.Layers.Add(hi);
            var a = AddLine(b, lo, 0.2, 0.5, 0.8, 0.5);
            var same1 = AddLine(b, hi, 0.2, 0.5, 0.8, 0.5);   // same stroke, higher layer
            var same2 = AddLine(b, hi, 0.2, 0.5, 0.8, 0.5);   // same stroke, later in the same layer
            var near = AddLine(b, lo, 0.2, 0.51, 0.8, 0.51);
            Check.That(SelectionGeometry.TryPick(b, new Vec2(0.5, 0.5), 0.02, out EntityId id) && id == same2.Id,
                $"exact tie: topmost layer, last shape wins (got {id})");
            Check.That(SelectionGeometry.TryPick(b, new Vec2(0.5, 0.509), 0.02, out id) && id == near.Id,
                "strictly nearer wins even from a lower layer");
            hi.Visible = false;
            Check.That(SelectionGeometry.TryPick(b, new Vec2(0.5, 0.5), 0.02, out id) && id == a.Id, "hidden layer skipped");
            Check.That(!SelectionGeometry.TryPick(b, new Vec2(0.5, 0.6), 0.02, out id) && id.IsNone, "nothing in range");
            Check.That(SelectionGeometry.TryPick(b, new Vec2(0.5, 0.529), 0.02, out id) && id == near.Id, "within the tolerance");
            Check.That(!SelectionGeometry.TryPick(b, new Vec2(0.5, 0.5), double.NaN, out _), "NaN tolerance picks nothing");
            Check.That(!SelectionGeometry.TryPick(null, new Vec2(0.5, 0.5), 0.02, out _), "null board");
            Check.That(same1.Id != same2.Id, "distinct ids");
        }

        // ---------- tool switching ----------

        public static void SetToolSelectAcceptedWithHintAndOneState()
        {
            var s = new EditorSession();
            var toasts = new List<string>();
            int states = 0;
            s.Toast += t => toasts.Add(t);
            s.StateChanged += () => states++;
            s.SetTool(ToolId.SelectMove);
            Check.That(s.ActiveTool == ToolId.SelectMove, "SelectMove accepted");
            Check.That(states == 1, $"exactly one StateChanged (got {states})");
            Check.That(toasts.Count == 1 && toasts[0] == EditorSession.SelectHintToast, "hint toast");
            Check.That(s.ToolHint == EditorSession.SelectHintToast, "ToolHint is the same string");
            s.SetTool(ToolId.SelectMove);
            Check.That(states == 1 && toasts.Count == 1, "same tool again: nothing");
            s.SetTool(ToolId.Rotate);
            Check.That(s.ActiveTool == ToolId.SelectMove && toasts[1] == EditorSession.ComingSoonToast, "Rotate still coming soon");
            s.SetTool(ToolId.Stamps);
            Check.That(s.ActiveTool == ToolId.SelectMove && toasts[2] == EditorSession.ComingSoonToast, "Stamps still coming soon");
        }

        public static void SetToolLineClearsSelectionWithOneState()
        {
            var f = new Fixture();
            Drag(f.S, 0.05, 0.05, 0.95, 0.95);
            Check.That(f.S.SelectionCount == 4, "all selected");
            f.States = 0;
            f.S.SetTool(ToolId.Line);
            Check.That(f.S.SelectionCount == 0, "Line clears the selection");
            Check.That(f.States == 1, $"one StateChanged (got {f.States})");
            Drag(f.S, 0.05, 0.05, 0.95, 0.95);   // draws a line now
            f.S.SetTool(ToolId.SelectMove);
            Check.That(f.S.SelectionCount == 0, "nothing re-selected");
        }

        public static void SetToolMidMarqueeRestoresThenClearsWithOneState()
        {
            var f = new Fixture();
            Drag(f.S, 0.05, 0.05, 0.35, 0.35);
            Check.That(f.Selected(f.L1), "L1 selected");
            f.S.Pointer(Ev(PointerPhase.Down, 0.05, 0.5));
            f.S.Pointer(Ev(PointerPhase.Move, 0.95, 0.95));
            Check.That(f.Selected(f.L2, f.C2), "live: marquee replaced the selection");
            f.States = 0;
            f.S.SetTool(ToolId.Circle);
            Check.That(f.S.SelectionCount == 0 && !f.S.GestureActive && f.S.Preview.Count == 0, "cleared and closed");
            Check.That(f.States == 1, $"one StateChanged (got {f.States})");
        }

        // ---------- marquee ----------

        public static void MarqueeSelectsAcrossLayersAndToastsCount()
        {
            var f = new Fixture();
            Drag(f.S, 0.05, 0.05, 0.5, 0.65);   // L1 and L2, C1 / C2 partly outside
            Check.That(f.Selected(f.L1, f.L2), "two lines on two layers");
            Check.That(f.Last == "2 shapes selected", $"toast: {f.Last}");
            Check.That(f.BoardChanges == 0, "no BoardChanged");
            Check.That(ShapeCount(f.S) == 4, "nothing committed");
            Check.That(f.S.Preview.Count == 0 && !f.S.Cursor.HasValue && !f.S.GestureActive, "marquee and cursor gone");

            Drag(f.S, 0.95, 0.05, 0.55, 0.35);   // reversed corners: C1 only
            Check.That(f.Selected(f.C1), "replaced, not added");
            Check.That(f.Last == "1 shape selected", $"singular toast: {f.Last}");
        }

        public static void PartialShapesAreExcludedAndEmptyBoxToasts()
        {
            var f = new Fixture();
            Drag(f.S, 0.05, 0.05, 0.95, 0.95);
            Check.That(f.S.SelectionCount == 4, "all four");
            Drag(f.S, 0.2, 0.0, 0.95, 0.4);   // covers half of L1 and all of C1
            Check.That(f.Selected(f.C1), "partly covered L1 excluded");
            Drag(f.S, 0.15, 0.15, 0.25, 0.25);   // inside L1 only
            Check.That(f.S.SelectionCount == 0, "nothing enclosed: selection cleared");
            Check.That(f.Last == EditorSession.NothingSelectedToast, $"toast: {f.Last}");
        }

        public static void MarqueePreviewOnlyAfterSlopAndNeverSnaps()
        {
            var f = new Fixture();
            f.S.SetSnapEnabled(true);
            f.S.Pointer(Ev(PointerPhase.Down, 0.097, 0.098));   // near L1's endpoint (a snap candidate)
            Check.That(f.S.GestureActive, "gesture open");
            Check.That(f.S.Preview.Count == 0, "nothing drawn at Down");
            Check.That(f.S.Cursor == new Vec2(0.097, 0.098), "cursor at the raw Down point (no snap)");
            f.S.Pointer(Ev(PointerPhase.Move, 0.099, 0.099));   // inside the slop (0.0083)
            Check.That(f.S.Preview.Count == 0, "nothing drawn inside the slop");
            f.S.Pointer(Ev(PointerPhase.Move, 0.497, 0.502));   // near the (0.5, 0.5) grid corner
            Check.That(f.S.Preview.Count == 4, $"4 marquee edges (got {f.S.Preview.Count})");
            foreach (var sh in f.S.Preview)
            {
                Check.That(sh is LineShape, "edges are lines");
                Check.That(sh.Style.Dash == LineStyle.Dotted && sh.Style.Width == LineWidth.Medium, "dotted Medium");
            }
            var bottom = (LineShape)f.S.Preview[0];
            Check.That(bottom.A == new Vec2(0.097, 0.098) && bottom.B == new Vec2(0.497, 0.098), "BL->BR from raw points");
            Check.That(f.S.Cursor == new Vec2(0.497, 0.502), "cursor follows the moving corner, unsnapped");
            Check.That(f.Selected(f.L1), "L1 live-selected");
            f.S.Pointer(Ev(PointerPhase.Up, 0.497, 0.502));
            Check.That(f.S.Preview.Count == 0 && !f.S.Cursor.HasValue, "marquee removed on Up");
            Check.That(f.Selected(f.L1), "kept after Up");
            Check.That(ShapeCount(f.S) == 4, "nothing committed");
        }

        public static void LiveSelectionRaisesStateOnlyOnChange()
        {
            var f = new Fixture();
            f.S.Pointer(Ev(PointerPhase.Down, 0.05, 0.05));
            f.States = 0;
            f.S.Pointer(Ev(PointerPhase.Move, 0.2, 0.2));
            Check.That(f.States == 0 && f.S.SelectionCount == 0, "nothing enclosed yet, no event");
            f.S.Pointer(Ev(PointerPhase.Move, 0.31, 0.31));
            Check.That(f.States == 1 && f.Selected(f.L1), "L1 enters: one event");
            f.S.Pointer(Ev(PointerPhase.Move, 0.32, 0.33));
            f.S.Pointer(Ev(PointerPhase.Move, 0.34, 0.35));
            Check.That(f.States == 1, "same set: no more events");
            f.S.Pointer(Ev(PointerPhase.Move, 0.25, 0.25));
            Check.That(f.States == 2 && f.S.SelectionCount == 0, "L1 leaves: one event");
            f.S.Pointer(Ev(PointerPhase.Move, 0.95, 0.95));
            Check.That(f.States == 3 && f.S.SelectionCount == 4, "everything: one event");
            Check.That(f.Toasts.Count == 0, "no toast during the drag");
            f.S.Pointer(Ev(PointerPhase.Up, 0.95, 0.95));
            Check.That(f.States == 3, "Up with the same set: no event");
            Check.That(f.Last == "4 shapes selected", $"toast on Up: {f.Last}");
        }

        // ---------- tap ----------

        public static void TapPicksStrokeAndEmptyTapClears()
        {
            var f = new Fixture();
            Tap(f.S, 0.2, 0.21);   // 0.007 from L1
            Check.That(f.Selected(f.L1), "tap near L1 selects it");
            Check.That(f.Last == "1 shape selected", $"toast: {f.Last}");
            int toasts = f.Toasts.Count;
            Tap(f.S, 0.7, 0.7);    // centre of C2: 0.15 from its ring
            Check.That(f.S.SelectionCount == 0, "tap inside a big circle away from its ring clears");
            Check.That(f.Toasts.Count == toasts, "empty tap: no toast");
            Tap(f.S, 0.7, 0.86);   // 0.01 outside C2's ring
            Check.That(f.Selected(f.C2), "tap on the ring selects the circle");
            Tap(f.S, 0.7, 0.2 + 0.1 + 0.019);
            Check.That(f.Selected(f.C1), "within 0.02 of C1's ring: replaces");
            Tap(f.S, 0.7, 0.2 + 0.1 + 0.021);
            Check.That(f.S.SelectionCount == 0, "0.021 away: out of range, clears");
            Check.That(f.BoardChanges == 0 && ShapeCount(f.S) == 4, "no model change");
        }

        public static void TapTieGoesToTopmost()
        {
            var f = new Fixture();
            var dupBottom = AddLine(f.S.Board, f.Bottom, 0.1, 0.6, 0.4, 0.6);   // same stroke as L2, lower layer
            Tap(f.S, 0.25, 0.605);
            Check.That(f.Selected(f.L2), "top layer wins the tie");
            Check.That(dupBottom.Id != f.L2.Id, "distinct");
        }

        public static void PickToleranceUsesTheMetric()
        {
            // Affine 1000 px per board unit: 12 px = 0.012 board units.
            var f = new Fixture();
            f.S.SnapMetric = new ScaleMetric { PxPerUnit = 1000, Tol = 12 };
            Tap(f.S, 0.2, 0.2 + 0.0115 * Math.Sqrt(2));   // 0.0115 from L1 (45 degree line)
            Check.That(f.Selected(f.L1), "within 12 px");
            Tap(f.S, 0.2, 0.2 + 0.0125 * Math.Sqrt(2));
            Check.That(f.S.SelectionCount == 0, "beyond 12 px (though within the 0.02 fallback)");
            // Very zoomed out: 100 px per unit, 12 px = 0.12, capped at MaxPickBoardDistance (0.03).
            f.S.SnapMetric = new ScaleMetric { PxPerUnit = 100, Tol = 12 };
            Tap(f.S, 0.7, 0.2 + 0.1 + 0.029);
            Check.That(f.Selected(f.C1), "0.029: inside the cap");
            Tap(f.S, 0.7, 0.2 + 0.1 + 0.032);
            Check.That(f.S.SelectionCount == 0, "0.032: beyond the cap");
            // Projection fails: the board-unit fallback (0.02).
            f.S.SnapMetric = new ScaleMetric { PxPerUnit = 1000, Tol = 12, Fail = true };
            Tap(f.S, 0.7, 0.2 + 0.1 + 0.019);
            Check.That(f.Selected(f.C1), "fallback tolerance");
        }

        sealed class ScaleMetric : ISnapMetric
        {
            public double PxPerUnit = 1000, Tol = 12;
            public bool Fail;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                screenPx = new Vec2(board.X * PxPerUnit + 5, board.Y * PxPerUnit - 3);
                return !Fail;
            }
        }

        // ---------- cancel, clear, load ----------

        public static void CancelRestoresPreviousSelection()
        {
            var f = new Fixture();
            Drag(f.S, 0.05, 0.05, 0.35, 0.35);
            Check.That(f.Selected(f.L1), "L1");
            int toasts = f.Toasts.Count;
            f.S.Pointer(Ev(PointerPhase.Down, 0.5, 0.0));
            f.S.Pointer(Ev(PointerPhase.Move, 0.95, 0.95));
            Check.That(f.Selected(f.C1, f.C2), "live replaced");
            f.States = 0;
            f.S.Pointer(Ev(PointerPhase.Cancel, 0.95, 0.95));
            Check.That(f.Selected(f.L1), "Cancel restores L1");
            Check.That(f.States == 1, "one StateChanged for the restore");
            Check.That(f.S.Preview.Count == 0 && !f.S.Cursor.HasValue && !f.S.GestureActive, "marquee removed");
            Check.That(f.Toasts.Count == toasts, "no toast on Cancel");
            f.S.Pointer(Ev(PointerPhase.Up, 0.95, 0.95));
            Check.That(f.Selected(f.L1), "a stray Up after Cancel changes nothing");
        }

        public static void LostUpRestoresThenStartsAgain()
        {
            var f = new Fixture();
            Drag(f.S, 0.05, 0.05, 0.35, 0.35);
            f.S.Pointer(Ev(PointerPhase.Down, 0.5, 0.0));
            f.S.Pointer(Ev(PointerPhase.Move, 0.95, 0.95));
            f.S.Pointer(Ev(PointerPhase.Down, 0.05, 0.5));   // lost Up: the first marquee is discarded
            f.S.Pointer(Ev(PointerPhase.Cancel, 0.05, 0.5));
            Check.That(f.Selected(f.L1), "both discarded: back to L1");
        }

        public static void ClearBoardMidDragResets()
        {
            var f = new Fixture();
            Drag(f.S, 0.05, 0.05, 0.35, 0.35);
            f.S.Pointer(Ev(PointerPhase.Down, 0.05, 0.05));
            f.S.Pointer(Ev(PointerPhase.Move, 0.95, 0.95));
            f.S.ClearBoard();
            Check.That(f.S.SelectionCount == 0, "selection cleared");
            Check.That(!f.S.GestureActive && f.S.Preview.Count == 0 && !f.S.Cursor.HasValue, "gesture aborted");
            Check.That(f.S.ActiveTool == ToolId.SelectMove, "tool kept");
            f.S.Pointer(Ev(PointerPhase.Up, 0.95, 0.95));
            Check.That(f.S.SelectionCount == 0 && ShapeCount(f.S) == 0, "stale Up does nothing");

            var g = new Fixture();
            Drag(g.S, 0.05, 0.05, 0.95, 0.95);
            g.S.LoadBoard(GoldenBoards.StarOnFullBoard());
            Check.That(g.S.SelectionCount == 0, "LoadBoard clears too");
        }

        // ---------- layers ----------

        public static void HiddenLayerShapesCannotBeSelected()
        {
            var f = new Fixture();
            f.S.SetLayerVisible(f.Bottom.Id, false);
            Drag(f.S, 0.05, 0.05, 0.95, 0.95);
            Check.That(f.Selected(f.L2, f.C2), "only the visible layer's shapes");
            Tap(f.S, 0.2, 0.2);
            Check.That(f.S.SelectionCount == 0, "a tap on a hidden line picks nothing");
        }

        public static void HidingOrDeletingPrunesAndShowingDoesNotReselect()
        {
            var f = new Fixture();
            Drag(f.S, 0.05, 0.05, 0.95, 0.95);
            int boardChanges = f.BoardChanges;
            f.States = 0;
            int statesAtBoardChanged = -1;
            f.S.BoardChanged += _ => statesAtBoardChanged = f.S.SelectionCount;
            f.S.SetLayerVisible(f.Bottom.Id, false);
            Check.That(f.Selected(f.L2, f.C2), "bottom's shapes pruned");
            Check.That(statesAtBoardChanged == 2, "pruned before BoardChanged listeners run");
            Check.That(f.States == 1, $"one StateChanged for the prune (got {f.States})");
            Check.That(f.BoardChanges == boardChanges + 1, "the layer change itself");
            f.S.SetLayerVisible(f.Bottom.Id, true);
            Check.That(f.Selected(f.L2, f.C2), "showing again does not re-select");
            Check.That(f.States == 1, "no prune, no StateChanged");

            f.S.SetLayerOpacity(f.Top.Id, 0.5);
            Check.That(f.States == 1, "unrelated model change: no StateChanged");

            Drag(f.S, 0.05, 0.05, 0.95, 0.95);
            f.S.DeleteBottomLayer();
            Check.That(f.Selected(f.L2, f.C2), "deleted layer's shapes pruned");
        }

        public static void HiddenActiveLayerDoesNotBlockSelect()
        {
            var f = new Fixture();
            f.S.SetLayerVisible(f.Top.Id, false);   // Top is the active layer
            int toasts = f.Toasts.Count;
            Drag(f.S, 0.05, 0.05, 0.95, 0.95);
            Check.That(f.Selected(f.L1, f.C1), "bottom layer selectable");
            Check.That(f.Toasts.Count == toasts + 1 && f.Last == "2 shapes selected", $"no hidden-layer toast ({f.Last})");
            Check.That(!f.Toasts.Contains(EditorSession.HiddenLayerToast), "never the hidden-layer toast");
        }

        // ---------- API ----------

        public static void SelectionQueriesAndSetSelection()
        {
            var f = new Fixture();
            var list = new List<EditorSession.SelectedShape> { default };
            f.S.GetSelectedShapes(list);
            Check.That(list.Count == 0, "empty selection: cleared list");
            Check.That(!f.S.TryGetSelectionBounds(out _), "no bounds when empty");

            f.States = 0;
            f.S.SetSelection(new[] { f.C2.Id, f.L1.Id, new EntityId(999) });
            Check.That(f.Selected(f.L1, f.C2), "unknown id dropped");
            Check.That(f.States == 1, "one StateChanged");
            f.S.SetSelection(new[] { f.L1.Id, f.C2.Id });
            Check.That(f.States == 1, "same set: no event");

            f.S.GetSelectedShapes(list);
            Check.That(list.Count == 2, "two");
            Check.That(list[0].Shape == f.L1 && list[0].Layer == f.Bottom, "bottom layer first");
            Check.That(list[1].Shape == f.C2 && list[1].Layer == f.Top, "then top");

            Check.That(f.S.TryGetSelectionBounds(out BoardRect b), "bounds");
            Check.Near(b.MinX, 0.1, "minX"); Check.Near(b.MinY, 0.1, "minY");
            Check.Near(b.MaxX, 0.85, "maxX"); Check.Near(b.MaxY, 0.85, "maxY");

            f.S.SetLayerVisible(f.Bottom.Id, false);
            f.S.SetSelection(new[] { f.L1.Id, f.C1.Id, f.C2.Id });
            Check.That(f.Selected(f.C2), "hidden layer ids dropped");

            f.States = 0;
            f.S.ClearSelection();
            Check.That(f.S.SelectionCount == 0 && f.States == 1, "ClearSelection: one event");
            f.S.ClearSelection();
            Check.That(f.States == 1, "already empty: no event");
            f.S.SetSelection(null);
            Check.That(f.States == 1 && f.S.SelectionCount == 0, "null: empty, no event");
            Check.That(f.BoardChanges == 1, "only SetLayerVisible changed the model");
        }

        // ---------- allocation ----------

        static int s_events;

        public static void MovePathAllocatesNothing()
        {
            foreach (bool withMetric in new[] { false, true })
            {
                var s = new EditorSession(GoldenBoards.StarOnFullBoard());
                if (withMetric) s.SnapMetric = new ScaleMetric { PxPerUnit = 900, Tol = 24 };
                s.PreviewChanged += () => s_events++;
                s.StateChanged += () => s_events++;
                s.SetTool(ToolId.SelectMove);
                Drag(s, 0.0, 0.0, 1.0, 1.0, 40);   // warm-up: every shape selected once (set capacity)
                Drag(s, 0.9, 0.9, 0.1, 0.1, 40);
                s.Pointer(Ev(PointerPhase.Down, 0.02, 0.03));
                var moves = new PointerEvent[200];
                for (int i = 0; i < moves.Length; i++)
                {
                    // Grows and shrinks the box over the star: the enclosed set changes many times.
                    double t = i / 199.0;
                    double k = 0.5 + 0.48 * Math.Sin(9 * t);
                    moves[i] = new PointerEvent(PointerPhase.Move, new Vec2(0.03 + k * 0.95, 0.03 + k * 0.96), true, i);
                }
                s.Pointer(moves[0]);
                int before = s_events;
                long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < moves.Length; i++) s.Pointer(moves[i]);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
                int events = s_events - before;
                s.Pointer(Ev(PointerPhase.Up, 0.5, 0.5));
                Check.That(events > 200, $"the moves did work ({events} events)");
                Check.That(bytes == 0, $"metric {withMetric}: 200 Moves allocated {bytes} bytes");
            }
        }
    }
}
