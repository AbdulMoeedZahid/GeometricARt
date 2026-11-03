using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>E2 golden: the Tiled2x2 pattern built with pointer events only, through snapping and tap-to-connect.</summary>
    public static class PatternChecks
    {
        static double s_T;
        static PointerEvent Ev(PointerPhase p, Vec2 v) => new PointerEvent(p, v, true, s_T += 0.016);

        static void Tap(EditorSession s, Vec2 raw)
        {
            s.Pointer(Ev(PointerPhase.Down, raw));
            s.Pointer(Ev(PointerPhase.Up, raw));
        }

        /// <summary>A raw position a few thousandths off <paramref name="p"/>, nudged toward <paramref name="inward"/>.</summary>
        static Vec2 Off(Vec2 p, Vec2 inward, int salt)
        {
            Vec2 d = inward - p;
            double len = d.Length;
            Vec2 u = len > 0 ? d * (1.0 / len) : new Vec2(1, 0);
            Vec2 perp = new Vec2(-u.Y, u.X);
            double side = (salt % 2 == 0) ? 1 : -1;
            return p + u * 0.003 + perp * (0.0015 * side);
        }

        public static void SnapBuildsTiled2x2()
        {
            var s = new EditorSession();   // empty board, square 2x2, no metric: board-unit tolerance
            Check.That(s.Board.GridN == 2 && s.Board.GridKind == GridKind.Square && s.SnapMetric == null, "2x2 square, fallback");
            const double r = GoldenBoards.TileStarRadius;
            var centers = new Vec2[4];
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                    centers[j * 2 + i] = new Vec2(0.25 + 0.5 * i, 0.25 + 0.5 * j);

            // 1. Four circles: centre snapped to the cell centre, radius snapped to the cell's right edge midpoint.
            s.SetTool(ToolId.Circle);
            for (int c = 0; c < 4; c++)
            {
                Vec2 cc = centers[c];
                Vec2 edgeMid = new Vec2(cc.X + r, cc.Y);
                Vec2 down = cc + new Vec2(0.003, -0.002);
                Vec2 up = edgeMid + new Vec2(-0.002, 0.003);
                s.Pointer(Ev(PointerPhase.Down, down));
                for (int k = 1; k <= 6; k++) s.Pointer(Ev(PointerPhase.Move, down + (up - down) * (k / 6.0)));
                s.Pointer(Ev(PointerPhase.Up, up));
            }

            // 2. Each star: two squares (division points 0,2,4,6 and 1,3,5,7) by tap-to-connect.
            s.SetTool(ToolId.Line);
            int salt = 0;
            for (int c = 0; c < 4; c++)
                for (int start = 0; start < 2; start++)
                    for (int i = 0; i < 4; i++)
                    {
                        int k0 = start + 2 * i, k1 = start + 2 * ((i + 1) % 4);
                        Tap(s, Off(GoldenBoards.StarPoint(centers[c], r, k0), centers[c], salt++));
                        Check.That(s.PendingConnectStart.HasValue, $"cell {c} k{k0}: first tap sets the pending start");
                        Tap(s, Off(GoldenBoards.StarPoint(centers[c], r, k1), centers[c], salt++));
                        Check.That(!s.PendingConnectStart.HasValue, $"cell {c} k{k0}-k{k1}: second tap connects");
                    }

            // Circles: exactly 4, centres and radii exact.
            var circles = new List<CircleShape>();
            var lines = new List<LineShape>();
            foreach (var l in s.Board.Layers)
                foreach (var sh in l.Shapes)
                {
                    if (sh is CircleShape ci) circles.Add(ci);
                    else if (sh is LineShape ln) lines.Add(ln);
                }
            Check.That(circles.Count == 4, $"4 circles, got {circles.Count}");
            for (int c = 0; c < 4; c++)
            {
                Check.That(circles[c].Center == centers[c], $"circle {c} centre exact, got {circles[c].Center.X:R},{circles[c].Center.Y:R}");
                Check.That(circles[c].Radius == r, $"circle {c} radius exactly 0.25, got {circles[c].Radius:R}");
            }
            Check.That(lines.Count == 32, $"32 lines, got {lines.Count}");

            // Lines == the golden fixture's lines, order-independent, either orientation, compared with == everywhere.
            // StarPoint and the snap candidates share the exact CircleDivisions table, so seams between neighbouring
            // stars are one bit-identical point in both the fixture and the drawing.
            var golden = new List<LineShape>();
            foreach (var l in GoldenBoards.Tiled2x2().Layers)
                foreach (var sh in l.Shapes)
                    if (sh is LineShape gl) golden.Add(gl);
            Check.That(golden.Count == 32, "fixture has 32 lines");

            var fixturePts = new List<Vec2>();
            foreach (var gl in golden) { AddDistinct(fixturePts, gl.A); AddDistinct(fixturePts, gl.B); }
            for (int i = 0; i < fixturePts.Count; i++)
                for (int j = i + 1; j < fixturePts.Count; j++)
                    Check.That(Vec2.Distance(fixturePts[i], fixturePts[j]) > 1e-9,
                        $"fixture seam is one point: ({fixturePts[i].X:R},{fixturePts[i].Y:R}) vs ({fixturePts[j].X:R},{fixturePts[j].Y:R})");
            var drawnPts = new List<Vec2>();
            foreach (var ln in lines) { AddDistinct(drawnPts, ln.A); AddDistinct(drawnPts, ln.B); }
            Check.That(drawnPts.Count == fixturePts.Count,
                $"drawn board has {drawnPts.Count} distinct endpoints, fixture {fixturePts.Count}");

            var used = new bool[golden.Count];
            foreach (var ln in lines)
            {
                int match = -1;
                for (int g = 0; g < golden.Count && match < 0; g++)
                {
                    if (used[g]) continue;
                    var gl = golden[g];
                    if ((ln.A == gl.A && ln.B == gl.B) || (ln.A == gl.B && ln.B == gl.A)) match = g;
                }
                Check.That(match >= 0, $"line ({ln.A.X:R},{ln.A.Y:R})-({ln.B.X:R},{ln.B.Y:R}) is in Tiled2x2 exactly");
                used[match] = true;
            }
        }

        /// <summary>The shared exact division table: exact on the axes, one diagonal constant, close to cos/sin.</summary>
        public static void CircleDivisionTableIsExact()
        {
            double d = CircleDivisions.Diagonal;
            Check.That(d == System.Math.Sqrt(2) / 2, "Diagonal is sqrt(2)/2 as a double");
            for (int k = 0; k < CircleDivisions.Count; k++)
            {
                double a = k * System.Math.PI / 4;
                Check.Near(CircleDivisions.Cos(k), System.Math.Cos(a), $"cos {k}", 1e-15);
                Check.Near(CircleDivisions.Sin(k), System.Math.Sin(a), $"sin {k}", 1e-15);
                double c = System.Math.Abs(CircleDivisions.Cos(k)), s = System.Math.Abs(CircleDivisions.Sin(k));
                Check.That(c == 0 || c == 1 || c == d, $"cos {k} uses only 0, 1, Diagonal");
                Check.That(s == 0 || s == 1 || s == d, $"sin {k} uses only 0, 1, Diagonal");
                Check.That(CircleDivisions.Point(new Vec2(0.3, 0.4), 0.2, k) == GoldenBoards.StarPoint(new Vec2(0.3, 0.4), 0.2, k),
                    $"StarPoint {k} uses the table");
            }
            Check.That(CircleDivisions.Cos(-1) == CircleDivisions.Cos(7) && CircleDivisions.Sin(9) == CircleDivisions.Sin(1), "k wraps mod 8");
            // Touching circles share their tangent point bit-for-bit (the property pattern seams rely on).
            var left = CircleDivisions.Point(new Vec2(0.25, 0.25), 0.25, 0);
            var right = CircleDivisions.Point(new Vec2(0.75, 0.25), 0.25, 4);
            Check.That(left == right && left == new Vec2(0.5, 0.25), "k0 of the left circle == k4 of the right circle == (0.5, 0.25)");
            var below = CircleDivisions.Point(new Vec2(0.25, 0.25), 0.25, 2);
            var above = CircleDivisions.Point(new Vec2(0.25, 0.75), 0.25, 6);
            Check.That(below == above && below == new Vec2(0.25, 0.5), "k2 below == k6 above == (0.25, 0.5)");
        }

        static void AddDistinct(List<Vec2> pts, Vec2 p)
        {
            if (!pts.Contains(p)) pts.Add(p);
        }
    }
}
