using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// Width-independent band topology of one layer (pure C#): the layer's lines and polygon edges (circles are never
    /// banded) planarized inside the board frame (<see cref="PlanarGraph"/>), the per-half-edge band side factor, the
    /// threads and the weave. Rebuilt only when the shapes change; <see cref="BandOutline"/> turns it into outlines for a
    /// given width, which is cheap, so a width drag never re-planarizes.
    ///
    /// Side factor: 1 for a structure half-edge, 0 for the frame and for the outer side of a structure line lying on the
    /// frame (so nothing is offset outside the board along it).
    ///
    /// Threads (one continuous band each) follow structure edges through structure-degree-2 nodes (the band turns, also
    /// on the frame) and through structure-degree-4 nodes, where sorted index i pairs with i + 2 (a crossing), unless two
    /// RING-ADJACENT edges are collinear: that is a line with a tip touching it from one side, so the straight pair
    /// continues, the tip pairs with itself and the node is not a crossing. Threads stop at degree 1, 3, 5+ and at
    /// degree-4 nodes on the frame. A crossing's pass p (0 or 1) is the pair holding sorted index p. Over/under must alternate along every thread: for consecutive visits (c1, p1),
    /// (c2, p2) the over-pass bits satisfy o1 XOR o2 = 1 XOR p1 XOR p2 (closed threads include the wrap-around pair).
    /// Solved by BFS 2-colouring in node order; each component is seeded at its smallest (x, y) crossing with o = 0 (the
    /// pass holding the smallest-angle half-edge goes over). The seed comes from geometry, so Undo, Redo and reload never
    /// flip the weave. A violated constraint keeps the BFS value and is counted in <see cref="WeaveConflicts"/>.
    /// Degree 6/8 nodes get no bit and are drawn flat.
    /// </summary>
    public sealed class BandGraph
    {
        public readonly PlanarGraph Graph;
        readonly int[] _structureDegree;
        readonly int[] _sRingStart, _sRing, _sRingIndex;   // structure-only CCW rings
        readonly double[] _side;                           // per half-edge band side factor (0 or 1)
        readonly int[] _crossingOf;                        // node -> crossing index or -1
        readonly byte[] _pairing;                          // degree-4 nodes: 0 = i with i+2, 1 = i with i^1, 2 = i with 3-i
        readonly List<int> _crossingNodes = new List<int>();
        readonly List<int> _over = new List<int>();
        readonly List<int> _threadStart = new List<int> { 0 };
        readonly List<int> _threadHalfEdges = new List<int>();
        readonly List<bool> _threadClosed = new List<bool>();

        /// <summary>Number of violated alternation constraints (0 when every thread alternates).</summary>
        public int WeaveConflicts { get; private set; }
        /// <summary>Number of structure edges (0 means nothing to band).</summary>
        public int StructureEdgeCount { get; private set; }

        /// <summary>Near-miss distances are only tracked below this (twice the widest band).</summary>
        public const double NearMissLimit = 2 * LayerBands.MaxWidth;

        /// <summary>
        /// Smallest distance between two structure edges that are topologically far apart (no shared node and no edge
        /// joining their ends), or +infinity when none is under <see cref="NearMissLimit"/>. Their bands touch at a width
        /// of exactly this distance (a stub stopping short of a line, an inner line close to a side). Edges one or two
        /// hops apart meet at joints, where the face-based width limit in BandOutline already applies.
        /// </summary>
        public double NearMissDistance { get; private set; } = double.PositiveInfinity;

        public int StructureDegree(int node) => _structureDegree[node];
        /// <summary>The k-th structure half-edge out of node n, CCW.</summary>
        public int StructureOut(int node, int k) => _sRing[_sRingStart[node] + k];
        /// <summary>Position of structure half-edge h in its origin's structure ring.</summary>
        public int StructureRingIndex(int h) => _sRingIndex[h];
        /// <summary>Band side factor of half-edge h: 1 = offset by the band's half width on its left, 0 = no offset.</summary>
        public double Side(int h) => _side[h];
        public bool IsStructure(int h) => !Graph.IsFrame(h);

        public int CrossingCount => _crossingNodes.Count;
        public int CrossingNode(int c) => _crossingNodes[c];
        /// <summary>Crossing index of a node, or -1.</summary>
        public int CrossingOf(int node) => _crossingOf[node];
        /// <summary>The pass (0 or 1) that goes over at crossing c, before FlipWeave.</summary>
        public int OverPass(int c) => _over[c];

        public int ThreadCount => _threadClosed.Count;
        public bool ThreadClosed(int t) => _threadClosed[t];
        public int ThreadLength(int t) => _threadStart[t + 1] - _threadStart[t];
        public int ThreadHalfEdge(int t, int k) => _threadHalfEdges[_threadStart[t] + k];

        /// <summary>Bands of a layer's shapes: LineShape segments and PolygonShape edges; circles and nulls are skipped.</summary>
        public static BandGraph Build(IReadOnlyList<Shape> shapes)
        {
            var segs = new List<PlanarSegment>();
            var edges = new List<int>();
            if (shapes != null)
                for (int i = 0; i < shapes.Count; i++) AddShape(segs, edges, shapes[i], i);
            return new BandGraph(PlanarGraph.Build(segs, true), segs, edges);
        }

        /// <summary>Bands of raw segments (Source = caller index, edge index 0); used by checks.</summary>
        public static BandGraph Build(IReadOnlyList<PlanarSegment> segments)
        {
            var segs = new List<PlanarSegment>(segments);
            var edges = new List<int>(segs.Count);
            for (int i = 0; i < segs.Count; i++) edges.Add(0);
            return new BandGraph(PlanarGraph.Build(segments, true), segs, edges);
        }

        static void AddShape(List<PlanarSegment> segs, List<int> edges, Shape shape, int index)
        {
            if (shape is LineShape line)
            {
                segs.Add(new PlanarSegment(line.A, line.B, index));
                edges.Add(0);
            }
            else if (shape is PolygonShape poly && poly.Sides >= 3 && poly.Sides <= 4096 && poly.Radius > 0)
            {
                for (int k = 0; k < poly.Sides; k++)
                {
                    segs.Add(new PlanarSegment(PolygonVertex(poly, k), PolygonVertex(poly, (k + 1) % poly.Sides), index));
                    edges.Add(k);
                }
            }
        }

        /// <summary>Vertex k of a polygon stamp, exactly as the bands and BoardView compute it.</summary>
        public static Vec2 PolygonVertex(PolygonShape p, int k) => Vertex(p, k);

        // ---------- input segments (for the interlace structure cut) ----------

        readonly List<PlanarSegment> _inputs;
        readonly List<int> _inputEdge;
        readonly Dictionary<int, int[]> _edgeInputs = new Dictionary<int, int[]>();

        /// <summary>Collinearity tolerance for matching input segments to planar edges (welds move ends by up to 1e-5).</summary>
        public const double InputMatchEps = 3 * PlanarGraph.WeldEps;   // two welds and a merge can each move an end by WeldEps

        public int InputCount => _inputs.Count;
        /// <summary>Input segment i as drawn: the shape's own A-B (unclipped), its shape index and polygon edge index.</summary>
        public PlanarSegment Input(int i) => _inputs[i];
        public int InputEdge(int i) => _inputEdge[i];

        /// <summary>
        /// The input segments that lie along planar edge <paramref name="edge"/>: both their ends within
        /// <see cref="InputMatchEps"/> of the edge's line and overlapping the edge. Collinear duplicates all match
        /// (the planarizer kept only one of them). Built once per edge on first use (topology data).
        /// </summary>
        public int[] InputsOnEdge(int edge)
        {
            if (_edgeInputs.TryGetValue(edge, out int[] found)) return found;
            Vec2 a = Graph.Node(Graph.Origin(2 * edge)), b = Graph.Node(Graph.Dest(2 * edge));
            var list = new List<int>();
            for (int i = 0; i < _inputs.Count; i++)
            {
                Vec2 p = _inputs[i].A, q = _inputs[i].B;
                if (LineDist(p, a, b) > InputMatchEps || LineDist(q, a, b) > InputMatchEps) continue;
                Vec2 d = q - p;
                double len = d.Length;
                if (!(len > 0)) continue;
                double ta = ((a.X - p.X) * d.X + (a.Y - p.Y) * d.Y) / len, tb = ((b.X - p.X) * d.X + (b.Y - p.Y) * d.Y) / len;
                if (Math.Min(len, Math.Max(ta, tb)) - Math.Max(0, Math.Min(ta, tb)) <= InputMatchEps) continue;   // no overlap
                list.Add(i);
            }
            found = list.ToArray();
            _edgeInputs[edge] = found;
            return found;
        }

        static double LineDist(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 d = b - a;
            double len = d.Length;
            return Math.Abs(d.X * (p.Y - a.Y) - d.Y * (p.X - a.X)) / len;
        }

        static Vec2 Vertex(PolygonShape p, int k)
        {
            double a = p.RotationRad + 2 * Math.PI * k / p.Sides;
            return new Vec2(p.Center.X + p.Radius * Math.Cos(a), p.Center.Y + p.Radius * Math.Sin(a));
        }

        BandGraph(PlanarGraph g, List<PlanarSegment> inputs, List<int> inputEdge)
        {
            Graph = g;
            _inputs = inputs;
            _inputEdge = inputEdge;
            int nn = g.NodeCount, nh = g.HalfEdgeCount;

            // Structure-only rings (CCW, a filtered copy of the full ring) and degrees.
            _structureDegree = new int[nn];
            _sRingStart = new int[nn + 1];
            _sRingIndex = new int[nh];
            var ring = new List<int>(nh);
            for (int v = 0; v < nn; v++)
            {
                _sRingStart[v] = ring.Count;
                for (int k = 0; k < g.Degree(v); k++)
                {
                    int h = g.Out(v, k);
                    if (g.IsFrame(h)) continue;
                    _sRingIndex[h] = ring.Count - _sRingStart[v];
                    ring.Add(h);
                }
                _structureDegree[v] = ring.Count - _sRingStart[v];
            }
            _sRingStart[nn] = ring.Count;
            _sRing = ring.ToArray();

            // Side factors; a structure line lying on a frame side keeps only its inner side.
            _side = new double[nh];
            for (int h = 0; h < nh; h++)
            {
                if (g.IsFrame(h)) continue;
                if ((h & 1) == 0) StructureEdgeCount++;
                _side[h] = OutwardOnFrame(g, h) ? 0 : 1;
            }

            // Crossings in node order (smallest (x, y) first: nodes are sorted).
            _crossingOf = new int[nn];
            _pairing = new byte[nn];
            for (int v = 0; v < nn; v++)
            {
                _crossingOf[v] = -1;
                if (_structureDegree[v] == 4) _pairing[v] = Pairing(v);
                if (_structureDegree[v] == 4 && !g.NodeOnFrame(v) && _pairing[v] == 0)
                {
                    _crossingOf[v] = _crossingNodes.Count;
                    _crossingNodes.Add(v);
                    _over.Add(-1);
                }
            }
            TraceThreadsAndWeave();
            NearMissDistance = ComputeNearMiss();
        }

        bool Neighbours(int a, int b)
        {
            for (int k = 0; k < _structureDegree[a]; k++)
                if (Graph.Dest(_sRing[_sRingStart[a] + k]) == b) return true;
            return false;
        }

        double ComputeNearMiss()
        {
            var g = Graph;
            var edges = new List<int>(StructureEdgeCount);
            for (int e = 0; e < g.EdgeCount; e++)
                if (!g.IsFrame(2 * e)) edges.Add(e);
            int n = edges.Count;
            var minX = new double[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = edges[i];
                minX[i] = Math.Min(g.Node(g.Origin(2 * edges[i])).X, g.Node(g.Dest(2 * edges[i])).X);
            }
            Array.Sort(minX, order);
            double best = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                int e = order[i];
                int a = g.Origin(2 * e), b = g.Dest(2 * e);
                Vec2 pa = g.Node(a), pb = g.Node(b);
                double reach = Math.Max(pa.X, pb.X) + Math.Min(best, NearMissLimit);
                double y0 = Math.Min(pa.Y, pb.Y), y1 = Math.Max(pa.Y, pb.Y);
                for (int j = i + 1; j < n && minX[j] <= reach; j++)
                {
                    int f = order[j];
                    int c = g.Origin(2 * f), d = g.Dest(2 * f);
                    if (a == c || a == d || b == c || b == d) continue;
                    if (Neighbours(a, c) || Neighbours(a, d) || Neighbours(b, c) || Neighbours(b, d)) continue;
                    Vec2 pc = g.Node(c), pd = g.Node(d);
                    double lim = Math.Min(best, NearMissLimit);
                    if (Math.Min(pc.Y, pd.Y) > y1 + lim || Math.Max(pc.Y, pd.Y) < y0 - lim) continue;
                    double dist = Math.Min(Math.Min(SegDist(pa, pc, pd), SegDist(pb, pc, pd)),
                                           Math.Min(SegDist(pc, pa, pb), SegDist(pd, pa, pb)));
                    if (dist < best && dist < NearMissLimit) best = dist;
                }
            }
            return best;
        }

        static double SegDist(Vec2 p, Vec2 a, Vec2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            double x = a.X + dx * t - p.X, y = a.Y + dy * t - p.Y;
            return Math.Sqrt(x * x + y * y);
        }

        /// <summary>True when h is a structure half-edge on a frame side whose left side points out of the board.</summary>
        static bool OutwardOnFrame(PlanarGraph g, int h)
        {
            Vec2 a = g.Node(g.Origin(h)), b = g.Node(g.Dest(h)), d = g.Dir(h);
            var nL = new Vec2(-d.Y, d.X);
            const double e = PlanarGraph.FrameEps;
            if (Math.Abs(a.X) <= e && Math.Abs(b.X) <= e) return nL.X < 0;
            if (Math.Abs(a.X - 1) <= e && Math.Abs(b.X - 1) <= e) return nL.X > 0;
            if (Math.Abs(a.Y) <= e && Math.Abs(b.Y) <= e) return nL.Y < 0;
            if (Math.Abs(a.Y - 1) <= e && Math.Abs(b.Y - 1) <= e) return nL.Y > 0;
            return false;
        }

        /// <summary>
        /// How a structure-degree-4 node pairs its edges: 0 (i with i + 2) unless two ring-adjacent edges point in opposite
        /// directions (a straight line with a tip touching it from one side): 1 for the pairs (0,1)(2,3), 2 for (0,3)(1,2).
        /// </summary>
        byte Pairing(int v)
        {
            int start = _sRingStart[v];
            for (int k = 0; k < 4; k++)
            {
                Vec2 a = Graph.Dir(_sRing[start + k]), b = Graph.Dir(_sRing[start + (k + 1) % 4]);
                double cross = a.X * b.Y - a.Y * b.X, dot = a.X * b.X + a.Y * b.Y;
                if (dot < 0 && Math.Abs(Math.Atan2(cross, -dot)) < BandOutline.StraightEps)
                    return (byte)(k == 0 || k == 2 ? 1 : 2);
            }
            return 0;
        }

        bool Stops(int v) =>
            !(_structureDegree[v] == 2 || _crossingOf[v] >= 0 || (_structureDegree[v] == 4 && _pairing[v] != 0 && !Graph.NodeOnFrame(v)));

        /// <summary>True when a thread runs from structure half-edge h straight on into <paramref name="next"/> (same band).</summary>
        public bool Continues(int h, int next) => IsStructure(h) && Continue(h, out _) == next;

        /// <summary>The structure half-edge a thread continues with after h, or -1 at a stop.</summary>
        int Continue(int h, out int pass)
        {
            pass = -1;
            int v = Graph.Dest(h);
            if (Stops(v)) return -1;
            int k = _sRingIndex[PlanarGraph.Twin(h)];
            int start = _sRingStart[v];
            if (_structureDegree[v] == 2) return _sRing[start + 1 - k];
            if (_pairing[v] == 1) return _sRing[start + (k ^ 1)];        // a turn, not a crossing
            if (_pairing[v] == 2) return _sRing[start + 3 - k];
            pass = k & 1;
            return _sRing[start + (k + 2) % 4];
        }

        void TraceThreadsAndWeave()
        {
            var g = Graph;
            var visited = new bool[g.EdgeCount];
            var c1 = new List<int>();   // constraints: crossing a, crossing b, parity
            var c2 = new List<int>();
            var cp = new List<int>();
            var visits = new List<int>();
            var passes = new List<int>();

            // Open threads first (from every stop node, in node and ring order), then the closed ones.
            for (int v = 0; v < g.NodeCount; v++)
            {
                if (!Stops(v)) continue;
                for (int k = 0; k < _structureDegree[v]; k++)
                {
                    int h = _sRing[_sRingStart[v] + k];
                    if (!visited[PlanarGraph.EdgeOf(h)]) Trace(h, false, visited, visits, passes, c1, c2, cp);
                }
            }
            for (int e = 0; e < g.EdgeCount; e++)
                if (!visited[e] && !g.IsFrame(2 * e)) Trace(2 * e, true, visited, visits, passes, c1, c2, cp);

            // BFS 2-colouring in crossing (= node) order.
            int nc = _crossingNodes.Count;
            var adj = new List<int>[nc];
            for (int i = 0; i < c1.Count; i++)
            {
                if (c1[i] == c2[i]) continue;   // self-loop: checked below
                (adj[c1[i]] ??= new List<int>()).Add(i);
                (adj[c2[i]] ??= new List<int>()).Add(i);
            }
            var queue = new Queue<int>();
            for (int s = 0; s < nc; s++)
            {
                if (_over[s] >= 0) continue;
                _over[s] = 0;
                queue.Enqueue(s);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    if (adj[c] == null) continue;
                    foreach (int i in adj[c])
                    {
                        int o = c1[i] == c ? c2[i] : c1[i];
                        if (_over[o] >= 0) continue;
                        _over[o] = _over[c] ^ cp[i];
                        queue.Enqueue(o);
                    }
                }
            }
            int conflicts = 0;
            for (int i = 0; i < c1.Count; i++)
                if ((_over[c1[i]] ^ _over[c2[i]]) != cp[i]) conflicts++;
            WeaveConflicts = conflicts;
        }

        void Trace(int start, bool closed, bool[] visited, List<int> visits, List<int> passes, List<int> c1,
            List<int> c2, List<int> cp)
        {
            visits.Clear();
            passes.Clear();
            int h = start;
            int guard = Graph.HalfEdgeCount + 1;
            while (guard-- > 0)
            {
                visited[PlanarGraph.EdgeOf(h)] = true;
                _threadHalfEdges.Add(h);
                int next = Continue(h, out int pass);
                if (pass >= 0)
                {
                    visits.Add(_crossingOf[Graph.Dest(h)]);
                    passes.Add(pass);
                }
                if (next < 0) { closed = false; break; }
                if (next == start) break;
                h = next;
            }
            _threadStart.Add(_threadHalfEdges.Count);
            _threadClosed.Add(closed);
            int n = visits.Count;
            for (int i = 0; i + 1 < n; i++) AddConstraint(visits[i], passes[i], visits[i + 1], passes[i + 1], c1, c2, cp);
            if (closed && n > 0) AddConstraint(visits[n - 1], passes[n - 1], visits[0], passes[0], c1, c2, cp);
        }

        static void AddConstraint(int a, int pa, int b, int pb, List<int> c1, List<int> c2, List<int> cp)
        {
            c1.Add(a);
            c2.Add(b);
            cp.Add(1 ^ pa ^ pb);
        }
    }
}
