using System;
using GeoART.BoardRender;
using GeoART.Contracts;
using UnityEngine;

namespace GeoART.Mocks
{
    /// <summary>
    /// Editor/flat-scene <see cref="IBoardSpace"/>: the board is always placed at this object's child transform and
    /// viewed by an ordinary camera. Uses the same projection code as ARBoardSpace, so a perspective camera here
    /// exercises the same corner/perspective behaviour (P2 4.3). Used by Sandbox_A_Flat and C's Flat.unity.
    /// Also registers a no-op <see cref="ICornerPlacement"/> (QuickPlace, never picking), so UI bound to it works here.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class FlatBoardSpace : MonoBehaviour, IBoardSpace, ICornerPlacement
    {
        [SerializeField, Tooltip("Defaults to Camera.main.")] Camera viewCamera;
        [SerializeField, Tooltip("Board transform (BoardView). Defaults to this transform.")] Transform board;
        [SerializeField] float sideMeters = BoardGeometry.DefaultSideMeters;

        bool _registered;

        public PlacementState State => PlacementState.Placed;
#pragma warning disable 67
        public event Action<PlacementState> StateChanged;
#pragma warning restore 67
        public bool IsPlaced => true;

        void Awake()
        {
            if (board == null) board = transform;
            ServiceHub.Register<IBoardSpace>(this);
            ServiceHub.Register<ICornerPlacement>(this);
            _registered = true;
        }

        void OnDestroy()
        {
            if (!_registered) return;
            ServiceHub.Unregister<IBoardSpace>(this);
            ServiceHub.Unregister<ICornerPlacement>(this);
        }

        Camera Cam => viewCamera != null ? viewCamera : Camera.main;

        public void PlaceAtReticle() { }
        public void Reset() { }

        public bool TryScreenToBoard(Vec2 screenPx, out Vec2 boardPos) =>
            BoardGeometry.TryScreenToBoard(Cam, board, sideMeters, screenPx, out boardPos);

        public bool TryBoardToScreen(Vec2 boardPos, out Vec2 screenPx) =>
            BoardGeometry.TryBoardToScreen(Cam, board, sideMeters, boardPos, out screenPx);

        // ---------- ICornerPlacement (no-op: the flat board is always placed) ----------

        public PlacementMode Mode => PlacementMode.QuickPlace;
        public void SetMode(PlacementMode mode) { }
        public int RequiredCorners => CornerPickSession.RequiredCorners;
        public int CornerCount => 0;
        public bool CanAddCorner => false;
        public bool CanConfirm => false;
        public CornerIssue Issue => CornerIssue.None;
        public int IssueCorner => -1;
        public double LiveEdgeMeters => double.NaN;
        public double SurfaceWidthMeters => double.NaN;
        public double SurfaceHeightMeters => double.NaN;
        public double BoardSideMeters => sideMeters;
        public bool TryAddCorner() => false;
        public void UndoCorner() { }
        public void ClearCorners() { }
        public bool ConfirmCorners() => false;
#pragma warning disable 67
        public event Action CornersChanged;
#pragma warning restore 67
    }
}
