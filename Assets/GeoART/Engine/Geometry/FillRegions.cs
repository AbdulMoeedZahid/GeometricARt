using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// The fillable regions of one layer (decision 6, docs/FILL.md), pure C#.
    ///   Boundaries  a plain layer: its lines, polygon edges and circles; a banded layer: its OUTER band outlines and
    ///               interlace bridges plus its circles (inner lines and the structure line only decorate a band, and the
    ///               inner lines depend on the board side, which the engine does not know). Circles are tessellated
    ///               with a chord error of at most 0.5e-5 board units (under the planarizer's weld), starting at angle 0
    ///               with a multiple of 8 vertices, so the eight circle divisions are vertices and a line ending on the
    ///               circle is welded onto it. All of it is planarized inside the board frame (<see cref="PlanarGraph"/>).
    ///   Region      a bounded face cycle (area &gt; 1e-12) with at least one shape edge whose other side is a different
    ///               face. So the frame closes a region only together with a shape (the gaps of a pattern that runs to
    ///               the board edge are fillable), while the bare board, the area around a floating star, or a board with
    ///               just a spur from the edge is "outside any closed shape".
    ///   Locate      the smallest-area face cycle whose polygon contains the point; it is the answer if it is a region.
    ///               Holes of a region: the outer boundaries (CW cycles without frame edges) of components that lie
    ///               inside it and in no smaller cycle.
    /// Flat bands leave their crossing squares open, so a band lattice is one region; interlace bridges split it into
    /// pieces (docs/BANDS.md).
    /// </summary>
    public sealed class FillRegions
    {
        public const double ChordError = 0.5e-5;
        public const int MaxCircleSegments = 1024;

        public readonly PlanarGraph Graph;
        readonly List<Vec2>[] _poly;     // per face cycle: its vertices
        readonly double[] _area;
        readonly double[] _minX, _minY, _maxX, _maxY;
        readonly bool[] _region;
        readonly List<int>[] _holes;     // per region: CW cycles inside it

        public int FaceCount => _area.Length;
        public bool IsRegion(int face) => _region[face];
        public double Area(int face) => _area[face];

        // ---------- build ----------

        /// <summary>Regions of a layer: band outlines when its bands are on, else its shapes (circles always).</summary>
        public static FillRegions Build(Layer layer)
        {
            var segs = new List<PlanarSegment>();
            if (layer == null) return new FillRegions(PlanarGraph.Build(segs, true));
            LayerBands bands = layer.Bands.Sanitized();
            if (bands.On)
            {
                var graph = BandGraph.Build(layer.Shapes);
                var o = BandOutlineOptions.From(bands, 0, 0);
                o.InnerLines = false;
                o.SkipSelfIntersections = true;
                var outline = BandOutline.Build(graph, o);
                foreach (var run in outline.Runs)
                {
                    int last = run.Closed ? run.Count : run.Count - 1;
                    for (int i = 0; i < last; i++)
                        segs.Add(new PlanarSegment(outline.Points[run.Start + i], outline.Points[run.Start + (i + 1) % run.Count], 0));
                }
                for (int i = 0; i + 1 < outline.Bridges.Count; i += 2)
                    segs.Add(new PlanarSegment(outline.Bridges[i], outline.Bridges[i + 1], 0));
            }
            for (int i = 0; i < layer.Shapes.Count; i++)
            {
                Shape s = layer.Shapes[i];
                switch (s)
                {
                    case CircleShape c:
                        AddCircle(segs, c.Center, c.Radius, i);
                        break;
                    case LineShape l when !bands.On:
                        segs.Add(new PlanarSegment(l.A, l.B, i));
                        break;
                    case PolygonShape p when !bands.On && p.Sides >= 3 && p.Sides <= 4096 && p.Radius > 0:
                        for (int k = 0; k < p.Sides; k++)
                            segs.Add(new PlanarSegment(BandGraph.PolygonVertex(p, k), BandGraph.PolygonVertex(p, (k + 1) % p.Sides), i));
                        break;
                }
            }
            return new FillRegions(PlanarGraph.Build(segs, true));
        }

        /// <summary>Regions of raw segments (checks).</summary>
        public static FillRegions Build(IReadOnlyList<PlanarSegment> segments) => new FillRegions(PlanarGraph.Build(segments, true));

        /// <summary>Segments of a circle with chord error at most <see cref="ChordError"/>; vertex 0 at angle 0.</summary>
        public static int CircleSegments(double radius)
        {
            if (!(radius > ChordError)) return 8;
            double half = Math.Acos(1 - ChordError / radius);
            int n = half > 0 ? (int)Math.Ceiling(Math.PI / half) : MaxCircleSegments;
            n = Math.Max(32, Math.Min(MaxCircleSegments, n));
            return (n + 7) / 8 * 8;
        }

        static void AddCircle(List<PlanarSegment> segs, Vec2 c, double r, int source)
        {
            if (!(r > 0) || !c.IsFinite || double.IsInfinity(r)) return;
            int n = CircleSegments(r), eighth = n / 8;
            Vec2 Point(int k) => k % eighth == 0
                ? CircleDivisions.Point(c, r, k / eighth)   // the exact snap points
                : new Vec2(c.X + r * Math.Cos(2 * Math.PI * k / n), c.Y + r * Math.Sin(2 * Math.PI * k / n));
            Vec2 prev = Point(0);
            for (int k = 1; k <= n; k++)
            {
                Vec2 next = Point(k % n);
                segs.Add(new PlanarSegment(prev, next, source));
                prev = next;
            }
        }

        FillRegions(PlanarGraph g)
        {
            Graph = g;
            int nf = g.FaceCount;
            _poly = new List<Vec2>[nf];
            _area = new double[nf];
            _minX = new double[nf]; _minY = new double[nf]; _maxX = new double[nf]; _maxY = new double[nf];
            _region = new bool[nf];
            _holes = new List<int>[nf];
            for (int f = 0; f < nf; f++)
            {
                var pts = new List<Vec2>(g.FaceSize(f));
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                bool shapeEdge = false;
                for (int k = 0; k < g.FaceSize(f); k++)
                {
                    int h = g.FaceHalfEdge(f, k);
                    Vec2 p = g.Node(g.Origin(h));
                    pts.Add(p);
                    x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                    // a shape edge with a different face on its other side (not a spur or a tree walked both ways)
                    if (!g.IsFrame(h) && g.FaceOf(PlanarGraph.Twin(h)) != f) shapeEdge = true;
                }
                _poly[f] = pts;
                _area[f] = g.FaceArea(f);
                _minX[f] = x0; _minY[f] = y0; _maxX[f] = x1; _maxY[f] = y1;
                _region[f] = _area[f] > 1e-12 && shapeEdge;
            }
            for (int f = 0; f < nf; f++)
            {
                if (!(_area[f] < -1e-12) || HasFrame(f)) continue;
                int parent = Smallest(Probe(f), f);
                if (parent >= 0 && _region[parent]) (_holes[parent] ??= new List<int>()).Add(f);
            }
        }

        bool HasFrame(int f)
        {
            for (int k = 0; k < Graph.FaceSize(f); k++)
                if (Graph.IsFrame(Graph.FaceHalfEdge(f, k))) return true;
            return false;
        }

        /// <summary>
        /// A point of the face just outside component boundary <paramref name="f"/> (a CW cycle has the surrounding face
        /// on its left): the midpoint of its longest edge, nudged a hair to the left. Its own vertices also lie on the
        /// component's inner faces, so they cannot be used to find the parent.
        /// </summary>
        Vec2 Probe(int f)
        {
            var p = _poly[f];
            int best = 0;
            double bestLen = -1;
            for (int i = 0; i < p.Count; i++)
            {
                double len = Vec2.Distance(p[i], p[(i + 1) % p.Count]);
                if (len > bestLen) { bestLen = len; best = i; }
            }
            Vec2 a = p[best], b = p[(best + 1) % p.Count];
            Vec2 d = (b - a) * (1 / Math.Max(bestLen, 1e-300));
            double nudge = Math.Min(1e-7, bestLen * 1e-3);
            return (a + b) * 0.5 + new Vec2(-d.Y, d.X) * nudge;
        }

        /// <summary>The smallest positive cycle (other than <paramref name="skip"/>) whose polygon contains <paramref name="p"/>.</summary>
        int Smallest(Vec2 p, int skip = -1)
        {
            int best = -1;
            double bestArea = double.MaxValue;
            for (int f = 0; f < _area.Length; f++)
            {
                if (f == skip || !(_area[f] > 1e-12) || _area[f] >= bestArea) continue;
                if (p.X < _minX[f] || p.X > _maxX[f] || p.Y < _minY[f] || p.Y > _maxY[f]) continue;
                if (!Triangulator.Contains(_poly[f], p)) continue;
                best = f;
                bestArea = _area[f];
            }
            return best;
        }

        // ---------- queries ----------

        /// <summary>The region containing <paramref name="p"/>, or -1 when the point is outside every closed region.</summary>
        public int Locate(Vec2 p)
        {
            if (!p.IsFinite) return -1;
            int f = Smallest(p);
            return f >= 0 && _region[f] ? f : -1;
        }

        /// <summary>Clears <paramref name="loops"/> and fills it with the region's outer boundary, then its holes.</summary>
        public void GetLoops(int face, List<List<Vec2>> loops)
        {
            loops.Clear();
            if (face < 0 || face >= _area.Length || !_region[face]) return;
            loops.Add(new List<Vec2>(_poly[face]));
            if (_holes[face] != null)
                foreach (int h in _holes[face]) loops.Add(new List<Vec2>(_poly[h]));
        }

        /// <summary>Area of the region: its cycle minus its holes.</summary>
        public double RegionArea(int face)
        {
            if (face < 0 || !_region[face]) return 0;
            double a = _area[face];
            if (_holes[face] != null) foreach (int h in _holes[face]) a += _area[h];   // hole areas are negative
            return a;
        }

        /// <summary>Appends the region's triangles (3 points each) to <paramref name="triangles"/>.</summary>
        public void Triangulate(int face, List<Vec2> triangles)
        {
            var loops = new List<List<Vec2>>();
            GetLoops(face, loops);
            if (loops.Count > 0) Triangulator.Triangulate(loops, triangles);
        }
    }
}
