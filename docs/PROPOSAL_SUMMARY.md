# AR Geometric Pattern Studio - Proposal Summary

Source: [AR_Geometric_Pattern_Studio_Proposal-1.pdf](../AR_Geometric_Pattern_Studio_Proposal-1.pdf) (9 pages, US Letter, exported from Google Docs). The repository copy matches the PDF supplied for the October 28, 2026 review.

**Document role:** historical summary of the proposal, not a claim that its features are implemented or an assignment rubric. The repository at reviewed commit `5691ec4` contains planning documents and the PDF, but no Unity project. See [P2.md](P2.md) for the current checkpoint plan and [MVP_PLAN.md](MVP_PLAN.md) for proposed implementation decisions.

**Later clarification, October 28, 2026:** the user confirmed that **hand tracking is optional**. Touch/mouse is the baseline. The original proposal inconsistently lists pinch input in its required scope while allowing it to be dropped in the description. Preserve that history below, but do not treat hand tracking as a current requirement. Other reductions, including independent duplicates instead of linked repetition, remain implementation-plan decisions rather than statements from the proposal.

Title: "AR Geometric Pattern Studio: Lines, Circles, and Islamic Geometric Art - Project Proposal".
Team: Abdul Moeed Zahid, Ratul Mahjabin, Joseph Flynn, Luis Gonzalez.

Page map: p1-2 = proposal text (description, motivation, scope, challenges, evaluation); p3-6 = four hand-drawn phone-UI sketches (one per page, each with a typed caption); p7-8 = remaining caption + literature references; p9 = citations.

NOTE on caption placement: the typed captions sit BELOW each sketch image and sometimes spill onto the next page. Mapping used here:
- Sketch 1 (p3) caption: "1. User opens the application and mounts the board, sees the quick menubar" (printed on p3 under the image)
- Sketch 2 (p4) caption: "2. User checks out the tools available" (printed at the top of p5)
- Sketch 3 (p5) caption: "3. User makes a motif, uses layers to make it easy to delete unnecessary lines." (printed at the top of p6)
- Sketch 4 (p6) caption: "4. User switches to tools mode, scales and tiles the pattern on a 2*2 grid using duplicate, select and then dragging the shape. Snap points are essential to snap the tiles into place." (printed at the top of p7)

The sketches are hand-drawn pencil/pen photos of phone outlines (portrait iPhone-style). Each has a circled number + title written in the top-left of the photo.

---

## 1. Product overview

**Name**: "AR Geometric Pattern Studio" (proposal title). The app's in-sketch title is **"Geometric ARt"** (written at top-left of the Home screen sketch).

**Purpose** (quoting): a "smartphone AR app for iPhone". The user "scans a textured tabletop and places one anchored art board. On it, they build a small design using either tools and buttons provided on the app or a fingertip cursor and pinch gestures, and the app repeats the design across the board in a square grid." An enlarged, editable unit cell sits beside the repeated pattern, "so editing the source motif updates every copy."

**Target users**: Not stated formally. Motivation text says it "will enable artists to first create an augmented pattern on a canvas, and that will allow them to draw these patterns on the canvas a lot more easily and save time. Creating construction lines on canvas is tedious and this application saves time by making it quicker to draw the construction lines as augmented reality via a phone screen." Also learners of Islamic geometric construction (guided lesson using a Metropolitan Museum of Art teaching example). Evaluation uses 3-5 users.

**Platform**: iPhone only. Developed on a MacBook.
**Tech stack (stated)**: Unity + AR Foundation / ARKit + Xcode. Apple Vision framework for hand tracking via a small Objective-C++ bridge (copy downsampled camera frames into an owned buffer, run one detection job at a time). Core geometry "in plain C# in the board's 2D space", unit-tested on the Mac without starting AR. Signed iPhone build needed (free developer builds expire after seven days, so signing/provisioning "set up early").

**Two connected activities**:
1. **Free Design**: users assemble triangle, square, and hexagon stamps, plus lines and circles, then recolor them and use a four- or eight-copy radial helper.
2. **Guided Eight-Pointed Star**: digital compass-and-straightedge lesson: place a guide circle; mark eight equally spaced points on it; join alternate points to form one square; join the rest to form a second square rotated by 45 degrees; then hide the guide circle and diameters to reveal the star. Optional connector segments run from the star's cardinal tips to the cell's edge midpoints so repeated cells join into a connected pattern.

**Constraints / scope / timeline** (p1-2): "To fit the effort of three assignments, we are keeping the scope narrow."
- Milestones: (1) signed iPhone build with board placement, a working hand marker, and basic line/circle tools; (2) gesture construction, snapping, and the eight-pointed lesson with connected repeats; (3) persistence, performance profiling, user testing, and demo preparation.
- Performance target: at least 30 FPS (median over two minutes).
- No calendar dates given.
- Explicit hedge: hand-gesture input "is a matter of scope and we might only go with the former [on-screen tools/buttons] as the latter is more complicated."

---

## 2. Features / requirements

### 2.1 Original proposal scope (verbatim; hand input clarified above)
"one board; stamps, lines, and circles; a small palette and line-width control; pinch plus touch/mouse input; snapping and a separate guide layer; the single guided eight-pointed lesson; square-grid repetition with a bounded row/column count; undo; and save/reload (the design is rebuilt on a newly placed board)."

### 2.2 STRETCH goals (explicitly "stretch goals only")
- A six-fold lesson with a triangular lattice
- More star recipes
- Trimming
- Interlacing
- A standalone Mac editor

### 2.3 Explicitly OUT of scope
"We will not attempt all wallpaper groups, gapless tessellation of arbitrary shapes, or a general constraint solver." Also: no automatic compatible line networks for arbitrary Free Design shapes (p8); seamless joins claimed only for the guided star; free designs "may have gaps or overlaps, and we will say so."

### 2.4 Features by area

**AR / board**
- Scan a textured tabletop, place ONE anchored art board (the sketch also says "Canvas/Board Mounted on wall or placed on table").
- Save/reload rebuilds design on a newly placed board (no persistent world anchor implied).
- Enlarged editable unit cell displayed beside the repeated pattern (prose); the sketches instead show a 2x2 tile count on the board itself (see sketches).

**Input**
- Fingertip cursor + pinch gestures via Apple Vision hand joints (own pinch logic: confidence limits, separate on/off thresholds, debouncing, timestamp-based stale rejection, joint mapping via real image orientation/display transform, cursor-vs-fingertip check at several screen positions).
- Touch/mouse alternative for every command ("Every action also works with touch or mouse"; "give touch alternatives for every command").
- If tracking is lost mid-action, the unfinished shape is cancelled; editing resumes only after a fresh pinch.
- Hand-tracking is "the biggest risk"; tested on the physical phone in Milestone 1.

**Drawing / geometry**
- Primitives: lines (endpoints), circles (center + radius), stored as exact control points. Minimum lengths and radii enforced (zero-length/tangent/parallel/coincident edge cases).
- Snapping to endpoints, centers, circle-division points, and intersections.
- Separate guide layer (construction geometry that can be shown/hidden).
- Stamps: triangle, square, hexagon.
- Recolor with a small palette; line-width control.
- Radial helper: four- or eight-copy.
- Undo; delete; clear; save/load. (Sketches add Redo, Erase, Duplicate, Select/Move, Rotate, Scale, Layers.)

**Repetition**
- Square-grid repetition with bounded rows/columns (capped for performance). Store only source geometry; reuse geometry.
- Show neighboring cells while editing so users can see seams.
- Guided star: connectors with known matching boundary positions; "a small linked center/radius parameter graph rather than a general solver".

**Guided lesson**
- Single Met-attributed Eight-Pointed Star lesson (steps listed in section 1).

**Evaluation (p2)**: 3-5 users build a stamped motif and the eight-pointed star, hide guides, repeat, edit, undo, save/reload; record task completion, time, false gestures, tracking loss. Targets (not results): 8 of 10 successful placement/drag trials; median >= 30 FPS over two minutes; guided seam positions matching within 0.00001 x cell side.

---

## 3. Sketch-by-sketch

General conventions across all sketches: portrait phone outline; the AR camera view fills the upper ~60-65% of the screen; a control panel occupies the lower portion. Handwriting is in pencil/pen, annotation labels sit outside the phone outline with arrows pointing in.

### Sketch 1 - "Home screen" (PDF page 3)
Label in photo: circled **1**, "Home screen". Typed caption: "User opens the application and mounts the board, sees the quick menubar".

Elements, top to bottom, inside the phone outline:
1. **Header row**: text "**Geometric ARt**" at top-left (underline-ish tick mark below the "t"), and a **gear/flower-shaped round icon at top-right** labeled (external annotation, arrow from right) "**Settings**".
2. **AR viewport** with the **Canvas/Board**: a slightly perspective-skewed (tilted) square quad, roughly centered, occupying most of the upper viewport. Inside it a second inner square (the board) with **dashed cross lines** (one vertical dashed line and one horizontal dashed line, dividing it into 4 quadrants = 2x2 cells; the horizontal dash line is slightly slanted due to perspective). External annotation with arrow pointing to the board: "**Canvas/Board** Mounted on wall or placed on table". (Note: "wall" is mentioned here, though prose says tabletop.)
3. **Left edge, just above the control panel (layer controls)** - a vertical mini-stack:
   - "**+**" at top, annotated "**Add Layers**" (arrow from the left label to the +).
   - Below it a small rectangular icon showing stacked horizontal lines (a list/layer-stack glyph), annotated "**Arrange Layers**".
   - At its bottom a "**-**" (small dash) annotated "**Delete Bottom Layer**" (leader line pointing to the bottom edge of the layer bar / minus).
4. **Right edge, just above the control panel**: a small pill/rounded-rectangle button reading "**2x2**", annotation "**Specify no of cells on canvas**" (arrow from right label). Interaction implied: tap to set the number of repeat cells (rows x columns) on the board; default shown 2x2.
5. **Mode toggle row** (just above the buttons): text "**Hand Gestures**" at left, a **toggle switch (pill with knob/circle at left)** in the center, text "**Screen**" at right with an arrow from external note: "**Can be operated via phone screen (objects added/ placed on canvas through phone**" (the closing parenthesis is missing in the handwriting). So the toggle switches input method between Hand Gestures and Screen (touch). Knob drawn at the left, so it appears to be in the Hand-Gestures position by default (guess; ambiguous).
6. **Button grid (2 rows x 3 columns of circular buttons)**:
   - Row 1, left: circle with a **filled dot** inside. Annotation (left, arrow): "**Place Snap Point**".
   - Row 1, center: circle containing the word "**click**" with a double ring (inner circle with text "click"). No annotation; presumably a click/select/confirm action (guess: the "pinch/click" equivalent for placing/confirming when using screen mode).
   - Row 1, right: circle containing a diagonal **line segment with a dot at each end**. Annotation (right, arrow): "**Line b/w Snap points**" (written "Line bw Snap points").
   - Row 2, left: circle with a **left arrow** (Undo - labeled explicitly only in sketch 3).
   - Row 2, center: circle with a **right arrow** (Redo - labeled in sketch 3).
   - Row 2, right: circle with the word "**Tools**" - opens the Tools screen (sketch 2).

Flow: App launch -> user mounts/places board (AR plane detection; no dedicated placement UI drawn) -> quick menubar (this layout) visible -> "Tools" opens the tools panel (sketch 2).

### Sketch 2 - "Tools Screen" (PDF page 4)
Label in photo: circled **2**, "Tools Screen". Caption: "User checks out the tools available".

Elements:
1. **AR viewport (upper ~70% of screen)**: shows the board as a **rotated quad (tilted ~30 degrees)** with a smaller inner rotated square inside it (outer quad + inner square, like the board plus its enlarged cell/canvas; no dashed cross lines drawn). Additionally, a **long thin diagonal double-line strip/bar** runs from the left-middle of the screen down to the bottom-right, ending in a small small square/clasp-like end near the bottom right. Meaning unclear (see open questions - possibly a table edge, a ruler/line being drawn, or the user's hand/stylus).
2. **No header, no layer controls, no 2x2 pill, no Hand/Screen toggle** are drawn on this screen: the Tools panel replaces the whole bottom button area.
3. **Tools panel (bottom ~25% of screen)**: a header bar with the title "**Tools**" centered, then a **3 x 3 grid of text cells** (each cell is a text label, apparently also with faint circles behind them, i.e. buttons):
   | col 1 | col 2 | col 3 |
   |---|---|---|
   | **Line** | **Select/Move** | **Rotate** |
   | **Circle** | **Snap** | **Scale** |
   | **Stamps** | **Erase** | **Duplicate** |
4. Faint pencil annotations (hard to read): below/near the Stamps cell there is a short vertical leader line and faint text "**Has options**" (readable more clearly in Sketch 4, where it says "Has options" below Stamps, probably meaning Stamps opens a sub-menu/options such as triangle/square/hexagon). Also faint ghost text to the right of the Snap/Scale cells that is likely show-through from the underlying printed page (ignore).

Interactions implied: tapping a cell selects that tool; Stamps has sub-options. The 9 tools: Line, Select/Move, Rotate, Circle, Snap, Scale, Stamps, Erase, Duplicate. No explicit "close" button; no explicit way back to quick menubar is drawn (guess: tap a tool, or tapping Tools again returns).

### Sketch 3 - "Preview / creating a Pattern" (PDF page 5)
Label in photo: circled **3**, "Preview / creating a Pattern". Caption: "User makes a motif, uses layers to make it easy to delete unnecessary lines."

Phone outline with no header/title row. Elements:
1. **AR viewport (upper half)**: the board drawn nearly front-on (slightly skewed square) showing the **Guided Eight-Pointed Star construction**:
   - Outer square (the cell) with a **dot at each of its 4 corners**, and **dashed diagonals** from the corner dots running toward the center.
   - Dashed **vertical and horizontal diameter lines** through the center, with a **dot at the center**.
   - A **circle** (the guide circle) centered in the cell, inscribed well inside the cell (does not touch cell edges).
   - **Eight dots on the circle** at 45-degree spacing: top, right, bottom, left (cardinal) and four diagonal ones.
   - **Two solid squares** formed by joining alternate points: one **axis-aligned square** (corners on the four diagonal dots) and one **45-degree rotated square/diamond** (corners on the four cardinal dots), overlapping to form an **8-pointed star**. (The corner positions in the drawing are somewhat irregular because hand-drawn.)
   - Note: no connector segments to the cell edge midpoints are drawn, only the star squares + guides.
2. **Layer controls** (left side, between viewport and button panel): a **vertical segmented slider/bar** with "**+**" above and "**-**" below (same layer widget as sketch 1, but here drawn as a vertical bar with 3 tick-separated segments). Three external arrows from the left labeling the segments, top to bottom:
   - "**8 pointed Star made w squares**" (top segment/layer)
   - "**Circle**" (middle segment/layer)
   - "**Dotted lines**" (bottom segment/layer)
   So layers are a stack: top = star squares, middle = circle, bottom = dotted construction/guide lines. Layers are used "to make it easy to delete unnecessary lines" (caption) - e.g., delete the bottom (guide/dotted) layer to reveal the star ("Delete Bottom Layer" in sketch 1).
3. **"2x2"** pill at right above the divider line (same cell-count control as sketch 1).
4. A horizontal divider line, then the **mode toggle row**: "**Hand**" (left), toggle pill (knob at left), "**Screen**" (right). (Here "Hand" instead of "Hand Gestures".)
5. **Button grid** (2x3):
   - Row 1 left: circle with filled dot (Place Snap Point).
   - Row 1 center: **double concentric circle** icon (no "click" text this time; likely the Circle-drawing tool, i.e. same slot as "click" in sketch 1 - guess).
   - Row 1 right: line-with-two-endpoints icon. Annotation (right, arrow): "**Double tap to switch to dotted line**" -> double tapping the line tool toggles line style to dotted (guide/construction line).
   - Row 2 left: circle with left arrow, labeled "**Undo**" above it.
   - Row 2 center: circle with right arrow, labeled "**Redo**" above it.
   - Row 2 right: circle "**Tools**".

Flow: draw circle (center + radius via snap points) -> place snap points (the 8 dots) -> draw lines between snap points (solid for the star squares, dotted for guide diameters/diagonals) -> use layers to hide/delete the guides -> proceed to tiling (sketch 4).

### Sketch 4 - "Scaling & tiling" (PDF page 6)
Label in photo: circled **4**, "Scaling & tiling". Caption: "User switches to tools mode, scales and tiles the pattern on a 2*2 grid using duplicate, select and then dragging the shape. Snap points are essential to snap the tiles into place."

Elements:
1. **AR viewport (upper ~55%)**: the board shown as a large square (front-on) containing a **2 x 2 arrangement of four copies of the 8-pointed-star motif**. Each cell contains the motif (rotated square + axis-aligned square, drawn in **heavy/thick strokes**) with faint dashed diameters/center dot/radiating thin lines (guides, lightly drawn) behind them. Bold diamond outlines of neighbouring cells meet at the shared cell edges and at the board center, forming a connected look (diamond corners touching at the center and at edge midpoints). Small dots mark corners and edge midpoints of the whole board (snap points at board corners, and midpoints along the top/left/bottom/right edges). The top-left cell shows a center point with thin radiating lines (a radial/snap helper); others show only a faint center dot. The four cells are slightly different in hand-drawn rendering (bottom-right axis-aligned square is offset), i.e. imprecision of sketch, not necessarily a feature.
2. **No header, no layer widget, no 2x2 pill, no Hand/Screen row, no undo/redo buttons** are drawn: the full bottom panel is the **Tools** 3x3 grid (same as sketch 2) with title "Tools" and cells: Line / Select / Rotate; circle / Snap / Scale; Stamps / Erase / Duplicate. (Note "Select" here vs "Select/Move" in sketch 2.) A short leader line below "Stamps" with faint text "**Has options**".
3. No other annotations.

Interaction implied by caption: in Tools mode, user uses **Scale** to resize the motif to cell size, **Duplicate** to copy it, **Select** (move) to drag each copy, and **Snap** points to lock the copies to the grid positions, producing the 2x2 tiling. This differs from the prose (automatic square-grid repetition of the enlarged cell); the sketch describes manual duplicate/drag tiling (see open questions).

---

## 4. Overall user flow / navigation graph

```
[Launch app]
   -> [1 Home screen: board mounted, quick menubar]
        - Settings icon (top-right)          -> Settings (screen NOT sketched)
        - Tools button (bottom-right)        -> [2 Tools screen (3x3 grid)]
        - Draw via quick menubar (snap point / line / click / undo / redo)
             -> [3 Preview / creating a Pattern] (same quick-menubar layout, now with content + layer stack)
   [2 Tools screen]
        - pick Line/Circle/Snap/Stamps(has options)/Erase/Select-Move/Rotate/Scale/Duplicate
             -> returns to canvas editing (back path not drawn)
   [3 Preview/creating]
        - Tools -> [2] (then) [4 Scaling & tiling]
   [4 Scaling & tiling]
        - Tools-mode only: Select, Scale, Duplicate, drag, Snap -> 2x2 tiled pattern
```
The numbered captions imply the narrative order 1 -> 2 -> 3 -> 4, but the sketches do not draw explicit arrows between screens. The Tools button is present on sketches 1 and 3; the Tools panel is shown in 2 and 4. Toggling Hand/Screen and the 2x2 cell-count are on the quick menubar (1 and 3).

---

## 5. Data / model concepts

- **Board / canvas**: single AR-anchored plane (table or wall per the sketch annotation); 2D coordinate space for all geometry (plain C#).
- **Cell / unit cell**: square cell holding the source motif; **grid** of bounded rows x columns (sketch default **2x2**, user-specifiable "no of cells" via the pill). Repeats generated from source geometry only (store only source geometry, cap rows/cols).
- **Primitives**: Line = two endpoints (control points); Circle = center + radius; **Snap points** (placed explicitly by the "place snap point" button) for endpoints, centers, circle-division points (eight equally spaced points), intersections. Minimum length/radius enforced.
- **Line styles**: solid and **dotted** (double tap line tool toggles dotted; dotted = guide/construction line).
- **Layers**: ordered stack, add (+), arrange, delete bottom layer (-). In sketch 3 layers = [8-pointed star (top), Circle, Dotted lines (bottom)]. PDF text separately mentions a "separate guide layer" and showing/hiding guide circle and diameters.
- **Stamps**: triangle, square, hexagon (and "has options"). Recolor from a "small palette"; "line-width control".
- **Radial helper**: 4- or 8-copy.
- **Transforms (tools)**: select/move, rotate, scale, duplicate, erase.
- **Symmetry / tiling**: square-grid translation only (no wallpaper groups); radial 4/8 helper; seam matching targeted only for the guided star via connectors (cardinal tips to edge midpoints) with matching boundary positions (tolerance 0.00001 x cell side). Linked center/radius parameter graph for the guided star (changing radius/center rebuilds the construction).
- **Persistence**: save/load the design (rebuilt on a newly placed board). No export/share (image, PDF, AR Quick Look, etc.) is mentioned.
- **Undo/Redo** (Redo only appears in sketches), delete, clear.
- **Hand input model**: Vision joints (image-space) -> mapped to board/screen; pinch with hysteresis; cursor "hand marker".

---

## 6. Original ambiguities and current clarifications

1. **Settings screen** content is not sketched (gear icon in sketch 1 only). Possibly color palette, line width, mode, save/load - all guesses.
2. **Where are the palette and line-width controls, save/load, clear, delete?** The required-features list includes palette, line width, and save/reload, but no sketch shows them (maybe under Settings, Stamps options, or Tools). Clear/delete appear only in prose.
3. **"click" button** (sketch 1 center, row 1) vs the **double-circle icon** (sketch 3 same slot): same button redrawn, or a Circle tool vs a click/select action? Guess: the sketch-1 "click" is the screen-mode equivalent of a pinch; the sketch-3 double circle is the Circle tool. Unresolved.
4. **Hand Gestures / Screen toggle**: original default state and exact semantics are unclear (knob drawn on left in both sketches). **Resolved scope:** hand tracking is optional, as confirmed by the user on October 28, 2026. The current plan uses touch/mouse and shows Hand disabled; the PDF's required-pinch wording does not override that clarification.
5. **Sketch 2 viewport**: what the long diagonal double-line strip is (table edge? a drawn line? user's finger/pen?) is unclear. Also whether the board is rotated because the phone is rotated in AR.
6. **Sketch 2/4 "Has options"** under Stamps is faint pencil; reading is my best interpretation. Also unclear what the options are (triangle/square/hexagon, colors?).
7. **Tools panel vs quick menubar**: does the Tools panel replace the bottom buttons (as drawn), slide up over them, or toggle? How to exit it (no close/back button drawn; Tools button not visible in sketches 2 and 4).
8. **Tiling model mismatch**: Prose says automatic square-grid repetition with an enlarged editable cell beside the repeated pattern (live updates). Sketches 1/3 show a "2x2" cell-count control and sketch 4 shows manual tiling with Duplicate + Select + drag + snap. Is the 2x2 pill an automatic repeater or just the grid size for snapping? No sketch shows the "enlarged unit cell beside the repeated pattern" layout.
9. **Wall vs table**: sketch 1 says "Mounted on wall or placed on table", prose says tabletop only (plane detection for table; wall would need vertical plane detection).
10. **Layer UI**: the segmented bar in sketch 3 vs. the "+ / list icon / -" in sketch 1: how a layer is selected, hidden/shown (eye icon?) is not drawn; only add, arrange, delete bottom layer. Whether the 3 layers in sketch 3 are auto-generated by the guided lesson or user-created is unclear.
11. **Place Snap Point** semantics: explicit snap-point placement vs automatic snapping to endpoints/centers/intersections described in prose.
12. **Guided lesson entry point**: the prose describes a separate "Guided Eight-Pointed Star" mode with step-by-step instructions, but no sketch shows lesson text, step navigation, a Free Design / Guided mode selector, or a home/main menu. Sketch 3 looks like the result of manual construction (labels "8 pointed Star made w squares"), possibly the lesson's end state.
13. **Connector segments** (cardinal tips to cell edge midpoints) are not drawn in sketches 3 or 4 (sketch 4 does show diamonds touching at edge midpoints/center, which may be what this means).
14. **Export/share** is not mentioned anywhere; **onboarding/board-placement UI** (plane scan, reticle, confirm placement) is not sketched.
15. Sketch 4 shows no Undo/Redo, Hand/Screen toggle, or layer controls - are they hidden in Tools mode?
16. Text legibility: "Can be operated via phone screen (objects added/placed on canvas through phone" and "Specify no of cells on canvas" are handwriting transcriptions; I am confident of them. The "Hand Gestures" label (sketch 1) vs "Hand" (sketch 3) both read clearly.

## Appendix: references (p7-9, for context only)
Kaplan 2000 (Computer Generated Islamic Star Patterns, Taprats); Kaplan & Salesin 2004 (Islamic Star Patterns in Absolute Geometry, Najm); Kaplan 2005 (Islamic Star Patterns from Polygons in Contact, Hankin's method); Zhang et al. 2020 (MediaPipe Hands). Stated relevance: separating motif from repeated pattern; construction geometry vs finished artwork (show/hide guides); adjustable geometric values (center, radius, equal points) for a consistent rebuild; connectors at cell boundaries; hand detection then gesture interpretation (pinch = thumb/index relationship). The proposal selects Apple Vision instead of MediaPipe if the optional hand-input feature is implemented; neither integration exists in the reviewed repository.
