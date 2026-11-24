using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>An input segment for <see cref="PlanarGraph.Build"/>; <see cref="Source"/> is the caller's index (&gt;= 0).</summary>
    public struct PlanarSegment
    {
        public Vec2 A, B;
        public int Source;

        public PlanarSegment(Vec2 a, Vec2 b, int source) { A = a; B = b; Source = source; }
    }

    /// <summary>
    /// Planar arrangement of line segments in board units (pure C#): welded, merged, split at every crossing, with a
    /// half-edge structure and its face cycles. Used by the bands (BandGraph) and later by Fill. Built once per topology
    /// change, so it allocates freely; nothing here runs on the pointer path.
    ///
    /// Steps (docs/BANDS.md):
    ///  1. Drop non-finite and shorter than <see cref="MinLength"/> segments. With a frame: clip to [0,1]^2 (the clipped
    ///     coordinate is written as exactly 0 or 1) and snap every endpoint coordinate within <see cref="WeldEps"/> of a
    ///     side onto it, so lines that end at the edge end exactly on the frame.
    ///  2. Endpoint weld: endpoints within <see cref="WeldEps"/> become one point (a frame point wins, then the
    ///     lexicographically smallest). Near-junction weld: an endpoint within <see cref="WeldEps"/> of another segment's
    ///     interior moves onto it (repeated until nothing moves, at most 4 passes).
    ///  3. Collinear merge: collinear segments (within <see cref="CollinearEps"/>) that overlap or touch become one
    ///     segment between the extreme original endpoints (bit-exact); it keeps the largest source index.
    ///  4. Crossings: every pair (uniform-grid prefilter) through <see cref="Intersect.SegmentSegment"/>, which returns
    ///     segment ends bit-exactly. Points within <see cref="NodeEps"/> become one node (a frame point wins, then the
    ///     lexicographically smallest). Segments are split at their nodes into edges.
    ///  5. Frame: the four sides become edges with <see cref="FrameSource"/>, cut at every node on them (a side piece
    ///     already covered by a segment is not duplicated).
    ///  6. Half-edges: edge e is half-edges 2e (A to B) and 2e+1 (B to A). Outgoing half-edges are sorted CCW at each
    ///     node; Next(h) is the outgoing half-edge at h's head just clockwise of Twin(h), so every face cycle has its face
    ///     on the left (bounded faces CCW, positive area; the outside of a component CW, negative area).
    /// Deterministic: the input is put into a canonical order first and every choice uses coordinates, never input order,
    /// so the same segments in any order or direction give an identical graph.
    /// </summary>
    public sealed class PlanarGraph
    {
        public const double WeldEps = 1e-5;
        public const double NodeEps = 1e-7;
        public const double CollinearEps = 1e-7;
        public const double MinLength = 1e-9;
        public const double FrameEps = 1e-9;
        /// <summary>Source of the frame's edges.</summary>
        public const int FrameSource = -1;

        Vec2[] _nodes;
        bool[] _nodeOnFrame;
        int[] _edgeA, _edgeB, _edgeSource;
        int[] _outStart, _outList, _ringIndex, _next, _prev, _faceOf, _faceStart, _faceList;
        double[] _angle, _length, _faceArea;
        Vec2[] _dir;

        public int NodeCount => _nodes.Length;
        public int EdgeCount => _edgeA.Length;
        public int HalfEdgeCount => 2 * _edgeA.Length;
        public int FaceCount => _faceArea.Length;
        public bool HasFrame { get; private set; }

        public Vec2 Node(int n) => _nodes[n];
        /// <summary>True when the node lies on a side of the board frame (only with a frame).</summary>
        public bool NodeOnFrame(int n) => _nodeOnFrame[n];
        public int Degree(int n) => _outStart[n + 1] - _outStart[n];
        /// <summary>The k-th outgoing half-edge of node n in CCW order (k in [0, Degree)).</summary>
        public int Out(int n, int k) => _outList[_outStart[n] + k];
        /// <summary>Position of half-edge h in its origin's CCW ring.</summary>
        public int RingIndex(int h) => _ringIndex[h];

        public static int Twin(int h) => h ^ 1;
        public static int EdgeOf(int h) => h >> 1;
        public int Origin(int h) => (h & 1) == 0 ? _edgeA[h >> 1] : _edgeB[h >> 1];
        public int Dest(int h) => (h & 1) == 0 ? _edgeB[h >> 1] : _edgeA[h >> 1];
        public int Next(int h) => _next[h];
        public int Prev(int h) => _prev[h];
        /// <summary>Unit direction from origin to head.</summary>
        public Vec2 Dir(int h) => _dir[h];
        public double Angle(int h) => _angle[h];
        public double Length(int h) => _length[h >> 1];
        public int Source(int h) => _edgeSource[h >> 1];
        public bool IsFrame(int h) => _edgeSource[h >> 1] == FrameSource;

        public int FaceOf(int h) => _faceOf[h];
        public int FaceSize(int f) => _faceStart[f + 1] - _faceStart[f];
        /// <summary>The k-th half-edge of face cycle f, in Next order.</summary>
        public int FaceHalfEdge(int f, int k) => _faceList[_faceStart[f] + k];
        /// <summary>Signed area of face cycle f (CCW positive).</summary>
        public double FaceArea(int f) => _faceArea[f];

        struct Seg
        {
            public Vec2 A, B;
            public int Src;
        }

        // ---------- build ----------

        public static PlanarGraph Build(IReadOnlyList<PlanarSegment> input, bool addFrame)
        {
            var segs = new List<Seg>(input?.Count ?? 0);
            if (input != null)
                for (int i = 0; i < input.Count; i++)
                {
                    PlanarSegment s = input[i];
                    if (!s.A.IsFinite || !s.B.IsFinite) continue;
                    Vec2 a = s.A, b = s.B;
                    if (addFrame && !ClipToFrame(ref a, ref b)) continue;
                    if (addFrame) { a = SnapToFrame(a); b = SnapToFrame(b); }
                    if (Vec2.Distance(a, b) < MinLength) continue;
                    segs.Add(new Seg { A = a, B = b, Src = s.Source });
                }
            Canonicalize(segs);
            WeldEndpoints(segs);
            WeldNearJunctions(segs);
            MergeCollinear(segs);

            var g = new PlanarGraph { HasFrame = addFrame };
            g.Planarize(segs, addFrame);
            g.BuildHalfEdges();
            g.BuildFaces();
            return g;
        }

        /// <summary>Liang-Barsky clip to [0,1]^2; a clipped end gets the side's coordinate exactly. False when outside.</summary>
        static bool ClipToFrame(ref Vec2 a, ref Vec2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double t0 = 0, t1 = 1;
            int side0 = -1, side1 = -1;   // 0: x=0, 1: x=1, 2: y=0, 3: y=1
            if (!Clip(-dx, a.X, 0, ref t0, ref t1, ref side0, ref side1)) return false;
            if (!Clip(dx, 1 - a.X, 1, ref t0, ref t1, ref side0, ref side1)) return false;
            if (!Clip(-dy, a.Y, 2, ref t0, ref t1, ref side0, ref side1)) return false;
            if (!Clip(dy, 1 - a.Y, 3, ref t0, ref t1, ref side0, ref side1)) return false;
            Vec2 a0 = a;
            if (side0 >= 0) a = OnSide(new Vec2(a0.X + dx * t0, a0.Y + dy * t0), side0);
            if (side1 >= 0) b = OnSide(new Vec2(a0.X + dx * t1, a0.Y + dy * t1), side1);
            return true;
        }

        static bool Clip(double p, double q, int side, ref double t0, ref double t1, ref int side0, ref int side1)
        {
            if (p == 0) return q >= 0;
            double r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) { t0 = r; side0 = side; }
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) { t1 = r; side1 = side; }
            }
            return true;
        }

        static Vec2 OnSide(Vec2 p, int side)
        {
            double x = Math.Max(0, Math.Min(1, p.X)), y = Math.Max(0, Math.Min(1, p.Y));
            switch (side)
            {
                case 0: return new Vec2(0, y);
                case 1: return new Vec2(1, y);
                case 2: return new Vec2(x, 0);
                default: return new Vec2(x, 1);
            }
        }

        static Vec2 SnapToFrame(Vec2 p)
        {
            double x = p.X, y = p.Y;
            if (Math.Abs(x) <= WeldEps) x = 0; else if (Math.Abs(x - 1) <= WeldEps) x = 1;
            if (Math.Abs(y) <= WeldEps) y = 0; else if (Math.Abs(y - 1) <= WeldEps) y = 1;
            return new Vec2(x, y);
        }

        static bool IsOnFrame(Vec2 p) =>
            Math.Abs(p.X) <= FrameEps || Math.Abs(p.X - 1) <= FrameEps || Math.Abs(p.Y) <= FrameEps || Math.Abs(p.Y - 1) <= FrameEps;

        static int FrameScore(Vec2 p) =>
            (p.X == 0 || p.X == 1 ? 1 : 0) + (p.Y == 0 || p.Y == 1 ? 1 : 0);

        static int Lex(Vec2 a, Vec2 b)
        {
            int c = a.X.CompareTo(b.X);
            return c != 0 ? c : a.Y.CompareTo(b.Y);
        }

        /// <summary>True when <paramref name="a"/> is the better representative: on more frame sides, then smaller.</summary>
        static bool Better(Vec2 a, Vec2 b)
        {
            int fa = FrameScore(a), fb = FrameScore(b);
            return fa != fb ? fa > fb : Lex(a, b) < 0;
        }

        /// <summary>Orients each segment A &lt; B (lexicographic), drops degenerate ones and sorts by (A, B, source).</summary>
        static void Canonicalize(List<Seg> segs)
        {
            for (int i = segs.Count - 1; i >= 0; i--)
            {
                Seg s = segs[i];
                if (Vec2.Distance(s.A, s.B) < MinLength) { segs.RemoveAt(i); continue; }
                if (Lex(s.B, s.A) < 0) { var t = s.A; s.A = s.B; s.B = t; segs[i] = s; }
            }
            segs.Sort((p, q) =>
            {
                int c = Lex(p.A, q.A);
                if (c != 0) return c;
                c = Lex(p.B, q.B);
                return c != 0 ? c : p.Src.CompareTo(q.Src);
            });
        }

        // ---------- welding ----------

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a);
            b = Find(parent, b);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }

        /// <summary>Clusters points within <paramref name="eps"/> (x-sorted sweep) and replaces each by its cluster's best point.</summary>
        static void ClusterPoints(Vec2[] pts, double eps)
        {
            int n = pts.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (p, q) => { int c = Lex(pts[p], pts[q]); return c != 0 ? c : p.CompareTo(q); });
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int i = 0; i < n; i++)
            {
                Vec2 p = pts[order[i]];
                for (int j = i + 1; j < n; j++)
                {
                    Vec2 q = pts[order[j]];
                    if (q.X - p.X > eps) break;
                    if (Vec2.Distance(p, q) <= eps) Union(parent, order[i], order[j]);
                }
            }
            var best = new Vec2[n];
            var has = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                if (!has[r] || Better(pts[i], best[r])) { best[r] = pts[i]; has[r] = true; }
            }
            for (int i = 0; i < n; i++) pts[i] = best[Find(parent, i)];
        }

        static void WeldEndpoints(List<Seg> segs)
        {
            var pts = new Vec2[segs.Count * 2];
            for (int i = 0; i < segs.Count; i++) { pts[2 * i] = segs[i].A; pts[2 * i + 1] = segs[i].B; }
            ClusterPoints(pts, WeldEps);
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                s.A = pts[2 * i];
                s.B = pts[2 * i + 1];
                segs[i] = s;
            }
            Canonicalize(segs);
        }

        /// <summary>
        /// Moves endpoints lying within WeldEps of another segment's interior onto it (nearest segment wins; frame points
        /// never move). Repeats against the updated geometry until nothing moves (at most 4 passes).
        /// </summary>
        static void WeldNearJunctions(List<Seg> segs)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                var grid = new SegmentGrid(segs, WeldEps);
                var moves = new Dictionary<Vec2, Vec2>();
                var cand = new List<int>();
                for (int i = 0; i < segs.Count; i++)
                    for (int e = 0; e < 2; e++)
                    {
                        Vec2 p = e == 0 ? segs[i].A : segs[i].B;
                        if (FrameScore(p) > 0 || moves.ContainsKey(p)) continue;
                        grid.Query(p, WeldEps, cand);
                        double bestD = double.MaxValue;
                        Vec2 bestF = p;
                        for (int k = 0; k < cand.Count; k++)
                        {
                            Seg s = segs[cand[k]];
                            if (s.A == p || s.B == p) continue;
                            if (Vec2.Distance(p, s.A) <= WeldEps || Vec2.Distance(p, s.B) <= WeldEps) continue;
                            Vec2 ab = s.B - s.A;
                            double len2 = ab.X * ab.X + ab.Y * ab.Y;
                            double t = ((p.X - s.A.X) * ab.X + (p.Y - s.A.Y) * ab.Y) / len2;
                            if (!(t > 0 && t < 1)) continue;
                            var f = new Vec2(s.A.X + ab.X * t, s.A.Y + ab.Y * t);
                            double d = Vec2.Distance(p, f);
                            if (d <= 1e-12 || d > WeldEps || d >= bestD) continue;
                            bestD = d;
                            bestF = f;
                        }
                        if (bestD < double.MaxValue) moves[p] = bestF;
                    }
                if (moves.Count == 0) return;
                for (int i = 0; i < segs.Count; i++)
                {
                    Seg s = segs[i];
                    if (moves.TryGetValue(s.A, out Vec2 a)) s.A = a;
                    if (moves.TryGetValue(s.B, out Vec2 b)) s.B = b;
                    segs[i] = s;
                }
                Canonicalize(segs);
            }
        }

        // ---------- collinear merge ----------

        static double DistToLine(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            double len = ab.Length;
            return Math.Abs(ab.X * (p.Y - a.Y) - ab.Y * (p.X - a.X)) / len;
        }

        static void MergeCollinear(List<Seg> segs)
        {
            int n = segs.Count;
            if (n < 2) return;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            var grid = new SegmentGrid(segs, CollinearEps);
            bool any = false;
            grid.ForEachPair(segs, (i, j) =>
            {
                Seg s = segs[i], t = segs[j];
                if (DistToLine(t.A, s.A, s.B) > CollinearEps || DistToLine(t.B, s.A, s.B) > CollinearEps) return;
                if (DistToLine(s.A, t.A, t.B) > CollinearEps || DistToLine(s.B, t.A, t.B) > CollinearEps) return;
                Vec2 u = s.B - s.A;
                double len = u.Length;
                double pa = ((t.A.X - s.A.X) * u.X + (t.A.Y - s.A.Y) * u.Y) / len;
                double pb = ((t.B.X - s.A.X) * u.X + (t.B.Y - s.A.Y) * u.Y) / len;
                if (Math.Max(pa, pb) < -CollinearEps || Math.Min(pa, pb) > len + CollinearEps) return;   // apart
                Union(parent, i, j);
                any = true;
            });
            if (!any) return;

            var merged = new List<Seg>(n);
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<int>();
                list.Add(i);
            }
            for (int i = 0; i < n; i++)
            {
                if (Find(parent, i) != i) continue;
                var list = groups[i];
                if (list.Count == 1) { merged.Add(segs[i]); continue; }
                Seg axis = segs[list[0]];   // the canonical first member defines the axis
                Vec2 u = axis.B - axis.A;
                Vec2 lo = axis.A, hi = axis.B;
                double tLo = double.MaxValue, tHi = double.MinValue;
                int src = int.MinValue;
                foreach (int k in list)
                {
                    Seg s = segs[k];
                    src = Math.Max(src, s.Src);
                    for (int e = 0; e < 2; e++)
                    {
                        Vec2 p = e == 0 ? s.A : s.B;
                        double t = (p.X - axis.A.X) * u.X + (p.Y - axis.A.Y) * u.Y;
                        if (t < tLo || (t == tLo && Lex(p, lo) < 0)) { tLo = t; lo = p; }
                        if (t > tHi || (t == tHi && Lex(p, hi) < 0)) { tHi = t; hi = p; }
                    }
                }
                merged.Add(new Seg { A = lo, B = hi, Src = src });
            }
            segs.Clear();
            segs.AddRange(merged);
            Canonicalize(segs);
        }

        // ---------- crossings, nodes, edges ----------

        void Planarize(List<Seg> segs, bool addFrame)
        {
            int n = segs.Count;
            // Points on each segment: its ends, then every crossing.
            var ptsOf = new List<Vec2>[n];
            for (int i = 0; i < n; i++) ptsOf[i] = new List<Vec2> { segs[i].A, segs[i].B };
            var grid = new SegmentGrid(segs, Intersect.ParamEps);
            grid.ForEachPair(segs, (i, j) =>
            {
                if (Intersect.SegmentSegment(segs[i].A, segs[i].B, segs[j].A, segs[j].B, out Vec2 p) == 1)
                {
                    ptsOf[i].Add(p);
                    ptsOf[j].Add(p);
                }
            });

            // Node dedupe over every point (and the frame corners).
            var all = new List<Vec2>();
            for (int i = 0; i < n; i++) all.AddRange(ptsOf[i]);
            if (addFrame)
            {
                all.Add(new Vec2(0, 0)); all.Add(new Vec2(1, 0)); all.Add(new Vec2(1, 1)); all.Add(new Vec2(0, 1));
            }
            var clustered = all.ToArray();
            ClusterPoints(clustered, NodeEps);
            var distinct = new List<Vec2>(clustered.Length);
            var seen = new HashSet<Vec2>();
            foreach (var p in clustered)
                if (seen.Add(p)) distinct.Add(p);
            distinct.Sort(Lex);
            _nodes = distinct.ToArray();
            var nodeId = new Dictionary<Vec2, int>(_nodes.Length);
            for (int i = 0; i < _nodes.Length; i++) nodeId[_nodes[i]] = i;
            _nodeOnFrame = new bool[_nodes.Length];
            if (addFrame)
                for (int i = 0; i < _nodes.Length; i++) _nodeOnFrame[i] = IsOnFrame(_nodes[i]);

            // Split each segment at its nodes (sorted along it) into edges; one edge per node pair.
            var edgeA = new List<int>();
            var edgeB = new List<int>();
            var edgeSrc = new List<int>();
            var pairIndex = new Dictionary<long, int>();
            int k0 = 0;
            var order = new List<KeyValuePair<double, int>>();
            for (int i = 0; i < n; i++)
            {
                Seg s = segs[i];
                Vec2 u = s.B - s.A;
                order.Clear();
                foreach (var p in ptsOf[i])
                {
                    int id = nodeId[clustered[k0++]];
                    double t = (p.X - s.A.X) * u.X + (p.Y - s.A.Y) * u.Y;
                    order.Add(new KeyValuePair<double, int>(t, id));
                }
                order.Sort((x, y) => { int c = x.Key.CompareTo(y.Key); return c != 0 ? c : x.Value.CompareTo(y.Value); });
                for (int k = 1; k < order.Count; k++)
                    AddEdge(order[k - 1].Value, order[k].Value, s.Src, edgeA, edgeB, edgeSrc, pairIndex);
            }

            if (addFrame)
            {
                var side = new List<int>();
                for (int sideIdx = 0; sideIdx < 4; sideIdx++)
                {
                    side.Clear();
                    for (int i = 0; i < _nodes.Length; i++)
                    {
                        Vec2 p = _nodes[i];
                        bool on = sideIdx == 0 ? Math.Abs(p.Y) <= FrameEps
                                : sideIdx == 1 ? Math.Abs(p.X - 1) <= FrameEps
                                : sideIdx == 2 ? Math.Abs(p.Y - 1) <= FrameEps
                                : Math.Abs(p.X) <= FrameEps;
                        if (on) side.Add(i);
                    }
                    bool horizontal = sideIdx == 0 || sideIdx == 2;
                    side.Sort((a, b) => horizontal ? _nodes[a].X.CompareTo(_nodes[b].X) : _nodes[a].Y.CompareTo(_nodes[b].Y));
                    for (int k = 1; k < side.Count; k++)
                    {
                        long key = PairKey(side[k - 1], side[k]);
                        if (!pairIndex.ContainsKey(key)) AddEdge(side[k - 1], side[k], FrameSource, edgeA, edgeB, edgeSrc, pairIndex);
                    }
                }
            }
            _edgeA = edgeA.ToArray();
            _edgeB = edgeB.ToArray();
            _edgeSource = edgeSrc.ToArray();
        }

        static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static void AddEdge(int a, int b, int src, List<int> edgeA, List<int> edgeB, List<int> edgeSrc,
            Dictionary<long, int> pairIndex)
        {
            if (a == b) return;
            long key = PairKey(a, b);
            if (pairIndex.TryGetValue(key, out int e))
            {
                if (src > edgeSrc[e]) edgeSrc[e] = src;
                return;
            }
            pairIndex[key] = edgeA.Count;
            edgeA.Add(Math.Min(a, b));
            edgeB.Add(Math.Max(a, b));
            edgeSrc.Add(src);
        }

        // ---------- half-edges and faces ----------

        void BuildHalfEdges()
        {
            int nh = HalfEdgeCount, nn = _nodes.Length;
            _dir = new Vec2[nh];
            _angle = new double[nh];
            _length = new double[EdgeCount];
            var count = new int[nn + 1];
            for (int h = 0; h < nh; h++)
            {
                Vec2 d = _nodes[Dest(h)] - _nodes[Origin(h)];
                double len = d.Length;
                if ((h & 1) == 0) _length[h >> 1] = len;
                _dir[h] = new Vec2(d.X / len, d.Y / len);
                _angle[h] = Math.Atan2(d.Y, d.X);
                count[Origin(h) + 1]++;
            }
            _outStart = new int[nn + 1];
            for (int i = 0; i < nn; i++) _outStart[i + 1] = _outStart[i] + count[i + 1];
            _outList = new int[nh];
            var fill = new int[nn];
            for (int h = 0; h < nh; h++)
            {
                int o = Origin(h);
                _outList[_outStart[o] + fill[o]++] = h;
            }
            _ringIndex = new int[nh];
            for (int v = 0; v < nn; v++)
            {
                Array.Sort(_outList, _outStart[v], _outStart[v + 1] - _outStart[v],
                    Comparer<int>.Create((a, b) => { int c = _angle[a].CompareTo(_angle[b]); return c != 0 ? c : a.CompareTo(b); }));
                for (int k = _outStart[v]; k < _outStart[v + 1]; k++) _ringIndex[_outList[k]] = k - _outStart[v];
            }
            _next = new int[nh];
            _prev = new int[nh];
            for (int h = 0; h < nh; h++)
            {
                int t = Twin(h), v = Origin(t), deg = Degree(v);
                int nx = _outList[_outStart[v] + (_ringIndex[t] - 1 + deg) % deg];
                _next[h] = nx;
                _prev[nx] = h;
            }
        }

        void BuildFaces()
        {
            int nh = HalfEdgeCount;
            _faceOf = new int[nh];
            for (int h = 0; h < nh; h++) _faceOf[h] = -1;
            var start = new List<int> { 0 };
            var list = new List<int>(nh);
            var areas = new List<double>();
            for (int h0 = 0; h0 < nh; h0++)
            {
                if (_faceOf[h0] >= 0) continue;
                int f = areas.Count;
                double area = 0;
                int h = h0;
                do
                {
                    _faceOf[h] = f;
                    list.Add(h);
                    Vec2 a = _nodes[Origin(h)], b = _nodes[Dest(h)];
                    area += a.X * b.Y - b.X * a.Y;
                    h = _next[h];
                } while (h != h0);
                areas.Add(area * 0.5);
                start.Add(list.Count);
            }
            _faceStart = start.ToArray();
            _faceList = list.ToArray();
            _faceArea = areas.ToArray();
        }

        // ---------- uniform grid prefilter ----------

        /// <summary>Buckets segment bounding boxes (padded) in a uniform grid of about sqrt(n) x sqrt(n) cells.</summary>
        sealed class SegmentGrid
        {
            readonly double _minX, _minY, _cell;
            readonly int _cols, _rows;
            readonly List<int>[] _cells;
            readonly double[] _bx0, _by0, _bx1, _by1;
            int[] _stamp;
            int _stampValue;

            public SegmentGrid(List<Seg> segs, double pad)
            {
                int n = segs.Count;
                _bx0 = new double[n]; _by0 = new double[n]; _bx1 = new double[n]; _by1 = new double[n];
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                for (int i = 0; i < n; i++)
                {
                    Seg s = segs[i];
                    _bx0[i] = Math.Min(s.A.X, s.B.X) - pad; _bx1[i] = Math.Max(s.A.X, s.B.X) + pad;
                    _by0[i] = Math.Min(s.A.Y, s.B.Y) - pad; _by1[i] = Math.Max(s.A.Y, s.B.Y) + pad;
                    minX = Math.Min(minX, _bx0[i]); minY = Math.Min(minY, _by0[i]);
                    maxX = Math.Max(maxX, _bx1[i]); maxY = Math.Max(maxY, _by1[i]);
                }
                if (n == 0) { minX = minY = 0; maxX = maxY = 1; }
                int g = Math.Max(1, Math.Min(64, (int)Math.Ceiling(Math.Sqrt(n))));
                _minX = minX;
                _minY = minY;
                _cell = Math.Max(Math.Max(maxX - minX, maxY - minY) / g, 1e-12);
                _cols = Math.Max(1, Math.Min(g, (int)Math.Ceiling((maxX - minX) / _cell)));
                _rows = Math.Max(1, Math.Min(g, (int)Math.Ceiling((maxY - minY) / _cell)));
                _cells = new List<int>[_cols * _rows];
                for (int i = 0; i < n; i++)
                    for (int cy = CellY(_by0[i]); cy <= CellY(_by1[i]); cy++)
                        for (int cx = CellX(_bx0[i]); cx <= CellX(_bx1[i]); cx++)
                        {
                            ref List<int> c = ref _cells[cy * _cols + cx];
                            if (c == null) c = new List<int>();
                            c.Add(i);
                        }
                _stamp = new int[n];
            }

            int CellX(double x) => Math.Max(0, Math.Min(_cols - 1, (int)Math.Floor((x - _minX) / _cell)));
            int CellY(double y) => Math.Max(0, Math.Min(_rows - 1, (int)Math.Floor((y - _minY) / _cell)));

            /// <summary>Calls <paramref name="visit"/>(i, j), i &lt; j, once for every pair whose padded boxes overlap.</summary>
            public void ForEachPair(List<Seg> segs, Action<int, int> visit)
            {
                for (int c = 0; c < _cells.Length; c++)
                {
                    var list = _cells[c];
                    if (list == null) continue;
                    int cx = c % _cols, cy = c / _cols;
                    for (int a = 0; a < list.Count; a++)
                        for (int b = a + 1; b < list.Count; b++)
                        {
                            int i = list[a], j = list[b];
                            if (_bx0[i] > _bx1[j] || _bx0[j] > _bx1[i] || _by0[i] > _by1[j] || _by0[j] > _by1[i]) continue;
                            // Visit the pair only in the cell holding the lower-left corner of the two boxes' overlap.
                            if (CellX(Math.Max(_bx0[i], _bx0[j])) != cx || CellY(Math.Max(_by0[i], _by0[j])) != cy) continue;
                            visit(i, j);
                        }
                }
            }

            /// <summary>Segments whose padded box overlaps the square p +- r (each once).</summary>
            public void Query(Vec2 p, double r, List<int> result)
            {
                result.Clear();
                if (++_stampValue == int.MaxValue) { Array.Clear(_stamp, 0, _stamp.Length); _stampValue = 1; }
                for (int cy = CellY(p.Y - r); cy <= CellY(p.Y + r); cy++)
                    for (int cx = CellX(p.X - r); cx <= CellX(p.X + r); cx++)
                    {
                        var list = _cells[cy * _cols + cx];
                        if (list == null) continue;
                        foreach (int i in list)
                        {
                            if (_stamp[i] == _stampValue) continue;
                            _stamp[i] = _stampValue;
                            if (_bx0[i] > p.X + r || _bx1[i] < p.X - r || _by0[i] > p.Y + r || _by1[i] < p.Y - r) continue;
                            result.Add(i);
                        }
                    }
            }
        }
    }
}
