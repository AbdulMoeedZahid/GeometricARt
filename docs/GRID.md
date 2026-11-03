# Tile grid (square and hex)

The user can lay a square or hexagonal tile grid over the board. Every tile fits completely inside the board they placed
(quick place or corner-registered). The grid is a drawing guide: it does not change shapes, and snapping to it comes later.

## How to use
1. Place the board. The tool bar on the right now has a third group under Line / Circle / Dotted / Width:
   - **Grid** (label `Square` or `Hex`): switches the tile shape. The icon shows the current kind.
   - **Tiles** (label `1x1` .. `4x4`): cycles tiles per side 1 -> 2 -> 3 -> 4 -> 1.
2. The dashed tile edges update immediately, in the Editor sandbox and on the phone in AR.
3. A new or cleared board starts as a 2x2 square grid (unchanged default, D-07).
4. `Sandbox_A_Flat` now starts on an empty board (`EngineInstaller.fixture = Empty`, also in `SceneBuilder`) instead of
   the eight-pointed star test design, so the grid is the only thing on screen at Play. Pick `StarOnFullBoard` in the
   App's EngineInstaller to get the star back. `Sandbox_A_AR` still loads the star; `Main` was already empty.

## Before and after
| | Before | After |
|---|---|---|
| Model | `Board.GridN` (1..4), square only | adds `Board.GridKind` (`Square` default, `Hex`) |
| Session API | `SetGrid(n)` | adds `SetGridKind(GridKind)`; both raise `BoardChanged(Grid)` only when something changes |
| Geometry | inline in `BoardView.RebuildBase` (vertical/horizontal dividers at i/n) | one pure-C# source of truth: `Contracts/Model/TileGrid.cs` |
| Rendering | dividers were part of the "base" mesh, so **hidden in AR** (`BoardRoot.prefab` has `showBase: 0`) | grid is drawn whenever `BoardView.showGrid` is on (default), independent of `showBase`, so it shows on the real surface |
| UI | no control; nothing called `SetGrid` | Grid and Tiles buttons in `ToolBar` |
| Stub snap candidates | hand-written square lattice | `TileGrid.SnapPoints`, so hex works too |

## Geometry (`TileGrid`)
Board space is `[0,1] x [0,1]` (the square board; for corner placement it is the largest centred square inside the
registered surface, decision D1 in `CORNER_PLACEMENT_PLAN.md`). `n` is clamped to `[Board.MinGridN, Board.MaxGridN]`.

- **Square**: `n x n` squares of side `1/n`. They cover the board exactly (outer edges are snapped to exactly 0 and 1).
- **Hex**: `n` columns x `n` rows of **pointy-top** hexagons with circumradius `r`. Odd rows are shifted right by half a
  hex width (offset rows), so neighbours share whole edges.
  - hex width `w = sqrt(3) r`, row pitch `1.5 r`
  - block width `w (n + 0.5)` for `n > 1` (the extra half is the row offset), `w` for `n = 1`
  - block height `r (1.5 n + 0.5)`
  - `r = 1 / max(sqrt(3) (n + 0.5 or 1), 1.5 n + 0.5)`: the largest radius that fits both ways
    (`TileGrid.HexRadius`). For n = 2..4 the width is the limit; for n = 1 the height is.
  - The block is centred, so the margins are equal left/right and top/bottom. A square cannot be tiled by whole
    hexagons; we chose **whole tiles with a margin** over clipped partial tiles at the frame.
  - Values: n=1 r=0.500, n=2 r=0.231, n=3 r=0.165, n=4 r=0.128 (board units; x 40 cm for the quick-place board).

API (all static, no UnityEngine):
- `Cells(kind, n)` -> `List<GridCell>`: `Column`, `Row`, `Center`, `Corners` (counter-clockwise; 4 or 6).
  Overload `Cells(kind, n, list)` reuses a list.
- `UniqueEdges(kind, n, list, skipBoardBoundary)`: each tile edge once, so shared hex edges are not drawn twice
  (translucent/dashed strokes would darken or misalign). `skipBoardBoundary` drops edges lying on the board frame.
- `SnapPoints(kind, n, list)`: corners (`SnapKind.CellCorner`), edge midpoints (`CellEdgeMid`) and centres
  (`CellCenter`), each position once. Ready for the snapping feature.
- `HexRadius(n)`, `ClampN(n)`.

Named `TileGrid`, not `GridLayout`: `UnityEngine.GridLayout` exists and the name is ambiguous in any file that also uses
`UnityEngine` (CS0104).

## Rendering (`BoardView`)
- `RebuildBase(kind, n)`: sheet + frame + registered-surface outline only if `showBase`; then, if `showGrid`, every edge
  from `TileGrid.UniqueEdges` as a dashed segment (1.1 mm at the 40 cm board, `dividerColor`, same dash/gap as before).
- With `showBase` on (Editor sandbox) the frame already draws the board edge, so boundary edges are skipped and the
  square grid looks exactly like before. With `showBase` off (AR) the square grid's outer edges outline the board.
- The base mesh rebuilds when `GridN` or `GridKind` changes (`_lastGridN`, `_lastGridKind`) or on
  `BoardChanged(Grid | Reset)`. No per-frame cost otherwise.
- Hide the grid per view with the `showGrid` checkbox on the BoardView component.

## UI (`ToolBar`)
- Two buttons in a new group, named `Square` (kind) and `2x2` (tiles) as GameObjects; labels update from the model.
- The tool bar subscribes to `BoardChanged` as well as `StateChanged`, so a grid change made anywhere (another UI, a
  loaded board) refreshes the labels.
- The column grew from 4 to 6 buttons (about 400 reference px tall), still vertically centred on the right edge.
- New procedural icons: `Square` (2x2 squares), `Hex` (hexagon outline), `Tiles` (3x3 dots).

## Files
- `Assets/GeoART/Contracts/Model/Board.cs`: `GridKind` enum, `Board.GridKind` field.
- `Assets/GeoART/Contracts/Model/TileGrid.cs` (new): grid geometry.
- `Assets/GeoART/Contracts/Session/IEditorSession.cs`: `SetGridKind`.
- `Assets/GeoART/Engine/EditorSession.cs`: `SetGridKind` (ignores unknown values and no-op repeats).
- `Assets/GeoART/Mocks/ForBoard/StubEditorSession.cs`: `SetGridKind`; candidates from `TileGrid.SnapPoints`.
- `Assets/GeoART/BoardRender/BoardView.cs`: `showGrid`, grid drawn from `TileGrid`, base split into
  `AddSheetFrameAndSurface`.
- `Assets/GeoART/App/ToolBar.cs`: Grid and Tiles buttons, icons, `BoardChanged` subscription.
- Tests: `tools/enginecheck/GridChecks.cs` (new, 9 checks), `Assets/Tests/EditMode/Verifier/VerifierToolBarToastTests.cs`
  (`ToolBarGridButtonsSwitchKindAndCycleTiles`), `Assets/Tests/EditMode/Board/BoardGeometryTests.cs` (stub grid kind),
  `Assets/Tests/PlayMode/Verifier/VerifierInputRouterPlayTests.cs` (wrapper forwards `SetGridKind`).

## How to check it works
- Engine: `tools/enginecheck` (command in `EngineCheck.csproj`). The grid checks assert: n^2 tiles; every corner inside
  `[0,1]^2`; square tiles cover area 1; hex tiles regular (radius and side = r) with equal margins and touching the board
  on one axis; hex neighbours exactly `sqrt(3) r` apart with no overlap and the expected neighbour count; shared edges
  emitted once; snap points unique; `SetGridKind` raises one `Grid` event; `ClearBoard` returns to 2x2 square.
- Unity: Test Runner, EditMode, `ToolBarGridButtonsSwitchKindAndCycleTiles` and `StubSession_ServesFixtureAndGridChanges`.
- By eye (sandbox or phone): Hex shows whole hexagons, none crossing the frame, centred; Tiles cycles 1..4; existing
  drawings do not move; in AR the grid now appears on the surface.

## Extending
- **Another tiling** (e.g. triangles): append to `GridKind` (keep ordinals), add a branch in `TileGrid.Cells`; edges,
  snap points, rendering and the checks follow automatically. Add a glyph and include it in `ToolBar.ToggleGridKind`.
- **Flat-top hexes or a different count rule**: change only `TileGrid.HexRadius` / `HexCells`; update the expected
  neighbour count in `GridChecks`.
- **Snapping to tiles**: `EditorSession.SnapCandidates` can be filled from `TileGrid.SnapPoints(board.GridKind,
  board.GridN, list)`; rebuild it on `BoardChanged(Grid | Reset)`.
- **Grid over the full registered rectangle** (not just the centred square): needs the deferred aspect-ratio contract
  (`CORNER_PLACEMENT_PLAN.md` D1, "Future path"); `TileGrid` would then take the aspect as a parameter.
- **Undo**: `SetGrid` / `SetGridKind` are direct mutations today; when undo lands, wrap them in commands like shapes.
- Save/reload: no serializer exists yet; when one is added, persist `GridKind` next to `GridN`.
