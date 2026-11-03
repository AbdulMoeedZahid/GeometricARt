using System;
using System.Collections.Generic;

namespace GeoART.Contracts
{
    /// <summary>One grid tile in board space. Corners are counter-clockwise.</summary>
    public sealed class GridCell
    {
        public readonly int Column, Row;
        public readonly Vec2 Center;
        public readonly Vec2[] Corners;

        public GridCell(int column, int row, Vec2 center, Vec2[] corners)
        {
            Column = column; Row = row; Center = center; Corners = corners;
        }
    }

    /// <summary>
    /// Pure grid geometry for <see cref="Board.GridKind"/> and <see cref="Board.GridN"/> (n is clamped to
    /// [<see cref="Board.MinGridN"/>, <see cref="Board.MaxGridN"/>]). Every tile lies fully inside the board [0,1]^2:
    ///   Square  n x n squares of side 1/n that cover the board exactly.
    ///   Hex     n columns x n rows of pointy-top hexagons, odd rows shifted right by half a hex (offset rows), so
    ///           neighbours share whole edges. A square cannot be tiled by whole hexagons, so the radius is the largest
    ///           that fits both ways (see <see cref="HexRadius"/>) and the block is centred, leaving equal margins.
    /// Used by the renderer (<see cref="UniqueEdges"/>) and by cell snapping (<see cref="SnapPoints"/>).
    /// </summary>
    public static class TileGrid
    {
        static readonly double Sqrt3 = Math.Sqrt(3.0);

        public static int ClampN(int n) => Math.Max(Board.MinGridN, Math.Min(Board.MaxGridN, n));

        /// <summary>
        /// Circumradius of a hex tile in board units. Width of the block: sqrt(3) r (n + 0.5) for n &gt; 1 (the half is
        /// the row offset), sqrt(3) r for n = 1. Height: r (1.5 n + 0.5). Both must be &lt;= 1.
        /// </summary>
        public static double HexRadius(int n)
        {
            n = ClampN(n);
            double widthUnits = Sqrt3 * (n > 1 ? n + 0.5 : 1.0);
            double heightUnits = 1.5 * n + 0.5;
            return 1.0 / Math.Max(widthUnits, heightUnits);
        }

        public static List<GridCell> Cells(GridKind kind, int n)
        {
            var cells = new List<GridCell>();
            Cells(kind, n, cells);
            return cells;
        }

        /// <summary>Fills <paramref name="into"/> (cleared first), row by row from the bottom.</summary>
        public static void Cells(GridKind kind, int n, List<GridCell> into)
        {
            into.Clear();
            n = ClampN(n);
            if (kind == GridKind.Hex) HexCells(n, into);
            else SquareCells(n, into);
        }

        static void SquareCells(int n, List<GridCell> into)
        {
            double s = 1.0 / n;
            for (int row = 0; row < n; row++)
                for (int col = 0; col < n; col++)
                {
                    double x0 = col * s, y0 = row * s;
                    double x1 = col == n - 1 ? 1.0 : x0 + s, y1 = row == n - 1 ? 1.0 : y0 + s;   // exact outer edge
                    into.Add(new GridCell(col, row, new Vec2((x0 + x1) * 0.5, (y0 + y1) * 0.5), new[]
                    {
                        new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1),
                    }));
                }
        }

        static void HexCells(int n, List<GridCell> into)
        {
            double r = HexRadius(n);
            double w = Sqrt3 * r;
            double blockW = w * (n > 1 ? n + 0.5 : 1.0);
            double blockH = r * (1.5 * n + 0.5);
            double x0 = (1.0 - blockW) * 0.5 + w * 0.5;   // centre of the bottom-left hex
            double y0 = (1.0 - blockH) * 0.5 + r;
            for (int row = 0; row < n; row++)
                for (int col = 0; col < n; col++)
                {
                    var c = new Vec2(x0 + col * w + (row % 2 == 1 ? w * 0.5 : 0.0), y0 + row * 1.5 * r);
                    var corners = new Vec2[6];
                    for (int k = 0; k < 6; k++)
                    {
                        double a = Math.PI / 6.0 + k * Math.PI / 3.0;   // pointy-top: vertices at 30, 90, ... degrees
                        corners[k] = new Vec2(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
                    }
                    into.Add(new GridCell(col, row, c, corners));
                }
        }

        /// <summary>
        /// Every tile edge once (shared edges are not repeated, so translucent or dashed strokes do not double up).
        /// With <paramref name="skipBoardBoundary"/>, edges lying on the board frame are left out (the frame draws them).
        /// </summary>
        public static void UniqueEdges(GridKind kind, int n, List<(Vec2 A, Vec2 B)> into, bool skipBoardBoundary = false)
        {
            into.Clear();
            var seen = new HashSet<(long, long, long, long)>();
            foreach (var cell in Cells(kind, n))
            {
                var p = cell.Corners;
                for (int k = 0; k < p.Length; k++)
                {
                    Vec2 a = p[k], b = p[(k + 1) % p.Length];
                    if (skipBoardBoundary && OnBoundary(a, b)) continue;
                    var ka = Key(a);
                    var kb = Key(b);
                    var key = Less(ka, kb) ? (ka.Item1, ka.Item2, kb.Item1, kb.Item2) : (kb.Item1, kb.Item2, ka.Item1, ka.Item2);
                    if (seen.Add(key)) into.Add((a, b));
                }
            }
        }

        /// <summary>Tile corners, edge midpoints and centres, each position once.</summary>
        public static void SnapPoints(GridKind kind, int n, List<SnapCandidate> into)
        {
            into.Clear();
            var seen = new HashSet<(long, long)>();
            foreach (var cell in Cells(kind, n))
            {
                var p = cell.Corners;
                for (int k = 0; k < p.Length; k++)
                {
                    Add(p[k], SnapKind.CellCorner);
                    Vec2 b = p[(k + 1) % p.Length];
                    Add(new Vec2((p[k].X + b.X) * 0.5, (p[k].Y + b.Y) * 0.5), SnapKind.CellEdgeMid);
                }
                Add(cell.Center, SnapKind.CellCenter);
            }

            void Add(Vec2 v, SnapKind k)
            {
                if (seen.Add(Key(v))) into.Add(new SnapCandidate(v, k));
            }
        }

        const double KeyScale = 1e7;   // positions closer than 1e-7 board units are the same point

        static (long, long) Key(Vec2 v) => ((long)Math.Round(v.X * KeyScale), (long)Math.Round(v.Y * KeyScale));

        static bool Less((long, long) a, (long, long) b) => a.Item1 < b.Item1 || (a.Item1 == b.Item1 && a.Item2 < b.Item2);

        static bool OnBoundary(Vec2 a, Vec2 b)
        {
            const double eps = 1e-9;
            return (Math.Abs(a.X) < eps && Math.Abs(b.X) < eps) || (Math.Abs(a.X - 1) < eps && Math.Abs(b.X - 1) < eps)
                || (Math.Abs(a.Y) < eps && Math.Abs(b.Y) < eps) || (Math.Abs(a.Y - 1) < eps && Math.Abs(b.Y - 1) < eps);
        }
    }
}
