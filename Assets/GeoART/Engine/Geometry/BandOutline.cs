using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>A polyline in <see cref="BandOutline.Points"/>: Count points from Start; Closed joins the last to the first.</summary>
    public struct BandRun
    {
        public int Start, Count;
        public bool Closed;
    }

    /// <summary>Options for <see cref="BandOutline.Build"/> (board units).</summary>
    public struct BandOutlineOptions
    {
        public double Width;              // band width, outer edge to outer edge
        public bool Interlace;            // add the over band's bridges at every woven crossing
        public bool FlipWeave;            // swap every over/under
        public bool InnerLines;           // second pair of lines inside each band
        public double InnerInsetFloor;    // inner lines sit at least this far in from the outer edge (1.5 mm on the board)
        public double InnerMinHalf;       // inner lines only when their half-width is at least this (core half + 1 mm)

        public static BandOutlineOptions From(in LayerBands bands, double innerInsetFloor, double innerMinHalf)
        {
            LayerBands b = bands.Sanitized();
            return new BandOutlineOptions
            {
                Width = b.Width,
                Interlace = b.Mode == BandMode.Interlace,
                FlipWeave = b.FlipWeave,
                InnerLines = b.InnerLines,
                InnerInsetFloor = innerInsetFloor,
                InnerMinHalf = innerMinHalf,
            };
        }
    }

    /// <summary>
    /// Band outlines of a <see cref="BandGraph"/> for one width (pure C#). With h = width / 2:
    ///
    /// Corners ("ruler method"): at node u, the wedge between outgoing half-edge g (direction d0, left offset x0 = h *
    /// side(g)) and its CCW successor (direction d1, whose right offset is the left offset x1 of prev(g)) has its corner
    /// where the two offset lines meet: t = (x1 + x0 cos)/sin along d0 and s = (x0 + x1 cos)/sin along d1 (equal sides:
    /// t = s = h cot(theta/2), computed as h sin/(1 - cos)). Special wedges: a free end gets a square cap extended by h; a
    /// near-straight wedge with unequal sides (a line on the frame running into the frame) a two-point step; the outer side
    /// of a tip under <see cref="BevelDegrees"/> a two-point bevel at the plain offset points. Every other tip keeps its
    /// true mitre, so 16-fold star tips are never blunted.
    ///
    /// Width safety: the offset of half-edge g has length L - t_tail - s_head = L - h (k_tail + k_head). It collapses at
    /// or below 0 (counted in <see cref="Collapsed"/>; a drawn one is replaced by the meeting point of its neighbours'
    /// offset lines, so the run stays continuous). <see cref="MaxSafeWidth"/> = 2 min L / (k_tail + k_head) over every
    /// half-edge with a positive sum, frame pieces included (two lines closer than the width both reaching the frame), and
    /// never more than <see cref="BandGraph.NearMissDistance"/> (parts that come close without touching).
    /// <see cref="SelfIntersections"/> counts proper crossings between all faces' inset polygons, which also catches
    /// split events (a reflex corner running into a non-adjacent side) and bands of separate parts running into each
    /// other.
    ///
    /// Runs: walking each face cycle, consecutive drawn (side &gt; 0) offsets join into one polyline; a zero-side
    /// half-edge (the frame) ends it, so bands end flush with the frame; a cycle with no zero side is a closed run. This
    /// alone is the flat Double look with open crossing squares (and the stripped look without the structure line).
    ///
    /// Interlace: at every woven crossing the over band's two bridges join the corners on each side of it, which breaks
    /// the under band at the over band's edges.
    ///
    /// Inner lines at h_in = h - max(0.2 width, floor), only when h_in &gt;= InnerMinHalf: per wedge, two drawn sides at a
    /// structure-degree-2 node (or a free end, a near-straight pass-through, a bevel) join like the outer lines at h_in;
    /// at any other wedge (junctions, crossings, the frame) each inner line ends on the neighbour's OUTER offset line.
    /// At woven crossings the over band's inner lines bridge straight through.
    /// </summary>
    public sealed class BandOutline
    {
        /// <summary>Tips sharper than this (outer side of the wedge) are bevelled for numerical safety.</summary>
        public const double BevelDegrees = 5.0;
        /// <summary>|theta - 180 deg| below this (radians) counts as a straight pass-through.</summary>
        public const double StraightEps = 1e-6;

        public readonly List<Vec2> Points = new List<Vec2>();
        public readonly List<BandRun> Runs = new List<BandRun>();
        public readonly List<BandRun> InnerRuns = new List<BandRun>();
        /// <summary>Over-band bridges, two points per segment (Interlace only).</summary>
        public readonly List<Vec2> Bridges = new List<Vec2>();
        public readonly List<Vec2> InnerBridges = new List<Vec2>();

        public double Width { get; private set; }
        /// <summary>Largest width at which no offset segment collapses (+infinity when nothing limits it).</summary>
        public double MaxSafeWidth { get; private set; } = double.PositiveInfinity;
        public int Collapsed { get; private set; }
        public int SelfIntersections { get; private set; }
        public int WeaveConflicts { get; private set; }
        /// <summary>True when inner lines were requested and fit (h_in &gt;= InnerMinHalf).</summary>
        public bool InnerShown { get; private set; }
        public double InnerHalfWidth { get; private set; }

        enum Join : byte { Mitre, Cap, Step, Bevel, Junction }

        BandGraph _bg;
        PlanarGraph _g;
        double _h;
        // Per wedge, indexed by the half-edge g whose tail it sits at (g's left, prev(g)'s left).
        Vec2[] _wIn, _wOut;      // end of prev(g)'s offset, start of g's offset
        double[] _kOut, _kIn;    // t/h along g, s/h along prev(g) back from u
        Join[] _join;
        Vec2[] _iIn, _iOut;      // inner lines
        double[] _iT, _iS;
        bool[] _iJoined;

        public static BandOutline Build(BandGraph graph, in BandOutlineOptions options)
        {
            var o = new BandOutline();
            if (graph == null || !(options.Width > 0) || double.IsInfinity(options.Width)) return o;
            o.Run(graph, options);
            return o;
        }

        void Run(BandGraph bg, in BandOutlineOptions opt)
        {
            _bg = bg;
            _g = bg.Graph;
            Width = opt.Width;
            _h = opt.Width * 0.5;
            WeaveConflicts = opt.Interlace ? bg.WeaveConflicts : 0;
            if (bg.StructureEdgeCount == 0) return;
            int nh = _g.HalfEdgeCount;
            _wIn = new Vec2[nh];
            _wOut = new Vec2[nh];
            _kOut = new double[nh];
            _kIn = new double[nh];
            _join = new Join[nh];
            for (int g = 0; g < nh; g++) OuterWedge(g);
            SafetyAndCollapse();
            BuildRuns();
            CountSelfIntersections();
            if (opt.Interlace) AddBridges(opt.FlipWeave, false);

            double inset = Math.Max(0.2 * opt.Width, opt.InnerInsetFloor);
            InnerHalfWidth = _h - inset;
            if (opt.InnerLines && InnerHalfWidth > 0 && InnerHalfWidth >= opt.InnerMinHalf)
            {
                InnerShown = true;
                _iIn = new Vec2[nh];
                _iOut = new Vec2[nh];
                _iT = new double[nh];
                _iS = new double[nh];
                _iJoined = new bool[nh];
                for (int g = 0; g < nh; g++)
                {
                    InnerWedge(g);
                    _iIn[g] = ClampToBoard(_iIn[g]);
                    _iOut[g] = ClampToBoard(_iOut[g]);
                }
                BuildInnerRuns();
                if (opt.Interlace) AddBridges(opt.FlipWeave, true);
            }
        }

        // ---------- corners ----------

        /// <summary>
        /// Corner of the wedge at u between direction d0 (left offset x0) and its CCW successor d1 (right offset x1).
        /// pOut lies on d0's offset line at t along d0, pIn on d1's at s along d1 (equal for a mitre).
        /// </summary>
        static Join Corner(Vec2 u, Vec2 d0, Vec2 d1, double x0, double x1, bool sameEdge, out Vec2 pOut, out Vec2 pIn,
            out double t, out double s)
        {
            var nL0 = new Vec2(-d0.Y, d0.X);
            var nR1 = new Vec2(d1.Y, -d1.X);
            if (sameEdge)
            {
                double ext = Math.Max(x0, x1);   // free end: square cap extended by the half width
                pOut = u + nL0 * x0 - d0 * ext;
                pIn = u + nR1 * x1 - d1 * ext;
                t = s = -ext;
                return Join.Cap;
            }
            double cross = d0.X * d1.Y - d0.Y * d1.X, dot = d0.X * d1.X + d0.Y * d1.Y;
            double theta = Math.Atan2(cross, dot);
            if (theta <= 0) theta += 2 * Math.PI;   // CCW from d0 to d1, in (0, 2 pi]
            if (Math.Abs(theta - Math.PI) < StraightEps)
            {
                pOut = u + nL0 * x0;
                pIn = x0 == x1 ? pOut : u + nR1 * x1;
                t = s = 0;
                return x0 == x1 ? Join.Mitre : Join.Step;
            }
            if (theta > 2 * Math.PI - BevelDegrees * Math.PI / 180.0 || !(Math.Abs(cross) > 1e-300))
            {
                pOut = u + nL0 * x0;
                pIn = u + nR1 * x1;
                t = s = 0;
                return Join.Bevel;
            }
            if (x0 == x1)
            {
                t = s = x0 * cross / (1 - dot);   // = h cot(theta / 2), stable near 180 degrees
            }
            else
            {
                t = (x1 + x0 * dot) / cross;
                s = (x0 + x1 * dot) / cross;
            }
            if (double.IsNaN(t) || double.IsInfinity(t) || double.IsNaN(s) || double.IsInfinity(s))
            {
                pOut = u + nL0 * x0;   // dot rounded to 1 with cross != 0: treat as a bevel
                pIn = u + nR1 * x1;
                t = s = 0;
                return Join.Bevel;
            }
            pOut = pIn = u + nL0 * x0 + d0 * t;
            return Join.Mitre;
        }

        void OuterWedge(int g)
        {
            int prev = _g.Prev(g);
            int succ = PlanarGraph.Twin(prev);   // CCW successor of g at its tail
            Vec2 u = _g.Node(_g.Origin(g));
            double x0 = _h * _bg.Side(g), x1 = _h * _bg.Side(prev);
            _join[g] = Corner(u, _g.Dir(g), _g.Dir(succ), x0, x1, succ == g, out _wOut[g], out _wIn[g], out double t,
                out double s);
            _kOut[g] = t / _h;
            _kIn[g] = s / _h;
        }

        void SafetyAndCollapse()
        {
            int nh = _g.HalfEdgeCount;
            double minSafe = double.PositiveInfinity;
            int collapsed = 0;
            for (int g = 0; g < nh; g++)
            {
                int next = _g.Next(g);
                double k = _kOut[g] + _kIn[next];
                double len = _g.Length(g);
                if (k > 1e-12) minSafe = Math.Min(minSafe, 2 * len / k);
                if (len - _h * k > 0) continue;
                collapsed++;
                if (_bg.Side(g) == 0) continue;
                // Replace the collapsed offset by the meeting point of its neighbours' offset lines.
                int prev = _g.Prev(g);
                Vec2 u = _g.Node(_g.Origin(g)), v = _g.Node(_g.Dest(g));
                Vec2 dp = _g.Dir(prev), dn = _g.Dir(next);
                Vec2 pp = u + new Vec2(-dp.Y, dp.X) * (_h * _bg.Side(prev));
                Vec2 pn = v + new Vec2(-dn.Y, dn.X) * (_h * _bg.Side(next));
                Vec2 x = LineLine(pp, dp, pn, dn, out bool ok);
                if (!ok || !x.IsFinite) x = (_wOut[g] + _wIn[next]) * 0.5;
                _wIn[g] = _wOut[g] = x;
                _wIn[next] = _wOut[next] = x;
            }
            MaxSafeWidth = Math.Min(minSafe, _bg.NearMissDistance);   // also: parts that come close without touching
            Collapsed = collapsed;
            // Nothing outside the board: a corner on a frame line stays on it, a tip spike or cap near the frame is cut.
            for (int g = 0; g < nh; g++)
            {
                _wIn[g] = ClampToBoard(_wIn[g]);
                _wOut[g] = ClampToBoard(_wOut[g]);
            }
        }

        static Vec2 ClampToBoard(Vec2 p) =>
            p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1 ? p : new Vec2(Math.Max(0, Math.Min(1, p.X)), Math.Max(0, Math.Min(1, p.Y)));

        static Vec2 LineLine(Vec2 p, Vec2 d, Vec2 q, Vec2 e, out bool ok)
        {
            double den = d.X * e.Y - d.Y * e.X;
            ok = Math.Abs(den) > 1e-12;
            if (!ok) return p;
            Vec2 w = q - p;
            double t = (w.X * e.Y - w.Y * e.X) / den;
            return p + d * t;
        }

        // ---------- runs ----------

        bool Drawn(int g) => _bg.Side(g) > 0;

        void Append(Vec2 p)
        {
            int n = Points.Count;
            if (n > 0 && _runOpen && Points[n - 1] == p) return;
            Points.Add(p);
        }

        bool _runOpen;
        int _runStart;

        void BeginRun() { _runStart = Points.Count; _runOpen = true; }

        void EndRun(List<BandRun> into, bool closed)
        {
            _runOpen = false;
            int count = Points.Count - _runStart;
            if (closed && count > 1 && Points[Points.Count - 1] == Points[_runStart]) { Points.RemoveAt(Points.Count - 1); count--; }
            if (count < 2) { Points.RemoveRange(_runStart, count); return; }
            into.Add(new BandRun { Start = _runStart, Count = count, Closed = closed && count > 2 });
        }

        void BuildRuns()
        {
            for (int f = 0; f < _g.FaceCount; f++)
            {
                int n = _g.FaceSize(f);
                int firstGap = -1;
                for (int k = 0; k < n; k++)
                    if (!Drawn(_g.FaceHalfEdge(f, k))) { firstGap = k; break; }
                if (firstGap < 0)
                {
                    BeginRun();
                    for (int k = 0; k < n; k++)
                    {
                        int g = _g.FaceHalfEdge(f, k);
                        Append(_wIn[g]);
                        Append(_wOut[g]);
                    }
                    EndRun(Runs, true);
                    continue;
                }
                // Open runs: start right after each gap, in cycle order from the first gap.
                for (int i = 1; i <= n; i++)
                {
                    int k = (firstGap + i) % n;
                    int g = _g.FaceHalfEdge(f, k);
                    if (!Drawn(g) || Drawn(_g.Prev(g))) continue;
                    BeginRun();
                    Append(_wIn[g]);
                    Append(_wOut[g]);
                    int cur = g;
                    while (true)
                    {
                        int next = _g.Next(cur);
                        Append(_wIn[next]);
                        Append(_wOut[next]);
                        if (!Drawn(next)) break;
                        cur = next;
                    }
                    EndRun(Runs, false);
                }
            }
        }

        /// <summary>
        /// Proper crossings between the inset polygons of all face cycles together (the polygon of each cycle: every
        /// wedge's points, frame pieces included). Comparing across cycles catches a face with two boundary loops (an
        /// island inside it) and separate components; neighbours within one polygon share an end and are skipped. A sweep
        /// over x-sorted segments keeps it near-linear.
        /// </summary>
        void CountSelfIntersections()
        {
            var a = new List<Vec2>();
            var b = new List<Vec2>();
            var poly = new List<int>();
            var index = new List<int>();
            var size = new List<int>();
            var pts = new List<Vec2>();
            for (int f = 0; f < _g.FaceCount; f++)
            {
                int n = _g.FaceSize(f);
                bool any = false;
                pts.Clear();
                for (int k = 0; k < n; k++)
                {
                    int g = _g.FaceHalfEdge(f, k);
                    if (Drawn(g)) any = true;
                    AddDistinct(pts, _wIn[g]);
                    AddDistinct(pts, _wOut[g]);
                }
                if (!any) continue;
                if (pts.Count > 1 && pts[pts.Count - 1] == pts[0]) pts.RemoveAt(pts.Count - 1);
                int m = pts.Count;
                if (m < 2) continue;
                for (int i = 0; i < m; i++)
                {
                    a.Add(pts[i]);
                    b.Add(pts[(i + 1) % m]);
                    poly.Add(f);
                    index.Add(i);
                    size.Add(m);
                }
            }
            int total = a.Count;
            var order = new int[total];
            var minX = new double[total];
            for (int i = 0; i < total; i++)
            {
                order[i] = i;
                minX[i] = Math.Min(a[i].X, b[i].X);
            }
            Array.Sort(minX, order);
            int count = 0;
            for (int u = 0; u < total; u++)
            {
                int i = order[u];
                Vec2 p0 = a[i], p1 = b[i];
                double maxX = Math.Max(p0.X, p1.X), y0 = Math.Min(p0.Y, p1.Y), y1 = Math.Max(p0.Y, p1.Y);
                for (int v = u + 1; v < total && minX[v] <= maxX; v++)
                {
                    int j = order[v];
                    if (poly[i] == poly[j])
                    {
                        int d = Math.Abs(index[i] - index[j]);
                        if (d <= 1 || d == size[i] - 1) continue;   // neighbours share an end
                    }
                    Vec2 q0 = a[j], q1 = b[j];
                    if (Math.Max(q0.Y, q1.Y) < y0 || Math.Min(q0.Y, q1.Y) > y1) continue;
                    if (ProperCross(p0, p1, q0, q1)) count++;
                }
            }
            SelfIntersections = count;
        }

        static void AddDistinct(List<Vec2> list, Vec2 p)
        {
            if (list.Count == 0 || list[list.Count - 1] != p) list.Add(p);
        }

        static bool ProperCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            double d1 = Orient(c, d, a), d2 = Orient(c, d, b), d3 = Orient(a, b, c), d4 = Orient(a, b, d);
            const double eps = 1e-18;
            return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
        }

        static double Orient(Vec2 a, Vec2 b, Vec2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        // ---------- interlace ----------

        /// <summary>
        /// At each crossing with over pass p (structure ring s0..s3), the over band s_p / s_p+2 bridges the wedges on its
        /// two sides: corner(s_p) to corner(s_p+1) and corner(s_p+2) to corner(s_p+3).
        /// </summary>
        void AddBridges(bool flip, bool inner)
        {
            var into = inner ? InnerBridges : Bridges;
            for (int c = 0; c < _bg.CrossingCount; c++)
            {
                int v = _bg.CrossingNode(c);
                int p = _bg.OverPass(c) ^ (flip ? 1 : 0);
                for (int side = 0; side < 2; side++)
                {
                    int a = _bg.StructureOut(v, (p + 2 * side) % 4);
                    int b = _bg.StructureOut(v, (p + 2 * side + 1) % 4);
                    Vec2 pa = inner ? _iOut[a] : _wOut[a];
                    Vec2 pb = inner ? _iIn[b] : _wIn[b];
                    if (pa == pb) continue;
                    into.Add(pa);
                    into.Add(pb);
                }
            }
        }

        // ---------- inner lines ----------

        void InnerWedge(int g)
        {
            int prev = _g.Prev(g);
            int succ = PlanarGraph.Twin(prev);
            int u = _g.Origin(g);
            Vec2 pu = _g.Node(u), d0 = _g.Dir(g), d1 = _g.Dir(succ);
            double hi = InnerHalfWidth;
            bool both = Drawn(g) && Drawn(prev);
            Join outer = _join[g];
            bool joined = both && (succ == g || _bg.Continues(prev, g) || outer == Join.Bevel ||
                                   (outer == Join.Mitre && Straight(d0, d1)));
            if (joined)
            {
                Corner(pu, d0, d1, hi, hi, succ == g, out _iOut[g], out _iIn[g], out double t, out double s);
                _iT[g] = t;
                _iS[g] = s;
                _iJoined[g] = true;
                return;
            }
            // Junction, crossing or frame: each inner line ends on the neighbour's outer offset line.
            double outerG = _h * _bg.Side(g), outerP = _h * _bg.Side(prev);
            if (Straight(d0, d1))
            {
                _iOut[g] = pu + new Vec2(-d0.Y, d0.X) * hi;
                _iIn[g] = pu + new Vec2(d1.Y, -d1.X) * hi;
                _iT[g] = _iS[g] = 0;
                return;
            }
            Corner(pu, d0, d1, hi, outerP, false, out _iOut[g], out _, out double tOut, out _);
            Corner(pu, d0, d1, outerG, hi, false, out _, out _iIn[g], out _, out double sIn);
            _iT[g] = tOut;
            _iS[g] = sIn;
        }

        static bool Straight(Vec2 d0, Vec2 d1)
        {
            double cross = d0.X * d1.Y - d0.Y * d1.X, dot = d0.X * d1.X + d0.Y * d1.Y;
            double theta = Math.Atan2(cross, dot);
            if (theta <= 0) theta += 2 * Math.PI;
            return Math.Abs(theta - Math.PI) < StraightEps;
        }

        bool InnerValid(int g) => Drawn(g) && _g.Length(g) - _iT[g] - _iS[_g.Next(g)] > 0;

        void BuildInnerRuns()
        {
            for (int f = 0; f < _g.FaceCount; f++)
            {
                int n = _g.FaceSize(f);
                int firstBreak = -1;
                for (int k = 0; k < n; k++)
                {
                    int g = _g.FaceHalfEdge(f, k);
                    if (!InnerValid(g) || !_iJoined[g]) { firstBreak = k; break; }
                }
                if (firstBreak < 0)
                {
                    BeginRun();
                    for (int k = 0; k < n; k++)
                    {
                        int g = _g.FaceHalfEdge(f, k);
                        Append(_iIn[g]);
                        Append(_iOut[g]);
                    }
                    EndRun(InnerRuns, true);
                    continue;
                }
                for (int i = 0; i < n; i++)
                {
                    int k = (firstBreak + i) % n;
                    int g = _g.FaceHalfEdge(f, k);
                    if (!InnerValid(g)) continue;
                    bool continues = _iJoined[g] && InnerValid(_g.Prev(g));
                    if (continues) continue;   // part of a run started earlier
                    BeginRun();
                    Append(_iOut[g]);
                    int cur = g;
                    while (true)
                    {
                        int next = _g.Next(cur);
                        Append(_iIn[next]);
                        if (!_iJoined[next] || !InnerValid(next)) break;
                        Append(_iOut[next]);
                        cur = next;
                    }
                    EndRun(InnerRuns, false);
                }
            }
        }
    }
}
