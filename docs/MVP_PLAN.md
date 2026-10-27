# Geometric ARt - MVP Plan (4 people, 4-week hard cap, parallel from Day 1)

Sources: `AR_Geometric_Pattern_Studio_Proposal-1.pdf` (sketches p3-6), `docs/PROPOSAL_SUMMARY.md`, page renders `/tmp/pdfpages/h03..h06.png` (re-checked directly).
Status: working document. **Timeline: 3 weeks target + week 4 buffer/polish/user test = 4 weeks hard cap.** Day 0 (2 hours) locks section 2.

Conventions: **A / B / C / D** = the four people (A = AR + rendering + build, B = engine, C = UI + Scale tool, D = input + lesson + save/load + Rotate/Erase/Duplicate tools). "Sketch" = the proposal PDF. Fidelity levels used throughout: **F** = fully working, **M** = visible and minimally working, **S** = visible stub (drawn exactly, behaviour deferred). `D-xx` = assumption/decision not in the sketches (section 6.2).

---

## 1. MVP definition, cuts, and sketch fidelity

### 1.1 MVP in one paragraph

An iPhone-only Unity (AR Foundation/ARKit) app where the user scans a table, places **one anchored square art board**, and draws with **on-screen touch and the sketched quick menubar** (Place Snap Point, click, Line between Snap points, Undo, Redo, Tools), uses the **Tools 3x3 grid**, organizes content with the **layer widget**, sets the cell count with the **"2x2" pill**, and follows **one guided Eight-Pointed Star lesson** that produces the sketch-3 figure and ends with the sketch-4 2x2 tiling. Every element drawn on pages 3-6 is visible in the UI. Input is **Screen only (touch + buttons)**; the "Hand Gestures / Screen" toggle from sketch 1 is drawn but disabled, with Hand Gestures greyed out and Screen the only active mode (D-02). Target: >= 30 FPS on a physical iPhone, one 2-minute measurement.

### 1.2 What is cut or reduced, and why

| Cut / reduced | Why | Visible in UI? |
|---|---|---|
| **Hand Gestures input mode**: not part of the MVP; the toggle is drawn and disabled (D-02) | Scope for a 4-week build; touch and buttons cover every command | Yes: toggle visible, Hand Gestures greyed out |
| Rotate: tap rotates the selection by +45 degrees (no drag) | Cheap, still demonstrates the tool | Yes (M) |
| Erase: tap a shape or snap point to delete it (no drag-erase) | Cheap | Yes (M) |
| Duplicate: tap copies the selection to the next free cell, snapped. "Fill grid" and linked repeat cut | Sketch 4 only needs copy + Select/Move + snap | Yes (M) |
| Scale: drag to scale about the selection center with snap-fit to the cell side. Fallback if it slips: tap = fit-to-cell | Needed for sketch 4 | Yes (F, fallback allowed) |
| Stamps: triangle/square/hexagon placed at a default size by tap (no drag-to-size) | Cheap | Yes (F for placement) |
| Layer widget: tap = select, double-tap = hide/show, "+" and "-" full; Arrange = long-press a segment moves it up one slot (wraps to bottom) instead of drag-reorder | Drag-reorder in UGUI costs a day | Yes (M for Arrange) |
| Wall (vertical plane): cut. Table only. Sketch 1's note "Mounted on wall or placed on table" is an annotation, not UI | Plane robustness is enough work | Annotation only |
| Anchoring: one ARAnchor + "Re-place board"; no relocalization work | Time | n/a |
| Snap intersections: P1 (only if time). Snap set = user points, endpoints, centers, circle divisions (8), cell corners/edge-midpoints/centers | Star construction needs none of the intersections | n/a |
| Save/load: single slot JSON (Save and Load in Settings). Autosave, named designs, schema migration cut | Needed only for "save/reload" in the demo | Settings only |
| Telemetry/CSV logger, PlayMode tests, CI: cut. Replaced by an FPS overlay, a smoke checklist, and a paper observer sheet | Overhead | Settings: "Show FPS" |
| Lesson: text coach card with step validation; no ghost overlays, no "Show me" | Overlays cost a day for small value | Coach card only during lesson (D-18) |
| Radial 4/8 helper, connectors, six-fold lesson, export/share, linked "source cell updates copies" | Not in required list / stretch / out of scope | No |
| Evaluation: informal 3 users in week 4 (observer sheet + FPS overlay) | Time | n/a |

Full engine behaviour kept: Line (click-click and drag), Circle, snapping, undo/redo (all commands), layers (add/delete bottom/hide/select), grid pill 1x1..4x4, Select/Move with snap-while-drag, dotted/solid line toggle, palette + width in Settings.

### 1.3 Interaction model (the one definition everybody codes against)

* There is a **cursor** in board space. In **Screen** mode the cursor is the last touch position on the board; touch down/move/up on the board are pointer Down/Move/Up with no finger offset (D-03). The six buttons act at the cursor.
* **Place Snap Point**: adds a persistent snap point at the (snapped) cursor on the active layer. If the cursor is on an existing snap point or a faint candidate (circle-division, cell point), that point is promoted/re-selected as "latest".
* **click** (double-ring button, "click" drawn inside in sketch 1): commits the active tool's action at the cursor (pointer Down+Up). With Line: click, move, click draws a line; dragging also draws one.
* **Line b/w Snap points**: draws a line between the latest and previous snap point (skips if it exists). **Double tap** toggles solid/dotted for new lines (sketch 3); the glyph shows the current style. Re-selecting an existing point as "latest" lets users close the squares of the lesson (D-23).
* **Tools** enters Tools mode (sketches 2/4): the bottom area becomes the Tools panel; header, layer widget, pill, toggle, and 6 buttons are hidden as drawn. Tapping a tool selects it and keeps the panel open; tapping the "Tools" title bar returns to the quick menubar (D-05). Undo/Redo are reachable only from the quick menubar, as drawn.
* **Snap** cell toggles magnetic snapping on/off (default on) (D-06).

### 1.4 Sketch fidelity checklist

Rule: every row is a must-have; any UI element not listed here must be in section 6.2 and may appear only where named there (Settings sheet, Stamps popover, lesson coach card, placement coach text, toasts). Layout reference for C (fractions of screen height, from sketches 1/2/4): header 0-7%; AR view behind everything; quick menubar zone bottom ~35% (toggle row ~70%, two rows of circle buttons ~77-96%); layer widget on the left edge and "2x2" pill on the right edge just above the toggle row (~62-66%); Tools panel bottom ~30-35% (title bar + 3x3 grid).

**Sketch 1 - Home screen (p3)** "User opens the application and mounts the board, sees the quick menubar"

| ID | Drawn element | Level | Owner | Task / acceptance |
|---|---|---|---|---|
| S1-01 | "Geometric ARt" title, top-left | F | C | C-1. Text exactly `Geometric ARt`. |
| S1-02 | Round gear "Settings" icon, top-right | F icon, M content | C (D hooks) | C-1, C-7. Opens Settings sheet (D-01). |
| S1-03 | AR view with perspective Canvas/Board: outer quad + inner square with dashed vertical/horizontal dividers; annotation "Mounted on wall or placed on table" | F (table), wall cut | A | A-2, A-3. Anchored, correct perspective, dividers = `GridN`, thin outer frame (D-04). |
| S1-04 | Layer widget left: "+" Add Layers, stack icon Arrange Layers, "-" Delete Bottom Layer | + F, select F, hide F, "-" F, Arrange M | C (UI), B (ops) | C-3, B-3. "+" adds a top layer and selects it; "-" deletes the bottom layer with content, undoable, never the last layer (D-10). |
| S1-05 | "2x2" pill, "Specify no of cells on canvas" | F | C, B, A | C-3, B-3, A-3. Tap cycles 1x1,2x2,3x3,4x4 (D-07); pill text and dividers update. |
| S1-06 | "Hand Gestures" [toggle] "Screen" | S (visible, disabled) | C | C-2. Labels exact; "Hand Gestures" greyed out, knob on the Screen side, toggle does not respond to taps (D-02). All commands work through Screen mode. |
| S1-07 | Dot button "Place Snap Point" | F | C, B | C-2, B-2. |
| S1-08 | Double-ring "click" button | F | C, B | C-2, B-2. |
| S1-09 | Line-with-two-dots "Line b/w Snap points" | F | C, B | C-2, B-2. |
| S1-10 | Left arrow (Undo) | F | C, B | C-2, B-2. Disabled when empty. |
| S1-11 | Right arrow (Redo) | F | C, B | C-2, B-2. |
| S1-12 | "Tools" button | F | C | C-2. Enters Tools mode. |
| S1-13 | Board "mounted" by the user at launch | F | A | A-2. Scanning text -> reticle -> tap to place. |

**Sketch 2 - Tools Screen (p4)** "User checks out the tools available"

| ID | Drawn element | Level | Owner | Task / acceptance |
|---|---|---|---|---|
| S2-01 | Panel with title bar "Tools" | F | C | C-2. Title bar tap returns to quick menubar (D-05). |
| S2-02 | 3x3 grid: Line, Select/Move, Rotate / Circle, Snap, Scale / Stamps, Erase, Duplicate | Line F, Select/Move F, Circle F, Snap F, Stamps F, Scale F, Rotate M, Erase M, Duplicate M | C (UI); B (Line, Circle, Select/Move, Snap); C (Scale); D (Stamps, Rotate, Erase, Duplicate) | C-2; B-2/B-4; C-5; D-2. Labels exact ("Select/Move" from sketch 2; "Circle" capitalized). Each cell highlights when active. |
| S2-03 | "Has options" on Stamps | F | C (popover), D (Stamps tool) | C-4, D-2. Popover Triangle/Square/Hexagon (D-11). |
| S2-04 | Board in the upper ~65-70%, panel does not cover it, other HUD hidden | F | A, C | A-2, C-2. |
| S2-05 | Long diagonal double line in the viewport | - | - | Read as the real table edge, not UI (D-12). |

**Sketch 3 - Preview / creating a Pattern (p5)** "User makes a motif, uses layers to make it easy to delete unnecessary lines."

| ID | Drawn element | Level | Owner | Task / acceptance |
|---|---|---|---|---|
| S3-01 | Board with corner dots, dashed diagonals/diameters, center dot, circle, 8 dots, two solid squares (8-pointed star) | F | D (lesson), B (geometry/snap), A (render) | D-3, B-5, A-3. Following lesson steps 1-5 yields this figure; golden test vs `GoldenBoards.StarOnFullBoard`. Board corners = derived snap candidates. |
| S3-02 | Layer bar with 3 segments labelled "8 pointed Star made w squares" / "Circle" / "Dotted lines" (top to bottom), "+" above, "-" below | F | C (widget), D (lesson creates layers) | C-3, D-3. "-" twice reveals the star. |
| S3-03 | "Double tap to switch to dotted line" | F | C, B | C-2, B-2, A-3. |
| S3-04 | "Undo" / "Redo" captions over the arrows | F | C | C-2. |
| S3-05 | 2x2 pill and Hand/Screen row; label "Hand" here vs "Hand Gestures" in sketch 1 | F | C | Use "Hand Gestures" (sketch 1 canonical). |
| S3-06 | Header not drawn in this sketch | - | C | Header always shown in quick-menubar mode (D-13). |

**Sketch 4 - Scaling & tiling (p6)** "User switches to tools mode, scales and tiles the pattern on a 2*2 grid using duplicate, select and then dragging the shape. Snap points are essential to snap the tiles into place."

| ID | Drawn element | Level | Owner | Task / acceptance |
|---|---|---|---|---|
| S4-01 | 2x2 star tiles in heavy strokes, dots at corners, edge midpoints, centers | F | A (render), B (candidates) | A-3, B-2. Thick preset; derived snap dots visible in Tools mode when Snap on. |
| S4-02 | Tools panel only | F | C | As S2-01..03. |
| S4-03 | Workflow Scale -> Duplicate -> Select -> drag -> snap = 2x2 tiling | Scale F, Duplicate M, Select F | C (Scale), D (Duplicate), B (Select/snap) | C-5, D-2, B-4. From the finished star on the full board a user reaches 4 copies in 4 cells with only those tools; diamond tips touch at cell-edge midpoints within 0.00001 x cell side (golden test `Tiled2x2_SeamsMatch`). |
| S4-04 | Undo/Redo, layers, pill, toggle not drawn in Tools mode | F | C | Hidden in Tools mode. |

---

## 2. Architecture and shared contracts

### 2.1 Principles

1. **Pure C# core.** Geometry, snapping, commands, tools, serialization live in `noEngineReferences` assemblies, runnable in EditMode on any machine without AR.
2. **Board space:** origin bottom-left as seen by the user, +x right, +y up, board = [0,1] x [0,1], N x N cells, cell side = 1/N. Doubles.
3. **One writer:** the board is mutated only by `ICommand`s executed through the engine. Others call `IEditorSession` or read.
4. **Four seams only** (everything else is internal to a module): `IEditorSession` (engine <-> UI/input/lesson/render), `IBoardSpace` (AR <-> input/render), `ILessonController`, `IDesignIO`. Tool authors additionally use `IToolHost`/`IDrawingTool`/`ICommand`.
5. **Consumer owns the mock; producer owns the real thing.**

Dropped from the earlier longer plan to keep Day 0 at 2 hours: event bus, input interfaces (touch is internal to D), snap-service interface (internal to B; exposed via `IToolHost.Snap` and `IEditorSession.SnapCandidates`), command-stack interface, serializer + store interfaces (merged into `IDesignIO`), placement interface (merged into `IBoardSpace`), overlay API.

### 2.2 Unity project layout (single project at repo root; owner per folder)

```
Assets/
  GeoART/
    Contracts/         [COUNCIL, frozen after Day 0]  asmdef GeoART.Contracts (noEngineReferences)
      Math/ Model/ Tools/ Session/ Board/ Lesson/ Persistence/ Fixtures/
    Mocks/             asmdef GeoART.Mocks
      ForUI/ [C]  MockEditorSession, MockLesson, MockDesignIO
      ForInput/ [D]  LoggingEditorSession, FakeToolHost
      ForBoard/ [A]  StubEditorSession, FlatBoardSpace
    Core/              [B] asmdef GeoART.Core (noEngineReferences); file-level ownership in Tools/:
      Geometry/ Snap/ Commands/ Session/ Layers/ Grid/                          [B]
      Tools/ LineTool, CircleTool, SelectMoveTool, SnapToggle                    [B]
      Tools/ ScaleTool                                                          [C]
      Tools/ StampsTool, RotateTool, EraseTool, DuplicateTool                   [D]
      (tools self-register with [Tool(ToolId.X)]; B's ToolRegistry scans them: nobody edits a shared list)
    ARBoard/           [A] asmdef GeoART.ARBoard   (AR session, planes, placement, anchor, ARBoardSpace)
    BoardRender/       [A] asmdef GeoART.BoardRender (BoardView)
    UI/                [C] asmdef GeoART.UI (UGUI + TextMeshPro)
    Input/             [D] asmdef GeoART.Input (Touch/)
    Lesson/            [D] asmdef GeoART.Lesson
    Persistence/       [D] asmdef GeoART.Persistence
    App/               [A] composition root, AppConfig, FPS overlay
  Prefabs/ Board/[A]  UI/[C]  Input/[D]  Engine/[B]  Lesson/[D]
  Scenes/ Main.unity [A]  Flat.unity [C]  Sandbox/Sandbox_<A|B|C|D>_*.unity (each owner-private)
  Editor/Build/        [A]
  Tests/EditMode/<Module>/  [module owner]
ProjectSettings/ Packages/ [A]
docs/
```

A pins Unity 6 LTS (or 2022.3 LTS) + AR Foundation + Apple ARKit XR Plugin + URP with AR Background Renderer Feature + TextMeshPro + Newtonsoft Json + Test Framework **before Day 0** (see 2.9) and nobody changes render pipeline later.

### 2.3 Wiring

Each module prefab carries an `Installer` MonoBehaviour (`[DefaultExecutionOrder(-100)]`) registering its service in `ServiceHub` in `Awake`; consumers resolve in `Start`. `Main.unity` (A) and `Flat.unity` (C) only instantiate the prefabs `EngineHost` (B), `BoardRoot` (A), `UIRoot` (C), `InputRoot` (D), `LessonHost` (D). Sandboxes instantiate their own real prefab plus mocks.

### 2.4 Contract code (Day 0 deliverable; namespace `GeoART.Contracts`)

```csharp
// ---------- Math / Model ----------
public readonly struct Vec2 : IEquatable<Vec2> {
    public readonly double X, Y;
    public Vec2(double x, double y) { X = x; Y = y; }
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public bool Equals(Vec2 o) => X == o.X && Y == o.Y;
}
/// Rotation about Pivot, uniform scale, then translation (circles stay circles).
public readonly struct Similarity2 { public readonly Vec2 Pivot, Translation; public readonly double RotationRad, Scale; public Vec2 Apply(Vec2 p) { /* ... */ } }
public readonly struct RgbaColor { public readonly byte R, G, B, A; }
public readonly struct EntityId : IEquatable<EntityId> { public readonly int Value; public static readonly EntityId None = new(0); public EntityId(int v) { Value = v; } public bool Equals(EntityId o) => Value == o.Value; }

public enum LineStyle { Solid, Dotted }
public enum LineWidth { Thin, Medium, Thick }
public enum StampKind { Triangle = 3, Square = 4, Hexagon = 6 }
public readonly struct StrokeStyle { public readonly RgbaColor Color; public readonly LineWidth Width; public readonly LineStyle Dash; }

public enum ShapeKind { Line, Circle, Polygon }
public abstract class Shape { public EntityId Id; public StrokeStyle Style; public abstract ShapeKind Kind { get; } public abstract Shape Clone(); }
public sealed class LineShape    : Shape { public Vec2 A, B; }
public sealed class CircleShape  : Shape { public Vec2 Center; public double Radius; }
public sealed class PolygonShape : Shape { public Vec2 Center; public int Sides; public double Radius, RotationRad; }   // stamps
public sealed class SnapPoint { public EntityId Id; public Vec2 Position; }                                            // user-placed

public sealed class Layer {
    public EntityId Id; public string Name; public bool Visible = true;
    public readonly List<Shape> Shapes = new();            // first = back
    public readonly List<SnapPoint> SnapPoints = new();    // hidden/deleted with the layer
}
public sealed class Board {
    public const int MaxGridN = 4;
    public int GridN = 2; public double CellSide => 1.0 / GridN;
    public readonly List<Layer> Layers = new();            // index 0 = BOTTOM
    public EntityId ActiveLayerId; public int NextId = 1; public EntityId NewId() => new(NextId++);
}
[Flags] public enum BoardChangeKind { None = 0, Shapes = 1, Layers = 2, SnapPoints = 4, Grid = 8, Reset = 16 }

// ---------- Snap result types ----------
public enum SnapKind { None, UserPoint, Endpoint, Center, CircleDivision, CellCorner, CellEdgeMid, CellCenter }
public readonly struct SnapCandidate { public readonly Vec2 Position; public readonly SnapKind Kind; }
public readonly struct SnapResult    { public readonly bool Snapped; public readonly Vec2 Position; public readonly SnapKind Kind; }

// ---------- Tools (for anyone writing a tool: B, C, D) ----------
public enum ToolId { None, Line, SelectMove, Rotate, Circle, Snap, Scale, Stamps, Erase, Duplicate }
public enum PointerPhase { Down, Move, Up, Cancel }        // Cancel = interrupted: abort unfinished shape
public readonly struct PointerEvent { public readonly PointerPhase Phase; public readonly Vec2 BoardPos; public readonly bool OnBoard; public readonly double Time; }
public interface ICommand { string Label { get; } BoardChangeKind Affects { get; } void Do(Board b); void Undo(Board b); }   // Undo exactly inverts Do
public sealed class Selection { public readonly HashSet<EntityId> Ids = new(); }
public interface IToolHost {
    Board Board { get; } Selection Selection { get; }
    StrokeStyle Style { get; } StampKind Stamp { get; }
    void Execute(ICommand c);                                 // pushes on undo stack
    SnapResult Snap(Vec2 raw, IReadOnlyCollection<EntityId> excludeShapes = null);   // honours Enabled + visible layers
    void SetPreview(IReadOnlyList<Shape> ghosts);             // transient ghosts, never in the model
    void Hint(string text);
}
public interface IDrawingTool { ToolId Id { get; } void Activate(IToolHost h); void Deactivate(); void OnPointer(in PointerEvent e); }   // Deactivate cancels unfinished work

// ---------- THE central seam: engine facade ----------
public interface IEditorSession {
    Board Board { get; }                                      // read-only by convention
    ToolId ActiveTool { get; } StampKind ActiveStamp { get; }
    bool SnapEnabled { get; } bool DottedLine { get; }
    StrokeStyle Style { get; set; }                           // palette color + width (Settings)
    Selection Selection { get; }
    Vec2? Cursor { get; }
    IReadOnlyList<Shape> Preview { get; }                     // tool ghosts
    IReadOnlyList<SnapCandidate> SnapCandidates { get; }      // user + derived points (for dots)
    string ToolHint { get; }
    // pointer path (called by touch input)
    void SetCursor(Vec2 boardPos, bool onBoard); void Pointer(in PointerEvent e);
    // quick menubar (sketches 1/3)
    void PlaceSnapPoint(); void ClickAtCursor(); void LineBetweenSnapPoints(); void ToggleDottedLine();
    // tools grid (sketches 2/4)
    void SetTool(ToolId t); void SetStamp(StampKind k); void SetSnapEnabled(bool on);
    // layers + grid
    EntityId AddLayer(); void SelectLayer(EntityId id); void MoveLayer(EntityId id, int newIndex);
    void DeleteBottomLayer(); void SetLayerVisible(EntityId id, bool visible); void SetGrid(int n);
    // history + lifecycle
    bool CanUndo { get; } bool CanRedo { get; } void Undo(); void Redo();
    void ClearBoard(); void LoadBoard(Board b);               // LoadBoard resets history
    event Action<BoardChangeKind> BoardChanged;               // model changed
    event Action StateChanged;                                // tool/selection/style/undo availability/hint
    event Action PreviewChanged;                              // ghosts/cursor (high frequency, renderer only)
    event Action<string> Toast;                               // UI shows toasts
}

// ---------- Board <-> screen/world + placement (A) ----------
public enum PlacementState { Unsupported, Scanning, PlaneFound, Placed, TrackingLost }
public interface IBoardSpace {
    PlacementState State { get; } event Action<PlacementState> StateChanged;
    bool IsPlaced { get; }
    void PlaceAtReticle(); void Reset();                      // Reset = "Re-place board" (Settings)
    bool TryScreenToBoard(Vec2 screenPx, out Vec2 board);     // screen px as Vec2 keeps Contracts engine-free
    double PxToBoardUnits(double px);                         // snap radius / hit tolerance
}

// ---------- Lesson (D) ----------
public sealed class LessonState { public string LessonId; public int StepIndex, StepCount; public string Title, Instruction, Hint; public bool StepComplete, Finished; }
public interface ILessonController {
    bool Active { get; } LessonState State { get; }
    void Start(string lessonId); void Stop(); void Next(); void Back();   // Start("eight-point-star") loads the lesson board (3 layers, GridN=2)
    event Action<LessonState> StateChanged;
}

// ---------- Persistence (D): single slot ----------
public interface IDesignIO { void Save(Board b); bool TryLoad(out Board b); bool HasSaved { get; } }

public static class ServiceHub { public static void Register<T>(T s); public static T Resolve<T>(); public static bool TryResolve<T>(out T s); }
public static class Palette { public static readonly RgbaColor[] Default = null; /* 6 colours, D-08 */ }
```

### 2.5 Fixtures (Day 0, hand-coded, small)

`Contracts/Fixtures/GoldenBoards.cs`: `Empty()`, `StarOnFullBoard()` (circle at (0.5,0.5) r=0.35, 8 snap points, dotted diameters, two solid squares; sketch 3), `Tiled2x2()` (four scaled stars, circle radius 0.25 per cell, diamond tips on cell-edge midpoints; sketch 4). A renders them on Day 1, D round-trips them, B uses them in golden tests, C uses them in mocks.

### 2.6 Who uses what before the real thing exists

| Consumer | Needs | Stand-in (owner) | Real provider |
|---|---|---|---|
| A (`BoardView`) | Board + preview + candidates | `StubEditorSession` + `GoldenBoards` (A) | B `EditorSession` |
| C (UI) | `IEditorSession`, `ILessonController`, `IBoardSpace`, `IDesignIO` | `MockEditorSession` (flips state, records calls) + small mocks (C) | B, D, A |
| D (touch, lesson) | `IBoardSpace`, `IEditorSession` | `FlatBoardSpace` (A, Day 0) + `LoggingEditorSession`; from W1 Wed real `EditorSession` classes | A, B |
| D/C (tools) | `IToolHost` | `FakeToolHost` in `Mocks/ForInput` (D; C copies) | B `EditorSession` implements it |
| B | `PxToBoardUnits` | constant | A |

### 2.7 Contract rules

Frozen at end of Day 0 (tag `contracts-v0`). Afterwards additive only, via PR labelled `contract`, approved by one person per consuming module, announced in chat. Renames/removals only at an integration checkpoint. No logic in Contracts beyond trivial struct helpers and `ServiceHub`.

### 2.8 Test seams

B: EditMode tests only. A: `Sandbox_A_Flat` (Editor, ortho) and `Sandbox_A_AR` (device). C: `Sandbox_C_UI` (Editor, Game view 390x844). D: `Sandbox_D_Input` (Editor, `FlatBoardSpace`, logging session), EditMode for lesson/save.

### 2.9 Day 0 kickoff (together, ~2 hours)

Pre-Day-0 homework (A, evening before, 1 hour): create the Unity project with pinned version/packages/URP, folder skeleton, asmdefs, `.gitignore`, `.gitattributes`, push; everyone clones and opens it before the call.

| Time | Activity | Output |
|---|---|---|
| 0:00-0:10 | Confirm the cuts (1.2) and `D-xx` decisions (6.2) | Signed off |
| 0:10-1:10 | Write `Contracts` from 2.4 (B+D: model/tools/persistence; A+C: session/board/input/lesson), cross-review, compile | `GeoART.Contracts` compiles |
| 1:10-1:45 | Mocks + fixtures: `StubEditorSession`, `FlatBoardSpace`, `MockEditorSession` skeleton, `GoldenBoards`; five empty prefab shells with Installers; empty `Main`, `Flat`, four sandbox scenes | Tag `contracts-v0`; sandboxes Play |
| 1:45-2:00 | Agree sync times (Wed + Fri), PR rules, who has Mac/iPhone, Apple provisioning plan | Task lists in section 3 |

Hardware: iOS builds need macOS + Xcode + a physical iPhone. A needs it from day 1; D needs a device for the QA smoke runs; B and C work Editor-only on any OS.

---

## 3. Four workstreams

Task IDs are referenced from the fidelity tables. Days: W1 Mon = day 1.

### 3.1 A - AR foundation, board rendering, build

**Scope:** signed iPhone build early; AR session, plane detection, reticle, tap-to-place, ARAnchor; `IBoardSpace` (ARBoardSpace + FlatBoardSpace); visual board (outer frame, dashed cell dividers); `BoardView` rendering of the model (solid/dotted lines, circles, polygons, thin/medium/thick, snap dots, candidate dots, selection highlight, preview ghosts); FPS overlay and one 2-minute run; owns `Main.unity`, `ProjectSettings`, `Packages`, `App/`.
**Owns:** `ARBoard/ BoardRender/ App/ Editor/Build/ Prefabs/Board/ Scenes/Main.unity Scenes/Sandbox/Sandbox_A_* Mocks/ForBoard/ ProjectSettings/ Packages/`.

| # | Task | When | Acceptance |
|---|---|---|---|
| A-0 | Project, packages, URP, `FlatBoardSpace` (Day 0) | pre-Day-0 / Day 0 | everyone opens it |
| A-1 | **Signed "hello AR" build on iPhone**; env-var Team ID; build menu item; one-page `docs/BUILD_IOS.md` | W1 Mon-Tue | any teammate with a Mac builds from `main`. Free profiles expire in 7 days: weekly re-sign reminder |
| A-2 | Plane detection (horizontal), reticle, coach states, tap-to-place board with outer frame + dashed dividers, `Reset` | W1 Wed-Thu | S1-03, S1-13, S2-04; board stays put when the camera moves |
| A-3 | `BoardView` renders `GoldenBoards` in `Sandbox_A_Flat` and on device (batched meshes per layer, dotted dashes, circles as polylines, polygons, snap dots, widths) + `ARBoardSpace.TryScreenToBoard/PxToBoardUnits` | W1 Thu-Fri | S3-01, S3-03, S4-01 render; **M1 device build shows placed board with golden star** |
| A-4 | `BoardView` driven by real session: preview ghosts, candidates (faint), user dots, selection highlight, grid dividers follow `GridN` | W2 Mon-Tue | live drawing visible in Flat (Int 3) |
| A-5 | `Main.unity` composes prefabs; touch on device works through `ARBoardSpace`; tracking-lost state (`PlacementState.TrackingLost`) | W2 Wed-Fri | **M2 device build: real engine on AR board** |
| A-6 | Performance: dirty-mesh rebuild on `BoardChanged`, FPS overlay, 2-minute median run on 2x2 star + stress board, fix hot spots | W3 Mon-Wed | median >= 30 FPS (one run recorded) |
| A-7 | RC device build, signing refresh, final build | W3 Thu - W4 | builds per integration tag |

**Isolated testing:** `Sandbox_A_Flat` (ortho + `FlatBoardSpace` + `StubEditorSession` + golden boards) and `Sandbox_A_AR` (device). No B/C/D code.
**Done when:** S1-03, S1-13, S2-04, S3-01/03/04 and S4-01 rendering pass; 2-minute median >= 30 FPS; anchored board survives a 3-surface walkthrough; signed build reproducible from `main`.

### 3.2 B - Core engine (plain C#)

**Scope:** model behaviour, geometry helpers, command stack, `SnapService`, `EditorSession` (implements `IEditorSession` and `IToolHost`), `ToolRegistry`, Line, Circle, Select/Move (snap-while-drag), Snap toggle, layers ops, grid; golden seam tests; geometry predicates for the lesson; then review and bug burn.
**Owns:** `Core/` (except the C and D tool files), `Prefabs/Engine/`, `Tests/EditMode/Core/`, `Scenes/Sandbox/Sandbox_B_*`, `GoldenBoards.cs` after Day 0.

| # | Task | When | Acceptance |
|---|---|---|---|
| B-1 | Geometry helpers (distance, bounds, `Similarity2` per shape kind, regular polygon vertices, circle-division points); `CommandStack` + commands (Add/Remove shape, Transform, Add/Remove snap point, Add/Remove/Move layer, Visibility, SetGrid, Clear); `ToolRegistry` | W1 Mon-Tue | random do/undo sequence returns the original board |
| B-2 | `SnapService` (user points, endpoints, centers, circle divisions N=8, cell corners/edge-midpoints/centers, visible layers only); `EditorSession`; Line + Circle tools (click-click and drag, minimum length/radius, Cancel leaves model unchanged); PlaceSnapPoint, ClickAtCursor, LineBetweenSnapPoints, ToggleDottedLine, Undo/Redo | W1 Wed-Fri | S1-07..11, S3-03; **usable `EditorSession` ready for Int 3 (W2 Wed) - earlier if possible** |
| B-3 | Layers (add on top, select, move, hide, `DeleteBottomLayer` incl. snap points, keep >= 1), `SetGrid` 1..4 | W1 Fri-W2 Mon | S1-04, S1-05 |
| B-4 | Select/Move (hit-test via `PxToBoardUnits`, drag snaps selection key points to other candidates, excludes self) | W2 Mon-Wed | S2-02, S4-03 snap-while-drag lands tiles exactly |
| B-5 | Golden tests: scripted pointer events build `StarOnFullBoard`; Scale-fit + Duplicate + Select/Move builds `Tiled2x2` with seam error < 1e-5 x cell; `FitToCell`/bounds helper for C and D tools; lesson predicates | W2 Thu-Fri | S3-01, S4-03 |
| B-6 | Review C/D tools, bug burn, engine cap (300 shapes + toast), P1: line/circle intersection snaps | W3 Mon-Wed | no Unity types in Core |
| B-7 | Pair-debug C's Scale tool and D's lesson validators against engine behaviour; device bug fixes; final regression | W3 Wed - W4 | no open engine bugs at M3 |

**Isolated testing:** EditMode only (`Tests/EditMode/Core`): drive `EditorSession` with `PointerEvent` sequences, assert board state.
**Done when:** all tools cancel cleanly; undo/redo exact; golden star and 2x2 seam tests pass; `EngineHost.prefab` registers `IEditorSession`.

### 3.3 C - UI/UX, Scale tool

**Scope:** every drawn UI element (header + gear, quick menubar with 6 buttons, captions, toggle, pill, layer widget, Tools panel 3x3, Stamps popover), tool highlights, Settings sheet, lesson coach card, placement coach text, toasts, `Flat.unity` integration scene; Scale tool.
**Owns:** `UI/`, `Prefabs/UI/`, `Mocks/ForUI/`, `Scenes/Flat.unity`, `Scenes/Sandbox/Sandbox_C_*`, `Core/Tools/ScaleTool.cs`, `Tests/EditMode/UI/`.

| # | Task | When | Acceptance |
|---|---|---|---|
| C-1 | Canvas (reference 390x844, safe area), theme, header "Geometric ARt" + gear | W1 Mon | S1-01, S1-02 |
| C-2 | Quick menubar: 2x3 circle buttons + glyphs, "Undo"/"Redo" captions, toggle, Tools button; Tools panel (title bar + 3x3 grid, exact labels, active highlight); Tools-mode switching (hides HUD); double-tap on line button toggles dotted + glyph | W1 Mon-Wed | S1-06..12, S2-01/02/04, S3-03..05; side-by-side screenshot vs sketches 1 and 2 on `MockEditorSession` (Int 1 / M1) |
| C-3 | Layer widget (vertical segmented bar, "+", "-", names, tap select, double-tap hide, long-press move-up) and "2x2" pill cycling 1x1..4x4 | W1 Thu-Fri | S1-04, S1-05, S3-02 |
| C-4 | Stamps popover (Triangle/Square/Hexagon), Snap cell toggle state, toasts, placement coach text; `Flat.unity` composed with real prefabs | W2 Mon-Tue | S2-03; Int 3 on real engine |
| C-5 | **Scale tool** (drag scale about selection center; snap-fit to cell side when Snap on; fallback tap = fit-to-cell) | W2 Tue-Thu | S4-03; EditMode tests; handles Cancel + undo |
| C-6 | Lesson coach card (title, instruction, hint, Back/Next/Exit) against `MockLessonController`, then the real one | W2 Thu-Fri | D-18 |
| C-7 | Settings sheet: palette (6 swatches), width (Thin/Medium/Thick), Start lesson, Save, Load, Clear (confirm), Re-place board, Show FPS | W2 Fri - W3 Tue | D-01 |
| C-8 | Device fit (safe area, hit targets >= 44 pt), polish, fidelity screenshots for every row in 1.4 | W3 Wed - W4 | all UI rows verified |

**Isolated testing:** `Sandbox_C_UI` + `MockEditorSession` (flips tool/layer/undo state; every button clickable in the Editor). EditMode tests for widget logic. No B/A/D code.
**Done when:** every UI row in 1.4 verified by screenshot; `GeoART.UI` never references `GeoART.Core`; Scale handles Cancel and undo.

### 3.4 D - Touch input, lesson, save/load, simple tools, QA

**Scope:** `TouchInputSource`; Stamps, Rotate, Erase, Duplicate tools; Eight-Pointed Star lesson; JSON single-slot save/load; QA smoke checklist, scripted playthrough test, observer sheet and user-test sessions; demo script.
**Owns:** `Input/ Lesson/ Persistence/ Prefabs/Input/ Prefabs/Lesson/ Mocks/ForInput/ Tests/EditMode/{Input,Lesson,Persistence,Playthrough}/ Scenes/Sandbox/Sandbox_D_*`, `Core/Tools/{Stamps,Rotate,Erase,Duplicate}Tool.cs`, `docs/QA_SMOKE.md`, `docs/OBSERVER_SHEET.md`.

| # | Task | When | Acceptance |
|---|---|---|---|
| D-1 | `TouchInputSource` (single touch -> `SetCursor/Pointer` via `IBoardSpace`; ignore touches over UI via EventSystem; mouse in Editor) | W1 Mon | single-touch drives the cursor and pointer events in `Sandbox_D_Input` |
| D-2 | **Tools:** Rotate (tap = +45 degrees about selection centroid), Erase (tap shape or snap point), Duplicate (tap = copy of selection to next free cell, snapped, one undo step), Stamps (triangle/square/hexagon, default radius 0.25 cell, placed by tap), with `FakeToolHost` unit tests; self-register with `[Tool]` | W1 Tue-Thu | S2-02, S2-03, S4-03 |
| D-3 | **Lesson** (7 steps below): data, state machine, validators (using B's predicates), layer setup, coach text | skeleton W1 Fri; steps 1-5 W2; steps 6-7 W3 Mon-Tue | S3-01, S3-02, S4-03; EditMode scripted correct and wrong runs |
| D-4 | **QA:** `docs/QA_SMOKE.md` smoke checklist; EditMode scripted playthrough that drives the real `EditorSession` through lesson steps 1-5 with pointer events and button calls and asserts `GoldenBoards.StarOnFullBoard`; smoke run on Flat and device at each checkpoint from Int 3 | W2 Thu - W3 Wed | checklist exists and is used; playthrough test green |
| D-5 | Single-slot JSON `IDesignIO` (Newtonsoft DTO with shape discriminator, doubles round-trip); Save/Load hooks into Settings | W3 Tue-Wed | save, relaunch, place new board, Load reproduces design; EditMode round-trip of golden boards |
| D-6 | Observer sheet (`docs/OBSERVER_SHEET.md`), demo script, lead the informal 3-user sessions in week 4 | W3 Thu - W4 | 3 sessions recorded; demo rehearsed |

**Lesson steps** (board center (0.5,0.5), guide circle radius ~0.35; coach text with hint, Next always allowed):
1. Circle on the board-center snap point, radius ~0.35 (layer `Circle`).
2. Place Snap Point on the eight equally spaced circle points (faint candidates shown).
3. Dotted vertical/horizontal diameters (double-tap line button; layer `Dotted lines`).
4. Join alternate points with Line b/w Snap points -> square 1 (layer `8 pointed Star made w squares`).
5. Join the remaining four points -> square 2 rotated 45 degrees.
6. Hide guides: "-" twice (deletes `Dotted lines`, then `Circle`) -> star revealed.
7. Tile (sketch 4): Tools -> Scale to cell size (snap-fit) -> Duplicate -> Select/Move and snap into the 4 cells; the card shows the seam error.
Lesson start creates the three layers (bottom to top: `Dotted lines`, `Circle`, `8 pointed Star made w squares`) with `GridN = 2`.

**Isolated testing:** `Sandbox_D_Input` (Editor, `FlatBoardSpace`, `LoggingEditorSession`); EditMode for tools/lesson/persistence.
**Done when:** touch drives every tool; lesson completes end to end; save/reload works.

### 3.5 Load balance

Approximate: A 25% (heavy W1-W2, light W3), B 23% (heavy W1-W2, then review/bug burn), C 27% (UI is the big visible deliverable; Scale added), D 25% (touch is small; absorbs Stamps, Rotate/Erase/Duplicate, lesson, save/load, QA, user sessions). If someone is idle in W3, they take the nearest item from Section 4.4's spillover list rather than inventing new features.

---

## 4. Timeline and integration

### 4.1 Milestones (mapped to the proposal's three)

| Milestone | End of | Proposal milestone | Acceptance |
|---|---|---|---|
| **M1** | Week 1 (Fri) | (1) signed iPhone build, board placement, basic line/circle | Signed device build with board placed and golden star rendered (A); full sketched UI (sketches 1, 2, 3 elements) running on mocks in the Editor (C); engine Line/Circle/undo tests green (B); touch input, Stamps/Rotate/Erase/Duplicate tools, lesson skeleton (D) |
| **M2** | Week 2 (Fri) | (2) construction, snapping, lesson with connected repeats | Real engine + real UI + touch on the AR board on device; all 9 tool cells present (Scale/Select/Stamps working); lesson steps 1-5 run; golden seam test passes |
| **M3** | Week 3 (Fri) | (3) persistence, profiling, demo prep | **Demo-ready RC:** lesson 1-7, Settings, single-slot save/reload, FPS evidence, scope freeze |
| Buffer | Week 4 | user testing | Polish, informal 3-user test, bug fixes, final demo build |

### 4.2 Checkpoints (twice a week: Wed and Fri, 30 min)

| ID | When | Proven / merged |
|---|---|---|
| Day 0 | Mon W1, 2 h | `contracts-v0`, mocks, prefab shells |
| Int 1 | W1 Wed | Every module runs in its sandbox on mocks; A: signed hello-AR on device; C: quick menubar + Tools panel on mock; B: session skeleton + commands tests; D: touch + simple tools |
| **Int 2 = M1** | W1 Fri | See M1 |
| Int 3 | W2 Wed | **Real engine in `Flat.unity`**: real `EditorSession` + `UIRoot` + touch + `BoardView`; draw line/circle, snap points, undo, layers, pill |
| **Int 4 = M2** | W2 Fri | Same code on device in `Main.unity` (`ARBoardSpace`); Stamps, Select/Move, Scale; lesson 1-5 |
| Int 5 | W3 Wed | Rotate/Erase/Duplicate on real engine, lesson 1-7, Settings, save/load, perf run done |
| **Int 6 = M3** | W3 Fri | Demo-ready RC, scope freeze, fidelity checklist fully verified |
| Int 7 | W4 Wed | User test feedback fixes |
| Final | W4 Fri | Demo build and script |

Everything between checkpoints is parallel. The only unavoidable syncs are Day 0, these checkpoints, and `contract` PRs.

### 4.3 Scope freeze and early warning

* **Scope freeze (W3 Fri, with M3):** only bug fixes, polish, and user-test findings after this point. Anything unfinished is dropped, not carried.
* **Early warning (W1 Fri, M1):** if M1 criteria are not met, the Monday standup of W2 re-cuts the lowest-value M/S rows in 1.4 before anything else slips.

### 4.4 Half-week Gantt

```
Slot            | A (AR/render/build)          | B (engine)                      | C (UI + Scale)                   | D (input/lesson/save)             | Sync
----------------+------------------------------+---------------------------------+----------------------------------+-----------------------------------+------------
W1 Mon          | Day 0 + A-1 signed hello-AR  | Day 0, B-1 geometry+commands    | Day 0, C-1 canvas+header, C-2    | Day 0, D-1 touch input           | Day 0 (2h)
W1 Tue          | A-1 finish build + doc       | B-1 + ToolRegistry              | C-2 quick menubar                | D-2 Rotate/Erase/Duplicate        |
W1 Wed          | A-2 planes/reticle           | B-2 snap + session skeleton     | C-2 Tools panel                  | D-2 tests, D-3 lesson data        | INT 1
W1 Thu          | A-2 place board; A-3 render  | B-2 Line/Circle/place-snap      | C-3 layer widget + pill          | D-2 Stamps tool + tests          |
W1 Fri          | A-3 device build w/ star     | B-2 undo/redo, B-3 layers       | C-3 finish, fidelity screenshots | D-3 lesson skeleton              | INT 2 = M1
W2 Mon          | A-4 live BoardView           | B-3 grid, B-4 Select/Move       | C-4 popover, toasts, Flat scene  | D-3 lesson steps 1-3              |
W2 Tue          | A-4 candidates/selection     | B-4 Select/Move + snap drag     | C-4 real wiring; C-5 Scale start | D-3 lesson steps 4-5              |
W2 Wed          | A-5 Main.unity on device     | B-4 bug fixes from Int 3        | C-5 Scale                        | lesson against real session       | INT 3
W2 Thu          | A-5 touch on AR              | B-5 golden star test            | C-5 finish, C-6 coach card       | D-4 QA checklist + playthrough   |
W2 Fri          | A-5 M2 device build          | B-5 seam test, FitToCell        | C-6 wire real lesson             | D-3 steps 1-5 on device; D-4     | INT 4 = M2
W3 Mon          | A-6 perf: dirty rebuild      | B-6 review, engine cap          | C-7 Settings sheet               | D-3 steps 6-7                     |
W3 Tue          | A-6 FPS overlay + run        | B-6 intersections (P1)          | C-7 finish, lesson UI polish     | D-5 save/load                     |
W3 Wed          | A-6 fixes                    | B-7 pair-debug Scale/lesson     | C-8 device fit                   | D-5 hook into Settings            | INT 5
W3 Thu          | A-7 RC build                 | B-7 device bug fixes            | C-8 polish + screenshots         | D-6 observer sheet, demo script  | 
W3 Fri          | RC device build              | bug burn                        | bug burn                         | bug burn                          | INT 6 = M3
W4 Mon-Tue      | bug fixes / signing refresh  | user test observer, fixes       | polish from feedback             | D-6 user tests 1-3 (lead)        |
W4 Wed          | fixes                        | fixes                           | fixes                            | fixes                             | INT 7
W4 Thu-Fri      | final build                  | final QA smoke                  | final screenshots                | demo rehearsal                    | FINAL
```

### 4.5 Dependency map

```
Contracts (Day 0) -> everyone starts in parallel against mocks.
A BoardView needs only Board + GoldenBoards.      C UI needs only the IEditorSession mock.
D touch needs IBoardSpace (FlatBoardSpace).       B needs nothing.
Soft dependencies: D lesson wants B's real session (W1 Wed onward); C Scale + D tools want B's FitToCell/bounds helper (B-1/B-5, otherwise local code).
First hard sync: Int 3 (B session + C UI + D touch + A BoardView). Second: Int 4 (A ARBoardSpace on device).
```

If B's session is late, Int 3 still happens with whatever tools exist (Line + Circle + undo is the minimum bar); later tools plug in without rewiring because tools self-register.

---

## 5. Git, branching, merge strategy

Same rules as for a normal Unity team, tightened for a 4-week sprint.

* **Repo setup (A, pre-Day-0):** Force Text serialization, Visible Meta Files; Unity `.gitignore` (Library, Temp, Obj, Logs, UserSettings, Builds, `*.csproj`, `*.sln`, `xcuserdata`, generated Xcode project, `Assets/Editor/Local/`); **Git LFS** for `*.png *.jpg *.psd *.fbx *.wav *.mp3 *.mp4 *.ttf *.otf *.a *.dylib *.bundle *.exr`; **UnityYAMLMerge** driver in `.gitattributes` (`*.unity *.prefab *.asset *.mat merge=unityyamlmerge eol=lf`) with the per-machine git config in `docs/GIT_SETUP.md`; `.editorconfig`; `CODEOWNERS`; PR template.
* **Ownership (what prevents conflicts):** folders and files are exclusively owned (section 2.2); only A edits `Main.unity`, only C edits `Flat.unity`; sandboxes are private; everything shared is a small additive prefab owned by one person (nested prefabs for UI: `QuickMenubar`, `ToolsPanel`, `LayerWidget`, `SettingsSheet`, `CoachCard`); `ProjectSettings`/`Packages` only via A; nobody edits another person's `.asmdef`; tools are added as new files that self-register (no shared registry file).
* **Branching (timeline-sensitive):** trunk-based, branches `feat/<area>-<topic>` live <= 1 day and merge to `main` at least daily; rebase every morning; squash merge; messages `area: summary`. `main` must always open in Unity, pass EditMode tests, and play `Flat.unity`. Tags `contracts-v0`, `int-1`..`int-7`, `m1`, `m2`, `m3`, `final`; A builds a device build for every `int` tag.
* **Review:** 1 approval (not the author) within 2 hours; PRs touching another owner's folder need that owner; `contract` PRs need one approver per consuming module. PRs < ~300 lines. During crunch (W3), reviews may be post-merge for the owner's own folders (still announced in chat). No CI licence assumed: `./tools/check.sh` runs `Unity -batchmode -runTests -testPlatform EditMode` before pushing to `main`.
* **Naming:** namespaces `GeoART.<Module>`; `PascalCase` types; `IThing`; `_camelCase` privates; `XxxTool`, `XxxCommand`, `XxxInstaller`, `MockXxx`; scenes `Sandbox_<Letter>_<Topic>.unity`; sprites `ui_<screen>_<name>.png`; coordinate variables end `...Board`, `...Px`, `...World`.

---

## 6. Risks, mitigations, assumptions

### 6.1 Risks

| Risk | L / I | Mitigation |
|---|---|---|
| Schedule: only 3 weeks of real work | High / High | Aggressive cuts (1.2); all behaviour behind mocks so nobody waits; week 4 is buffer; scope freeze at M3; spillover list = polish only |
| Signing/provisioning (7-day free profile, Xcode problems) | Med / High | A-1 first thing in W1; weekly re-sign reminder; Team ID via env var; documented path |
| Only some teammates have a Mac/iPhone | Med / High | A and D own device work; B and C Editor-only; A builds each `int` tag |
| **B critical path** (Int 3 needs a working session) | Med / High | Others run on mocks; B ships Line + Circle + undo first; tools self-register so later ones do not block; C and D carry the Scale, Stamps, Rotate, Erase and Duplicate tools |
| AR drift/tracking loss | Med / Med | ARAnchor, `TrackingLost` state cancels unfinished shapes, "Re-place board", textured-surface guidance; test on 3 surfaces |
| Performance < 30 FPS | Med / High | Batched meshes, dirty rebuild only, 300-shape cap, grid cap 4x4; FPS overlay from W2, measured once in W3 |
| Merge conflicts in scenes/prefabs | Med / Med | Ownership rules (5), nested prefabs, YAML merge, A alone edits Main |
| Contract churn | Med / High | Fewer, simpler interfaces; freeze after Day 0; additive-only; review by consumers |
| Sketch/prose ambiguity (tiling, "click", Settings) | High / Med | Decisions D-xx; fidelity checklist with F/M/S levels; Day 0 sign-off |
| Scale tool slips (needed for sketch 4) | Med / Med | Fallback: tap = fit-to-cell; C-5 starts W2 Tue with a day of slack |
| Save/load and lesson validators eat the buffer | Med / Med | Single-slot save; validators lenient (Next always allowed) |

### 6.2 Assumptions and decisions (not in the sketches, or interpretations)

Sketched elements are never altered by these.

| ID | Topic | Decision |
|---|---|---|
| D-01 | Settings sheet | One sheet: palette (6 swatches), width (Thin/Medium/Thick), Start lesson, Save, Load, Clear (confirm), Re-place board, Show FPS. No other new screens. |
| D-02 | Hand Gestures / Screen toggle | Drawn exactly as in sketch 1 (labels "Hand Gestures" and "Screen", pill toggle) but non-functional: "Hand Gestures" is greyed out/disabled, the knob rests on the Screen side, and Screen is the only active mode. Taps on the toggle do nothing. All input is touch plus the on-screen buttons. |
| D-03 | Screen-mode input | Touch on board = pointer events, cursor = last touch; buttons act at the cursor; no finger offset. |
| D-04 | Board | 40 cm square drawing area with a thin outer frame (sketch 1 shows an outer and inner quad), dashed dividers per cell. |
| D-05 | Tools mode | Replaces header/layers/pill/toggle/buttons (sketches 2/4); tap tool = select and stay; tap "Tools" title bar = back; Undo/Redo only in quick menubar. |
| D-06 | "Snap" cell | Toggles magnetic snapping (default on). |
| D-07 | Grid | N x N, N in 1..4, default 2; pill cycles on tap. |
| D-08 | Palette | 6 swatches: black, white, red, orange, blue, green; default black, Medium; sketch-4 heavy strokes = Thick. |
| D-09 | Wall vs table | Table only; sketch's "wall" annotation is not implemented (cut). |
| D-10 | Layer widget | Segments bottom-to-top; tap = active; double-tap = show/hide; "+" adds top layer; "-" deletes the bottom layer with content and snap points (undoable, min 1 layer); Arrange = long-press a segment moves it up one slot (wraps). |
| D-11 | Stamps options | Triangle, Square, Hexagon; current palette color and width; default size. |
| D-12 | Sketch 2 diagonal strip | The real table edge; not UI. |
| D-13 | Header | Shown in quick-menubar mode; sketch 3 omits it, not read as intentional. |
| D-14 | Duplicate | Tap copies the selection to the next free cell (snapped), one undo step; subsequent moves use Select/Move + snap (sketch 4). Fill grid cut. |
| D-15 | Save/reload | Single slot in Settings; Load always rebuilds the design on the currently placed board (prompt to place if none). |
| D-16 | Cap | 300 shapes; toast beyond that. |
| D-17 | Connectors | Not built. Seams in the guided star come from Scale-fit + snap so diamond tips land on cell-edge midpoints (visible in sketch 4). |
| D-18 | Lesson UI | Text coach card at the top of the view only while the lesson runs, started from Settings. Builds the sketch-3 figure on the full board, then step 7 tiles to sketch 4. No ghost overlays. Free Design = no lesson. |
| D-19 | Time frame | 3-week target + 1-week buffer, 4-week hard cap; milestones at end of W1, W2, W3. |
| D-20 | "click" vs double ring | One button; commits the active tool; Circle is chosen from the Tools grid. |
| D-21 | Placement UX | Scanning coach text, reticle on detected plane, tap places; no confirm; Settings > Re-place board. |
| D-22 | Rotate | Tap = +45 degrees about the selection centroid. Scale: drag, uniform, snap-fit to cell side when Snap is on (fallback tap = fit-to-cell). Erase: tap only. |
| D-23 | Line b/w Snap points | Joins latest and previous snap point; placing on an existing point re-selects it as latest (needed to close squares). |
| D-24 | Radial 4/8 helper | Out of MVP. |
| D-25 | Export/share, onboarding | Not in MVP. |
| D-26 | Evaluation | Informal: 3 users in week 4, observer sheet (completion, time, problems) + FPS overlay; proposal targets (seam 1e-5 x cell, 30 FPS) reported as measured, not guaranteed. |

---

## 7. Verification note

Checked against `PROPOSAL_SUMMARY.md` and the sketch renders: all four sketches mapped in 1.4 with every drawn element present in the UI (F/M/S level stated); required-feature list covered or explicitly reduced in 1.2 (one board, stamps/lines/circles, palette + width in Settings, touch input, snapping, layers/guide lines, guided lesson, undo, save/reload; bounded grid repetition reduced to the pill + Duplicate + snap tiling, "fill grid" cut); proposal milestones mapped in 4.1; stretch/out-of-scope kept out; input is touch/screen only and the Hand Gestures / Screen toggle is drawn disabled (D-02).
