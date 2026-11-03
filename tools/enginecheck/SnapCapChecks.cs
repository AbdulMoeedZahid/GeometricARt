using System;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Board-distance cap (EditorSession.MaxSnapBoardDistance): a candidate snaps only if it is within the screen
    /// tolerance AND within the cap on the board. Uses a strongly perspective fake metric (a grazing view: the far edge is
    /// compressed 4x), where 24 px covers far more than 0.03 board units. Each test first proves the candidate IS inside
    /// the screen tolerance, so the cap (not the pixels) is what decides.
    /// </summary>
    public static class SnapCapChecks
    {
        /// <summary>w = 1 + 3y, screen = 1000 * (x, y) / w. Near edge ~1000 px per unit, far edge ~62 px per unit in y.</summary>
        sealed class GrazingMetric : ISnapMetric
        {
            public double TolerancePx => 24;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                double w = 1 + 3 * board.Y;
                screenPx = new Vec2(1000 * board.X / w, 1000 * board.Y / w);
                return true;
            }
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

        public static void CapIsANamedConstant()
        {
            Check.That(EditorSession.MaxSnapBoardDistance == 0.03, "cap is 0.03 board units (1.2 cm on the 40 cm board)");
            Check.That(EditorSession.FallbackSnapTolerance <= EditorSession.MaxSnapBoardDistance,
                "the board-unit fallback tolerance is inside the cap");
        }

        public static void FarEdgeCandidateInsidePixelsButBeyondCapDoesNotSnap()
        {
            var m = new GrazingMetric();
            var s = new EditorSession { SnapMetric = m };
            var corner = new Vec2(0.5, 1.0);   // grid corner on the far edge
            var raw = new Vec2(0.5, 0.94);     // 0.06 board units short of it
            Check.That(ScreenDistance(m, raw, corner) <= m.TolerancePx,
                $"precondition: on screen only {ScreenDistance(m, raw, corner):F1} px apart (inside 24 px)");
            Check.That(Vec2.Distance(raw, corner) > EditorSession.MaxSnapBoardDistance, "precondition: beyond the cap");
            Drag(s, 0.31, 0.43, raw.X, raw.Y);
            Check.That(LastLine(s).B == raw, $"board cap: stays raw, got {P(LastLine(s).B)}");
        }

        public static void FarEdgeCandidateWithinCapSnaps()
        {
            var m = new GrazingMetric();
            var s = new EditorSession { SnapMetric = m };
            var corner = new Vec2(0.5, 1.0);
            var raw = new Vec2(0.5, 0.975);    // 0.025 board units: inside the cap and inside 24 px
            Check.That(ScreenDistance(m, raw, corner) <= m.TolerancePx, "precondition: inside 24 px");
            Drag(s, 0.31, 0.43, raw.X, raw.Y);
            Check.That(LastLine(s).B == corner, $"snaps exactly to the far corner, got {P(LastLine(s).B)}");
        }

        public static void NearEdgeScreenToleranceStillLimits()
        {
            // Near the bottom edge the metric magnifies, so pixels are the tighter limit there; the cap changes nothing.
            var m = new GrazingMetric();
            var corner = new Vec2(0, 0);
            var inside = new Vec2(0, 0.02);    // 18.9 px, 0.02 board units
            var outside = new Vec2(0, 0.028);  // 25.8 px, 0.028 board units (inside the cap, outside the pixels)
            Check.That(ScreenDistance(m, inside, corner) <= 24 && ScreenDistance(m, outside, corner) > 24, "precondition: pixels");
            var s = new EditorSession { SnapMetric = m };
            Drag(s, 0.3, 0.3, inside.X, inside.Y);
            Check.That(LastLine(s).B == corner, $"18.9 px and 0.02 board: snaps, got {P(LastLine(s).B)}");
            Drag(s, 0.3, 0.3, outside.X, outside.Y);
            Check.That(LastLine(s).B == outside, $"25.8 px: stays raw although inside the cap, got {P(LastLine(s).B)}");
        }

        public static void OrthoLockAlignmentRespectsPerpendicularCap()
        {
            // Horizontal axis at y = 0.9 (far side). A user point at (0.71, 0.96) is ~4 px off the axis on screen but
            // 0.06 board units off it: without the cap the end would align to x = 0.71.
            var m = new GrazingMetric();
            var s = new EditorSession { SnapMetric = m };
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.71, 0.96)));
            m.TryToScreen(new Vec2(0.71, 0.96), out Vec2 q);
            m.TryToScreen(new Vec2(0.71, 0.9), out Vec2 onAxis);
            Check.That(Math.Abs(q.Y - onAxis.Y) <= 24, $"precondition: {Math.Abs(q.Y - onAxis.Y):F1} px off the axis");
            s.SetOrthoLock(true);
            Drag(s, 0.3, 0.9, 0.70, 0.905);
            var line = LastLine(s);
            Check.That(line.A == new Vec2(0.3, 0.9) && line.B.Y == line.A.Y, "exactly horizontal from the raw start");
            Check.That(line.B == new Vec2(0.70, 0.9), $"perpendicular cap: end stays at x = 0.70, got {P(line.B)}");
        }

        public static void OrthoLockAlignmentWithinCapStillSnaps()
        {
            var m = new GrazingMetric();
            var s = new EditorSession { SnapMetric = m };
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.71, 0.92)));   // 0.02 off the axis
            s.SetOrthoLock(true);
            Drag(s, 0.3, 0.9, 0.70, 0.905);
            Check.That(LastLine(s).B == new Vec2(0.71, 0.9), $"aligns to the user point's x exactly, got {P(LastLine(s).B)}");
        }
    }
}
