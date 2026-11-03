using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>E1: Intersect (pure geometry) and SnapIndex (candidate sources, priority, dedup, lazy rebuild).</summary>
    public static class SnapChecks
    {
        static readonly StrokeStyle Style = StrokeStyle.Default;

        // ---------- Intersect ----------

        public static void SegmentSegmentPerpendicularCrossing()
        {
            int n = Intersect.SegmentSegment(new Vec2(0, 0.5), new Vec2(1, 0.5), new Vec2(0.25, 0), new Vec2(0.25, 1), out var p);
            Check.That(n == 1, $"one crossing, got {n}");
            Check.That(p == new Vec2(0.25, 0.5), $"exact (0.25, 0.5), got {p.X:R},{p.Y:R}");

            n = Intersect.SegmentSegment(new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1), new Vec2(1, 0), out p);
            Check.That(n == 1, "diagonals cross");
            Check.Near(p.X, 0.5, "diagonal x", 1e-15);
            Check.Near(p.Y, 0.5, "diagonal y", 1e-15);

            n = Intersect.SegmentSegment(new Vec2(0, 0), new Vec2(0.4, 0.4), new Vec2(0, 1), new Vec2(1, 0), out _);
            Check.That(n == 0, "lines cross at (0.5,0.5) but segment a stops at 0.4: none");
        }

        public static void SegmentSegmentEndpointTouches()
        {
            // T-junction: b starts on the middle of a; the touch point is b's start, bit for bit.
            var b0 = new Vec2(0.5, 0.5);
            int n = Intersect.SegmentSegment(new Vec2(0.2, 0.5), new Vec2(0.8, 0.5), b0, new Vec2(0.5, 0.9), out var p);
            Check.That(n == 1 && p == b0, $"T-junction touches at b0, got {n} {p}");
            // Same on a diagonal, where a0 + t * (a1 - a0) need not round to b0.
            for (int i = 1; i < 10; i++)
            {
                var t0 = new Vec2(0.1 + 0.06 * i, 0.3 + 0.06 * i);
                n = Intersect.SegmentSegment(new Vec2(0.1, 0.3), new Vec2(0.7, 0.9), t0, new Vec2(0.95, 0.05), out p);
                Check.That(n == 1 && p == t0, $"diagonal T-junction {i}: touch point is b0 bit for bit, got {n} ({p.X:R}, {p.Y:R})");
            }

            // L-corner: two segments sharing an end.
            n = Intersect.SegmentSegment(new Vec2(0, 0), new Vec2(0.5, 0), new Vec2(0.5, 0), new Vec2(0.5, 0.5), out p);
            Check.That(n == 1 && p == new Vec2(0.5, 0), $"shared end, got {n} {p}");

            // Stopping short of a by 0.01 is a miss.
            n = Intersect.SegmentSegment(new Vec2(0.2, 0.5), new Vec2(0.8, 0.5), new Vec2(0.5, 0.51), new Vec2(0.5, 0.9), out _);
            Check.That(n == 0, "gap of 0.01 is not a touch");
        }

        public static void ParallelAndCollinearGiveNone()
        {
            Check.That(Intersect.SegmentSegment(new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 0.1), new Vec2(1, 0.1), out _) == 0, "parallel");
            Check.That(Intersect.SegmentSegment(new Vec2(0, 0), new Vec2(0.6, 0), new Vec2(0.4, 0), new Vec2(1, 0), out _) == 0, "collinear overlap");
            Check.That(Intersect.SegmentSegment(new Vec2(0, 0), new Vec2(0.5, 0), new Vec2(0.5, 0), new Vec2(1, 0), out _) == 0, "collinear end to end");
            Check.That(Intersect.SegmentSegment(new Vec2(0.2, 0.2), new Vec2(0.2, 0.2), new Vec2(0, 0), new Vec2(1, 1), out _) == 0, "zero-length segment");
            Check.That(Intersect.LineSegment(new Vec2(0, 0.3), new Vec2(2, 0), new Vec2(0, 0.6), new Vec2(1, 0.6), out _) == 0, "line parallel to segment");
        }

        public static void SegmentThroughCircleGivesTwoExactPoints()
        {
            var c = new Vec2(0.5, 0.5);
            int n = Intersect.SegmentCircle(new Vec2(0, 0.5), new Vec2(1, 0.5), c, 0.25, out var p0, out var p1);
            Check.That(n == 2, $"secant gives 2, got {n}");
            Check.That(p0 == new Vec2(0.25, 0.5) && p1 == new Vec2(0.75, 0.5), $"exact horizontal crossings, got {p0} {p1}");

            n = Intersect.SegmentCircle(new Vec2(0.5, 1), new Vec2(0.5, 0), c, 0.25, out p0, out p1);
            Check.That(n == 2 && p0 == new Vec2(0.5, 0.75) && p1 == new Vec2(0.5, 0.25), $"vertical, p0 nearer a, got {p0} {p1}");

            n = Intersect.SegmentCircle(new Vec2(0, 0), new Vec2(1, 1), c, 0.25, out p0, out p1);
            double o = 0.25 / Math.Sqrt(2);
            Check.That(n == 2, "diagonal secant");
            Check.Near(p0.X, 0.5 - o, "diag p0.x", 1e-12); Check.Near(p0.Y, 0.5 - o, "diag p0.y", 1e-12);
            Check.Near(p1.X, 0.5 + o, "diag p1.x", 1e-12); Check.Near(p1.Y, 0.5 + o, "diag p1.y", 1e-12);

            // Segment starting at the centre crosses once.
            n = Intersect.SegmentCircle(c, new Vec2(1, 0.5), c, 0.25, out p0, out _);
            Check.That(n == 1 && p0 == new Vec2(0.75, 0.5), $"half-inside gives 1, got {n} {p0}");
        }

        public static void TangentSegmentGivesOne()
        {
            int n = Intersect.SegmentCircle(new Vec2(0, 0.75), new Vec2(1, 0.75), new Vec2(0.5, 0.5), 0.25, out var p0, out _);
            Check.That(n == 1 && p0 == new Vec2(0.5, 0.75), $"tangent at (0.5,0.75), got {n} {p0}");
            n = Intersect.SegmentCircle(new Vec2(0.6, 0.75), new Vec2(1, 0.75), new Vec2(0.5, 0.5), 0.25, out _, out _);
            Check.That(n == 0, "tangent point outside the segment: none");
        }

        public static void SegmentInsideCircleGivesZero()
        {
            Check.That(Intersect.SegmentCircle(new Vec2(0.45, 0.5), new Vec2(0.55, 0.52), new Vec2(0.5, 0.5), 0.25, out _, out _) == 0, "inside");
            Check.That(Intersect.SegmentCircle(new Vec2(0, 0.9), new Vec2(1, 0.9), new Vec2(0.5, 0.5), 0.25, out _, out _) == 0, "far away");
        }

        public static void CircleCircleCases()
        {
            int n = Intersect.CircleCircle(new Vec2(0.4, 0.5), 0.2, new Vec2(0.6, 0.5), 0.2, out var p0, out var p1);
            double h = Math.Sqrt(0.2 * 0.2 - 0.1 * 0.1);
            Check.That(n == 2, $"overlapping: 2, got {n}");
            Check.Near(p0.X, 0.5, "p0.x", 1e-12); Check.Near(p0.Y, 0.5 + h, "p0.y (left of c0->c1)", 1e-12);
            Check.Near(p1.X, 0.5, "p1.x", 1e-12); Check.Near(p1.Y, 0.5 - h, "p1.y", 1e-12);

            Check.That(Intersect.CircleCircle(new Vec2(0.2, 0.5), 0.1, new Vec2(0.8, 0.5), 0.1, out _, out _) == 0, "apart");
            Check.That(Intersect.CircleCircle(new Vec2(0.5, 0.5), 0.3, new Vec2(0.55, 0.5), 0.1, out _, out _) == 0, "nested");

            n = Intersect.CircleCircle(new Vec2(0.25, 0.5), 0.25, new Vec2(0.75, 0.5), 0.25, out p0, out _);
            Check.That(n == 1, $"outside tangent: 1, got {n}");
            Check.Near(p0.X, 0.5, "outside tangent x", 1e-12); Check.Near(p0.Y, 0.5, "outside tangent y", 1e-12);

            n = Intersect.CircleCircle(new Vec2(0.5, 0.5), 0.25, new Vec2(0.625, 0.5), 0.125, out p0, out _);
            Check.That(n == 1, $"inside tangent: 1, got {n}");
            Check.Near(p0.X, 0.75, "inside tangent x", 1e-12); Check.Near(p0.Y, 0.5, "inside tangent y", 1e-12);
            n = Intersect.CircleCircle(new Vec2(0.625, 0.5), 0.125, new Vec2(0.5, 0.5), 0.25, out p0, out _);
            Check.That(n == 1, "inside tangent, small circle first");
            Check.Near(p0.X, 0.75, "inside tangent (swapped) x", 1e-12); Check.Near(p0.Y, 0.5, "inside tangent (swapped) y", 1e-12);

            Check.That(Intersect.CircleCircle(new Vec2(0.5, 0.5), 0.2, new Vec2(0.5, 0.5), 0.3, out _, out _) == 0, "concentric");
            Check.That(Intersect.CircleCircle(new Vec2(0.5, 0.5), 0.2, new Vec2(0.5, 0.5), 0.2, out _, out _) == 0, "identical");
        }

        public static void InfiniteLineVariants()
        {
            // LineCircle: the line is not clipped, so an origin outside the circle on either side still gives 2.
            int n = Intersect.LineCircle(new Vec2(0.9, 0.5), new Vec2(3, 0), new Vec2(0.5, 0.5), 0.25, out var p0, out var p1);
            Check.That(n == 2, $"line through circle: 2, got {n}");
            Check.That(p0.Y == 0.5 && p1.Y == 0.5, "horizontal line keeps y exactly");
            Check.Near(p0.X, 0.25, "first along dir", 1e-12); Check.Near(p1.X, 0.75, "second along dir", 1e-12);
            n = Intersect.LineCircle(new Vec2(0, 0.75), new Vec2(1, 0), new Vec2(0.5, 0.5), 0.25, out p0, out _);
            Check.That(n == 1 && p0 == new Vec2(0.5, 0.75), $"tangent line: 1, got {n} {p0}");
            Check.That(Intersect.LineCircle(new Vec2(0, 0.8), new Vec2(1, 0), new Vec2(0.5, 0.5), 0.25, out _, out _) == 0, "line misses");
            Check.That(Intersect.LineCircle(new Vec2(0, 0.5), Vec2.Zero, new Vec2(0.5, 0.5), 0.25, out _, out _) == 0, "zero direction");

            // LineSegment: segment x = 0.7; the line passes it even though the segment is behind the origin.
            n = Intersect.LineSegment(new Vec2(0.9, 0.4), new Vec2(1, 0), new Vec2(0.7, 0), new Vec2(0.7, 1), out var p);
            Check.That(n == 1 && p.Y == 0.4, $"line hits segment behind origin, y exact, got {n} {p}");
            Check.Near(p.X, 0.7, "x on the segment", 1e-15);
            var end = new Vec2(0.7, 0.4);
            n = Intersect.LineSegment(new Vec2(0, 0.4), new Vec2(1, 0), end, new Vec2(0.9, 0.9), out p);
            Check.That(n == 1 && p == end, "touching the segment end returns the end exactly");
            Check.That(Intersect.LineSegment(new Vec2(0, 0.95), new Vec2(1, 0), new Vec2(0.7, 0), new Vec2(0.7, 0.9), out _) == 0, "line past segment end");
        }

        // ---------- SnapIndex: grid intersections ----------

        public static void EmptySquareBoardGridCorners()
        {
            var idx = new SnapIndex();
            var board = GoldenBoards.Empty();
            idx.Rebuild(board);
            var corners = OfKind(idx, SnapKind.CellCorner);
            Check.That(corners.Count == 9, $"2x2: 9 grid intersections, got {corners.Count}");
            for (int i = 0; i <= 2; i++)
                for (int j = 0; j <= 2; j++)
                    Check.That(corners.Contains(new Vec2(i / 2.0, j / 2.0)), $"corner ({i}/2, {j}/2) present exactly");
            Check.That(OfKind(idx, SnapKind.CellEdgeMid).Count == 12 && OfKind(idx, SnapKind.CellCenter).Count == 4,
                "2x2 also has 12 edge midpoints and 4 centres");
            Check.That(idx.Candidates.Count == 25, $"empty board: grid only (25), got {idx.Candidates.Count}");

            board.GridN = 3;
            Check.That(idx.Sync(board), "GridN change rebuilds");
            corners = OfKind(idx, SnapKind.CellCorner);
            Check.That(corners.Count == 16, $"3x3: 16, got {corners.Count}");
            for (int i = 0; i <= 3; i++)
                for (int j = 0; j <= 3; j++)
                    Check.That(corners.Exists(v => Math.Abs(v.X - i / 3.0) < 1e-12 && Math.Abs(v.Y - j / 3.0) < 1e-12), $"3x3 corner ({i},{j})");

            board.GridN = 1;
            idx.Sync(board);
            corners = OfKind(idx, SnapKind.CellCorner);
            Check.That(corners.Count == 4, $"1x1: 4 board corners, got {corners.Count}");
            foreach (var v in new[] { new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), new Vec2(0, 1) })
                Check.That(corners.Contains(v), $"board corner {v}");

            board.GridN = 4;
            idx.Sync(board);
            Check.That(OfKind(idx, SnapKind.CellCorner).Count == 25, "4x4: 25");
        }

        public static void HexCornersMatchTileGridExactly()
        {
            var tile = new List<SnapCandidate>();
            for (int n = Board.MinGridN; n <= Board.MaxGridN; n++)
            {
                var board = GoldenBoards.Empty();
                board.GridKind = GridKind.Hex;
                board.GridN = n;
                var idx = new SnapIndex();
                idx.Rebuild(board);
                TileGrid.SnapPoints(GridKind.Hex, n, tile);
                var expected = new List<Vec2>();
                foreach (var t in tile) if (t.Kind == SnapKind.CellCorner) expected.Add(t.Position);
                var got = OfKind(idx, SnapKind.CellCorner);
                Check.That(got.Count == expected.Count, $"hex {n}: {expected.Count} corners, got {got.Count}");
                for (int i = 0; i < expected.Count; i++)
                    Check.That(got[i] == expected[i], $"hex {n}: corner {i} bit-identical and in TileGrid order");
                // n hexagons, 6 vertices each, shared ones once: 1 hex has 6, 2x2 offset hexes share 5 edges -> 16.
                if (n == 1) Check.That(got.Count == 6, "one hexagon: 6 corners");
                if (n == 2) Check.That(got.Count == 16, $"2x2 hex: 16 corners, got {got.Count}");
            }
        }

        public static void SwitchingGridKindRebuildsCorners()
        {
            var board = GoldenBoards.Empty();
            var idx = new SnapIndex();
            Check.That(idx.Sync(board), "first sync builds");
            Check.That(OfKind(idx, SnapKind.CellCorner).Contains(new Vec2(0, 0)), "square has the (0,0) corner");
            board.GridKind = GridKind.Hex;
            Check.That(idx.Sync(board), "GridKind change rebuilds");
            var corners = OfKind(idx, SnapKind.CellCorner);
            Check.That(corners.Count == 16, $"hex 2x2: 16 corners, got {corners.Count}");
            Check.That(!corners.Contains(new Vec2(0, 0)) && !corners.Contains(new Vec2(0.5, 0.5)), "square corners gone");
            board.GridKind = GridKind.Square;
            Check.That(idx.Sync(board) && OfKind(idx, SnapKind.CellCorner).Count == 9, "back to square: 9");
        }

        // ---------- SnapIndex: shapes ----------

        public static void StarFixtureDivisionsAndInnerVertices()
        {
            var board = GoldenBoards.StarOnFullBoard();
            var idx = new SnapIndex();
            idx.Rebuild(board);
            var c = GoldenBoards.BoardCenter;
            const double r = GoldenBoards.StarRadius;

            // The circle layer stores the 8 divisions as user points, so they survive as UserPoint, exact.
            for (int k = 0; k < 8; k++)
            {
                var want = GoldenBoards.StarPoint(c, r, k);
                int i = IndexOfExact(idx, want);
                Check.That(i >= 0, $"division {k} present at StarPoint exactly");
                Check.That(idx.Candidates[i].Kind == SnapKind.UserPoint, $"division {k}: UserPoint wins, got {idx.Candidates[i].Kind}");
                Check.That(CountWithin(idx, want, SnapIndex.DedupEpsilon) == 1, $"division {k}: merged to one candidate");
            }

            // Inner star vertices: the two squares (apothem r/sqrt2) intersect in a regular octagon with vertices at
            // pi/8 + k*pi/4 and circumradius (r/sqrt2)/cos(pi/8).
            double rin = r / Math.Sqrt(2) / Math.Cos(Math.PI / 8);
            for (int k = 0; k < 8; k++)
            {
                double a = Math.PI / 8 + k * Math.PI / 4;
                var want = new Vec2(c.X + rin * Math.Cos(a), c.Y + rin * Math.Sin(a));
                int i = IndexNear(idx, want, 1e-12);
                Check.That(i >= 0, $"inner vertex {k} near ({want.X:R}, {want.Y:R})");
                Check.That(idx.Candidates[i].Kind == SnapKind.Intersection, $"inner vertex {k}: Intersection, got {idx.Candidates[i].Kind}");
            }

            // Board centre: user point on the dotted layer, also a grid corner, circle centre and guide crossing.
            int ci = IndexOfExact(idx, c);
            Check.That(ci >= 0 && idx.Candidates[ci].Kind == SnapKind.UserPoint && CountWithin(idx, c, 1e-7) == 1,
                "centre is one UserPoint candidate");
        }

        public static void LoneCircleGivesExactDivisionsAndCenter()
        {
            var board = GoldenBoards.StarOnFullBoard();
            foreach (var l in board.Layers) l.Visible = l.Name == GoldenBoards.CircleLayerName;
            board.Layers[1].SnapPoints.Clear();   // the circle layer's 8 user points
            var idx = new SnapIndex();
            idx.Rebuild(board);
            var c = GoldenBoards.BoardCenter;
            var divs = OfKind(idx, SnapKind.CircleDivision);
            Check.That(divs.Count == 8, $"8 divisions, got {divs.Count}");
            for (int k = 0; k < 8; k++)
                Check.That(divs[k] == GoldenBoards.StarPoint(c, GoldenBoards.StarRadius, k), $"division {k} == StarPoint, in k order");
            // The centre is also a 2x2 grid corner, which outranks Center.
            int ci = IndexOfExact(idx, c);
            Check.That(ci >= 0 && idx.Candidates[ci].Kind == SnapKind.CellCorner, "centre on grid corner reports CellCorner");
            Check.That(OfKind(idx, SnapKind.Center).Count == 0 && OfKind(idx, SnapKind.Intersection).Count == 0, "no other centre, no crossings");

            board.GridKind = GridKind.Hex;   // hex 2x2 has no vertex at the board centre
            idx.Sync(board);
            ci = IndexOfExact(idx, c);
            Check.That(ci >= 0 && idx.Candidates[ci].Kind == SnapKind.Center, $"hex grid: the centre is a Center candidate");
        }

        public static void HiddenLayerContributesNothing()
        {
            var empty = GoldenBoards.Empty();
            var baseline = new SnapIndex();
            baseline.Rebuild(empty);

            var board = GoldenBoards.Empty();
            var layer = board.Layers[0];
            layer.Shapes.Add(new LineShape(board.NewId(), new Vec2(0.1, 0.2), new Vec2(0.3, 0.7), Style));
            layer.Shapes.Add(new CircleShape(board.NewId(), new Vec2(0.3, 0.3), 0.15, Style));
            layer.SnapPoints.Add(new SnapPoint(board.NewId(), new Vec2(0.15, 0.85)));
            var idx = new SnapIndex();
            idx.Rebuild(board);
            Check.That(IndexOfExact(idx, new Vec2(0.1, 0.2)) >= 0 && IndexOfExact(idx, new Vec2(0.15, 0.85)) >= 0, "visible: endpoint and user point present");
            Check.That(OfKind(idx, SnapKind.Intersection).Count == 2, $"visible: line crosses circle twice, got {OfKind(idx, SnapKind.Intersection).Count}");

            layer.Visible = false;
            Check.That(idx.Sync(board), "visibility toggle rebuilds");
            Check.That(SameSequence(idx, baseline), "hidden layer: candidates identical to an empty board (grid only)");
        }

        public static void DedupKeepsHighestPriority()
        {
            var board = GoldenBoards.Empty();
            var layer = board.Layers[0];
            // Line endpoint exactly on the grid point (0.5, 0.5).
            layer.Shapes.Add(new LineShape(board.NewId(), new Vec2(0.5, 0.5), new Vec2(0.9, 0.3), Style));
            // Two lines whose ends are 5e-8 apart (same priority): the earlier one's position survives.
            layer.Shapes.Add(new LineShape(board.NewId(), new Vec2(0.3, 0.8), new Vec2(0.1, 0.9), Style));
            layer.Shapes.Add(new LineShape(board.NewId(), new Vec2(0.3 + 5e-8, 0.8), new Vec2(0.1, 0.6), Style));
            // A user point 5e-8 from the grid corner (1, 0.5): user point wins, keeping its own position.
            var up = new Vec2(1 - 5e-8, 0.5);
            layer.SnapPoints.Add(new SnapPoint(board.NewId(), up));
            var idx = new SnapIndex();
            idx.Rebuild(board);

            int i = IndexOfExact(idx, new Vec2(0.5, 0.5));
            Check.That(i >= 0 && idx.Candidates[i].Kind == SnapKind.Endpoint, "endpoint on grid point reports Endpoint");
            Check.That(CountWithin(idx, new Vec2(0.5, 0.5), 1e-7) == 1, "one candidate at (0.5,0.5)");

            Check.That(IndexOfExact(idx, new Vec2(0.3, 0.8)) >= 0 && IndexOfExact(idx, new Vec2(0.3 + 5e-8, 0.8)) < 0,
                "equal priority: the earlier endpoint survives");

            i = IndexOfExact(idx, up);
            Check.That(i >= 0 && idx.Candidates[i].Kind == SnapKind.UserPoint, "user point near a grid corner wins");
            Check.That(IndexOfExact(idx, new Vec2(1, 0.5)) < 0, "the grid corner it absorbed is gone");

            for (int a = 0; a < idx.Candidates.Count; a++)
                for (int b = a + 1; b < idx.Candidates.Count; b++)
                    Check.That(Vec2.Distance(idx.Candidates[a].Position, idx.Candidates[b].Position) > SnapIndex.DedupEpsilon,
                        $"candidates {a} and {b} are distinct");
        }

        public static void PolygonVerticesEdgesAndCenter()
        {
            var board = GoldenBoards.Empty();
            board.GridKind = GridKind.Hex;   // keep grid points away from the polygon
            var poly = new PolygonShape(board.NewId(), new Vec2(0.3, 0.7), 4, 0.1, 0.3, Style);
            board.Layers[0].Shapes.Add(poly);
            board.Layers[0].Shapes.Add(new LineShape(board.NewId(), new Vec2(0.1, 0.7), new Vec2(0.25, 0.7), Style));
            var idx = new SnapIndex();
            idx.Rebuild(board);
            var ends = OfKind(idx, SnapKind.Endpoint);
            for (int k = 0; k < 4; k++)
            {
                double a = 0.3 + 2.0 * Math.PI * k / 4;
                var v = new Vec2(0.3 + 0.1 * Math.Cos(a), 0.7 + 0.1 * Math.Sin(a));
                Check.That(ends.Contains(v), $"polygon vertex {k} is an Endpoint, exact");
            }
            Check.That(OfKind(idx, SnapKind.Center).Contains(new Vec2(0.3, 0.7)), "polygon centre");
            var x = OfKind(idx, SnapKind.Intersection);
            Check.That(x.Count == 1, $"line crosses one polygon edge (its other end is inside), got {x.Count}");
            Check.Near(x[0].Y, 0.7, "crossing on the line", 1e-12);
            // Left edge: distance r*cos(pi/4) from the centre along its normal at 0.3 + 3pi/4, so the crossing is that
            // distance divided by cos(pi - (0.3 + 3pi/4)) to the left of the centre.
            double dx = 0.1 * Math.Cos(Math.PI / 4) / Math.Cos(Math.PI - (0.3 + 3 * Math.PI / 4));
            Check.Near(x[0].X, 0.3 - dx, "crossing x on the left edge", 1e-12);
        }

        public static void PointsOutsideBoardAreDiscarded()
        {
            var board = GoldenBoards.Empty();
            var layer = board.Layers[0];
            layer.Shapes.Add(new CircleShape(board.NewId(), new Vec2(0.9, 0.5), 0.2, Style));   // pokes out at the right
            layer.SnapPoints.Add(new SnapPoint(board.NewId(), new Vec2(1 + 5e-10, 0.3)));       // within slack: kept
            layer.SnapPoints.Add(new SnapPoint(board.NewId(), new Vec2(1 + 1e-6, 0.4)));        // outside: dropped
            layer.SnapPoints.Add(new SnapPoint(board.NewId(), new Vec2(double.NaN, 0.4)));      // non-finite: dropped
            var idx = new SnapIndex();
            idx.Rebuild(board);
            // Divisions k = 0 (1.1, 0.5), 1 and 7 (x = 0.9 + 0.2 cos 45 ~ 1.04) are off the board.
            var divs = OfKind(idx, SnapKind.CircleDivision);
            Check.That(divs.Count == 5, $"5 of 8 divisions on the board, got {divs.Count}");
            Check.That(IndexOfExact(idx, new Vec2(1 + 5e-10, 0.3)) >= 0, "point within 1e-9 slack kept");
            Check.That(OfKind(idx, SnapKind.UserPoint).Count == 1, "off-board and NaN user points dropped");
            foreach (var cand in idx.Candidates)
                Check.That(cand.Position.IsFinite && cand.Position.X >= -1e-9 && cand.Position.X <= 1 + 1e-9
                    && cand.Position.Y >= -1e-9 && cand.Position.Y <= 1 + 1e-9, $"{cand.Kind} {cand.Position} inside");
        }

        public static void ShapeGridCrossingsFlag()
        {
            var board = GoldenBoards.Empty();
            board.Layers[0].Shapes.Add(new LineShape(board.NewId(), new Vec2(0.1, 0.2), new Vec2(0.9, 0.3), Style));
            var idx = new SnapIndex();
            Check.That(!idx.IncludeShapeGridCrossings, "off by default");
            idx.Sync(board);
            Check.That(OfKind(idx, SnapKind.Intersection).Count == 0, "no crossing with the x = 0.5 divider by default");

            idx.IncludeShapeGridCrossings = true;
            Check.That(idx.Sync(board), "flag change rebuilds");
            var x = OfKind(idx, SnapKind.Intersection);
            Check.That(x.Count == 1, $"crosses the x = 0.5 divider once, got {x.Count}");
            Check.Near(x[0].X, 0.5, "divider crossing x", 1e-12);
            Check.Near(x[0].Y, 0.25, "divider crossing y", 1e-12);
        }

        // ---------- SnapIndex: ordering, priority, lazy rebuild ----------

        public static void PriorityOrderMatchesPlan()
        {
            var order = new[] { SnapKind.UserPoint, SnapKind.Endpoint, SnapKind.Intersection, SnapKind.CellCorner, SnapKind.Center,
                SnapKind.CircleDivision, SnapKind.CellEdgeMid, SnapKind.CellCenter, SnapKind.None };
            Check.That(SnapIndex.Priority(SnapKind.UserPoint) == 0, "UserPoint is 0");
            for (int i = 1; i < order.Length; i++)
                Check.That(SnapIndex.Priority(order[i - 1]) < SnapIndex.Priority(order[i]), $"{order[i - 1]} above {order[i]}");
        }

        public static void CandidateOrderIsDeterministicAndByPriority()
        {
            var a = new SnapIndex();
            var b = new SnapIndex();
            a.Rebuild(GoldenBoards.StarOnFullBoard());
            b.Rebuild(GoldenBoards.StarOnFullBoard());
            Check.That(a.Candidates.Count > 40, $"star board has many candidates, got {a.Candidates.Count}");
            Check.That(SameSequence(a, b), "two builds of the same board give identical sequences");
            for (int i = 1; i < a.Candidates.Count; i++)
                Check.That(SnapIndex.Priority(a.Candidates[i - 1].Kind) <= SnapIndex.Priority(a.Candidates[i].Kind), $"sorted by priority at {i}");
            Check.That(a.Candidates[0].Kind == SnapKind.UserPoint && a.Candidates[0].Position == GoldenBoards.BoardCenter,
                "first candidate: the dotted layer's user point (bottom layer first)");
        }

        public static void SyncIsLazyAndDetectsChanges()
        {
            var board = GoldenBoards.StarOnFullBoard();
            var idx = new SnapIndex();
            Check.That(idx.Sync(board), "first Sync builds");
            var list = idx.Candidates;
            var before = Snapshot(idx);
            Check.That(!idx.Sync(board), "unchanged board: no rebuild");
            Check.That(ReferenceEquals(list, idx.Candidates) && SameAs(idx, before), "same list, same content");

            long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) idx.Sync(board);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            Check.That(allocated == 0, $"Sync with nothing changed allocates nothing, got {allocated} bytes");

            var star = board.Layers[2];
            star.Shapes.Add(new LineShape(board.NewId(), new Vec2(0.05, 0.05), new Vec2(0.05, 0.3), Style));
            Check.That(idx.Sync(board), "added shape rebuilds");
            Check.That(IndexOfExact(idx, new Vec2(0.05, 0.3)) >= 0, "new endpoint present");
            Check.That(!idx.Sync(board), "then quiet again");

            star.Visible = false;
            Check.That(idx.Sync(board), "visibility toggle rebuilds");
            Check.That(IndexOfExact(idx, new Vec2(0.05, 0.3)) < 0, "hidden layer's endpoint gone");

            board.GridN = 3;
            Check.That(idx.Sync(board), "GridN change rebuilds");
            board.Layers[0].SnapPoints.Add(new SnapPoint(board.NewId(), new Vec2(0.9, 0.1)));
            Check.That(idx.Sync(board), "added user point rebuilds");

            // A move keeps every count the same: invisible to the signature, so the owner calls MarkDirty.
            var moved = (LineShape)board.Layers[0].Shapes[0];
            moved.A = new Vec2(0.11, 0.5);
            Check.That(!idx.Sync(board), "pure move is not in the signature");
            idx.MarkDirty();
            Check.That(idx.Sync(board), "MarkDirty forces a rebuild");
            Check.That(IndexOfExact(idx, new Vec2(0.11, 0.5)) >= 0, "moved endpoint present after MarkDirty");

            Check.That(idx.Sync(GoldenBoards.StarOnFullBoard()), "another board instance rebuilds");
            Check.That(idx.Sync(null) && idx.Candidates.Count == 0 && !idx.Sync(null), "null board: empty, then quiet");
        }

        // ---------- helpers ----------

        static List<Vec2> OfKind(SnapIndex idx, SnapKind kind)
        {
            var list = new List<Vec2>();
            foreach (var c in idx.Candidates) if (c.Kind == kind) list.Add(c.Position);
            return list;
        }

        static int IndexOfExact(SnapIndex idx, Vec2 p)
        {
            for (int i = 0; i < idx.Candidates.Count; i++) if (idx.Candidates[i].Position == p) return i;
            return -1;
        }

        static int IndexNear(SnapIndex idx, Vec2 p, double eps)
        {
            for (int i = 0; i < idx.Candidates.Count; i++)
                if (Math.Abs(idx.Candidates[i].Position.X - p.X) <= eps && Math.Abs(idx.Candidates[i].Position.Y - p.Y) <= eps) return i;
            return -1;
        }

        static int CountWithin(SnapIndex idx, Vec2 p, double eps)
        {
            int n = 0;
            foreach (var c in idx.Candidates) if (Vec2.Distance(c.Position, p) <= eps) n++;
            return n;
        }

        static List<SnapCandidate> Snapshot(SnapIndex idx) => new List<SnapCandidate>(idx.Candidates);

        static bool SameAs(SnapIndex idx, List<SnapCandidate> snap)
        {
            if (idx.Candidates.Count != snap.Count) return false;
            for (int i = 0; i < snap.Count; i++)
                if (idx.Candidates[i].Kind != snap[i].Kind || idx.Candidates[i].Position != snap[i].Position) return false;
            return true;
        }

        static bool SameSequence(SnapIndex a, SnapIndex b) => SameAs(a, Snapshot(b));
    }
}
