using System;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// p -&gt; Pivot + Scale * (p - Pivot) + Offset; radii * Scale. A translation has Scale 1 and is then exactly
    /// p + Offset (no pivot arithmetic, so snapped positions stay exact).
    /// </summary>
    public readonly struct ShapeTransform
    {
        public readonly Vec2 Pivot, Offset;
        public readonly double Scale;

        ShapeTransform(Vec2 pivot, Vec2 offset, double scale)
        {
            Pivot = pivot; Offset = offset; Scale = scale;
        }

        public static ShapeTransform Identity => new ShapeTransform(Vec2.Zero, Vec2.Zero, 1.0);

        public static ShapeTransform Translation(Vec2 t) => new ShapeTransform(Vec2.Zero, t, 1.0);

        public static ShapeTransform ScaleAbout(Vec2 pivot, double s) => new ShapeTransform(pivot, Vec2.Zero, s);

        /// <summary>Offset == (0,0) and Scale == 1.</summary>
        public bool IsIdentity => Offset.X == 0 && Offset.Y == 0 && Scale == 1.0;

        public Vec2 Apply(Vec2 p) =>
            Scale == 1.0
                ? new Vec2(p.X + Offset.X, p.Y + Offset.Y)
                : new Vec2(Pivot.X + Scale * (p.X - Pivot.X) + Offset.X, Pivot.Y + Scale * (p.Y - Pivot.Y) + Offset.Y);
    }

    /// <summary>Which point of a shape a transform gesture holds: the selection's pivot, a line end, a centre, a polygon vertex, a ring point.</summary>
    public enum GrabKind { Pivot, LineA, LineB, Center, Vertex, Ring }

    /// <summary>
    /// Move/Scale/Duplicate geometry in board units. Pure C#, allocation-free. Non-drawable shapes (null, non-finite
    /// values, radius &lt;= 0, polygons with fewer than 3 sides) have no key points, no support point and size 0.
    /// </summary>
    public static class TransformGeometry
    {
        /// <summary>
        /// Writes <paramref name="src"/>'s transformed geometry into <paramref name="dst"/> (same concrete type; Id and
        /// Style untouched). Safe when src == dst: every input is read before anything is written. Polygon: Center
        /// transformed, Radius * Scale, RotationRad and Sides copied. Circle: Radius * Scale. False on a type mismatch or null.
        /// </summary>
        public static bool Apply(Shape src, in ShapeTransform xf, Shape dst)
        {
            if (src == null || dst == null) return false;
            switch (src)
            {
                case LineShape line:
                {
                    if (!(dst is LineShape d)) return false;
                    Vec2 a = line.A, b = line.B;
                    d.A = xf.Apply(a);
                    d.B = xf.Apply(b);
                    return true;
                }
                case CircleShape circle:
                {
                    if (!(dst is CircleShape d)) return false;
                    Vec2 c = circle.Center;
                    double r = circle.Radius;
                    d.Center = xf.Apply(c);
                    d.Radius = xf.Scale == 1.0 ? r : r * xf.Scale;
                    return true;
                }
                case PolygonShape poly:
                {
                    if (!(dst is PolygonShape d)) return false;
                    Vec2 c = poly.Center;
                    double r = poly.Radius, rot = poly.RotationRad;
                    int sides = poly.Sides;
                    d.Center = xf.Apply(c);
                    d.Radius = xf.Scale == 1.0 ? r : r * xf.Scale;
                    d.RotationRad = rot;
                    d.Sides = sides;
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>Line length, circle radius, polygon radius; 0 if not drawable.</summary>
        public static double Size(Shape s)
        {
            switch (s)
            {
                case LineShape line:
                    if (!line.A.IsFinite || !line.B.IsFinite) return 0;
                    double len = Vec2.Distance(line.A, line.B);
                    return IsNumber(len) ? len : 0;
                case CircleShape circle:
                    return IsDrawable(circle) ? circle.Radius : 0;
                case PolygonShape poly:
                    return IsDrawable(poly) ? poly.Radius : 0;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Key point of <paramref name="s"/> nearest <paramref name="p"/>: line A and B, circle centre, polygon centre and
        /// vertices (the first wins a tie). <paramref name="vertex"/> is the polygon vertex index (else -1). False if none.
        /// </summary>
        public static bool TryNearestKeyPoint(Shape s, Vec2 p, out Vec2 point, out GrabKind kind, out int vertex,
            out double distance)
        {
            point = default;
            kind = GrabKind.Pivot;
            vertex = -1;
            distance = double.PositiveInfinity;
            if (!p.IsFinite) return false;
            bool found = false;
            switch (s)
            {
                case LineShape line:
                    if (line.A.IsFinite) Offer(line.A, GrabKind.LineA, -1, p, ref found, ref point, ref kind, ref vertex, ref distance);
                    if (line.B.IsFinite) Offer(line.B, GrabKind.LineB, -1, p, ref found, ref point, ref kind, ref vertex, ref distance);
                    break;
                case CircleShape circle:
                    if (IsDrawable(circle))
                        Offer(circle.Center, GrabKind.Center, -1, p, ref found, ref point, ref kind, ref vertex, ref distance);
                    break;
                case PolygonShape poly:
                    if (!IsDrawable(poly)) break;
                    Offer(poly.Center, GrabKind.Center, -1, p, ref found, ref point, ref kind, ref vertex, ref distance);
                    for (int k = 0; k < poly.Sides; k++)
                        Offer(SnapIndex.PolygonVertex(poly, k), GrabKind.Vertex, k, p, ref found, ref point, ref kind,
                            ref vertex, ref distance);
                    break;
            }
            return found;
        }

        static void Offer(Vec2 q, GrabKind k, int v, Vec2 p, ref bool found, ref Vec2 point, ref GrabKind kind,
            ref int vertex, ref double distance)
        {
            double d = Vec2.Distance(q, p);
            if (!IsNumber(d) || (found && !(d < distance))) return;
            found = true;
            point = q;
            kind = k;
            vertex = v;
            distance = d;
        }

        /// <summary>
        /// Support point of <paramref name="s"/> along the unit direction <paramref name="dir"/> from
        /// <paramref name="pivot"/>: the point of the geometry farthest along dir. Line: the endpoint with the larger
        /// (X - pivot)·dir (a tie picks A). Circle: centre + radius * dir (<see cref="GrabKind.Ring"/>). Polygon: the vertex
        /// with the largest dot (the first wins a tie). <paramref name="along"/> is that dot. False if not drawable.
        /// </summary>
        public static bool TrySupportPoint(Shape s, Vec2 pivot, Vec2 dir, out Vec2 point, out GrabKind kind,
            out int vertex, out double along)
        {
            point = default;
            kind = GrabKind.Pivot;
            vertex = -1;
            along = double.NegativeInfinity;
            if (!pivot.IsFinite || !dir.IsFinite) return false;
            switch (s)
            {
                case LineShape line:
                {
                    if (!line.A.IsFinite || !line.B.IsFinite) return false;
                    double da = Dot(line.A, pivot, dir), db = Dot(line.B, pivot, dir);
                    if (db > da) { point = line.B; kind = GrabKind.LineB; along = db; }
                    else { point = line.A; kind = GrabKind.LineA; along = da; }
                    return true;
                }
                case CircleShape circle:
                {
                    if (!IsDrawable(circle)) return false;
                    double r = circle.Radius;
                    point = new Vec2(circle.Center.X + r * dir.X, circle.Center.Y + r * dir.Y);
                    kind = GrabKind.Ring;
                    along = Dot(point, pivot, dir);
                    return true;
                }
                case PolygonShape poly:
                {
                    if (!IsDrawable(poly)) return false;
                    for (int k = 0; k < poly.Sides; k++)
                    {
                        Vec2 v = SnapIndex.PolygonVertex(poly, k);
                        double d = Dot(v, pivot, dir);
                        if (k > 0 && !(d > along)) continue;
                        point = v;
                        along = d;
                        vertex = k;
                    }
                    kind = GrabKind.Vertex;
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// "Never worse" translation limits for the bounds <paramref name="b"/> (lo &lt;= 0 &lt;= hi on each axis):
        /// loX = min(0, -MinX), hiX = max(0, 1 - MaxX), the same for Y. A translation inside them keeps a selection that is
        /// inside [0,1]^2 inside, never pushes one that sticks out further out, and never forces it to jump in.
        /// </summary>
        public static void TranslationLimits(BoardRect b, out double loX, out double hiX, out double loY, out double hiY)
        {
            loX = Math.Min(0, -b.MinX);
            hiX = Math.Max(0, 1 - b.MaxX);
            loY = Math.Min(0, -b.MinY);
            hiY = Math.Max(0, 1 - b.MaxY);
        }

        /// <summary>
        /// Largest uniform scale about <paramref name="pivot"/> that keeps <paramref name="b"/> inside [0,1]^2, never
        /// less than 1 (never worse). Per side: the pivot's distance to the board edge over the bounds' extent on that side,
        /// e.g. right (1 - P.X) / (MaxX - P.X); a side whose extent is &lt;= 1e-12 is skipped.
        /// </summary>
        public static double MaxScaleInBoard(BoardRect b, Vec2 pivot)
        {
            double s = double.PositiveInfinity;
            Side(1 - pivot.X, b.MaxX - pivot.X, ref s);
            Side(pivot.X, pivot.X - b.MinX, ref s);
            Side(1 - pivot.Y, b.MaxY - pivot.Y, ref s);
            Side(pivot.Y, pivot.Y - b.MinY, ref s);
            return Math.Max(1.0, s);
        }

        static void Side(double room, double extent, ref double s)
        {
            if (!(extent > 1e-12)) return;
            double k = room / extent;
            if (k < s) s = k;
        }

        static double Dot(Vec2 q, Vec2 pivot, Vec2 dir) => (q.X - pivot.X) * dir.X + (q.Y - pivot.Y) * dir.Y;

        static bool IsNumber(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        static bool IsDrawable(CircleShape c) =>
            c.Radius > 0 && !double.IsInfinity(c.Radius) && c.Center.IsFinite;

        static bool IsDrawable(PolygonShape p) =>
            p.Sides >= 3 && p.Radius > 0 && !double.IsInfinity(p.Radius) && p.Center.IsFinite
            && !double.IsNaN(p.RotationRad) && !double.IsInfinity(p.RotationRad);
    }
}
