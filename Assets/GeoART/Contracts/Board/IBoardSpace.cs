using System;

namespace GeoART.Contracts
{
    /// <summary>
    /// Placement lifecycle. New values are appended at the end so existing ordinals never change.
    /// PickingCorners: the user is selecting surface corners (<see cref="ICornerPlacement"/>); the board is not placed
    /// and projection returns false.
    /// </summary>
    public enum PlacementState { Unsupported, Scanning, PlaneFound, Placed, TrackingLost, PickingCorners }

    /// <summary>
    /// Board placement and board/screen projection (A). Real providers: ARBoardSpace (device) and FlatBoardSpace (Editor).
    ///
    /// Screen pixels use Unity screen space: origin at the bottom-left, Y up, in pixels. That is the same
    /// space as Input System touch/mouse positions, so pass them through unchanged.
    ///
    /// Board orientation: at placement the board's bottom edge (v = 0) faces the user and +u points to the
    /// user's right. The board is a square of side <see cref="ICornerPlacement.BoardSideMeters"/>: 0.40 m for quick
    /// place (D-04), or the largest centred square inside a corner-registered surface. Board space is still [0,1] x [0,1].
    /// </summary>
    public interface IBoardSpace
    {
        PlacementState State { get; }
        event Action<PlacementState> StateChanged;

        /// <summary>True in Placed and TrackingLost. Only one board exists; placing again requires Reset.</summary>
        bool IsPlaced { get; }

        /// <summary>Quick place: places a 0.40 m board at the aim point, in either placement mode. No-op unless State is PlaneFound.</summary>
        void PlaceAtReticle();

        /// <summary>"Re-place board": removes the board and returns to Scanning. Consumers should cancel open gestures.</summary>
        void Reset();

        /// <summary>
        /// Intersects the camera ray with the placed board plane. False if not placed, tracking is lost, the ray is
        /// near-parallel or behind the camera, or the hit is outside [0,1] x [0,1]. Never raycasts other planes.
        /// </summary>
        bool TryScreenToBoard(Vec2 screenPx, out Vec2 board);

        /// <summary>Perspective-correct projection for screen-space snap/hit tolerances. False if behind the camera.</summary>
        bool TryBoardToScreen(Vec2 board, out Vec2 screenPx);
    }
}
