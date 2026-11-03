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
