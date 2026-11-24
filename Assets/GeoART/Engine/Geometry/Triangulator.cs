using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// Triangulates a fill region (pure C#): an outer loop plus hole loops as produced by a half-edge face walk, which
    /// are only weakly simple. Preprocessing makes them simple first:
    ///   spurs     a dangling edge walked out and back (A, B, A) is removed, repeatedly, so whole trees disappear
    ///   pinches   a vertex visited twice splits the loop into separate loops (CCW ones become outers, CW ones holes)
    ///   cleanup   consecutive duplicates (within 1e-12) and straight-through vertices are dropped
    /// Holes are joined to their outer loop by a bridge from the hole's rightmost vertex to a visible outer vertex
    /// (ray cast to +x, then the reflex vertex inside the candidate triangle with the smallest angle), and the result is
    /// ear-clipped. If no ear can be found (numerically degenerate input) the flattest vertex is clipped, so it always
    /// terminates. O(n^2): fine for regions of up to a few thousand vertices, rebuilt only when the design changes.
    /// </summary>
    public static class Triangulator
    {
        const double Eps = 1e-12;

        /// <summary>
        /// Appends the triangles (3 points each, CCW) of the region bounded by <paramref name="loops"/>: loops[0] the
        /// outer boundary, the rest holes (any orientation and pinches are handled).
        /// </summary>
        public static void Triangulate(List<List<Vec2>> loops, List<Vec2> triangles)
        {
            var outers = new List<List<Vec2>>();
            var holes = new List<List<Vec2>>();
            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 3) continue;
                foreach (var simple in Simplify(loop))
                {
                    double a = Area(simple);
                    if (a > Eps) outers.Add(simple);
                    else if (a < -Eps) holes.Add(simple);
                }
            }
            if (outers.Count == 0) return;
            // Each hole goes to the smallest outer containing the midpoint of its first edge.
            var holesOf = new List<List<Vec2>>[outers.Count];
            foreach (var h in holes)
            {
                Vec2 probe = (h[0] + h[1]) * 0.5;
                int best = -1;
                double bestArea = double.MaxValue;
                for (int i = 0; i < outers.Count; i++)
                {
                    double a = Area(outers[i]);
                    if (a < bestArea && Contains(outers[i], probe)) { best = i; bestArea = a; }
                }
                if (best < 0) continue;
                (holesOf[best] ??= new List<List<Vec2>>()).Add(h);
            }
            for (int i = 0; i < outers.Count; i++)
            {
                var poly = new List<Vec2>(outers[i]);
                if (holesOf[i] != null)
                {
                    holesOf[i].Sort((p, q) => MaxX(q).CompareTo(MaxX(p)));
                    foreach (var h in holesOf[i]) Bridge(poly, h);
                }
                EarClip(poly, triangles);
            }
        }

        public static double Area(List<Vec2> p)
        {
            double a = 0;
            for (int i = 0, n = p.Count; i < n; i++)
            {
                Vec2 u = p[i], v = p[(i + 1) % n];
                a += u.X * v.Y - v.X * u.Y;
            }
            return a * 0.5;
        }

        /// <summary>Even-odd ray cast; points exactly on the boundary may go either way.</summary>
        public static bool Contains(List<Vec2> poly, Vec2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vec2 a = poly[i], b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    double x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    if (p.X < x) inside = !inside;
                }
            }
            return inside;
        }

        static double MaxX(List<Vec2> p)
        {
            double m = double.MinValue;
            foreach (var v in p) m = Math.Max(m, v.X);
            return m;
        }

        static double Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        static bool Same(Vec2 a, Vec2 b) => Math.Abs(a.X - b.X) <= Eps && Math.Abs(a.Y - b.Y) <= Eps;

        // ---------- preprocessing ----------

        /// <summary>Removes spurs and duplicates, then splits at repeated vertices into simple loops.</summary>
        static List<List<Vec2>> Simplify(List<Vec2> input)
        {
            var p = new List<Vec2>(input.Count);
            foreach (var v in input)
                if (p.Count == 0 || !Same(p[p.Count - 1], v)) p.Add(v);
            while (p.Count > 1 && Same(p[0], p[p.Count - 1])) p.RemoveAt(p.Count - 1);

            // Spurs: ... A, B, A ... -> ... A ...  (repeat until none, wrapping around)
            bool changed = true;
            while (changed && p.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < p.Count && p.Count >= 3; i++)
                {
                    int n = p.Count, prev = (i - 1 + n) % n, next = (i + 1) % n;
                    if (Same(p[prev], p[next]))
                    {
                        // remove the tip (i) and the return (next)
                        int hi = Math.Max(i, next), lo = Math.Min(i, next);
                        p.RemoveAt(hi);
                        p.RemoveAt(lo);
                        changed = true;
                        i = Math.Max(-1, lo - 2);
                    }
                }
            }

            var result = new List<List<Vec2>>();
            SplitPinches(p, result);
            for (int k = result.Count - 1; k >= 0; k--)
            {
                DropStraight(result[k]);
                if (result[k].Count < 3) result.RemoveAt(k);
            }
            return result;
        }

        static void SplitPinches(List<Vec2> p, List<List<Vec2>> into)
        {
            for (int i = 0; i < p.Count; i++)
                for (int j = i + 1; j < p.Count; j++)
                {
                    if (!Same(p[i], p[j])) continue;
                    var inner = p.GetRange(i, j - i);                 // p[i] .. p[j-1]
                    var outer = new List<Vec2>(p.Count - (j - i));
                    outer.AddRange(p.GetRange(0, i));
                    outer.AddRange(p.GetRange(j, p.Count - j));       // p[j] (== p[i]) .. end
                    SplitPinches(inner, into);
                    SplitPinches(outer, into);
                    return;
                }
            if (p.Count >= 3) into.Add(p);
        }

        static void DropStraight(List<Vec2> p)
        {
            bool changed = true;
            while (changed && p.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < p.Count && p.Count >= 3; i++)
                {
                    int n = p.Count;
                    Vec2 a = p[(i - 1 + n) % n], b = p[i], c = p[(i + 1) % n];
                    if (Same(a, b) || Same(b, c))
                    {
                        p.RemoveAt(i);
                        changed = true;
                        i--;
                        continue;
                    }
                    double cross = Cross(a, b, c);
                    double scale = Vec2.Distance(a, b) * Vec2.Distance(b, c);
                    double dot = (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y);
                    if (Math.Abs(cross) <= 1e-14 * scale && dot > 0)
                    {
                        p.RemoveAt(i);
                        changed = true;
                        i--;
                    }
                }
            }
        }

        // ---------- holes ----------

        /// <summary>Joins hole <paramref name="hole"/> (made CW) into <paramref name="poly"/> (CCW) with a two-way bridge.</summary>
        static void Bridge(List<Vec2> poly, List<Vec2> hole)
        {
            if (Area(hole) > 0) hole.Reverse();
            int hm = 0;
            for (int i = 1; i < hole.Count; i++)
                if (hole[i].X > hole[hm].X || (hole[i].X == hole[hm].X && hole[i].Y < hole[hm].Y)) hm = i;
            Vec2 m = hole[hm];

            // Nearest outer edge hit by the ray from m to +x.
            int edge = -1;
            double bestX = double.MaxValue;
            Vec2 hit = m;
            for (int i = 0, n = poly.Count; i < n; i++)
            {
                Vec2 a = poly[i], b = poly[(i + 1) % n];
                if ((a.Y > m.Y) == (b.Y > m.Y) && !(a.Y == m.Y && b.Y == m.Y)) continue;
                if (a.Y == b.Y) continue;
                double x = a.X + (m.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (x < m.X - Eps || x >= bestX) continue;
                bestX = x;
                edge = i;
                hit = new Vec2(x, m.Y);
            }
            int pi;
            if (edge < 0) pi = NearestVertex(poly, m);
            else
            {
                int n = poly.Count;
                pi = poly[edge].X > poly[(edge + 1) % n].X ? edge : (edge + 1) % n;
                // A reflex vertex inside triangle (m, hit, p) would block the bridge: take the one closest in angle.
                Vec2 pv = poly[pi];
                double bestAngle = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (i == pi) continue;
                    Vec2 v = poly[i];
                    if (!Reflex(poly, i) || !InTriangle(m, hit, pv, v)) continue;
                    double ang = Math.Abs(Math.Atan2(v.Y - m.Y, v.X - m.X));
                    if (ang < bestAngle) { bestAngle = ang; pi = i; }
                }
            }
            var spliced = new List<Vec2>(poly.Count + hole.Count + 2);
            for (int i = 0; i <= pi; i++) spliced.Add(poly[i]);
            for (int k = 0; k <= hole.Count; k++) spliced.Add(hole[(hm + k) % hole.Count]);
            for (int i = pi; i < poly.Count; i++) spliced.Add(poly[i]);
            poly.Clear();
            poly.AddRange(spliced);
        }

        static int NearestVertex(List<Vec2> poly, Vec2 m)
        {
            int best = 0;
            double d = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                double e = Vec2.Distance(poly[i], m);
                if (e < d) { d = e; best = i; }
            }
            return best;
        }

        static bool Reflex(List<Vec2> p, int i)
        {
            int n = p.Count;
            return Cross(p[(i - 1 + n) % n], p[i], p[(i + 1) % n]) < 0;
        }

        static bool InTriangle(Vec2 a, Vec2 b, Vec2 c, Vec2 p)
        {
            double d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ---------- ear clipping ----------

        static void EarClip(List<Vec2> poly, List<Vec2> triangles)
        {
            var idx = new List<int>(poly.Count);
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            int guard = poly.Count * poly.Count + 10;
            while (idx.Count > 3 && guard-- > 0)
            {
                int n = idx.Count, ear = -1;
                for (int k = 0; k < n && ear < 0; k++)
                {
                    Vec2 a = poly[idx[(k - 1 + n) % n]], b = poly[idx[k]], c = poly[idx[(k + 1) % n]];
                    if (Cross(a, b, c) <= Eps * Eps) continue;   // reflex or flat
                    bool blocked = false;
                    for (int j = 0; j < n && !blocked; j++)
                    {
                        if (j == k || j == (k - 1 + n) % n || j == (k + 1) % n) continue;
                        Vec2 q = poly[idx[j]];
                        if (Same(q, a) || Same(q, b) || Same(q, c)) continue;   // bridge duplicates
                        if (InsideOrOn(a, b, c, q)) blocked = true;   // a vertex on the new diagonal blocks too
                    }
                    if (!blocked) ear = k;
                }
                if (ear < 0)
                {
                    // Degenerate: clip the flattest vertex so it always terminates.
                    double flattest = double.MaxValue;
                    for (int k = 0; k < n; k++)
                    {
                        double cr = Math.Abs(Cross(poly[idx[(k - 1 + n) % n]], poly[idx[k]], poly[idx[(k + 1) % n]]));
                        if (cr < flattest) { flattest = cr; ear = k; }
                    }
                    idx.RemoveAt(ear);
                    continue;
                }
                Vec2 ea = poly[idx[(ear - 1 + n) % n]], eb = poly[idx[ear]], ec = poly[idx[(ear + 1) % n]];
                triangles.Add(ea);
                triangles.Add(eb);
                triangles.Add(ec);
                idx.RemoveAt(ear);
            }
            if (idx.Count == 3 && Cross(poly[idx[0]], poly[idx[1]], poly[idx[2]]) > Eps * Eps)
            {
                triangles.Add(poly[idx[0]]);
                triangles.Add(poly[idx[1]]);
                triangles.Add(poly[idx[2]]);
            }
        }

        static bool InsideOrOn(Vec2 a, Vec2 b, Vec2 c, Vec2 p) =>
            Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0;
    }
}
