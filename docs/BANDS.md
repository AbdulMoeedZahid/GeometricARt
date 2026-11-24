# Bands (double lines), stripped view and interlace

Book pages 22-23 ("Design Tips"): each structure line of a pattern is encased in a band of equal width. The band edges
meet in sharp mitres at corners, and at a crossing each band's edges stop at the other band's edge, so the crossing square
is open. With the structure line removed the same outlines are the "stripped" view. In an interlace one band goes over and
the other under at each crossing, alternating along every band, and an optional second pair of lines runs inside each band.

Bands are a **per-layer, non-destructive setting** (`LayerBands`, `Contracts/Model/Bands.cs`). A banded layer draws its
lines and polygon edges as bands and recomputes them after every edit, Undo or Redo. Circles are never banded.

Status: the model type and the geometry engine are done (P2). The layer field, save format, rendering, layer popup and
toolbar button come in P4; interlace and inner-line wiring in P5.

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
3. **Collinear merge**: segments collinear within 1e-5 that overlap or touch become one segment between the extreme
   original endpoints (bit-exact). Tap-to-connect chains (A-B + B-C) and lines drawn over lines collapse to one edge. One
   more near-junction pass then re-attaches ends to the merged segments.
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
  are flagged). `SelfIntersections` counts proper crossings inside each face's inset polygon, which also catches a reflex
  corner running into a non-adjacent side (split events). The layer popup will show amber near `MaxSafeWidth` and red
  when either count is above 0.
- **Runs**: walking each face cycle, consecutive drawn offsets join into one polyline with mitred joins; the frame ends a
  run, so bands end flush with the board edge; a face that touches no frame is one closed run. This alone is the flat
  Double look with open crossing squares, and without the structure line the stripped look.
- **Interlace**: at each woven crossing the over band's two **bridges** join the corners on each side of it, which breaks
  the under band at the over band's edges. Bridges are separate segments.
- **Inner lines** at `h_in = h - max(0.2 width, 1.5 mm)`, drawn only when `h_in >= core half width + 1 mm` (turning them
  on will raise the width to at least 8 mm). Per wedge:
  - between two sides of the same band (a thread turns there: a degree-2 corner or a tip resting on a line), at a free
    end, at a near-straight pass-through (the bar of a T) and at a bevel: they **join** like the outer lines, at h_in;
  - at every other wedge (junctions, crossings, the frame) each inner line **ends on the neighbouring band's outer
    edge** (on the frame itself for the frame). So at a T the bar's inner line stops at the stem's outer edges and the
    stem's inner lines stop at the bar's outer edge, and in flat Double both bands' inner lines stop at the crossing;
  - at a woven crossing the over band's inner lines are bridged straight through (`InnerBridges`), and the under band's
    inner lines end on the over band's outer edge (book p23 bottom).

Budget: 500 tile-like segments planarize and outline in about 10-15 ms on the desktop (check limit 50 ms); the book
fixtures (8 and 32 lines) take under 0.5 ms.

- **Inside the board**: after the safety counts every corner is clamped to [0,1]^2. A corner on a frame line stays on
  it; a tip spike or cap near the frame is cut at the board edge.

Known limits (v1):
- A kinked "crossing" (two bent bands meeting at one point, pairing i with i+2) gets bridges between the corners next to
  the over band, a chord rather than the band's own mitre.
- `SelfIntersections` is per face cycle (a sweep over x-sorted segments); a floating component's band running into another component is not counted.
- With "Show structure line" on, the under band's structure line would cross the over band at a woven crossing; how
  P5 handles it (Interlace hides the structure line, or the structure is cut there) is decided in P5.

## Checks
`tools/enginecheck`: `BandGraphChecks` (model value, merge, welds, degrees, caps, frame, clipping, polygons, determinism,
faces), `BandOutlineChecks` (corner distances, true mitres at 45/30/22.5 degree tips, bevel under 5 degrees, no spikes,
`MaxSafeWidth` of a triangle and of the star, parallel lines at the frame, open crossing square, symmetry, split events,
fuzz), `BandWeaveChecks` (alternation on the star, the tiled board and a pentagram, an odd closed thread, rosette centre,
bridges on the over band only, Flip, inner lines at corners, T-junctions and woven crossings), `BandPerfChecks`.
`Assets/Editor/Build/ToolsRenderCheck.cs` writes `board-bands-*`, `board-bands-stripped-*`, `board-interlace-*`,
`board-interlace-inner-*`, `board-bands-tiled-*` and `board-interlace-tiled-*` PNGs (45 cm, 90 cm and straight down).
