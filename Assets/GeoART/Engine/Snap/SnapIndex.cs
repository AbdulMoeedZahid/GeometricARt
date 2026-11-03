using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// Every snap candidate of a board, deduplicated and ordered by priority (plan 3.2). Pure C#, no Unity types.
    ///
    /// Sources, high to low priority: user points, endpoints (line ends, polygon vertices), shape/shape intersections,
    /// grid intersections (tile corners), circle and polygon centres, the 8 circle divisions, tile edge midpoints, tile
    /// centres. Shapes and user points come from visible layers only; grid points come from
    /// <see cref="TileGrid.SnapPoints"/> and are there even on an empty board. Points that are non-finite or outside
    /// [0,1]^2 (with <see cref="BoundsSlack"/>) are dropped.
    ///
    /// Candidate positions are the source values themselves (shape fields, <see cref="SnapPoint.Position"/>, the values
    /// TileGrid returns), so a snapped coordinate can be compared with ==. When two candidates lie within
    /// <see cref="DedupEpsilon"/>, the one with higher priority (then the earlier one) is kept, position and kind.
    ///
    /// The list is rebuilt lazily by <see cref="Sync"/>: never per pointer move. Edits that keep every count the same
    /// (moving or restyling a shape, moving a user point) are not visible to the signature: call <see cref="MarkDirty"/>
    /// on every model change.
    /// </summary>
    public sealed class SnapIndex
    {
        /// <summary>Candidates closer than this (board units) are one point.</summary>
        public const double DedupEpsilon = 1e-7;

        /// <summary>A candidate is kept when -BoundsSlack &lt;= x, y &lt;= 1 + BoundsSlack.</summary>
        public const double BoundsSlack = 1e-9;

        /// <summary>Division points per circle, at k * 45 degrees from the exact table in Contracts.CircleDivisions.</summary>
        public const int CircleDivisions = Contracts.CircleDivisions.Count;

        /// <summary>
        /// When true, visible shapes are also intersected with the grid lines (<see cref="TileGrid.UniqueEdges"/>),
        /// reported as <see cref="SnapKind.Intersection"/>. Off by default (plan 3.2). Changing it is picked up by Sync.
        /// </summary>
        public bool IncludeShapeGridCrossings;

        public IReadOnlyList<SnapCandidate> Candidates => _candidates;

        readonly List<SnapCandidate> _candidates = new List<SnapCandidate>();

        // Dedup: uniform hash grid with cell side DedupEpsilon; each bucket is a chain of candidate indices.
        readonly Dictionary<long, int> _bucketHead = new Dictionary<long, int>();
        readonly List<int> _bucketNext = new List<int>();

        // Drawn geometry flattened into segments and circles, in layer then shape order.
        readonly List<Prim> _prims = new List<Prim>();

        // Grid caches, refilled only when the layout changes.
        readonly List<SnapCandidate> _gridPoints = new List<SnapCandidate>();
        GridKind _gridPointsKind;
        int _gridPointsN;   // 0 = not filled yet
        readonly List<(Vec2 A, Vec2 B)> _gridEdges = new List<(Vec2 A, Vec2 B)>();
        GridKind _gridEdgesKind;
        int _gridEdgesN;

        // Signature of the last build.
        bool _dirty = true;
        Board _sigBoard;
        int _sigGridN;
        GridKind _sigGridKind;
        bool _sigCrossings;
        readonly List<LayerSig> _sigLayers = new List<LayerSig>();

        readonly struct LayerSig
        {
            public readonly EntityId Id;
            public readonly bool Visible;
            public readonly int Shapes, SnapPoints;

            public LayerSig(Layer l)
            {
                Id = l.Id; Visible = l.Visible; Shapes = l.Shapes.Count; SnapPoints = l.SnapPoints.Count;
            }

            public bool Matches(Layer l) =>
                l != null && Id == l.Id && Visible == l.Visible && Shapes == l.Shapes.Count && SnapPoints == l.SnapPoints.Count;
        }

        readonly struct Prim
        {
            public readonly bool IsCircle;
            public readonly Vec2 A, B;        // segment ends, or A = circle centre
            public readonly double Radius;

            public Prim(Vec2 a, Vec2 b) { IsCircle = false; A = a; B = b; Radius = 0; }
            public Prim(Vec2 center, double radius) { IsCircle = true; A = center; B = center; Radius = radius; }
        }

        /// <summary>0 = highest. Total order, so ties between kinds never depend on insertion order.</summary>
        public static int Priority(SnapKind k)
        {
            switch (k)
            {
                case SnapKind.UserPoint: return 0;
                case SnapKind.Endpoint: return 1;
                case SnapKind.Intersection: return 2;
                case SnapKind.CellCorner: return 3;
                case SnapKind.Center: return 4;
                case SnapKind.CircleDivision: return 5;
                case SnapKind.CellEdgeMid: return 6;
                case SnapKind.CellCenter: return 7;
                default: return 8;   // None and unknown values
            }
        }

        /// <summary>Forces the next <see cref="Sync"/> to rebuild.</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>
        /// Rebuilds only if <see cref="MarkDirty"/> was called or the board's signature changed (board reference, GridN,
        /// GridKind, <see cref="IncludeShapeGridCrossings"/>, layer count, and per layer Id, Visible, Shapes.Count,
        /// SnapPoints.Count). Allocation-free when nothing changed. Returns true if it rebuilt.
        /// </summary>
        public bool Sync(Board board)
        {
            if (!_dirty && SignatureMatches(board)) return false;
            Rebuild(board);
            return true;
        }

        /// <summary>Unconditional rebuild. A null board gives an empty list.</summary>
        public void Rebuild(Board board)
        {
            _candidates.Clear();
            _bucketHead.Clear();
            _bucketNext.Clear();
            _prims.Clear();
            RecordSignature(board);
            _dirty = false;
            if (board == null) return;

            var layers = board.Layers;

            // 1. User points.
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                for (int i = 0; i < layer.SnapPoints.Count; i++)
                {
                    var sp = layer.SnapPoints[i];
                    if (sp != null) Add(sp.Position, SnapKind.UserPoint);
                }
            }

            // 2. Endpoints, collecting the drawn primitives on the way.
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                for (int i = 0; i < layer.Shapes.Count; i++)
                {
                    switch (layer.Shapes[i])
                    {
                        case LineShape line:
                            Add(line.A, SnapKind.Endpoint);
                            Add(line.B, SnapKind.Endpoint);
                            _prims.Add(new Prim(line.A, line.B));
                            break;
                        case CircleShape circle:
                            if (IsDrawable(circle)) _prims.Add(new Prim(circle.Center, circle.Radius));
                            break;
                        case PolygonShape poly:
                            if (!IsDrawable(poly)) break;
                            Vec2 first = PolygonVertex(poly, 0), prev = first;
                            Add(first, SnapKind.Endpoint);
                            for (int k = 1; k < poly.Sides; k++)
                            {
                                Vec2 v = PolygonVertex(poly, k);
                                Add(v, SnapKind.Endpoint);
                                _prims.Add(new Prim(prev, v));
                                prev = v;
                            }
                            _prims.Add(new Prim(prev, first));
                            break;
                    }
                }
            }

            // 3. Intersections: every pair of drawn primitives, then (optionally) primitives x grid lines.
            for (int i = 0; i < _prims.Count; i++)
                for (int j = i + 1; j < _prims.Count; j++)
                    AddCrossings(_prims[i], _prims[j]);

            if (IncludeShapeGridCrossings && _prims.Count > 0)
            {
                FillGridEdges(board.GridKind, board.GridN);
                for (int i = 0; i < _prims.Count; i++)
                    for (int e = 0; e < _gridEdges.Count; e++)
                        AddCrossings(_prims[i], new Prim(_gridEdges[e].A, _gridEdges[e].B));
            }

            // 4. Grid intersections (tile corners).
            FillGridPoints(board.GridKind, board.GridN);
            AddGrid(SnapKind.CellCorner);

            // 5. Centres, 6. circle divisions.
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                for (int i = 0; i < layer.Shapes.Count; i++)
                {
                    if (layer.Shapes[i] is CircleShape circle && IsDrawable(circle)) Add(circle.Center, SnapKind.Center);
                    else if (layer.Shapes[i] is PolygonShape poly && IsDrawable(poly)) Add(poly.Center, SnapKind.Center);
                }
            }
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                for (int i = 0; i < layer.Shapes.Count; i++)
                {
                    if (!(layer.Shapes[i] is CircleShape circle) || !IsDrawable(circle)) continue;
                    Vec2 c = circle.Center;
                    double r = circle.Radius;
                    for (int k = 0; k < CircleDivisions; k++)   // exact table, shared with GoldenBoards.StarPoint
                        Add(Contracts.CircleDivisions.Point(c, r, k), SnapKind.CircleDivision);
                }
            }

            // 7. Tile edge midpoints, tile centres.
            AddGrid(SnapKind.CellEdgeMid);
            AddGrid(SnapKind.CellCenter);
        }

        /// <summary>Vertex k of a regular polygon; the same expression BoardView draws with.</summary>
        public static Vec2 PolygonVertex(PolygonShape poly, int k)
        {
            double a = poly.RotationRad + 2.0 * Math.PI * k / poly.Sides;
            return new Vec2(poly.Center.X + poly.Radius * Math.Cos(a), poly.Center.Y + poly.Radius * Math.Sin(a));
        }

        static bool IsDrawable(CircleShape c) => c.Radius > 0 && !double.IsInfinity(c.Radius) && c.Center.IsFinite;

        static bool IsDrawable(PolygonShape p) =>
            p.Sides >= 3 && p.Radius > 0 && !double.IsInfinity(p.Radius) && p.Center.IsFinite && !double.IsNaN(p.RotationRad)
            && !double.IsInfinity(p.RotationRad);

        void AddCrossings(in Prim a, in Prim b)
        {
            Vec2 p0, p1;
            int n;
            if (!a.IsCircle && !b.IsCircle)
            {
                n = Intersect.SegmentSegment(a.A, a.B, b.A, b.B, out p0);
                p1 = Vec2.Zero;
            }
            else if (!a.IsCircle) n = Intersect.SegmentCircle(a.A, a.B, b.A, b.Radius, out p0, out p1);
            else if (!b.IsCircle) n = Intersect.SegmentCircle(b.A, b.B, a.A, a.Radius, out p0, out p1);
            else n = Intersect.CircleCircle(a.A, a.Radius, b.A, b.Radius, out p0, out p1);
            if (n >= 1) Add(p0, SnapKind.Intersection);
            if (n >= 2) Add(p1, SnapKind.Intersection);
        }

        void FillGridPoints(GridKind kind, int n)
        {
            n = TileGrid.ClampN(n);
            if (_gridPointsN == n && _gridPointsKind == kind) return;
            TileGrid.SnapPoints(kind, n, _gridPoints);
            _gridPointsKind = kind;
            _gridPointsN = n;
        }

        void FillGridEdges(GridKind kind, int n)
        {
            n = TileGrid.ClampN(n);
            if (_gridEdgesN == n && _gridEdgesKind == kind) return;
            TileGrid.UniqueEdges(kind, n, _gridEdges);
            _gridEdgesKind = kind;
            _gridEdgesN = n;
        }

        void AddGrid(SnapKind kind)
        {
            for (int i = 0; i < _gridPoints.Count; i++)
                if (_gridPoints[i].Kind == kind) Add(_gridPoints[i].Position, kind);
        }

        /// <summary>
        /// Appends p unless it is off the board or within DedupEpsilon of a candidate already in the list. Sources are
        /// added in priority order, so whatever is already there has priority >= this one and wins.
        /// </summary>
        void Add(Vec2 p, SnapKind kind)
        {
            if (!p.IsFinite) return;
            if (p.X < -BoundsSlack || p.X > 1 + BoundsSlack || p.Y < -BoundsSlack || p.Y > 1 + BoundsSlack) return;

            long cx = (long)Math.Floor(p.X / DedupEpsilon), cy = (long)Math.Floor(p.Y / DedupEpsilon);
            const double eps2 = DedupEpsilon * DedupEpsilon;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (!_bucketHead.TryGetValue(BucketKey(cx + dx, cy + dy), out int idx)) continue;
                    for (; idx >= 0; idx = _bucketNext[idx])
                    {
                        Vec2 q = _candidates[idx].Position;
                        double ex = q.X - p.X, ey = q.Y - p.Y;
                        if (ex * ex + ey * ey <= eps2) return;
                    }
                }

            long key = BucketKey(cx, cy);
            _bucketNext.Add(_bucketHead.TryGetValue(key, out int head) ? head : -1);
            _bucketHead[key] = _candidates.Count;
            _candidates.Add(new SnapCandidate(p, kind));
        }

        // Cell coordinates on the board are in about [-1, 1e7 + 1], so this packing is collision-free.
        static long BucketKey(long cx, long cy) => cx * 1_000_000_007L + cy;

        bool SignatureMatches(Board board)
        {
            if (!ReferenceEquals(board, _sigBoard) || IncludeShapeGridCrossings != _sigCrossings) return false;
            if (board == null) return true;
            if (board.GridN != _sigGridN || board.GridKind != _sigGridKind) return false;
            var layers = board.Layers;
            if (layers.Count != _sigLayers.Count) return false;
            for (int i = 0; i < layers.Count; i++)
                if (!_sigLayers[i].Matches(layers[i])) return false;
            return true;
        }

        void RecordSignature(Board board)
        {
            _sigBoard = board;
            _sigCrossings = IncludeShapeGridCrossings;
            _sigLayers.Clear();
            if (board == null) return;
            _sigGridN = board.GridN;
            _sigGridKind = board.GridKind;
            for (int i = 0; i < board.Layers.Count; i++)
            {
                var l = board.Layers[i];
                // A null layer gets an Id that never matches a real one, so it always reads as changed.
                _sigLayers.Add(l != null ? new LayerSig(l) : default);
            }
        }
    }
}
