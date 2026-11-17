# Line, Circle, Select, Move, Scale and Duplicate tools

Drawing on the placed board, plus thinner and smoother strokes. Snapping is **on by default**: line ends, circle
centres and radius points jump exactly onto snap points (grid intersections, shape crossings, endpoints, circle
divisions...), and tapping two points connects them with a line. An H/V lock keeps new lines horizontal or vertical. See
`docs/SNAPPING.md`. PlaceSnapPoint / ClickAtCursor / LineBetweenSnapPoints (quick menubar) are still no-ops. Undo is
not implemented.

## How to draw
1. Place the board (quick place or corners). The tool bar appears on the right edge, vertically centred (on short or
   landscape screens it shrinks to stay between the placement row and the coach text).
2. **Line** (default tool): touch the board at the start point, drag, lift at the end point. An orange ghost follows the
   finger while dragging.
3. **Circle**: touch the centre, drag outwards, lift. The ghost shows the circle and a dotted radius line.
4. **Dotted** toggles dashed strokes for new shapes (orange when on). **Width** cycles Thin / Medium / Thick for new shapes.
   **Snap** (orange = on, the default) and **H/V** (orange = on) are at the bottom of the tool bar; see `docs/SNAPPING.md`.
5. Shapes go into the active layer (the one selected in the left sidebar). If that layer is hidden, nothing is drawn and a
   toast says "Layer is hidden: double-tap it in the sidebar to show it".

Details:
- A line shorter than 0.01 board units (4 mm on the 40 cm board) or a circle with a smaller radius is discarded, so a tap
  draws nothing.
- Touches that start on any button/sidebar are ignored for drawing. A touch that starts off the board does nothing.
- Dragging past the board edge clamps the point to the edge (so edge-to-edge lines are easy). If even the last on-board
  screen position no longer projects (tracking lost), the last valid point is kept.
- The stroke is cancelled (nothing committed) by a second finger, by "Re-place board" or any placement state other than
  Placed (e.g. tracking lost), by the app pausing / losing focus, by switching tools, and by loading or clearing the board.
- Other tool ids (Rotate, Stamps, Erase, Snap) toast "Coming soon" and keep the current tool.

## Select tool
Chooses lines and circles (and polygons) for the tools that act on them (Move, Scale, Duplicate, below). It only
selects: nothing is moved, added or removed, the board never raises `BoardChanged` and no undo step is made.
1. Tap **Select** (third button, four corner brackets). It turns orange, Line/Circle go idle, and a toast and the tool hint
   say "Select: drag a box around whole shapes, or tap one".
2. **Drag a box**: once the finger leaves the tap slop an orange dotted rectangle follows it (board-aligned, from the
   touch-down point to the finger; never snapped, no H/V lock). Every shape that lies **wholly** inside the box (edges
   count) is highlighted in cyan as you drag; a shape the box only partly covers is not. Lift: the selection stays and a
   toast says "N shapes selected", or "Nothing selected: the box must enclose whole shapes".
3. **Tap a stroke**: selects only the shape whose line or circle outline is nearest the finger (within 24 screen px, at
   most 1.2 cm on the 40 cm board; the topmost shape wins a tie). Tapping inside a circle away from its outline does not
   pick it. A tap on empty board clears the selection.
4. A new box or tap always **replaces** the selection (no add mode).
5. A second finger, Re-place, the app pausing, a tool switch, Clear or Load during a drag **cancel** it: the box goes
   away and the selection from before the drag comes back.

Rules:
- Shapes on every **visible** layer can be selected together; shapes on hidden layers can't. Hiding or deleting a layer
  drops its shapes from the selection, and showing it again does not re-select them. A hidden active layer does not
  block Select (the "Layer is hidden" toast is for drawing only).
- Switching to Line, Circle or None clears the selection (Move, Scale and Duplicate keep it). Clear and Load clear it.
- Rendering: `BoardView` draws a "Selection" mesh (render queue 3002, between Shapes 3001 and the snap dots 3003; the
  Overlay with the marquee is 3004): a wide translucent cyan halo under each selected shape, then the shape's own stroke
  again on top.
- Engine API (concrete `EditorSession`): `SelectionCount`, `GetSelectedShapes` (shape and layer, bottom layer first),
  `TryGetSelectionBounds` (union bounds), `SetSelection`, `ClearSelection` (also on `IEditorSession`, for the action
  bar). Geometry lives in `Engine/Geometry/SelectionGeometry.cs` (`BoardRect`, enclosure, stroke distance, pick).

## Move, Scale, Duplicate
Act on the selection. While something is selected, a cyan **dashed box** surrounds the selection and a cyan **pivot
mark** (a ring with a "+") sits at the centre of that box: the selection's "origin". Move and Scale work about it.

**Action bar.** With a selection, in Select, Move, Scale or Duplicate, a row of four buttons appears at the bottom, left
of the tool column and just above "Re-place board": **Move | Scale | Duplicate | Deselect** (64x52 reference px each,
280 wide; on narrow screens it shrinks, never below 40%). Move, Scale and Duplicate are modes: the active one is orange
(and so is the column's Select button), tapping it again returns to Select, tapping another switches. **Deselect** empties
the selection; the bar hides and the tool returns to Select. The bar is hidden with no selection, before placement and
in Line or Circle. Entering a mode without a selection toasts "Select shapes first".

Shared rules (all three modes):
- A drag can start **anywhere on the board**, not only on the shapes (the finger never has to cover small geometry).
- Nothing happens until the finger leaves the tap slop. A **tap** on a stroke makes that shape the selection ("1 shape
  selected"), so you can switch targets without going back to Select; a tap on empty board does nothing.
- While dragging, the originals stay put with their halo, frame and pivot mark; **orange ghosts** show the result (each
  with its own width and dash), the orange cursor shows the reference/handle point, and an orange ring shows when it
  snapped. Lift to commit: one board change, snap points are rebuilt for the new geometry. A zero drag commits nothing.
- **Cancel** (second finger, Re-place, app pause, tool switch, Clear, Load) drops the ghosts; the shapes never moved.
- **Clamps ("never worse")**: a selection inside the board stays inside; one that already sticks out can't be pushed
  further out and is never forced to jump in. A clamped position is not a snap.
- The **H/V lock does not apply** to these modes.
- Shapes on hidden layers are never selected, moved, scaled or copied. If the selection empties while in a mode (layer
  hidden or deleted, Deselect, Clear, Load), the tool falls back to Select.

**Move.** The reference point is the selection's line end, circle centre or polygon centre/vertex nearest the touch-down
point within 3.2 cm (0.08 board units, `MoveGrabRadius`); otherwise it is the pivot. The selection moves by the finger's
travel, and the reference snaps to the nearest dot: a snapped line end or centre lands **exactly** (`==`) on the dot,
like Line/Circle snapping. While dragging, the selection's **own dots are hidden** (its endpoints, centres, circle
divisions and its crossings with other shapes), so it never snaps to itself; other shapes' dots and the grid stay.

**Scale.** Uniform about the pivot (which never moves): lines scale both ends, circles their centre and radius, polygons
their centre and radius; **stroke width does not change**. Drag away from the pivot to grow, toward it to shrink. The
factor is 1 + (finger distance from the pivot - start distance) / max(start distance, 2 cm), so it starts at exactly 1
and a touch near the pivot is not hypersensitive. The handle is the selection's outermost point in the finger's direction
(a line end, a circle's ring point, a polygon vertex); it snaps radially to dots, so scaling a single circle about its
centre makes it pass exactly through the dot. No shape gets smaller than 4 mm (0.01, `MinStrokeLength`) and the selection
never grows past the board edge. Lift: "Scaled to N%".

**Duplicate.** A drag places copies of every selected shape, following the Move rules (same reference point, snapping
and clamps), but the originals' dots stay snappable, so a copy can chain end-to-end onto its original. On lift each copy
gets a new id and is appended to **its original's layer** (keeping the design's layers, opacity and order); the originals
are unchanged. The copies become the selection and the tool switches to **Move**, so the next drag moves them anywhere
("Copy placed: drag to move it" / "N copies placed: drag to move them"). Tap Duplicate again to repeat. A zero drag
creates nothing ("Drag to place the copy"); Cancel creates nothing. Copying in place is impossible by design, so there
are never invisible stacked copies.

Not yet: **Undo** (still a stub with no button; every transform commits through `EditorSession.CommitTransform`, where
an undo command can snapshot clones later) and **locked layers** (not in the model; when added, `SelectionGeometry` and
`PruneSelection` must skip them).

Rendering: `BoardView` part "SelectionFrame" (render queue 3005, after the Overlay; 3000..3004 unchanged) draws the dashed
box (padded by the halo width) and the pivot mark from `SelectionGeometry` bounds, so the drawn pivot is the engine's.
Render check: `board-xform-*.png`, `board-xform-dark-*.png`, `board-xform-scale-*.png`.

## Smoothing (BoardInputRouter)
AR tracking makes the finger's projected board point jitter even when the finger is still, so the router re-projects the
finger every frame and filters the board point with a One Euro filter (`Engine/OneEuroFilter2D.cs`, time-based with
`Time.unscaledTimeAsDouble`, deterministic):
- min cutoff 1.2 Hz, beta 5 (per board-side/s), derivative cutoff 1 Hz (serialized on BoardInputRouter, tunable in the
  Inspector).
- At rest, jitter is reduced several times (check: +-0.005 jitter stays within 0.0025). While dragging at 0.5 board/s the
  ghost lags about 0.011 board units (4.5 mm); after the finger stops it settles within ~150 ms. The Down point is the raw
  projection; Move and Up use the filtered point. Same output at 30/60/120 fps for the same motion.

## Rendering changes (BoardView, BoardMeshBuilder, BoardUnlit.shader, URP)
- **Widths** (at the 40 cm board, scaled by StrokeScale as before): Thin 1.0 mm (was 1.8), Medium 1.7 mm (was 3.2),
  Thick 2.8 mm (was 5.5). Dotted pattern for strokes: 5 mm dash, 3.5 mm gap. Base frame 2 mm, grid dividers 1.1 mm.
- **Anti-aliasing in the shader**: ribbon vertices carry their offset from the centre line and side/miter scale in UV
  channel 1. The vertex shader measures the half-width in screen pixels, widens strokes thinner than 1.5 px to 1.5 px
  while fading alpha by the same factor (coverage stays right, thin lines do not break up or shimmer at distance), and
  pads 1 px for the edge ramp; the fragment shader fades the long edges over one pixel with `fwidth`. Discs and fills
  (no UV1 data) stay solid. Shader properties: `_MinWidthPx` (1.5), `_AAPadPx` (1).
- **Joins**: solid polylines (circles, polygons, the base frame) are one continuous strip with mitred joins (limit 4),
  so translucent strokes and ghosts have no darker overlaps at joints. Dashed strokes keep the per-segment dashes and the
  bounded integer loop in `AddDashedSegment` (unchanged).
- **Round circles**: segment count from the radius so the chord error stays under 0.04 mm (24..512 segments;
  `BoardMeshBuilder.CircleSegmentsFor`), instead of a fixed 96.
- **URP (Mobile_RPAsset, used by iOS)**: MSAA 4x (cleans dash ends, caps, discs; cheap on Apple tile GPUs) and render
  scale 1.0 (was 0.8, which blurred and stair-stepped thin lines).
- Visual check without the phone: `-executeMethod GeoART.EditorTools.ToolsRenderCheck.RunBatch` (no `-nographics`)
  compiles the shader for Metal and writes PNGs of a test board at 45 cm and 90 cm to `Logs/GeoARtRenderCheck/`.

## Files
- `Assets/GeoART/Engine/EditorSession.Tools.cs`: tools, gestures, ghosts, cursor, dotted toggle.
- `Assets/GeoART/Engine/EditorSession.Select.cs`, `Engine/Geometry/SelectionGeometry.cs`: the Select tool.
- `Assets/GeoART/Engine/EditorSession.Transform.cs`, `Engine/Geometry/TransformGeometry.cs`: Move, Scale, Duplicate;
  `Engine/Snap/SnapIndex.cs` `ExcludedShapes` (a transform's own shapes are not snap sources).
- `Assets/GeoART/App/ToolBar.ActionBar.cs`: the Move | Scale | Duplicate | Deselect bar.
- `Assets/GeoART/Engine/OneEuroFilter2D.cs` (new): the smoothing filter.
- `Assets/GeoART/App/BoardInputRouter.cs`: touch/mouse to `IEditorSession.Pointer`.
- `Assets/GeoART/App/ToolBar.cs`: right-edge tool column (procedural icons).
- `Assets/GeoART/BoardRender/BoardView.cs`, `BoardMeshBuilder.cs`, `Shaders/BoardUnlit.shader`: widths, AA, joins, circles.
- `Assets/Settings/Mobile_RPAsset.asset`: MSAA 4x, render scale 1.
- `Assets/Editor/Build/ToolsRenderCheck.cs` (new): shader compile + render check.
- `tools/enginecheck/ToolChecks.cs`: engine and filter checks. `tools/enginecheck/SelectChecks.cs`: Select tool checks.
  `tools/enginecheck/TransformChecks.cs`: Move/Scale/Duplicate checks.
