using System;
using GeoART.Contracts;

namespace GeoART.Engine
{
    // OWNER: snapping (E2). Implements the C0 hooks declared in EditorSession.cs and the snap query the tools use.
    /// <summary>
    /// Snapping for the Line and Circle tools (plan 3.3).
    ///   Candidates   <see cref="SnapIndex"/> owns the list; it is synced lazily (<see cref="EnsureCandidates"/>) and
    ///                marked dirty on every model change, so it is never rebuilt per pointer Move.
    ///   Snap rule    project the pointer and every candidate with <see cref="SnapMetric"/> (screen px, radius
    ///                <see cref="ISnapMetric.TolerancePx"/>); without a metric, or when the pointer is not projectable,
    ///                compare in board units with <see cref="FallbackSnapTolerance"/>. The nearest candidate within the
    ///                tolerance wins; equal distance goes to the higher priority (<see cref="SnapIndex.Priority"/>), then
    ///                to the earlier candidate. The snapped point is the candidate's position itself (== holds).
    ///   Taps         tap slop is <see cref="TapSlopFraction"/> times the tolerance, in the same space.
    ///   H/V lock     (E3) <see cref="TryOrthoEnd"/>: the Line end constrained to the anchor's board X or Y axis, with
    ///                alignment snap along that axis in the same tolerance space.
    /// </summary>
    public sealed partial class EditorSession
    {
        /// <summary>Radius of the hover ring and the pending tap-to-connect ring, in board units.</summary>
        public const double HoverRingRadius = 0.012;

        /// <summary>
        /// Tap slop as a fraction of the snap tolerance (10 reference px of 24). A gesture whose pointer never moves this
        /// far from its Down position (screen px with a metric, board units without) is a tap.
        /// </summary>
        public const double TapSlopFraction = 10.0 / 24.0;

        readonly SnapIndex _snapIndex = new SnapIndex();

        partial void EnsureCandidates()
        {
            if (!_snapIndex.Sync(_board)) return;
            _candidates.Clear();
            _candidates.AddRange(_snapIndex.Candidates);   // only on rebuild
        }

        // SnapIndex's signature cannot see edits that keep every count (a moved shape), so every model change dirties it.
        partial void OnModelChanged(BoardChangeKind kind) => _snapIndex.MarkDirty();

        partial void OnSnapSettingsChanged()
        {
            if (!_snapEnabled && DropPending()) RaisePreviewChanged();
            if (_orthoLock != _orthoLockSeen)
            {
                _orthoLockSeen = _orthoLock;
                OnOrthoLockToggled();   // E3: an open Line gesture re-tracks at once
            }
        }

        /// <summary>
        /// Nearest snap candidate to <paramref name="p"/> within the tolerance, or false (snapping off, nothing in range).
        /// Allocation-free once the candidate list is up to date.
        /// </summary>
        bool TrySnap(Vec2 p, out SnapCandidate hit)
        {
            hit = default;
            if (!_snapEnabled || !p.IsFinite) return false;
            EnsureCandidates();
            var list = _candidates;
            if (list.Count == 0) return false;

            ISnapMetric metric = SnapMetric;
            bool screen = false;
            Vec2 origin = p;
            double tol = FallbackSnapTolerance;
            if (metric != null && metric.TryToScreen(p, out Vec2 ps) && ps.IsFinite)
            {
                screen = true;
                origin = ps;
                tol = metric.TolerancePx;
            }
            double tol2 = tol * tol;

            int best = -1, bestPriority = 0;
            double bestD2 = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Vec2 q = list[i].Position;
                if (screen && (!metric.TryToScreen(q, out q) || !q.IsFinite)) continue;
                double dx = q.X - origin.X, dy = q.Y - origin.Y;
                double d2 = dx * dx + dy * dy;
                if (!(d2 <= tol2)) continue;
                int priority = SnapIndex.Priority(list[i].Kind);
                if (best < 0 || d2 < bestD2 || (d2 == bestD2 && priority < bestPriority))
                {
                    best = i;
                    bestD2 = d2;
                    bestPriority = priority;
                }
            }
            if (best < 0) return false;
            hit = list[best];
            return true;
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
            var f = new OrthoSearch { Horizontal = horizontal, Anchor = a, S0 = end, Tol = FallbackSnapTolerance };
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

            // (a) Candidates near the axis line contribute their along-axis coordinate.
            var list = _candidates;
            for (int i = 0; i < list.Count; i++)
            {
                Vec2 q = list[i].Position;
                double qa = horizontal ? q.X : q.Y;
                if (!(qa >= 0 && qa <= 1)) continue;
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
            public bool Found;
            public double BestAlong, BestD2;
            public int BestPriority;

            /// <summary>An axis crossing: only its along-axis coordinate is used; off-board crossings are ignored.</summary>
            public void OfferHit(Vec2 p, int priority)
            {
                double along = Horizontal ? p.X : p.Y;
                if (along >= 0 && along <= 1) Offer(along, priority);
            }

            /// <summary>Considers the axis point at <paramref name="along"/>: nearest to S0 within Tol, then priority, then first.</summary>
            public void Offer(double along, int priority)
            {
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
