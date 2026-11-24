# Fill

Book p23: "the individual forms ... can be elaborately decorated with patterns and colour". The **Fill** tool colours a
closed region of the active layer.

**Status: switched off for now.** `ToolBar.FillEnabled` is `false`: the tool column has no Fill button (8 buttons) and
the colour bar never shows "No fill", because of the triangulator issue below. Everything else stays: the engine's Fill
tool, `Layer.Fills`, saving and loading, and drawing. Fills saved by an earlier build still load, draw and survive a
re-save. To turn Fill back on, fix the issue below and set `FillEnabled = true` (the column goes back to 9 buttons;
the view tests follow the switch). Next steps: `docs/FUTURE_WORK.md`.

## Using it
1. Tap **Fill** (paint bucket, fourth button in the tool column). A toast says "Fill: tap inside a closed shape".
2. Pick a colour in the colour bar (any swatch, or the colour wheel's colour).
3. Tap inside a closed region: it fills with that colour. Tap a filled region with another colour: its colour is
   replaced. Tapping it again with the same colour changes nothing.
4. To clear a fill, tap **No fill** in the colour bar (it takes the Board button's place while Fill is active), then tap
   the region. Picking any colour, or leaving the Fill tool, switches No fill off again.
5. A tap outside every closed region toasts "Tap inside a closed shape to fill it". With No fill, a tap on an unfilled
   region toasts "Nothing to clear here". A hidden active layer gets the usual "Layer is hidden" toast. A drag does
   nothing, and nothing snaps.

Every fill and clear is one undo step ("Fill", "Clear fill"). Fills are saved with the design (`docs/SAVE.md`); older
files load without fills. Fills draw under the layer's bands and strokes, with the layer's opacity, and only while the
layer is visible.

## What is a region
The regions of a layer are the closed faces formed by what is drawn on it:
- **Plain layer:** its lines, polygon stamps and circles (circles are split into short chords, fine enough that a line
  ending on a circle closes the region; the eight circle divisions are exact chord ends). Dotted lines bound regions too.
- **Banded layer** (`docs/BANDS.md`): the bands' **outer** outlines and interlace bridges, plus the layer's circles. The
  structure line and the inner lines decorate a band and do not split it. In flat Double (and the stripped view) the
  crossing squares are open, so all the band strips form **one connected lattice**: a tap on any strip fills the whole
  lattice (the book's geometry). In Interlace each band piece between two crossings is its own region. Stars, octagons
  and the gaps between them are separate regions in every mode.
- **The board edge** closes a region only together with drawn shapes: the gaps of a pattern that runs to the edge are
  fillable, but the bare board, the area around a floating star, or a board with just one line poking in from the edge is
  "outside any closed shape".
- A floating line inside a region does not split it (and is drawn on top of the fill).

## Fills follow edits (seeds)
A fill is stored as a **seed**: the point you tapped and the colour (`Layer.Fills`, `FillSeed`). After every edit the
region containing the point is found again, so a fill follows its shapes as you draw, undo or change band settings.
Consequences, by design:
- A line drawn across a filled region splits it; only the half holding the seed stays filled.
- Move, Scale and Duplicate move shapes, not seeds: moving a filled square away leaves the fill's seed behind, where it
  resolves to whatever region (or nothing) is now there. A seed outside every region is kept (an Undo or a new shape can
  bring the fill back) but draws nothing.
- When two seeds end up in one region, the later one wins (the region is drawn once).

## How it works
- `Engine/Geometry/FillRegions.cs`: builds the boundaries above, planarizes them inside the board frame
  (`PlanarGraph`), and classifies faces: a region is a bounded face (area above 1e-12) with at least one shape edge whose
  other side is a different face (so spurs and trees walked both ways don't count). `Locate(point)` returns the
  smallest face containing the point if it is a region. Holes are the outer boundaries of components inside a region.
- `Engine/Geometry/Triangulator.cs`: turns a region (outer loop + holes, as walked from the half-edges, so possibly with
  spurs and pinches) into triangles: spur removal, pinch splitting, hole bridging, ear clipping. The checks assert that
  the triangles' area equals the region's area exactly on every fixture.
- `Engine/EditorSession.Fill.cs`: the tool (`ToolId.Fill`), `FillErase` / `SetFillErase` (No fill). Fill changes raise
  `BoardChanged(Fills)`, a flag of its own, so drawing caches for shapes and band topology are not rebuilt.
- `BoardRender/FillCache.cs` + `BoardView`: per layer with fills, the regions and the triangles of each filled region,
  drawn first in the layer's part of the Shapes mesh (one mesh, so a lower layer's fill never covers a higher layer's
  strokes). Shape, fill, undo and reload changes rebuild at once; layer-setting changes (band width drags) rebuild at most
  every 150 ms, so the previous fill is drawn during a drag and catches up right after it.
- Cost: a banded 8-pointed star with two circles and two fills rebuilds its fills in about 10 ms in the editor; that is
  why width drags defer it.

## Known issue: holes that share their rightmost x

The triangulator (`Engine/Geometry/Triangulator.cs`) can produce **overlapping triangles** when two or more holes of a
region have the same rightmost x. The fill then paints more than the region: parts of the holes are covered.

- **Repro**: a square from (0.1, 0.1) to (0.9, 0.9) with two stacked square holes, (0.15, 0.15)-(0.21, 0.21) and
  (0.15, 0.30)-(0.21, 0.36). The triangles add up to 0.7003 against the region's 0.6328. Grids of 2x2, 3x3 and 4x4
  holes fail the same way. A realistic case: four stars in a 2x2 grid inside a square; filling the background gives
  triangles totalling 0.947 against 0.599 (39% of the star interiors are painted).
- **Suspected cause**: `Triangulator.Bridge`. The ray from a hole's rightmost vertex hits a vertical outer edge whose
  two ends share the same x, and the tie picks the far corner. Several holes then bridge to the same outer vertex;
  later holes are spliced into the wrong copy of that duplicated vertex, the bridges cross, and ear clipping makes
  overlapping triangles. The existing checks pass only because their holes have distinct x.
- **Suggested fix**: the standard Eberly hole bridging: from the ray's hit point take the candidate vertex, check the
  reflex vertices inside the triangle (hole vertex, hit point, candidate) and keep the one with the smallest angle to
  the ray, and when the chosen vertex appears more than once in the polygon (an earlier bridge), pick the copy whose
  wedge contains the bridge direction. A simpler alternative is to bridge to the hit point itself (insert it into the
  outer loop). Then add a fuzz check (random non-overlapping holes in random polygons: the triangles' area equals the
  region's area and no two triangles overlap) and bring in the tester's `VerifierFillChecks` and `VerifierFillViewTests`.

## Checks
`tools/enginecheck/FillChecks.cs` (regions, frame rule, star and tiled faces, holes, circles closed by lines, band lattice
vs interlace pieces, triangulation area, the tool: fill, replace, no-op, clear, toasts, undo, seeds after edits,
DeepClone). EditMode `FillViewTests` (Fill button, No fill, fills under strokes, hidden layers, width-drag cost),
`DesignSerializerTests.FillsRoundTripAndOldFilesHaveNone`.
