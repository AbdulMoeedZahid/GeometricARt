using System;
using GeoART.Contracts;

namespace GeoART.Engine
{
    // OWNER: snapping (E2). Implements the C0 hooks declared in EditorSession.cs and the snap query the tools use.
    /// <summary>
    /// Snapping for the Line, Circle and transform tools (plan 3.3, ui-line-plan 4).
    ///   Candidates   <see cref="SnapIndex"/> owns the list; it is synced lazily (<see cref="EnsureCandidates"/>) and
    ///                marked dirty on every model change, so it is never rebuilt per pointer Move.
    ///   Snap rule    project the pointer and every candidate with <see cref="SnapMetric"/> (screen px, radius
    ///                <see cref="ISnapMetric.TolerancePx"/>, 32 reference px on device); without a metric, or when the
    ///                pointer is not projectable, compare in board units with <see cref="FallbackSnapTolerance"/>. Only
    ///                candidates within <see cref="BoardCap"/> on the board count. The nearest within the tolerance wins;
    ///                equal distance goes to the higher priority (<see cref="SnapIndex.Priority"/>), then the earlier
    ///                candidate. Tie window: a grid filler (cell corner, edge midpoint, cell centre) loses to a shape or
    ///                construction point no more than <see cref="TieWindowFraction"/> x tol farther. The snapped point is
    ///                the candidate's position itself (== holds).
    ///   Sticky       plain Line/Circle drags: after a snap to C the point stays on C while the pointer is within
    ///                <see cref="StickyFactor"/> x tol and BoardCap of C, unless another candidate in range is nearer by
    ///                more than <see cref="StickyMarginFraction"/> x tol. The gesture's own start is never sticky. Reset
    ///                per gesture; dropped when snapping goes off, the H/V lock goes on, or C stops being a candidate.
    ///   Feedback     <see cref="SnapAcquired"/> on each new acquisition (a snapped Down, or a different candidate).
    ///   Taps         tap slop is <see cref="TapSlopFraction"/> times the tolerance, in the same space.
    ///   H/V lock     (E3) <see cref="TryOrthoEnd"/>: the Line end constrained to the anchor's board X or Y axis, with
    ///                alignment snap along that axis in the same tolerance space and the same BoardCap (no sticky).
    /// </summary>
    public sealed partial class EditorSession
    {
        /// <summary>Radius of the hover ring and the pending tap-to-connect ring, in board units.</summary>
        public const double HoverRingRadius = 0.012;

        /// <summary>
        /// Tap slop as a fraction of the snap tolerance (10 reference px of 32). A gesture whose pointer never moves this
        /// far from its Down position (screen px with a metric, board units without) is a tap.
        /// </summary>
        public const double TapSlopFraction = 10.0 / 32.0;

        /// <summary>
        /// Lower bound of <see cref="BoardCap"/> in board units (0.03 = 1.2 cm on the 40 cm board), and the cap whenever
        /// there is no metric or a projection fails. The board-unit fallback tolerance (0.02) is inside it.
        /// </summary>
        public const double SnapCapFloor = 0.03;
        /// <summary>The cap's ceiling is max(this, half a tile), so a far, tiny board never snaps across tiles.</summary>
        public const double SnapCapCeilingMin = 0.10;
        /// <summary>BoardCap = this x the board reach of the screen circle along the most-stretched screen direction.</summary>
        public const double SnapCapStretch = 1.5;
        /// <summary>Probe step (board units) for the board-to-screen Jacobian.</summary>
        public const double SnapCapProbe = 1e-3;
        /// <summary>Grid fillers lose to a shape/construction point at most this x tol farther (6 of 32 px).</summary>
        public const double TieWindowFraction = 6.0 / 32.0;
        /// <summary>Sticky release radius, x tol (48 of 32 px).</summary>
        public const double StickyFactor = 1.5;
        /// <summary>A sticky snap switches only to a candidate nearer by more than this x tol (4 of 32 px).</summary>
        public const double StickyMarginFraction = 4.0 / 32.0;

        /// <summary>
        /// Raised when a gesture acquires a new snap candidate: a Down that snaps, or a different candidate during a
        /// Line/Circle drag or a Move/Scale/Duplicate drag (after its clamp). Not for H/V alignment, Select, raw points or
        /// snapping off. Leaving every candidate and coming back counts as a new acquisition. Concrete (not on
        /// IEditorSession), like <see cref="SnapMetric"/>; drives the ring pop and the haptic tick.
        /// </summary>
        public event Action<SnapCandidate> SnapAcquired;

        /// <summary>The hover ring shape the tools put in Preview while the point is snapped (the renderer pops it).</summary>
        public Shape HoverRing => _hoverRing;

        readonly SnapIndex _snapIndex = new SnapIndex();

        // Sticky snap of the open Line/Circle gesture, and the last acquired candidate (SnapAcquired).
        bool _stickyOn, _stickyRecheck, _acqOn, _resolveDirty;
        SnapCandidate _sticky, _acq;

        partial void EnsureCandidates()
        {
            if (!_snapIndex.Sync(_board)) return;
            _candidates.Clear();
            _candidates.AddRange(_snapIndex.Candidates);   // only on rebuild
            _stickyRecheck = true;                          // the sticky candidate may be gone (layer hidden, shape moved)
        }

        static bool SameCandidate(in SnapCandidate a, in SnapCandidate b) => a.Position == b.Position && a.Kind == b.Kind;

        static bool IsGridFiller(SnapKind k) =>
            k == SnapKind.CellCorner || k == SnapKind.CellEdgeMid || k == SnapKind.CellCenter;

        /// <summary>Raises <see cref="SnapAcquired"/> when <paramref name="hit"/> is a new candidate; a miss forgets the last one.</summary>
        void NoteSnap(bool hit, in SnapCandidate c)
        {
            if (!hit) { _acqOn = false; return; }
            if (_acqOn && SameCandidate(_acq, c)) return;
            _acqOn = true;
            _acq = c;
            SnapAcquired?.Invoke(c);
        }

        /// <summary>Clears the sticky snap and the acquisition memory (every new gesture and every gesture end).</summary>
        void ResetSnapFeel()
        {
            _stickyOn = false;
            _acqOn = false;
        }

        // SnapIndex's signature cannot see edits that keep every count (a moved shape), so every model change dirties it.
        // An open gesture re-resolves its point at the next pointer event even if the pointer did not move (a dot it is
        // held on may have been hidden), so an Up at the last Move position never commits onto a vanished candidate.
        partial void OnModelChanged(BoardChangeKind kind)
        {
            _snapIndex.MarkDirty();
            _resolveDirty = true;
        }

        partial void OnSnapSettingsChanged()
        {
            if (!_snapEnabled) _stickyOn = false;   // Snap off: the next point is raw
            if (!_snapEnabled && DropPending()) RaisePreviewChanged();
            if (_orthoLock != _orthoLockSeen)
            {
                _orthoLockSeen = _orthoLock;
                OnOrthoLockToggled();   // E3: an open Line gesture re-tracks at once
            }
        }

        /// <summary>The measuring space of one snap query: the pointer, its projection, tolerance and BoardCap.</summary>
        struct SnapSpace
        {
            public Vec2 P;              // pointer, board units
            public ISnapMetric Metric;  // null: board units
            public Vec2 Origin;         // pointer in the measuring space
            public double Tol, Cap;
        }

        /// <summary>
        /// Adaptive board-distance cap at <paramref name="p"/> (board units): clamp(1.5 tol / sigma_max, 0.03, ceiling),
        /// where sigma_max is the largest singular value of the board-to-screen Jacobian (px per board unit along the
        /// most-stretched direction), so tol / sigma_max is the shortest board reach of the screen circle and the cap
        /// limits the reach into the compressed depth direction of a grazing view to 1.5x that. Looking straight down it
        /// never binds. The ceiling is max(0.10, half a tile: 1/GridN square, the hex radius hex). Without a metric, or
        /// when any of the three projections fails, the cap is <see cref="SnapCapFloor"/>.
        /// </summary>
        public double BoardCap(Vec2 p)
        {
            ISnapMetric metric = SnapMetric;
            if (metric == null || !p.IsFinite || !metric.TryToScreen(p, out Vec2 ps) || !ps.IsFinite) return SnapCapFloor;
            return CapFrom(metric, p, ps, metric.TolerancePx);
        }

        double CapFrom(ISnapMetric metric, Vec2 p, Vec2 ps, double tol)
        {
            const double e = SnapCapProbe;
            if (!metric.TryToScreen(new Vec2(p.X + e, p.Y), out Vec2 px) || !px.IsFinite) return SnapCapFloor;
            if (!metric.TryToScreen(new Vec2(p.X, p.Y + e), out Vec2 py) || !py.IsFinite) return SnapCapFloor;
            double j1x = (px.X - ps.X) / e, j1y = (px.Y - ps.Y) / e;
            double j2x = (py.X - ps.X) / e, j2y = (py.Y - ps.Y) / e;
            double t = j1x * j1x + j1y * j1y + j2x * j2x + j2y * j2y;
            double d = j1x * j2y - j1y * j2x;
            double sigma = Math.Sqrt((t + Math.Sqrt(Math.Max(0, t * t - 4 * d * d))) * 0.5);
            if (!(sigma > 0) || double.IsInfinity(sigma) || !(tol > 0)) return SnapCapFloor;
            int n = TileGrid.ClampN(_board.GridN);
            double tile = _board.GridKind == GridKind.Hex ? TileGrid.HexRadius(n) : 1.0 / n;
            double ceiling = Math.Max(SnapCapCeilingMin, 0.5 * tile);
            double cap = SnapCapStretch * tol / sigma;
            return cap < SnapCapFloor ? SnapCapFloor : cap > ceiling ? ceiling : cap;
        }

        /// <summary>
        /// Sets up a query at <paramref name="p"/> (one projection of the pointer and one cap). False when snapping is
        /// off or the pointer is not finite. Refreshes the candidate list.
        /// </summary>
        bool PrepareSnap(Vec2 p, out SnapSpace s)
        {
            s = default;
            if (!_snapEnabled || !p.IsFinite) return false;
            EnsureCandidates();
            s.P = p;
            s.Origin = p;
            s.Tol = FallbackSnapTolerance;
            s.Cap = SnapCapFloor;
            ISnapMetric metric = SnapMetric;
            if (metric != null && metric.TryToScreen(p, out Vec2 ps) && ps.IsFinite)
            {
                s.Metric = metric;
                s.Origin = ps;
                s.Tol = metric.TolerancePx;
                s.Cap = CapFrom(metric, p, ps, s.Tol);
            }
            return true;
        }

        /// <summary>Squared distance from the pointer to <paramref name="q"/> in the query's space, if within BoardCap and projectable.</summary>
        static bool Distance2(in SnapSpace s, Vec2 q, out double d2)
        {
            d2 = 0;
            double bx = q.X - s.P.X, by = q.Y - s.P.Y;   // board cap first (also skips most projections)
            if (!(bx * bx + by * by <= s.Cap * s.Cap)) return false;
            if (s.Metric != null && (!s.Metric.TryToScreen(q, out q) || !q.IsFinite)) return false;
            double dx = q.X - s.Origin.X, dy = q.Y - s.Origin.Y;
            d2 = dx * dx + dy * dy;
            return true;
        }

        /// <summary>
        /// Nearest candidate within the tolerance and BoardCap (exact ties by priority, then list order), with the tie
        /// window: a winning grid filler gives way to the nearest shape/construction point at most
        /// <see cref="TieWindowFraction"/> x tol farther. <paramref name="dist"/> is the winner's distance.
        /// </summary>
        bool Scan(in SnapSpace s, out SnapCandidate hit, out double dist)
        {
            hit = default;
            dist = 0;
            var list = _candidates;
            double tol2 = s.Tol * s.Tol;
            int best = -1, bestPriority = 0, other = -1, otherPriority = 0;
            double bestD2 = 0, otherD2 = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!Distance2(s, list[i].Position, out double d2) || !(d2 <= tol2)) continue;
                int priority = SnapIndex.Priority(list[i].Kind);
                if (best < 0 || d2 < bestD2 || (d2 == bestD2 && priority < bestPriority))
                {
                    best = i;
                    bestD2 = d2;
                    bestPriority = priority;
                }
                if (!IsGridFiller(list[i].Kind) && (other < 0 || d2 < otherD2 || (d2 == otherD2 && priority < otherPriority)))
                {
                    other = i;
                    otherD2 = d2;
                    otherPriority = priority;
                }
            }
            if (best < 0) return false;
            if (other >= 0 && other != best && IsGridFiller(list[best].Kind)
                && Math.Sqrt(otherD2) - Math.Sqrt(bestD2) <= TieWindowFraction * s.Tol)
            {
                best = other;
                bestD2 = otherD2;
            }
            hit = list[best];
            dist = Math.Sqrt(bestD2);
            return true;
        }

        /// <summary>
        /// Nearest snap candidate to <paramref name="p"/> (see <see cref="Scan"/>), or false (snapping off, nothing in
        /// range). Allocation-free once the candidate list is up to date. No sticky; callers raise SnapAcquired.
        /// </summary>
        bool TrySnap(Vec2 p, out SnapCandidate hit)
        {
            hit = default;
            return PrepareSnap(p, out SnapSpace s) && Scan(s, out hit, out _);
        }

        /// <summary>
        /// The plain Line/Circle snap during a drag, with the sticky rule (class summary). One pointer projection and one
        /// cap per call, shared by the scan and the sticky test. The candidate at the gesture's start (<c>_anchor</c>) never
        /// becomes sticky, so a short line or a small circle can still be drawn from a snapped start.
        /// </summary>
        bool TrySnapTracking(Vec2 raw, out SnapCandidate hit)
        {
            hit = default;
            if (!PrepareSnap(raw, out SnapSpace s))
            {
                _stickyOn = false;
                return false;
            }
            bool found = Scan(s, out hit, out double d);
            if (_stickyOn)
            {
                bool valid = true;
                if (_stickyRecheck)
                {
                    valid = IsCandidate(_sticky);   // e.g. its layer was hidden mid-drag
                    _stickyRecheck = false;
                }
                double ds2 = 0;
                valid = valid && Distance2(s, _sticky.Position, out ds2);
                double ds = Math.Sqrt(ds2);
                if (valid && ds <= StickyFactor * s.Tol)
                {
                    if (!(found && !SameCandidate(hit, _sticky) && d < ds - StickyMarginFraction * s.Tol))
                    {
                        hit = _sticky;
                        return true;
                    }
                }
                else _stickyOn = false;
            }
            if (found && hit.Position != _anchor)
            {
                _sticky = hit;
                _stickyOn = true;
                _stickyRecheck = false;
            }
            return found;
        }

        bool IsCandidate(in SnapCandidate c)
        {
            var list = _candidates;
            for (int i = 0; i < list.Count; i++)
                if (SameCandidate(list[i], c)) return true;
            return false;
        }

        // ---------- H/V lock (E3) ----------

        bool _orthoLockSeen;   // the OrthoLock value OnSnapSettingsChanged last saw (detects a lock toggle)

        /// <summary>
        /// H/V lock end point for the raw pointer <paramref name="raw"/>, relative to the gesture's <c>_anchor</c>.
        /// The dominant axis of raw - anchor (board units, |dx| &gt;= |dy| is horizontal) fixes the axis; the end copies the
        /// anchor's perpendicular coordinate exactly and takes the pointer's along-axis coordinate clamped to [0, 1]. With
        /// snapping on it then aligns along the axis (see <see cref="OrthoSearch"/>): (a) candidates whose perpendicular
        /// distance to the axis line is within the tolerance offer their own along-axis coordinate; (b) crossings of the
        /// axis line with visible lines, polygon edges and circles offer theirs. The nearest offer within the tolerance
        /// of the unsnapped end wins. Returns true if the end snapped. Allocation-free.
        /// </summary>
        bool TryOrthoEnd(Vec2 raw, out Vec2 end)
        {
            Vec2 a = _anchor;
            double dx = raw.X - a.X, dy = raw.Y - a.Y;
            bool horizontal = !(Math.Abs(dx) < Math.Abs(dy));   // tie goes horizontal
            double along = Clamp01(horizontal ? raw.X : raw.Y);
            end = OnAxis(horizontal, a, along);
            if (!_snapEnabled) return false;
            EnsureCandidates();

            // Measuring space: screen px when the unsnapped end and a second axis point project, else board units.
            var f = new OrthoSearch { Horizontal = horizontal, Anchor = a, S0 = end, Tol = FallbackSnapTolerance, RawAlong = along };
            f.AxisDir = horizontal ? new Vec2(1, 0) : new Vec2(0, 1);
            ISnapMetric metric = SnapMetric;
            if (metric != null && metric.TryToScreen(end, out Vec2 e0) && e0.IsFinite)
            {
                // A projective map sends the axis line to a screen line; any second point on it fixes the direction.
                // The farther board edge is at least 0.5 board units away, so the direction is well conditioned.
                Vec2 far = OnAxis(horizontal, a, along <= 0.5 ? 1.0 : 0.0);
                if (TryScreenDirection(metric, far, e0, out Vec2 u) || TryScreenDirection(metric, a, e0, out u))
                {
                    f.Metric = metric;
                    f.S0 = e0;
                    f.AxisDir = u;
                    f.Tol = metric.TolerancePx;
                }
            }
            f.Tol2 = f.Tol * f.Tol;
            f.Cap = f.Metric != null ? CapFrom(f.Metric, end, f.S0, f.Tol) : SnapCapFloor;   // BoardCap at the unsnapped end

            // (a) Candidates near the axis line contribute their along-axis coordinate.
            var list = _candidates;
            for (int i = 0; i < list.Count; i++)
            {
                Vec2 q = list[i].Position;
                double qa = horizontal ? q.X : q.Y;
                if (!(qa >= 0 && qa <= 1)) continue;
                double perpBoard = horizontal ? q.Y - a.Y : q.X - a.X;   // board-distance cap, perpendicular part
                if (!(Math.Abs(perpBoard) <= f.Cap)) continue;
                if (f.Metric != null && (!f.Metric.TryToScreen(q, out q) || !q.IsFinite)) continue;
                double perp = Math.Abs(f.AxisDir.X * (q.Y - f.S0.Y) - f.AxisDir.Y * (q.X - f.S0.X));
                if (!(perp <= f.Tol)) continue;
                f.Offer(qa, SnapIndex.Priority(list[i].Kind));
            }

            // (b) Crossings of the axis line with visible shapes (same visibility rule as SnapIndex).
            Vec2 dir = horizontal ? new Vec2(1, 0) : new Vec2(0, 1);
            int pri = SnapIndex.Priority(SnapKind.Intersection);
            var layers = _board.Layers;
            for (int li = 0; li < layers.Count; li++)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = 0; si < shapes.Count; si++)
                {
                    Vec2 p0, p1;
                    switch (shapes[si])
                    {
                        case LineShape line:
                            if (Intersect.LineSegment(a, dir, line.A, line.B, out p0) == 1) f.OfferHit(p0, pri);
                            break;
                        case CircleShape circle:
                            int n = Intersect.LineCircle(a, dir, circle.Center, circle.Radius, out p0, out p1);
                            if (n >= 1) f.OfferHit(p0, pri);
                            if (n >= 2) f.OfferHit(p1, pri);
                            break;
                        case PolygonShape poly:
                            if (!(poly.Sides >= 3 && poly.Radius > 0 && !double.IsInfinity(poly.Radius) && poly.Center.IsFinite
                                  && !double.IsNaN(poly.RotationRad) && !double.IsInfinity(poly.RotationRad))) break;
                            Vec2 first = SnapIndex.PolygonVertex(poly, 0), prev = first;
                            for (int k = 1; k <= poly.Sides; k++)
                            {
                                Vec2 v = k == poly.Sides ? first : SnapIndex.PolygonVertex(poly, k);
                                if (Intersect.LineSegment(a, dir, prev, v, out p0) == 1) f.OfferHit(p0, pri);
                                prev = v;
                            }
                            break;
                    }
                }
            }

            if (!f.Found) return false;
            end = OnAxis(horizontal, a, f.BestAlong);
            return true;
        }

        /// <summary>The point on the anchor's axis at <paramref name="along"/>; the other coordinate is the anchor's own.</summary>
        static Vec2 OnAxis(bool horizontal, Vec2 anchor, double along) =>
            horizontal ? new Vec2(along, anchor.Y) : new Vec2(anchor.X, along);

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>Unit screen direction from <paramref name="s0"/> to the projection of <paramref name="p"/>, if any.</summary>
        static bool TryScreenDirection(ISnapMetric metric, Vec2 p, Vec2 s0, out Vec2 unit)
        {
            unit = default;
            if (!metric.TryToScreen(p, out Vec2 s1) || !s1.IsFinite) return false;
            double ux = s1.X - s0.X, uy = s1.Y - s0.Y;
            double len = Math.Sqrt(ux * ux + uy * uy);
            if (!(len > 1e-9) || double.IsInfinity(len)) return false;
            unit = new Vec2(ux / len, uy / len);
            return true;
        }

        /// <summary>
        /// One H/V lock alignment search (a struct, so a Move allocates nothing). Distances are measured from
        /// <see cref="S0"/>, the unsnapped end, in screen px when <see cref="Metric"/> is set, else in board units.
        /// </summary>
        struct OrthoSearch
        {
            public bool Horizontal;
            public Vec2 Anchor;
            public ISnapMetric Metric;   // null: board units
            public Vec2 S0;              // the unsnapped end, in the measuring space
            public Vec2 AxisDir;         // unit direction of the axis line, in the measuring space
            public double Tol, Tol2;
            public double RawAlong;      // the unsnapped end's along-axis coordinate (board units), for the board cap
            public double Cap;           // BoardCap at the unsnapped end
            public bool Found;
            public double BestAlong, BestD2;
            public int BestPriority;

            /// <summary>An axis crossing: only its along-axis coordinate is used; off-board crossings are ignored.</summary>
            public void OfferHit(Vec2 p, int priority)
            {
                double along = Horizontal ? p.X : p.Y;
                if (along >= 0 && along <= 1) Offer(along, priority);
            }

            /// <summary>
            /// Considers the axis point at <paramref name="along"/>: within <see cref="Cap"/> of the
            /// unsnapped end on the board, then nearest to S0 within Tol, then priority, then first.
            /// </summary>
            public void Offer(double along, int priority)
            {
                if (!(Math.Abs(along - RawAlong) <= Cap)) return;
                Vec2 q = OnAxis(Horizontal, Anchor, along);
                if (Metric != null && (!Metric.TryToScreen(q, out q) || !q.IsFinite)) return;
                double dx = q.X - S0.X, dy = q.Y - S0.Y;
                double d2 = dx * dx + dy * dy;
                if (!(d2 <= Tol2)) return;
                if (Found && !(d2 < BestD2 || (d2 == BestD2 && priority < BestPriority))) return;
                Found = true;
                BestAlong = along;
                BestD2 = d2;
                BestPriority = priority;
            }
        }

        /// <summary>Snap tolerance and the space it is measured in for a pointer at <paramref name="p"/>.</summary>
        bool TryScreenSpace(Vec2 p, out Vec2 screen, out double tolerance)
        {
            ISnapMetric metric = SnapMetric;
            if (metric != null && metric.TryToScreen(p, out screen) && screen.IsFinite)
            {
                tolerance = metric.TolerancePx;
                return true;
            }
            screen = p;
            tolerance = FallbackSnapTolerance;
            return false;
        }
    }
}
