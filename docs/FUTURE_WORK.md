# Future work

Suggestions for later, in the order the UI-lines plan used. Each section says what it is and why it helps geometric
design, the proposed UX, how to build it (with the code it builds on and the files it would likely touch), the main
risks and a rough size (S = a day or less, M = a few days, L = a week or more).

Already done on this branch: the Line hold menu, bands (Double, stripped, interlace, inner lines, structure cut),
easier snapping (32 px, adaptive cap, sticky release, haptics), restyle selection, the colour wheel, Delete in the action
bar and the Redo button. Fill is built but switched off (section 1).

---

## 1. Fill: finishing it
**What and why.** Colouring closed regions is the book's next step after bands (p23: shapes "decorated with patterns and
colour"). The tool, regions, seeds, undo, saving and drawing are all built (`docs/FILL.md`); it is hidden only because
of the triangulator issue described in `docs/FILL.md`, section "Known issue".

**UX.** As documented in `docs/FILL.md`: a paint-bucket tool after Select, tap a closed region to fill it with the
current colour, tap again to replace, "No fill" in the colour bar to clear.

**Approach.**
- Fix `Triangulator.Bridge` (`Engine/Geometry/Triangulator.cs`): standard Eberly hole bridging (hit point, candidate
  vertex, reflex-vertex visibility test with the angle tie-break, and the right copy of a repeated vertex chosen by its
  wedge), or bridge to the ray's hit point itself.
- Add a fuzz check in `tools/enginecheck/FillChecks.cs`: random non-overlapping holes in random polygons; the triangles'
  area must equal the region's and no two triangles may overlap. Bring in the tester's `VerifierFillChecks` and
  `VerifierFillViewTests`.
- Set `ToolBar.FillEnabled = true` (`App/ToolBar.cs`): the Fill button returns after Select (9 buttons) and the colour
  bar's "No fill" works again; the view tests already follow the switch.

**Risks.** Other degenerate regions (many holes touching the outer boundary at one vertex). The fuzz check is the
safety net.
**Size.** S-M.

## 2. Angle lock
**What and why.** Islamic patterns are built on 4-, 8-, 6/12- and 16-fold symmetry. Locking new lines to multiples of
90, 45, 30 or 22.5 degrees makes stars and rosettes quick to draw by hand, generalising the H/V lock.

**UX.** Hold **Snap**: chips H/V (90), 45°, 30°, 22.5° next to each other (the H/V chip is already there). Tap a chip to
lock to that step, tap the active chip to unlock. The Snap label shows the step ("H/V", "45°", ...).

**Approach** (reviewed with the advisor):
- Contract: `double AngleStep { get; }` (0 = off) and `SetAngleStep(double)`; `OrthoLock` stays (`AngleStep > 0`),
  `SetOrthoLock(true)` keeps the current step. Three implementers: `EditorSession`, `StubEditorSession`, the PlayMode
  `RecordingSession`. One static array of the allowed steps shared by UI, session and checks.
- Engine (`EditorSession.Snap.cs`): generalise `TryOrthoEnd` to an axis direction. For 90 keep today's dominant-axis
  test and the exact `OnAxis` path (the H/V checks require bit-exact coordinates). For oblique steps choose the
  direction with the largest `dot(raw - anchor, dir)`; directions come from an exact table (sqrt(0.5),
  sqrt(3)/2, cos/sin 22.5) built by quadrant swaps and negations, next to `CircleDivisions`, never `Math.Cos`.
- End point: clamp to the board by setting the binding coordinate to exactly 0 or 1. Alignment offers along the axis use
  a parameter t; the winning point is the crossing itself, or the candidate's own position when it lies on the axis
  line, else `anchor + dir * t`. BoardCap applies as it does for H/V.
- Re-track an open gesture when the step changes (compare the step, not just the bool, in `OnSnapSettingsChanged`).
- Tap-to-connect with the lock on: keep the exact X/Y rule for 90; for other steps connect only pairs whose direction is
  a multiple of the step (`|cross(q - a, dir_k)| <= 1e-9 |q - a|`), else a toast.
- UI: `ToolBar.HoldMenus.cs` (the Snap menu's chips and label), one angle glyph in `ToolBar.cs`.

**Risks.** Exactness of oblique ends; tie rules in the H/V checks (`OrthoChecks`).
**Size.** M.

## 3. Rotate tool
**What and why.** Rotating a selection about its pivot, snapping to the angle step, is how a motif is copied around a
centre. `ToolId.Rotate` already exists (it toasts "Coming soon").

**UX.** A Rotate button in the action bar (with Move, Scale, Duplicate). Drag anywhere: the selection rotates about its
pivot by the finger's angle around the pivot, snapping to the angle-lock step when one is on (15 degrees otherwise).

**Approach.** Mirror the Scale path in `EditorSession.Transform.cs`: a `ShapeTransform.RotateAbout(pivot, angle)` in
`Engine/Geometry/TransformGeometry.cs` (lines, circles, polygon `RotationRad`), clamp to the board as Move/Scale do, ghosts
and one "Rotate" undo step; `ToolBar.ActionBar.cs` button and glyph. Fill seeds would not follow (see `docs/FILL.md`).

**Risks.** Exactness after rotation (snapped points become irrational); clamping rotated shapes that would leave the board.
**Size.** M.

## 4. Radial symmetry drawing
**What and why.** The proposal's "4/8 radial helper": draw one line and get its N-fold rotated copies, the fastest way to
build a rosette.

**UX.** A symmetry chip (in the Line hold menu): pick a centre (a snapped dot) and N = 4, 6, 8, 12 or 16. Every line drawn
adds N - 1 rotated copies, shown as ghosts while dragging; one undo step per stroke.

**Approach.** Session state (centre, N) and a commit hook in `EditorSession.Tools.cs` that adds the copies; exact
rotation for N = 4 and 8 via the angle table of section 2; ghosts in `RebuildGhost`; snap points rebuilt once per stroke.
Builds on Angle lock's direction table.

**Risks.** Many shapes quickly (snap point count), near-duplicate copies when a line passes through the centre.
**Size.** M-L.

## 5. Chain tap-to-connect (off by default)
**What and why.** After connecting A to B by taps, B becomes the next start, so a star outline is one tap per corner.

**UX.** A "Chain" toggle chip in the Line hold menu, off by default. With it on, tap the ringed point or empty board to
stop the chain.

**Approach.** In `EditorSession.Tools.cs` `Tap()`, after a connection keep `_pending = q` instead of clearing it when the
setting is on. The off state keeps today's tested rule. Toggle on the contract or as a concrete setting read by the menu.

**Risks.** It inverts a tested rule, so it must stay opt-in; users may not notice the chain is still active (the ring
shows it).
**Size.** S.

## 6. Bake bands to lines
**What and why.** Turns a banded layer's outlines into real, editable lines (snappable, restylable) for people who want to
edit individual outline segments.

**UX.** A "Bake" button in the layer popup's Bands section: writes the outline runs (and bridges, inner lines) as lines
on a new layer "<name> bands" above, hides the source layer. One undo step "Bake bands". Warns when that is many lines.

**Approach.** Read `BandOutline.Runs`, `InnerRuns`, `Bridges`, `InnerBridges` (`Engine/Geometry/BandOutline.cs`) for the
layer's current options (`BandOutlineOptions.ForBoard` with the board side); one `LineShape` per run segment with new ids;
a session command (contract + three implementers); UI in `App/LayerSidebar.cs`.

**Risks.** Many snap points (performance and clutter); the baked lines no longer follow the structure.
**Size.** M.

## 7. Guide / construction line mode
**What and why.** The book draws construction "in pencil" and erases it later. A faint line mode that is never banded and
can be hidden helps keep construction apart from the pattern (layers cover most of this today).

**UX.** A third chip "Guide" in the Line hold menu; guides draw thin and faint; a "Hide guides" view switch.

**Approach.** `LineStyle.Guide = 2` (append-only; an older app reads it as Solid), drawn faint in `BoardView`, skipped by
`BandGraph` and fill regions, a static view flag like `BoardAppearance.Guides` (not saved).

**Risks.** Overlaps with what layers already do; one more style everyone must handle.
**Size.** S-M.

## 8. Fewer, bigger buttons (Grid + Tiles merge)
**What and why.** Bigger targets are easier to hit with a thumb while holding the phone steady in AR. H/V already moved
into Snap's hold menu; merging Grid (Square/Hex) and Tiles (1..4) into one button frees another slot.

**UX.** One grid button: tap cycles the tiles, hold opens chips for Square/Hex x 1..4.

**Approach.** `ToolHoldMenu` needs a **two-row chip menu**: 8 chips x (56 + 8) px = 504 px is wider than the 390 px
screen. Add rows to `ToolHoldMenu.Configure` and the row builder in `ToolBar.HoldMenus.cs`; update the column (`ToolBar.cs`
`ButtonCount`, `ButtonH`) and the view tests that list the column. Also fix the colour bar's 21:9 overlap here (section 13).

**Risks.** Layout tests across screen shapes; discoverability of the hold menu.
**Size.** M.

## 9. Live measurement
**What and why.** Length in cm and angle in degrees next to the finger while drawing makes precise constructions
possible (equal segments, exact angles).

**UX.** A small label near the cursor during a Line drag: "12.4 cm · 45°"; for Circle the radius.

**Approach.** The engine already has `_anchor`/`_current`; expose them (or a measurement string) to the view; draw a
`Text` that follows the projected cursor (an App component using `IBoardSpace.TryBoardToScreen`); cm from the board side
(`ICornerPlacement.BoardSideMeters`).

**Risks.** Clutter under the finger; per-frame text updates (keep them allocation-light).
**Size.** S-M.

## 10. Clean view / screenshot
**What and why.** Hide dots, grid and ghosts and share an image of the design, for showing and checking work.

**UX.** A camera button: hides the UI, candidates and grid for one frame, captures the screen, shows the iOS share sheet.

**Approach.** View flags in `BoardView` (candidates, base) and a hide-all on the UI canvases; `ScreenCapture` on the next
frame; a small native share plugin (iOS) like the haptics plugin.

**Risks.** Native share sheet plumbing; AR camera image permissions.
**Size.** M.

## 11. Width picker chips
**What and why.** Picking Thin/Medium/Thick directly instead of cycling saves taps and shows the choice.

**UX.** Hold **Width**: chips Thin / Medium / Thick (the current one orange); a tap still cycles. With a selection the
chip restyles it (as the line-mode chips do).

**Approach.** Reuse `ToolHoldMenu` via `AddHoldMenu` in `ToolBar.HoldMenus.cs` with three width glyphs; `PickWidth` sets
`Style` and calls `RestyleSelection(StyleParts.Width, ...)` when something is selected.

**Risks.** None to speak of.
**Size.** S.

## 12. Inner-line distance fade (AR readability)
**What and why.** At about 90 cm on a tilt, a band's inner and outer lines can merge into one grey band on the far side,
because the shader keeps every stroke at least 1.5 px wide. Fading the inner lines with distance keeps the pattern
readable.

**UX.** Nothing to set: inner lines fade out smoothly where they would merge and come back as you move closer.

**Approach.** Draw inner runs and inner bridges on their own mesh part in `BoardView` (today they share the Shapes mesh),
and fade them in `BoardUnlit.shader` by a per-frame uniform (pixels per board unit at the fragment, or camera distance
and angle), so nothing is rebuilt per frame. A level of detail chosen only when the mesh is rebuilt would go stale as the
user walks.

**Risks.** Shader work on device (Metal), the draw order of the extra part (fills and strokes share one mesh today).
**Size.** M.

## 13. Small follow-ups (open NITs)
- **Colour bar overlaps the tool column on 21:9 Android screens** (about 4.5 x 25 ref px). The bar grew from 292 to
  308 px with the custom swatch; iPhone keeps about 11 px clear. Fix with the toolbar rework (section 8): shrink the bar on
  narrow aspect ratios (`App/ColorBar.cs` `ApplySafeArea`) or move the Board button. Size S.
- **Band topology rebuilt for every banded layer on any shape change** (`BoardRender/BandCache.cs`), including a stroke
  on another layer and a restyle. Fix: keep a per-layer geometry signature (kind, id and coordinate bits, not style) and
  re-planarize only when it changed; the same idea works for `FillCache` if seeds are included. Size S.
- **Tiny specks where an over band's outline meets its bridge** (double-blended anti-aliasing, visible at 5x zoom).
  Fix (worked out with the advisor): trim each bridge end by `h / sin(angle)` along the bridge, where h is half the
  outline stroke width and the angle is between the bridge and the under band's edge at that corner, and end it with a
  cut along the under band's edge (`BoardMeshBuilder.AddOpenPolyline`). The bridge and the run then tile without overlap
  at any crossing angle. Size S.
- **Transitive collinear merge**: fixed (`PlanarGraph.MergeCollinear` merges only real overlaps along one axis).
- **Pathological band speed**: 500 long board-spanning lines (about 10^4 crossings) take about 250 ms for the global
  crossing count; it runs only in the layer popup (open, release, shape edits), never while drawing. If it ever matters,
  count crossings with a sweep line or only near the changed region. Size S-M.
