using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>Axis-aligned rectangle in board units (doubles). Edges count as inside (see <see cref="Contains(Vec2, double)"/>).</summary>
    public readonly struct BoardRect
    {
        public readonly double MinX, MinY, MaxX, MaxY;

        public BoardRect(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        }

        /// <summary>The rectangle spanned by two opposite corners given in any order.</summary>
        public static BoardRect FromCorners(Vec2 a, Vec2 b) =>
            new BoardRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        /// <summary>All four bounds are finite numbers and Min &lt;= Max on both axes.</summary>
        public bool IsFinite =>
            IsNumber(MinX) && IsNumber(MinY) && IsNumber(MaxX) && IsNumber(MaxY) && MinX <= MaxX && MinY <= MaxY;

        /// <summary>True if <paramref name="p"/> lies inside or on the edge, with <paramref name="eps"/> slack. NaN is outside.</summary>
        public bool Contains(Vec2 p, double eps = SelectionGeometry.EdgeEpsilon) =>
            p.X >= MinX - eps && p.X <= MaxX + eps && p.Y >= MinY - eps && p.Y <= MaxY + eps;

        /// <summary>True if <paramref name="inner"/> lies wholly inside (edges inclusive, <paramref name="eps"/> slack).</summary>
        public bool Contains(BoardRect inner, double eps = SelectionGeometry.EdgeEpsilon) =>
            inner.MinX >= MinX - eps && inner.MaxX <= MaxX + eps && inner.MinY >= MinY - eps && inner.MaxY <= MaxY + eps;

        public static BoardRect Union(BoardRect a, BoardRect b) =>
            new BoardRect(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));

        static bool IsNumber(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public override string ToString() => $"[{MinX:0.#####}, {MinY:0.#####} .. {MaxX:0.#####}, {MaxY:0.#####}]";
    }

    /// <summary>
    /// Select tool geometry in board units: shape bounds, full enclosure by a marquee rectangle, distance to a shape's
    /// stroke for tap-picking. Pure C#, allocation-free. Non-drawable shapes (null, non-finite values, radius &lt;= 0,
    /// polygons with fewer than 3 sides) have no bounds, are never enclosed and are infinitely far away. Only visible
    /// layers take part.
    /// </summary>
    public static class SelectionGeometry
    {
        /// <summary>Slack for "on the edge counts as inside", in board units.</summary>
        public const double EdgeEpsilon = 1e-9;

        /// <summary>The shape's axis-aligned bounds. False for null or non-drawable shapes.</summary>
        public static bool TryGetBounds(Shape s, out BoardRect b)
        {
            b = default;
            switch (s)
            {
                case LineShape line:
                    if (!line.A.IsFinite || !line.B.IsFinite) return false;
                    b = BoardRect.FromCorners(line.A, line.B);
                    return true;

                case CircleShape circle:
                    if (!IsDrawable(circle)) return false;
                    double r = circle.Radius;
                    b = new BoardRect(circle.Center.X - r, circle.Center.Y - r, circle.Center.X + r, circle.Center.Y + r);
                    return b.IsFinite;

                case PolygonShape poly:
                    if (!IsDrawable(poly)) return false;
                    Vec2 v0 = SnapIndex.PolygonVertex(poly, 0);
                    double minX = v0.X, minY = v0.Y, maxX = v0.X, maxY = v0.Y;
                    for (int k = 1; k < poly.Sides; k++)
                    {
                        Vec2 v = SnapIndex.PolygonVertex(poly, k);
                        if (v.X < minX) minX = v.X;
                        if (v.X > maxX) maxX = v.X;
                        if (v.Y < minY) minY = v.Y;
                        if (v.Y > maxY) maxY = v.Y;
                    }
                    b = new BoardRect(minX, minY, maxX, maxY);
                    return b.IsFinite;

                default:
                    return false;
            }
        }

        /// <summary>
        /// True if the whole shape lies inside <paramref name="r"/> (edges inclusive, <see cref="EdgeEpsilon"/>): a line's
        /// two endpoints, a circle's centre +- radius on both axes, every polygon vertex. Non-drawable shapes: false.
        /// </summary>
        public static bool IsEnclosed(Shape s, BoardRect r) =>
            r.IsFinite && TryGetBounds(s, out BoardRect b) && r.Contains(b);

        /// <summary>
        /// Board distance from <paramref name="p"/> to the drawn stroke: the segment for a line, the ring for a circle
        /// (| |p - c| - r |, so the inside of a circle is not "on" it), the nearest edge for a polygon.
        /// +Infinity when the shape is not drawable or <paramref name="p"/> is not finite.
        /// </summary>
        public static double DistanceToStroke(Vec2 p, Shape s)
        {
            if (!p.IsFinite) return double.PositiveInfinity;
            switch (s)
            {
                case LineShape line:
                    if (!line.A.IsFinite || !line.B.IsFinite) return double.PositiveInfinity;
                    return SegmentDistance(p, line.A, line.B);

                case CircleShape circle:
                    if (!IsDrawable(circle)) return double.PositiveInfinity;
                    return Math.Abs(Vec2.Distance(p, circle.Center) - circle.Radius);

                case PolygonShape poly:
                    if (!IsDrawable(poly)) return double.PositiveInfinity;
                    Vec2 first = SnapIndex.PolygonVertex(poly, 0), prev = first;
                    double best = double.PositiveInfinity;
                    for (int k = 1; k <= poly.Sides; k++)
                    {
                        Vec2 v = k == poly.Sides ? first : SnapIndex.PolygonVertex(poly, k);
                        double d = SegmentDistance(p, prev, v);
                        if (d < best) best = d;
                        prev = v;
                    }
                    return best;

                default:
                    return double.PositiveInfinity;
            }
        }

        /// <summary>
        /// Clears <paramref name="into"/>, then adds the id of every shape on a visible layer that <paramref name="r"/>
        /// encloses. A null board leaves it empty. Allocation-free once the set has grown.
        /// </summary>
        public static void CollectEnclosed(Board board, BoardRect r, HashSet<EntityId> into)
        {
            into.Clear();
            if (board == null || !r.IsFinite) return;
            var layers = board.Layers;
            for (int li = 0; li < layers.Count; li++)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = 0; si < shapes.Count; si++)
                {
                    Shape s = shapes[si];
                    if (s != null && IsEnclosed(s, r)) into.Add(s.Id);
                }
            }
        }

        /// <summary>
        /// The shape on a visible layer whose stroke is nearest <paramref name="p"/>, within <paramref name="tolerance"/>
        /// (inclusive). Ties go to the topmost: layers are scanned top to bottom and shapes last to first, and a later
        /// candidate replaces the best only if it is strictly nearer. False (id None) if nothing is in range.
        /// </summary>
        public static bool TryPick(Board board, Vec2 p, double tolerance, out EntityId id)
        {
            id = EntityId.None;
            if (board == null || !p.IsFinite || !(tolerance >= 0)) return false;
            double best = double.PositiveInfinity;
            bool found = false;
            var layers = board.Layers;
            for (int li = layers.Count - 1; li >= 0; li--)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = shapes.Count - 1; si >= 0; si--)
                {
                    Shape s = shapes[si];
                    if (s == null) continue;
                    double d = DistanceToStroke(p, s);
                    if (!(d <= tolerance)) continue;
                    if (found && !(d < best)) continue;
                    found = true;
                    best = d;
                    id = s.Id;
                }
            }
            return found;
        }

        static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double len2 = abx * abx + aby * aby;
            double t = len2 > 0 ? ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2 : 0;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double dx = p.X - (a.X + abx * t), dy = p.Y - (a.Y + aby * t);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static bool IsDrawable(CircleShape c) =>
            c.Radius > 0 && !double.IsInfinity(c.Radius) && c.Center.IsFinite;

        static bool IsDrawable(PolygonShape p) =>
            p.Sides >= 3 && p.Radius > 0 && !double.IsInfinity(p.Radius) && p.Center.IsFinite
            && !double.IsNaN(p.RotationRad) && !double.IsInfinity(p.RotationRad);
    }
}
