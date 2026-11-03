using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// E3: the Line tool's H/V lock (board axes). Unless noted there is no metric (tolerance 0.02 board units) and the
    /// board is the default empty 2x2 square grid, whose candidates sit on multiples of 0.25.
    /// </summary>
    public static class OrthoChecks
    {
        static double s_T;
        static PointerEvent Ev(PointerPhase p, double x, double y, bool on = true) =>
            new PointerEvent(p, new Vec2(x, y), on, s_T += 0.016);

        static void Drag(EditorSession s, double ax, double ay, double bx, double by, int steps = 6)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * t, ay + (by - ay) * t));
            }
            s.Pointer(Ev(PointerPhase.Up, bx, by));
        }

        static void Tap(EditorSession s, double x, double y)
        {
            s.Pointer(Ev(PointerPhase.Down, x, y));
            s.Pointer(Ev(PointerPhase.Up, x, y));
        }

        static EditorSession Locked(ISnapMetric metric = null)
        {
            var s = new EditorSession { SnapMetric = metric };
            s.SetOrthoLock(true);
            return s;
        }

        static int ShapeCount(EditorSession s)
        {
            int n = 0;
            foreach (var l in s.Board.Layers) n += l.Shapes.Count;
            return n;
        }

        static LineShape LastLine(EditorSession s)
        {
            var shapes = s.ActiveLayer.Shapes;
            Check.That(shapes.Count > 0, "a shape was committed");
            var line = shapes[shapes.Count - 1] as LineShape;
            Check.That(line != null, "the last shape is a LineShape");
            return line;
        }

        static LineShape Ghost(EditorSession s)
        {
            foreach (var sh in s.Preview) if (sh is LineShape l) return l;
            throw new Exception("no ghost line in Preview");
        }

        static List<CircleShape> Rings(EditorSession s)
        {
            var r = new List<CircleShape>();
            foreach (var sh in s.Preview)
                if (sh is CircleShape c && c.Radius == EditorSession.HoverRingRadius && c.Style.Width == LineWidth.Thin
                    && c.Style.Dash == LineStyle.Solid)
                    r.Add(c);
            return r;
        }

        static void AddUserPoint(EditorSession s, double x, double y) =>
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(x, y)));

        static string P(Vec2 v) => $"({v.X:R}, {v.Y:R})";

        /// <summary>Projective fake metric: w = 1 + 0.8 y, screen = 1000 * (x, y) / w (same as SnapToolChecks).</summary>
        sealed class PerspectiveMetric : ISnapMetric
        {
            public double Tol = 12;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                double w = 1 + 0.8 * board.Y;
                screenPx = new Vec2(1000 * board.X / w, 1000 * board.Y / w);
                return true;
            }
        }

        /// <summary>screen = board * Scale + offset (pixels).</summary>
        sealed class AffineMetric : ISnapMetric
        {
            public double Scale = 1000, Tol = 24;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                screenPx = new Vec2(board.X * Scale + 37, board.Y * Scale - 12);
                return true;
            }
        }

        // ---------- the constraint ----------

        public static void DiagonalDragCommitsExactlyHorizontalOrVertical()
        {
            var s = Locked();
            Drag(s, 0.1, 0.12, 0.83, 0.47);   // dx 0.73 > dy 0.35: horizontal
            var h = LastLine(s);
            Check.That(h.A == new Vec2(0.1, 0.12), $"start raw, got {P(h.A)}");
            Check.That(h.A.Y == h.B.Y, $"horizontal: A.Y == B.Y, got {P(h.A)} {P(h.B)}");
            Check.That(h.B == new Vec2(0.83, 0.12), $"end = (pointer X, anchor Y), got {P(h.B)}");

            Drag(s, 0.1, 0.12, 0.37, 0.88);   // dx 0.27 < dy 0.76: vertical
            var v = LastLine(s);
            Check.That(v.A.X == v.B.X, $"vertical: A.X == B.X, got {P(v.A)} {P(v.B)}");
            Check.That(v.B == new Vec2(0.1, 0.88), $"end = (anchor X, pointer Y), got {P(v.B)}");
            Check.That(ShapeCount(s) == 2, "two lines");
        }

        public static void DominantAxisFollowsEachMoveAndTieGoesHorizontal()
        {
            var s = Locked();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.12));
            s.Pointer(Ev(PointerPhase.Move, 0.6, 0.2));
            Check.That(Ghost(s).B == new Vec2(0.6, 0.12), $"ghost horizontal, got {P(Ghost(s).B)}");
            s.Pointer(Ev(PointerPhase.Move, 0.2, 0.7));
            Check.That(Ghost(s).B == new Vec2(0.1, 0.7), $"same gesture flips to vertical, got {P(Ghost(s).B)}");
            Check.That(s.Cursor == new Vec2(0.1, 0.7), "cursor at the locked end");
            s.Pointer(Ev(PointerPhase.Move, 0.7, 0.3));
            Check.That(Ghost(s).B == new Vec2(0.7, 0.12), "and back to horizontal");
            s.Pointer(Ev(PointerPhase.Up, 0.7, 0.3));
            Check.That(LastLine(s).B == new Vec2(0.7, 0.12), "commit matches the last ghost");

            // Negative directions (left / down).
            Drag(s, 0.9, 0.88, 0.3, 0.6);    // dx -0.6, dy -0.28
            Check.That(LastLine(s).B == new Vec2(0.3, 0.88), $"leftward horizontal, got {P(LastLine(s).B)}");
            Drag(s, 0.9, 0.88, 0.7, 0.2);    // dx -0.2, dy -0.68
            Check.That(LastLine(s).B == new Vec2(0.9, 0.2), $"downward vertical, got {P(LastLine(s).B)}");

            // |dx| == |dy| exactly (dyadic values): horizontal. Snap off so no candidate interferes.
            var t = Locked();
            t.SetSnapEnabled(false);
            Drag(t, 0.125, 0.125, 0.625, 0.625);
            Check.That(LastLine(t).B == new Vec2(0.625, 0.125), $"tie (+,+) goes horizontal, got {P(LastLine(t).B)}");
            Drag(t, 0.625, 0.625, 0.125, 0.125);
            Check.That(LastLine(t).B == new Vec2(0.125, 0.625), $"tie (-,-) goes horizontal, got {P(LastLine(t).B)}");
        }

        public static void ClampsToBoard()
        {
            var s = Locked();
            Drag(s, 0.6, 0.12, 1.2, 0.2);    // on-board flag, but X past the right edge
            Check.That(LastLine(s).B == new Vec2(1.0, 0.12), $"clamped to x = 1, got {P(LastLine(s).B)}");
            Drag(s, 0.4, 0.12, -0.3, 0.15);
            Check.That(LastLine(s).B == new Vec2(0.0, 0.12), $"clamped to x = 0, got {P(LastLine(s).B)}");
            Drag(s, 0.1, 0.6, 0.12, 1.5);
            Check.That(LastLine(s).B == new Vec2(0.1, 1.0), $"clamped to y = 1, got {P(LastLine(s).B)}");
        }

        // ---------- alignment snap ----------

        public static void AlignmentSnapToOffAxisCandidate()
        {
            var s = Locked();
            AddUserPoint(s, 0.613, 0.137);   // 0.017 off the axis y = 0.12
            AddUserPoint(s, 0.4, 0.146);     // 0.026 off: too far
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.12));
            s.Pointer(Ev(PointerPhase.Move, 0.62, 0.3));
            Check.That(Ghost(s).B == new Vec2(0.613, 0.12), $"ghost aligned, got {P(Ghost(s).B)}");
            var rings = Rings(s);
            Check.That(rings.Count == 1 && rings[0].Center == new Vec2(0.613, 0.12), "hover ring at the aligned end");
            Check.That(s.Cursor == new Vec2(0.613, 0.12), "cursor at the aligned end");
            s.Pointer(Ev(PointerPhase.Move, 0.66, 0.3));   // 0.047 along: out of range
            Check.That(Ghost(s).B == new Vec2(0.66, 0.12) && Rings(s).Count == 0, "unsnapped along the axis, ring gone");
            s.Pointer(Ev(PointerPhase.Move, 0.62, 0.3));
            s.Pointer(Ev(PointerPhase.Up, 0.62, 0.3));
            var line = LastLine(s);
            Check.That(line.B.X == 0.613, $"end X is the candidate's X exactly, got {line.B.X:R}");
            Check.That(line.B.Y == line.A.Y && line.B.Y == 0.12, $"end Y is the anchor's Y exactly, got {line.B.Y:R}");

            Drag(s, 0.1, 0.12, 0.405, 0.3);
            Check.That(LastLine(s).B == new Vec2(0.405, 0.12), $"0.026 off the axis does not align, got {P(LastLine(s).B)}");

            // Snap off: the lock still constrains, with no alignment.
            s.SetSnapEnabled(false);
            Drag(s, 0.1, 0.12, 0.62, 0.3);
            Check.That(LastLine(s).B == new Vec2(0.62, 0.12), $"snap off: locked but unaligned, got {P(LastLine(s).B)}");
        }

        public static void AxisCircleIntersectionSnapBeatsFartherCandidate()
        {
            var s = Locked();
            s.ActiveLayer.Shapes.Add(new CircleShape(s.Board.NewId(), new Vec2(0.5, 0.6), 0.2, StrokeStyle.Default));
            AddUserPoint(s, 0.318, 0.655);   // user point: best priority, 0.008 along from the raw end
            double crossing = 0.5 - Math.Sqrt(0.2 * 0.2 - 0.05 * 0.05);   // y = 0.65 meets the circle at x ~ 0.30635
            Drag(s, 0.1, 0.65, 0.31, 0.75);  // raw locked end (0.31, 0.65): crossing 0.00365 away
            var line = LastLine(s);
            Check.That(line.A == new Vec2(0.1, 0.65), $"start raw, got {P(line.A)}");
            Check.Near(line.B.X, crossing, "end X is the axis/circle crossing", 1e-12);
            Check.That(line.B.Y == 0.65, $"end Y copied from the anchor exactly, got {line.B.Y:R}");
            Check.That(line.B.X != 0.318, "the farther user point lost");

            // The other crossing, from the right.
            Drag(s, 0.95, 0.65, 0.70, 0.7);
            Check.Near(LastLine(s).B.X, 1 - crossing, "right-hand crossing", 1e-12);
            Check.That(LastLine(s).B.Y == 0.65, "Y exact");

            // Hidden layer: no crossing (and its candidates vanish), so the end stays raw.
            var h = Locked();
            var hidden = h.ActiveLayer;
            hidden.Shapes.Add(new CircleShape(h.Board.NewId(), new Vec2(0.5, 0.6), 0.2, StrokeStyle.Default));
            h.AddLayer();
            hidden.Visible = false;
            Drag(h, 0.1, 0.65, 0.31, 0.75);
            Check.That(LastLine(h).B == new Vec2(0.31, 0.65), $"hidden circle gives nothing, got {P(LastLine(h).B)}");
        }

        public static void AxisLineAndPolygonEdgeIntersectionSnap()
        {
            var s = Locked();
            s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), new Vec2(0.4, 0.1), new Vec2(0.47, 0.9), StrokeStyle.Default));
            double x = 0.4 + 0.07 * (0.23 / 0.8);   // y = 0.33 crosses the slanted line at x = 0.420125
            Drag(s, 0.1, 0.33, 0.43, 0.4);           // raw end (0.43, 0.33): 0.0099 along
            var line = LastLine(s);
            Check.Near(line.B.X, x, "end X is the axis/line crossing", 1e-12);
            Check.That(line.B.Y == 0.33, $"Y exact, got {line.B.Y:R}");
            Check.That(Math.Abs(line.B.X - 0.43) > 0.009, "it actually moved along the axis");

            // Vertical axis through a hexagon's lower edge.
            var p = Locked();
            var hex = new PolygonShape(p.Board.NewId(), new Vec2(0.6, 0.6), 6, 0.2, 0.3, StrokeStyle.Default);
            p.ActiveLayer.Shapes.Add(hex);
            const double ax = 0.64;
            double yb = double.PositiveInfinity;
            for (int k = 0; k < 6; k++)
            {
                double a0 = 0.3 + 2 * Math.PI * k / 6, a1 = 0.3 + 2 * Math.PI * (k + 1) / 6;
                double x0 = 0.6 + 0.2 * Math.Cos(a0), y0 = 0.6 + 0.2 * Math.Sin(a0);
                double x1 = 0.6 + 0.2 * Math.Cos(a1), y1 = 0.6 + 0.2 * Math.Sin(a1);
                if ((x0 - ax) * (x1 - ax) >= 0) continue;
                yb = Math.Min(yb, y0 + (y1 - y0) * (ax - x0) / (x1 - x0));
            }
            Check.That(yb > 0.4 && yb < 0.45, $"expected a lower-edge crossing near 0.43, got {yb:R}");
            Drag(p, ax, 0.05, ax + 0.01, yb + 0.008);
            var v = LastLine(p);
            Check.That(v.B.X == ax && v.A.X == ax, $"X copied exactly, got {P(v.B)}");
            Check.Near(v.B.Y, yb, "end Y is the axis/polygon-edge crossing", 1e-12);
        }

        public static void EndsOnGridIntersection()
        {
            var s = Locked();
            Drag(s, 0.252, 0.497, 0.745, 0.56);
            var h = LastLine(s);
            Check.That(h.A == new Vec2(0.25, 0.5), $"start snaps normally, got {P(h.A)}");
            Check.That(h.B == new Vec2(0.75, 0.5), $"end on the grid point (0.75, 0.5), got {P(h.B)}");
            Drag(s, 0.252, 0.497, 0.29, 0.993);
            Check.That(LastLine(s).B == new Vec2(0.25, 1.0), $"vertical end on (0.25, 1), got {P(LastLine(s).B)}");

            // Hex grid: from one corner to the nearest other corner on the same row.
            var x = Locked();
            x.SetGridKind(GridKind.Hex);
            var grid = new List<SnapCandidate>();
            TileGrid.SnapPoints(GridKind.Hex, x.Board.GridN, grid);
            Vec2 a = default, b = default;
            bool found = false;
            foreach (var c in grid)
            {
                if (c.Kind != SnapKind.CellCorner) continue;
                foreach (var d in grid)
                    if (d.Kind == SnapKind.CellCorner && d.Position.Y == c.Position.Y && d.Position.X - c.Position.X > 0.1
                        && (!found || d.Position.X - c.Position.X < b.X - a.X))
                    {
                        a = c.Position; b = d.Position; found = true;
                    }
            }
            Check.That(found, "two hex corners on one row");
            Drag(x, a.X + 0.002, a.Y - 0.003, b.X - 0.004, b.Y + 0.05);
            var hl = LastLine(x);
            Check.That(hl.A == a && hl.B == b, $"hex corners exact: {P(hl.A)} vs {P(a)}, {P(hl.B)} vs {P(b)}");
        }

        // ---------- lock off / other tools ----------

        public static void LockOffRestoresFreeLines()
        {
            var s = Locked();
            Drag(s, 0.1, 0.12, 0.83, 0.47);
            Check.That(LastLine(s).B == new Vec2(0.83, 0.12), "locked first");
            s.SetOrthoLock(false);
            Drag(s, 0.1, 0.12, 0.83, 0.47);
            Check.That(LastLine(s).B == new Vec2(0.83, 0.47), $"free diagonal again, got {P(LastLine(s).B)}");
            Drag(s, 0.1, 0.12, 0.503, 0.498);
            Check.That(LastLine(s).B == new Vec2(0.5, 0.5), "normal point snap again");
        }

        public static void CircleToolUnaffected()
        {
            var s = Locked();
            s.SetTool(ToolId.Circle);
            Drag(s, 0.1, 0.12, 0.83, 0.47);
            var c = (CircleShape)s.ActiveLayer.Shapes[0];
            Check.That(c.Center == new Vec2(0.1, 0.12), "centre raw");
            Check.That(c.Radius == Vec2.Distance(new Vec2(0.1, 0.12), new Vec2(0.83, 0.47)), $"diagonal radius, got {c.Radius:R}");
            Drag(s, 0.252, 0.247, 0.497, 0.252);
            var d = (CircleShape)s.ActiveLayer.Shapes[1];
            Check.That(d.Center == new Vec2(0.25, 0.25) && d.Radius == 0.25, "normal snapping in the Circle tool");

            // Toggling the lock during a Circle gesture changes nothing.
            int previews = 0;
            s.PreviewChanged += () => previews++;
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.12));
            s.Pointer(Ev(PointerPhase.Move, 0.6, 0.4));
            int before = previews;
            s.SetOrthoLock(false);
            s.SetOrthoLock(true);
            Check.That(previews == before, "no PreviewChanged for a Circle gesture");
            s.Pointer(Ev(PointerPhase.Up, 0.6, 0.4));
            Check.That(((CircleShape)s.ActiveLayer.Shapes[2]).Radius == Vec2.Distance(new Vec2(0.1, 0.12), new Vec2(0.6, 0.4)), "free radius");
        }

        // ---------- screen-space tolerance ----------

        public static void ToleranceIsInScreenSpace()
        {
            // Near y = 0.12 the perspective metric magnifies: 0.017 off the axis is 14 px > 12 px. Fallback would align.
            var near = Locked(new PerspectiveMetric());
            AddUserPoint(near, 0.613, 0.137);
            Drag(near, 0.1, 0.12, 0.62, 0.3);
            Check.That(LastLine(near).B == new Vec2(0.62, 0.12), $"14 px off the axis: no snap, got {P(LastLine(near).B)}");
            var nearFallback = Locked();
            AddUserPoint(nearFallback, 0.613, 0.137);
            Drag(nearFallback, 0.1, 0.12, 0.62, 0.3);
            Check.That(LastLine(nearFallback).B == new Vec2(0.613, 0.12), "same geometry in board units aligns");

            // Near y = 0.9 it shrinks: 0.025 off the axis is 8.4 px <= 12 px. Fallback (0.025 > 0.02) would not align.
            var far = Locked(new PerspectiveMetric());
            AddUserPoint(far, 0.613, 0.925);
            Drag(far, 0.1, 0.9, 0.62, 0.7);
            Check.That(LastLine(far).B == new Vec2(0.613, 0.9), $"8 px off the axis: snaps, got {P(LastLine(far).B)}");
            var farFallback = Locked();
            AddUserPoint(farFallback, 0.613, 0.925);
            Drag(farFallback, 0.1, 0.9, 0.62, 0.7);
            Check.That(LastLine(farFallback).B == new Vec2(0.62, 0.9), "same geometry in board units stays unaligned");

            // Along the axis too: scale 500, 24 px = 0.048 board units; a candidate 0.04 along (20 px) snaps.
            var along = Locked(new AffineMetric { Scale = 500, Tol = 24 });
            AddUserPoint(along, 0.66, 0.12);
            Drag(along, 0.1, 0.12, 0.62, 0.3);
            Check.That(LastLine(along).B == new Vec2(0.66, 0.12), $"20 px along: snaps, got {P(LastLine(along).B)}");
            var alongFallback = Locked();
            AddUserPoint(alongFallback, 0.66, 0.12);
            Drag(alongFallback, 0.1, 0.12, 0.62, 0.3);
            Check.That(LastLine(alongFallback).B == new Vec2(0.62, 0.12), "0.04 along in board units: no snap");
        }

        // ---------- toggling mid-gesture ----------

        public static void TogglingLockMidGestureUpdatesGhost()
        {
            var s = new EditorSession();
            AddUserPoint(s, 0.613, 0.137);
            int previews = 0;
            s.PreviewChanged += () => previews++;
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.12));
            s.Pointer(Ev(PointerPhase.Move, 0.62, 0.3));
            Check.That(Ghost(s).B == new Vec2(0.62, 0.3) && Rings(s).Count == 0, "free ghost first");

            int before = previews;
            s.SetOrthoLock(true);
            Check.That(previews == before + 1, $"one PreviewChanged on lock, got {previews - before}");
            Check.That(Ghost(s).B == new Vec2(0.613, 0.12), $"ghost locked and aligned at once, got {P(Ghost(s).B)}");
            Check.That(s.Cursor == new Vec2(0.613, 0.12), "cursor follows");
            var rings = Rings(s);
            Check.That(rings.Count == 1 && rings[0].Center == new Vec2(0.613, 0.12), "hover ring at the aligned end");

            before = previews;
            s.SetOrthoLock(false);
            Check.That(previews == before + 1, "one PreviewChanged on unlock");
            Check.That(Ghost(s).B == new Vec2(0.62, 0.3) && Rings(s).Count == 0, $"free again at once, got {P(Ghost(s).B)}");

            s.SetOrthoLock(true);
            s.Pointer(Ev(PointerPhase.Up, 0.62, 0.3));   // same position as the last Move
            var line = LastLine(s);
            Check.That(line.A == new Vec2(0.1, 0.12) && line.B == new Vec2(0.613, 0.12), $"commit uses the re-tracked end, got {P(line.B)}");

            // No gesture: toggling only raises StateChanged.
            before = previews;
            s.SetOrthoLock(false);
            Check.That(previews == before && s.Preview.Count == 0, "idle toggle touches no preview");
        }

        // ---------- tap-to-connect ----------

        public static void TapToConnectWithLock()
        {
            var s = Locked();
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);

            Tap(s, 0.502, 0.497);
            Tap(s, 0.748, 0.503);   // (0.75, 0.5): same row
            var h = LastLine(s);
            Check.That(h.A == new Vec2(0.5, 0.5) && h.B == new Vec2(0.75, 0.5), $"row pair commits, got {P(h.A)} {P(h.B)}");

            Tap(s, 0.502, 0.497);
            Tap(s, 0.497, 0.748);   // (0.5, 0.75): same column
            var v = LastLine(s);
            Check.That(v.A == new Vec2(0.5, 0.5) && v.B == new Vec2(0.5, 0.75), $"column pair commits, got {P(v.A)} {P(v.B)}");
            Check.That(toasts.Count == 0 && ShapeCount(s) == 2, "no toast so far, two lines");

            Tap(s, 0.502, 0.497);
            Tap(s, 0.748, 0.253);   // (0.75, 0.25): diagonal
            Check.That(ShapeCount(s) == 2, "diagonal pair commits nothing");
            Check.That(toasts.Count == 1 && toasts[0] == EditorSession.OrthoLockToast, "lock toast once");
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "pending cleared, no ring");

            // Same pair with the lock off commits (E2 behaviour).
            s.SetOrthoLock(false);
            Tap(s, 0.502, 0.497);
            Tap(s, 0.748, 0.253);
            Check.That(ShapeCount(s) == 3 && LastLine(s).B == new Vec2(0.75, 0.25), "lock off: diagonal tap pair commits");
            Check.That(toasts.Count == 1, "no further toast");
        }

        // ---------- allocation ----------

        static int s_previews;

        public static void LockedMovePathAllocatesNothing()
        {
            foreach (bool withMetric in new[] { false, true })
            {
                var s = new EditorSession();
                var layer = s.ActiveLayer;
                layer.Shapes.Add(new LineShape(s.Board.NewId(), new Vec2(0.1, 0.2), new Vec2(0.9, 0.8), StrokeStyle.Default));
                layer.Shapes.Add(new LineShape(s.Board.NewId(), new Vec2(0.2, 0.9), new Vec2(0.8, 0.1), StrokeStyle.Default));
                layer.Shapes.Add(new LineShape(s.Board.NewId(), new Vec2(0.05, 0.37), new Vec2(0.95, 0.41), StrokeStyle.Default));
                layer.Shapes.Add(new CircleShape(s.Board.NewId(), new Vec2(0.5, 0.5), 0.3, StrokeStyle.Default));
                layer.Shapes.Add(new PolygonShape(s.Board.NewId(), new Vec2(0.5, 0.5), 6, 0.15, 0.2, StrokeStyle.Default));
                if (withMetric) s.SnapMetric = new PerspectiveMetric { Tol = 24 };
                s.PreviewChanged += () => s_previews++;
                s.SetOrthoLock(true);
                Drag(s, 0.2, 0.3, 0.7, 0.6, 40);   // warm-up (rebuilds candidates)
                Tap(s, 0.5, 0.5);                  // a pending ring too

                var moves = new PointerEvent[200];
                for (int i = 0; i < moves.Length; i++)
                {
                    // Circles round the anchor: the dominant axis flips, and the end crosses shapes and candidates.
                    double t = i / 199.0;
                    moves[i] = new PointerEvent(PointerPhase.Move,
                        new Vec2(0.53 + 0.42 * Math.Cos(6 * t), 0.47 + 0.42 * Math.Sin(9 * t)), true, i);
                }

                // Untimed pass: the sweep really exercises both axes and alignment snaps.
                s.Pointer(Ev(PointerPhase.Down, 0.53, 0.47));
                Vec2 anc = s.Cursor.Value;   // the anchor (snapped or raw)
                int horiz = 0, vert = 0, snapped = 0;
                for (int i = 0; i < moves.Length; i++)
                {
                    s.Pointer(moves[i]);
                    Vec2 c = s.Cursor.Value;
                    if (c.Y == anc.Y && c.X != anc.X) horiz++;
                    if (c.X == anc.X && c.Y != anc.Y) vert++;
                    if (Rings(s).Count == 1) snapped++;   // hover ring (the pending start dropped with the first Move)
                }
                Check.That(horiz > 20 && vert > 20 && snapped > 10, $"sweep coverage: {horiz} horizontal, {vert} vertical, {snapped} snapped");

                s.Pointer(moves[0]);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < moves.Length; i++) s.Pointer(moves[i]);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                s.Pointer(Ev(PointerPhase.Up, 0.9, 0.5));
                Check.That(bytes == 0, $"locked Line, metric {withMetric}: 200 Moves allocated {bytes} bytes");
            }
        }
    }
}
