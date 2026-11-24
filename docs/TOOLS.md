# Line, Circle, Select, Move, Scale and Duplicate tools, and Undo

Drawing on the placed board, plus thinner and smoother strokes. Snapping is **on by default**: line ends, circle
centres and radius points jump exactly onto snap points (grid intersections, shape crossings, endpoints, circle
divisions...), and tapping two points connects them with a line. An H/V lock keeps new lines horizontal or vertical. See
`docs/SNAPPING.md`. PlaceSnapPoint / ClickAtCursor / LineBetweenSnapPoints (quick menubar) are still no-ops. **Undo**
(top-left button) takes back the last edit, one tap per edit, all the way to the start (see "Undo" below).

## How to draw
1. Place the board (quick place or corners). The tool bar appears on the right edge, vertically centred (on short or
   landscape screens it shrinks to stay between the placement row and the coach text).
2. **Line** (default tool): touch the board at the start point, drag, lift at the end point. An orange ghost follows the
   finger while dragging.
3. **Circle**: touch the centre, drag outwards, lift. The ghost shows the circle and a dotted radius line.
4. **Line mode** (Solid or Dotted, for new lines and circles; shared by Line and Circle). Line and Circle show the mode in
   their icon (solid or dashed diagonal, solid or dashed ring); a small corner triangle marks the hold menu.
   - **Tap** Line or Circle: selects the tool and keeps the mode. Tapping the active tool again never opens the menu.
   - **Hold** Line or Circle for 0.45 s without moving: the button brightens while it "charges", then that tool is selected
     and a row of chips (**Solid**, **Dotted**) pops out to the left of the button, the current mode in orange.
     Moving more than 8 reference px before 0.45 s cancels the hold (the release can still be a tap).
   - **Slide** onto a chip (it lights up) and release to pick it; the row closes. Release anywhere else and the row stays
     open: tap a chip to pick, or tap anywhere outside the chips to close without a change (that whole touch only
     closes the row: nothing is drawn and no other button is pressed). Holding the other button opens its row and closes
     this one; rotating or resizing the screen closes an open row.
   - After the first placement a one-time toast says "Hold Line or Circle for line styles" (remembered once shown).
   **Bands** (under Select): tap turns the active layer's bands (double lines) on or off, keeping its remembered mode
   and width; hold opens the layer popup with the band settings; the label shows Off / Double / Weave and the button is
   orange while on (`docs/BANDS.md`). **Width** cycles Thin / Medium / Thick for new shapes. **Colour** (under Width)
   shows the current colour and opens the colour palette (see "Colours" below).
   - **Restyle selection**: while something is selected (Select, Move, Scale, Duplicate), a palette colour, the Width
     button or a line-mode chip also applies that one attribute to the selected shapes (one "Style" undo step, toast
     "Restyled N shapes"), besides setting it for new shapes. Holding Line or Circle with a selection opens the chips
     **without** switching tool or clearing the selection; a plain tap still switches tools. This is how a band's
     structure line is highlighted in colour or weight, as in the book (p22).
   **Snap** (orange = on, the default) is at the bottom of the tool bar; **hold Snap** for the **H/V** lock chip (the Snap
   label reads "H/V" while the lock is on); see `docs/SNAPPING.md`. H/V had its own button until the **Fill** tool
   (paint bucket) needed the slot. Fill is **switched off for now** (`ToolBar.FillEnabled`, known issue in
   `docs/FILL.md`), so the column has 9 buttons: [Line, Circle, Select] [Bands, Width, Colour] [Square, 2x2] [Snap];
   with Fill on it goes after Select. The column is 550 ref px tall: it fits a 390x844 screen without insets, and on
   iPhones with a notch or Dynamic Island (390x844 with insets, 14 Pro Max) it shrinks to about 0.97 to stay between the
   placement row and the coach text. Suggestions for later work: `docs/FUTURE_WORK.md`.
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
3. **Tap a stroke**: selects only the shape whose line or circle outline is nearest the finger (within 32 screen px, at
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

**Action bar.** With a selection, in Select, Move, Scale or Duplicate, a row of six buttons appears at the bottom, left
of the tool column and just above "Re-place board": **Move | Scale | Duplicate | Extend | Delete | Deselect** (48x52
reference px each with 4 px gaps, 308 wide, so it fits the 310 px left of the column on a 390-wide phone such as the 14 Pro
Max without shrinking; on narrower screens it shrinks, never below 40%). Move, Scale and Duplicate are modes: the active one is orange
(and so is the column's Select button), tapping it again returns to Select, tapping another switches. **Deselect** empties
the selection; the bar hides and the tool returns to Select. **Delete** (between Duplicate and Deselect, a trash can)
removes the selected shapes: one "Delete" undo step, toast "Deleted N shapes", and the tool returns to Select. The bar is hidden with no selection, before placement and
in Line or Circle. Entering a mode without a selection toasts "Select shapes first".

**Extend** (between Duplicate and Delete, an arrow running into a bar) lengthens the selected lines, the way a ruler line
is run on until it meets the next line of the construction. Each end of every selected line moves along the line's own
direction to the first drawn thing in the way: another line, a circle, or a polygon edge, on any visible layer, selected
or not. The new end is the exact crossing (`Intersect.LineSegment` / `LineCircle`; when the boundary is horizontal or
vertical its fixed coordinate is copied exactly, so a horizontal line run into a vertical one ends on exactly its x).
- An end with nothing in the way stays where it is. The board edge, the grid and snap points are not boundaries.
- Hits at or behind the end (within 1e-9 board units) do not count, so an end that already sits on a line or circle goes
  on to the next boundary beyond it. Lines and polygon edges collinear with (or parallel to) the line are ignored.
- Circles and polygons in the selection are not extended (they still act as boundaries). Shapes on hidden layers are
  neither extended nor boundaries.
- All new ends are worked out against the board as it was before the tap, then applied together, so the order of the
  selected lines does not matter. Ids, styles and layers are kept; bands, fills and snap points update as for any edit.
- One "Extend" undo step, one board change, toast "Extended N ends" ("Extended 1 end"). If no end moves: no step and the
  toast "Nothing in the way to extend to". The selection, the tool and the bar stay.

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

Every Move, Scale and Duplicate commit is one undo step (see "Undo"). Not yet: **locked layers** (not in the model; when
added, `SelectionGeometry` and `PruneSelection` must skip them).

Rendering: `BoardView` part "SelectionFrame" (render queue 3005, after the Overlay; 3000..3004 unchanged) draws the dashed
box (padded by the halo width) and the pivot mark from `SelectionGeometry` bounds, so the drawn pivot is the engine's.
Render check: `board-xform-*.png`, `board-xform-dark-*.png`, `board-xform-scale-*.png`.

## Undo

**Redo.** A "Redo" button (the arrow mirrored) sits right of Undo (x 72-128 on 390x844), shown and dimmed by the same
rules with `CanRedo`; it re-applies the last undone step, and any new edit clears what can be redone.

**Button.** "Undo" (U-turn arrow) sits at the top-left corner of the safe area, 8 ref px in from the corner, 56x52 and
scaled with the tool column (`ToolBar.Undo.cs`). On a 390x844 screen it covers x 8-64, 1-7% from the top; on an iPhone 14
Pro Max it sits just below the status bar (about 7-13% from the top), clear of the column, the action bar, the layer
sidebar, the placement row, the toast and the measurements box. It shows in every tool, but only while the board is
**Placed** (hidden while placing, re-placing and in TrackingLost, so it never covers DevHud's coach line). It is dimmed
(alpha 0.4) when there is nothing to undo; a tap then toasts "Nothing to undo" and never reaches the board. While the
layer opacity popup is open its full-screen blocker covers the button. One tap = one step; no long-press repeat.

**What is a step** (toast "Undid: label"):

| Action | Label |
|---|---|
| Line drag, tap-to-connect line | Line |
| Circle | Circle |
| Move / Scale / Duplicate commit | Move / Scale / Duplicate |
| Add layer / Delete (bottom) layer / reorder | Add layer / Delete layer / Layer order |
| Hide / show a layer | Hide layer / Show layer |
| Layer opacity (one popup session on one layer) | Layer opacity |
| Band width (one slider session on one layer) / any other band setting | Band width / Bands |
| Restyle selection (colour, width or dash) | Style |
| Extend (action bar) | Extend |
| Delete (action bar) | Delete |
| Fill a region / clear a fill | Fill / Clear fill |
| Grid Square/Hex / Tiles | Grid type / Tiles |
| ClearBoard (API only, no button) | Clear board |

Never a step: selecting a layer, switching tools, selecting (marquee, tap, Deselect), Snap, H/V, the line mode, Width, the first
tap of tap-to-connect, refused actions (hidden layer, too short, 12 layers, only layer, unchanged values), identity
transforms, cancelled gestures and re-placing the board.

**History.** It starts empty at launch and walks back through every edit, up to the last **200** (`MaxUndoSteps`). When
older steps were dropped, Undo past the oldest kept one toasts "Nothing more to undo: only the last 200 actions are
kept"; otherwise "Nothing to undo". ClearBoard is undoable and keeps the history; `LoadBoard` (a different document)
clears it; re-placing the board keeps it (it only moves the AR anchor). A new edit clears Redo.

**Restore rules.** Undo first cancels any open gesture (ghosts, marquee, transform exclusion) and the pending
tap-to-connect start, then restores the board exactly (ids, order, doubles, layers, grid). The selection stored with the
step comes back in Select/Move/Scale/Duplicate (undoing a Duplicate removes the copies and re-selects the originals; the
tool stays Move); in Line/Circle the selection stays empty, and a transform tool left with nothing selected falls back to
Select. The active layer is a UI choice, not history: the current one is kept if it still exists (undoing Add layer goes
back to the previous one). Opacity drags merge: consecutive `SetLayerOpacity` calls on one layer are one step until
anything else is recorded, undone or redone, or `SelectLayer` is called (opening the popup selects the layer, so one popup
session is one step). Ids are never reissued: `NextId` never decreases between LoadBoards (every swap keeps the larger
one), and ClearBoard's fresh "Layer 1" gets a new id.

**Events.** Undo/Redo raise `[PreviewChanged if a gesture or ring was visible]`, one `BoardChanged(HistoryChangeKind)`
(Shapes, Layers, SnapPoints, Grid plus the new `BoardChangeKind.History`; Reset stays "a different document"), one
`StateChanged` and one toast. Recording a step raises nothing, so `CanUndo`/`CanRedo` only change together with a
BoardChanged and every existing event count is unchanged; the ToolBar refreshes the button on every BoardChanged.

**Redo** is in the engine (`Redo()`, `CanRedo`, toast "Redid: label" / "Nothing to redo") but has no button.

**Design: board snapshots, not commands.** Before each step the session stores a deep clone of the whole Board
(`Board.DeepClone`, about 22 KB and microseconds for 300 shapes) plus the selection ids and a label; Undo/Redo swap the
stored board in without cloning. This replaces the command stack proposed in `docs/SNAP_PATTERN_PLAN.md` Appendix A:
14 mutation paths (several with fallbacks, e.g. delete moving the active layer, Duplicate across layers, pinned doubles,
ClearBoard swapping the board) would each need a hand-written inverse, while a snapshot restores everything exactly with
one `RecordUndo(label)` line per path, placed after every refusal guard and before the first mutation (NewId included).
Snapshots are taken on Up or a button press, never per Move, so the Move path stays allocation-free.
Render check: `board-undo-*.png` (the test board after two Undos).

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

## Colours (Colour button, ColorBar, BoardAppearance)

Colour lives in the tool column: the **Colour** button is the third of the style group (Bands, Width, Colour). Its icon
is a disc in the current colour inside a light ring (a rainbow ring when the current colour is the custom one), and it
has the small corner mark of a button with a menu. Until this branch a slim colour pill sat at the bottom-left; the Select
action bar covered it exactly when a colour was needed to restyle the selection, so it was removed and nothing sits
under the action bar any more.

- **Opening the palette:** the **press** on Colour opens the palette to the left of the button, vertically centred on it
  (kept on screen). Opening it never switches tool and never touches the selection, so the action bar stays.
  - **Tap** Colour, then tap a chip; or **press, slide** onto a chip (it lights up) and **release** to pick it.
  - A touch anywhere outside the chips closes it without a change (on release, so that whole touch never draws and never
    presses another button). A second tap on Colour closes it too. Opening another hold menu (Line, Circle, Snap) closes
    it, and so do rotating or resizing the screen.
- **Palette:** two rows of 48 x 52 chips (256 ref px wide, so it ends right of the layer sidebar on a 390-wide screen):
  - row 1: the 5 shape colours, Black, White, Red, Green, Blue (the D-08 `Palette` values);
  - row 2: **Custom**, **Wheel…**, **Board** (and **No fill** while the Fill tool is active, only when
    `ToolBar.FillEnabled`; see `docs/FILL.md`).
  The current colour's chip is orange.
- **Shape colour:** a colour chip sets the colour for **new** lines, circles and polygons, exactly like the Width button,
  and closes the palette. Shapes you already drew keep their colour, so one pattern can mix colours; with a selection
  the chip also restyles it (one "Style" undo step; the selection and the action bar stay). The preview while dragging
  stays orange, as before. Default: Black.
- **Custom colour / colour wheel:** **Custom** shows the last colour picked on the wheel in a rainbow ring (grey until
  one is picked). A tap selects that colour; with none yet it opens the wheel. **Wheel…** closes the palette and opens
  the **colour wheel**: a hue/saturation disc (drag on it), a brightness slider and a preview, centred in the space left
  of the tool column (it never covers the column). **Use colour** makes it the current colour, restyles the selection if
  there is one, keeps it as the custom colour (remembered between sessions, PlayerPrefs `GeoART.CustomColor`) and closes.
  A touch outside closes without a change (on release, so it never draws). Custom colours are saved in designs like any
  other colour.
- **Board colour:** the **Board** chip switches the board's **guide lines** between **Black** (default, the original
  look) and **White** (for dark surfaces); its icon is the grid in the guide colour. It is a toggle, so the palette stays
  open. Only the dashed grid and outline (and the frame and user snap points) change colour. Nothing is filled: the real
  surface stays visible under the board.

Reference colours:

| What | Black (default) | White |
|---|---|---|
| Grid and outline (dashed) | #333340 at 85% | #EDEDF2 at 85% |
| Frame (flat scene only) | #141419 | #F2F2F7 |
| Default shape colour (`Palette.Black`) | #141418 | #141418 |

## Circles stay on the board

Like lines, a circle can no longer leave the board: its radius is capped at the distance from its centre to the nearest board edge, in the preview (circle and radius line) and in the committed circle. A circle whose centre is on the edge draws nothing. Circles that fit are unchanged (radius = drag distance, snapping as before). Engine checks: `tools/enginecheck/CircleBoundsChecks.cs`.

Notes for developers:

- The guide colour is a display setting (`BoardAppearance.Guides` in Contracts), not part of the `Board` model, so undo, layers, snapping and fixtures do not see it. It is static (reset on SubsystemRegistration with `ServiceHub`), because `BoardView` stays inactive until placement.
- The Colour button and the palette are in `App/ToolBar.Colour.cs`; the palette is a `ToolHoldMenu` with
  `OpenOnPress` (the press opens it, no hold) and `KeepOpenOnPick` (Board), its chips laid out as a grid by
  `AddHoldMenu(..., columns, chipW, spacing)` in `ToolBar.HoldMenus.cs`. `ColorBar` (same GameObject) keeps the colour
  logic (`SelectColor`, `ApplyColor`, `SelectCustom`, `SelectNoFill`, `ToggleBoardGuides`), the custom colour and the
  wheel (`ColorBar.Wheel.cs`); it no longer draws anything of its own besides the wheel.
- `SceneBuilder` adds `ColorBar` next to `ToolBar`. Scenes saved earlier (Main.unity) get one at runtime on the `ToolBar`'s GameObject.
- Tests: `VerifierColorTests`, `ColourPaletteViewTests` (EditMode), `ColourPalettePlayTests` (PlayMode, the real input
  path).

## Files
- `Assets/GeoART/Engine/EditorSession.Tools.cs`: tools, gestures, ghosts, cursor, dotted toggle.
- `Assets/GeoART/Engine/EditorSession.Select.cs`, `Engine/Geometry/SelectionGeometry.cs`: the Select tool.
- `Assets/GeoART/Engine/EditorSession.Transform.cs`, `Engine/Geometry/TransformGeometry.cs`: Move, Scale, Duplicate;
  `Engine/Snap/SnapIndex.cs` `ExcludedShapes` (a transform's own shapes are not snap sources).
- `Assets/GeoART/App/ToolBar.ActionBar.cs`: the Move | Scale | Duplicate | Extend | Delete | Deselect bar.
- `Assets/GeoART/Engine/EditorSession.Extend.cs`: Extend (`ExtendSelection`); checks in `tools/enginecheck/ExtendChecks.cs`.
- `Assets/GeoART/Engine/EditorSession.Undo.cs`, `Contracts/Model/Board.cs` (`DeepClone`): undo/redo history.
  `Assets/GeoART/App/ToolBar.Undo.cs`: the Undo button.
- `Assets/GeoART/Engine/OneEuroFilter2D.cs` (new): the smoothing filter.
- `Assets/GeoART/App/BoardInputRouter.cs`: touch/mouse to `IEditorSession.Pointer`.
- `Assets/GeoART/App/ToolBar.cs`: right-edge tool column (procedural icons).
- `Assets/GeoART/App/ToolHoldMenu.cs`: the reusable hold-to-pick menu (hold timer or open-on-press, slide-to-pick,
  blocker); `Assets/GeoART/App/ToolBar.HoldMenus.cs`: the Line and Circle mode menus, mode icons, the Bands slot;
  `Assets/GeoART/App/ToolBar.Colour.cs`: the Colour button and palette; `Assets/GeoART/App/ColorBar.cs`,
  `ColorBar.Wheel.cs`: colour logic, custom colour, colour wheel.
  EditMode `ToolHoldMenuViewTests`: tap, hold, slide, release-then-tap, blocker, slop cancel, icons.
- `Assets/GeoART/BoardRender/BoardView.cs`, `BoardMeshBuilder.cs`, `Shaders/BoardUnlit.shader`: widths, AA, joins, circles.
- `Assets/Settings/Mobile_RPAsset.asset`: MSAA 4x, render scale 1.
- `Assets/Editor/Build/ToolsRenderCheck.cs` (new): shader compile + render check.
- `tools/enginecheck/ToolChecks.cs`: engine and filter checks. `tools/enginecheck/SelectChecks.cs`: Select tool checks.
  `tools/enginecheck/TransformChecks.cs`: Move/Scale/Duplicate checks. `tools/enginecheck/UndoChecks.cs`: undo/redo checks.
