# Bands (double lines), stripped view and interlace

Book pages 22-23 ("Design Tips"): each structure line of a pattern is encased in a band of equal width. The band edges
meet in sharp mitres at corners, and at a crossing each band's edges stop at the other band's edge, so the crossing square
is open. With the structure line removed the same outlines are the "stripped" view. In an interlace one band goes over and
the other under at each crossing, alternating along every band, and an optional second pair of lines runs inside each band.

Bands are a **per-layer, non-destructive setting** (`LayerBands`, `Contracts/Model/Bands.cs`). A banded layer draws its
lines and polygon edges as bands and recomputes them after every edit, Undo or Redo. Circles are never banded.

Status: model, geometry, layer field, undo, save, rendering, the layer popup and the toolbar button are done (P2, P4):
Double, stripped, Interlace with Flip weave, inner lines, and the under band's structure line cut at woven crossings
(P5).

## Using bands
1. Draw the structure on a layer (lines and stamps; circles are construction and are never banded).
2. Tap **Bands** in the tool column: the active layer's lines become bands (6 mm on the 40 cm board). Tap again: off.
   The layer remembers its settings while off.
3. Hold **Bands** (or hold the layer's dash in the sidebar) to open the layer popup:
   - **Off / Double / Interlace**: Interlace weaves the bands over and under, alternating along every band (toolbar
     label "Weave"),
   - **Width** slider, 1.6 to 20 mm, shown in mm for this board (board units x the board's side). One drag is one undo
     step ("Band width"),
   - **Structure line**: on = the book's bold line inside the band; off = the "stripped" view,
   - **Outline**: sets the band outline colour to the current colour (the swatch shows it),
   - **Inner lines**: a second pair of lines inside each band (book p23 bottom). Turning them on raises the band to at
     least **10 mm** (times the stroke scale, like the inner-line limits) in the same undo step, so the inner lines show and stay 2 mm clear of a Medium
     structure line,
   - **Flip weave** (Interlace only; dimmed otherwise, the setting is remembered): swaps every over and under,
   - a note: amber "Bands this wide make the shapes small." above 75% of the largest safe width, red "Too wide: the bands
     overlap. Make them narrower." at or beyond it **or** whenever the outlines cross (`SelfIntersections` > 0, e.g. a
     pinched U whose ends nearly touch), amber "N crossings can't alternate." in Interlace when the weave has conflicts
     (T-junctions, free ends or a closed band crossed an odd number of times), grey "Inner lines need a wider band."
     when inner lines are on but don't fit, grey "Circles are not banded." when the layer has circles. Priority: red,
     amber width, amber conflicts, grey.
4. To highlight the structure line, select its lines and tap a colour or Width (restyle selection, `docs/TOOLS.md`).

Every band change is undoable ("Bands"; width drags merge per layer into "Band width"), saved with the design
(`docs/SAVE.md`) and recomputed after any edit, Undo or Redo. The weave never flips across Undo, Redo or reload.

### Rendering and cost
- `BoardView` draws a banded visible layer's outlines (outline colour and width, layer opacity) before its structure,
  hides lines and polygons when the structure is off, and always draws circles and snap points.
- `BandCache` (BoardRender) keeps per layer the topology (rebuilt only after a shape change, Undo/Redo or a reload) and
  the outline (rebuilt only when the band settings change). A width drag never re-planarizes; however many slider
  events arrive in one frame, BoardView rebuilds once in LateUpdate. Hidden layers and layers with bands off cost
  nothing.
- The global crossing count (`SelfIntersections`) is not needed for drawing and is skipped there; the popup counts it
  on open, after shape edits and when the width slider is released, never per slider tick.
- Measured in the editor (EditMode `BandsViewTests.BandRebuildCostForTheWidthSlider`, 200 tile-like lines): planarize +
  outline 4.0 ms, a width-slider frame (outline + the Shapes mesh) 4.6 ms. Estimate for an iPhone: 2-3x, so about
  10-14 ms for 200 lines; the book fixtures (8-128 lines) stay well under a frame. To be confirmed on the device.

## Model: `LayerBands`
| Field | Meaning | Zero / false value |
|---|---|---|
| `On` | the layer is banded | off (old saves, `default(LayerBands)`) |
| `Mode` | `Double` or `Interlace` (enum append-only) | Double |
| `Width` | band width, outer edge to outer edge, board units | NaN / <= 0 read as 0.015 (6 mm on the 40 cm board) |
| `HideStructure` | stripped view | structure line shown |
| `InnerLines` | second pair of lines inside each band | off |
| `FlipWeave` | swap every over/under | off |
| `OutlineColor` | outline colour | A == 0 reads as black |
| `OutlineWidth` | outline stroke width | Thin |

`Sanitized()` clamps the width to [0.004, 0.05] (1.6 to 20 mm), maps unknown enum values to Double / Thin and a
transparent colour to black. `LayerBands.Default` is off, Double, 6 mm, structure shown, black Thin outlines.

## Geometry (pure C#, `Engine/Geometry/`)
Three classes, built only when the shapes or the width change (never on the pointer path):

### `PlanarGraph`: planar arrangement (also reused by Fill later)
1. Drop non-finite and zero-length segments. Snap every endpoint coordinate within 1e-5 of a side onto it, clip to the
   board [0,1]^2 (a clipped end gets the side's coordinate exactly) and snap again.
2. **Endpoint weld**: endpoints within 1e-5 become one point (a point on more frame sides wins, so a corner beats a
   side point, then the lexicographically smallest). **Near-junction weld**: an endpoint within 1e-5 of another
   segment's interior (under- or overshoot) moves onto it; the ends are re-welded and the pass repeats against the moved
   geometry until nothing moves (at most 4 passes).
3. **Collinear merge**: segments collinear within 1e-5 that **overlap** become one segment between the extreme original
   endpoints (bit-exact), provided every member lies on that segment's own line (lines drawn over lines collapse to one
   edge). Segments that only touch end to end (a tap-to-connect chain A-B + B-C) stay separate edges meeting at a straight
   node, so a chain of short chords along a shallow arc never fuses into one straight line. One more near-junction pass
   then re-attaches ends to the merged segments.
4. **Crossings**: every pair (uniform-grid prefilter) through `Intersect.SegmentSegment`, which returns segment ends
   bit-exactly (T-junctions). Points within 1e-7 are one node. Segments are split at their nodes into edges.
5. **Frame**: the four board sides become edges (source `FrameSource`), cut at every node on them.
6. **Half-edges**: at each node the outgoing half-edges are sorted CCW; `Next(h)` is the half-edge at h's head just
   clockwise of its twin, so every face cycle keeps its face on the left. Bounded faces are CCW (positive area).

The input is put into a canonical order first and every tie is broken by coordinates, so the same shapes in any order or
direction give a bit-identical graph and outline.

### `BandGraph`: band topology of a layer
- **Side factor** per half-edge: 1 for a structure line, 0 for the frame and for the outer side of a structure line lying
  on the frame (nothing is offset outside the board along it).
- **Threads** (one continuous band each) turn at degree-2 nodes (also on the frame) and pass through structure-degree-4
  nodes, where sorted index i pairs with i+2 (a crossing; this also covers two star tips touching tip to tip). The
  exception is a degree-4 node where two ring-ADJACENT edges are collinear: a straight line with a tip touching it from
  one side. There the line goes straight on, the tip turns, and the node is a flat junction, not a crossing. Threads stop
  at degree 1, 3, 5+ and at degree-4 nodes on the frame.
- **Crossings** are the other structure-degree-4 nodes off the frame. Pass p (0 or 1) of a crossing is the pair holding sorted
  index p. **Weave**: along every thread over and under alternate, so consecutive visits (c1, p1), (c2, p2) give
  `o1 XOR o2 = 1 XOR p1 XOR p2` for the over-pass bits (closed threads include the wrap-around pair; a thread passing the
  same crossing twice is a self-constraint). BFS 2-colouring in node order; each component is seeded at its smallest
  (x, y) crossing with the pass holding the smallest-angle half-edge over. The seed comes from geometry, never from ids,
  so Undo, Redo and reload never flip the weave. A violated constraint is counted in `WeaveConflicts` ("N crossings can't
  alternate"): it can only happen where threads are cut at odd-degree nodes (T-junctions, free ends) or a closed band
  crosses another band an odd number of times. Degree 6 and 8 nodes (rosette centres) get no bit and are drawn flat.

### `BandOutline`: outlines for one width (h = width / 2)
- **Corner** of the wedge at node u between a half-edge (direction d0, left offset x0) and its CCW successor (direction
  d1, right offset x1): the two offset lines meet at `t = (x1 + x0 cos theta) / sin theta` along d0 and
  `s = (x0 + x1 cos theta) / sin theta` along d1. With equal sides that is `h cot(theta/2)` along each edge (h at 90
  degrees, 2.41h at a 45-degree tip), or `h / sin(theta/2)` along the bisector. Every tip keeps its **true mitre**
  (16-fold star tips are never blunted); only the outer side of a tip under 5 degrees is bevelled, at the two plain offset
  points.
- **Free end**: a square cap extended by h around the end (the book's "compass method" circle of radius h at every node),
  so the structure stroke always stays inside the band.
- **Near-straight joint** (within 1e-6 rad of 180 degrees): the plain offset point; with unequal sides (a line on the frame
  running into the frame) a two-point step down to the frame.
- **Width safety**: the offset of half-edge g has length `L - h (k_tail + k_head)`, where k = t / h at each end
  (`cot(theta/2)` between two bands, `cot(theta)` for a band against the frame, `1 / sin(theta)` for a frame piece next to
  a band, 0 at a bevel, -1 at a cap). It **collapses** at or below 0 (`Collapsed`; a drawn one is replaced by the meeting
  point of its neighbours' offset lines so the run stays continuous). `MaxSafeWidth = 2 min L / (k_tail + k_head)` over
  every half-edge with a positive sum, frame pieces included (two lines closer than the width that both reach the frame
  are flagged), and never more than the **near-miss distance**: the smallest gap between two structure edges that are
  topologically far apart (no shared node, no edge joining their ends), such as a stub that stops short of a line or an
  inner line close to a side; their bands touch at exactly that width. `SelfIntersections` counts proper crossings between
  the inset polygons of all faces together (one face's loops, separate parts), which also catches a reflex corner
  running into a non-adjacent side (split events). The layer popup will show amber near `MaxSafeWidth` and red
  when either count is above 0.
- **Runs**: walking each face cycle, consecutive drawn offsets join into one polyline with mitred joins; the frame ends a
  run, so bands end flush with the board edge; a face that touches no frame is one closed run. This alone is the flat
  Double look with open crossing squares, and without the structure line the stripped look.
- **Interlace**: at each woven crossing the over band's two **bridges** join the corners on each side of it, which breaks
  the under band at the over band's edges. Bridges are separate segments.
- **Inner lines** at `h_in = h - max(0.2 width, 2 mm)`, drawn only when `h_in >= Medium core half width + 1 mm` (both mm
  values scale like the strokes; one formula, `BandOutlineOptions.ForBoard`, is used by BoardView and the popup). At
  10 mm (the bump when they are turned on) the inner line is 2.15 mm from a Medium core. Per wedge:
  - between two sides of the same band (a thread turns there: a degree-2 corner or a tip resting on a line), at a free
    end, at a near-straight pass-through (the bar of a T) and at a bevel: they **join** like the outer lines, at h_in;
  - at every other wedge (junctions, crossings, the frame) each inner line **ends on the neighbouring band's outer
    edge** (on the frame itself for the frame). So at a T the bar's inner line stops at the stem's outer edges and the
    stem's inner lines stop at the bar's outer edge, and in flat Double both bands' inner lines stop at the crossing;
  - at a woven crossing the over band's inner lines are bridged straight through (`InnerBridges`), and the under band's
    inner lines end on the over band's outer edge (book p23 bottom).

- **Inside the board**: after the safety counts every corner is clamped to [0,1]^2. A corner on a frame line stays on
  it; a tip spike or cap near the frame is cut at the board edge.

Budget: 500 tile-like segments planarize and outline in about 10-15 ms on the desktop (check limit 50 ms); the book
fixtures (8 and 32 lines) take under 0.5 ms.

Known limits and decisions (v1):
- A kinked "crossing" (two bent bands meeting at one point, pairing i with i+2) gets bridges between the corners next to
  the over band, a chord rather than the band's own mitre.
- Near misses are tracked only below 0.1 board units (twice the widest band).
- Pathological designs (500 long board-spanning lines, about 10^4 crossings) take about 250 ms for the global crossing
  count on the Mac; it only runs in the popup (open, release, shape edits), never while drawing or dragging.
- Interlace with "Show structure line" on: the under band's structure line is **cut** at each woven crossing, so it goes
  under with its band (`BandOutline.StructureCuts`). Per under half-edge the hidden stretch runs from the node to the
  over band's edge on that side (the under line meets that side's bridge; `h / sin(angle)` when the over thread is
  straight), so a kinked over or under thread is handled side by side. Every input segment lying along that half-edge
  (collinear duplicates included, matched within 2e-5 so welded lines still count) is cut in its own parameter; a segment
  through two crossings gets two cuts. BoardView draws the visible pieces as open polylines whose cut ends run along the
  over band's edge (skewed ends, so no sliver pokes past the outline), with square caps only at the shape's real ends;
  polygons are split at the cuts and keep their mitred corners; dotted lines carry their dash phase across the gap.
  Degree 6/8 nodes, frame nodes and T-junctions are not woven and so never cut. A selected under line is drawn whole in
  the selection highlight (you see what you picked).
- AR readability (P5): inner and outer lines of a pair are 2 mm apart at the least (the inset floor) and turning inner
  lines on gives at least 10 mm bands. At about 90 cm on a tilt the far side's pairs can still merge into one grey band:
  the shader keeps every stroke at least 1.5 px wide, so no stroke width or colour change can separate them there. A
  per-frame fade of the inner lines with distance (inner runs on their own mesh with a distance uniform) is the real fix
  and is deferred; a level of detail chosen only when the mesh is rebuilt would go stale as the user walks.
- Structure cut, wider than an edge: when the band is wider than the under half-edge is long and the line continues in a
  separate input that only touches it end to end, only the first input is cut. This needs a width at or past the safe
  limit, where the popup is already red.
- Fill (P6, `docs/FILL.md`) on a banded layer uses the outer band outlines and bridges (not the structure or inner
  lines) as region boundaries: in flat Double and in the stripped view the crossing squares are open, so all the band
  strips form one connected region and a tap on any strip fills the whole band lattice (the book's geometry). In
  Interlace each band piece between two crossings is its own region. Stars, octagons and the gaps are separate regions
  in every mode.

## Checks
`tools/enginecheck`: `BandGraphChecks` (model value, merge, welds, degrees, caps, frame, clipping, polygons, determinism,
faces), `BandOutlineChecks` (corner distances, true mitres at 45/30/22.5 degree tips, bevel under 5 degrees, no spikes,
`MaxSafeWidth` of a triangle and of the star, parallel lines at the frame, open crossing square, symmetry, split events,
fuzz), `BandWeaveChecks` (alternation on the star, the tiled board and a pentagram, an odd closed thread, rosette centre,
bridges on the over band only, Flip, inner lines at corners, T-junctions and woven crossings), `BandPerfChecks`.
`Assets/Editor/Build/ToolsRenderCheck.cs` writes `board-bands-*`, `board-bands-stripped-*`, `board-interlace-*`,
`board-interlace-inner-*`, `board-bands-tiled-*` and `board-interlace-tiled-*` PNGs (45 cm, 90 cm and straight down).
