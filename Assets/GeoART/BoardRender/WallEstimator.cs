using System.Collections.Generic;
using UnityEngine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Finds a wall (any vertical flat surface: wall, projector screen, whiteboard, board held upright) under the
    /// crosshair from AR feature points, before ARKit/ARCore have classified it as a vertical plane. Plane detection
    /// finds walls much later and less often than tables, so this is what makes wall placement start reliably.
    ///
    /// The owner feeds feature points (with their stable identifiers) every frame and asks for an estimate along the
    /// screen-centre ray. Points in a cone around the ray are fitted with a vertical plane (a line in world XZ: RANSAC
    /// over point pairs, then least squares on the inliers), and the result must be flat, wide and tall enough, and
    /// faced rather than grazed, so floors,
    /// table tops and clutter are rejected. Results are low-pass filtered so the aim does not jitter.
    /// Only vertical surfaces are ever returned: tables keep using detected planes exactly as before.
    ///
    /// Plain walls (white paint, blank screens) have feature points only at their edges, sockets, frames and marks. Two
    /// things make them usable: a wide-cone second pass when the narrow cone has too few points, and remembered walls:
    /// every confirmed wall is kept (plane + where its points were) and the crosshair stays on its plain parts, up to
    /// RememberReach beyond the points that found it. So: aim at any textured spot on the wall once, then move.
    /// Not a MonoBehaviour and no AR Foundation, so it is tested in EditMode with synthetic points.
    /// </summary>
    public sealed class WallEstimator
    {
        /// <summary>Feature points older than this (seconds since last seen) are forgotten.</summary>
        public const float PointLifetime = 1.5f;
        public const int MaxStoredPoints = 4096, MaxFitPoints = 512, MinFitPoints = 12;

        /// <summary>Points within max(MinConeRadius, ConeSlope * depth) of the ray are used.</summary>
        public const float MinConeRadius = 0.06f, ConeSlope = 0.18f;
        public const float MinDepth = 0.15f, MaxDepth = 2.5f;
        /// <summary>RANSAC inlier band, and flatness limit of the final fit (metres).</summary>
        public const float InlierDistance = 0.02f, MaxRmsResidual = 0.012f;
        public const int RansacIterations = 48;
        /// <summary>Inliers must span this much along the wall and vertically (rejects posts, edges and floor strips).</summary>
        public const float MinSpan = 0.10f;
        public const float MinInlierFraction = 0.5f;

        /// <summary>Second pass for sparse (plain) walls: a wider cone, and a lower inlier fraction (flatness and span checks still apply).</summary>
        public const float WideMinConeRadius = 0.15f, WideConeSlope = 0.45f, WideMinInlierFraction = 0.35f;

        /// <summary>Remembered walls: how far beyond their supporting points the plane is still used (along, vertical), metres.</summary>
        public const float RememberReach = 1.5f, RememberReachVertical = 1.2f;
        /// <summary>Remembered walls not re-confirmed for this long are forgotten (seconds).</summary>
        public const float RememberSeconds = 60f;
        public const int MaxRememberedWalls = 4;
        /// <summary>The view ray must be within about 66 degrees of the wall normal (rejects grazing floors).</summary>
        public const float MinFacingCos = 0.4f;
        /// <summary>A measured normal counts as a wall if it is within about 20 degrees of horizontal.</summary>
        public const float MaxWallNormalUpDot = 0.342f;

        // Filter: blend factor per estimate, and when a new estimate is a different surface (no blending).
        const float FilterBlend = 0.25f, FilterResetSeconds = 0.5f, FilterJumpDistance = 0.05f;
        static readonly float s_FilterJumpCos = Mathf.Cos(10f * Mathf.Deg2Rad);

        struct Stored
        {
            public Vector3 Position;
            public float Time;
        }

        readonly Dictionary<ulong, Stored> _points = new Dictionary<ulong, Stored>(MaxStoredPoints);
        readonly List<ulong> _expired = new List<ulong>(256);
        readonly Vector3[] _fit = new Vector3[MaxFitPoints];

        struct Wall
        {
            public Vector3 Origin, Normal;
            public float MinA, MaxA, MinY, MaxY, LastSeen;
            public Vector3 Along => new Vector3(-Normal.z, 0f, Normal.x);
        }

        readonly Wall[] _walls = new Wall[MaxRememberedWalls];
        int _wallCount;

        bool _hasFiltered;
        Vector3 _filteredNormal;
        float _filteredOffset, _filteredTime;

        public int StoredPointCount => _points.Count;
        public int RememberedWallCount => _wallCount;

        /// <summary>Forgets points, remembered walls and the filter (tracking lost, reset, placed).</summary>
        public void Clear()
        {
            _points.Clear();
            _wallCount = 0;
            _hasFiltered = false;
        }

        /// <summary>
        /// Everything in one call: fit at the crosshair (narrow, then wide cone) and remember it; else bridge a short gap;
        /// else a remembered wall's plane under the crosshair. Filtered over time. False if no wall is under the crosshair.
        /// </summary>
        public bool TryEstimate(Ray ray, float now, float holdSeconds, out Vector3 hit, out Vector3 normal)
        {
            if (TryFit(ray, out Vector3 point, out Vector3 n, out float minA, out float maxA, out float minY, out float maxY))
            {
                Remember(point, n, minA, maxA, minY, maxY, now);
                return Filter(point, n, ray, now, out hit, out normal);
            }
            if (TryHold(ray, now, holdSeconds, out hit, out normal)) return true;
            if (TryRemembered(ray, now, out point, out n)) return Filter(point, n, ray, now, out hit, out normal);
            return false;
        }

        /// <summary>Adds or updates a feature point (world space). Ignored when the store is full.</summary>
        public void Observe(ulong id, Vector3 world, float time)
        {
            if (!IsFinite(world)) return;
            if (_points.Count >= MaxStoredPoints && !_points.ContainsKey(id)) return;
            _points[id] = new Stored { Position = world, Time = time };
        }

        /// <summary>Forgets points not seen for <see cref="PointLifetime"/>.</summary>
        public void Prune(float now)
        {
            _expired.Clear();
            foreach (var kv in _points)
                if (now - kv.Value.Time > PointLifetime) _expired.Add(kv.Key);
            for (int i = 0; i < _expired.Count; i++) _points.Remove(_expired[i]);
        }

        /// <summary>
        /// Snaps a measured surface normal to an exact wall normal (horizontal, facing <paramref name="viewer"/>).
        /// False if the normal is not within about 20 degrees of horizontal.
        /// </summary>
        public static bool TrySnapWallNormal(Vector3 normal, Vector3 pointOnSurface, Vector3 viewer, out Vector3 wallNormal)
        {
            wallNormal = default;
            if (normal.sqrMagnitude < 1e-8f) return false;
            Vector3 n = normal.normalized;
            if (Mathf.Abs(n.y) > MaxWallNormalUpDot) return false;
            n.y = 0f;
            if (n.sqrMagnitude < 1e-6f) return false;
            n.Normalize();
            if (Vector3.Dot(n, viewer - pointOnSurface) < 0f) n = -n;
            wallNormal = n;
            return true;
        }

        /// <summary>Raw wall fit from the stored feature points around <paramref name="ray"/> (no filtering, no memory).</summary>
        public bool TryFit(Ray ray, out Vector3 planePoint, out Vector3 normal) =>
            TryFit(ray, out planePoint, out normal, out _, out _, out _, out _);

        /// <summary>Narrow cone first; if that fails, the wide cone for sparse walls. Also returns the inliers' extents.</summary>
        public bool TryFit(Ray ray, out Vector3 planePoint, out Vector3 normal, out float minA, out float maxA, out float minY, out float maxY) =>
            TryFitCone(ray, MinConeRadius, ConeSlope, MinInlierFraction, out planePoint, out normal, out minA, out maxA, out minY, out maxY) ||
            TryFitCone(ray, WideMinConeRadius, WideConeSlope, WideMinInlierFraction, out planePoint, out normal, out minA, out maxA, out minY, out maxY);

        bool TryFitCone(Ray ray, float minRadius, float slope, float minFraction, out Vector3 planePoint, out Vector3 normal,
                        out float minA, out float maxA, out float minY, out float maxY)
        {
            planePoint = normal = default;
            minA = maxA = minY = maxY = 0f;
            Vector3 dir = ray.direction.normalized;

            int n = 0;
            foreach (var kv in _points)
            {
                Vector3 v = kv.Value.Position - ray.origin;
                float t = Vector3.Dot(v, dir);
                if (t < MinDepth || t > MaxDepth) continue;
                float radius = Mathf.Max(minRadius, slope * t);
                if ((v - dir * t).sqrMagnitude > radius * radius) continue;
                _fit[n++] = kv.Value.Position;
                if (n == MaxFitPoints) break;
            }
            if (n < MinFitPoints) return false;

            // RANSAC in XZ: the line through a pair of points that has the most points within InlierDistance.
            // Deterministic pseudo-random pairs, so results are reproducible.
            uint state = 2463534242u;
            int bestCount = 0;
            float bestNx = 0f, bestNz = 0f, bestD = 0f;
            for (int it = 0; it < RansacIterations; it++)
            {
                int i = (int)(Next(ref state) % (uint)n), j = (int)(Next(ref state) % (uint)n);
                float dx = _fit[j].x - _fit[i].x, dz = _fit[j].z - _fit[i].z;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 0.05f) continue;
                float nx = -dz / len, nz = dx / len, d = nx * _fit[i].x + nz * _fit[i].z;
                int count = 0;
                for (int k = 0; k < n; k++)
                    if (Mathf.Abs(nx * _fit[k].x + nz * _fit[k].z - d) <= InlierDistance) count++;
                if (count > bestCount) { bestCount = count; bestNx = nx; bestNz = nz; bestD = d; }
            }
            if (bestCount < MinFitPoints || bestCount < minFraction * n) return false;

            int inliers = 0;
            for (int k = 0; k < n; k++)
                if (Mathf.Abs(bestNx * _fit[k].x + bestNz * _fit[k].z - bestD) <= InlierDistance) _fit[inliers++] = _fit[k];
            if (!FitVertical(_fit, inliers, out Vector3 mean, out Vector3 nrm)) return false;

            // Flat, wide and tall enough.
            Vector3 along = new Vector3(-nrm.z, 0f, nrm.x);
            float sumR2 = 0f;
            minA = minY = float.MaxValue;
            maxA = maxY = float.MinValue;
            for (int i = 0; i < inliers; i++)
            {
                Vector3 d = _fit[i] - mean;
                float r = Vector3.Dot(d, nrm);
                sumR2 += r * r;
                float a = Vector3.Dot(d, along);
                if (a < minA) minA = a;
                if (a > maxA) maxA = a;
                if (_fit[i].y < minY) minY = _fit[i].y;
                if (_fit[i].y > maxY) maxY = _fit[i].y;
            }
            if (Mathf.Sqrt(sumR2 / inliers) > MaxRmsResidual) return false;
            if (maxA - minA < MinSpan || maxY - minY < MinSpan) return false;

            if (Vector3.Dot(nrm, ray.origin - mean) < 0f)   // face the viewer (along flips with it: keep extents consistent)
            {
                nrm = -nrm;
                float a0 = minA;
                minA = -maxA;
                maxA = -a0;
            }
            if (-Vector3.Dot(dir, nrm) < MinFacingCos) return false;      // grazing view: a floor strip, not a wall
            minY -= mean.y;
            maxY -= mean.y;

            planePoint = mean;
            normal = nrm;
            return true;
        }

        /// <summary>
        /// Low-pass filters a wall plane over time and intersects <paramref name="ray"/> with the filtered plane.
        /// A different surface (more than 10 degrees or 5 cm away) or a gap of 0.5 s restarts the filter.
        /// </summary>
        public bool Filter(Vector3 planePoint, Vector3 normal, Ray ray, float now, out Vector3 hit, out Vector3 filteredNormal)
        {
            hit = filteredNormal = default;
            Vector3 n = normal.normalized;
            float offset = Vector3.Dot(n, planePoint);

            bool restart = !_hasFiltered || now - _filteredTime > FilterResetSeconds ||
                           Vector3.Dot(n, _filteredNormal) < s_FilterJumpCos ||
                           Mathf.Abs(offset - _filteredOffset) > FilterJumpDistance;
            if (restart)
            {
                _filteredNormal = n;
                _filteredOffset = offset;
            }
            else
            {
                Vector3 blended = Vector3.Lerp(_filteredNormal, n, FilterBlend);
                blended.y = 0f;   // stays an exact wall normal
                _filteredNormal = blended.normalized;
                _filteredOffset = Mathf.Lerp(_filteredOffset, offset, FilterBlend);
            }
            _hasFiltered = true;
            _filteredTime = now;
            return IntersectFiltered(ray, out hit, out filteredNormal);
        }

        /// <summary>
        /// Bridges short gaps (a frame or two with too few points): intersects <paramref name="ray"/> with the filtered
        /// plane if it was updated within <paramref name="maxAge"/> seconds. Does not refresh the filter's age.
        /// </summary>
        public bool TryHold(Ray ray, float now, float maxAge, out Vector3 hit, out Vector3 filteredNormal)
        {
            hit = filteredNormal = default;
            if (!_hasFiltered || now - _filteredTime > maxAge) return false;
            return IntersectFiltered(ray, out hit, out filteredNormal);
        }

        bool IntersectFiltered(Ray ray, out Vector3 hit, out Vector3 filteredNormal)
        {
            hit = filteredNormal = default;
            Vector3 dir = ray.direction.normalized;
            float denom = Vector3.Dot(dir, _filteredNormal);
            if (Mathf.Abs(denom) < 1e-4f) return false;
            float t = (_filteredOffset - Vector3.Dot(_filteredNormal, ray.origin)) / denom;
            if (t <= 0f || t > MaxDepth) return false;
            hit = ray.origin + dir * t;
            filteredNormal = _filteredNormal;
            return true;
        }

        /// <summary>
        /// Keeps a confirmed wall. A fit on an already remembered wall (within 10 degrees and 5 cm) refines its plane and
        /// grows its extents; otherwise it replaces the wall seen longest ago when the list is full.
        /// Extents are relative to <paramref name="origin"/>, along (-n.z, 0, n.x) and world Y.
        /// </summary>
        public void Remember(Vector3 origin, Vector3 normal, float minA, float maxA, float minY, float maxY, float now)
        {
            Vector3 n = normal.normalized;
            float offset = Vector3.Dot(n, origin);
            for (int i = 0; i < _wallCount; i++)
            {
                ref Wall w = ref _walls[i];
                if (Vector3.Dot(w.Normal, n) < s_FilterJumpCos || Mathf.Abs(Vector3.Dot(w.Normal, w.Origin) - offset) > FilterJumpDistance)
                    continue;
                // New extents as world points, re-expressed in this wall's frame.
                Vector3 along = new Vector3(-n.z, 0f, n.x), wAlong = w.Along;
                float a0 = Vector3.Dot(origin + along * minA - w.Origin, wAlong);
                float a1 = Vector3.Dot(origin + along * maxA - w.Origin, wAlong);
                float y0 = origin.y + minY - w.Origin.y, y1 = origin.y + maxY - w.Origin.y;
                w.MinA = Mathf.Min(w.MinA, Mathf.Min(a0, a1));
                w.MaxA = Mathf.Max(w.MaxA, Mathf.Max(a0, a1));
                w.MinY = Mathf.Min(w.MinY, y0);
                w.MaxY = Mathf.Max(w.MaxY, y1);
                // Refine the plane slowly; keep the origin on it so the extents stay valid.
                Vector3 blended = Vector3.Lerp(w.Normal, n, 0.2f);
                blended.y = 0f;
                Vector3 newNormal = blended.normalized;
                float newOffset = Mathf.Lerp(Vector3.Dot(w.Normal, w.Origin), offset, 0.2f);
                w.Origin += newNormal * (newOffset - Vector3.Dot(newNormal, w.Origin));
                w.Normal = newNormal;
                w.LastSeen = now;
                return;
            }

            int slot = _wallCount;
            if (_wallCount == MaxRememberedWalls)
            {
                slot = 0;
                for (int i = 1; i < _wallCount; i++)
                    if (_walls[i].LastSeen < _walls[slot].LastSeen) slot = i;
            }
            else _wallCount++;
            _walls[slot] = new Wall { Origin = origin, Normal = n, MinA = minA, MaxA = maxA, MinY = minY, MaxY = maxY, LastSeen = now };
        }

        /// <summary>
        /// The nearest remembered wall under <paramref name="ray"/>: faced (not grazed), within reach, and within
        /// RememberReach / RememberReachVertical of where its points were. Lets the crosshair sit on plain wall areas.
        /// </summary>
        public bool TryRemembered(Ray ray, float now, out Vector3 planePoint, out Vector3 normal)
        {
            planePoint = normal = default;
            Vector3 dir = ray.direction.normalized;
            float bestT = float.MaxValue;
            for (int i = 0; i < _wallCount; i++)
            {
                Wall w = _walls[i];
                if (now - w.LastSeen > RememberSeconds) continue;
                Vector3 n = Vector3.Dot(w.Normal, ray.origin - w.Origin) >= 0f ? w.Normal : -w.Normal;
                float denom = -Vector3.Dot(dir, n);
                if (denom < MinFacingCos) continue;
                float t = Vector3.Dot(w.Origin - ray.origin, n) / -denom;
                if (t < MinDepth || t > MaxDepth || t >= bestT) continue;
                Vector3 hit = ray.origin + dir * t;
                float a = Vector3.Dot(hit - w.Origin, w.Along), y = hit.y - w.Origin.y;
                if (a < w.MinA - RememberReach || a > w.MaxA + RememberReach) continue;
                if (y < w.MinY - RememberReachVertical || y > w.MaxY + RememberReachVertical) continue;
                bestT = t;
                planePoint = hit;
                normal = n;
            }
            return bestT < float.MaxValue;
        }

        /// <summary>Forgets the filtered plane (next estimate starts fresh); keeps the stored points.</summary>
        public void ResetFilter() => _hasFiltered = false;

        /// <summary>Least-squares vertical plane: 2D principal axis of the points' (x, z). Normal is horizontal.</summary>
        static bool FitVertical(Vector3[] pts, int count, out Vector3 mean, out Vector3 normal)
        {
            mean = normal = default;
            if (count < 3) return false;
            double mx = 0, my = 0, mz = 0;
            for (int i = 0; i < count; i++) { mx += pts[i].x; my += pts[i].y; mz += pts[i].z; }
            mx /= count; my /= count; mz /= count;
            double sxx = 0, sxz = 0, szz = 0;
            for (int i = 0; i < count; i++)
            {
                double dx = pts[i].x - mx, dz = pts[i].z - mz;
                sxx += dx * dx;
                sxz += dx * dz;
                szz += dz * dz;
            }
            double trace = sxx + szz;
            if (trace < 1e-10) return false;
            double theta = 0.5 * System.Math.Atan2(2.0 * sxz, sxx - szz);   // major axis (along the wall)
            mean = new Vector3((float)mx, (float)my, (float)mz);
            normal = new Vector3((float)-System.Math.Sin(theta), 0f, (float)System.Math.Cos(theta));
            return true;
        }

        static uint Next(ref uint x)   // xorshift32
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
