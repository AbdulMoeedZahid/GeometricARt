using GeoART.Contracts;
using UnityEngine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Corner-picking state shared by ARBoardSpace, FlatBoardSpace and the EditMode tests (CORNER_PLACEMENT_PLAN 4.3).
    /// Not a MonoBehaviour and no AR Foundation: the owner feeds it a plane and the screen-centre ray every frame.
    ///
    /// Plane 2D coordinates: n = plane normal, ex = world right projected onto the plane, ey = ex x n, origin = PlanePoint.
    /// (x, y) on the plane behaves exactly like board-local (x, z): LookRotation(ey, n) maps local +X to ex and +Z to ey,
    /// so a counter-clockwise turn in 2D is not mirrored in 3D. Corners are stored in world space and re-projected onto
    /// the current plane whenever they are read, which absorbs ARKit's plane height refinement.
    /// No allocations after construction.
    /// </summary>
    public sealed class CornerPickSession
    {
        public const int RequiredCorners = 4, SteadyFrames = 6;
        /// <summary>Ray must be within about 75 degrees of the plane normal (15 degrees from grazing).</summary>
        public const float MinAimCos = 0.26f;
        public const float MaxAimDistance = 2.5f, MaxSteadySpread = 0.008f;

        // While no corner exists, a new plane farther than this from the old one (or tilted more) is a different surface.
        const float PlaneJumpDistance = 0.02f;
        static readonly float s_PlaneJumpCos = Mathf.Cos(5f * Mathf.Deg2Rad);

        Vector3 _planePoint, _normal = Vector3.up, _ex = Vector3.right, _ey = Vector3.forward;
        bool _hasPlane;

        readonly Vector3[] _corners = new Vector3[RequiredCorners];
        readonly Vec2[] _corners2D = new Vec2[RequiredCorners];
        int _count;

        readonly Vector3[] _aimSamples = new Vector3[SteadyFrames];
        int _aimSampleCount, _aimSampleHead;
        bool _aimValid, _hasLastAim;
        Vector3 _aimPoint, _lastAimPoint;
        CornerIssue _aimIssue = CornerIssue.NoSurface;

        bool _hasValidFit;
        FittedRect _fit;
        CornerIssue _fitIssue;
        int _fitIssueCorner = -1;

        CornerIssue _lastActionIssue;
        int _lastActionIssueCorner = -1;
        int _version;

        public bool HasPlane => _hasPlane;
        public Vector3 PlanePoint => _planePoint;
        public Vector3 PlaneNormal => _normal;
        /// <summary>World direction of plane 2D +x.</summary>
        public Vector3 PlaneAxisX => _ex;
        /// <summary>World direction of plane 2D +y.</summary>
        public Vector3 PlaneAxisY => _ey;

        public int CornerCount => _count;

        /// <summary>World position of corner <paramref name="i"/> (pick order), projected onto the current plane.</summary>
        public Vector3 GetCorner(int i) => ProjectOntoPlane(_corners[i]);

        public bool AimValid => _aimValid;
        /// <summary>Current crosshair hit on the plane (raw, unsmoothed). Only meaningful while AimValid.</summary>
        public Vector3 AimPoint => _aimPoint;
        public CornerIssue AimIssue => _aimValid ? CornerIssue.None : _aimIssue;
        /// <summary>The last valid aim point, kept while the aim is invalid so views can draw it greyed.</summary>
        public bool HasLastAim => _hasLastAim;
        public Vector3 LastAimPoint => _lastAimPoint;

        /// <summary>Only when CornerCount == 4.</summary>
        public bool HasValidFit => _hasValidFit;
        public FittedRect Fit => _fit;
        public CornerIssue FitIssue => _fitIssue;
        public int FitIssueCorner => _fitIssueCorner;

        public CornerIssue LastActionIssue => _lastActionIssue;
        public int LastActionIssueCorner => _lastActionIssueCorner;

        /// <summary>Incremented on every corner or fit change.</summary>
        public int Version => _version;

        // ---------- plane ----------

        /// <summary>Call every frame. While CornerCount == 0 a jump to another surface clears the aim samples.</summary>
        public void SetPlane(Vector3 point, Vector3 normal)
        {
            if (normal.sqrMagnitude < 1e-12f) return;
            Vector3 n = normal.normalized;

            if (_hasPlane && _count == 0 &&
                (Vector3.Dot(n, _normal) < s_PlaneJumpCos || Mathf.Abs(Vector3.Dot(point - _planePoint, _normal)) > PlaneJumpDistance))
                _aimSampleCount = 0;

            bool changed = !_hasPlane || point != _planePoint || n != _normal;
            _planePoint = point;
            _normal = n;
            _ex = Vector3.ProjectOnPlane(Vector3.right, n);
            if (_ex.sqrMagnitude < 1e-4f) _ex = Vector3.ProjectOnPlane(Vector3.forward, n);
            _ex.Normalize();
            _ey = Vector3.Cross(_ex, n);
            _hasPlane = true;

            if (changed && _count == RequiredCorners) Refit();
        }

        public Vec2 ToPlane(Vector3 world)
        {
            Vector3 d = world - _planePoint;
            return new Vec2(Vector3.Dot(d, _ex), Vector3.Dot(d, _ey));
        }

        public Vector3 FromPlane(Vec2 p) => _planePoint + FromPlaneDir(p);

        public Vector3 FromPlaneDir(Vec2 d) => _ex * (float)d.X + _ey * (float)d.Y;

        Vector3 ProjectOntoPlane(Vector3 p) => p - _normal * Vector3.Dot(p - _planePoint, _normal);

        // ---------- aim ----------

        /// <summary>Call every frame with the screen-centre ray. Valid hits feed the steadiness buffer.</summary>
        public void UpdateAim(Ray ray)
        {
            _aimValid = false;
            if (!_hasPlane) { SetAimInvalid(CornerIssue.NoSurface); return; }

            Vector3 dir = ray.direction.normalized;
            float denom = Vector3.Dot(dir, _normal);
            if (Mathf.Abs(denom) < 1e-6f) { SetAimInvalid(CornerIssue.AimTooShallow); return; }
            float t = Vector3.Dot(_planePoint - ray.origin, _normal) / denom;
            if (t <= 0f) { SetAimInvalid(CornerIssue.NoSurface); return; }
            if (Mathf.Abs(denom) < MinAimCos) { SetAimInvalid(CornerIssue.AimTooShallow); return; }
            if (t > MaxAimDistance) { SetAimInvalid(CornerIssue.TooFar); return; }

            Vector3 hit = ray.origin + dir * t;
            _aimValid = true;
            _aimIssue = CornerIssue.None;
            _aimPoint = hit;
            _lastAimPoint = hit;
            _hasLastAim = true;
            _aimSamples[_aimSampleHead] = hit;
            _aimSampleHead = (_aimSampleHead + 1) % SteadyFrames;
            if (_aimSampleCount < SteadyFrames) _aimSampleCount++;
        }

        /// <summary>Marks the aim invalid (e.g. no surface or tracking not normal) and resets the steadiness buffer.</summary>
        public void SetAimInvalid(CornerIssue reason)
        {
            _aimValid = false;
            _aimIssue = reason == CornerIssue.None ? CornerIssue.NoSurface : reason;
            _aimSampleCount = 0;
        }

        // ---------- corners ----------

        /// <summary>
        /// Adds the mean of the last <see cref="SteadyFrames"/> aim samples as the next corner. False with
        /// <see cref="LastActionIssue"/> set if the aim is invalid, unsteady, or too close to an existing corner.
        /// </summary>
        public bool TryAddCorner()
        {
            if (_count >= RequiredCorners) return false;
            if (!_hasPlane || !_aimValid) return FailAction(_hasPlane ? AimIssue : CornerIssue.NoSurface, -1);
            if (_aimSampleCount < SteadyFrames) return FailAction(CornerIssue.Unsteady, -1);

            Vector3 mean = Vector3.zero;
            for (int i = 0; i < SteadyFrames; i++) mean += _aimSamples[i];
            mean /= SteadyFrames;
            float maxSq = MaxSteadySpread * MaxSteadySpread;
            for (int i = 0; i < SteadyFrames; i++)
                if ((_aimSamples[i] - mean).sqrMagnitude > maxSq) return FailAction(CornerIssue.Unsteady, -1);

            Vector3 p = ProjectOntoPlane(mean);
            for (int i = 0; i < _count; i++)
                if (Vector3.Distance(p, GetCorner(i)) < (float)CornerRectFit.MinCornerSeparation)
                    return FailAction(CornerIssue.TooCloseToCorner, i);

            _corners[_count++] = p;
            _lastActionIssue = CornerIssue.None;
            _lastActionIssueCorner = -1;
            if (_count == RequiredCorners) Refit();
            _version++;
            return true;
        }

        public void UndoCorner()
        {
            if (_count == 0) return;
            _count--;
            ClearFit();
            _lastActionIssue = CornerIssue.None;
            _lastActionIssueCorner = -1;
            _version++;
        }

        /// <summary>Removes all corners and forgets the plane and aim (the owner sets the plane again next frame).</summary>
        public void Clear()
        {
            _count = 0;
            ClearFit();
            _lastActionIssue = CornerIssue.None;
            _lastActionIssueCorner = -1;
            _hasPlane = false;
            _hasLastAim = false;
            SetAimInvalid(CornerIssue.NoSurface);
            _version++;
        }

        /// <summary>
        /// Board pose and frame for the current 4 corners, re-projected onto the current plane. +v is the rectangle axis
        /// closest to <paramref name="towardTopWorld"/>; the pose is lifted by <paramref name="lift"/> along the normal.
        /// </summary>
        public bool TryGetPlacement(Vector3 towardTopWorld, float lift, out Pose pose, out RectFrame frame)
        {
            pose = default;
            frame = default;
            if (_count != RequiredCorners || !_hasPlane) return false;
            Refit();
            if (!_hasValidFit) return false;

            var towardTop2D = new Vec2(Vector3.Dot(towardTopWorld, _ex), Vector3.Dot(towardTopWorld, _ey));
            frame = CornerRectFit.Orient(_fit, towardTop2D);
            pose = new Pose(FromPlane(frame.Center) + _normal * lift,
                            Quaternion.LookRotation(FromPlaneDir(frame.Up), _normal));
            return true;
        }

        /// <summary>
        /// Direction on the plane that should become the board top. Horizontal surfaces: the camera forward projected onto
        /// the plane (camera up when looking straight down). <paramref name="gravityUp"/> (vertical surfaces): world up.
        /// </summary>
        public static Vector3 TowardTop(Vector3 normal, Vector3 camForward, Vector3 camUp, bool gravityUp)
        {
            Vector3 n = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;
            if (gravityUp)
            {
                Vector3 g = Vector3.ProjectOnPlane(Vector3.up, n);
                if (g.sqrMagnitude >= 0.04f) return g;
            }
            Vector3 f = Vector3.ProjectOnPlane(camForward, n);
            if (f.sqrMagnitude < 0.04f) f = Vector3.ProjectOnPlane(camUp, n);
            return f;
        }

        // ---------- internals ----------

        bool FailAction(CornerIssue issue, int corner)
        {
            _lastActionIssue = issue;
            _lastActionIssueCorner = corner;
            return false;
        }

        void ClearFit()
        {
            _hasValidFit = false;
            _fit = default;
            _fitIssue = CornerIssue.None;
            _fitIssueCorner = -1;
        }

        void Refit()
        {
            for (int i = 0; i < RequiredCorners; i++) _corners2D[i] = ToPlane(_corners[i]);
            _hasValidFit = CornerRectFit.TryFit(_corners2D, out _fit, out _fitIssue, out _fitIssueCorner);
        }
    }
}
