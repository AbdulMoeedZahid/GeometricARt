using System;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// Pure 2D intersection math in board units (doubles). Every method is allocation-free and returns how many points
    /// it wrote; unused out parameters are set to <see cref="Vec2.Zero"/>. Degenerate input (zero-length segment or
    /// direction, radius &lt;= 0, non-finite values) gives 0.
    ///
    /// Exactness: when a crossing lies within <see cref="ParamEps"/> of a segment end, the end itself is returned
    /// (bit-identical), so a T-junction reports the touching endpoint, not a value 1 ulp away.
    /// </summary>
    public static class Intersect
    {
        /// <summary>Slack in board units for "on the segment" (end touches count) and for tangency.</summary>
        public const double ParamEps = 1e-9;

        /// <summary>Two directions are parallel when |cross(u, v)| &lt;= ParallelSinEps * |u| * |v| (sine of the angle).</summary>
        public const double ParallelSinEps = 1e-12;

        /// <summary>
        /// Crossing of segments a0-a1 and b0-b1. End touches count (T-junctions, shared ends). Parallel and collinear
        /// segments give 0, even when they overlap or share an end.
        /// </summary>
        public static int SegmentSegment(Vec2 a0, Vec2 a1, Vec2 b0, Vec2 b1, out Vec2 p)
        {
            p = Vec2.Zero;
            Vec2 r = a1 - a0, s = b1 - b0;
            double lenR = r.Length, lenS = s.Length;
            if (!(lenR > 0) || !(lenS > 0) || double.IsInfinity(lenR) || double.IsInfinity(lenS)) return 0;
            double denom = Cross(r, s);
            if (!(Math.Abs(denom) > ParallelSinEps * lenR * lenS)) return 0;   // parallel, collinear or NaN

            Vec2 w = b0 - a0;
            double t = Cross(w, s) / denom;   // along a, 0..1
            double u = Cross(w, r) / denom;   // along b, 0..1
            double slackT = ParamEps / lenR, slackU = ParamEps / lenS;
            if (t < -slackT || t > 1 + slackT || u < -slackU || u > 1 + slackU) return 0;

            if (Math.Abs(t) <= slackT) p = a0;
            else if (Math.Abs(t - 1) <= slackT) p = a1;
            else if (Math.Abs(u) <= slackU) p = b0;
            else if (Math.Abs(u - 1) <= slackU) p = b1;
            else p = new Vec2(a0.X + r.X * t, a0.Y + r.Y * t);
            return p.IsFinite ? 1 : 0;
        }

        /// <summary>
        /// Segment a-b against the circle outline. 2 = secant (p0 nearer a), 1 = tangent or only one crossing inside the
        /// segment, 0 = miss (including a segment entirely inside the circle).
        /// </summary>
        public static int SegmentCircle(Vec2 a, Vec2 b, Vec2 center, double radius, out Vec2 p0, out Vec2 p1)
        {
            p0 = p1 = Vec2.Zero;
            Vec2 d = b - a;
            double len = d.Length;
            if (!(len > 0) || double.IsInfinity(len)) return 0;
            int n = LineCircleCore(a, d, len, center, radius, out Vec2 q0, out double s0, out Vec2 q1, out double s1);
            int count = 0;
            if (n >= 1) count = Keep(a, b, len, q0, s0, count, ref p0, ref p1);
            if (n == 2) count = Keep(a, b, len, q1, s1, count, ref p0, ref p1);
            return count;
        }

        /// <summary>
        /// Two circle outlines. 2 = crossing, 1 = tangent (outside or inside), 0 = apart, nested, or concentric
        /// (including identical circles). For 2, p0 is to the left of the direction c0 -> c1.
        /// </summary>
        public static int CircleCircle(Vec2 c0, double r0, Vec2 c1, double r1, out Vec2 p0, out Vec2 p1)
        {
            p0 = p1 = Vec2.Zero;
            if (!(r0 > 0) || !(r1 > 0) || !c0.IsFinite || !c1.IsFinite || double.IsInfinity(r0) || double.IsInfinity(r1))
                return 0;
            Vec2 delta = c1 - c0;
            double dist = delta.Length;
            if (!(dist > ParamEps)) return 0;                                       // concentric
            double sum = r0 + r1, diff = Math.Abs(r0 - r1);
            if (dist > sum + ParamEps || dist < diff - ParamEps) return 0;          // apart or nested

            double ux = delta.X / dist, uy = delta.Y / dist;
            double along = (dist * dist + r0 * r0 - r1 * r1) / (2 * dist);           // from c0 toward c1
            double h2 = r0 * r0 - along * along;
            if (Math.Abs(dist - sum) <= ParamEps || Math.Abs(dist - diff) <= ParamEps || !(h2 > 0))
            {
                // Tangent: the touch point lies on the centre line at +-r0 from c0 (along ~ +r0 outside or when c0's
                // circle is the larger one, ~ -r0 when c0's circle is inside the other).
                double a = along >= 0 ? r0 : -r0;
                p0 = new Vec2(c0.X + ux * a, c0.Y + uy * a);
                return p0.IsFinite ? 1 : 0;
            }
            double h = Math.Sqrt(h2);
            double bx = c0.X + ux * along, by = c0.Y + uy * along;
            p0 = new Vec2(bx - uy * h, by + ux * h);
            p1 = new Vec2(bx + uy * h, by - ux * h);
            return p0.IsFinite && p1.IsFinite ? 2 : 0;
        }

        /// <summary>
        /// Infinite line through <paramref name="origin"/> along <paramref name="dir"/> (any non-zero length) against the
        /// circle outline. 2 = secant (p0 first along dir), 1 = tangent, 0 = miss.
        /// </summary>
        public static int LineCircle(Vec2 origin, Vec2 dir, Vec2 center, double radius, out Vec2 p0, out Vec2 p1)
        {
            p0 = p1 = Vec2.Zero;
            double len = dir.Length;
            if (!(len > 0) || double.IsInfinity(len)) return 0;
            return LineCircleCore(origin, dir, len, center, radius, out p0, out _, out p1, out _);
        }

        /// <summary>
        /// Infinite line through <paramref name="origin"/> along <paramref name="dir"/> against segment b0-b1. End
        /// touches count and return the end exactly. Parallel or collinear gives 0. The point is computed from the line
        /// (origin + dir * t), so an axis-aligned line keeps its fixed coordinate exactly.
        /// </summary>
        public static int LineSegment(Vec2 origin, Vec2 dir, Vec2 b0, Vec2 b1, out Vec2 p)
        {
            p = Vec2.Zero;
            Vec2 s = b1 - b0;
            double lenD = dir.Length, lenS = s.Length;
            if (!(lenD > 0) || !(lenS > 0) || double.IsInfinity(lenD) || double.IsInfinity(lenS)) return 0;
            double denom = Cross(dir, s);
            if (!(Math.Abs(denom) > ParallelSinEps * lenD * lenS)) return 0;

            Vec2 w = b0 - origin;
            double t = Cross(w, s) / denom;
            double u = Cross(w, dir) / denom;
            double slackU = ParamEps / lenS;
            if (u < -slackU || u > 1 + slackU) return 0;

            if (Math.Abs(u) <= slackU) p = b0;
            else if (Math.Abs(u - 1) <= slackU) p = b1;
            else p = new Vec2(origin.X + dir.X * t, origin.Y + dir.Y * t);
            return p.IsFinite ? 1 : 0;
        }

        static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>
        /// Line origin + dir (|dir| = len) vs circle. Points are foot +- unit * h, where foot is the closest point to the
        /// centre, so an axis-aligned line through an axis-aligned centre gives exact values. s0, s1 are the distances
        /// along the line from origin (board units), used by the segment variant for its range test.
        /// </summary>
        static int LineCircleCore(Vec2 origin, Vec2 dir, double len, Vec2 center, double radius,
            out Vec2 q0, out double s0, out Vec2 q1, out double s1)
        {
            q0 = q1 = Vec2.Zero;
            s0 = s1 = 0;
            if (!(radius > 0) || double.IsInfinity(radius) || !center.IsFinite || !origin.IsFinite) return 0;
            double ux = dir.X / len, uy = dir.Y / len;
            double sc = (center.X - origin.X) * ux + (center.Y - origin.Y) * uy;
            double fx = origin.X + ux * sc, fy = origin.Y + uy * sc;
            double dx = fx - center.X, dy = fy - center.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (!(dist <= radius + ParamEps)) return 0;
            if (dist >= radius - ParamEps)
            {
                q0 = new Vec2(fx, fy);
                s0 = sc;
                return q0.IsFinite ? 1 : 0;
            }
            double h = Math.Sqrt(radius * radius - dist * dist);
            q0 = new Vec2(fx - ux * h, fy - uy * h);
            q1 = new Vec2(fx + ux * h, fy + uy * h);
            s0 = sc - h;
            s1 = sc + h;
            return q0.IsFinite && q1.IsFinite ? 2 : 0;
        }

        /// <summary>Writes q into the next free slot if it lies on segment a-b (snapping to an end when within slack).</summary>
        static int Keep(Vec2 a, Vec2 b, double len, Vec2 q, double s, int count, ref Vec2 p0, ref Vec2 p1)
        {
            if (s < -ParamEps || s > len + ParamEps) return count;
            if (Math.Abs(s) <= ParamEps) q = a;
            else if (Math.Abs(s - len) <= ParamEps) q = b;
            if (count == 0) p0 = q; else p1 = q;
            return count + 1;
        }
    }
}
