using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>TileGrid geometry (square and hex tiles fit the board) and EditorSession.SetGrid / SetGridKind.</summary>
    public static class GridChecks
    {
        const double Eps = 1e-9;

        static readonly GridKind[] Kinds = { GridKind.Square, GridKind.Hex };

        static void ForEachLayout(Action<GridKind, int> body)
        {
            foreach (var kind in Kinds)
                for (int n = Board.MinGridN; n <= Board.MaxGridN; n++)
                    body(kind, n);
        }

        public static void CellCountIsNSquared()
        {
            ForEachLayout((kind, n) =>
                Check.That(TileGrid.Cells(kind, n).Count == n * n, $"{kind} {n}: {n * n} cells"));
        }

        public static void EveryCornerInsideBoard()
        {
            ForEachLayout((kind, n) =>
            {
                foreach (var c in TileGrid.Cells(kind, n))
                    foreach (var v in c.Corners)
                        Check.That(v.X >= -Eps && v.X <= 1 + Eps && v.Y >= -Eps && v.Y <= 1 + Eps,
                            $"{kind} {n}: corner {v.X},{v.Y} outside the board");
            });
        }

        public static void SquareCellsCoverBoardExactly()
        {
            for (int n = Board.MinGridN; n <= Board.MaxGridN; n++)
            {
                double area = 0;
                foreach (var c in TileGrid.Cells(GridKind.Square, n)) area += Area(c.Corners);
                Check.Near(area, 1.0, $"square {n}: total area");
            }
        }

        public static void HexCellsAreRegularAndCentred()
        {
            for (int n = Board.MinGridN; n <= Board.MaxGridN; n++)
            {
                double r = TileGrid.HexRadius(n);
                var cells = TileGrid.Cells(GridKind.Hex, n);
                double minX = 1, maxX = 0, minY = 1, maxY = 0;
                foreach (var c in cells)
                {
                    Check.That(c.Corners.Length == 6, "six corners");
                    for (int k = 0; k < 6; k++)
                    {
                        Check.Near(Vec2.Distance(c.Center, c.Corners[k]), r, $"hex {n}: radius");
                        Check.Near(Vec2.Distance(c.Corners[k], c.Corners[(k + 1) % 6]), r, $"hex {n}: side");
                        minX = Math.Min(minX, c.Corners[k].X); maxX = Math.Max(maxX, c.Corners[k].X);
                        minY = Math.Min(minY, c.Corners[k].Y); maxY = Math.Max(maxY, c.Corners[k].Y);
                    }
                }
                Check.Near(minX, 1 - maxX, $"hex {n}: equal left/right margins");
                Check.Near(minY, 1 - maxY, $"hex {n}: equal top/bottom margins");
                Check.That(Math.Abs(minX) < Eps || Math.Abs(minY) < Eps, $"hex {n}: the block touches the board on one axis (largest fit)");
            }
        }

        public static void HexNeighboursShareEdgesAndNeverOverlap()
        {
            for (int n = 2; n <= Board.MaxGridN; n++)
            {
                var cells = TileGrid.Cells(GridKind.Hex, n);
                double r = TileGrid.HexRadius(n);
                double neighbour = Math.Sqrt(3) * r;
                int shared = 0;
                for (int i = 0; i < cells.Count; i++)
                    for (int j = i + 1; j < cells.Count; j++)
                    {
                        double d = Vec2.Distance(cells[i].Center, cells[j].Center);
                        Check.That(d >= neighbour - Eps, $"hex {n}: cells {i},{j} overlap");
                        if (Math.Abs(d - neighbour) < Eps) shared++;
                    }
                // Offset rows: n-1 per row horizontally, plus 2n-1 diagonal pairs between each pair of rows.
                int expected = n * (n - 1) + (n - 1) * (2 * n - 1);
                Check.That(shared == expected, $"hex {n}: {expected} neighbour pairs, got {shared}");

                var edges = new List<(Vec2 A, Vec2 B)>();
                TileGrid.UniqueEdges(GridKind.Hex, n, edges);
                Check.That(edges.Count == 6 * n * n - shared, $"hex {n}: shared edges drawn once ({edges.Count})");
            }
        }

        public static void SquareEdgesSkipBoundaryWhenAsked()
        {
            var edges = new List<(Vec2 A, Vec2 B)>();
            TileGrid.UniqueEdges(GridKind.Square, 2, edges);
            Check.That(edges.Count == 12, $"2x2: 12 unique edges, got {edges.Count}");
            TileGrid.UniqueEdges(GridKind.Square, 2, edges, skipBoardBoundary: true);
            Check.That(edges.Count == 4, $"2x2 interior: 4 half-dividers, got {edges.Count}");
            TileGrid.UniqueEdges(GridKind.Square, 1, edges, skipBoardBoundary: true);
            Check.That(edges.Count == 0, "1x1 has no interior edges");
        }

        public static void SnapPointsAreUnique()
        {
            var pts = new List<SnapCandidate>();
            TileGrid.SnapPoints(GridKind.Square, 2, pts);
            Check.That(pts.Count == 25, $"2x2 square: 5x5 lattice, got {pts.Count}");
            TileGrid.SnapPoints(GridKind.Hex, 3, pts);
            var seen = new HashSet<(double, double)>();
            foreach (var p in pts) Check.That(seen.Add((Math.Round(p.Position.X, 7), Math.Round(p.Position.Y, 7))), "no duplicate");
        }

        public static void SessionGridKindRaisesGridOnce()
        {
            var s = new EditorSession();
            var seen = new List<BoardChangeKind>();
            s.BoardChanged += k => seen.Add(k);
            Check.That(s.Board.GridKind == GridKind.Square, "default square");
            s.SetGridKind(GridKind.Hex);
            s.SetGridKind(GridKind.Hex);
            Check.That(s.Board.GridKind == GridKind.Hex, "hex set");
            Check.That(seen.Count == 1 && seen[0] == BoardChangeKind.Grid, "one Grid event, repeats ignored");
            s.SetGridKind((GridKind)42);
            Check.That(s.Board.GridKind == GridKind.Hex && seen.Count == 1, "unknown kind ignored");
            s.SetGrid(99);
            Check.That(s.Board.GridN == Board.MaxGridN, "n clamped");
        }

        public static void ClearBoardResetsToSquare()
        {
            var s = new EditorSession();
            s.SetGridKind(GridKind.Hex);
            s.ClearBoard();
            Check.That(s.Board.GridKind == GridKind.Square && s.Board.GridN == 2, "fresh board is 2x2 square");
        }

        static double Area(Vec2[] p)
        {
            double a = 0;
            for (int i = 0; i < p.Length; i++)
            {
                Vec2 u = p[i], v = p[(i + 1) % p.Length];
                a += u.X * v.Y - v.X * u.Y;
            }
            return a * 0.5;
        }
    }
}
