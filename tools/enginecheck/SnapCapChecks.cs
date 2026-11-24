using System;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Adaptive board-distance cap (EditorSession.BoardCap, ui-line-plan 4.2): a candidate snaps only if it is within the
    /// screen tolerance AND within BoardCap(pointer) = clamp(1.5 tol / sigma_max, 0.03, max(0.10, half a tile)) on the
    /// board. Rewritten from the fixed 0.03 cap: looking straight down the cap no longer binds (a 28 px dot snaps), a
    /// grazing view is still capped (strongly perspective fake metric: the far edge is compressed about 4x), magnified
    /// views keep the 0.03 floor, a far tiny board is held by the tile ceiling, and a failed projection falls back to
    /// 0.03. Each test proves its precondition (pixels vs cap) with an independent cap computation.
    /// </summary>
    public static class SnapCapChecks
    {
        const double Tol = 32;

        /// <summary>w = 1 + 3y, screen = 1000 * (x, y) / w. Near edge ~1000 px per unit, far edge ~62 px per unit in y.</summary>
        sealed class GrazingMetric : ISnapMetric
        {
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                double w = 1 + 3 * board.Y;
                screenPx = new Vec2(1000 * board.X / w, 1000 * board.Y / w);
                return true;
            }
        }

        /// <summary>Straight down: screen = scale * board (an orthographic top view).</summary>
        sealed class TopDownMetric : ISnapMetric
        {
            public double Scale = 400;
            public double TolerancePx => Tol;
            public bool FailRight;   // projections with x > 0.9 fail (a failed Jacobian probe)

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                screenPx = new Vec2(board.X * Scale, board.Y * Scale);
                return !(FailRight && board.X > 0.9);
            }
        }

        /// <summary>The spec's cap, computed here independently (central differences).</summary>
        static double SpecCap(ISnapMetric m, Vec2 p, double tile)
        {
            const double e = 1e-4;
            m.TryToScreen(new Vec2(p.X - e, p.Y), out Vec2 x0);
            m.TryToScreen(new Vec2(p.X + e, p.Y), out Vec2 x1);
            m.TryToScreen(new Vec2(p.X, p.Y - e), out Vec2 y0);
            m.TryToScreen(new Vec2(p.X, p.Y + e), out Vec2 y1);
            Vec2 j1 = (x1 - x0) * (1 / (2 * e)), j2 = (y1 - y0) * (1 / (2 * e));
            double t = j1.X * j1.X + j1.Y * j1.Y + j2.X * j2.X + j2.Y * j2.Y, d = j1.X * j2.Y - j1.Y * j2.X;
            double sigma = Math.Sqrt((t + Math.Sqrt(Math.Max(0, t * t - 4 * d * d))) / 2);
            return Math.Max(0.03, Math.Min(Math.Max(0.10, 0.5 * tile), 1.5 * m.TolerancePx / sigma));
        }

        static PointerEvent Ev(PointerPhase phase, double x, double y) =>
            new PointerEvent(phase, new Vec2(x, y), true, 0);

        static void Drag(EditorSession s, double ax, double ay, double bx, double by)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= 5; i++)
            {
                double k = i / 5.0;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * k, ay + (by - ay) * k));
            }
            s.Pointer(Ev(PointerPhase.Up, bx, by));
        }

        static LineShape LastLine(EditorSession s)
        {
            var shapes = s.ActiveLayer.Shapes;
            return (LineShape)shapes[shapes.Count - 1];
        }

        static double ScreenDistance(ISnapMetric m, Vec2 a, Vec2 b)
        {
            m.TryToScreen(a, out Vec2 sa);
            m.TryToScreen(b, out Vec2 sb);
            return Vec2.Distance(sa, sb);
        }

        static string P(Vec2 v) => $"({v.X:R}, {v.Y:R})";

        public static void CapConstantsAndNoMetricFloor()
        {
            Check.That(EditorSession.SnapCapFloor == 0.03, "floor 0.03 board units (1.2 cm on the 40 cm board)");
            Check.That(EditorSession.SnapCapCeilingMin == 0.10 && EditorSession.SnapCapStretch == 1.5, "ceiling 0.10, stretch 1.5");
            Check.That(EditorSession.FallbackSnapTolerance <= EditorSession.SnapCapFloor, "the board-unit fallback is inside the floor");
            Check.That(new EditorSession().BoardCap(new Vec2(0.5, 0.5)) == 0.03, "without a metric the cap is 0.03");
        }

        public static void TopDownCapNeverBindsAndA28PxDotSnaps()
        {
            var m = new TopDownMetric();   // 400 px per unit: 0.03 board units is only 12 px
            var s = new EditorSession { SnapMetric = m };
            Check.Near(s.BoardCap(new Vec2(0.3, 0.3)), 1.5 * Tol / 400, "top-down cap = 1.5 tol / scale", 1e-9);
            var corner = new Vec2(0.5, 0.5);
            var raw = new Vec2(0.5 - 0.07, 0.5);   // 28 px, 0.07 board units: the old 0.03 cap blocked this
            Check.That(Vec2.Distance(raw, corner) * 400 <= Tol, "precondition: inside 32 px");
            Drag(s, 0.1, 0.12, raw.X, raw.Y);
            Check.That(LastLine(s).B == corner, $"28 px top-down snaps exactly, got {P(LastLine(s).B)}");
        }

        public static void FarEdgeCandidateInsidePixelsButBeyondCapDoesNotSnap()
        {
            var m = new GrazingMetric();
            var s = new EditorSession { SnapMetric = m };
            s.SetGrid(1);   // few grid points: only the target is near
            var corner = new Vec2(0.5, 1.0);   // grid corner on the far edge
            var raw = new Vec2(0.5, 0.78);     // 0.22 board units short of it
            double cap = SpecCap(m, raw, 0.5);
            Check.Near(s.BoardCap(raw), cap, "engine cap matches the spec", cap * 0.01);
            Check.That(ScreenDistance(m, raw, corner) <= Tol,
                $"precondition: on screen only {ScreenDistance(m, raw, corner):F1} px apart (inside 32 px)");
            Check.That(Vec2.Distance(raw, corner) > cap * 1.05, $"precondition: beyond the cap {cap:F3}");
            Drag(s, 0.31, 0.43, raw.X, raw.Y);
            Check.That(LastLine(s).B == raw, $"grazing view still capped: stays raw, got {P(LastLine(s).B)}");
        }

        public static void FarEdgeCandidateWithinCapSnaps()
        {
            var m = new GrazingMetric();
            var s = new EditorSession { SnapMetric = m };
            s.SetGrid(1);   // few grid points: only the target is near
            var corner = new Vec2(0.5, 1.0);
            var raw = new Vec2(0.5, 0.94);     // 0.06 board units: beyond the old 0.03, inside the adaptive cap and 32 px
            Check.That(ScreenDistance(m, raw, corner) <= Tol, "precondition: inside 32 px");
            Check.That(Vec2.Distance(raw, corner) < SpecCap(m, raw, 0.5) && Vec2.Distance(raw, corner) > 0.03,
                "precondition: inside the adaptive cap, beyond the old fixed cap");
            Drag(s, 0.31, 0.43, raw.X, raw.Y);
            Check.That(LastLine(s).B == corner, $"snaps exactly to the far corner, got {P(LastLine(s).B)}");
        }

        public static void MagnifiedViewKeepsTheFloor_FarTinyBoardHitsTheTileCeiling()
        {
            var s = new EditorSession { SnapMetric = new TopDownMetric { Scale = 20000 } };
            Check.That(s.BoardCap(new Vec2(0.4, 0.4)) == 0.03, "huge magnification: floor 0.03");
            var far = new TopDownMetric { Scale = 60 };   // 1.5 * 32 / 60 = 0.8: held by the ceiling
            var t = new EditorSession { SnapMetric = far };
            t.SetGrid(2);
            Check.Near(t.BoardCap(new Vec2(0.5, 0.5)), 0.25, "2x2 square: half a tile", 1e-12);
            t.SetGrid(4);
            Check.Near(t.BoardCap(new Vec2(0.5, 0.5)), 0.125, "4x4 square: half a tile", 1e-12);
            t.SetGridKind(GridKind.Hex);
            Check.Near(t.BoardCap(new Vec2(0.5, 0.5)), Math.Max(0.10, 0.5 * TileGrid.HexRadius(4)), "4x4 hex: half the hex radius", 1e-12);
        }

        public static void FailedProjectionFallsBackToTheFloor()
        {
            var s = new EditorSession { SnapMetric = new TopDownMetric { FailRight = true } };
            Check.That(s.BoardCap(new Vec2(0.8999995, 0.5)) == 0.03, "the x probe fails: 0.03");
            Check.Near(s.BoardCap(new Vec2(0.5, 0.5)), 1.5 * Tol / 400, "elsewhere the adaptive cap", 1e-9);
        }

        public static void NearEdgeScreenToleranceStillLimits()
        {
            // Near the bottom edge the metric magnifies, so pixels are the tighter limit there; the cap changes nothing.
            var m = new GrazingMetric();
            var corner = new Vec2(0, 0);
            var inside = new Vec2(0, 0.025);   // 23.3 px
            var outside = new Vec2(0, 0.036);  // 32.5 px, inside the cap
            Check.That(ScreenDistance(m, inside, corner) <= Tol && ScreenDistance(m, outside, corner) > Tol, "precondition: pixels");
            Check.That(0.036 < SpecCap(m, outside, 0.5), "precondition: inside the cap");
            var s = new EditorSession { SnapMetric = m };
            s.SetGrid(1);   // few grid points: only the target is near
            Drag(s, 0.3, 0.3, inside.X, inside.Y);
            Check.That(LastLine(s).B == corner, $"23 px: snaps, got {P(LastLine(s).B)}");
            Drag(s, 0.3, 0.3, outside.X, outside.Y);
            Check.That(LastLine(s).B == outside, $"32.5 px: stays raw although inside the cap, got {P(LastLine(s).B)}");
        }

        public static void OrthoLockAlignmentRespectsPerpendicularCap()
        {
            // Horizontal axis at y = 0.9 (far side). A user point at (0.71, 0.70) is ~17 px off the axis on screen but
            // 0.2 board units off it, beyond BoardCap at the end: without the cap the end would align to x = 0.71.
            var m = new GrazingMetric();
            var s = new EditorSession { SnapMetric = m };
            s.SetGrid(1);   // few grid points: only the target is near
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.71, 0.70)));
            m.TryToScreen(new Vec2(0.71, 0.70), out Vec2 q);
            m.TryToScreen(new Vec2(0.71, 0.9), out Vec2 onAxis);
            Check.That(Math.Abs(q.Y - onAxis.Y) <= Tol, $"precondition: {Math.Abs(q.Y - onAxis.Y):F1} px off the axis");
            Check.That(0.2 > SpecCap(m, new Vec2(0.70, 0.9), 0.5) * 1.05, "precondition: beyond the cap at the end");
            s.SetOrthoLock(true);
            Drag(s, 0.3, 0.9, 0.70, 0.905);
            var line = LastLine(s);
            Check.That(line.A == new Vec2(0.3, 0.9) && line.B.Y == line.A.Y, "exactly horizontal from the raw start");
            Check.That(line.B == new Vec2(0.70, 0.9), $"perpendicular cap: end stays at x = 0.70, got {P(line.B)}");
        }

        public static void OrthoLockAlongAxisRespectsCap()
        {
            // Vertical axis x = 0.3 running into the compressed far side. The end is at y = 0.80; a user point ON the
            // axis at y = 1.0 is 0.2 along (about 20 px) but beyond BoardCap at the end; one at y = 0.9 is inside it.
            var m = new GrazingMetric();
            Check.That(0.2 > SpecCap(m, new Vec2(0.3, 0.8), 1.0) * 1.05 && 0.1 < SpecCap(m, new Vec2(0.3, 0.8), 1.0),
                "precondition: 0.2 beyond the cap, 0.1 inside");
            Check.That(ScreenDistance(m, new Vec2(0.3, 0.8), new Vec2(0.3, 1.0)) <= Tol, "precondition: 0.2 along is inside 32 px");
            var s = new EditorSession { SnapMetric = m };
            s.SetGrid(1);
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.3, 1.0)));
            s.SetOrthoLock(true);
            Drag(s, 0.3, 0.3, 0.305, 0.80);
            Check.That(LastLine(s).B == new Vec2(0.3, 0.8), $"beyond the cap along the axis: stays, got {P(LastLine(s).B)}");
            var t = new EditorSession { SnapMetric = m };
            t.SetGrid(1);
            t.ActiveLayer.SnapPoints.Add(new SnapPoint(t.Board.NewId(), new Vec2(0.3, 0.9)));
            t.SetOrthoLock(true);
            Drag(t, 0.3, 0.3, 0.305, 0.80);
            Check.That(LastLine(t).B == new Vec2(0.3, 0.9), $"inside the cap: aligns exactly, got {P(LastLine(t).B)}");
        }

        public static void OrthoLockAlignmentWithinCapStillSnaps()
        {
            var m = new GrazingMetric();
            var s = new EditorSession { SnapMetric = m };
            s.SetGrid(1);   // few grid points: only the target is near
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.71, 0.92)));   // 0.02 off the axis
            s.SetOrthoLock(true);
            Drag(s, 0.3, 0.9, 0.70, 0.905);
            Check.That(LastLine(s).B == new Vec2(0.71, 0.9), $"aligns to the user point's x exactly, got {P(LastLine(s).B)}");
        }
    }
}
