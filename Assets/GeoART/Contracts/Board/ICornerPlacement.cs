using System;

// Uses only System: this file is also compiled by tools/mathcheck (dotnet console harness).
namespace GeoART.Contracts
{
    /// <summary>QuickPlace: one tap places a 0.40 m board. Corners: the board is registered to a real rectangle.</summary>
    public enum PlacementMode { QuickPlace, Corners }

    /// <summary>Why a corner action or the current corner set cannot be used. None = OK.</summary>
    public enum CornerIssue
    {
        None,
        NoSurface,           // no plane under the crosshair
        AimTooShallow,       // view ray is within 15 degrees of grazing the plane
        TooFar,              // aim point farther than 2.5 m
        Unsteady,            // aim samples spread > 8 mm
        TooCloseToCorner,    // closer than 5 cm to an existing corner (IssueCorner = that corner)
        NotConvex,           // one corner lies inside the triangle of the other three
        NotRectangular,      // angle or residual check failed (IssueCorner = worst corner)
        TooSmall,            // shorter side < 10 cm
        TooLarge,            // longer side > 150 cm
        TooElongated,        // aspect > 4:1
        TrackingInterrupted  // tracking was not normal for > 1 s while picking; corners were cleared
    }

    /// <summary>
    /// Corner registration of the board to a real rectangle (A). Provided by ARBoardSpace (and as a no-op by
    /// FlatBoardSpace). Resolve with ServiceHub.TryResolve; UI binds buttons to it, IBoardSpace still owns State.
    /// Corners are taken at the screen-centre crosshair, never at the touch position.
    /// The board is the largest centred square inside the fitted rectangle, so board space stays [0,1] x [0,1].
    /// </summary>
    public interface ICornerPlacement
    {
        PlacementMode Mode { get; }
        /// <summary>Ignored while IBoardSpace.IsPlaced. Clears picked corners.</summary>
        void SetMode(PlacementMode mode);

        /// <summary>Always 4.</summary>
        int RequiredCorners { get; }
        /// <summary>0..RequiredCorners.</summary>
        int CornerCount { get; }
        /// <summary>Mode == Corners, not placed, count &lt; 4, tracking, and the aim is valid now.</summary>
        bool CanAddCorner { get; }
        /// <summary>Count == 4 and the rectangle fit is valid.</summary>
        bool CanConfirm { get; }

        /// <summary>Fit issue (4 corners, invalid) &gt; last action issue (for 3 s) &gt; live aim issue &gt; None.</summary>
        CornerIssue Issue { get; }
        /// <summary>0-based index in pick order, or -1.</summary>
        int IssueCorner { get; }

        /// <summary>Last corner -&gt; aim point, metres; NaN if there is no corner or the aim is invalid.</summary>
        double LiveEdgeMeters { get; }
        /// <summary>Fitted extent along +u, metres; NaN unless previewing (CanConfirm) or placed by corners.</summary>
        double SurfaceWidthMeters { get; }
        /// <summary>Fitted extent along +v, metres; same validity as <see cref="SurfaceWidthMeters"/>.</summary>
        double SurfaceHeightMeters { get; }
        /// <summary>Current or previewed board side, metres; 0.40 for quick place.</summary>
        double BoardSideMeters { get; }

        /// <summary>Adds the crosshair point as the next corner. False (and Issue set) if not possible.</summary>
        bool TryAddCorner();
        void UndoCorner();
        /// <summary>"Restart": removes all picked corners.</summary>
        void ClearCorners();
        /// <summary>Places the board on the fitted rectangle; State -&gt; Placed. False unless CanConfirm.</summary>
        bool ConfirmCorners();

        /// <summary>Raised on add/undo/clear/mode/fit/issue/CanAddCorner/CanConfirm changes. Not raised per frame for LiveEdgeMeters.</summary>
        event Action CornersChanged;
    }
}
