# Snap Points and H/V Lock: Change Report

Audience: a teammate (or a future Claude session) who needs to upgrade or change snapping without re-reading the code.
This report follows the **merged code**; where the code differs from `docs/SNAP_PATTERN_PLAN.md`, the code wins and
the difference is noted. Baseline: `grid-and-android` at `9270ba3` (plan commit `2a0f6b4` on `feature/snap-pattern`).

## 1. Summary

_In progress. Filled in by the final pass (R1)._

## 2. Files changed

### C0 (contracts)
- **Contracts**
  - `Contracts/Tools/ToolTypes.cs`: appended `SnapKind.Intersection` (last, so existing ordinals are unchanged).
  - `Contracts/Session/ISnapMetric.cs` (new): board -> screen projection plus tolerance in pixels for snapping and taps.
  - `Contracts/Session/IEditorSession.cs`: added `OrthoLock` and `SetOrthoLock(bool)`.
- **Engine**
  - `Engine/EditorSession.cs`: snap state now lives here: `SnapMetric`, `FallbackSnapTolerance`, `SnapEnabled` (default
    true), `SetSnapEnabled`, `OrthoLock`, `SetOrthoLock`, and `SnapCandidates` (empty when snapping is off). It also adds
    three `partial void` hooks for the snap partial, and `RaiseBoardChanged` now calls `OnModelChanged` first.
  - `Engine/EditorSession.Tools.cs`: removed the old `SnapEnabled => false` and the no-op `SetSnapEnabled` (moved to core).
- **Mocks**
  - `Mocks/ForBoard/StubEditorSession.cs`: `OrthoLock` / `SetOrthoLock` (state only, raises `StateChanged`).
- **Tests**
  - `Tests/PlayMode/Verifier/VerifierInputRouterPlayTests.cs`: `RecordingSession` forwards `OrthoLock` / `SetOrthoLock`.
  - `tools/enginecheck/ToolChecks.cs`: `DefaultToolIsLine` now expects snapping on, lock off and no metric. New check
    `SnapAndOrthoTogglesRaiseStateOnce`.

### E1 (geometry + SnapIndex)
- **Engine**
  - `Engine/Geometry/Intersect.cs` (new): allocation-free segment/segment, segment/circle, circle/circle, infinite
    line/circle and infinite line/segment intersection.
  - `Engine/Snap/SnapIndex.cs` (new): builds the deduplicated, priority-ordered snap candidate list for a `Board`, with
    a lazy rebuild (`Sync`) driven by a dirty flag and a cheap board signature. Not wired into `EditorSession` yet (E2).
- **Tests**
  - `tools/enginecheck/SnapChecks.cs` (new): 21 checks for `Intersect` and `SnapIndex`.

No existing file was changed. Unity will generate `.meta` files for the two new folders and two new scripts on the
next import; they are not part of this commit.

### U1 (screen snap metric)
- **App**
  - `App/ScreenSnapMetric.cs` (new): the App's `ISnapMetric`. It wraps `IBoardSpace.TryBoardToScreen` (resolved lazily from `ServiceHub`) and returns a 24 reference px tolerance times the CanvasScaler factor.
  - `App/EngineInstaller.cs`: creates the `EditorSession` with `SnapMetric = new ScreenSnapMetric()`.

### U2 (ToolBar toggles)
- **App**
  - `App/ToolBar.cs`: adds `Snap` and `HV` buttons as a fourth group, so the column grows from 6 to 8 buttons. `ButtonH` drops from 58 to 56. Adds the `ColumnHeight` constant, takes the scaler constants from `ScreenSnapMetric`, and adds two glyphs.
- **Tests**
  - `Tests/EditMode/Verifier/VerifierToolBarToastTests.cs`: adds `ToolBarSnapAndHVButtonsToggleSession` and `ToolBarEightButtonsFitInsideCanvas`.

### U3 (overlay dots)
- **BoardRender**
  - `BoardRender/BoardView.cs`: candidate dots move into their own mesh part, `Candidates`, drawn in four tiers by `SnapKind`. The overlay and candidate loops use index loops, with no boxed enumerators.

### E2 (snapping in Line/Circle, tap-to-connect, Snap toggle)
- **Engine**
  - `Engine/EditorSession.Snap.cs` (new): owns the `SnapIndex`, implements the three C0 hooks (`EnsureCandidates`,
    `OnModelChanged`, `OnSnapSettingsChanged`) and the private snap query `TrySnap`. Adds `HoverRingRadius` and
    `TapSlopFraction`.
  - `Engine/EditorSession.Tools.cs`: Down/Move/Up now snap; tap detection; tap-to-connect with a pending start;
    hover ring and pending ring in `Preview`. Adds `PendingConnectStart`. The Line hint now reads
    "Line: drag from start to end, or tap two points".
- **Tests**
  - `tools/enginecheck/SnapToolChecks.cs` (new): 22 checks for snapping in the tools, taps, rings, metrics, allocation.
  - `tools/enginecheck/PatternChecks.cs` (new): the golden `SnapBuildsTiled2x2`.
  - `tools/enginecheck/ToolChecks.cs`: `TooShortIsDiscarded` now calls `s.SetSnapEnabled(false)` (see section 8, E2).

`EditorSession.cs`, `SnapIndex.cs`, `Intersect.cs` and Contracts are unchanged. Unity will generate
`EditorSession.Snap.cs.meta` on the next import; it is not part of this commit.

### Q1 follow-up (exact circle divisions)
- **Contracts**
  - `Contracts/Model/CircleDivisions.cs` (new): exact cos/sin table for the 8 circle divisions.
  - `Contracts/Fixtures/GoldenBoards.cs`: `StarPoint` now reads from the table. **Team note: this changes fixture
    coordinates by at most 1 ulp** (e.g. (0.5, 0.25000000000000006) becomes (0.5, 0.25)). Nothing visible changes. A
    test that recomputes star points with `Math.Cos`/`Math.Sin` and compares with `==` would now need
    `CircleDivisions.Point` instead. None of the current tests do.
- **Engine**
  - `Engine/Snap/SnapIndex.cs`: circle-division candidates come from `CircleDivisions.Point`.
- **Checks**
  - `tools/enginecheck/PatternChecks.cs`: `SnapBuildsTiled2x2` is exact everywhere (seam exception removed). Adds
    `CircleDivisionTableIsExact`.

### Q3/Q4 follow-up (dot visibility, sandbox camera)
- **BoardRender**
  - `BoardRender/BoardView.cs`: candidate dots are now a dark fill inside a light outline, in 4 tiers (new colour fields, see section 4, U3).
- **Mocks**
  - `Mocks/ForBoard/FlatCameraFit.cs` (new): fits the flat-sandbox camera so the whole board is visible at any aspect ratio.
  - `Mocks/ForBoard/FlatBoardSpace.cs`: adds `BoardTransform` (read-only) for `FlatCameraFit`.
- **Editor**
  - `Editor/Build/SceneBuilder.cs`: `BuildFlatScene` adds `FlatCameraFit` to the camera, so a regenerated `Sandbox_A_Flat` keeps the fit.
- **Scenes**
  - `Scenes/Sandbox/Sandbox_A_Flat.unity`: `FlatCameraFit` on Main Camera (the only scene change).

### E3 (H/V lock)
- **Engine**
  - `Engine/EditorSession.Snap.cs`: adds the private lock query `TryOrthoEnd` (with helpers `OnAxis`, `Clamp01`,
    `TryScreenDirection` and the private struct `OrthoSearch`), and lock handling inside `OnSnapSettingsChanged`
    (it detects a lock toggle and calls `OnOrthoLockToggled`).
  - `Engine/EditorSession.Tools.cs`: Move/Up go through `ResolvePoint` (locked end for the Line tool with the lock on,
    plain snap otherwise); `OnOrthoLockToggled` re-tracks an open Line gesture; `Tap` refuses a non-aligned pair with
    the lock on. Adds `OrthoLockToast`.
- **Checks**
  - `tools/enginecheck/OrthoChecks.cs` (new): 13 checks.

`EditorSession.cs`, `SnapIndex.cs`, `Intersect.cs`, Contracts and the App are unchanged. No new files under `Assets/`,
so no .meta files are needed (`OrthoChecks.cs` is outside `Assets/`).

## 3. Contract changes

### C0
```csharp
// Contracts/Tools/ToolTypes.cs (append-only)
public enum SnapKind { None, UserPoint, Endpoint, Center, CircleDivision, CellCorner, CellEdgeMid, CellCenter, Intersection }

// Contracts/Session/ISnapMetric.cs (new)
public interface ISnapMetric
{
    bool TryToScreen(Vec2 board, out Vec2 screenPx);   // board -> screen px, perspective-correct; false if not projectable
    double TolerancePx { get; }                         // snap and tap radius in screen px
}

// Contracts/Session/IEditorSession.cs (added)
bool OrthoLock { get; }
void SetOrthoLock(bool on);                             // raises StateChanged when the value changes

// Engine/EditorSession.cs (concrete class only, not on the interface)
public ISnapMetric SnapMetric { get; set; }             // null = board-unit fallback
public const double FallbackSnapTolerance = 0.02;       // board units, used when SnapMetric is null
```
`SnapEnabled`, `SnapCandidates` and `SetSnapEnabled` were already on `IEditorSession`. They are now implemented:
`SnapEnabled` defaults to **true**, and `SetSnapEnabled` raises `StateChanged` only when the value changes.

Implementers updated: `Engine/EditorSession` (real), `Mocks/ForBoard/StubEditorSession`, and `RecordingSession` in
`Tests/PlayMode/Verifier/VerifierInputRouterPlayTests.cs`. There are no other `: IEditorSession` types.

### E1
Both types are new and live in `namespace GeoART.Engine`. No Contracts type changed.
```csharp
// Engine/Geometry/Intersect.cs
public static class Intersect
{
    public const double ParamEps = 1e-9;          // board units: "on the segment" slack and tangency band
    public const double ParallelSinEps = 1e-12;   // |sin(angle)| at or below this = parallel

    // All allocation-free. Return the number of points written (0..1 or 0..2); unused outs are Vec2.Zero.
    public static int SegmentSegment(Vec2 a0, Vec2 a1, Vec2 b0, Vec2 b1, out Vec2 p);
    public static int SegmentCircle(Vec2 a, Vec2 b, Vec2 center, double radius, out Vec2 p0, out Vec2 p1);   // p0 nearer a
    public static int CircleCircle(Vec2 c0, double r0, Vec2 c1, double r1, out Vec2 p0, out Vec2 p1);       // p0 left of c0->c1
    public static int LineCircle(Vec2 origin, Vec2 dir, Vec2 center, double radius, out Vec2 p0, out Vec2 p1); // p0 first along dir
    public static int LineSegment(Vec2 origin, Vec2 dir, Vec2 b0, Vec2 b1, out Vec2 p);
}

// Engine/Snap/SnapIndex.cs
public sealed class SnapIndex
{
    public const double DedupEpsilon = 1e-7;
    public const double BoundsSlack = 1e-9;
    public const int CircleDivisions = 8;
    public bool IncludeShapeGridCrossings;                     // default false; a change is picked up by Sync
    public IReadOnlyList<SnapCandidate> Candidates { get; }    // by priority (high first), then source order
    public static int Priority(SnapKind k);                    // 0 = UserPoint (highest) ... 8 = None
    public static Vec2 PolygonVertex(PolygonShape poly, int k); // same expression BoardView draws with
    public void MarkDirty();
    public bool Sync(Board board);                             // rebuilds if dirty or signature changed; true if rebuilt
    public void Rebuild(Board board);                          // unconditional; null board = empty list
}
```
Additions beyond the plan's required API: `Intersect.ParamEps`, `Intersect.ParallelSinEps`, `SnapIndex.BoundsSlack`,
`SnapIndex.CircleDivisions` and `SnapIndex.PolygonVertex`. `Candidates` returns the same list instance for the life
of the index; it is cleared and refilled in place on rebuild.

### E2
No Contracts or `IEditorSession` change. New public members on the concrete `EditorSession` only:
```csharp
// Engine/EditorSession.Snap.cs
public const double HoverRingRadius = 0.012;          // board units: hover ring and pending ring
public const double TapSlopFraction = 10.0 / 24.0;    // tap slop = this * snap tolerance, in the tolerance's space

// Engine/EditorSession.Tools.cs
public Vec2? PendingConnectStart { get; }              // pending tap-to-connect start (exact candidate position) or null
```
Behaviour changes behind existing members: `Pointer` snaps and handles taps; `Preview` can now hold ring
`CircleShape`s (Thin, solid, current colour, radius `HoverRingRadius`), including the pending ring while no gesture is
open; `SetSnapEnabled(false)`, `SetTool`, `ClearBoard`/`LoadBoard` and `Cancel` clear the pending start;
`SnapCandidates` is now filled.

### Q1 follow-up
```csharp
// Contracts/Model/CircleDivisions.cs (new)
public static class CircleDivisions
{
    public const int Count = 8;
    public const double Diagonal = 0.70710678118654752440;   // sqrt(2)/2, used for both cos(45) and sin(45)
    public static double Cos(int k);                        // k mod 8, from the exact table {1, D, 0, -D, -1, -D, 0, D}
    public static double Sin(int k);                        // {0, D, 1, D, 0, -D, -1, -D}
    public static Vec2 Point(Vec2 center, double radius, int k);   // center + radius * (Cos(k), Sin(k))
}
// GoldenBoards.StarPoint(center, radius, k) == CircleDivisions.Point(center, radius, k)  (signature unchanged)
```

### E3
No Contracts or `IEditorSession` change. One new public member on the concrete `EditorSession`:
```csharp
// Engine/EditorSession.Tools.cs
public const string OrthoLockToast = "H/V lock is on: tap two points in the same row or column";
```
Behaviour changes behind existing members: with `OrthoLock` on, `Pointer` constrains the Line tool's end to the
anchor's board X or Y axis; `SetOrthoLock` during an open Line gesture updates `Preview`/`Cursor` at once and raises
`PreviewChanged`; a tap-to-connect pair that shares neither X nor Y raises `Toast(OrthoLockToast)` and commits nothing.

## 4. How it works

### C0: extension points in the shared core
`EditorSession.cs` is the shared core file. It declares three `partial void` hooks so that snapping can live entirely
in `EditorSession.Snap.cs` without editing the core again:
- `EnsureCandidates()`: called by the `SnapCandidates` getter while snapping is on. It brings the private
  `_candidates` list up to date, lazily, behind a dirty flag. If no snap partial implements it, the list stays empty.
- `OnModelChanged(BoardChangeKind)`: called inside `RaiseBoardChanged`, before any `BoardChanged` listener runs, so the
  candidate cache can be marked dirty before the renderer reads `SnapCandidates`.
- `OnSnapSettingsChanged()`: called when `SnapEnabled` or `OrthoLock` changes, before `StateChanged` is raised.

When snapping is off, `SnapCandidates` returns a shared empty array and never calls `EnsureCandidates`.

### E1: SnapIndex and Intersect

**Sources and priority.** `Rebuild` emits sources in this order. `Priority` returns the same order, 0 to 7, and
`None` or unknown kinds get 8:

| Priority | Kind | Source |
|---|---|---|
| 0 | `UserPoint` | `Layer.SnapPoints[i].Position`, visible layers |
| 1 | `Endpoint` | `LineShape.A`, `LineShape.B`; polygon vertices via `SnapIndex.PolygonVertex` |
| 2 | `Intersection` | every pair of drawn primitives (see below); optionally primitives x grid lines |
| 3 | `CellCorner` | grid intersections: the `CellCorner` entries of `TileGrid.SnapPoints(board.GridKind, board.GridN, list)` |
| 4 | `Center` | `CircleShape.Center`, `PolygonShape.Center` |
| 5 | `CircleDivision` | per circle, k = 0..7: `CircleDivisions.Point(c, r, k)`, the exact table shared with `GoldenBoards.StarPoint` (see "Exact circle divisions" below; E1 originally used `Math.Cos`/`Math.Sin`) |
| 6 | `CellEdgeMid` | `TileGrid.SnapPoints` entries of that kind |
| 7 | `CellCenter` | `TileGrid.SnapPoints` entries of that kind |

The plan lists `CellEdgeMid` and `CellCenter` on one line. They get separate ranks (6, 7) so that `Priority` is a
total order. `TileGrid` never emits the two kinds at the same position, so this does not change any result.

Within a source, the order is layer index (0 = bottom) then shape index or snap-point index. For polygons it is
vertex k. For intersections it is pair (i, j) with i < j over the flattened primitive list. The grid sources keep
`TileGrid`'s own order. Shapes and user points come only from layers with `Visible == true`. Grid points do not depend
on layers, so an empty board has the full grid: 9 corners, 12 edge midpoints and 4 centres on the default 2x2.

**Drawn primitives.** Each visible line is a segment. Each visible circle is a circle, provided `Radius > 0`, the
radius is finite and the centre is finite. Each visible polygon with `Sides >= 3`, finite `Radius > 0` and a finite
centre and rotation becomes `Sides` segments between consecutive vertices. These are the same conditions `BoardView`
uses to decide what to draw. Circles and polygons that fail them contribute nothing: no centre, divisions,
vertices or crossings. Pairs are tested with segment/segment, segment/circle or circle/circle as appropriate. Adjacent
polygon edges "cross" at their shared vertex, and that point dedups into the vertex's `Endpoint`.

**Optional grid crossings.** When `IncludeShapeGridCrossings` is true, every primitive is also intersected with every
segment from `TileGrid.UniqueEdges(kind, n, list)`, with kind `Intersection`. These are emitted after the
shape/shape crossings and before `CellCorner`.

**Filter.** Every candidate passes through one `Add`. Non-finite points are dropped. So are points with x or y outside
`[-BoundsSlack, 1 + BoundsSlack]`. This applies to all sources (a circle division can fall off the board), not only
intersections.

**Dedup.** `Add` uses a uniform hash grid with cell side `DedupEpsilon` (key = `floor(x / eps)`, `floor(y / eps)`,
packed into one `long`; each bucket chains candidate indices). A new point is compared with the points in its own
cell and the 8 neighbouring cells. If any accepted candidate lies within `DedupEpsilon` (Euclidean), the new point is
dropped. Sources are emitted in priority order, so the candidate already in the list always has the same or higher
priority and arrived earlier. The survivor therefore keeps the winning candidate's position and kind unchanged, and
the output list is sorted by priority and then by source order with no separate sort step. Merging is greedy: a
point is compared with accepted candidates, not with points that were dropped. If A is accepted, B lies 0.6e-7 from A
(dropped), and C lies 0.6e-7 beyond B (1.2e-7 from A), then C survives. With real geometry this does not come up.

**Exactness.** Positions are never recomputed or averaged. Endpoints are the shape fields. Centres are
`CircleShape.Center` / `PolygonShape.Center`. User points are `SnapPoint.Position`. Grid points are the `Vec2` values
`TileGrid` returned. Divisions use the expression above. A downstream `==` against these values holds.

**Lazy rebuild.** `Sync(board)` rebuilds when `MarkDirty()` was called since the last build, or when the signature
differs. The signature is: board reference, `GridN`, `GridKind`, `IncludeShapeGridCrossings`, `Layers.Count`, and per
layer index: `Id`, `Visible`, `Shapes.Count`, `SnapPoints.Count`. Otherwise it returns false. That path only compares
ints and bools against a stored `List<LayerSig>` and does not allocate (checked with
`GC.GetAllocatedBytesForCurrentThread`). The signature does not catch edits that keep every count: moving, rotating
or scaling a shape, moving a user point, or swapping one shape for another. The owner (E2) must call `MarkDirty()`
from `OnModelChanged`. A new index starts dirty.

**Allocation on rebuild.** `Rebuild` clears and reuses its lists (`_candidates`, the bucket dictionary and chain, the
primitive list). Grid points are cached per (kind, clamped n), and `TileGrid.SnapPoints` (which allocates internally)
runs only when the layout changes. `UniqueEdges` is cached the same way when the flag is on. The pair loop is
O(P^2) in drawn primitives: 300 lines give about 45k pair tests per rebuild. A rebuild happens only after a model
change, never per pointer Move.

**Intersection math (`Intersect`).** All lengths are in board units.
- *Segment/segment.* `r = a1 - a0`, `s = b1 - b0`, `denom = cross(r, s)`. If `|denom| <= ParallelSinEps * |r| * |s|`
  (parallel or collinear, overlapping or not), or either segment has zero length, the result is 0. Otherwise
  `t = cross(b0 - a0, s) / denom` and `u = cross(b0 - a0, r) / denom`. The point counts if both are in `[0, 1]` with a
  slack of `ParamEps / length` (that is, `ParamEps` board units). If t or u is within that slack of 0 or 1, the
  matching end (`a0`, `a1`, `b0`, `b1`, checked in that order) is returned bit for bit. That is how a T-junction
  reports the touching endpoint exactly. Otherwise the point is `a0 + r * t`.
- *Segment/circle and line/circle.* Use the unit direction `u` and the foot of the perpendicular from the centre,
  `foot = origin + u * dot(c - origin, u)`. Let `dist = |foot - c|`. If `dist > R + ParamEps` the result is 0. If
  `dist >= R - ParamEps` it is a tangent: 1 point, the foot. Otherwise the points are `foot -+ u * sqrt(R^2 - dist^2)`.
  Building the points from the foot keeps axis-aligned cases exact (a horizontal line keeps its y). For the segment
  variant, each point counts if its distance along the segment is in `[-ParamEps, len + ParamEps]`. Within
  `ParamEps` of an end it snaps to that end exactly. A segment entirely inside the circle gives 0.
- *Circle/circle.* `d = |c1 - c0|`. If `d <= ParamEps` (concentric, identical circles included) the result is 0. It is
  also 0 if `d > r0 + r1 + ParamEps` or `d < |r0 - r1| - ParamEps`. Within `ParamEps` of either bound, or when the
  computed half-chord squared is not positive, it is a tangent: 1 point at `c0 + u * (+-r0)`. The sign of
  `along = (d^2 + r0^2 - r1^2) / 2d` picks the side, which handles outside tangency and inside tangency in either
  argument order. Otherwise there are 2 points, `c0 + u * along -+ perp(u) * sqrt(r0^2 - along^2)`, with p0 on the left
  of c0 -> c1.
- *Line/segment.* Same as segment/segment, but only `u` (along the segment) is range-checked. The point is
  `origin + dir * t`, so a horizontal or vertical lock line keeps its fixed coordinate exactly. Touching a segment end
  returns the end exactly.
- Every function returns 0 for a zero-length segment or direction, a radius <= 0, or non-finite input, and never
  writes a non-finite point.

### U1: from screen pixels to the engine
`EngineInstaller.Awake` gives the real session a `ScreenSnapMetric`. `TryToScreen` calls the registered `IBoardSpace` on every query: `FlatBoardSpace` in the Editor sandbox, `ARBoardSpace` on device. The lookup is a dictionary read with no allocation. The board space is resolved lazily because `FlatBoardSpace`/`ARBoardSpace` run `Awake` at the same execution order (-100) as `EngineInstaller`, so they can register in either order. `TolerancePx` = `ToleranceReferencePx` (24) times `ScaleFactor(Screen.width, Screen.height)`. That is the CanvasScaler ScaleWithScreenSize formula for 390x844 with match 0.5, the same scaler the ToolBar uses, so a tolerance of 24 px looks the same size as 24 px of UI. Verified in Play Mode in `Sandbox_A_Flat` at 390x844: tolerance 24 px, and board (0.5, 0.5) projects to screen (195, 422).

### U2: toolbar
There are four groups of two: Line/Circle, Dotted/Width, Grid kind/Tiles, and Snap/HV. The Snap button calls `SetSnapEnabled(!SnapEnabled)` and HV calls `SetOrthoLock(!OrthoLock)`. Both are orange while on. They refresh on the session's `StateChanged`, so a change made elsewhere (code, tests) updates the colours too. The GameObject names are `Snap` and `HV` (label "H/V"). The existing names `Line`, `Circle`, `Square` and `2x2` are kept.

### U3: candidate rendering
`BoardView` now has four mesh parts: Base (queue 3000), Shapes (3001), **Candidates** (3002), and Overlay (3003, ghosts and cursor). The Candidates part is rebuilt only on `BoardChanged` and `StateChanged` (the Snap toggle). Overlay is rebuilt on `PreviewChanged`, which fires during a drag, so dragging no longer re-meshes every dot. The dots are drawn lowest tier first, so strong dots sit on top:

| Tier | Kinds | Fill radius (0.40 m board) | Outline | Colour fields |
|---|---|---|---|---|
| 0 | `CellEdgeMid`, `CellCenter` | 1.9 mm | 0.9 mm | `gridDotFill` / `gridDotOutline`, both at 60% alpha (`MinorGridAlpha`) |
| 1 | `CellCorner` (grid intersections) | 2.5 mm | 0.9 mm | `gridDotFill` (dark, 85%) / `gridDotOutline` (white, 95%) |
| 2 | `Center`, `CircleDivision`, `UserPoint` | 3.0 mm | 1.1 mm | `guideDotFill` (blue) / `dotOutline` (white) |
| 3 | `Intersection`, `Endpoint` | 3.6 mm | 1.1 mm | `shapeDotFill` (deep blue) / `dotOutline` (white) |

Each dot is a light outline disc with a dark fill disc on top (`BoardView.AddDot`). The dark fill reads on the light sheet and the light rim reads on dark strokes and on a dark real surface in AR (decision Q3, 2026-11-03; the first U3 version used one colour per tier and was too faint on the grey sheet). Checked with 4x renders of `Sandbox_A_Flat` with the sheet on and with `showBase` off (dark background). The colour field names are new, so the old `candidateColor` value serialized in `BoardRoot.prefab` / `FlatBoardRoot.prefab` is ignored.

The radii and outlines scale with `StrokeScale`, like strokes. The hover ring and the pending tap-to-connect ring are `Preview` shapes from the engine, so they draw in the Overlay with `previewColor` (orange).

### E2: snapping in the tools and tap-to-connect

**Data flow.** `BoardInputRouter` -> `Pointer(e)` -> `TrySnap(raw)` -> the snapped point (a candidate's own `Position`,
or the raw point if nothing is in range) becomes the Line start / Circle centre on Down (`_anchor`) and the Line end /
radius point on Move and Up (`_current`). The ghost, the cursor and the commit all use those snapped values, so a
committed `LineShape.A/B` or `CircleShape.Center` is `==` the candidate, and a snapped radius is
`Vec2.Distance(centre, candidate)` exactly. Nothing is rounded or re-derived after the snap.

**Where the candidates come from.** `EditorSession.Snap.cs` holds one `SnapIndex`. `EnsureCandidates` (called by the
`SnapCandidates` getter and by `TrySnap`) runs `SnapIndex.Sync(_board)` and copies its list into the core's
`_candidates` only when Sync rebuilt. What invalidates it:
- `OnModelChanged` (every `RaiseBoardChanged`: commits, layer commands, grid changes, `LoadBoard`) calls `MarkDirty`,
  because the SnapIndex signature cannot see an in-place edit such as a moved shape.
- The signature itself catches a different board instance, GridN/GridKind, layer count, and per-layer Id, Visible,
  shape count and user-point count, so direct model edits that change those are also picked up.
Sync never runs a rebuild during a drag (nothing changes the model between Down and Up), so Move only scans the list.

**Snapping rule (`TrySnap`).** Off (`SnapEnabled == false`) means no candidates and no snap. Otherwise:
- With `SnapMetric` set and the pointer projectable (`TryToScreen` true and finite), distances are in screen pixels
  and the radius is `SnapMetric.TolerancePx`. Candidates the metric cannot project are skipped.
- Otherwise (no metric, or the pointer itself not projectable), distances are in board units with identity
  projection and radius `FallbackSnapTolerance` (0.02).
- Squared distances are compared; "within" is `d2 <= tol * tol`. The nearest wins; on an exactly equal distance the
  lower `SnapIndex.Priority` wins; then the earlier list index. The list is already in priority order, so in practice
  the strict `<` scan gives "nearest, then first in the list".

**Tap detection.** At Down the engine records the raw Down position, projected with the metric if it can (else board
units), and the slop `TapSlopFraction * tolerance` in that same space (10 px of 24 px with a metric; 0.00833 board
units without). Every later event of the gesture (Moves and the Up) is measured against the Down position. The first
sample at or beyond the slop, off the board, non-finite, or not projectable turns the gesture into a drag for good
(so moving out and back is still a drag). A drag drops the pending start at that moment. Up then either runs the
normal drag commit (unchanged apart from snapping) or, for a tap, the tap step below. A tap never goes through the
drag commit, so a tap never commits a zero-length shape.

**Tap-to-connect state machine (Line tool only).** The tap's candidate is `TrySnap(raw Down position)`.

| State | Event | Result |
|---|---|---|
| no pending | tap on candidate C | pending = C.Position; pending ring stays in `Preview` |
| no pending | tap off any candidate | nothing |
| pending P | tap on candidate C, C != P, length >= `MinStrokeLength`, layer visible | commit `LineShape(P, C)` (current style) into the layer active at Down; one `BoardChanged(Shapes)`; pending cleared |
| pending P | tap on C, C != P, length < `MinStrokeLength` | nothing committed; pending cleared |
| pending P | tap on C, layer hidden or missing | `HiddenLayerToast`; pending cleared (also if the Down itself is refused for a hidden layer) |
| pending P | tap on P again | pending cleared (toggle) |
| pending P | tap off any candidate, Down off the board | pending cleared |
| pending P | gesture becomes a drag | pending cleared at once; the drag draws a normal line |
| pending P | Cancel (with or without a gesture), `SetTool` to another tool, `ClearBoard`/`LoadBoard`, lost Up (second Down) | pending cleared |
| pending P | `SetSnapEnabled(false)` | pending cleared, one `PreviewChanged` |

In the Circle tool a tap does nothing (no circle, no pending start), even when the wiggle is above `MinStrokeLength`
in board units. With snapping off there are no candidates, so every tap is "off any candidate".

**Preview contents.** During a gesture, `Preview` is: the ghost (Line: one `LineShape`; Circle: the ghost circle and
the dotted radius line), then the hover ring if the current point snapped, then the pending ring if a pending start
exists. Outside a gesture it holds only the pending ring (or nothing). All five shapes are preallocated and reused;
`EndGesture` clears `Preview` and re-adds the pending ring. The cursor shows the snapped point during a gesture.

**Allocation.** `Pointer(Move)` allocates 0 bytes once warm: index loops over `List<SnapCandidate>`, the metric is
called through the interface with `out Vec2` (no boxing), rings and ghosts are reused, `StrokeStyle` is a struct.
`SnapMetric` is read once per snap; tap measurement uses the metric captured at Down.

### Exact circle divisions (Q1 follow-up)
Division point k of a circle is `center + r * (cos, sin)(k * 45 degrees)`. With `Math.Cos`/`Math.Sin`, the axis values
are not exact (`Math.Sin(Math.PI)` is about 1.2e-16). The two 45-degree values also come out 1 ulp apart:
`Math.Cos(pi/4)` is not `Math.Sin(pi/4)`. So two touching circles produced their shared point at two slightly
different coordinates. `CircleDivisions` replaces this with a table built from 0, +-1 and one `Diagonal` constant. As
a result, the left circle's k = 0 point and the right circle's k = 4 point are the same double pair, and pattern seams
are exact by construction. `GoldenBoards.StarPoint` and `SnapIndex` both call `CircleDivisions.Point`, so a fixture
built from `StarPoint` and a drawing built by snapping agree with `==`.

### Q4: flat sandbox camera fit
Before, the sandbox camera sat 0.47 m from the board at a 50-degree tilt, framed for a landscape Game view; in the 390x844 portrait view only the middle half of the board's width was visible. `FlatCameraFit` (on the camera) keeps the camera's viewing direction (re-aimed at the board centre) and bisects the distance (0.05..20 m, 40 steps) until all four board corners project inside the screen's safe area minus margins for the UI: left 72, right 76, top 24, bottom 24 reference px (390x844, match 0.5, like the ToolBar). It runs on Start, whenever `Screen.width/height/safeArea` change, and on `Refit()` (call it after changing the camera tilt). Verified at 390x844 (camera ends ~1.36 m away, whole board visible) and 844x390 (whole board visible; in landscape the portrait-only ToolBar column runs off the bottom, which is expected). Device scenes are unaffected: the component is only on the flat sandbox camera.

### E3: H/V lock

"Horizontal" and "vertical" mean the board's own X and Y axes (decision Q1), so the lock stays fixed to the real
surface whatever the camera angle. Only the Line tool uses it; the Circle tool ignores it, and with the lock off
everything behaves exactly as in E2.

**Start.** Down snaps normally (`TrySnap`), giving the anchor `_anchor`.

**End, on every Move and on Up (`TryOrthoEnd(raw)`):**
1. Axis. `dx = raw.X - anchor.X`, `dy = raw.Y - anchor.Y` in board units. `|dx| >= |dy|` is horizontal (so an exact
   tie is horizontal), else vertical. This is decided again on every Move.
2. Unsnapped end. Horizontal: `new Vec2(clamp01(raw.X), anchor.Y)`. Vertical: `new Vec2(anchor.X, clamp01(raw.Y))`.
   The perpendicular coordinate is always the anchor's own value, copied, never computed, so a committed line has
   `A.Y == B.Y` (or `A.X == B.X`) exactly.
3. Snapping off: stop here. The lock still constrains; nothing aligns.
4. Measuring space (same rule as `TrySnap`). If `SnapMetric` is set and the unsnapped end projects, distances are in
   screen px with tolerance `TolerancePx`. The axis line's screen image is the line through the projected unsnapped end
   and the projection of a second axis point: the board edge farther along the axis (at least 0.5 board units away,
   which keeps the direction well conditioned), or the anchor if that edge does not project. A projective map sends a
   line to a line, so this is the true image. If neither second point projects (or the two images are within 1e-9 px)
   the whole step uses board units with `FallbackSnapTolerance`, where the axis direction is (1, 0) or (0, 1).
5. Offers along the axis. Each offer is just an along-axis value `t` in [0, 1]; its point is `OnAxis(t)` (again the
   anchor's perpendicular coordinate copied).
   - (a) Candidates: each `SnapCandidate` whose along-axis coordinate is in [0, 1] is projected; if its perpendicular
     distance to the axis image `|cross(axisDir, q - s0)|` is `<= tol`, it offers its own X (horizontal) or Y
     (vertical), copied exactly, with its `SnapIndex.Priority`.
   - (b) Crossings: the infinite axis line (origin = anchor, dir = (1, 0) or (0, 1)) against every shape of every
     visible layer: lines with `Intersect.LineSegment`, polygon edges (vertices from `SnapIndex.PolygonVertex`, same
     drawability test as `SnapIndex`) with `Intersect.LineSegment`, circles with `Intersect.LineCircle`. Each crossing
     with along-axis coordinate in [0, 1] offers that coordinate with the `Intersection` priority. Only the along-axis
     value is used; the crossing's other coordinate is discarded.
6. Choice. For each offer, `d2` is the squared distance between the projected offer point and the projected unsnapped
   end (board units without a metric). Offers with `d2 > tol^2` are dropped. The smallest `d2` wins; on an exactly equal
   `d2` the lower priority value wins; then the first offer ((a) in candidate-list order before (b) in layer/shape
   order). If nothing qualifies, the end is the unsnapped end from step 2.

**Feedback.** The ghost line runs from the anchor to the end, the cursor sits on the end, and the hover ring
(`HoverRingRadius`, as in E2) marks the end when it aligned in step 6.

**Toggling during a gesture.** `SetOrthoLock` calls `OnSnapSettingsChanged`, which compares the lock with the value it
last saw. On a change with an open Line gesture, `OnOrthoLockToggled` re-runs `ResolvePoint` on the last raw pointer
position, updates end, cursor and hover ring, and rebuilds the ghost (one `PreviewChanged`). Circle gestures and idle
sessions get no `PreviewChanged`. `SetSnapEnabled` mid-gesture is unchanged from E2 (no re-track).

**Taps with the lock on.** Tap detection and the pending start are unchanged. When the second tap's candidate `q`
differs from the pending `p`: if `p.X == q.X` or `p.Y == q.Y` (exact) the line commits as in E2; otherwise nothing is
committed, pending is cleared, and `Toast(OrthoLockToast)` is raised. The alignment test comes before the
`MinStrokeLength` test, so a too-short non-aligned pair also gets the toast. Tapping the same candidate again still
toggles pending off silently. Drags always go through the lock.

**Allocation.** Step 4 to 6 run each Move with no caching: index loops over the candidate list, layers and shapes,
pattern matching on the shape types, and a local `OrthoSearch` struct for the running best. The metric is called
through the interface with `out Vec2`. 0 bytes per Move (checked).

## 5. Invariants

_Filled in by the final pass (R1)._

## 6. Tuning knobs

| Constant | Value | File and member | Ticket |
|---|---|---|---|
| Snap tolerance without a screen metric | 0.02 board units | `Engine/EditorSession.cs` `FallbackSnapTolerance` | C0 |
| Dedup radius (candidates closer than this merge) | 1e-7 board units | `Engine/Snap/SnapIndex.cs` `DedupEpsilon` | E1 |
| Off-board slack (keep if in [-slack, 1 + slack]) | 1e-9 board units | `Engine/Snap/SnapIndex.cs` `BoundsSlack` | E1 |
| Division points per circle | 8 (k * 45 degrees) | `Contracts/Model/CircleDivisions.cs` `Count` + the exact `s_Cos`/`s_Sin` tables (`SnapIndex.CircleDivisions` mirrors `Count`). Not a free knob: another count needs a new exact table and changes `GoldenBoards` | E1, Q1 |
| Shape x grid-line crossings as candidates | false | `Engine/Snap/SnapIndex.cs` `IncludeShapeGridCrossings` (instance field) | E1 |
| Source priority order | UserPoint, Endpoint, Intersection, CellCorner, Center, CircleDivision, CellEdgeMid, CellCenter | `Engine/Snap/SnapIndex.cs` `Priority` (and the source order in `Rebuild`; both must agree) | E1 |
| On-segment / tangency slack | 1e-9 board units | `Engine/Geometry/Intersect.cs` `ParamEps` | E1 |
| Parallel threshold (sine of angle) | 1e-12 | `Engine/Geometry/Intersect.cs` `ParallelSinEps` | E1 |
| Snap/tap tolerance | 24 reference px | `App/ScreenSnapMetric.cs` `ToleranceReferencePx` | U1 |
| UI reference resolution / match | 390x844, 0.5 | `App/ScreenSnapMetric.cs` `ReferenceResolution`, `MatchWidthOrHeight` (used by ToolBar) | U1 |
| ToolBar button height | 56 reference px (was 58) | `App/ToolBar.cs` `ButtonH` | U2 |
| ToolBar column height | 522 reference px | `App/ToolBar.cs` `ColumnHeight` | U2 |
| Candidate dot fill radii | 1.9 / 2.5 / 3.0 / 3.6 mm | `BoardRender/BoardView.cs` `MinorGridDotRadius`, `GridDotRadius`, `GuideDotRadius`, `ShapeDotRadius` | U3, Q3 |
| Candidate dot outlines | 0.9 mm (grid), 1.1 mm (others); minor grid alpha 0.6 | `BoardRender/BoardView.cs` `GridOutlineWidth`, `DotOutlineWidth`, `MinorGridAlpha` | U3, Q3 |
| Candidate colours | dark fills, white outlines (section 4, U3) | `BoardRender/BoardView.cs` `gridDotFill`, `gridDotOutline`, `guideDotFill`, `shapeDotFill`, `dotOutline` (Inspector) | U3, Q3 |
| Sandbox camera fit margins | left 72, right 76, top 24, bottom 24 reference px | `Mocks/ForBoard/FlatCameraFit.cs` `leftPx`, `rightPx`, `topPx`, `bottomPx` (Inspector) | Q4 |
| Hover ring / pending ring radius | 0.012 board units (about 4.8 mm), Thin, solid, current colour | `Engine/EditorSession.Snap.cs` `HoverRingRadius` (style in `EditorSession.Tools.cs` `RingStyle`) | E2 |
| Tap slop | 10/24 of the snap tolerance (10 reference px with the App metric; 0.00833 board units without) | `Engine/EditorSession.Snap.cs` `TapSlopFraction` | E2 |
| Shortest line a tap-to-connect commits | 0.01 board units (same as drags) | `Engine/EditorSession.Tools.cs` `MinStrokeLength` (existing) | E2 |
| H/V lock alignment tolerance | no new constant: `TolerancePx` with a metric, `FallbackSnapTolerance` without (both for distance off the axis and along it) | `Engine/EditorSession.Snap.cs` `TryOrthoEnd` | E3 |
| H/V lock dominant-axis tie | abs(dx) == abs(dy) goes horizontal | `Engine/EditorSession.Snap.cs` `TryOrthoEnd` | E3 |
| Shortest usable screen image of the axis (else board units) | 1e-9 px | `Engine/EditorSession.Snap.cs` `TryScreenDirection` (literal) | E3 |

## 7. How to extend

_Filled in by the final pass (R1)._

## 8. Tests

How to run the engine checks on Windows (Luis):
`DOTNET_ROLL_FORWARD=Major DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run --project tools/enginecheck -c Release`
On the Mac (teammates), use the command in `tools/enginecheck/EngineCheck.csproj`.

### C0
- `ToolChecks.DefaultToolIsLine`: snapping on, H/V lock off, `SnapMetric` null, candidate list available.
- `ToolChecks.SnapAndOrthoTogglesRaiseStateOnce`: each toggle raises exactly one `StateChanged`, repeats are no-ops,
  and candidates are empty while snapping is off.

### E1
All in `tools/enginecheck/SnapChecks.cs` (21 checks; the harness total went from 81 to 102).
- `SegmentSegmentPerpendicularCrossing`: exact (0.25, 0.5) crossing, diagonals at the centre, a near miss off the end.
- `SegmentSegmentEndpointTouches`: T-junctions (axis-aligned and 9 diagonal ones) return b0 bit for bit; L-corner; a 0.01 gap is a miss.
- `ParallelAndCollinearGiveNone`: parallel, collinear overlap, collinear end to end, zero-length, line parallel to segment.
- `SegmentThroughCircleGivesTwoExactPoints`: horizontal and vertical secants exact (p0 nearer a), diagonal within 1e-12, half-inside gives 1.
- `TangentSegmentGivesOne`: tangent point exact; tangent point outside the segment gives 0.
- `SegmentInsideCircleGivesZero`: segment inside the circle, and far away.
- `CircleCircleCases`: 2 crossings (p0 left of c0->c1), apart, nested, outside tangent, inside tangent in both argument orders, concentric, identical.
- `InfiniteLineVariants`: `LineCircle` (unclipped, tangent, miss, zero dir) and `LineSegment` (behind origin, end touch exact, past the end).
- `EmptySquareBoardGridCorners`: 2x2 has 9 `CellCorner` at exactly (i/2, j/2) plus 12 edge mids and 4 centres; GridN 3 gives 16, 1 gives 4, 4 gives 25.
- `HexCornersMatchTileGridExactly`: for n = 1..4 the hex `CellCorner` list equals `TileGrid.SnapPoints` corners element by element (6 for n = 1, 16 for n = 2).
- `SwitchingGridKindRebuildsCorners`: `Sync` after setting `GridKind` rebuilds; square corners disappear; switching back restores 9.
- `StarFixtureDivisionsAndInnerVertices`: on `StarOnFullBoard` the 8 divisions are present at `StarPoint` exactly as `UserPoint` (one candidate each); the 8 inner star vertices are `Intersection` within 1e-12 of `c + (r/sqrt2)/cos(pi/8) * (cos, sin)(pi/8 + k pi/4)`; the centre is one `UserPoint`.
- `LoneCircleGivesExactDivisionsAndCenter`: with only the circle visible and its user points removed, 8 `CircleDivision` == `StarPoint` in k order; the centre reports `CellCorner` on square 2x2 and `Center` on hex.
- `HiddenLayerContributesNothing`: a hidden layer with a line, a circle and a user point gives exactly the empty board's sequence.
- `DedupKeepsHighestPriority`: an endpoint on a grid point reports `Endpoint`; of two equal-priority endpoints 5e-8 apart the earlier survives; a user point 5e-8 from a grid corner wins with its own position; all survivors are more than 1e-7 apart.
- `PolygonVerticesEdgesAndCenter`: polygon vertices are `Endpoint` (exact), its centre is `Center`, and a line crosses one polygon edge at the expected point.
- `PointsOutsideBoardAreDiscarded`: 5 of 8 divisions of a circle poking out on the right; a point within 1e-9 slack is kept; points 1e-6 outside and NaN are dropped.
- `ShapeGridCrossingsFlag`: off by default (no divider crossing); turning it on makes `Sync` rebuild and adds the (0.5, 0.25) crossing.
- `PriorityOrderMatchesPlan`: `Priority` is strictly increasing in the plan's order, UserPoint = 0, None last.
- `CandidateOrderIsDeterministicAndByPriority`: two builds of the star board give identical sequences, sorted by priority, starting with the bottom layer's user point.
- `SyncIsLazyAndDetectsChanges`: unchanged board returns false, same list instance and content, 0 bytes allocated over 1000 calls; true after adding a shape, toggling Visible, changing GridN, adding a user point, MarkDirty, a different board, null; a pure move is not detected without MarkDirty.

Mutation spot-check, done once and not in the repo: each of these deliberate bugs made at least one of the checks
above fail: removing end snapping in `SegmentSegment`, swapping `CellCorner`/`Intersection` priority, dropping
`Visible` from the signature, removing the bounds filter, a strict tangent test, and disabling dedup.

### U1-U3
- EditMode `VerifierToolBarToastTests.ToolBarSnapAndHVButtonsToggleSession`: the Snap button turns snapping off and on, and HV turns the lock on. Colours follow the state, including changes made outside the ToolBar.
- EditMode `VerifierToolBarToastTests.ToolBarEightButtonsFitInsideCanvas`: all 8 buttons are inside the canvas, in column order (run with a 390x844 portrait Game view).
- The existing `ToolBarOnRightEdge` still passes at 390x844. It **fails in a landscape Game view** (1920x1080) on the baseline too, because it measures against the Game view's size.
- Screenshot `Logs/SnapScreens/u2_toolbar_ui_390x844.png` (not committed): all 8 buttons fit at 390x844.

### E2
23 new checks (the harness total went from 102 to 125). Unless noted, no metric is set, so the tolerance is 0.02 board
units on the default 2x2 square grid. "Exact" means compared with `==`.

`tools/enginecheck/SnapToolChecks.cs`:
- `DragEndWithinToleranceCommitsCandidateExactly`: a drag ending 0.0036 from the corner (0.5, 0.5) commits B == (0.5, 0.5); a start with nothing in range stays raw.
- `DragEndOutsideToleranceCommitsRaw`: 0.03 from the nearest candidate commits the raw end.
- `SnapOffCommitsRaw`: with Snap off, line ends and circle centre/radius are raw and there are no candidates; turning it back on snaps again.
- `LineStartSnapsOnDown`: the Line start snaps on Down (cursor and hover ring on the candidate) and is kept in the commit.
- `CircleCentreAndRadiusSnap`: the centre snaps on Down; the radius point snaps on Move (ghost) and Up; radius is exactly 0.25 to an edge midpoint and exactly `Distance(centre, corner)` on a diagonal.
- `TieBreakIsDeterministic`: on an exactly equal distance a user point beats a cell corner; of two equidistant user points the earlier wins, and swapping their order swaps the winner, every time; a nearer corner beats a farther user point.
- `GridIntersectionOnEmptyBoardSquareAndHex`: empty board, square: corner (0, 0) and edge midpoint (0.75, 0.5) exactly; hex: two interior hex corners from `TileGrid.SnapPoints` exactly.
- `CandidatesRefreshAfterCommit`: no `Intersection` with one line; after the second (crossing) line commits, the crossing is a candidate and a third line ends on it exactly.
- `AnyModelChangeRefreshesCandidates`: a line moved in place (same counts) is picked up after any `BoardChanged` (here `SetLayerOpacity`), and the next drag snaps to the moved endpoint.
- `HoverRingOnlyWhileSnapped`: no ring at a raw start; a ring (radius `HoverRingRadius`, Thin, solid, current colour) at the candidate while the end is snapped, cursor and ghost on it; gone when out of range; everything cleared after Up.
- `TapTapConnectsTwoCandidates`: tap, tap commits one line between the exact candidates with the current style, one `BoardChanged(Shapes)`, pending cleared, empty `Preview`.
- `PendingRingPersistsAndClears`: after the tap gesture ends, `Preview` holds just the pending ring; a tap on empty clears it (with `PreviewChanged`); tapping the same candidate twice toggles off with no zero-length line.
- `CancelToolSwitchBoardReplaceAndSnapOffClearPending`: Cancel (no gesture and mid-tap), tool switch, `ClearBoard`, `LoadBoard`, `SetSnapEnabled(false)` (exactly one `PreviewChanged`) and an off-board Down each clear pending; with Snap off a tap only clears pending; a stray Up changes nothing.
- `DragAfterPendingClearsPendingAndDrawsNormalLine`: the pending ring still shows at Down, disappears as soon as the gesture becomes a drag, and the drag commits a normal raw line.
- `TapSlopUsesMaximumDistance`: moving out past the slop and back is a drag; staying inside it is a tap; no zero-length commit.
- `TapNeverCommitsTooShortLine`: two user points 0.005 apart: the connecting tap commits nothing and clears pending.
- `CircleToolTapCommitsNothing`: taps in the Circle tool make no pending start and no shape, also with a screen metric where the wiggle (0.012 board units, 12 px < 20.8 px slop) is above `MinStrokeLength`.
- `HiddenLayerOnConnectingTapToastsAndCommitsNothing`: layer hidden before the connecting tap, and hidden between its Down and Up: one `HiddenLayerToast` each, nothing committed, pending cleared.
- `ScaleOffsetMetricToleranceIsInPixels`: fake metric `screen = board * scale + offset`: at scale 1000, 22 px snaps (0.022 board units would not in fallback) and 26 px does not; at scale 500, 0.04 board units (20 px) snaps; at scale 2000 the tap slop is in pixels (0.008 board units = 16 px is a drag, 8 px is a tap).
- `PerspectiveMetricIsAnisotropic`: projective fake metric `1000 * (x, y) / (1 + 0.8 y)`, 12 px: 0.019 board units along x does not snap but along y does; 0.015 along x does not snap near y = 0 but does near y = 1.
- `UnprojectableCandidatesAndPointerFallBack`: a candidate the metric cannot project is skipped; if the pointer itself cannot be projected the board-unit fallback is used.
- `MovePathAllocatesNothing`: on `StarOnFullBoard`, Line and Circle tools, with no metric and with the perspective metric, after a warm-up gesture: 200 Moves sweeping in and out of candidates allocate 0 bytes (`GC.GetAllocatedBytesForCurrentThread`).

`tools/enginecheck/PatternChecks.cs`:
- `SnapBuildsTiled2x2`: pointer events only, no metric, 2x2 grid. Four circles (centre snapped to each cell centre from
  (+0.003, -0.002) off, radius snapped to the cell's right edge midpoint from (-0.002, +0.003) off), then each star's
  two squares by tap-to-connect, every tap 0.003 inward plus 0.0015 sideways from the division point. Asserts exactly
  4 circles with exact centres and radius == 0.25, exactly 32 lines, and an order- and orientation-independent `==`
  match with `GoldenBoards.Tiled2x2()` using `==` on every endpoint. It also asserts that the fixture has no two
  endpoints within 1e-9 of each other: every seam is a single point.

  History: E2 first shipped with a 1-ulp "seam group" exception, because `StarPoint` used `Math.Cos`/`Math.Sin`
  (`0.25 * sin(pi)` is not 0). Luis approved replacing that with the exact `CircleDivisions` table (decision Q1,
  2026-11-03). The exception is gone and the check is exact everywhere.
- `PatternChecks.CircleDivisionTableIsExact`: the table uses only 0, +-1 and one `Diagonal` constant; it is within
  1e-15 of cos/sin; `StarPoint` uses it; k wraps mod 8; touching circles share their tangent point bit-for-bit.

Existing checks opted out of snapping (`s.SetSnapEnabled(false)` right after the session is created, no assertion
changed):
- `ToolChecks.TooShortIsDiscarded`: its raw drags start on the grid corner (0.5, 0.5) and the "just above the minimum"
  drag ends 0.011 from it, so with snapping both ends snap to the corner and nothing commits.
No other existing check needed it; `VerifierToolChecks` is unchanged. (`V_TooShortLineNotCommitted` still passes with
snapping on: its 0.003 drag is now a tap that sets a pending start on (0.5, 0.5), and its following tap on the same
point toggles it off, so `Preview` is empty as asserted.)

Mutation spot-check, done once and not in the repo; each deliberate bug made at least one check fail: snapped
position rounded to float (4 checks), tap path skipped (10), pending not cleared on discard (1), tie going to the
later candidate (1), a list allocated per snap (1, 288 KB over 200 Moves), `OnModelChanged` not marking dirty (1),
metric ignored (3).
- How to screenshot UI over MCP: the MCP screenshot tool renders through the camera, which leaves out Screen Space Overlay canvases. In Play Mode, switch the canvases to Screen Space Camera for one `Camera.Render` into a RenderTexture, then restore them (see the Progress Log).

### E3
13 new checks in `tools/enginecheck/OrthoChecks.cs` (the harness total went from 126 to 139). No metric and the
default empty 2x2 square grid unless noted; "exact" means `==`.
- `DiagonalDragCommitsExactlyHorizontalOrVertical`: diagonal drags commit B == (0.83, 0.12) with `A.Y == B.Y`, and B == (0.1, 0.88) with `A.X == B.X`.
- `DominantAxisFollowsEachMoveAndTieGoesHorizontal`: one gesture's ghost flips horizontal, vertical, horizontal as the pointer moves; leftward and downward drags; an exact `|dx| == |dy|` tie goes horizontal in both (+,+) and (-,-).
- `ClampsToBoard`: on-board events past the right, left and top edges end at x = 1, x = 0 and y = 1.
- `AlignmentSnapToOffAxisCandidate`: a user point 0.017 off the axis gives B == (0.613, 0.12) (X from the candidate, Y from the anchor), ghost, cursor and hover ring on it; 0.047 along or 0.026 off the axis does not align; with Snap off the lock still constrains.
- `AxisCircleIntersectionSnapBeatsFartherCandidate`: B.X is the axis/circle crossing within 1e-12 and B.Y == anchor Y, chosen over a farther user point (better priority, 0.008 away); the right-hand crossing too; a circle on a hidden layer gives nothing.
- `AxisLineAndPolygonEdgeIntersectionSnap`: horizontal axis meets a slanted line at x = 0.420125 (1e-12); vertical axis meets a hexagon's lower edge at the independently computed y (1e-12) with X exact.
- `EndsOnGridIntersection`: square grid ends on (0.75, 0.5) and (0.25, 1) exactly; hex grid from one corner to the nearest corner on the same row, both exact.
- `LockOffRestoresFreeLines`: after turning the lock off a diagonal drag is free again and point snapping works as in E2.
- `CircleToolUnaffected`: with the lock on, circles keep diagonal radii and normal snapping; toggling the lock mid-Circle gesture raises no `PreviewChanged`.
- `ToleranceIsInScreenSpace`: perspective metric (12 px): 0.017 off the axis near y = 0.12 (14 px) does not align though board units would; 0.025 off near y = 0.9 (8.4 px) aligns though board units would not; affine scale 500: 0.04 along the axis (20 px) aligns, board units would not.
- `TogglingLockMidGestureUpdatesGhost`: lock on mid-drag moves ghost, cursor and hover ring to the aligned end with exactly one `PreviewChanged`; off restores the free ghost; the Up commits the re-tracked end; an idle toggle touches no preview.
- `TapToConnectWithLock`: a row pair and a column pair commit; a diagonal pair commits nothing, toasts `OrthoLockToast` once and clears pending; the same pair commits with the lock off.
- `LockedMovePathAllocatesNothing`: three lines, a circle and a hexagon, with and without the perspective metric: an untimed pass shows the sweep covers both axes and alignment snaps, then 200 locked Moves allocate 0 bytes.

Mutation spot-check, done once and not in the repo; each deliberate bug made at least one check fail: perpendicular
coordinate computed instead of copied (10 checks), dominant axis ignored (6), tie sent vertical (1), (b) crossings
skipped (2), a list allocated per Move (1, 317 KB over 200 Moves), metric ignored (1), clamp removed (1), no re-track
on toggle (1), tap alignment rule removed (1).

## 9. Known limitations and untested areas

_Filled in by the final pass (R1)._ It must say "Editor-verified only" and list the device checks still owed (plan Q12).
