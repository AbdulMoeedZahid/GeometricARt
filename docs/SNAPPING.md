# Snapping, tap-to-connect and H/V lock

Snap points are dots on the board that line ends, circle centres and radius points jump onto **exactly** (the same
numbers, so patterns join without gaps). They are created automatically; there is nothing to place by hand.
Engineering details, tuning constants and how to extend it: `docs/SNAP_CHANGE_REPORT.md`.

## What becomes a snap point

| Dot | Where | Look |
|---|---|---|
| Line/circle crossings | wherever two drawn shapes cross (line/line, line/circle, circle/circle; polygon edges count as lines) | large, deep blue, white rim |
| Endpoints | both ends of every line, polygon corners | large, deep blue, white rim |
| Centres, circle divisions | circle and polygon centres; 8 points on every circle at 0, 45, 90... degrees (the star points) | medium, blue, white rim |
| User snap points | points stored on layers (e.g. in the star fixture) | medium, blue, white rim |
| Grid intersections | every tile corner of the square or hex grid, present even on an empty board | small, dark with a light rim |
| Tile edge midpoints, tile centres | from the same grid | smallest, translucent |

Hidden layers contribute nothing. Dots update when you draw, change the grid (Square/Hex, 1x1..4x4) or show/hide layers.
Where two kinds fall on the same spot, one dot is kept, in this order of priority: user point, endpoint, crossing, grid
intersection, centre, circle division, edge midpoint, tile centre (so a line ending on a grid corner shows an endpoint
dot, and a circle centred on a grid corner shows a grid dot; the position is the same either way).

## Gestures

| Tool | Gesture | Effect |
|---|---|---|
| Line | drag | Start and end snap to the nearest dot within reach. An orange ring marks the dot in use while dragging. |
| Line | tap a dot, then tap another dot | Connects the two dots with a line. After the first tap an orange ring stays on it. |
| Line | tap the ringed dot again, tap empty board, start a drag, switch tool, Cancel | Forgets the first tap. |
| Circle | drag | Centre snaps on touch-down; the radius snaps to the dot under the finger (radius = exact distance). |
| Move, Duplicate | drag (action bar mode) | The grabbed line end / centre (or the selection's pivot) snaps to the nearest dot and lands on it exactly. Move hides the selection's own dots while dragging; Duplicate keeps them, so a copy can chain onto its original. See `docs/TOOLS.md`. |
| Scale | drag (action bar mode) | The handle (the selection's outermost point toward the finger) snaps radially to a dot: a single circle scaled about its centre passes exactly through it. The selection's own dots are hidden while dragging. |
| any | **Snap** button | Turns snapping off/on (default on). Off: no dots, nothing snaps, taps connect nothing. |
| Line | **H/V** (hold Snap, then the H/V chip; the Snap label shows "H/V" while on) | Lock on: every new line is exactly horizontal or vertical along the board edges (whichever way you dragged more), and its end lines up with dots in the same row or column. With the lock on, tapping two dots connects them only if they are in the same row or column ("H/V lock is on: tap two points in the same row or column"). Circle tool ignores it. |

"Within reach" means both: within **32 px** on a 390x844 reference screen (a 64 pt circle, wider than a fingertip;
scaled like the tool bar and measured on screen, so dots are as easy to hit at the far edge of a tilted board as at the
near edge) **and** within the **board cap** (`EditorSession.BoardCap`), so a flat viewing angle cannot pull a line end far
across the board. A tap is a touch that moves less than 10 reference px.

### Board cap (adaptive)
Until the snapping rework the cap was a fixed 1.2 cm (0.03 board units). At ordinary phone distance that halved the
24 px radius (a 40 cm board filling a 390 pt screen gives 0.03 x 390 = 11.7 pt), which is why connecting points felt hard.
Now the cap only limits grazing angles:

`BoardCap(p) = clamp(1.5 x tol / sigma_max, 0.03, max(0.10, half a tile))`

- sigma_max is the largest singular value of the board-to-screen Jacobian at the pointer (pixels per board unit along the
  most-stretched screen direction; two extra projections, p + (0.001, 0) and p + (0, 0.001)). tol / sigma_max is the
  shortest board reach of the screen circle, so the cap lets the circle reach at most 1.5x that into the compressed depth
  direction.
- Looking straight down it never binds; it starts to bind past about a 48 degree tilt, so up to about 45 degrees the full
  32 pt works.
- The 0.03 floor means it is never worse than before. It is also the cap without a metric and when a projection fails.
- The ceiling (half a tile: 1/GridN square, the hex radius hex; at least 0.10) stops a far, tiny board from snapping
  across tiles: on a 2x2 grid it binds from about 1.3 m, on 4x4 from about 65 cm.
- The H/V lock uses the same cap at the unsnapped end, both off the axis and along it.

### Which dot wins
- The nearest dot within reach (exact ties by the priority above).
- **Tie window**: grid filler dots (grid intersections, tile edge midpoints, tile centres) lose to a shape or construction
  dot (crossing, endpoint, centre, circle division, user point) that is at most 6 px farther. Shape and construction dots
  compete by distance only (a 6 px window would let a crossing steal a star tip's circle division).

### Sticky release (plain Line and Circle drags)
After a drag snaps to a dot it **stays on that dot** while the finger is within **48 px** (1.5 x 32) and within the board
cap of it, unless another dot within reach is nearer by more than 4 px. This forgives the few millimetres the smoothing
filter and the lifting finger add. The gesture's own start dot is never sticky (short lines and small circles from a
snapped start still work), and returning to it drops any older sticky dot. The sticky is reset for every gesture and dropped when Snap is turned off, the H/V lock is
turned on, or the dot stops being a dot (its layer hidden mid-drag; an Up then commits the raw point). The H/V lock and
Move/Scale/Duplicate have no sticky.

### Feedback
- The snapped dot is drawn as a filled orange disc under the orange hover ring.
- Each new acquisition (a snapped touch-down, or a different dot during a drag; Move/Scale/Duplicate too) raises
  `EditorSession.SnapAcquired`: the hover ring **pops** from 1.6x to 1x over 120 ms, and the phone gives a light
  **haptic tick** (at most one per 60 ms). Opening a Line/Circle hold menu ticks too. H/V alignments have no candidate, so
  they neither pop nor tick.
- Haptics: iOS `UISelectionFeedbackGenerator` (`Assets/Plugins/iOS/GeoHaptics.mm`), Android
  `View.performHapticFeedback(CLOCK_TICK)`, nothing in the Editor (`Assets/GeoART/App/Haptics.cs`, `SnapFeedback.cs`,
  added at runtime by `EngineInstaller`).

## Defaults and limits
- Snap on, H/V lock off, on every new session.
- Snapped coordinates equal the dot's coordinates exactly (checked with `==`), so a pattern drawn by snapping (for example
  four stars in a 2x2 grid) lines up perfectly at shared points.
- Circle divisions are always 8 per circle (k x 45 degrees).
- Crossings of drawn shapes with the grid lines are **not** dots (one switch in code: `SnapIndex.IncludeShapeGridCrossings`).
- When looking at the board at a very flat angle, 32 px covers a lot of board depth; the board cap keeps snapping local
  there. Away from dots nothing snaps, so free drawing is unchanged; turn Snap off to draw fully raw.
- Select's tap-pick of a stroke uses the same 32 px, still capped at 1.2 cm (`MaxPickBoardDistance`).

## Device protocol (before / after the snapping rework)
At about 45 cm with the phone pitched about 30 degrees, on the 2x2 star pattern: 20 tap-to-connects and 20 drag-ends aimed
at crossings. Count exact hits (the line lands on the dot).

| Build | Tap-to-connect hits / 20 | Drag-end hits / 20 |
|---|---|---|
| Before (24 px, fixed 1.2 cm cap) | to be measured on the device | to be measured on the device |
| After (32 px, adaptive cap, sticky, tie window) | to be measured on the device | to be measured on the device |
- Verified in the Unity Editor only (sandbox, Device Simulator, tests). Device checks are still owed; see the change
  report, section 9.

## How to check it works
- Engine (no Unity): `DOTNET_ROLL_FORWARD=Major dotnet run --project tools/enginecheck -c Release` (Windows/Mac). Snap
  checks: `SnapChecks` (candidates, geometry), `SnapToolChecks` (snapping, taps), `SnapCapChecks` (board cap),
  `SnapStickyChecks` (sticky, tie window, tap slop, SnapAcquired, allocation), `PatternChecks` (the 2x2 star pattern
  built only by snapping equals `GoldenBoards.Tiled2x2` exactly), `OrthoChecks` (H/V lock).
- EditMode `SnapFeedbackViewTests` (filled dot, ring pop, haptic throttle, SnapFeedback).
- Unity Test Runner: EditMode `VerifierToolBarToastTests` (Snap/H/V buttons, 9-button fit; any Game view size, also the
  640x480 batch-mode screen); PlayMode `VerifierSnapPlayTests` (touch -> router -> snapped commit, exact coordinates).
- Render check: **GeoART > Checks > Board rendering check** writes `Logs/GeoARtRenderCheck/board-snap-*.png`.
- By hand: open `Sandbox_A_Flat`, press Play, draw a circle and a line through it, then drag a new line end near the
  crossing: it lands exactly on the blue dot.

## Files
- `Assets/GeoART/Engine/Snap/SnapIndex.cs` candidate list (sources, priority, dedup, lazy rebuild)
- `Assets/GeoART/Engine/Geometry/Intersect.cs` crossings
- `Assets/GeoART/Engine/EditorSession.Snap.cs` snap query, H/V lock
- `Assets/GeoART/Engine/EditorSession.Tools.cs` Line/Circle gestures, tap-to-connect
- `Assets/GeoART/Engine/EditorSession.Transform.cs` Move/Scale/Duplicate snapping (`SnapIndex.ExcludedShapes`)
- `Assets/GeoART/Contracts/Model/CircleDivisions.cs` exact 8-way circle division table
- `Assets/GeoART/App/ScreenSnapMetric.cs` screen-pixel tolerance
- `Assets/GeoART/App/ToolBar.cs` Snap and H/V buttons
- `Assets/GeoART/BoardRender/BoardView.cs` dots and rings, the snapped disc and the ring pop
- `Assets/GeoART/App/Haptics.cs`, `SnapFeedback.cs`, `Assets/Plugins/iOS/GeoHaptics.mm` haptic tick
