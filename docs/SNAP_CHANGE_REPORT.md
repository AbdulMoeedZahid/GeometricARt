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
| 5 | `CircleDivision` | per circle, k = 0..7: `new Vec2(c.X + r * Math.Cos(k * Math.PI / 4), c.Y + r * Math.Sin(k * Math.PI / 4))` (same expression as `GoldenBoards.StarPoint`) |
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

## 5. Invariants

_Filled in by the final pass (R1)._

## 6. Tuning knobs

| Constant | Value | File and member | Ticket |
|---|---|---|---|
| Snap tolerance without a screen metric | 0.02 board units | `Engine/EditorSession.cs` `FallbackSnapTolerance` | C0 |
| Dedup radius (candidates closer than this merge) | 1e-7 board units | `Engine/Snap/SnapIndex.cs` `DedupEpsilon` | E1 |
| Off-board slack (keep if in [-slack, 1 + slack]) | 1e-9 board units | `Engine/Snap/SnapIndex.cs` `BoundsSlack` | E1 |
| Division points per circle | 8 (k * pi/4) | `Engine/Snap/SnapIndex.cs` `CircleDivisions` (the angle expression is fixed at pi/4 to match `GoldenBoards.StarPoint`; changing the count also needs that expression changed) | E1 |
| Shape x grid-line crossings as candidates | false | `Engine/Snap/SnapIndex.cs` `IncludeShapeGridCrossings` (instance field) | E1 |
| Source priority order | UserPoint, Endpoint, Intersection, CellCorner, Center, CircleDivision, CellEdgeMid, CellCenter | `Engine/Snap/SnapIndex.cs` `Priority` (and the source order in `Rebuild`; both must agree) | E1 |
| On-segment / tangency slack | 1e-9 board units | `Engine/Geometry/Intersect.cs` `ParamEps` | E1 |
| Parallel threshold (sine of angle) | 1e-12 | `Engine/Geometry/Intersect.cs` `ParallelSinEps` | E1 |

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

## 9. Known limitations and untested areas

_Filled in by the final pass (R1)._ It must say "Editor-verified only" and list the device checks still owed (plan Q12).
