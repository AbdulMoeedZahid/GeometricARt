# Corner placement: register the board to a real surface (A)

**Status:** implementation plan, written Oct 28, 2026. Implemented the same day in `GeoARt-A-stage` and synced to the Mac (uncommitted). Device results are still pending. Usage notes are in `docs/BOARD_PLACEMENT.md`.

| Part | Status |
|---|---|
| 3 Contracts (`PickingCorners` appended, `ICornerPlacement`) | Implemented as specified |
| 4.2 `CornerRectFit`, 4.3 `CornerPickSession`, 4.4 `BoardView`, 4.5 `CornerPickerView` | Implemented |
| 4.6 `ARBoardSpace`, 4.7 tap handler, 4.8 `PlacementControls` (temporary uGUI) + `DevHud`, 4.9 `FlatBoardSpace` no-op | Implemented. The infinite-plane fallback is used in Corners mode only, so QuickPlace behaves exactly as before. `CornerPickerView.Show` has an extra optional `highlightCorner` argument. Picking corners is cleared on app pause. |
| 4.10 `SceneBuilder` (CornerPicker, PlacementControls, PlaneFill alpha 0.12), 4.11 `.gitignore` | Implemented |
| 5 EditMode tests 1-16 | Written; batch compile is clean. Must be run from the Unity GUI Test Runner. Deviations: test 2 bound is worst < 10 mm, RMS < 4 mm (6 mm is statistically too tight for ±5 mm noise); trapezoid top 0.13 instead of 0.15, and corner moved 4 cm instead of 3 cm (the plan values sit exactly on the 15 mm residual limit) |
| 6.3 `tools/mathcheck` | Implemented; 30/30 checks pass on the Mac with Unity's bundled .NET 8 SDK |
| 6.5/6.6 export, sign and install | Left to the user (full `tools/ios_build.sh --sandbox`) |
| 7 Device checklist C-01 to C-14 | Pending |
| D8 vertical surfaces | Deferred: the `allowVerticalSurfaces` flag exists, is off, and is not wired into plane detection |
| 4.9 optional `simulateCornerPlacement` | Deferred |
| D1 future aspect-ratio board, 3-corner mode, thick-canvas height offset | Deferred (post-P2) |
**Owner:** A (AR/board). **Edit target:** `/home/macharradm/MO/GeoARt-A-stage/` (mirrors the repo root). Commit happens on the Mac.
**Request:** "I want to be able to place a canvas, and then simply select its corners via my iphone, so that the canvas or page or surface I chose via selecting corners is registered as the surface on which I will place the design."

**Goal:** the user registers the drawing board to a real flat object on a table (sheet of paper, canvas, page, taped area) by aiming at its 4 corners. The board then coincides with that object. The current one-tap 40 cm "quick place" stays as a fallback. Board space stays `[0,1] x [0,1]`, so B/C/D are unaffected.

---

## 1. User flow and state machine

### 1.1 Flow (Corners mode, the default)

1. **Scanning.** Coach text: "Move your phone slowly over a textured table". Plane detection works as it does today.
2. **PlaneFound.** A small aim marker (ring + cross, about 1 cm) sits on the surface under the screen centre. Coach text: "Aim the + at a corner of your sheet and tap (1 of 4)". A **Quick place 40 cm** button is also shown.
3. Tap anywhere that is not UI. The point under the **screen-centre crosshair** becomes corner 1, not the point under the finger. This locks the plane. State changes to **PickingCorners**.
4. For corners 2 to 4: the crosshair aims at the locked plane's *infinite* extension. A rubber band runs from the last corner to the aim point, and a dashed closing segment appears once 3 corners exist. Coach text shows the live edge length, for example "Corner 3 of 4 · edge 21.0 cm". Buttons: **Undo corner**, **Restart**.
5. After the 4th corner, the rectangle fit runs:
   - **Invalid:** the coach names the problem and the corner (for example "Corner 2 doesn't fit a rectangle: Undo or Restart"). That marker turns red.
   - **Valid:** the preview shows the fitted rectangle outline (green), the inner square board footprint, and an arrow on the edge that will become the top. The arrow updates live as the user moves. Coach text: "Surface 21.0 x 29.7 cm, board 21.0 cm. Tap to place". There is also a **Place board** button.
6. Tap or **Place board** confirms. The board is anchored, sized to the surface, and the state is **Placed**. Plane detection turns off as it does today. The HUD shows "Surface 21.0 x 29.7 cm · board 21.0 cm" so the user can check it against a ruler.
7. **Re-place board** behaves as `Reset()` does today. It also clears corners and restores the default size.

QuickPlace mode is unchanged: a 40 cm reticle footprint, and a tap places the board.

### 1.2 States

`Confirm` is not a separate state. It is `PickingCorners` with 4 corners and `CanConfirm == true`, which keeps the enum small.

```
 Unsupported (no ARKit / Editor)

 Scanning  <---------------------------------------------------------------+
   | surface under the + (polygon hit, or near a tracked plane's extension) |
   v                                                                         |
 PlaneFound --[QuickPlace: tap]  or  ["Quick place 40 cm" button]--> PLACE (0.40 m)
   |                                                                         |
   | [Corners: tap] = TryAddCorner #1 (locks the plane)                      |
   v                                                                         |
 PickingCorners n=1..3 --tap--> n+1      Undo at n=1 -> PlaneFound/Scanning  |
   |  Restart -> n=0 -> PlaneFound/Scanning                                  |
   |  tracking not normal for > 1.0 s -> corners cleared (TrackingInterrupted) ---+
   v n=4: fit
 PickingCorners n=4 --invalid--> Issue + red corner; Undo (n=3) / Restart
   |--valid (CanConfirm)--> preview; tap or "Place board" = ConfirmCorners
   v
 Placed <--> TrackingLost   (as today)
   Reset / "Re-place board" (from Placed or TrackingLost) -> Scanning; plane detection back on
```

Invariants:

- Exactly one board.
- `IsPlaced` is true only in `Placed` and `TrackingLost`.
- `TryScreenToBoard` returns false before placement, so no drawing input can happen before placement completes.
- `PlaceAtReticle()` is still a no-op unless the state is `PlaneFound`, so it does nothing while picking corners.

---

## 2. Decisions and rationale

| # | Decision | Rationale and alternatives |
|---|---|---|
| D1 | **Non-square surfaces:** the board is the **largest centred square** inside the fitted rectangle (side = `min(W, H)`). The rest of the rectangle is margin, drawn only as a thin dashed outline of the registered surface. | Keeps uniform scale, so circles stay circular and the 8-pointed star and 2x2 tiling stay exact. `[0,1]^2`, `GridN` cells, snapping, fixtures and the lesson stay unchanged, so B/C/D are untouched for the Sun Nov 1 P2 deadline. *Rejected:* a non-uniform map (circles become ellipses on paper, and the grid and snap-distance assumptions break). *Rejected for now:* an aspect-ratio contract (board `[0,a] x [0,1]`), because it ripples into B's `Board`/grid/cell math, fixtures, D's lesson and C's grid pill. **Future path (post-P2):** add `double Aspect` to the model (`Board`) and `IBoardSpace`, with rectangular cells, and let users opt in to the full rectangle. Example: A4 portrait (21 x 29.7 cm) gives a 21 cm board with 4.35 cm margins top and bottom. |
| D2 | **Aim with a screen-centre crosshair, then tap anywhere** (not on the corner). | A finger covers exactly the corner the user is trying to hit, and touch-point error on a phone is about 5-10 px. The crosshair lets the user line up the corner visually. Phone jitter when the screen is pressed is removed by using the **mean of the last 6 aim samples taken before the press** (the press is detected before the phone moves much). If those samples spread more than 8 mm, the add is rejected with "Hold steady". |
| D3 | **4 corners required.** No 3-corner mode in this iteration. | 4 points over-determine a rectangle (5 DOF from 8 numbers), so noisy or wrong taps can be detected and rejected, and the residual is a useful quality check. 3 corners (completing a parallelogram) cannot detect a bad corner and would silently skew the result. A 3-corner fallback for an occluded corner is a possible later addition. |
| D4 | **Plane:** before the first corner, use a polygon raycast (`TrackableType.PlaneWithinPolygon`) or, if that fails, a ray against the **infinite plane** of a tracked plane near the aim point. **At corner 1 the plane is locked.** Corners 2 to 4 always intersect the locked plane's infinite extension (`ARPlane.infinitePlane`, refreshed each frame from the locked `ARPlane`, following `subsumedBy`; if the plane disappears, the cached plane is used). | Paper is textureless, so ARKit's plane polygon often does not cover the paper's far corners. All 4 corners must lie on one plane for the fit to work. Use our own ray-plane math rather than `ARRaycastManager.Raycast(Ray, ...)`: ARKit 6.5 reports `supportsWorldBasedRaycast = false`, so the Ray overload is not supported. At confirm time, all corners are re-projected onto the current locked plane, which absorbs ARKit's height refinement. |
| D5 | **Rectangle fit:** sort the corners by angle around their centroid. Get the orientation from the **4-fold circular mean of the edge directions**, then compute least-squares extents (section 4.2). **Not PCA.** | PCA fails on squares: the covariance of a square's corners is isotropic, so the PCA axes are arbitrary. The 4-fold mean (`theta = atan2(sum L_k sin 4phi_k, sum L_k cos 4phi_k) / 4`) works for every aspect ratio and does not depend on tap order. |
| D6 | **Orientation:** the bottom edge (v = 0) is the fitted edge that **faces the camera at confirm time**. +v is the rectangle axis closest to the camera forward direction projected onto the plane. If the phone points straight down, the camera's up direction projected onto the plane is used instead. +u = the user's right. The preview arrow shows the top edge before confirming. | This matches today's contract ("bottom edge faces the user", `FacingRotation`) and works with any tap order. *Rejected:* "the first two taps define the bottom edge", because it forces a tap order and is easy to get wrong. To make another side the bottom, the user walks to that side before confirming. |
| D7 | **Board size flows at runtime:** `ARBoardSpace` owns `_sideMeters` (0.40 m for quick place, `min(W, H)` for corners). It passes the size to `BoardGeometry` and calls `BoardView.SetSideMeters` / `SetSurfaceSize`. **Stroke widths stay proportional** to board size (today's `Scale`), clamped to `[0.4, 2.0]`. | Users hold the phone at a distance proportional to the board, so proportional strokes keep the same on-screen thickness and the design looks the same at any size. For a 21 cm sheet, Thin/Medium/Thick are about 0.95/1.7/2.9 mm. The clamp stops strokes from vanishing on 10 cm boards or getting very thick on 1.5 m boards. Model geometry always uses the true side. |
| D8 | **Vertical surfaces (easel/wall): recommend deferring.** The math is written to work with any plane normal (`towardTop` and the plane normal are parameters), and an `allowVerticalSurfaces` flag exists but is **off and not wired into plane detection** for P2. | Cost: about 0.5 day of code (alignment filter, `PlaneDetectionMode.Horizontal \| Vertical` in SceneBuilder, `towardTop` = gravity-up projected onto the plane, a quick-place rotation for walls, coach text) plus 0.5-1 day of device testing. Risks: blank walls and blank canvases are textureless, so ARKit often does not detect them. Easel canvases are tilted, and ARKit only classifies horizontal and vertical planes. Stretched canvas thickness (about 2 cm) above the wall plane causes parallax. Drawing with the arm raised is tiring. D-09 already cuts wall support, and the P2 freeze is Sat 5 pm. **This is the user's decision** (section 8, Q2). |
| D9 | **Temporary controls are uGUI buttons, not IMGUI.** They are created at runtime by a new `PlacementControls` component in `App`. | `ARPlacementTapHandler` (and D's future input router) ignores UI by calling `EventSystem.RaycastAll`, which **cannot see IMGUI buttons**. An IMGUI "Undo corner" would also add a corner. uGUI buttons are blocked automatically. Coach text stays in IMGUI `DevHud` because it is not interactive. C replaces both by binding its real UI to `ICornerPlacement`/`IBoardSpace`. |
| D10 | **Default mode = Corners**, with a Quick place button always available in `PlaneFound`. A serialized field allows switching to `QuickPlace`. | This is what the user asked for. If the device checks fail by the Fri 7-9 pm phone gate, set `defaultMode = QuickPlace` for the P2 recording. That is one field and does not affect P2-01..08 (section 8, Q1). |

---

## 3. Contract changes (additive; need a `contract` PR approved by C and D, MVP 2.7)

### 3.1 `Assets/GeoART/Contracts/Board/IBoardSpace.cs`

- Append one enum value **at the end**, so existing ordinals do not change:
  `public enum PlacementState { Unsupported, Scanning, PlaneFound, Placed, TrackingLost, PickingCorners }`
- No interface members change. Update the doc comments:
  - The board is a square of side `ICornerPlacement.BoardSideMeters`: 0.40 m for quick place, or the largest centred square in a corner-registered surface. Board space is still `[0,1]^2`.
  - `PickingCorners`: the user is selecting surface corners. Not placed; projection returns false.
  - `PlaceAtReticle`: "places a 0.40 m board at the aim point (quick place) in either mode".

Impact on consumers: D's input must already block drawing unless `IsPlaced`, and the new value is not placed, so nothing changes for D. C's coach text needs one more case (it can use `ICornerPlacement` for details).

### 3.2 New file `Assets/GeoART/Contracts/Board/ICornerPlacement.cs`

This file uses only `System`. It is also compiled by the dotnet harness (section 6.3).

```csharp
using System;

namespace GeoART.Contracts
{
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
    /// </summary>
    public interface ICornerPlacement
    {
        PlacementMode Mode { get; }
        /// <summary>Ignored while IBoardSpace.IsPlaced. Clears picked corners.</summary>
        void SetMode(PlacementMode mode);

        int RequiredCorners { get; }            // 4
        int CornerCount { get; }                // 0..RequiredCorners
        bool CanAddCorner { get; }              // Mode == Corners, not placed, count < 4, aim valid now
        bool CanConfirm { get; }                // count == 4 and the fit is valid

        /// <summary>Fit issue (4 corners, invalid) > last action issue (for 3 s) > live aim issue > None.</summary>
        CornerIssue Issue { get; }
        int IssueCorner { get; }                // 0-based index in pick order, or -1

        double LiveEdgeMeters { get; }          // last corner -> aim point; NaN if no corner or aim invalid
        double SurfaceWidthMeters { get; }      // fitted extent along +u; NaN unless previewing (CanConfirm) or placed by corners
        double SurfaceHeightMeters { get; }     // along +v; same validity
        double BoardSideMeters { get; }         // current/previewed board side; 0.40 for quick place

        bool TryAddCorner();                    // false and Issue set if not possible
        void UndoCorner();
        void ClearCorners();                    // "Restart"
        bool ConfirmCorners();                  // places the board; State -> Placed

        /// <summary>Raised on add/undo/clear/mode/fit/issue/CanAddCorner/CanConfirm changes. Not raised per frame for LiveEdgeMeters.</summary>
        event Action CornersChanged;
    }
}
```

Registration: `ARBoardSpace` registers `IBoardSpace` **and** `ICornerPlacement` in `Awake`. `FlatBoardSpace` registers both as well. `ICornerPlacement` is optional for consumers (use `TryResolve`).

---

## 4. File-by-file implementation

Order: 4.1 → 4.2 (+ harness + tests) → 4.3 (+ tests) → 4.4 → 4.5 → 4.6 → 4.7 → 4.8 → 4.9 → 4.10. Do not touch B/C/D folders.

Before editing:

- Back up the staging copy: `cp -a /home/macharradm/MO/GeoARt-A-stage /home/macharradm/MO/GeoARt-A-stage.bak-precorner`.
- Recommend that the user commit the current P2 baseline on the Mac. That repo is entirely untracked on `a/p2-ar-foundation`, so this feature cannot be reverted cleanly otherwise.

### 4.1 Contracts

Make the changes in section 3: the enum value, the doc comments, and the new `ICornerPlacement.cs`.

### 4.2 `Assets/GeoART/BoardRender/CornerRectFit.cs` (new, **pure C#: `System` + `GeoART.Contracts` only, no `UnityEngine`**)

The header comment must say it is also compiled by `tools/mathcheck`. Use C# 9 features at most, because that is what Unity 6 supports.

```csharp
namespace GeoART.BoardRender
{
    /// Rectangle in 2D plane coordinates (metres). AxisB = Perp(AxisA) = (-A.Y, A.X).
    public readonly struct FittedRect
    {
        public readonly Vec2 Center, AxisA, AxisB;
        public readonly double LengthA, LengthB, MaxResidual, RmsResidual;
        public readonly int WorstCorner;          // input index
        public FittedRect(Vec2 center, Vec2 axisA, double lengthA, double lengthB, double maxResidual, double rmsResidual, int worstCorner);
    }

    /// Oriented board frame on the plane: Up = board +v, Right = board +u = (Up.Y, -Up.X).
    public readonly struct RectFrame
    {
        public readonly Vec2 Center, Right, Up;
        public readonly double Width, Height;                 // along Right, along Up
        public double SquareSide => Math.Min(Width, Height);
        public Vec2 SurfacePoint(double s, double t) => Center + Right * ((s - 0.5) * Width) + Up * ((t - 0.5) * Height);
        public Vec2 BoardPoint(Vec2 uv) => Center + Right * ((uv.X - 0.5) * SquareSide) + Up * ((uv.Y - 0.5) * SquareSide);
    }

    public static class CornerRectFit
    {
        public const double MinCornerSeparation = 0.05, MinSide = 0.10, MaxSide = 1.50, MaxAspect = 4.0;
        public const double MaxAngleDeviationDeg = 15.0, MinResidualLimit = 0.015, RelResidualLimit = 0.05;

        /// Fills order[0..3] with input indices sorted counter-clockwise by atan2 around the centroid.
        public static void OrderAroundCentroid(ReadOnlySpan<Vec2> pts, Span<int> order);
        /// corners.Length must be 4. Allocation-free (stackalloc). issueCorner = input index or -1.
        public static bool TryFit(ReadOnlySpan<Vec2> corners, out FittedRect rect, out CornerIssue issue, out int issueCorner);
        /// Picks Up in {A, B, -A, -B} with max dot(towardTop); |towardTop| < 1e-9 -> Up = AxisA.
        public static RectFrame Orient(in FittedRect rect, Vec2 towardTop);
    }
}
```

`TryFit` math. `q[k]` are the corners in sorted order, and `idx[k]` maps back to the input index.

1. Reject if not exactly 4 corners or any corner is non-finite (`NotRectangular`, corner -1).
2. **Separation:** for every pair `i < j`, if `|p_i - p_j| < MinCornerSeparation`, return `TooCloseToCorner` with `issueCorner = i`. This is the earlier corner, so the message reads "on corner i+1".
3. **Order:** `c = mean(p)`. Sort the indices by `atan2(p.Y - c.Y, p.X - c.X)` (insertion sort on 4 items).
4. **Convexity:** `e_k = q[k+1] - q[k]` (indices mod 4). `z_k = cross(e_k, e_{k+1}) = e_k.X * e_{k+1}.Y - e_k.Y * e_{k+1}.X`. All `z_k` must be `> 1e-6` (the angle sort is counter-clockwise). Otherwise return `NotConvex` with `issueCorner = idx[k+1]` for the first failing `k`.
5. **Angles:** at sorted corner `k+1`, the interior angle is `acos(dot(-e_k, e_{k+1}) / (|e_k| |e_{k+1}|))`. If any `|angle - 90°| > 15°`, return `NotRectangular` with the worst corner.
6. **Orientation (4-fold mean):** `phi_k = atan2(e_k.Y, e_k.X)`, `L_k = |e_k|`. `S = sum_k L_k * (cos 4phi_k, sin 4phi_k)`. If `|S| < 1e-9 * sum L_k`, return `NotRectangular`. Otherwise `theta = atan2(S.Y, S.X) / 4`, `A = (cos theta, sin theta)`, `B = (-sin theta, cos theta)`.
7. **Extents (least squares, given orientation):** `x_k = dot(q_k - c, A)` and `y_k = dot(q_k - c, B)`. Exactly 2 corners must have `x > 0`, exactly 2 must have `y > 0`, and the four `(sign x, sign y)` pairs must be distinct. Otherwise return `NotRectangular`. Then:
   - `xMin = mean(x | x < 0)`, `xMax = mean(x | x > 0)`, and the same for `y`.
   - `Center = c + A * (xMin + xMax) / 2 + B * (yMin + yMax) / 2`.
   - `LengthA = xMax - xMin`, `LengthB = yMax - yMin`.
8. **Residuals:** the fitted corner for `k` is `(x_k > 0 ? xMax : xMin, y_k > 0 ? yMax : yMin)`. `r_k` = distance from `(x_k, y_k)` to that point. Compute `MaxResidual`, `RmsResidual`, and the worst corner. If `MaxResidual > max(MinResidualLimit, RelResidualLimit * min(LengthA, LengthB))`, return `NotRectangular` with the worst corner.
9. **Size:**
   - `min(LengthA, LengthB) < MinSide` → `TooSmall`
   - `max(LengthA, LengthB) > MaxSide` → `TooLarge`
   - `max / min > MaxAspect` → `TooElongated`

   All of these use corner -1.

`Orient`: choose `Up` from the 4 candidates; `Right = (Up.Y, -Up.X)`; `Width = (Up is ±B) ? LengthA : LengthB`; `Height` is the other length.

### 4.3 `Assets/GeoART/BoardRender/CornerPickSession.cs` (new; Unity math types, no AR Foundation, not a MonoBehaviour)

This class holds the picking state so that `ARBoardSpace`, `FlatBoardSpace` and the EditMode tests share it.

```csharp
public sealed class CornerPickSession
{
    public const int RequiredCorners = 4, SteadyFrames = 6;
    public const float MinAimCos = 0.26f;        // ray within ~75 deg of the plane normal
    public const float MaxAimDistance = 2.5f, MaxSteadySpread = 0.008f;

    public bool HasPlane { get; }  public Vector3 PlanePoint { get; }  public Vector3 PlaneNormal { get; }
    public int CornerCount { get; }
    public Vector3 GetCorner(int i);                     // world, projected onto the current plane
    public bool AimValid { get; }  public Vector3 AimPoint { get; }  public CornerIssue AimIssue { get; }
    public bool HasValidFit { get; }  public FittedRect Fit { get; }   // only when CornerCount == 4
    public CornerIssue FitIssue { get; }  public int FitIssueCorner { get; }
    public CornerIssue LastActionIssue { get; }  public int LastActionIssueCorner { get; }
    public int Version { get; }                          // ++ on every corner/fit change (views rebuild on change)

    public void SetPlane(Vector3 point, Vector3 normal); // every frame; while CornerCount == 0 a jump (>2 cm along n or >5 deg) clears the aim buffer
    public void UpdateAim(Ray ray);                      // every frame
    public bool TryAddCorner();                          // smoothed aim; separation check; fit when count reaches 4
    public void UndoCorner();
    public void Clear();
    public bool TryGetPlacement(Vector3 towardTopWorld, float lift, out Pose pose, out RectFrame frame);

    public Vec2 ToPlane(Vector3 world);  public Vector3 FromPlane(Vec2 p);  public Vector3 FromPlaneDir(Vec2 d);
    public static Vector3 TowardTop(Vector3 normal, Vector3 camForward, Vector3 camUp, bool gravityUp);
}
```

- **Plane basis.** The basis is recomputed from the normal and a fixed world axis, so it is stable across frames:
  - `n = normalized normal`
  - `ex = normalize(ProjectOnPlane(Vector3.right, n))`, or `Vector3.forward` if degenerate
  - `ey = Vector3.Cross(ex, n)`
  - `ToPlane(p) = (dot(p - O, ex), dot(p - O, ey))` with `O = PlanePoint`. This makes 2D plane coordinates behave exactly like board-local (x, z), so a counter-clockwise rotation in 2D is not mirrored in 3D.
  - Checked: `Cross(n, ex) = -ey`, `Cross(n, ey) = ex`, so `LookRotation(Up3D, n) * Vector3.right = Right3D`.
- **Aim.** `denom = dot(ray.dir, n)`; `t = dot(O - ray.origin, n) / denom`.
  - `t <= 0` → `NoSurface`
  - `|denom| < MinAimCos` → `AimTooShallow`
  - `t > MaxAimDistance` → `TooFar`
  - Otherwise push the hit into a `Vector3[SteadyFrames]` ring buffer. An invalid aim resets the buffer count.
- **TryAddCorner.** The buffer must hold `SteadyFrames` samples. Take their mean; every sample must lie within `MaxSteadySpread` of the mean, otherwise fail with `Unsteady`. Check separation against the existing corners. Store the world point in a `Vector3[4]`. At 4 corners, run `CornerRectFit.TryFit` on `ToPlane(corner)` values held in a preallocated `Vec2[4]`, passed as `AsSpan()`.
- **TryGetPlacement.**
  - `frame = CornerRectFit.Orient(Fit, towardTop2D)`, where `towardTop2D = (dot(t, ex), dot(t, ey))`.
  - `pose.position = FromPlane(frame.Center) + n * lift`.
  - `pose.rotation = Quaternion.LookRotation(FromPlaneDir(frame.Up), n)`.
  - The fit is recomputed from the corners re-projected onto the current plane first.
- **TowardTop.**
  - If `gravityUp` (vertical surfaces), return `ProjectOnPlane(Vector3.up, n)`.
  - Otherwise `f = ProjectOnPlane(camForward, n)`; if `f.sqrMagnitude < 0.04`, use `ProjectOnPlane(camUp, n)` instead.
- No allocations after construction. All arrays are allocated once.

### 4.4 `BoardView.cs` (runtime size and surface outline)

- Add `public void SetSideMeters(float side)`. It clamps to `[0.05, 3]`, assigns `sideMeters`, and calls `MarkAllDirty()`.
- Add `public void SetSurfaceSize(float widthMeters, float heightMeters)`. NaN or `<= 0` hides the outline. It marks the base mesh dirty.
- Add `public static float StrokeScaleFor(float side) => Mathf.Clamp(side / BoardGeometry.DefaultSideMeters, 0.4f, 2f);` and use `StrokeScale` (instead of `Scale`) for every width, dash, gap, disc radius and cursor size. Positions and circle radii keep using `sideMeters`.
- In `RebuildBase`, if a surface size is set and `|W - side| > 0.002` or `|H - side| > 0.002`, add a closed dashed polyline at local `(±W/2, 0, ±H/2)`:
  - width `0.0012f * StrokeScale`
  - new field `surfaceOutlineColor = (0.1, 0.8, 1, 0.9)`

  The board is centred on the rectangle (D1), so the outline is symmetric.
- The `[SerializeField] sideMeters` inspector value stays the default.

### 4.5 `Assets/GeoART/BoardRender/CornerPickerView.cs` (new MonoBehaviour)

- Fields:
  - `[SerializeField] Material material`
  - colours: `aim` = white, `corner` = cyan `(0.1, 0.85, 1)`, `bad` = red, `fit` = green `(0.2, 1, 0.4)`, `boardFill` = white with alpha 0.12
- Methods:
  - `public void Show(CornerPickSession s, bool previewValid, in RectFrame frame, float lift)`
  - `public void Hide()`

  `ARBoardSpace` calls them every frame while not placed in Corners mode.
- Mesh setup: one child mesh built with a reused `BoardMeshBuilder`, render queue 3004, no shadows, like `ReticleView`/`BoardView.Part`.
- Each frame, set the transform to `position = PlanePoint + n * lift` and `rotation = LookRotation(ey, n)`. The view's local XZ is then the plane's 2D coordinates, so it draws at `(p.X, 0, p.Y)` using `ToPlane`.
- Rebuild every frame while visible. The mesh is tiny and the lists are reused, so there is zero GC.
- **Aim marker** (when `AimValid`):
  - ring radius `r = Clamp(0.03 * distance(cam, aim), 0.008, 0.03)`, width `0.0015`
  - cross arms from `0.35r` to `1.4r`, with a gap in the centre so the exact point stays visible
  - draw it greyed (alpha 0.4) if the aim is invalid but the last valid point is known
- **Corners:** disc 4 mm. The `IssueCorner` corner is red, the others cyan.
- **Rubber band:** solid segments between the picked corners in pick order. A dashed segment runs from the last corner to the aim point, and a dashed closing segment from the aim point to corner 0 when the count is 3.
- **Preview** (count 4):
  - **Valid:** green rectangle outline at `frame.SurfacePoint` corners. Board square with translucent fill and frame at `frame.BoardPoint` of (0,0), (1,0), (1,1), (0,1). A top arrow at `BoardPoint(0.5, 0.97)`, the same shape as in `ReticleView`.
  - **Invalid:** red outline through the 4 corners in sorted order.
- Widths: `0.002` for outlines and `0.0015` for the band.

### 4.6 `ARBoardSpace.cs` (state machine and placement)

New fields:

- `[SerializeField] PlacementMode defaultMode = PlacementMode.Corners;`
- `[SerializeField] bool allowVerticalSurfaces;` (off; see D8)
- `[SerializeField] CornerPickerView cornerView;`
- `[SerializeField] BoardView boardView;` (falls back to `boardRoot.GetComponent<BoardView>()`)
- `readonly CornerPickSession _picker = new CornerPickSession();`
- `ARPlane _candidatePlane, _lockedPlane;`
- `Pose _aimPose; bool _aimValid;`
- `float _sideMeters; float _surfaceW = NaN, _surfaceH = NaN;`
- `PlacementMode _mode;`
- `float _notTrackingSince = -1;`
- cached `CornerIssue _lastIssue; bool _lastCanAdd, _lastCanConfirm;` (raise `CornersChanged` on change)
- action issue timestamp, cleared after 3 s

Changes:

- **Class/Awake.** Implement `ICornerPlacement`. In `Awake`: `_sideMeters = sideMeters; _mode = defaultMode;`, register `ICornerPlacement` next to `IBoardSpace`, and unregister both in `OnDestroy`.
- **`SideMeters`.** Return `_sideMeters`.
- **`TryScreenToBoard` / `TryBoardToScreen`.** Pass `_sideMeters`.
- **`Update()`.**
  - If placed: unchanged.
  - Otherwise, if the session is not `SessionTracking` with `notTrackingReason == None`:
    - start `_notTrackingSince`
    - hide the reticle and aim marker
    - if `CornerCount > 0` and lost for more than 1.0 s, `ClearCorners()` with issue `TrackingInterrupted`
    - `SetState(CornerCount > 0 ? PickingCorners : Scanning)` and return
    - adding corners is blocked while not tracking (`CanAddCorner` is false)
  - **CornerCount == 0:** `AcquireSurface(centerRay)` (below) sets `_candidatePlane`, `_aimPose` (`hit + n * lift`, `FacingRotation(n, cam.forward)`), and `_aimValid`.
    - QuickPlace mode: position the reticle at `_aimPose` and show it, as today. Hide `cornerView`.
    - Corners mode: hide the reticle. Call `_picker.SetPlane(hit, n)` and `_picker.UpdateAim(centerRay)`, then `cornerView.Show(...)`.
    - `SetState(_aimValid ? PlaneFound : Scanning)`.
  - **CornerCount > 0:**
    - follow `_lockedPlane.subsumedBy` until it is null
    - if `_lockedPlane != null && trackingState == Tracking`, call `_picker.SetPlane(_lockedPlane.transform.position, _lockedPlane.normal)` (the same plane as `infinitePlane`); otherwise keep the last plane
    - `_picker.UpdateAim(centerRay)`
    - if `CornerCount == 4 && HasValidFit`, `TryGetPlacement(TowardTop(...), liftMeters, out _, out _previewFrame)`
    - `cornerView.Show(...)`
    - `SetState(PickingCorners)`
  - `centerRay = arCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f))`.
- **`AcquireSurface(Ray ray)`.**
  1. The existing `raycastManager.Raycast(center, s_Hits, TrackableType.PlaneWithinPolygon)` with the alignment filter `Allowed(plane)`: `HorizontalUp`, or also `Vertical` if `allowVerticalSurfaces`.
  2. If there is no hit, iterate `foreach (var p in planeManager.trackables)` (struct enumerator, no GC). Skip planes that are not tracking, subsumed, or not allowed. Intersect `ray` with `p.infinitePlane` (`Plane.Raycast`) with the cos check `>= MinAimCos` and `0 < t <= 2.5`. The hit must lie within `max(p.extents) + 0.75 m` of `p.center`. Keep the smallest `t`.
- **`PlaceAtReticle()`.** Requires `_state == PlaneFound && _aimValid`. Calls `PlaceBoard(_aimPose, sideMeters, NaN, NaN)`.
- **New `void PlaceBoard(Pose pose, float side, float surfaceW, float surfaceH)`.** This is today's `PlaceAtReticle` body: create the anchor GameObject at `pose`, add `ARAnchor`, reparent `boardRoot`, turn plane detection off. Additionally:
  - `_sideMeters = side`
  - `boardView.SetSideMeters(side)` and `boardView.SetSurfaceSize(surfaceW, surfaceH)`
  - hide the reticle and `cornerView`
  - `_picker.Clear()` and unlock the plane
  - `SetState(Placed)`, `EvaluateSession`, raise `CornersChanged`
- **`TryAddCorner()`.** Guards: Corners mode, not placed, state is `PlaneFound` or `PickingCorners`, count < 4, tracking. If count is 0, set `_lockedPlane = _candidatePlane` (it may be null after the fallback; the cached plane is enough). Then `_picker.TryAddCorner()`. On failure, record the action issue and its time. On success, clear the action issue and `SetState(PickingCorners)`. Raise `CornersChanged` in both cases.
- **`UndoCorner()` / `ClearCorners()`.** Call the picker. When the count reaches 0, `_lockedPlane = null`. The next `Update` sets `PlaneFound` or `Scanning`. Raise `CornersChanged`.
- **`ConfirmCorners()`.** Requires `CanConfirm`. Calls `_picker.TryGetPlacement(towardTop, liftMeters, out pose, out frame)` and then `PlaceBoard(pose, (float)frame.SquareSide, (float)frame.Width, (float)frame.Height)`.
- **`SetMode(m)`.** Ignored if placed. Sets `_mode`, calls `ClearCorners()`, and raises the event.
- **`Reset()`.** Today's behaviour, plus:
  - `_picker.Clear()`, `_lockedPlane = null`
  - `_sideMeters = sideMeters`
  - `boardView.SetSideMeters(sideMeters)`, `boardView.SetSurfaceSize(NaN, NaN)`
  - `_surfaceW = _surfaceH = NaN`
  - `cornerView.Hide()`
  - raise `CornersChanged`
- **`ICornerPlacement` properties.**
  - `SurfaceWidthMeters` / `SurfaceHeightMeters` / `BoardSideMeters` come from `_previewFrame` while `CanConfirm`, otherwise from the placed values.
  - `LiveEdgeMeters = |aim - lastCorner|` if `AimValid && count > 0`, otherwise NaN.
  - `Issue` follows the priority stated in the contract.
- Update the class summary comment with the new state and the "corners lie on the locked plane's infinite extension" rule (consistent with P2 4.3: placed-board projection never raycasts planes).

### 4.7 `ARPlacementTapHandler.cs`

- Resolve `ICornerPlacement _corners` in `Start` (optional).
- `Update`:
  - Return if `_boardSpace == null || _boardSpace.IsPlaced`.
  - `s = State`. Return unless `s` is `PlaneFound` or `PickingCorners`.
  - Return if there is no press this frame or the press is over UI (as today).
  - If `_corners == null || _corners.Mode == QuickPlace`: if `s == PlaneFound`, call `PlaceAtReticle()`. Return.
  - Else if `_corners.CanConfirm`: call `ConfirmCorners()`.
  - Else if `CornerCount < RequiredCorners`: call `TryAddCorner()`.
- Update the summary comment: the tap position is ignored; the crosshair defines the point.

### 4.8 `Assets/GeoART/App/PlacementControls.cs` (new, temporary) and `DevHud.cs`

**`GeoART.App.asmdef`:** add the reference `"UnityEngine.UI"`.

**`PlacementControls`:**

- Header comment: `// TEMPORARY (A) until C's UI binds ICornerPlacement; remove from Main.unity then.`
- `[SerializeField] bool show = true`.
- In `Awake`, build:
  - a `Canvas` (ScreenSpaceOverlay, `sortingOrder` 100)
  - a `CanvasScaler` (ScaleWithScreenSize 390 x 844, match 0.5)
  - a `GraphicRaycaster`
  - a bottom row of buttons, anchored inside `Screen.safeArea` at y 4-11% of the height, using `DefaultControls.CreateButton(new DefaultControls.Resources())`
  - label font `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
- In `Start`, resolve `IBoardSpace` and `ICornerPlacement` and subscribe to `StateChanged` / `CornersChanged` → `Refresh()`. Unsubscribe in `OnDestroy`. No per-frame work.

| Button | Visible when | Action |
|---|---|---|
| Undo corner | `!IsPlaced && CornerCount > 0` | `UndoCorner()` |
| Restart | `!IsPlaced && CornerCount > 0` | `ClearCorners()` |
| Quick place 40 cm | `Mode == Corners && State == PlaneFound && CornerCount == 0` | `IBoardSpace.PlaceAtReticle()` |
| Mode: Corners / Quick | `!IsPlaced && CornerCount == 0 && State != Unsupported` | `SetMode(toggle)`; the label shows the current mode |
| Place board | `CanConfirm` | `ConfirmCorners()` |
| Re-place board | `IsPlaced` | `IBoardSpace.Reset()` |

Because these are uGUI, `ARPlacementTapHandler.IsOverUI` (and D's router) ignore taps on them. Set `DevHud.showResetButton` to false in scenes; it already defaults to false, so the IMGUI reset button is no longer needed.

**`DevHud`:**

- Resolve `ICornerPlacement` (optional).
- `CoachText(PlacementState, ICornerPlacement)`:
  - `PlaneFound` + Corners: "Aim the + at a corner of your sheet and tap (1 of 4)"
  - `PickingCorners`, n < 4: "Corner {n+1} of 4: aim the + at the next corner and tap · edge {LiveEdge * 100:0.0} cm"
  - n = 4 and valid: "Surface {W:0.0} x {H:0.0} cm, board {S:0.0} cm. Tap to place"
  - when `Issue != None`, the issue message replaces the line
  - existing strings are kept for the other states
- Issue messages:

  | Issue | Message |
  |---|---|
  | NoSurface | "Point the + at the table" |
  | AimTooShallow | "Tilt the phone down: view is too flat" |
  | TooFar | "Move closer (within 2.5 m)" |
  | Unsteady | "Hold steady, then tap" |
  | TooCloseToCorner | "That is corner {k}: aim at the next corner" |
  | NotConvex / NotRectangular | "Corner {k} doesn't fit a rectangle: Undo or Restart" |
  | TooSmall | "Too small (min 10 cm side)" |
  | TooLarge | "Too large (max 150 cm side)" |
  | TooElongated | "Too narrow (max 4:1)" |
  | TrackingInterrupted | "Tracking interrupted: corners cleared, start again" |

- **Caching:** rebuild the string only when the tuple (state, mode, count, issue, issueCorner, round(LiveEdge, 1 mm), round(W/H/S, 1 mm)) changes, so there is no per-frame string allocation from the coach.
- New `[SerializeField] bool showMeasurements = true`. When placed and `SurfaceWidthMeters` is finite, show a small label "Surface {W} x {H} cm · board {S} cm" (cached string), used by the device checklist.
- The CoachText comment says "C replaces this".

### 4.9 `FlatBoardSpace.cs` (parity)

- Implement `ICornerPlacement` as a no-op provider and register/unregister it next to `IBoardSpace`:
  - `Mode = QuickPlace`, `RequiredCorners = 4`, `CornerCount = 0`
  - `CanAddCorner` / `CanConfirm` false
  - `Issue = None`, `IssueCorner = -1`
  - `LiveEdge`, `Surface*` = NaN; `BoardSideMeters = sideMeters`
  - methods do nothing or return false
  - `#pragma warning disable 67` for the unused event
- C's `Flat.unity` and D's sandboxes keep working unchanged.
- *Optional, only if time remains:* `simulateCornerPlacement` with a y = 0 table, using the mouse position as the aim. Not required, because the `CornerPickSession` tests cover the flow.

### 4.10 `SceneBuilder.cs`

- **`BuildBoardRootPrefab`:** add child `"CornerPicker"` with `CornerPickerView` (`material` = boardMat). Set `space.cornerView` and `space.boardView` (the Board child's `BoardView`).
- **`BuildARScene`:** `app.AddComponent<PlacementControls>()`. Plane detection stays `PlaneDetectionMode.Horizontal`. Add a code comment: if `allowVerticalSurfaces`, use `Horizontal | Vertical` (deferred, D8).
- Optional: lower `PlaneFill.mat` alpha from 0.18 to 0.12 so paper corners are easier to see.
- Update the summary header comment (BoardRoot now includes CornerPicker; Main includes PlacementControls).
- `Sandbox_A_AR` (star fixture) gets the same changes and is the device test scene. The star on paper is the circle-roundness check.

### 4.11 Repo hygiene

- `.gitignore`: add `/tools/mathcheck/bin/` and `/tools/mathcheck/obj/`.
- Do not create `.meta` files in staging (staging has none). Unity generates them on the Mac at import, and they are committed from the Mac.

---

## 5. Tests (EditMode, `Assets/Tests/EditMode/Board/`, asmdef unchanged)

**`CornerRectFitTests.cs`** (pure math):

1. `ExactA4_AllPermutations_RecoverSizeAndCenter`
   - Setup: an A4 sheet (0.210 x 0.297) rotated 30°, offset (0.4, -0.2); all 24 input orders.
   - Expect: `{LengthA, LengthB}` = {0.210, 0.297} within 1e-9, centre within 1e-9, residual about 0.
2. `NoisyCorners_SizeWithin6mm_AngleWithin3deg`
   - Setup: `new System.Random(1234)`; 200 trials with ±5 mm uniform noise per coordinate.
   - Expect: every trial fits, `|W - 0.21| < 0.006`, axis error < 3°.
3. `Square_OrientationIsDefined`
   - Setup: a 0.30 m square rotated 17°. This is the case where PCA fails.
   - Expect: the axis equals 17° mod 90 within 0.01°.
4. `OrderAroundCentroid_FixesBowTieOrder`
   - Setup: input order 0, 2, 1, 3 of a rectangle.
   - Expect: a convex counter-clockwise cycle.
5. Degenerate cases, each asserting the specific `CornerIssue` and `issueCorner`:

   | Input | Expected issue |
   |---|---|
   | duplicate corner (3 cm apart) | `TooCloseToCorner` |
   | one point inside the triangle of the others | `NotConvex` |
   | 3 collinear points + 1 | `NotConvex` or `NotRectangular` |
   | parallelogram skewed 25° | `NotRectangular` |
   | trapezoid (top 0.15, bottom 0.21) | `NotRectangular` |
   | rectangle with one corner moved 3 cm | `NotRectangular`, worst corner = that index |
   | 0.06 x 0.08 | `TooSmall` |
   | 2.0 x 1.0 | `TooLarge` |
   | 0.12 x 0.60 | `TooElongated` |
   | NaN corner | `NotRectangular` |

6. `Orient_ChoosesAxisClosestToTowardTop`
   - Setup: `towardTop` = ±A, ±B, an oblique vector at 40° from A, and zero.
   - Expect: `Up` as specified; `Right == (Up.Y, -Up.X)`; `Width` and `Height` swap correctly; `SquareSide == min`.
7. `BoardPoint_IsCenteredSquare`
   - Expect: for A4, `BoardPoint(0,0)` and `BoardPoint(1,1)` are `±0.105` along `Right`/`Up` from the centre, i.e. a 4.35 cm margin top and bottom.

**`CornerPickSessionTests.cs`** (Unity types, no AR):

8. `PlacementRotation_IsNotMirrored`
   - Setup: horizontal plane; fit a known rectangle; `TryGetPlacement`.
   - Expect: `rot * Vector3.right ≈ FromPlaneDir(frame.Right)`, `rot * Vector3.forward ≈ FromPlaneDir(frame.Up)`, `rot * Vector3.up ≈ n` (dot > 0.9999).
9. `FullFlow_SyntheticRays_A4` (the end-to-end check)
   - Setup: plane y = 0; camera at (0, 0.35, -0.30) looking at an A4 rectangle centred at (0.05, 0, 0.1), rotated 20°. For each corner, feed `UpdateAim(rayToCorner)` 6 times, then `TryAddCorner()`.
   - Expect: `HasValidFit`, `TryGetPlacement(TowardTop(n, cam.forward, cam.up, false), 0.002f, ...)`, `SquareSide ≈ 0.21`, and the position equals the rectangle centre + 2 mm.
10. `FittedBoard_ScreenRoundTrip_NonDefaultSide`
    - Setup: apply the pose from test 9 to a board transform with `side = 0.21`; test camera with a `RenderTexture` 390 x 844, as in `BoardGeometryTests`.
    - Expect: `TryBoardToScreen` → `TryScreenToBoard` round-trips (0.5, 0.5) and (0.02 / 0.98) corners within 1e-4. Also `board.TransformPoint(BoardToLocal(uv, 0.21))` equals `FromPlane(frame.BoardPoint(uv))` + lift within 1e-4. This is the handedness consistency check between `CornerRectFit` and `BoardGeometry`.
11. `BottomEdgeFacesCamera`
    - Setup: the camera from test 9 (at -Z).
    - Expect: board v = 0 is the edge nearest the camera; `TryBoardToScreen` gives `far.Y > near.Y` and `right.X > left.X`. Move the camera to +X looking -X and expect the frame rotated by 90°.
12. `TowardTop_LookingStraightDown_UsesCameraUp`, and `gravityUp` returns the projected world up for a vertical plane.
13. `Aim_Rejections`
    - Grazing ray (`dot < 0.26`) → `AimTooShallow`.
    - Ray pointing away from the plane → `NoSurface`.
    - Hit 3 m away → `TooFar`.
    - Fewer than 6 samples, or samples spread 2 cm → `TryAddCorner` false, `Unsteady`.
14. `UndoClear_UpdateCountsAndFit`
    - Expect: with 4 corners and an invalid fit, Undo gives 3 corners and clears the fit issue; Clear gives 0 corners; `Version` increments.
15. `CornersOffPlane_AreProjected`
    - Setup: exact rectangle corners in the plane, each lifted ±1 cm along the normal (no in-plane noise).
    - Expect: the corners are projected onto the plane and the fit is exact.
16. `BoardView.StrokeScaleFor`
    - Expect: 0.21 → 0.525, 0.10 → 0.4 (clamped), 1.5 → 2.0.

The existing `BoardGeometryTests` must still pass unchanged.

---

## 6. Build and verify

`REPO="/Users/root1/Desktop/Undergraduate Degree/Fall 26/CAP 4111/GeometricARt"`. The Unity Editor for this project must be closed during batch runs.

### 6.1 Back up the Mac copy, then sync staging

```bash
ssh root1@100.91.35.24 "cd \"$REPO\" && mkdir -p ~/GeoArtBuilds && tar -czf ~/GeoArtBuilds/precorner-\$(date +%Y%m%d-%H%M).tgz Assets tools .gitignore"
cd /home/macharradm/MO/GeoARt-A-stage && tar -cf - . | ssh root1@100.91.35.24 "tar -C \"$REPO\" -xf -"
```

### 6.2 Compile and regenerate the prefabs and scenes (batch compile works; batch tests hang)

```bash
ssh root1@100.91.35.24 "cd \"$REPO\" && /Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath \"$REPO\" -executeMethod GeoART.EditorTools.SceneBuilder.BuildBatch -logFile /tmp/geoart-corners.log; echo exit=\$?; grep -n 'error CS' /tmp/geoart-corners.log; grep -n 'Built A prefabs' /tmp/geoart-corners.log"
```

Pass condition: exit 0, no `error CS`, and the "Built A prefabs" line is present. Also check for `warning CS` in the new files.

### 6.3 Run the pure-math harness without the test runner

Create `tools/mathcheck/` (outside `Assets`, so Unity ignores it).

`MathCheck.csproj`:

- `Exe`, `net8.0`, `LangVersion 9.0`, `UseAppHost false`, `EnableDefaultCompileItems false`
- `Compile Include`: `Program.cs`, `../../Assets/GeoART/Contracts/Math/Vec2.cs`, `../../Assets/GeoART/Contracts/Board/ICornerPlacement.cs`, `../../Assets/GeoART/BoardRender/CornerRectFit.cs`

`Program.cs` re-runs tests 1-7 as plain assertions, prints `PASS`/`FAIL` per check, and exits non-zero on failure. Unity bundles a .NET 8 SDK, so no install is needed:

```bash
ssh root1@100.91.35.24 "cd \"$REPO\" && DOTNET_CLI_HOME=/tmp/geoart-dotnet DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 /Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet run --project tools/mathcheck -c Release"
```

If restore fails offline, report it and rely on 6.4.

### 6.4 Full EditMode tests (the user, in the Editor GUI)

Open the project, then **Window > General > Test Runner > EditMode > Run All**. Expect `BoardGeometryTests`, `CornerRectFitTests` and `CornerPickSessionTests` all green. Record the counts.

### 6.5 Export the Xcode project (signing over SSH is expected to fail)

```bash
ssh root1@100.91.35.24 "cd \"$REPO\" && GEOART_TEAM_ID=F5GPULCDNM tools/ios_build.sh --sandbox --no-install"
```

Unity export step [1/4] must succeed. xcodebuild then fails with `errSecInternalComponent`; that is expected over SSH.

### 6.6 Sign and install (the user, in Terminal on the Mac, iPhone connected and unlocked)

```bash
cd "/Users/root1/Desktop/Undergraduate Degree/Fall 26/CAP 4111/GeometricARt" && GEOART_TEAM_ID=F5GPULCDNM tools/ios_build.sh --sandbox --skip-unity
```

This uses `Sandbox_A_AR` (star fixture). For `Main.unity`, run it again without `--sandbox`; that needs a fresh Unity export first (6.5 without `--sandbox`).

---

## 7. Device checklist (iPhone)

**Setup:**

- A sheet of A4 (21.0 x 29.7 cm) or Letter (21.6 x 27.9 cm) paper on a **textured** table (wood grain, or a patterned cloth around the paper).
- Good light; a ruler.
- Hold the phone 25-40 cm away, **roughly above each corner** when aiming.

**Expected accuracy:** corner error about 0.5-1.5 cm; measured size within ±1 cm of the ruler; outline within about 1-2 cm of the paper edges.

| ID | Maps to | Steps | Pass |
|---|---|---|---|
| C-01 | P2-01 | Install, launch from the Home Screen | Camera feed. Coach text "Move your phone slowly...". Buttons visible: Mode, plus Quick place once a plane is found. |
| C-02 | P2-02 regression | Switch Mode to Quick, tap | A 40 cm board as before. Further taps do nothing. Re-place board works. |
| C-03 | new | Corners mode: aim at each paper corner (any order), tap 4 times | Each tap adds a cyan marker exactly under the + (not under the finger). Live edge length is about 21 / 29.7 cm. After the 4th tap: green outline, square, top arrow, and coach "Surface ~21.0 x 29.7 cm". |
| C-04 | new | Confirm (tap or Place board) | Board placed. The dashed surface outline lies on the paper edges within about 1-2 cm all round, checked from directly above and from 2 oblique angles. The square board is centred with its side equal to the short side. The HUD measurement matches the ruler within ±1 cm. |
| C-05 | P2-02 | Walk around the table for about 20 s, 0.5-1.5 m away | Board and outline stay on the paper (drift within about 1 cm). No second board. Plane visuals are gone (detection off). |
| C-06 | orientation | Before confirming, stand at the short side, then at the long side | The arrow moves so that the bottom edge always faces you. After placement, the board does not rotate as you move. |
| C-07 | D1 | In the Sandbox build, measure the star's circle on the paper along u and along v | Diameter = 0.70 x side (14.7 cm on A4) ±1 cm in **both** directions, so the circle is round, not an ellipse. |
| C-08 | P2-04 | (Only when D's input is in the build) touch the centre and the 4 board corners | The cursor lands at the touched point. Not mirrored or rotated. Taps on the margin are ignored. Otherwise covered by test 10. |
| C-09 | rejections | (a) Tap the same corner twice. (b) Use 3 paper corners plus a point 5 cm off. (c) Pick a 5 cm square. | (a) "That is corner 1". (b) "Corner 4 doesn't fit a rectangle" with a red marker; Undo, re-aim, success. (c) "Too small". |
| C-10 | UI leakage | During picking, tap Undo, Restart and Mode | The corner count changes only as each button says. A button tap **never** adds a corner. |
| C-11 | plane extension | Paper with one corner over a plain or white part of the table, or near the table edge | The corner is still accepted (infinite plane). The outline is still correct. |
| C-12 | tracking | (a) During picking, cover the camera for 2 s. (b) After placing, cover it. | (a) "Tracking interrupted: corners cleared". (b) TrackingLost coach text, then recovery; the board is in the same place (as today). |
| C-13 | P2-08 / re-place | Re-place board, then pick again; also background the app during picking | Back to Scanning with plane detection on, size reset, corners cleared. A new registration works. |
| C-14 | perf | FPS overlay on (Sandbox) during picking and after placing | ≥ 30 fps (about 60 expected) with no hitching. |

Record the commit/build number, device and iOS version, measured W x H versus the ruler for 3 runs, and pass/fail per row.

---

## 8. Risks and open questions for the user

**Open questions:**

| # | Question | Recommendation |
|---|---|---|
| Q1 | Default mode for the P2 recording: Corners or Quick place? | **Corners**, with the Quick button. If C-03 to C-05 do not pass by the Fri Oct 30 7-9 pm phone gate, ship with `defaultMode = QuickPlace` and mention corners as in-progress. |
| Q2 | Vertical surfaces (easel/wall canvas): now behind a flag, or after P2? | **After P2** (D8). The math is ready for it. Enabling it later is about 1-1.5 days including device testing, and blank canvases and walls often are not detected. |
| Q3 | Non-square surfaces: is the "largest centred square, margins unused" behaviour acceptable for now? | Yes for P2. Using the full rectangle needs an aspect-ratio contract change across B/C/D (D1 future path). Also confirm centred versus bottom-aligned square; recommend centred. |
| Q4 | What will you register in practice: thin paper or a **stretched canvas (about 2 cm thick)**? | A thick object's top sits above the table plane, so aiming at an angle gives a parallax error of about thickness x tan(view angle): about 2 cm at 45°. The workaround is to aim from directly above each corner. A later fix is a "surface height" offset or LiDAR depth. |
| Q5 | Contract PR | `PlacementState.PickingCorners` and `ICornerPlacement` need sign-off from C and D (MVP 2.7). C's coach text should handle the new state. |

**Risks:**

- **Plain table:** white paper on a plain white table means no plane and no contrast, so a textured table is required (coach text and checklist).
- **Timing:** the work is about 1-1.5 days for A (math and tests about 0.5 day, `ARBoardSpace`/views/UI about 0.5 day, build and device about 0.5 day). It competes with A's P2 integration (`Main.unity` composition, A-5).
- **Fallback:** keep the change isolated so `defaultMode = QuickPlace` restores today's behaviour exactly.
