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
| any | **Snap** button | Turns snapping off/on (default on). Off: no dots, nothing snaps, taps connect nothing. |
| Line | **H/V** button | Lock on: every new line is exactly horizontal or vertical along the board edges (whichever way you dragged more), and its end lines up with dots in the same row or column. With the lock on, tapping two dots connects them only if they are in the same row or column ("H/V lock is on: tap two points in the same row or column"). Circle tool ignores it. |

"Within reach" means both: within 24 px on a 390x844 reference screen (scaled like the tool bar, measured on screen, so
dots are as easy to hit at the far edge of a tilted board as at the near edge) **and** within 1.2 cm on the board (0.03
board units, `EditorSession.MaxSnapBoardDistance`), so a flat viewing angle cannot pull a line end far across the board.
A tap is a touch that moves less than 10 reference px.

## Defaults and limits
- Snap on, H/V lock off, on every new session.
- Snapped coordinates equal the dot's coordinates exactly (checked with `==`), so a pattern drawn by snapping (for example
  four stars in a 2x2 grid) lines up perfectly at shared points.
- Circle divisions are always 8 per circle (k x 45 degrees).
- Crossings of drawn shapes with the grid lines are **not** dots (one switch in code: `SnapIndex.IncludeShapeGridCrossings`).
- When looking at the board at a very flat angle, 24 px covers a lot of board depth; the 1.2 cm board cap keeps snapping
  local there. Turn Snap off for free drawing.
- Verified in the Unity Editor only (sandbox, Device Simulator, tests). Device checks are still owed; see the change
  report, section 9.

## How to check it works
- Engine (no Unity): `DOTNET_ROLL_FORWARD=Major dotnet run --project tools/enginecheck -c Release` (Windows/Mac). Snap
  checks: `SnapChecks` (candidates, geometry), `SnapToolChecks` (snapping, taps), `PatternChecks` (the 2x2 star pattern
  built only by snapping equals `GoldenBoards.Tiled2x2` exactly), `OrthoChecks` (H/V lock).
- Unity Test Runner: EditMode `VerifierToolBarToastTests` (Snap/H/V buttons, 8-button fit; run with a portrait Game view,
  e.g. 390x844); PlayMode `VerifierSnapPlayTests` (touch -> router -> snapped commit, exact coordinates).
- Render check: **GeoART > Checks > Board rendering check** writes `Logs/GeoARtRenderCheck/board-snap-*.png`.
- By hand: open `Sandbox_A_Flat`, press Play, draw a circle and a line through it, then drag a new line end near the
  crossing: it lands exactly on the blue dot.

## Files
- `Assets/GeoART/Engine/Snap/SnapIndex.cs` candidate list (sources, priority, dedup, lazy rebuild)
- `Assets/GeoART/Engine/Geometry/Intersect.cs` crossings
- `Assets/GeoART/Engine/EditorSession.Snap.cs` snap query, H/V lock
- `Assets/GeoART/Engine/EditorSession.Tools.cs` Line/Circle gestures, tap-to-connect
- `Assets/GeoART/Contracts/Model/CircleDivisions.cs` exact 8-way circle division table
- `Assets/GeoART/App/ScreenSnapMetric.cs` screen-pixel tolerance
- `Assets/GeoART/App/ToolBar.cs` Snap and H/V buttons
- `Assets/GeoART/BoardRender/BoardView.cs` dots and rings
