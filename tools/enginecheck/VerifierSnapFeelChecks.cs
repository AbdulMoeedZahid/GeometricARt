using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER (ui-line-plan 4.2, P3): snapping with a capture radius, written from the plan only.
    ///   Radius      the engine uses metric.TolerancePx (32 on device); tap slop is 10 of 32.
    ///   BoardCap    clamp(1.5 tol / sigma_max, 0.03, max(0.10, 0.5 tile)), 0.03 when a projection fails. Used by the
    ///               plain snap and both H/V-lock places.
    ///   Tie window  grid fillers (CellCorner, CellEdgeMid, CellCenter) lose to a shape/construction point within
    ///               6/32 tol of the nearest distance; shape and construction points compete by distance only.
    ///   Sticky      after a snap to C in a plain Line/Circle drag, stay on C while within 1.5 tol and BoardCap of C,
    ///               unless another in-range candidate is nearer by more than 4/32 tol. Resets per gesture.
    ///   Event       EditorSession.SnapAcquired(SnapCandidate) once per new acquisition (Down that snaps, or a change).
    /// Capture tests use Down (+ Cancel) so the sticky rule cannot interfere; sticky tests use explicit Move paths.
    /// Every geometric test asserts its own preconditions (pixel and board distances) with the spec formula.
    /// </summary>
    public static class VerifierSnapFeelChecks
    {
        // Spec values (plan 4.2), restated so the checks do not read them from the code under test.
        const double Tol = 32;              // reference px on device; the fake metrics below report it directly
        const double Floor = 0.03;
        const double MinCeiling = 0.10;
        const double TieWindowPx = 6.0 / 32 * Tol;
        const double StickyFactor = 1.5;
        const double StickyMarginPx = 4.0 / 32 * Tol;
        const double Eps = 1e-3;            // Jacobian step

        // ================= metrics =================

        /// <summary>screen = (Kx x + Sxy y + Ox, Ky y + Oy); fails when x &gt; FailAboveX or y &gt; FailAboveY.</summary>
        sealed class AffineMetric : ISnapMetric
        {
            public double Kx = 400, Ky = 400, Sxy, Ox = 13, Oy = -7, T = Tol;
            public double FailAboveX = double.PositiveInfinity, FailAboveY = double.PositiveInfinity;
            public int Calls;
            public double TolerancePx => T;

            public bool TryToScreen(Vec2 b, out Vec2 s)
            {
                Calls++;
                if (b.X > FailAboveX || b.Y > FailAboveY) { s = default; return false; }
                s = new Vec2(Kx * b.X + Sxy * b.Y + Ox, Ky * b.Y + Oy);
                return true;
            }
        }

        /// <summary>Grazing view: w = 1 + 3y, screen = 1000 (x, y) / w (the far edge is compressed ~4x in depth).</summary>
        sealed class GrazingMetric : ISnapMetric
        {
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 b, out Vec2 s)
            {
                double w = 1 + 3 * b.Y;
                s = new Vec2(1000 * b.X / w, 1000 * b.Y / w);
                return true;
            }
        }

        // ================= helpers =================

        static double s_T;
        static int s_acquired;
        static SnapCandidate s_lastAcquired;
        static PointerEvent Ev(PointerPhase p, double x, double y) => new PointerEvent(p, new Vec2(x, y), true, s_T += 0.016);
        static Vec2 V(double x, double y) => new Vec2(x, y);
        static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        static string P(Vec2? v) => v.HasValue ? "(" + R(v.Value.X) + ", " + R(v.Value.Y) + ")" : "null";

        static void Down(EditorSession s, double x, double y) => s.Pointer(Ev(PointerPhase.Down, x, y));
        static void MoveTo(EditorSession s, double x, double y) => s.Pointer(Ev(PointerPhase.Move, x, y));
        static void Up(EditorSession s, double x, double y) => s.Pointer(Ev(PointerPhase.Up, x, y));
        static void Cancel(EditorSession s) => s.Pointer(new PointerEvent(PointerPhase.Cancel, V(0, 0), false, s_T += 0.016));

        /// <summary>A session on an empty board with GridN = n (no undo step needed: the board is built first).</summary>
        static EditorSession NewSession(ISnapMetric m, int gridN = 1, GridKind kind = GridKind.Square)
        {
            var b = GoldenBoards.Empty();
            b.GridN = gridN;
            b.GridKind = kind;
            var s = new EditorSession(b) { SnapMetric = m };
            s.SnapAcquired += OnAcquired;
            return s;
        }

        static void OnAcquired(SnapCandidate c)
        {
            s_acquired++;
            s_lastAcquired = c;
        }

        static SnapPoint AddUserPoint(EditorSession s, double x, double y)
        {
            var p = new SnapPoint(s.Board.NewId(), V(x, y));
            s.ActiveLayer.SnapPoints.Add(p);
            s.LoadBoard(s.Board);   // a model change the engine sees (marks candidates dirty); also resets gestures
            return p;
        }

        static LineShape AddLine(EditorSession s, Vec2 a, Vec2 b)
        {
            var l = new LineShape(s.Board.NewId(), a, b, StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(l);
            s.LoadBoard(s.Board);
            return l;
        }

        static CircleShape AddCircle(EditorSession s, Vec2 c, double r)
        {
            var sh = new CircleShape(s.Board.NewId(), c, r, StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(sh);
            s.LoadBoard(s.Board);
            return sh;
        }

        static double Px(ISnapMetric m, Vec2 a, Vec2 b)
        {
            Check.That(m.TryToScreen(a, out Vec2 sa) & m.TryToScreen(b, out Vec2 sb), "precondition: projectable");
            return Vec2.Distance(sa, sb);
        }

        static double TileSize(Board b) =>
            b.GridKind == GridKind.Hex ? TileGrid.HexRadius(b.GridN) : 1.0 / TileGrid.ClampN(b.GridN);

        /// <summary>The spec's BoardCap, computed independently (forward-difference Jacobian, eps 1e-3).</summary>
        static double SpecCap(ISnapMetric m, Board b, Vec2 p)
        {
            double ceiling = Math.Max(MinCeiling, 0.5 * TileSize(b));
            if (m == null) return Floor;
            if (!m.TryToScreen(p, out Vec2 s0) || !m.TryToScreen(V(p.X + Eps, p.Y), out Vec2 sx)
                || !m.TryToScreen(V(p.X, p.Y + Eps), out Vec2 sy)) return Floor;
            double j1x = (sx.X - s0.X) / Eps, j1y = (sx.Y - s0.Y) / Eps;
            double j2x = (sy.X - s0.X) / Eps, j2y = (sy.Y - s0.Y) / Eps;
            double t = j1x * j1x + j1y * j1y + j2x * j2x + j2y * j2y;
            double d = j1x * j2y - j1y * j2x;
            double sigma = Math.Sqrt((t + Math.Sqrt(Math.Max(0, t * t - 4 * d * d))) / 2);
            double cap = 1.5 * m.TolerancePx / sigma;
            return Math.Max(Floor, Math.Min(ceiling, cap));
        }

        /// <summary>EditorSession.BoardCap(Vec2) by reflection (its visibility is not fixed by the plan).</summary>
        static double BoardCap(EditorSession s, Vec2 p)
        {
            var m = typeof(EditorSession).GetMethod("BoardCap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Vec2) }, null);
            Check.That(m != null && m.ReturnType == typeof(double), "EditorSession.BoardCap(Vec2) -> double exists (plan 4.2)");
            return (double)m.Invoke(s, new object[] { p });
        }

        /// <summary>Cursor after a Line-tool Down at (x, y) (the Down's snap result), then Cancel.</summary>
        static Vec2? DownCursor(EditorSession s, double x, double y)
        {
            Down(s, x, y);
            Vec2? c = s.Cursor;
            Cancel(s);
            return c;
        }

        static void ExpectDownSnaps(EditorSession s, double x, double y, Vec2 expected, string what)
        {
            Vec2? c = DownCursor(s, x, y);
            Check.That(c.HasValue && c.Value == expected, $"{what}: Down at {P(V(x, y))} snaps exactly to {P(expected)}, got {P(c)}");
        }

        static void ExpectDownRaw(EditorSession s, double x, double y, string what)
        {
            Vec2? c = DownCursor(s, x, y);
            Check.That(c.HasValue && c.Value == V(x, y), $"{what}: Down at {P(V(x, y))} stays raw, got {P(c)}");
        }

        static LineShape LastLine(EditorSession s)
        {
            var sh = s.ActiveLayer.Shapes;
            Check.That(sh.Count > 0 && sh[sh.Count - 1] is LineShape, "a line was committed");
            return (LineShape)sh[sh.Count - 1];
        }

        // ================= constants =================

        public static void V_TapSlopIsTenOfThirtyTwo()
        {
            Check.Near(EditorSession.TapSlopFraction, 10.0 / 32.0, "TapSlopFraction = 10/32 (still 10 px at the 32 px radius)", 1e-15);
            Check.That(EditorSession.FallbackSnapTolerance == 0.02, "board-unit fallback unchanged (0.02)");
        }

        // ================= BoardCap formula (direct) =================

        public static void V_BoardCapTopDownIsOnePointFiveReach()
        {
            var m = new AffineMetric { Kx = 400, Ky = 400 };
            var s = NewSession(m, 2);
            double cap = BoardCap(s, V(0.37, 0.61));
            Check.Near(cap, 1.5 * Tol / 400, "isotropic 400 px/unit: 1.5 * 32 / 400 = 0.12", 1e-9);
            Check.Near(cap, SpecCap(m, s.Board, V(0.37, 0.61)), "matches the spec formula", 1e-9);
        }

        public static void V_BoardCapUsesTheMostStretchedDirection()
        {
            // Anisotropic and sheared: sigma_max, not sigma_min or an axis scale, sets the cap.
            var m = new AffineMetric { Kx = 400, Ky = 100, Sxy = 0 };
            var s = NewSession(m, 1);
            Check.Near(BoardCap(s, V(0.5, 0.5)), 1.5 * Tol / 400, "(400, 100): sigma_max = 400", 1e-9);
            var sh = new AffineMetric { Kx = 300, Ky = 120, Sxy = 250 };
            var s2 = NewSession(sh, 1);
            double expected = SpecCap(sh, s2.Board, V(0.2, 0.8));
            Check.That(expected > Floor && expected < 0.5, $"precondition: sheared cap {R(expected)} not clamped");
            Check.Near(BoardCap(s2, V(0.2, 0.8)), expected, "sheared: sigma_max of the full 2x2 Jacobian", 1e-9);
        }

        public static void V_BoardCapPerspectiveMatchesSpec()
        {
            var m = new GrazingMetric();
            var s = NewSession(m, 1);
            foreach (var p in new[] { V(0.5, 0.8), V(0.1, 0.95), V(0.9, 0.05), V(0.5, 0.5) })
            {
                double exp = SpecCap(m, s.Board, p);
                double got = BoardCap(s, p);
                Check.That(Math.Abs(got - exp) <= 1e-2 * exp, $"grazing at {P(p)}: cap {R(got)} vs spec {R(exp)} (1% for the difference scheme)");
            }
        }

        public static void V_BoardCapFloorIsPointZeroThree()
        {
            var m = new AffineMetric { Kx = 5000, Ky = 5000 };   // 1.5 * 32 / 5000 = 0.0096 -> floor
            var s = NewSession(m, 2);
            Check.That(BoardCap(s, V(0.4, 0.4)) == Floor, $"huge magnification: floor 0.03, got {R(BoardCap(s, V(0.4, 0.4)))}");
        }

        public static void V_BoardCapCeilingScalesWithTheTile()
        {
            var m = new AffineMetric { Kx = 100, Ky = 100 };     // 1.5 * 32 / 100 = 0.48 before the ceiling
            var cases = new (int n, GridKind k, double expected)[]
            {
                (1, GridKind.Square, 0.48),                       // ceiling 0.5 does not bind
                (2, GridKind.Square, 0.25),
                (3, GridKind.Square, 0.5 / 3),
                (4, GridKind.Square, 0.125),
                (1, GridKind.Hex, 0.5 * TileGrid.HexRadius(1)),
                (2, GridKind.Hex, 0.5 * TileGrid.HexRadius(2)),
                (4, GridKind.Hex, MinCeiling),                    // 0.5 * 0.128 < 0.10: the ceiling's own floor
            };
            foreach (var c in cases)
            {
                var s = NewSession(m, c.n, c.k);
                double got = BoardCap(s, V(0.5, 0.5));
                Check.Near(got, c.expected, $"{c.k} {c.n}x{c.n} far board: cap", 1e-9);
            }
        }

        public static void V_BoardCapFallsBackWhenAProjectionFails()
        {
            var s = NewSession(new AffineMetric { FailAboveX = 0.6 }, 1);
            Check.That(BoardCap(s, V(0.5995, 0.5)) == Floor, "p + (eps, 0) unprojectable: 0.03");
            var s2 = NewSession(new AffineMetric { FailAboveY = 0.6 }, 1);
            Check.That(BoardCap(s2, V(0.3, 0.5995)) == Floor, "p + (0, eps) unprojectable: 0.03");
            Check.That(BoardCap(s2, V(0.3, 0.7)) == Floor, "p itself unprojectable: 0.03");
        }

        public static void V_BoardCapWithoutMetricIsTheFloor()
        {
            // Not stated outright: without a metric the fallback tolerance (0.02) is used and the cap stays 0.03.
            var s = NewSession(null, 2);
            Check.That(BoardCap(s, V(0.5, 0.5)) == Floor, $"no metric: 0.03, got {R(BoardCap(s, V(0.5, 0.5)))}");
        }

        // ================= capture radius + adaptive cap (behaviour) =================

        public static void V_ThirtyTwoPixelRadiusTopDownSnaps()
        {
            // 400 px per board unit (a 40 cm board filling ~400 px): 28 px = 0.07 board units, beyond the old 0.03 cap.
            var m = new AffineMetric();
            var s = NewSession(m, 1);
            var c = V(0.5, 0.5);   // the 1x1 tile centre
            Check.That(Math.Abs(Px(m, V(0.57, 0.5), c) - 28) < 1e-9, "precondition: 28 px");
            ExpectDownSnaps(s, 0.57, 0.5, c, "28 px (inside 32, outside the old 24 and the old 0.03 cap)");
            ExpectDownSnaps(s, 0.5, 0.5 - 0.0795, c, "31.8 px");
            ExpectDownRaw(s, 0.585, 0.5, "34 px is outside the radius");
        }

        public static void V_ThirtyTwoPixelRadiusDragEndCommitsExactly()
        {
            var s = NewSession(new AffineMetric(), 1);
            Down(s, 0.1, 0.9);
            for (int i = 1; i <= 8; i++) MoveTo(s, 0.1 + (0.57 - 0.1) * i / 8, 0.9 + (0.5 - 0.9) * i / 8);
            Up(s, 0.57, 0.5);
            var line = LastLine(s);
            Check.That(line.A == V(0.1, 0.9), $"raw start, got {P(line.A)}");
            Check.That(line.B == V(0.5, 0.5), $"end == candidate exactly (==), got {P(line.B)}");
        }

        public static void V_GrazingViewIsStillCapped()
        {
            var m = new GrazingMetric();
            var s = NewSession(m, 1);
            var far = V(0.5, 1.0);                   // far edge midpoint (1x1 grid)
            var p = V(0.5, 0.8);
            double cap = SpecCap(m, s.Board, p);
            Check.That(Px(m, p, far) < Tol, $"precondition: {Px(m, p, far):F1} px, inside the radius");
            Check.That(Vec2.Distance(p, far) > cap * 1.05, $"precondition: 0.2 board units beyond the cap {R(cap)}");
            Check.That(Px(m, p, V(0.5, 0.5)) > Tol, "precondition: the tile centre is out of pixel range");
            ExpectDownRaw(s, p.X, p.Y, "grazing: inside 32 px but beyond BoardCap");

            var q = V(0.5, 0.9);
            double capQ = SpecCap(m, s.Board, q);
            Check.That(Vec2.Distance(q, far) < capQ * 0.95 && Px(m, q, far) < Tol, "precondition: inside cap and pixels");
            ExpectDownSnaps(s, q.X, q.Y, far, "grazing, within the cap");
        }

        public static void V_ProjectionFailureFallsBackToTheOldCap()
        {
            var m = new AffineMetric { FailAboveX = 0.6 };
            var s = NewSession(m, 1);
            AddUserPoint(s, 0.55, 0.5);
            Check.That(Px(m, V(0.5995, 0.5), V(0.55, 0.5)) < Tol, "precondition: 19.8 px");
            ExpectDownRaw(s, 0.5995, 0.5, "p + eps unprojectable: cap 0.03 < 0.0495");
            ExpectDownSnaps(s, 0.5795, 0.5, V(0.55, 0.5), "control: p + eps projectable again (cap 0.12)");
            var ok = NewSession(new AffineMetric(), 1);
            AddUserPoint(ok, 0.55, 0.5);
            ExpectDownSnaps(ok, 0.5995, 0.5, V(0.55, 0.5), "control: same layout, projectable -> cap 0.12 -> snaps");
        }

        public static void V_OrthoLockUsesBoardCapPerpendicular()
        {
            // Axis y = 0.7. A user point 0.06 off the axis (24 px): beyond the old 0.03 cap, inside BoardCap 0.12.
            var m = new AffineMetric();
            var s = NewSession(m, 1);
            AddUserPoint(s, 0.71, 0.76);
            s.SetOrthoLock(true);
            Down(s, 0.3, 0.7);
            Check.That(s.Cursor == V(0.3, 0.7), $"precondition: raw start, got {P(s.Cursor)}");
            for (int i = 1; i <= 6; i++) MoveTo(s, 0.3 + 0.4 * i / 6, 0.7 + 0.005 * i / 6);
            Up(s, 0.70, 0.705);
            var line = LastLine(s);
            Check.That(line.A.Y == line.B.Y, "exactly horizontal");
            Check.That(line.B == V(0.71, 0.7), $"aligned to the user point's x via BoardCap, got {P(line.B)}");
        }

        public static void V_OrthoLockUsesBoardCapAlongTheAxis()
        {
            var m = new AffineMetric();
            var s = NewSession(m, 1);
            AddUserPoint(s, 0.76, 0.7);   // on the axis, 0.06 (24 px) beyond the raw end along it
            s.SetOrthoLock(true);
            Down(s, 0.3, 0.7);
            for (int i = 1; i <= 6; i++) MoveTo(s, 0.3 + 0.4 * i / 6, 0.7);
            Up(s, 0.70, 0.7);
            Check.That(LastLine(s).B == V(0.76, 0.7), $"along-axis offer within BoardCap snaps, got {P(LastLine(s).B)}");
        }

        public static void V_OrthoLockGrazingStillCapped()
        {
            // Far side of a grazing view: a point 0.25 off the axis can be a few px away on screen; BoardCap blocks it.
            var m = new GrazingMetric();
            var s = NewSession(m, 1);
            var up = V(0.71, 0.98);
            AddUserPoint(s, up.X, up.Y);
            var axisEnd = V(0.70, 0.73);
            double cap = SpecCap(m, s.Board, axisEnd);
            Check.That(up.Y - axisEnd.Y > cap * 1.05, $"precondition: 0.25 off axis beyond cap {R(cap)}");
            m.TryToScreen(up, out Vec2 su);
            m.TryToScreen(V(up.X, axisEnd.Y), out Vec2 sa);
            m.TryToScreen(axisEnd, out Vec2 s0);
            m.TryToScreen(V(1.0, axisEnd.Y), out Vec2 s1);
            double ux = s1.X - s0.X, uy = s1.Y - s0.Y, len = Math.Sqrt(ux * ux + uy * uy);
            double perp = Math.Abs((ux * (su.Y - s0.Y) - uy * (su.X - s0.X)) / len);
            Check.That(perp < Tol, $"precondition: {perp:F1} px off the axis on screen (inside 32)");
            s.SetOrthoLock(true);
            Down(s, 0.3, 0.73);
            for (int i = 1; i <= 6; i++) MoveTo(s, 0.3 + 0.4 * i / 6, 0.73 + 0.004 * i / 6);
            Up(s, 0.70, 0.734);
            Check.That(LastLine(s).B == axisEnd, $"perpendicular part beyond BoardCap: end stays at x = 0.70, got {P(LastLine(s).B)}");
        }

        // ================= tie window =================

        /// <summary>Lines x = mx + gap (vertical) and y = 0.5 (horizontal) crossing at X = (0.25 + gap, 0.5); grid 2x2.</summary>
        static EditorSession TieLayout(double gap, out Vec2 mid, out Vec2 x)
        {
            var s = NewSession(new AffineMetric(), 2);
            mid = V(0.25, 0.5);                      // CellEdgeMid of the 2x2 grid
            x = V(0.25 + gap, 0.5);
            AddLine(s, V(x.X, 0.32), V(x.X, 0.68));
            AddLine(s, V(0.25 + gap - 0.15, 0.5), V(0.25 + gap + 0.15, 0.5));
            bool found = false;
            foreach (var c in s.SnapCandidates)
                if (c.Position == x && c.Kind == SnapKind.Intersection) found = true;
            Check.That(found, $"precondition: an Intersection candidate at {P(x)}");
            return s;
        }

        public static void V_TieWindowGridMidpointThreePxNearerLosesToIntersection()
        {
            var s = TieLayout(0.0125, out Vec2 mid, out Vec2 x);   // 5 px apart
            double px = 0.25 + 0.0025;                              // d(mid) = 1 px, d(X) = 4 px: 3 px nearer the grid
            Check.Near((x.X - px) * 400 - (px - mid.X) * 400, 3, "precondition: grid point 3 px nearer", 1e-9);
            ExpectDownSnaps(s, px, 0.5, x, "grid filler within 6 px of the nearest loses");
        }

        public static void V_TieWindowBoundary()
        {
            var s = TieLayout(0.03, out Vec2 mid, out Vec2 x);     // 12 px apart
            ExpectDownSnaps(s, 0.26, 0.5, x, "grid 4 px nearer (d 4 vs 8): intersection wins");
            ExpectDownSnaps(s, 0.255, 0.5, mid, "grid 8 px nearer (d 2 vs 10): beyond the 6 px window, grid wins");
            ExpectDownSnaps(s, 0.2515, 0.5, mid, "grid 10.8 px nearer: grid wins");
        }

        public static void V_TieWindowDoesNotApplyBetweenShapePoints()
        {
            // Circle (0.6, 0.4) r 0.2: division D = (0.8, 0.4). Vertical line x = 0.799 crosses it at X ~ (0.799, 0.41998),
            // 0.02 board units (8 px) from D.
            var s = NewSession(new AffineMetric(), 2);
            AddCircle(s, V(0.6, 0.4), 0.2);
            AddLine(s, V(0.799, 0.2), V(0.799, 0.62));
            Vec2 d = V(0.8, 0.4), x = default;
            var xExpected = V(0.799, 0.4 + Math.Sqrt(0.04 - 0.199 * 0.199));
            bool hasD = false, hasX = false;
            foreach (var c in s.SnapCandidates)
            {
                if (c.Position == d && c.Kind == SnapKind.CircleDivision) hasD = true;
                if (c.Kind == SnapKind.Intersection && Vec2.Distance(c.Position, xExpected) < 1e-9) { hasX = true; x = c.Position; }
            }
            Check.That(hasD && hasX, "precondition: the division and the crossing are candidates");
            double gap = Vec2.Distance(d, x);
            Vec2 dir = (x - d) * (1 / gap);
            // Pointer on D-X, 3 px nearer D: a priority/tie rule would pick X; pure distance picks D.
            double a = (gap - 3.0 / 400) / 2;
            Vec2 p = d + dir * a;
            ExpectDownSnaps(s, p.X, p.Y, d, "division 3 px nearer than an intersection: division (distance only)");
            Vec2 q = d + dir * (gap - a);
            ExpectDownSnaps(s, q.X, q.Y, x, "intersection 3 px nearer: intersection");
        }

        // ================= sticky release =================

        public static void V_StickyHoldsWithinOnePointFiveTol()
        {
            var s = NewSession(new AffineMetric(), 1);
            var c = V(0.5, 0.5);
            Down(s, 0.2, 0.5);
            MoveTo(s, 0.35, 0.5);
            MoveTo(s, 0.505, 0.5);
            Check.That(s.Cursor == c, $"snapped at 2 px, got {P(s.Cursor)}");
            MoveTo(s, 0.5 + 0.09, 0.5);              // 36 px: outside the capture radius, inside 48
            Check.That(s.Cursor == c, $"36 px: sticky keeps C, got {P(s.Cursor)}");
            MoveTo(s, 0.5 + 0.115, 0.5);             // 46 px
            Check.That(s.Cursor == c, $"46 px: sticky keeps C, got {P(s.Cursor)}");
            Up(s, 0.5 + 0.115, 0.5);
            Check.That(LastLine(s).B == c, $"Up commits the sticky candidate exactly, got {P(LastLine(s).B)}");
        }

        public static void V_StickyReleasesBeyondOnePointFiveTol()
        {
            var s = NewSession(new AffineMetric(), 1);
            var c = V(0.5, 0.5);
            Down(s, 0.2, 0.5);
            MoveTo(s, 0.505, 0.5);
            Check.That(s.Cursor == c, "precondition: snapped");
            MoveTo(s, 0.5 + 0.125, 0.5);             // 50 px
            Check.That(s.Cursor == V(0.625, 0.5), $"50 px: released to raw, got {P(s.Cursor)}");
            MoveTo(s, 0.5 + 0.115, 0.5);             // back to 46 px: no longer snapped, so plain capture (32 px) applies
            Check.That(s.Cursor == V(0.615, 0.5), $"back at 46 px after release: raw (no re-stick), got {P(s.Cursor)}");
            Up(s, 0.615, 0.5);
            Check.That(LastLine(s).B == V(0.615, 0.5), "raw end committed");
        }

        public static void V_StickyPixelRadiusBindsWhenMagnified()
        {
            // With an isotropic metric BoardCap (1.5 tol / sigma) equals the 48 px reach exactly, so the pixel radius only
            // binds on its own when the cap is raised to its 0.03 floor: 2000 px/unit -> 48 px = 0.024 < 0.03.
            var m = new AffineMetric { Kx = 2000, Ky = 2000 };
            var s = NewSession(m, 1);
            var c = V(0.5, 0.5);
            Check.That(BoardCap(s, c) == Floor, "precondition: cap at the floor");
            Down(s, 0.3, 0.5);
            MoveTo(s, 0.45, 0.5);
            MoveTo(s, 0.5005, 0.5);
            Check.That(s.Cursor == c, "precondition: snapped");
            MoveTo(s, 0.5 + 46.0 / 2000, 0.5);
            Check.That(s.Cursor == c, $"46 px: sticky, got {P(s.Cursor)}");
            MoveTo(s, 0.5 + 50.0 / 2000, 0.5);       // 0.025 board: inside the cap, outside 48 px
            Check.That(s.Cursor == V(0.525, 0.5), $"50 px: released by the 1.5 tol pixel radius, got {P(s.Cursor)}");
            Cancel(s);
        }

        public static void V_StickyIsLimitedByBoardCap()
        {
            // Grazing far side: 1.5 tol in pixels covers a lot of board depth; the sticky radius is also capped on the board.
            var m = new GrazingMetric();
            var s = NewSession(m, 1);
            var c = V(0.5, 1.0);
            Down(s, 0.5, 0.6);
            MoveTo(s, 0.5, 0.95);
            MoveTo(s, 0.5, 0.995);
            Check.That(s.Cursor == c, $"precondition: snapped to the far edge midpoint, got {P(s.Cursor)}");
            // Walk back toward the viewer until the board distance exceeds the spec cap while still within 48 px.
            double y = 0.995;
            Vec2 p = V(0.5, y);
            for (; y > 0.5; y -= 0.005)
            {
                p = V(0.5, y);
                if (Vec2.Distance(p, c) > SpecCap(m, s.Board, p) * 1.05) break;
            }
            Check.That(Px(m, p, c) < StickyFactor * Tol, $"precondition: {Px(m, p, c):F1} px, inside 48 px");
            MoveTo(s, p.X, p.Y);
            Check.That(s.Cursor == p, $"beyond BoardCap: sticky released, got {P(s.Cursor)}");
            Cancel(s);
        }

        public static void V_StickySwitchesOnlyToAClearlyNearerCandidate()
        {
            var s = NewSession(new AffineMetric(), 1);
            var u1 = V(0.4, 0.6);
            var u2 = V(0.48, 0.6);                   // 32 px apart
            AddUserPoint(s, u1.X, u1.Y);
            AddUserPoint(s, u2.X, u2.Y);
            Down(s, 0.4, 0.9);
            MoveTo(s, 0.4, 0.75);
            MoveTo(s, 0.402, 0.6);
            Check.That(s.Cursor == u1, $"snapped to U1, got {P(s.Cursor)}");
            MoveTo(s, 0.4425, 0.6);                  // d(U1) 17 px, d(U2) 15 px: only 2 px nearer
            Check.That(s.Cursor == u1, $"competitor 2 px nearer: stays on U1, got {P(s.Cursor)}");
            MoveTo(s, 0.4445, 0.6);                  // 17.8 vs 14.2: 3.6 px
            Check.That(s.Cursor == u1, $"competitor 3.6 px nearer: stays on U1, got {P(s.Cursor)}");
            MoveTo(s, 0.45, 0.6);                    // 20 vs 12: 8 px nearer
            Check.That(s.Cursor == u2, $"competitor 8 px nearer: switches to U2, got {P(s.Cursor)}");
            MoveTo(s, 0.4425, 0.6);                  // now sticky on U2: U1 only 2 px nearer from here? (15 vs 17: U2 nearer anyway)
            Check.That(s.Cursor == u2, $"stays on U2, got {P(s.Cursor)}");
            MoveTo(s, 0.43, 0.6);                    // d(U1) 12, d(U2) 20: U1 8 px nearer
            Check.That(s.Cursor == u1, $"back to U1 when it is 8 px nearer, got {P(s.Cursor)}");
            Up(s, 0.43, 0.6);
            Check.That(LastLine(s).B == u1, "commit on U1");
        }

        public static void V_StickyResetsPerGesture()
        {
            var s = NewSession(new AffineMetric(), 1);
            var c = V(0.5, 0.5);
            Down(s, 0.2, 0.5);
            MoveTo(s, 0.505, 0.5);
            Up(s, 0.505, 0.5);
            Check.That(LastLine(s).B == c, "gesture 1 ends on C");
            // Gesture 2 never comes within 32 px of C; 44 px only matters if a stale sticky state survived.
            Down(s, 0.61, 0.9);
            MoveTo(s, 0.61, 0.7);
            MoveTo(s, 0.61, 0.5);                    // 44 px from C
            Check.That(s.Cursor == V(0.61, 0.5), $"no sticky carried over, got {P(s.Cursor)}");
            Cancel(s);
            // Cancel also resets: snap, cancel, then a fresh gesture at 44 px.
            Down(s, 0.2, 0.5);
            MoveTo(s, 0.505, 0.5);
            Cancel(s);
            Down(s, 0.61, 0.9);
            MoveTo(s, 0.61, 0.5);
            Check.That(s.Cursor == V(0.61, 0.5), $"no sticky after Cancel, got {P(s.Cursor)}");
            Cancel(s);
        }

        public static void V_StickyWithoutMetricUsesBoardUnits()
        {
            // Fallback: 1.5 * 0.02 = 0.03 board units (the plan moves DragEndOutsideToleranceCommitsRaw to 0.035).
            var s = new EditorSession();
            Down(s, 0.1, 0.12);
            MoveTo(s, 0.3, 0.3);
            MoveTo(s, 0.505, 0.5);
            Check.That(s.Cursor == V(0.5, 0.5), "precondition: snapped (fallback)");
            MoveTo(s, 0.528, 0.5);
            Check.That(s.Cursor == V(0.5, 0.5), $"0.028 < 0.03: sticky, got {P(s.Cursor)}");
            MoveTo(s, 0.535, 0.5);
            Check.That(s.Cursor == V(0.535, 0.5), $"0.035 > 0.03: raw, got {P(s.Cursor)}");
            Up(s, 0.535, 0.5);
        }

        public static void V_StickyDoesNotApplyToTheOrthoPath()
        {
            // H/V path unchanged in v1: once the along-axis offer is out of the 32 px tolerance, the end is raw.
            var s = NewSession(new AffineMetric(), 1);
            AddUserPoint(s, 0.6, 0.7);
            s.SetOrthoLock(true);
            Down(s, 0.3, 0.7);
            MoveTo(s, 0.45, 0.7);
            MoveTo(s, 0.605, 0.702);
            Check.That(s.Cursor == V(0.6, 0.7), $"precondition: aligned to x = 0.6, got {P(s.Cursor)}");
            MoveTo(s, 0.695, 0.702);                 // 38 px along the axis: outside 32
            Check.That(s.Cursor == V(0.695, 0.7), $"H/V: no sticky, got {P(s.Cursor)}");
            Cancel(s);
        }

        public static void V_SnapOffMidDragReleasesSticky()
        {
            // "Snap off still draws raw" (plan 6, P3): the sticky state must not outlive the toggle.
            var s = NewSession(new AffineMetric(), 1);
            var c = V(0.5, 0.5);
            Down(s, 0.2, 0.5);
            MoveTo(s, 0.505, 0.5);
            Check.That(s.Cursor == c, "precondition: snapped");
            s.SetSnapEnabled(false);
            MoveTo(s, 0.51, 0.5);                     // 4 px: inside both radii
            Check.That(s.Cursor == V(0.51, 0.5), $"snapping off: raw, got {P(s.Cursor)}");
            s.SetSnapEnabled(true);
            MoveTo(s, 0.5 + 0.1, 0.5);                // 40 px: only a stale sticky could pull it back to C
            Check.That(s.Cursor == V(0.6, 0.5), $"snapping back on: no stale sticky, got {P(s.Cursor)}");
            Up(s, 0.6, 0.5);
            Check.That(LastLine(s).B == V(0.6, 0.5), "raw end");
        }

        public static void V_StickyCandidateThatDisappearsIsDropped()
        {
            // Not spelled out in the plan: a sticky candidate that stops being a candidate mid-drag (its layer hidden) must
            // not be committed. Engine-only path (on device a layer tap needs a second finger, which cancels first).
            var b = GoldenBoards.Empty();
            b.GridN = 1;
            var guides = new Layer(b.NewId(), "guides");
            b.Layers.Insert(0, guides);
            guides.Shapes.Add(new LineShape(b.NewId(), V(0.1, 0.62), V(0.4, 0.62), StrokeStyle.Default));
            var s = new EditorSession(b) { SnapMetric = new AffineMetric() };
            var e = V(0.4, 0.62);
            Down(s, 0.75, 0.85);
            MoveTo(s, 0.6, 0.7);
            MoveTo(s, 0.402, 0.62);
            Check.That(s.Cursor == e, $"precondition: snapped to the guide's end, got {P(s.Cursor)}");
            s.SetLayerVisible(guides.Id, false);
            MoveTo(s, 0.44, 0.62);                    // 16 px: plain capture would also keep it if it were a candidate
            Check.That(s.Cursor == V(0.44, 0.62), $"hidden source: no longer snapped, got {P(s.Cursor)}");
            Up(s, 0.44, 0.62);
            Check.That(LastLine(s).B == V(0.44, 0.62), $"commits raw, not the hidden endpoint, got {P(LastLine(s).B)}");
        }

        public static void V_ReturningToTheStartDotDropsTheOldSticky()
        {
            // The start dot A is never sticky (builder rule, so short lines can be drawn). But after C -> A the sticky C must
            // not revive: from A, a point 36 px from A and 44 px from C (outside both capture radii) is raw. "After
            // snapping to C" no longer holds once the end snapped to A.
            var s = NewSession(new AffineMetric(), 1);
            var a = V(0.3, 0.3);
            var c = V(0.4, 0.3);
            AddUserPoint(s, a.X, a.Y);
            AddUserPoint(s, c.X, c.Y);
            Down(s, a.X, a.Y);
            MoveTo(s, 0.35, 0.3);
            MoveTo(s, 0.399, 0.3);
            Check.That(s.Cursor == c, $"precondition: on C, got {P(s.Cursor)}");
            MoveTo(s, 0.301, 0.3);
            Check.That(s.Cursor == a, $"precondition: back on the start A, got {P(s.Cursor)}");
            var p = V(0.3 + 12.0 / 400, 0.3 - 34.0 / 400);
            MoveTo(s, p.X, p.Y);
            Check.That(s.Cursor == p, $"36 px from A, 44 px from C after leaving C for A: raw, got {P(s.Cursor)}");
            Up(s, p.X, p.Y);
            Check.That(LastLine(s).B == p, $"commits raw, got {P(LastLine(s).B)}");
        }

        public static void V_HiddenMidDragThenLiftInPlaceCommitsRaw()
        {
            var b = GoldenBoards.Empty();
            b.GridN = 1;
            var guides = new Layer(b.NewId(), "guides");
            b.Layers.Insert(0, guides);
            guides.Shapes.Add(new LineShape(b.NewId(), V(0.1, 0.62), V(0.4, 0.62), StrokeStyle.Default));
            var s = new EditorSession(b) { SnapMetric = new AffineMetric() };
            Down(s, 0.75, 0.85);
            MoveTo(s, 0.6, 0.7);
            MoveTo(s, 0.41, 0.62);                    // 4 px from the guide's end E
            Check.That(s.Cursor == V(0.4, 0.62), "precondition: snapped to E");
            s.SetLayerVisible(guides.Id, false);
            Up(s, 0.41, 0.62);                        // lift at the very same position
            Check.That(LastLine(s).B == V(0.41, 0.62), $"E's layer hidden: the lift commits raw, got {P(LastLine(s).B)}");
        }

        public static void V_OrthoToggledMidDragDoesNotReviveSticky()
        {
            var s = NewSession(new AffineMetric(), 1);
            var c = V(0.5, 0.5);
            Down(s, 0.2, 0.43);
            MoveTo(s, 0.4, 0.47);
            MoveTo(s, 0.505, 0.5);
            Check.That(s.Cursor == c, "precondition: snapped (plain)");
            MoveTo(s, 0.6, 0.5);                      // 40 px: held by the sticky rule
            Check.That(s.Cursor == c, "precondition: sticky at 40 px");
            s.SetOrthoLock(true);                     // H/V path: no sticky (re-tracks at once)
            s.SetOrthoLock(false);                    // re-tracks at the same 40 px point with the plain rule
            Check.That(s.Cursor == V(0.6, 0.5), $"H/V on then off at 40 px: the sticky was dropped, got {P(s.Cursor)}");
            Cancel(s);
        }

        public static void V_SnapAcquiredDuringMove()
        {
            // Move drags the selection's reference point onto candidates: each new one is acquired once.
            var s = NewSession(new AffineMetric(), 1);
            var line = AddLine(s, V(0.2, 0.2), V(0.3, 0.2));
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { line.Id });
            s.SetTool(ToolId.Move);
            Check.That(s.ActiveTool == ToolId.Move, "precondition: Move");
            s_acquired = 0;
            Down(s, 0.3, 0.2);                        // grab the end B (0.3, 0.2)
            MoveTo(s, 0.4, 0.3);
            MoveTo(s, 0.48, 0.48);
            MoveTo(s, 0.499, 0.499);                  // B onto the tile centre (0.5, 0.5)
            MoveTo(s, 0.501, 0.5);
            int n = s_acquired;
            Check.That(n == 1 && s_lastAcquired.Position == V(0.5, 0.5), $"one acquisition on the centre, got {n}");
            Up(s, 0.501, 0.5);
            Check.That(s_acquired == 1, "Up adds none");
        }

        // ================= tap slop =================

        public static void V_TapSlopIsTenPixelsAtThirtyTwo()
        {
            var s = NewSession(new AffineMetric(), 1);
            var u = V(0.5, 0.5);
            Down(s, 0.5, 0.5);
            MoveTo(s, 0.5 + 9.0 / 400, 0.5);         // 9 px
            Up(s, 0.5 + 9.0 / 400, 0.5);
            Check.That(s.PendingConnectStart == u, $"9 px: still a tap (pending start set), got {P(s.PendingConnectStart)}");
            Down(s, 0.5, 0.5);                       // same candidate again: toggles pending off
            Up(s, 0.5, 0.5);
            Check.That(!s.PendingConnectStart.HasValue, "precondition: pending cleared");
            Down(s, 0.5, 0.5);
            MoveTo(s, 0.5 + 11.0 / 400, 0.5);        // 11 px: a drag
            Up(s, 0.5 + 11.0 / 400, 0.5);
            Check.That(!s.PendingConnectStart.HasValue, "11 px: a drag, no pending start");
            Check.That(s.ActiveLayer.Shapes.Count == 0, "the drag end (11 px, inside 32) snapped back onto the start: nothing committed");
        }

        // ================= SnapAcquired =================

        public static void V_SnapAcquiredOncePerNewAcquisition()
        {
            var s = NewSession(new AffineMetric(), 1);
            var u1 = V(0.4, 0.6);
            var u2 = V(0.48, 0.6);
            AddUserPoint(s, u1.X, u1.Y);
            AddUserPoint(s, u2.X, u2.Y);
            s_acquired = 0;
            Down(s, 0.401, 0.6);
            Check.That(s_acquired == 1 && s_lastAcquired.Position == u1, $"Down that snaps: 1 event on U1, got {s_acquired}");
            MoveTo(s, 0.403, 0.6);
            MoveTo(s, 0.404, 0.601);
            MoveTo(s, 0.41, 0.6);
            Check.That(s_acquired == 1, $"moves that stay on U1: no event, got {s_acquired}");
            MoveTo(s, 0.47, 0.6);
            Check.That(s_acquired == 2 && s_lastAcquired.Position == u2, $"switch to U2: 1 more, got {s_acquired}");
            MoveTo(s, 0.475, 0.6);
            Check.That(s_acquired == 2, "still U2: none");
            Up(s, 0.475, 0.6);
            Check.That(s_acquired == 2, $"Up: no event, got {s_acquired}");
        }

        public static void V_SnapAcquiredNotRaisedForRawOrSelectOrSnapOff()
        {
            var s = NewSession(new AffineMetric(), 1);
            AddUserPoint(s, 0.4, 0.6);
            s_acquired = 0;
            Down(s, 0.2, 0.2);
            MoveTo(s, 0.25, 0.25);
            Up(s, 0.3, 0.3);
            Check.That(s_acquired == 0, $"raw drag: no event, got {s_acquired}");
            s.SetSnapEnabled(false);
            Down(s, 0.401, 0.6);
            Up(s, 0.401, 0.6);
            Check.That(s_acquired == 0, $"snapping off: no event, got {s_acquired}");
            s.SetSnapEnabled(true);
            s.SetTool(ToolId.SelectMove);
            Down(s, 0.401, 0.6);
            MoveTo(s, 0.5, 0.7);
            Up(s, 0.5, 0.7);
            Check.That(s_acquired == 0, $"Select never snaps: no event, got {s_acquired}");
        }

        public static void V_SnapAcquiredForCircleTool()
        {
            var s = NewSession(new AffineMetric(), 1);
            s.SetTool(ToolId.Circle);
            s_acquired = 0;
            Down(s, 0.502, 0.5);
            Check.That(s_acquired == 1, $"circle centre snap: 1 event, got {s_acquired}");
            Cancel(s);
        }

        // ================= exactness and cost =================

        public static void V_CommittedEndsAreCandidatesExactlyUnderPerspective()
        {
            // Star board, perspective metric: every committed endpoint that snapped is bit-identical to a candidate.
            var s = new EditorSession(GoldenBoards.StarOnFullBoard()) { SnapMetric = new GrazingMetric() };
            var rnd = new Random(4111);
            int snapped = 0;
            for (int i = 0; i < 60; i++)
            {
                var before = new HashSet<Vec2>();   // the candidates while the gesture runs (the commit adds its own ends)
                foreach (var c in s.SnapCandidates) before.Add(c.Position);
                var list = new List<SnapCandidate>(s.SnapCandidates);
                var target = list[rnd.Next(list.Count)].Position;
                double ox = (rnd.NextDouble() - 0.5) * 0.01, oy = (rnd.NextDouble() - 0.5) * 0.01;
                var end = V(target.X + ox, target.Y + oy);
                int count = s.ActiveLayer.Shapes.Count;
                Down(s, 0.05 + 0.9 * rnd.NextDouble(), 0.05 + 0.9 * rnd.NextDouble());
                MoveTo(s, target.X + ox * 3, target.Y + oy * 3);
                MoveTo(s, end.X, end.Y);
                Up(s, end.X, end.Y);
                var sh = s.ActiveLayer.Shapes;
                if (sh.Count != count + 1) continue;   // too short: nothing committed
                var l = (LineShape)sh[sh.Count - 1];
                if (l.B == end) continue;               // raw
                snapped++;
                Check.That(before.Contains(l.B), $"snapped end {P(l.B)} is a candidate exactly (==)");
            }
            Check.That(snapped > 20, $"precondition: most ends snapped ({snapped})");
        }

        public static void V_MovePathAllocatesNothing()
        {
            foreach (var metric in new ISnapMetric[] { null, new AffineMetric(), new GrazingMetric() })
            {
                var s = new EditorSession(GoldenBoards.StarOnFullBoard()) { SnapMetric = metric };
                s.SnapAcquired += OnAcquired;   // a static method group: invoking it allocates nothing
                foreach (var tool in new[] { ToolId.Line, ToolId.Circle })
                {
                    s.SetTool(tool);
                    s.SetOrthoLock(false);
                    Down(s, 0.2, 0.3); MoveTo(s, 0.5, 0.5); Up(s, 0.7, 0.6);   // warm-up: candidate rebuild
                    var moves = new PointerEvent[200];
                    for (int i = 0; i < moves.Length; i++)
                    {
                        double t = i / 199.0;   // sweeps across the star: acquire, stick, release, switch
                        moves[i] = new PointerEvent(PointerPhase.Move, V(0.1 + 0.8 * t, 0.5 + 0.35 * Math.Sin(7 * t)), true, i);
                    }
                    Down(s, 0.15, 0.2);
                    s.Pointer(moves[0]);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < moves.Length; i++) s.Pointer(moves[i]);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    Cancel(s);
                    Check.That(bytes == 0, $"{tool}, metric {metric?.GetType().Name ?? "none"}: 200 Moves allocated {bytes} bytes");
                }
            }
        }

        public static void V_ProjectionCostIsBounded()
        {
            // BoardCap adds 2 projections per query (plan 4.2). Per Move: pointer + 2 Jacobian probes + candidates in the
            // cap. A guard against projecting the whole candidate list several times per Move.
            var m = new AffineMetric();
            var s = new EditorSession(GoldenBoards.StarOnFullBoard()) { SnapMetric = m };
            int n = s.SnapCandidates.Count;
            Down(s, 0.2, 0.2);
            MoveTo(s, 0.25, 0.25);
            m.Calls = 0;
            MoveTo(s, 0.3, 0.31);
            Check.That(m.Calls <= n + 8, $"one Move projected {m.Calls} times for {n} candidates");
            Cancel(s);
        }
    }
}
