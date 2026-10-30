# Line and Circle tools

Drawing on the placed board, plus thinner and smoother strokes. Snap points come later (snapping is off: no candidates,
PlaceSnapPoint / ClickAtCursor / LineBetweenSnapPoints are no-ops). Undo is not implemented.

## How to draw
1. Place the board (quick place or corners). The tool bar appears on the right edge, vertically centred.
2. **Line** (default tool): touch the board at the start point, drag, lift at the end point. An orange ghost follows the
   finger while dragging.
3. **Circle**: touch the centre, drag outwards, lift. The ghost shows the circle and a dotted radius line.
4. **Dotted** toggles dashed strokes for new shapes (orange when on). **Width** cycles Thin / Medium / Thick for new shapes.
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
- Other tool ids (Select, Rotate, Stamps, ...) toast "Coming soon" and keep the current tool.

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
- `Assets/GeoART/Engine/OneEuroFilter2D.cs` (new): the smoothing filter.
- `Assets/GeoART/App/BoardInputRouter.cs`: touch/mouse to `IEditorSession.Pointer`.
- `Assets/GeoART/App/ToolBar.cs`: right-edge tool column (procedural icons).
- `Assets/GeoART/BoardRender/BoardView.cs`, `BoardMeshBuilder.cs`, `Shaders/BoardUnlit.shader`: widths, AA, joins, circles.
- `Assets/Settings/Mobile_RPAsset.asset`: MSAA 4x, render scale 1.
- `Assets/Editor/Build/ToolsRenderCheck.cs` (new): shader compile + render check.
- `tools/enginecheck/ToolChecks.cs`: engine and filter checks.
