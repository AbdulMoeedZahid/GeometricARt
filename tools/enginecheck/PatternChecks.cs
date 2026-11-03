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

            // Lines == the golden fixture's lines, order-independent, either orientation, compared with ==.
            //
            // Fixture caveat: GoldenBoards.StarPoint evaluates each star separately, and 0.25 * sin(pi) is not 0, so two
            // neighbouring stars put their shared tip 1 ulp apart: (0.5, 0.25) vs (0.5, 0.25000000000000006) and
            // (0.25, 0.5) vs (0.24999999999999994, 0.5). A snapped seam is ONE point (stronger than the fixture), so at
            // those fixture "seam groups" (distinct fixture endpoints within 1e-12 of each other) the drawn board must use
            // one single bit-identical value everywhere, and that value must be one of the fixture's variants. Every other
            // endpoint must equal the fixture exactly.
            var golden = new List<LineShape>();
            foreach (var l in GoldenBoards.Tiled2x2().Layers)
                foreach (var sh in l.Shapes)
                    if (sh is LineShape gl) golden.Add(gl);
            Check.That(golden.Count == 32, "fixture has 32 lines");

            var fixturePts = new List<Vec2>();
            foreach (var gl in golden) { AddDistinct(fixturePts, gl.A); AddDistinct(fixturePts, gl.B); }
            var drawnPts = new List<Vec2>();
            foreach (var ln in lines) { AddDistinct(drawnPts, ln.A); AddDistinct(drawnPts, ln.B); }

            // Seam groups: the drawn value there must be unique and a member of the group.
            int seamGroups = 0;
            for (int i = 0; i < fixturePts.Count; i++)
            {
                var group = Near(fixturePts, fixturePts[i]);
                if (group.Count < 2 || !(group[0] == fixturePts[i])) continue;   // singletons, and count each group once
                seamGroups++;
                var drawn = Near(drawnPts, fixturePts[i]);
                Check.That(drawn.Count == 1, $"seam at {fixturePts[i]} is one bit-identical point, got {drawn.Count} values");
                Check.That(group.Contains(drawn[0]), $"seam value ({drawn[0].X:R},{drawn[0].Y:R}) is one of the fixture's variants");
            }
            Check.That(seamGroups == 2, $"expected the fixture's 2 known 1-ulp seam groups, found {seamGroups}");
            Check.That(drawnPts.Count == fixturePts.Count - seamGroups, $"drawn board has {drawnPts.Count} distinct endpoints");

            var used = new bool[golden.Count];
            foreach (var ln in lines)
            {
                int match = -1;
                for (int g = 0; g < golden.Count && match < 0; g++)
                {
                    if (used[g]) continue;
                    var gl = golden[g];
                    if ((Same(ln.A, gl.A, fixturePts) && Same(ln.B, gl.B, fixturePts))
                        || (Same(ln.A, gl.B, fixturePts) && Same(ln.B, gl.A, fixturePts))) match = g;
                }
                Check.That(match >= 0, $"line ({ln.A.X:R},{ln.A.Y:R})-({ln.B.X:R},{ln.B.Y:R}) is in Tiled2x2 exactly");
                used[match] = true;
            }
        }

        const double SeamEps = 1e-12;

        static void AddDistinct(List<Vec2> pts, Vec2 p)
        {
            if (!pts.Contains(p)) pts.Add(p);
        }

        static List<Vec2> Near(List<Vec2> pts, Vec2 p)
        {
            var r = new List<Vec2>();
            foreach (var q in pts) if (Vec2.Distance(p, q) <= SeamEps) r.Add(q);
            return r;
        }

        /// <summary>
        /// Drawn d matches fixture f: d == f, or f is in a fixture seam group (another fixture endpoint within 1e-12)
        /// and d is one of that group's exact values.
        /// </summary>
        static bool Same(Vec2 d, Vec2 f, List<Vec2> fixturePts)
        {
            if (d == f) return true;
            var group = Near(fixturePts, f);
            return group.Count >= 2 && group.Contains(d);
        }
    }
}
