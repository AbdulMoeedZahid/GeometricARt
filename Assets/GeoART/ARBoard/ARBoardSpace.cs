using System;
using System.Collections.Generic;
using GeoART.BoardRender;
using GeoART.Contracts;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GeoART.ARBoard
{
    /// <summary>
    /// Device implementation of <see cref="IBoardSpace"/> and <see cref="ICornerPlacement"/> (A-2, A-3, A-5).
    ///
    ///   Scanning       -> no horizontal plane under the screen centre (or the session is not tracking yet)
    ///   PlaneFound     -> QuickPlace: reticle shown on an upward-facing plane; PlaceAtReticle() places a 0.40 m board.
    ///                     Corners: aim marker under the crosshair; TryAddCorner() takes corner 1 and locks the plane.
    ///   PickingCorners -> 1..4 corners picked (Corners mode). With 4 corners and a valid rectangle fit (CanConfirm),
    ///                     ConfirmCorners() places the largest centred square of the fitted rectangle.
    ///   Placed         -> one board on an ARAnchor; plane detection is switched off to save CPU/battery
    ///   TrackingLost   -> session lost tracking after placement; projection is refused until it recovers
    ///   Unsupported    -> ARKit unavailable (also the state in the Editor without a device)
    ///
    /// Corners 2 to 4 always lie on the locked plane's infinite extension (paper is textureless, so ARKit's polygon
    /// rarely covers the far corners), intersected with our own ray-plane math: ARKit reports
    /// supportsWorldBasedRaycast = false, so ARRaycastManager.Raycast(Ray) is not used. Tracking that is not normal for
    /// more than 1 s while picking clears the corners (TrackingInterrupted).
    /// After placement, screen points are projected by intersecting the camera ray with the placed board's plane,
    /// never by raycasting detected planes (P2 4.3).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class ARBoardSpace : MonoBehaviour, IBoardSpace, ICornerPlacement
    {
        [Header("AR (found in the scene if left empty)")]
        [SerializeField] ARRaycastManager raycastManager;
        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] Camera arCamera;
        [SerializeField] XROrigin xrOrigin;

        [Header("Board")]
        [SerializeField, Tooltip("Child holding BoardView; hidden until placed.")]
        Transform boardRoot;
        [SerializeField, Tooltip("BoardView on boardRoot (found there if left empty); resized at placement.")]
        BoardView boardView;
        [SerializeField, Tooltip("Child holding ReticleView; shown while a plane is found in QuickPlace mode.")]
        Transform reticle;
        [SerializeField, Tooltip("Quick-place board side (D-04). Corner placement uses the fitted square instead.")]
        float sideMeters = BoardGeometry.DefaultSideMeters;
        [SerializeField, Tooltip("Height above the detected surface, avoids z-fighting with the plane visual.")]
        float liftMeters = 0.002f;
        [SerializeField] bool disablePlaneDetectionWhenPlaced = true;
        [SerializeField] bool registerInServiceHub = true;

        [Header("Corner placement")]
        [SerializeField, Tooltip("Corners registers the board to a real sheet; QuickPlace restores the one-tap 40 cm flow.")]
        PlacementMode defaultMode = PlacementMode.Corners;
        [SerializeField, Tooltip("Deferred (CORNER_PLACEMENT_PLAN D8): also accept vertical planes. Keep OFF for P2; " +
                                 "SceneBuilder still requests horizontal plane detection only.")]
        bool allowVerticalSurfaces;
        [SerializeField, Tooltip("Child holding CornerPickerView; drawn while picking corners.")]
        CornerPickerView cornerView;

        const float ActionIssueSeconds = 3f;
        const float TrackingGraceSeconds = 1f;
        const float PlaneExtensionMargin = 0.75f;   // fallback hits must lie within the plane's extents + this
        const int MaxSubsumeHops = 8;

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        PlacementState _state = PlacementState.Scanning;
        GameObject _anchorObject;
        Transform _boardHome;
        bool _registered;

        readonly CornerPickSession _picker = new CornerPickSession();
        ARPlane _candidatePlane, _lockedPlane;
        Pose _aimPose;
        bool _aimValid, _tracking;
        float _sideMeters;
        float _surfaceW = float.NaN, _surfaceH = float.NaN;
        PlacementMode _mode;
        float _notTrackingSince = -1f;
        RectFrame _previewFrame;
        bool _previewValid;

        CornerIssue _actionIssue;
        int _actionIssueCorner = -1;
        float _actionIssueTime;

        // Last values seen by CornersChanged listeners.
        CornerIssue _lastIssue;
        int _lastIssueCorner = -1, _lastCount, _lastVersion;
        bool _lastCanAdd, _lastCanConfirm;
        PlacementMode _lastMode;

        public PlacementState State => _state;
        public event Action<PlacementState> StateChanged;
        public bool IsPlaced => _state == PlacementState.Placed || _state == PlacementState.TrackingLost;
        public float SideMeters => _sideMeters;
        public Transform BoardTransform => boardRoot;
        public Camera Camera => arCamera;

        void Awake()
        {
            if (raycastManager == null) raycastManager = FindAnyObjectByType<ARRaycastManager>();
            if (planeManager == null) planeManager = FindAnyObjectByType<ARPlaneManager>();
            if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();
            if (arCamera == null) arCamera = xrOrigin != null && xrOrigin.Camera != null ? xrOrigin.Camera : Camera.main;
            if (boardView == null && boardRoot != null) boardView = boardRoot.GetComponent<BoardView>();

            _sideMeters = sideMeters;
            _mode = _lastMode = defaultMode;
            _boardHome = transform;
            if (boardRoot != null) boardRoot.gameObject.SetActive(false);
            if (reticle != null) reticle.gameObject.SetActive(false);
            if (cornerView != null)
            {
                cornerView.ViewCamera = arCamera;
                cornerView.Hide();
            }

            if (registerInServiceHub)
            {
                ServiceHub.Register<IBoardSpace>(this);
                ServiceHub.Register<ICornerPlacement>(this);
                _registered = true;
            }
        }

        void OnEnable()
        {
            ARSession.stateChanged += OnSessionStateChanged;
            EvaluateSession(ARSession.state);
        }

        void OnDisable() => ARSession.stateChanged -= OnSessionStateChanged;

        void OnDestroy()
        {
            if (_registered)
            {
                ServiceHub.Unregister<IBoardSpace>(this);
                ServiceHub.Unregister<ICornerPlacement>(this);
            }
            if (_anchorObject != null) Destroy(_anchorObject);
        }

        void OnApplicationPause(bool paused)
        {
            // The world origin can move while the app is in the background, so picked corners are no longer valid.
            if (paused && !IsPlaced && _picker.CornerCount > 0)
            {
                ClearCorners();
                SetActionIssue(CornerIssue.TrackingInterrupted, -1);
                RaiseCornersChanged();
            }
        }

        void Update()
        {
            if (IsPlaced)
            {
                EvaluateSession(ARSession.state);   // notTrackingReason changes do not always raise stateChanged
                return;
            }
            if (_state == PlacementState.Unsupported) return;

            if (_actionIssue != CornerIssue.None && Time.unscaledTime - _actionIssueTime > ActionIssueSeconds)
                ClearActionIssue();

            _tracking = arCamera != null && ARSession.state == ARSessionState.SessionTracking &&
                        ARSession.notTrackingReason == NotTrackingReason.None;
            if (!_tracking) UpdateNotTracking();
            else
            {
                _notTrackingSince = -1f;
                Ray centerRay = arCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
                if (_picker.CornerCount == 0) UpdateAcquire(centerRay);
                else UpdatePicking(centerRay);
            }
            RaiseCornersChangedIfNeeded();
        }

        // ---------- IBoardSpace ----------

        /// <summary>Quick place: a 0.40 m board at the aim point, in either mode. No-op unless State is PlaneFound.</summary>
        public void PlaceAtReticle()
        {
            if (_state != PlacementState.PlaneFound || !_aimValid || boardRoot == null) return;
            PlaceBoard(_aimPose, sideMeters, float.NaN, float.NaN);
        }

        public void Reset()
        {
            if (boardRoot != null)
            {
                boardRoot.gameObject.SetActive(false);
                boardRoot.SetParent(_boardHome, false);
            }
            if (_anchorObject != null)
            {
                Destroy(_anchorObject);
                _anchorObject = null;
            }
            if (planeManager != null)
            {
                planeManager.enabled = true;
                planeManager.SetTrackablesActive(true);
            }

            _picker.Clear();
            _lockedPlane = _candidatePlane = null;
            _aimValid = _previewValid = false;
            _notTrackingSince = -1f;
            _sideMeters = sideMeters;
            _surfaceW = _surfaceH = float.NaN;
            if (boardView != null)
            {
                boardView.SetSideMeters(sideMeters);
                boardView.SetSurfaceSize(float.NaN, float.NaN);
            }
            if (cornerView != null) cornerView.Hide();
            ClearActionIssue();

            if (_state != PlacementState.Unsupported) SetState(PlacementState.Scanning);
            RaiseCornersChanged();
        }

        public bool TryScreenToBoard(Vec2 screenPx, out Vec2 board)
        {
            if (_state != PlacementState.Placed)
            {
                board = default;
                return false;
            }
            return BoardGeometry.TryScreenToBoard(arCamera, boardRoot, _sideMeters, screenPx, out board);
        }

        public bool TryBoardToScreen(Vec2 board, out Vec2 screenPx)
        {
            if (!IsPlaced)
            {
                screenPx = default;
                return false;
            }
            return BoardGeometry.TryBoardToScreen(arCamera, boardRoot, _sideMeters, board, out screenPx);
        }

        // ---------- ICornerPlacement ----------

        public PlacementMode Mode => _mode;
        public int RequiredCorners => CornerPickSession.RequiredCorners;
        public int CornerCount => _picker.CornerCount;

        public bool CanAddCorner =>
            _mode == PlacementMode.Corners && !IsPlaced && _tracking &&
            (_state == PlacementState.PlaneFound || _state == PlacementState.PickingCorners) &&
            _picker.CornerCount < CornerPickSession.RequiredCorners && _picker.AimValid;

        public bool CanConfirm =>
            _mode == PlacementMode.Corners && !IsPlaced && _tracking &&
            _picker.CornerCount == CornerPickSession.RequiredCorners && _picker.HasValidFit && _previewValid;

        public CornerIssue Issue
        {
            get
            {
                GetIssue(out var issue, out _);
                return issue;
            }
        }

        public int IssueCorner
        {
            get
            {
                GetIssue(out _, out int corner);
                return corner;
            }
        }

        public double LiveEdgeMeters =>
            _picker.CornerCount > 0 && _picker.AimValid
                ? Vector3.Distance(_picker.AimPoint, _picker.GetCorner(_picker.CornerCount - 1))
                : double.NaN;

        public double SurfaceWidthMeters => CanConfirm ? _previewFrame.Width : IsPlaced ? _surfaceW : double.NaN;
        public double SurfaceHeightMeters => CanConfirm ? _previewFrame.Height : IsPlaced ? _surfaceH : double.NaN;
        public double BoardSideMeters => CanConfirm ? _previewFrame.SquareSide : _sideMeters;

        public event Action CornersChanged;

        public void SetMode(PlacementMode mode)
        {
            if (IsPlaced || mode == _mode) return;
            _mode = mode;
            ClearCorners();
            if (reticle != null && mode == PlacementMode.Corners) reticle.gameObject.SetActive(false);
            if (cornerView != null && mode == PlacementMode.QuickPlace) cornerView.Hide();
            RaiseCornersChanged();
        }

        public bool TryAddCorner()
        {
            if (_mode != PlacementMode.Corners || IsPlaced || _picker.CornerCount >= CornerPickSession.RequiredCorners)
                return false;

            bool added = false;
            if (!_tracking || (_state != PlacementState.PlaneFound && _state != PlacementState.PickingCorners))
                SetActionIssue(CornerIssue.NoSurface, -1);
            else
            {
                bool first = _picker.CornerCount == 0;
                if (first) _lockedPlane = _candidatePlane;   // may be null after the infinite-plane fallback; the cached plane is enough
                added = _picker.TryAddCorner();
                if (added)
                {
                    ClearActionIssue();
                    SetState(PlacementState.PickingCorners);
                }
                else
                {
                    if (first) _lockedPlane = null;
                    SetActionIssue(_picker.LastActionIssue, _picker.LastActionIssueCorner);
                }
            }
            RaiseCornersChanged();
            return added;
        }

        public void UndoCorner()
        {
            if (IsPlaced || _picker.CornerCount == 0) return;
            _picker.UndoCorner();
            _previewValid = false;
            ClearActionIssue();
            if (_picker.CornerCount == 0) Unlock();
            RaiseCornersChanged();
        }

        public void ClearCorners()
        {
            if (IsPlaced) return;
            _picker.Clear();
            _previewValid = false;
            ClearActionIssue();
            Unlock();
            RaiseCornersChanged();
        }

        public bool ConfirmCorners()
        {
            if (!CanConfirm) return false;
            if (!_picker.TryGetPlacement(TowardTopNow(), liftMeters, out Pose pose, out RectFrame frame)) return false;
            PlaceBoard(pose, (float)frame.SquareSide, (float)frame.Width, (float)frame.Height);
            return true;
        }

        // ---------- per-frame internals ----------

        void UpdateNotTracking()
        {
            if (_notTrackingSince < 0f) _notTrackingSince = Time.unscaledTime;
            SetReticleVisible(false);
            if (cornerView != null) cornerView.Hide();
            _aimValid = false;
            _previewValid = false;
            _picker.SetAimInvalid(CornerIssue.NoSurface);

            if (_picker.CornerCount > 0 && Time.unscaledTime - _notTrackingSince > TrackingGraceSeconds)
            {
                ClearCorners();
                SetActionIssue(CornerIssue.TrackingInterrupted, -1);
            }
            SetState(_picker.CornerCount > 0 ? PlacementState.PickingCorners : PlacementState.Scanning);
        }

        /// <summary>No corner yet: find the surface under the crosshair (reticle or aim marker).</summary>
        void UpdateAcquire(Ray centerRay)
        {
            bool corners = _mode == PlacementMode.Corners;
            _aimValid = AcquireSurface(centerRay, corners, out Vector3 hit, out Vector3 normal, out _candidatePlane);
            if (_aimValid)
                _aimPose = new Pose(hit + normal * liftMeters, BoardGeometry.FacingRotation(normal, arCamera.transform.forward));

            if (!corners)
            {
                if (_aimValid && reticle != null) reticle.SetPositionAndRotation(_aimPose.position, _aimPose.rotation);
                SetReticleVisible(_aimValid);
                if (cornerView != null) cornerView.Hide();
            }
            else
            {
                SetReticleVisible(false);
                if (_aimValid)
                {
                    _picker.SetPlane(hit, normal);
                    _picker.UpdateAim(centerRay);
                }
                else _picker.SetAimInvalid(CornerIssue.NoSurface);
                if (cornerView != null) cornerView.Show(_picker, false, default, liftMeters);
            }
            _previewValid = false;
            SetState(_aimValid ? PlacementState.PlaneFound : PlacementState.Scanning);
        }

        /// <summary>1..4 corners: aim at the locked plane's infinite extension and preview the fit.</summary>
        void UpdatePicking(Ray centerRay)
        {
            SetReticleVisible(false);
            // Follow merges to the surviving plane; bounded so a subsumedBy cycle can never hang the frame.
            for (int i = 0; i < MaxSubsumeHops && _lockedPlane != null && _lockedPlane.subsumedBy != null; i++)
                _lockedPlane = _lockedPlane.subsumedBy;
            if (_lockedPlane != null && _lockedPlane.trackingState == TrackingState.Tracking)
                _picker.SetPlane(_lockedPlane.transform.position, _lockedPlane.normal);   // same plane as infinitePlane
            _picker.UpdateAim(centerRay);

            _previewValid = _picker.CornerCount == CornerPickSession.RequiredCorners && _picker.HasValidFit &&
                            _picker.TryGetPlacement(TowardTopNow(), liftMeters, out _, out _previewFrame);

            if (cornerView != null)
            {
                GetIssue(out _, out int highlight);
                cornerView.Show(_picker, _previewValid, _previewFrame, liftMeters, highlight);
            }
            SetState(PlacementState.PickingCorners);
        }

        /// <summary>
        /// Surface under the screen centre: ARKit polygon raycast first; in Corners mode, if that misses, the nearest
        /// tracked plane whose infinite extension the ray hits close to the plane (paper corners off the polygon).
        /// </summary>
        bool AcquireSurface(Ray ray, bool allowExtension, out Vector3 hit, out Vector3 normal, out ARPlane plane)
        {
            hit = normal = default;
            plane = null;

            if (raycastManager != null)
            {
                var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (raycastManager.Raycast(center, s_Hits, TrackableType.PlaneWithinPolygon))
                {
                    foreach (var h in s_Hits)
                    {
                        var p = h.trackable as ARPlane;
                        if (p == null && planeManager != null) p = planeManager.GetPlane(h.trackableId);
                        if (p == null || !Allowed(p)) continue;   // table only unless vertical surfaces are enabled (D-09, D8)
                        hit = h.pose.position;
                        normal = p.normal;
                        plane = p;
                        return true;
                    }
                }
            }

            if (!allowExtension || planeManager == null) return false;
            float bestT = float.MaxValue;
            foreach (var p in planeManager.trackables)
            {
                if (p.trackingState != TrackingState.Tracking || p.subsumedBy != null || !Allowed(p)) continue;
                Plane infinite = p.infinitePlane;
                if (Mathf.Abs(Vector3.Dot(ray.direction, infinite.normal)) < CornerPickSession.MinAimCos) continue;
                if (!infinite.Raycast(ray, out float t) || t <= 0f || t > CornerPickSession.MaxAimDistance || t >= bestT) continue;
                Vector3 point = ray.GetPoint(t);
                float reach = Mathf.Max(p.extents.x, p.extents.y) + PlaneExtensionMargin;
                if ((point - p.center).sqrMagnitude > reach * reach) continue;
                bestT = t;
                hit = point;
                normal = p.normal;
                plane = p;
            }
            return plane != null;
        }

        bool Allowed(ARPlane plane) =>
            plane.alignment == PlaneAlignment.HorizontalUp ||
            (allowVerticalSurfaces && plane.alignment == PlaneAlignment.Vertical);

        Vector3 TowardTopNow()
        {
            Vector3 n = _picker.PlaneNormal;
            bool gravityUp = allowVerticalSurfaces && Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.5f;
            Transform cam = arCamera.transform;
            return CornerPickSession.TowardTop(n, cam.forward, cam.up, gravityUp);
        }

        void PlaceBoard(Pose pose, float side, float surfaceW, float surfaceH)
        {
            _anchorObject = new GameObject("BoardAnchor");
            if (xrOrigin != null && xrOrigin.TrackablesParent != null)
                _anchorObject.transform.SetParent(xrOrigin.TrackablesParent, true);
            _anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _anchorObject.AddComponent<ARAnchor>();   // ARAnchorManager in the scene turns this into an ARKit anchor

            _sideMeters = side;
            _surfaceW = surfaceW;
            _surfaceH = surfaceH;
            if (boardView != null)
            {
                boardView.SetSideMeters(side);
                boardView.SetSurfaceSize(surfaceW, surfaceH);
            }

            boardRoot.SetParent(_anchorObject.transform, false);
            boardRoot.localPosition = Vector3.zero;   // the pose already includes the lift
            boardRoot.localRotation = Quaternion.identity;
            boardRoot.localScale = Vector3.one;
            boardRoot.gameObject.SetActive(true);
            SetReticleVisible(false);
            if (cornerView != null) cornerView.Hide();

            _picker.Clear();
            _previewValid = false;
            ClearActionIssue();
            Unlock();

            if (disablePlaneDetectionWhenPlaced && planeManager != null)
            {
                planeManager.SetTrackablesActive(false);
                planeManager.enabled = false;
            }
            SetState(PlacementState.Placed);
            EvaluateSession(ARSession.state);
            RaiseCornersChanged();
        }

        void Unlock()
        {
            if (_picker.CornerCount > 0) return;
            _lockedPlane = null;
            if (!IsPlaced && _state == PlacementState.PickingCorners)
                SetState(_aimValid ? PlacementState.PlaneFound : PlacementState.Scanning);
        }

        void SetReticleVisible(bool visible)
        {
            if (reticle != null && reticle.gameObject.activeSelf != visible) reticle.gameObject.SetActive(visible);
        }

        // ---------- issues and events ----------

        /// <summary>Contract priority: fit issue (4 corners, invalid) &gt; last action issue (3 s) &gt; live aim issue &gt; None.</summary>
        void GetIssue(out CornerIssue issue, out int corner)
        {
            issue = CornerIssue.None;
            corner = -1;
            if (IsPlaced) return;
            int count = _picker.CornerCount;
            if (count == CornerPickSession.RequiredCorners && !_picker.HasValidFit && _picker.FitIssue != CornerIssue.None)
            {
                issue = _picker.FitIssue;
                corner = _picker.FitIssueCorner;
            }
            else if (_actionIssue != CornerIssue.None)
            {
                issue = _actionIssue;
                corner = _actionIssueCorner;
            }
            else if (_mode == PlacementMode.Corners && _tracking && count < CornerPickSession.RequiredCorners &&
                     (_state == PlacementState.PlaneFound || _state == PlacementState.PickingCorners) && !_picker.AimValid)
            {
                issue = _picker.AimIssue;
            }
        }

        void SetActionIssue(CornerIssue issue, int corner)
        {
            _actionIssue = issue;
            _actionIssueCorner = issue == CornerIssue.None ? -1 : corner;
            _actionIssueTime = Time.unscaledTime;
        }

        void ClearActionIssue()
        {
            _actionIssue = CornerIssue.None;
            _actionIssueCorner = -1;
        }

        void RaiseCornersChangedIfNeeded()
        {
            GetIssue(out var issue, out int corner);
            if (issue == _lastIssue && corner == _lastIssueCorner && CanAddCorner == _lastCanAdd &&
                CanConfirm == _lastCanConfirm && _picker.CornerCount == _lastCount && _picker.Version == _lastVersion &&
                _mode == _lastMode)
                return;
            RaiseCornersChanged();
        }

        void RaiseCornersChanged()
        {
            GetIssue(out _lastIssue, out _lastIssueCorner);
            _lastCanAdd = CanAddCorner;
            _lastCanConfirm = CanConfirm;
            _lastCount = _picker.CornerCount;
            _lastVersion = _picker.Version;
            _lastMode = _mode;
            CornersChanged?.Invoke();
        }

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args) => EvaluateSession(args.state);

        void EvaluateSession(ARSessionState sessionState)
        {
            if (sessionState == ARSessionState.Unsupported || sessionState == ARSessionState.NeedsInstall)
            {
                SetState(PlacementState.Unsupported);
                return;
            }
            if (_state == PlacementState.Unsupported && sessionState >= ARSessionState.Ready)
                SetState(PlacementState.Scanning);

            if (!IsPlaced) return;
            bool tracking = sessionState == ARSessionState.SessionTracking &&
                            ARSession.notTrackingReason == NotTrackingReason.None;
            SetState(tracking ? PlacementState.Placed : PlacementState.TrackingLost);
        }

        void SetState(PlacementState next)
        {
            if (_state == next) return;
            _state = next;
            StateChanged?.Invoke(next);
        }
    }
}
