# Geometric ARt - MVP Plan (4 people, parallel from Day 1)

Sources: `AR_Geometric_Pattern_Studio_Proposal-1.pdf` (sketches p3-6), `docs/PROPOSAL_SUMMARY.md`, and the page renders in `/tmp/pdfpages/h03..h06.png` (re-checked directly while writing this plan).
Status: working document. Day 0 locks section 2; after that, changes follow the contract-change protocol (2.8).

Conventions used below: **A / B / C / D** = the four people (A = AR + rendering + build, B = engine, C = UI, D = input + lesson + persistence + QA). "Sketch" always means the proposal PDF. "ASSUMED" = not in the sketches; see section 6.2 for the full list of assumptions (IDs `D-xx`).

---

## 1. MVP definition and sketch fidelity

### 1.1 MVP in one paragraph

An iPhone-only Unity app (Unity + AR Foundation/ARKit) where the user scans a surface (table; wall as a bonus, because sketch 1 says "Mounted on wall or placed on table"), places **one anchored square art board**, and draws on it with the **sketched quick menubar** (Place Snap Point, click, Line between Snap points, Undo, Redo, Tools) or with the **Tools 3x3 grid** (Line, Select/Move, Rotate, Circle, Snap, Scale, Stamps, Erase, Duplicate), organizes content in **layers** (+, arrange, delete bottom), sets the **cell count** with the "2x2" pill, switches between **Hand Gestures** and **Screen** input, follows **one guided Eight-Pointed Star lesson** that produces exactly the construction in sketch 3 and ends with the 2x2 tiling of sketch 4, and can **save/reload** the design onto a newly placed board. Target: >= 30 FPS median over 2 minutes on a physical iPhone.

### 1.2 In / out of MVP

| In (required by proposal scope and/or drawn in sketches) | Out (stretch or explicitly excluded) |
|---|---|
| One board, bounded N x N cell grid (1..4, default 2) | Six-fold lesson / triangular lattice, more star recipes, trimming, interlacing, standalone Mac editor (stretch list) |
| Line, Circle, Stamps (triangle/square/hexagon), Select/Move, Rotate, Scale, Snap, Erase, Duplicate | Radial 4/8-copy helper (in proposal prose, not in the required-feature list and not sketched) - stretch |
| Explicit snap points + automatic snapping (endpoints, centers, circle-division points, intersections, cell corners/midpoints) | All wallpaper groups, arbitrary tessellation, general constraint solver (explicitly out of scope) |
| Layers (add, arrange, delete bottom), guide (dotted) lines, show/hide | Auto-generated connector segments (cardinal tips to cell edge midpoints) - see D-17: the seam is achieved by Scale-fit + snap instead |
| Palette + line-width (placed in Settings, ASSUMED) | Export/share, onboarding tutorial, multi-user, persistent world anchors |
| Undo/Redo, save/reload (rebuild on new board), Clear | Live "source cell updates every copy" linked repeat (prose only; sketch 4 shows manual duplicate/drag). Optional P2 in B's plan |
| Touch input for every command; Vision hand tracking (fingertip cursor + pinch) with on-screen buttons as the always-available fallback | |
| Guided Eight-Pointed Star lesson (coach card ASSUMED) | |
| Evaluation with 3-5 users, FPS profiling | |

### 1.3 Interaction model (resolves the "click / Place Snap Point / Line b/w Snap points" ambiguity)

This is the one behavioural definition everybody codes against.

* There is a **cursor** in board space. In **Hand Gestures** mode the cursor is the index fingertip (pinch = pointer Down, release = Up). In **Screen** mode the cursor is the last touch position on the board; touch down/move/up on the board are pointer Down/Move/Up with no offset (D-03). The on-screen buttons work in both modes ("Every action also works with touch or mouse").
* **Place Snap Point** (dot button): adds a persistent snap point at the (snapped) cursor position, on the active layer. If the cursor is on an existing snap point or on a faint candidate (circle-division, intersection, cell point), that point becomes/gets promoted and is marked "latest".
* **click** (double-ring button, the "click" word is drawn inside it in sketch 1): commits the active tool's action at the cursor. For Line/Circle/Stamps it equals pointer Down+Up at the cursor, so with the Line tool "click, move, click" draws a line and "drag" also draws a line. It is the screen-mode equivalent of a pinch.
* **Line b/w Snap points** (line-with-two-dots button): draws a line between the latest and the previous snap point (skips if that line already exists). **Double tap** toggles solid/dotted for new lines (sketch 3 annotation); the button glyph shows the current style.
* **Tools** opens "Tools mode" (sketches 2 and 4): the whole lower area becomes the Tools panel. The header, layer widget, 2x2 pill, mode toggle, and 6 buttons are hidden exactly as drawn. Tapping a tool selects it and keeps the panel open (Select, Scale, Duplicate need repeated use in sketch 4). Tapping the "Tools" title bar of the panel returns to the quick menubar (D-05; the sketches draw no back control). Undo/Redo are reachable only from the quick menubar, as drawn.
* Snap is on by default; the **Snap** cell in the Tools grid toggles snapping on/off (highlighted when on) (D-06).

### 1.4 Sketch fidelity checklist

Rule: every row below is a must-have; every UI element that is **not** in this table must be listed in section 6.2 and may only appear in the places named there (Settings sheet, Stamps popover, layer widget gestures, lesson coach card, placement coach text, toasts). Layout reference for C (fractions of screen height, from sketch 1/2/4): header 0-7%; AR viewport behind everything; quick menubar zone = bottom ~35% (mode toggle row at ~70%, two rows of circle buttons at ~77-96%); layer widget at left edge and "2x2" pill at right edge, both just above the toggle row (~62-66%); Tools panel = bottom ~30-35% (title bar + 3x3 grid).

**Sketch 1 - Home screen (p3)** caption: "User opens the application and mounts the board, sees the quick menubar"

| ID | Drawn element | Owner | Task / acceptance criterion |
|---|---|---|---|
| S1-01 | Title text "Geometric ARt" top-left | C | C-1. Visible in quick-menubar mode; text exactly `Geometric ARt`. |
| S1-02 | Round gear/flower "Settings" icon top-right | C (shell), D (save/load/lesson actions), B (palette/width state) | C-1, C-6. Tap opens Settings sheet (contents ASSUMED: D-01). Sheet closes back to Home. |
| S1-03 | AR viewport with perspective-skewed Canvas/Board: outer quad (canvas) + inner square with dashed vertical and horizontal lines (2x2 cell dividers); "Mounted on wall or placed on table" | A | A-2, A-4. Board is anchored in world, correct perspective, dashed dividers = current `GridN`. Horizontal plane required; vertical plane P1 (D-09). Outer thin frame drawn around the drawing area (D-04). |
| S1-04 | Layer widget left edge: "+" (Add Layers), stack icon (Arrange Layers), "-" (Delete Bottom Layer) | C (UI), B (ops) | C-3, B-4. "+" adds a layer on top and selects it; bar = one segment per layer (S3-02); drag a segment to rearrange; "-" deletes the bottom layer and its content (undoable). Never deletes the last layer (D-10). |
| S1-05 | "2x2" pill right edge; "Specify no of cells on canvas" | C (UI), B (grid), A (dividers) | C-3, B-4, A-4. Tap cycles/opens picker 1x1, 2x2, 3x3, 4x4 (cap 4; D-07). Pill text updates. Board dividers update. Default 2x2. |
| S1-06 | Toggle row: "Hand Gestures" [toggle] "Screen" | C (UI), D (router) | C-2, D-1/D-5. Labels exact; toggle knob drawn at left = Hand Gestures. Switching changes the active `IInputSource`; "Can be operated via phone screen" holds: every action works in Screen mode. Default per D-02 (gate-dependent). |
| S1-07 | Button r1c1: circle with filled dot, "Place Snap Point" | C, B | C-2, B-3. Places a snap point at the cursor (see 1.3). |
| S1-08 | Button r1c2: double ring with "click" | C, B, D | C-2, B-3. Commits active tool at cursor. |
| S1-09 | Button r1c3: diagonal line with two dots, "Line b/w Snap points" | C, B | C-2, B-3. Draws line between latest two snap points. |
| S1-10 | Button r2c1: left arrow (Undo) | C, B | C-2, B-3. Undo; disabled when stack empty. |
| S1-11 | Button r2c2: right arrow (Redo) | C, B | C-2, B-3. Redo. |
| S1-12 | Button r2c3: "Tools" | C | C-2. Enters Tools mode (S2). |
| S1-13 | Board "mounted" by the user at launch | A | A-2. Launch -> scanning coach text (ASSUMED) -> tap places the board on the detected plane. |

**Sketch 2 - Tools Screen (p4)** caption: "User checks out the tools available"

| ID | Drawn element | Owner | Task / acceptance criterion |
|---|---|---|---|
| S2-01 | Panel with centered title bar "Tools" | C | C-2. Title text `Tools`; tapping it returns to quick menubar (D-05). |
| S2-02 | 3x3 grid, labels in this order: Line, Select/Move, Rotate / Circle, Snap, Scale / Stamps, Erase, Duplicate | C (UI), B+C (tool logic) | C-2; B-3/B-5, C-8. Labels exact (use "Select/Move" from sketch 2, not sketch 4's "Select"; capitalize "Circle"). Each cell activates its tool and highlights it. |
| S2-03 | "Has options" on Stamps | C (popover), B (StampKind) | C-5, B-5. Tapping Stamps opens a small popover with Triangle / Square / Hexagon (ASSUMED options, D-11); chosen stamp is placed by click/tap. |
| S2-04 | Board visible in the upper ~65-70% of the screen, in perspective, panel does not hide it; other HUD hidden | A, C | A-2, C-2. In Tools mode no header, layer widget, pill, toggle, or button grid is shown. Board stays live and interactive. |
| S2-05 | Long thin diagonal double line from left-middle to bottom-right (viewport) | - | Interpreted as the real table edge in the camera image, not an app element. No task (D-12). |

**Sketch 3 - Preview / creating a Pattern (p5)** caption: "User makes a motif, uses layers to make it easy to delete unnecessary lines."

| ID | Drawn element | Owner | Task / acceptance criterion |
|---|---|---|---|
| S3-01 | Board front-on showing: board-square with corner dots and dashed diagonals; dashed vertical/horizontal diameters; center dot; circle; 8 dots on circle; two solid squares (axis-aligned + 45 degrees) = 8-pointed star | D (lesson produces it), B (geometry/snap), A (rendering) | D-3, B-2, A-3. The lesson, followed to the end of step 5, yields exactly this figure: circle on board center, 8 snap points at 45-degree spacing, dotted diameters (and diagonals), two solid squares. Golden test in `Core.Tests` compares to `GoldenBoards.StarOnFullBoard`. Corner dots = derived board-corner snap candidates. |
| S3-02 | Layer widget as vertical segmented bar with "+" above, "-" below, three segments labelled top to bottom "8 pointed Star made w squares", "Circle", "Dotted lines" | C (widget), D (lesson creates the 3 layers) | C-3, D-3. Segments labelled with layer names to the left (leader arrows optional). Tap segment = active layer; deleting bottom ("-") twice reveals the star. |
| S3-03 | "Double tap to switch to dotted line" on the line button | C, B | C-2, B-3. Double tap toggles the line style; glyph shows solid vs dotted; dotted lines render as dashes (A-3). |
| S3-04 | Small captions "Undo" over left-arrow button, "Redo" over right-arrow button | C | C-2. Captions drawn above those two buttons. |
| S3-05 | "2x2" pill and Hand/Screen row present (label "Hand" here, "Hand Gestures" in sketch 1) | C | C-2. Use "Hand Gestures" (sketch 1, canonical). Noted deviation. |
| S3-06 | Header row not drawn in this sketch | C | Header shown in all quick-menubar states (sketch 1 canonical; D-13). |

**Sketch 4 - Scaling & tiling (p6)** caption: "User switches to tools mode, scales and tiles the pattern on a 2*2 grid using duplicate, select and then dragging the shape. Snap points are essential to snap the tiles into place."

| ID | Drawn element | Owner | Task / acceptance criterion |
|---|---|---|---|
| S4-01 | Board with 2x2 copies of the star motif in heavy strokes; thin faint construction behind; dots at board corners, edge midpoints, centers | A (render), B (snap candidates), C/B (Scale, Duplicate, Select) | A-3, B-3, C-8. Thick line-width preset renders heavy; derived snap dots (corners, edge midpoints, cell centers) visible in Tools mode when Snap is on. |
| S4-02 | Tools panel only (same 3x3 as sketch 2, with "Stamps / has options") | C | Same as S2-01..03. |
| S4-03 | Workflow: Scale -> Duplicate -> Select -> drag -> snap => tiled 2x2 | B, C, D | B-5 (snap-while-drag, fit helper), C-8 (Scale, Duplicate tools), D-3 (lesson step 7). Acceptance: from the finished star on the full board, a user reaches 4 star copies in the 4 cells using only those tools; the 45-degree squares touch at cell-edge midpoints within 0.00001 x cell side (golden test `Tiled2x2_SeamsMatch`). |
| S4-04 | Undo/Redo, layers, pill, toggle not drawn in Tools mode | C | Hidden in Tools mode (see S2-04, D-05). |

---

## 2. Architecture and shared contracts

### 2.1 Principles

1. **Pure C# core.** Everything about geometry, snapping, tools, commands, layers, serialization lives in `noEngineReferences` assemblies so it runs in EditMode tests on any machine without AR (the proposal requires unit tests on the Mac without starting AR).
2. **Board space.** All geometry is in a normalized board space: origin bottom-left as seen from the user, +x right, +y up, board = [0,1] x [0,1]. Grid N x N: cell side = 1/N. Doubles everywhere (seam tolerance 1e-5 x cell side).
3. **One writer.** The board is mutated only by `ICommand`s executed by the engine. UI, input, lesson, renderer, and persistence never write to the model directly; they call `IEditorSession` or read it.
4. **Four seams, all interfaces:** `IEditorSession` (engine <-> UI/input/lesson/renderer), `IBoardSpace` (AR <-> input/hand mapping), `IInputSource` (input -> engine), `IDesignStore/ISerializer` (persistence <-> everyone). Everything else is data in `GeoART.Contracts`.
5. **Consumer owns the mock; producer owns the real thing.** Day 0 ships minimal stubs so every sandbox compiles; the consumer extends their mock as needed.

### 2.2 Unity project layout (single Unity project at repo root; owner per folder)

```
Assets/
  GeoART/
    Contracts/            [COUNCIL] asmdef GeoART.Contracts (noEngineReferences)
      Math/ Model/ Commands/ Snap/ Tools/ Session/ Input/ Board/ Lesson/ Persistence/ Events/ Fixtures/
    Mocks/                asmdef GeoART.Mocks (refs Contracts) - per-subfolder owner
      ForUI/              [C]   MockEditorSession, MockLessonController, MockPlacement, MockDesignStore
      ForInput/           [D]   MockEditorSession (minimal logger), FlatBoardSpace usage, replay hand data
      ForBoard/           [A]   StubEditorSession (static board), FlatBoardSpace (ortho camera), SampleBoards
    Core/                 [B]   asmdef GeoART.Core (noEngineReferences)
      Geometry/ Snap/ Commands/ Session/ Grid/ Layers/
      Tools/  (Line, Circle, Stamps, SelectMove, SnapToggle)       [B]
      Tools/Transform/ (Rotate, Scale, Duplicate, Erase)           [C from week 3 - own subfolder]
    ARBoard/              [A]   asmdef GeoART.ARBoard  (AR session, plane detect, placement, anchor, ARBoardSpace)
    BoardRender/          [A]   asmdef GeoART.BoardRender (BoardView: layers -> meshes, dots, overlay, selection)
    UI/                   [C]   asmdef GeoART.UI (UGUI + TextMeshPro): Screens/, Widgets/, Theme/
    Input/                [D]   asmdef GeoART.Input: Touch/, Hands/, Router/
    Lesson/               [D]   asmdef GeoART.Lesson
    Persistence/          [D]   asmdef GeoART.Persistence
    Telemetry/            [D]   asmdef GeoART.Telemetry (eval logger, FPS sampler)
    App/                  [A]   asmdef GeoART.App: Bootstrap (composition root), AppConfig
  Plugins/iOS/            [D]   VisionHandBridge.mm / .h (+ optional macOS editor build of the bridge)
  Prefabs/
    Board/ [A]  UI/ [C]  Input/ [D]  Engine/ [B]  Lesson/ [D]
  Scenes/
    Main.unity            [A]   AR app: composes prefabs only
    Flat.unity            [C]   no-AR integration scene (ortho camera, runs on any machine)
    Sandbox/              Sandbox_A_AR, Sandbox_B_Engine, Sandbox_C_UI, Sandbox_D_Input, Sandbox_D_Hands (each owner-private)
  Editor/Build/           [A]   BuildScripts, post-process Xcode, env-var-based signing
  Tests/EditMode/<Module>/ owner of module
  Tests/PlayMode/         [D]
ProjectSettings/ Packages/   [A] (changes via PR requested from A)
docs/                      shared
```

Unity version/packages pinned on Day 0 by A (suggest Unity 6 LTS + AR Foundation 6.x + Apple ARKit XR Plugin + URP with AR Background Renderer Feature + TextMeshPro + Newtonsoft Json + Unity Test Framework). Decide URP vs built-in on Day 0 and never change it.

### 2.3 Wiring: ServiceHub + EventBus + installers (no hard references between modules)

Each module's prefab carries an `Installer` MonoBehaviour (`[DefaultExecutionOrder(-100)]`) that registers its services in `ServiceHub` in `Awake`. Consumers `ServiceHub.Resolve<T>()` in `Start`. `Main.unity` (A) and `Flat.unity` (C) only instantiate the five prefabs: `EngineHost` (B), `BoardRoot` (A; in Flat it uses `FlatBoardSpace`), `UIRoot` (C), `InputRoot` (D), `LessonHost` (D). Sandboxes instantiate the real prefab of their own module plus mocks for the rest.

### 2.4 Contract code (Day 0 deliverable; compile this first)

Namespace `GeoART.Contracts`. This is the starting point, not holy text; Day 0 may adjust it, then it freezes (2.8).

```csharp
// ---------- Math (Contracts/Math) ----------
public readonly struct Vec2 : IEquatable<Vec2> {
    public readonly double X, Y;
    public Vec2(double x, double y) { X = x; Y = y; }
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public bool Equals(Vec2 o) => X == o.X && Y == o.Y;
}
public readonly struct Rect2 { public readonly Vec2 Min, Max; /* ctor, Center, Size, Contains */ }
/// Rotation about Pivot then translation; uniform scale only (no shear; keeps circles circles).
public readonly struct Similarity2 {
    public readonly Vec2 Pivot, Translation; public readonly double RotationRad, Scale;
    public Vec2 Apply(Vec2 p) { /* p' = Pivot + Scale*R(p-Pivot) + Translation */ }
}
public readonly struct RgbaColor { public readonly byte R, G, B, A; }
public readonly struct EntityId : IEquatable<EntityId> {
    public readonly int Value; public static readonly EntityId None = new(0);
    public EntityId(int v) { Value = v; } public bool Equals(EntityId o) => Value == o.Value;
}

// ---------- Model (Contracts/Model) ----------
public enum LineStyle { Solid, Dotted }               // Dotted = construction/guide look
public enum LineWidth { Thin, Medium, Thick }          // renderer maps to board-relative widths
public enum StampKind { Triangle = 3, Square = 4, Hexagon = 6 }
public readonly struct StrokeStyle {
    public readonly RgbaColor Color; public readonly LineWidth Width; public readonly LineStyle Dash;
}
public enum ShapeKind { Line, Circle, Polygon }
public abstract class Shape {
    public EntityId Id; public StrokeStyle Style;
    public abstract ShapeKind Kind { get; }
    public abstract Shape Clone();
}
public sealed class LineShape    : Shape { public Vec2 A, B; }
public sealed class CircleShape  : Shape { public Vec2 Center; public double Radius; }
public sealed class PolygonShape : Shape { public Vec2 Center; public int Sides; public double Radius, RotationRad; } // stamps

public enum SnapKind { None, UserPoint, Endpoint, Center, CircleDivision, Intersection, CellCorner, CellEdgeMid, CellCenter }
public sealed class SnapPoint { public EntityId Id; public Vec2 Position; }   // user-placed; owned by a Layer

public sealed class Layer {
    public EntityId Id; public string Name; public bool Visible = true;
    public readonly List<Shape> Shapes = new();           // draw order: first = back
    public readonly List<SnapPoint> SnapPoints = new();   // deleted/hidden together with the layer
}
[Flags] public enum BoardChangeKind { None = 0, Shapes = 1, Layers = 2, SnapPoints = 4, Grid = 8, Reset = 16 }
public sealed class Board {
    public const int MaxGridN = 4;                        // bounded repetition cap (D-07)
    public int GridN = 2;                                 // N x N cells; cell side = 1/GridN
    public double CellSide => 1.0 / GridN;
    public readonly List<Layer> Layers = new();           // index 0 = BOTTOM layer
    public EntityId ActiveLayerId;
    public int NextId = 1;
    public EntityId NewId() => new(NextId++);
}

// ---------- Commands / undo-redo (Contracts/Commands) ----------
public interface ICommand {
    string Label { get; }
    BoardChangeKind Affects { get; }
    void Do(Board b);
    void Undo(Board b);                                   // must exactly invert Do (store what you removed)
}
public interface ICommandStack {
    bool CanUndo { get; } bool CanRedo { get; }
    void Execute(ICommand c);                             // clears redo
    bool Undo(); bool Redo(); void Clear();
    IDisposable BeginGroup(string label);                 // macro: one undo step (e.g. Fill grid)
    event Action Changed;
}

// ---------- Snapping (Contracts/Snap) ----------
public readonly struct SnapCandidate { public readonly Vec2 Position; public readonly SnapKind Kind; public readonly EntityId Source; }
public readonly struct SnapResult   { public readonly bool Snapped; public readonly Vec2 Position; public readonly SnapKind Kind; public readonly EntityId Source; }
public interface ISnapService {
    bool Enabled { get; set; }                            // the "Snap" tool cell toggles this
    double Radius { get; set; }                           // board units; session derives from ~24 px via IBoardSpace
    int CircleDivisions { get; set; }                     // default 8
    SnapResult Snap(Board b, Vec2 raw, IReadOnlyCollection<EntityId> excludeShapes = null);
    IReadOnlyList<SnapCandidate> Candidates(Board b);     // user points + derived (for drawing dots); visible layers only
}

// ---------- Tools (Contracts/Tools) ----------
public enum ToolId { None, Line, SelectMove, Rotate, Circle, Snap, Scale, Stamps, Erase, Duplicate }
public enum PointerPhase { Down, Move, Up, Cancel }       // Cancel = tracking lost / interrupted: abort unfinished shape
public enum InputKind { Touch, Hand, Button }
public readonly struct PointerEvent {
    public readonly PointerPhase Phase; public readonly Vec2 BoardPos; public readonly bool OnBoard;
    public readonly double Time; public readonly InputKind Source; public readonly int PointerId;
}
public interface IToolHost {                              // everything a tool may touch
    Board Board { get; } ICommandStack Commands { get; } ISnapService Snap { get; }
    StrokeStyle Style { get; }                            // current palette color + width + dash
    StampKind Stamp { get; }
    Selection Selection { get; }
    IReadOnlyList<Shape> PreviewShapes { get; }
    void SetPreview(IReadOnlyList<Shape> ghosts);         // transient, never in the model
    void Hint(string text);                               // short coach text, optional
}
public sealed class Selection { public readonly HashSet<EntityId> Ids = new(); }
public interface IDrawingTool {
    ToolId Id { get; }
    void Activate(IToolHost host); void Deactivate();     // Deactivate must cancel unfinished work
    void OnPointer(in PointerEvent e);                    // Down/Move/Up/Cancel; commit = host.Commands.Execute(...)
}

// ---------- Engine facade (Contracts/Session) - THE central seam ----------
public interface IEditorSession {
    Board Board { get; }                                  // read-only by convention
    ToolId ActiveTool { get; } StampKind ActiveStamp { get; }
    bool SnapEnabled { get; } bool DottedLine { get; }
    StrokeStyle Style { get; set; }                       // palette color + line width (Settings)
    Selection Selection { get; }
    Vec2? Cursor { get; } SnapResult CursorSnap { get; }
    IReadOnlyList<Shape> Preview { get; }                 // tool ghosts
    IReadOnlyList<Shape> Overlay { get; }                 // lesson ghosts / hints (set by SetOverlay)
    string ToolHint { get; }
    // pointer path (from IInputSource)
    void SetCursor(Vec2 boardPos, bool onBoard);          // hover; hand cursor or last touch
    void Pointer(in PointerEvent e);
    // quick menubar (sketches 1/3)
    void PlaceSnapPoint(); void ClickAtCursor(); void LineBetweenSnapPoints(); void ToggleDottedLine();
    // tools grid (sketches 2/4)
    void SetTool(ToolId t); void SetStamp(StampKind k); void SetSnapEnabled(bool on);
    void DuplicateSelection();                            // tap = one copy to next free cell, snapped (D-14)
    void FillGrid();                                      // double-tap Duplicate = copy selection to all cells (bounded) (D-14)
    // layers
    EntityId AddLayer(); void SelectLayer(EntityId id); void MoveLayer(EntityId id, int newIndex);
    void DeleteBottomLayer(); void SetLayerVisible(EntityId id, bool visible);
    // grid
    void SetGrid(int n);                                  // 1..Board.MaxGridN
    // history + lifecycle
    bool CanUndo { get; } bool CanRedo { get; } void Undo(); void Redo();
    void ClearBoard(); void LoadBoard(Board b);           // LoadBoard resets history
    void SetOverlay(IReadOnlyList<Shape> ghosts);
    event Action<BoardChangeKind> BoardChanged;           // model changed (renderer, lesson, autosave)
    event Action StateChanged;                            // tool/selection/style/undo-availability/hint
    event Action PreviewChanged;                          // ghosts/cursor (high frequency; renderer only)
}

// ---------- Board <-> screen/world (Contracts/Board) ----------  implemented by A (ARBoardSpace, FlatBoardSpace)
public interface IBoardSpace {
    bool IsPlaced { get; }
    bool TryScreenToBoard(Vec2 screenPx, out Vec2 board);  // ray vs board plane; false if no hit/not placed
    bool TryBoardToScreen(Vec2 board, out Vec2 screenPx);
    double PxToBoardUnits(double px);                       // for snap radius / hit tolerance
    double BoardSizeMeters { get; }
    event Action Changed;                                   // placed / moved / re-anchored / camera pose update
}
public enum PlacementState { Unsupported, Scanning, PlaneFound, Placed, TrackingLost }
public interface IBoardPlacement {
    PlacementState State { get; }
    void PlaceAtReticle(); void Reset();                   // Reset = "re-place board" (Settings)
    event Action<PlacementState> StateChanged;
}

// ---------- Input (Contracts/Input) ----------  implemented by D
public enum InputMode { HandGestures, Screen }
public enum TrackingState { Unavailable, Searching, Tracking, Lost }
public interface IInputSource {
    InputMode Mode { get; }
    bool Active { get; set; }                               // inactive sources emit nothing
    TrackingState Tracking { get; }
    event Action<Vec2, bool> CursorMoved;                   // board pos, onBoard
    event Action<PointerEvent> Pointer;
    event Action<TrackingState> TrackingChanged;
}
public interface IInputRouter {                             // forwards the active source into IEditorSession; owned by D
    InputMode Mode { get; set; }                            // the Hand Gestures / Screen toggle binds here
    event Action<InputMode> ModeChanged;
}
public readonly struct HandJoint { public readonly Vec2 ImageNorm; public readonly float Confidence; }   // Vision normalized image coords
public sealed class HandFrame {                             // joints: wrist, thumbTip, indexTip (+ others as needed)
    public double Timestamp; public int ImageOrientation; public HandJoint Wrist, ThumbTip, IndexTip; public bool HasHand;
}
public interface IHandFrameSource {                         // lets D run Vision on device, on Mac webcam, or on a replay file
    event Action<HandFrame> Frame;                          // timestamped joints (index tip, thumb tip, wrist...) + confidence
}

// ---------- Lesson (Contracts/Lesson) ----------  implemented by D
public sealed class LessonState {
    public string LessonId; public int StepIndex, StepCount;
    public string Title, Instruction, Hint; public bool StepComplete, Finished;
}
public interface ILessonController {
    bool Active { get; } LessonState State { get; }
    void Start(string lessonId);                            // "eight-point-star": clears board, creates the 3 layers, GridN=2
    void Stop(); void Next(); void Back(); void Skip();
    event Action<LessonState> StateChanged;
}

// ---------- Persistence (Contracts/Persistence) ----------  implemented by D
public interface IDesignSerializer {
    int SchemaVersion { get; }
    string Serialize(Board b);                              // JSON, includes schemaVersion; geometry only (board space)
    Board Deserialize(string json);                         // throws DesignFormatException; migrates older versions
}
public interface IDesignStore {
    IReadOnlyList<DesignInfo> List();
    void Save(string name, Board b); Board Load(string name); void Delete(string name);
    void AutoSave(Board b); Board TryLoadAutoSave();        // on app background (D-15)
}

// ---------- Events (Contracts/Events) ----------
public static class EventBus { public static void Publish<T>(T e); public static IDisposable Subscribe<T>(Action<T> h); }
public readonly struct ToastRequested   { public readonly string Message; }   // UI shows toasts
public readonly struct FpsSample        { public readonly float Fps, FrameMs; }
public readonly struct FalseGestureLogged { public readonly string Kind; }   // eval metrics
public static class ServiceHub { public static void Register<T>(T s); public static T Resolve<T>(); public static bool TryResolve<T>(out T s); }

// ---------- Palette (Contracts/Model) ----------
public static class Palette { public static readonly RgbaColor[] Default = /* 8 colours, D-08 */ null; }
```

### 2.5 Fixtures and golden data (Day 0, small, hand-coded)

`Contracts/Fixtures/GoldenBoards.cs`: `Empty()`, `StarOnFullBoard()` (circle at (0.5,0.5) r=0.35, 8 user snap points, dotted diameters, two solid squares - sketch 3), `Tiled2x2()` (four scaled stars, circle radius 0.25 per cell, diamond tips on edge midpoints - sketch 4). A needs them to render on Day 1, D to serialize, B for golden tests, C for mock screens. Whoever finishes first (B+D during Day 0) writes them; later edits are additive.

### 2.6 Who consumes what, and what they use until the real thing exists

| Consumer | Needs | Day-1 stand-in (owner of stand-in) | Real provider |
|---|---|---|---|
| A (BoardView) | Board data + preview/overlay | `StubEditorSession` + `GoldenBoards` (A) | B `EditorSession` |
| C (UI) | Everything in `IEditorSession`, `IInputRouter`, `ILessonController`, `IBoardPlacement`, `IDesignStore` | `MockEditorSession` (in-memory fake that records calls and flips state; C) + other mocks (C) | B, D, A |
| D (touch, hand) | `IBoardSpace`, `IEditorSession.SetCursor/Pointer` | `FlatBoardSpace` (A writes the first version in Day 0, D uses it), event logger session (D) | A, B |
| D (lesson) | `IEditorSession`, golden boards | `MockEditorSession` driven by scripted commands via B's real command classes (once available, week 1) | B |
| B | `IBoardSpace.PxToBoardUnits` for snap radius | constant | A |
| D (persistence) | `Board` | `GoldenBoards` | - |

### 2.7 Test seams per module (details in each workstream)

B: EditMode tests only. A: `Sandbox_A_AR` (device) and `Sandbox_A_Flat` (Editor, ortho). C: `Sandbox_C_UI` (Editor, Game view at 390x844 / iPhone sim aspect). D: `Sandbox_D_Input` (Editor), `Sandbox_D_Hands` (device or Mac), replay harness (EditMode).

### 2.8 Contract rules and change protocol

* `Contracts` is frozen at the end of Day 0 (tag `contracts-v0`). After that: additive changes only (new members, new events, new optional parameters) via PR labelled `contract`, needing approval from one person in each consuming module and a one-line chat announcement. Renames/removals only at integration checkpoints (section 4) with a migration note.
* Nobody puts logic in `Contracts` beyond trivial struct helpers (Vec2 ops, `Similarity2.Apply`, EventBus, ServiceHub).
* Contract tests: `Contracts.Tests` has one smoke test per fixture (serializable, within board bounds). Compile of all asmdefs is the integration gate in CI.

### 2.9 Day 0 kickoff (together, 2.5-3 hours, before anyone works alone)

| Time | Activity | Output |
|---|---|---|
| 0:00-0:20 | Walk the four sketches together against section 1.4 and 6.2; confirm or change each `D-xx` decision | Decisions signed off in this doc |
| 0:20-1:00 | A creates the Unity project (pinned version/packages/URP), folder skeleton, asmdefs, `.gitignore`, `.gitattributes` (LFS + UnityYAMLMerge), `CODEOWNERS`, PR template; everyone clones, opens, and builds Editor once (others watch/verify, B+C+D in parallel start writing 2.4) | Repo `main` opens in Unity on all machines |
| 1:00-2:00 | Write `Contracts` from 2.4 (B+D: model/commands/snap/tools/persistence; A+C: board/session/input/lesson/events); review each other's half; compile | `GeoART.Contracts` compiles |
| 2:00-2:30 | Mocks + fixtures: `StubEditorSession`, `FlatBoardSpace`, `MockEditorSession` skeleton, `GoldenBoards`; empty prefab shells (`EngineHost`, `BoardRoot`, `UIRoot`, `InputRoot`, `LessonHost`) each with an Installer; empty `Main.unity`, `Flat.unity`, four sandbox scenes | Tag `contracts-v0`; each sandbox scene enters Play without errors |
| 2:30-3:00 | Agree: daily 10-min async standup, sync points (section 4), PR/review rules (section 5), Apple Developer/provisioning plan (who owns which Mac and device), definition of Int-1 | Calendar invites; each person has a task list from section 3 |

Hardware reality check on Day 0: iOS builds need macOS + Xcode + a physical iPhone. A and D must have Mac access from week 1; B and C can work entirely in the Editor on any OS. Share one build Mac or run a "device build" rotation if Macs are limited.

---

## 3. Four workstreams

Load balance (approximate share of total effort): A 25%, B 25% (+ C takes four transform tools from week 3, so B ends lighter), C 25%, D 25%. Hand tracking is the highest-risk item and has an explicit time cap (gates G1/G2, section 4.4).

### 3.1 Workstream A - AR foundation, board anchoring, board rendering, build pipeline

**Scope.** AR session and camera; plane detection (horizontal required, vertical P1); reticle and one-tap board placement; anchoring and drift handling; `IBoardSpace` (ARBoardSpace + FlatBoardSpace); the visual board (canvas frame, dashed cell dividers); rendering of the model (`BoardView`: solid/dotted lines, circles, polygons, snap-point dots, candidates, selection highlight, tool preview, overlay ghosts, thin/medium/thick widths); performance; iOS build, signing, Xcode pipeline; owner of `Main.unity`, `ProjectSettings`, `Packages`, `App/` composition root.

**Owns.** `ARBoard/`, `BoardRender/`, `App/`, `Editor/Build/`, `Prefabs/Board/`, `Scenes/Main.unity`, `Scenes/Sandbox/Sandbox_A_*`, `Mocks/ForBoard/`, `ProjectSettings/`, `Packages/`.

**Task list (in order).**

| # | Task | Notes / acceptance |
|---|---|---|
| A-0 | Day 0: Unity project, packages, URP, asmdefs, ignore/LFS rules, `FlatBoardSpace` | Everyone opens it; flat board space maps mouse to board in Editor |
| A-1 | Signed iPhone "hello AR" build in <= 3 days; document steps; env-var based Team ID; write `Build iOS` menu item and Xcode post-process | **Proposal: free dev builds expire after 7 days; set up signing early.** Calendar reminder to re-sign weekly |
| A-2 | AR session, plane detection, reticle, coach text events (`PlacementState`), tap to place board; board quad with outer frame and dashed cell dividers; `Reset` | S1-03, S1-13, S2-04. Placed board stays put when camera moves; 3 test surfaces |
| A-3 | `BoardView`: render `GoldenBoards.StarOnFullBoard` and `Tiled2x2` from a `StubEditorSession` (meshes batched per layer/style; dotted lines via dash shader/texture; widths thin/medium/thick; circles as 96-segment polylines; polygons; snap dots solid + faint candidates; selection highlight; preview ghosts; overlay ghosts) | S3-01, S3-03, S4-01. Visual diff against sketches 3 and 4 side by side |
| A-4 | `ARBoardSpace`: screen<->board raycast, `PxToBoardUnits`, `Changed` on camera/anchor update; grid dividers follow `GridN` | Touch at screen px maps to correct board point within 2 mm |
| A-5 | Anchoring robustness: ARAnchor on board, tracking-lost handling (`PlacementState.TrackingLost`, freeze input), re-localization; scale of board 40 cm default (D-04) | Walk away and back: drift < 1 cm; message when lost |
| A-6 | Vertical plane (wall) support and board orientation (board +y up on wall, away from user on table) (D-09) | P1: works on a wall, otherwise documented as limitation |
| A-7 | Performance: dirty-rect/dirty-layer mesh rebuild on `BoardChanged` only, preview updates cheap; max 500 shapes (D-16); `FpsSample` publisher; 2-minute median FPS script; Xcode Instruments profile | Median >= 30 FPS on target iPhone with star tiled 2x2 + 4x4 stress board |
| A-8 | Integration duties: wire real prefabs into `Main.unity` (int. points), keep build green weekly, TestFlight/Xcode device build of every integration tag | Build artifact per `int-N` tag |
| A-9 | Support: help D with camera frames for Vision (A exposes `ARCameraManager` CPU image to D via `IHandFrameSource` adapter or D uses `ARCameraManager` directly in the same scene) | Agreed in week 1 |

**Isolated testing.** `Sandbox_A_Flat`: ortho camera + `FlatBoardSpace` + `StubEditorSession` shows the golden boards in Editor (no AR). `Sandbox_A_AR`: device scene: placement + `BoardView` with golden boards + FPS overlay. Neither needs B, C, or D code.

**Deliverables.** Signed build pipeline doc (`docs/BUILD_IOS.md`), `ARBoardSpace`, `BoardRoot.prefab`, `BoardView`, perf report. **Definition of done.** S1-03, S1-13, S2-04, S3-01 (render), S3-03 (render), S4-01 (render) pass; 2-minute median >= 30 FPS; board holds its anchor in the three-surface walkthrough; any teammate can produce a signed device build from `main` using the doc.

### 3.2 Workstream B - Core geometry and drawing engine (plain C#)

**Scope.** Model behaviour, geometry math, snapping, commands/undo-redo, tools (Line, Circle, Stamps, Select/Move, Snap toggle), `EditorSession` facade, layers operations, grid operations including bounded repeat (`FillGrid`), engine unit tests; geometry predicates the lesson needs. From week 3, C implements Rotate/Scale/Duplicate/Erase in `Core/Tools/Transform/` against the same interfaces; B reviews them and supplies the shared helpers.

**Owns.** `Core/` (except `Core/Tools/Transform/` after week 3), `Prefabs/Engine/`, `Tests/EditMode/Core/`, `Scenes/Sandbox/Sandbox_B_*`, `Contracts/Fixtures/GoldenBoards.cs` (after Day 0).

**Task list (in order).**

| # | Task | Notes / acceptance |
|---|---|---|
| B-0 | Day 0: contracts for model/commands/snap/tools/session | Compile |
| B-1 | Geometry helpers: distance to line/circle/polygon, bounds, `Similarity2` application to each shape kind, intersections (line-line, line-circle, circle-circle), regular polygon vertices; epsilon policy (`1e-9` geometry, `1e-5 x cell` seam) | Unit tests for parallel, coincident, tangent, zero-length cases |
| B-2 | `CommandStack` + commands: AddShape, RemoveShape, TransformShapes, AddSnapPoint, RemoveSnapPoint, AddLayer, RemoveLayer, MoveLayer, SetLayerVisible, SetGrid, ClearBoard, plus group macro | `Do/Undo` property test: random sequence then undo all == original board (structural equality) |
| B-3 | `SnapService` (user points, endpoints, centers, circle-division N=8, intersections, cell corners/edge midpoints/centers, board corners; radius; filter by visible layers and exclusions); `EditorSession` (cursor, preview, active tool, quick-menubar actions: PlaceSnapPoint, ClickAtCursor, LineBetweenSnapPoints with latest/previous semantics, ToggleDottedLine, Undo/Redo); Line and Circle tools with min length/radius, click-click and drag, cancel on `Cancel` | S1-07..11, S3-03. Hidden checks: Cancel mid-draw leaves model unchanged; minimum sizes enforced |
| B-4 | Layers: Add (on top, becomes active), Move, SetVisible, `DeleteBottomLayer` (removes shapes + snap points, keeps >= 1 layer, undoable); `SetGrid` 1..4 | S1-04, S1-05 |
| B-5 | Stamps tool (triangle/square/hexagon, default radius 0.25 cell, drag to size); Select/Move (hit-test tolerance via `PxToBoardUnits`, drag with snap of selection key points to other snap candidates, excluding self); Snap toggle; `FitToCell` helper and `ShapeSetBounds` used by Scale; `DuplicateSelection` and `FillGrid` (bounded by `GridN^2`, one undo step) | S4-03: snap-while-drag lands tiles exactly |
| B-6 | Golden tests: build star by driving `EditorSession` with scripted pointer events -> equals `GoldenBoards.StarOnFullBoard`; scale-fit + duplicate -> `Tiled2x2` with seam error < 1e-5 x cell side | Part of "seam match" evaluation target |
| B-7 | Engine performance test: 500 shapes, snap query < 1 ms; no allocation in per-frame `SetCursor`/`Pointer` path | Profiler/test assertions |
| B-8 | Support lesson predicates (`IsRegularNGonInCircle`, `Collinear`, etc.); review C's transform tools; bug fixes from Int 2-5 | |
| B-9 (P2, optional) | Linked repeat mode: ghost copies of the source cell shown in neighbours for seam preview (proposal prose) | Only after Int 4 and only if on schedule |

**Isolated testing.** All in EditMode (`Tests/EditMode/Core`): tests drive `EditorSession` with `PointerEvent` sequences and assert board state. No scene needed. `Sandbox_B_Engine.unity` (optional) uses A's `BoardView` if available, else a Gizmos-based debug drawer in B's own folder.

**Deliverables.** `GeoART.Core` assembly, `EngineHost.prefab` (Installer that builds `EditorSession`, `CommandStack`, `SnapService`, registers `IEditorSession`), tests >= 80% of geometry/commands, README of tool semantics. **Definition of done.** All B-rows pass; every tool cancels cleanly on `Cancel`; undo/redo exact; golden star and 2x2 tiling tests pass; no Unity types referenced.

### 3.3 Workstream C - UI/UX (all sketched screens), transform tools

**Scope.** Every drawn screen/element: header + Settings icon, quick menubar (6 buttons, toggle, pill, layer widget), Tools panel (3x3 with Stamps popover), plus the ASSUMED UI (Settings sheet, lesson coach card, placement coach text, toasts, tool highlight). Safe-area layout, hit targets >= 44 pt, haptics, pixel fidelity to the sketches. From week 3, implements Rotate, Scale, Duplicate, Erase tools (pure C#, `Core/Tools/Transform/`).

**Owns.** `UI/`, `Prefabs/UI/`, `Mocks/ForUI/`, `Scenes/Flat.unity` (the integration scene), `Scenes/Sandbox/Sandbox_C_*`, `Core/Tools/Transform/` (from week 3), `Tests/EditMode/UI/`.

**Task list (in order).**

| # | Task | Notes / acceptance |
|---|---|---|
| C-0 | Day 0: contracts for session/lesson/events; `MockEditorSession`, `MockLessonController`, `MockPlacement`, `MockDesignStore` skeletons | Sandbox_C_UI runs |
| C-1 | UGUI canvas (Screen Space Overlay, reference 390x844 pt, safe-area), theme, header "Geometric ARt" + gear | S1-01, S1-02 |
| C-2 | Quick menubar: 2x3 round buttons with the six glyphs, "Undo"/"Redo" captions, Hand Gestures/Screen toggle, Tools button; Tools panel (title bar + 3x3 grid, exact labels), mode switching (hides HUD in Tools mode), double-tap on the line button (dotted toggle + glyph change) | S1-06..12, S2-01, S2-02, S2-04, S3-03..05. Screenshot compared with sketches 1 and 2 side by side |
| C-3 | Layer widget (vertical segmented bar, "+", "-", labels, tap select, drag-reorder, double-tap segment toggles visibility D-10) and 2x2 pill with picker (1x1..4x4) | S1-04, S1-05, S3-02 |
| C-4 | Wiring to `IEditorSession` only (no direct engine types): button states from `StateChanged`, `CanUndo/CanRedo`, tool highlight, active layer; toasts from `ToastRequested`; placement coach text from `IBoardPlacement.StateChanged` | |
| C-5 | Stamps popover (Triangle/Square/Hexagon, D-11) and "Snap" cell as on/off toggle | S2-03 |
| C-6 | Settings sheet (ASSUMED, D-01): palette (8 swatches), line width (Thin/Medium/Thick), Start Eight-Pointed lesson, Save, Load (list, single-screen), Clear board (confirm), Re-place board, show FPS toggle | All requirement-list items (palette, width, save/reload) reachable |
| C-7 | `Flat.unity` integration scene: real `UIRoot` + `EngineHost` + `InputRoot` + `BoardRoot (flat)`; used for Int 2 | Plays on any OS |
| C-8 | Transform tools in `Core/Tools/Transform/`: Rotate (drag about selection centroid, 15-degree detents when Snap on), Scale (drag/pinch about bbox center, snap-fit to cell side when Snap on), Duplicate (tap = copy to next free cell via B's helper; double tap = FillGrid), Erase (tap shape or snap point; drag-erase) | S4-03; each with EditMode tests |
| C-9 | Lesson coach card UI (title, instruction, hint, Back/Next/Skip/Exit), appears only during lesson (D-18) | Against `MockLessonController` first |
| C-10 | Polish: animations, haptics, VoiceOver labels (optional), dark/light camera backgrounds legibility, 6.1" and 6.7" layouts; UX walkthrough notes for evaluation | |

**Isolated testing.** `Sandbox_C_UI`: UI + `MockEditorSession`; mock flips tool/layers/undo state so every button is exercisable in Editor with the mouse; Game view set to 1170x2532 / 390x844. Mock also simulates `StateChanged`. No B/A/D code needed. EditMode tests for widget state logic (layer widget reorder math, double-tap detector).

**Deliverables.** `UIRoot.prefab`, `Flat.unity`, Settings sheet, transform tools + tests, UI screenshot set for the report. **Definition of done.** Every UI row in 1.4 verified with a side-by-side screenshot; zero references to `GeoART.Core` from `GeoART.UI`; all four transform tools handle `Cancel` and undo.

### 3.4 Workstream D - Input, hand tracking, lesson, persistence, QA/evaluation

**Scope.** Touch input source; input router + mode toggle backend; Apple Vision hand tracking via Objective-C++ bridge, pinch state machine, cursor mapping; button fallback (guaranteed by the buttons already being in C/B); guided Eight-Pointed Star lesson; serializer + design store + autosave; telemetry/eval logger; QA test plan; evaluation plan from the proposal.

**Owns.** `Input/`, `Plugins/iOS/`, `Lesson/`, `Persistence/`, `Telemetry/`, `Prefabs/Input/`, `Prefabs/Lesson/`, `Mocks/ForInput/`, `Tests/EditMode/{Input,Lesson,Persistence}`, `Tests/PlayMode/`, `docs/QA_PLAN.md`, `docs/EVAL_PLAN.md`, `Scenes/Sandbox/Sandbox_D_*`.

**Task list (in order).**

| # | Task | Notes / acceptance |
|---|---|---|
| D-0 | Day 0: contracts for input, lesson, persistence, events | |
| D-1 | `TouchInputSource` (single-touch -> `PointerEvent` via `IBoardSpace.TryScreenToBoard`; ignore touches over UI via EventSystem; two-finger pinch reserved for Scale tool as pinch zoom event; mouse in Editor); `InputRouter` forwarding only the active source into `IEditorSession`; emits `ModeChanged` | Works in `Sandbox_D_Input` with `FlatBoardSpace` + logging session |
| D-2 | `ISerializer` JSON (Newtonsoft; DTO with discriminator for line/circle/polygon; schema version; geometry only); `DesignStore` (persistentDataPath/designs, atomic write, list/load/delete, autosave on `OnApplicationPause`); round-trip tests; schema migration stub | Round-trip golden boards bit-exact in doubles ("R" format) |
| D-3 | Lesson engine: `LessonController` + 7 steps of the Eight-Pointed Star (below), validators (use B's predicates), ghost overlays via `IEditorSession.SetOverlay`, hints, auto-advance on validation, Skip; lesson setup creates layers `Dotted lines` (bottom), `Circle`, `8 pointed Star made w squares` (top) and GridN = 2 | S3-01, S3-02, S4-03. EditMode tests drive a scripted correct and incorrect run |
| D-4 | Vision bridge: `VisionHandBridge.mm` (C API: init, submit downsampled BGRA/YpCbCr frame into an owned buffer + orientation + timestamp, one detection job at a time, poll latest result with joints/confidences/timestamp); Unity wrapper; optional macOS bundle build for Editor testing with webcam | Builds in Xcode; per proposal: owned buffer, single in-flight job |
| D-5 | `HandInputSource`: joint mapping through the display transform/real orientation, cursor smoothing (1-euro or EMA), pinch detector (confidence limits, separate on/off thresholds, debounce, stale timestamp rejection), `Cancel` on tracking lost, resume only after fresh pinch; false-gesture counters; `TrackingState` | Replay tests with recorded joint streams (EditMode) + device trials |
| D-6 | Cursor-vs-fingertip calibration test scene (cursor vs actual fingertip at several screen positions, as per proposal) + 10-trial placement/drag protocol script; decide gate G1/G2 (section 4.4) | 8/10 target |
| D-7 | Telemetry/eval logger: task timestamps, task completion flags, false gestures, tracking-loss events, FPS samples -> CSV in app documents | Matches proposal evaluation metrics |
| D-8 | QA test plan and evaluation protocol docs (below) | |
| D-9 | Integration duties: wire `InputRoot`/`LessonHost` into Main and Flat; run user testing; demo script | |

**Lesson steps (D-3).** All in board space, board center (0.5, 0.5), guide circle radius ~0.35.
1. Circle tool: click the board-center snap point, drag/click radius ~0.35 (validator: center within 0.01 of board center, radius within 0.30-0.42). Layer `Circle`.
2. Place Snap Point on all eight equally spaced circle points (shown as faint candidates). Validator: 8 user snap points within 1e-3 of the 45-degree divisions.
3. Draw the vertical and horizontal diameters (and optionally the diagonals) with the Line tool/button; use double tap for dotted. Layer `Dotted lines` (dotted style). Matches sketch 3's dashed lines.
4. Join alternate points: Line b/w Snap points x4 -> square 1 on layer `8 pointed Star made w squares`.
5. Join the remaining 4 points -> square 2 rotated by 45 degrees.
6. Hide guides: use `-` on the layer widget twice (deletes `Dotted lines` then `Circle`) -> star revealed (step validator: those layers gone/hidden, star shapes intact).
7. Tile (sketch 4): Tools -> Scale to cell size (snap-fit), Duplicate, Select/drag, snap into the 4 cells. Validator: 4 star copies centered at cell centers; seam deviation reported (target < 1e-5 x cell side), shown in the coach card.

**Isolated testing.** `Sandbox_D_Input`: Editor, `FlatBoardSpace`, a logging mock session (prints pointer events); keyboard keys simulate pinch for hand-source plumbing. Hand pipeline: `Sandbox_D_Hands` (device or Mac webcam) with its own minimal ARCameraManager or webcam source, draws a cursor dot + pinch indicator; EditMode replay of recorded `HandFrame` JSON. Lesson: EditMode with `EditorSession` once B's real session exists; before that with D's scripted-command `MockEditorSession` that applies B's command classes (week 1) or simple stubs. Persistence: EditMode round trips.

**Deliverables.** `InputRoot.prefab`, `LessonHost.prefab`, Vision plugin + wrapper, serializer/store, telemetry CSV, QA and evaluation docs. **Definition of done.** Touch mode fully drives every tool; hand marker + pinch pass G1/G2 or the documented fallback is accepted (D-02); lesson completable end to end in both modes; save -> quit -> relaunch -> place new board -> load reproduces the design exactly; evaluation documents ready.

#### QA test plan (D owns, all contribute)

| Layer | What | When |
|---|---|---|
| EditMode unit | geometry, snap, commands, tools, serializer, lesson validators, pinch detector (replay) | CI on every PR |
| Contract/compile | all asmdefs compile; fixtures valid | every PR |
| Flat-scene smoke (Editor) | scripted playthrough: draw line/circle, snap, undo/redo, layers, 2x2, lesson steps 1-7 | before each integration tag |
| Device smoke (iPhone) | place board on 3 surfaces; draw star manually; lesson; save/reload on new board; hand mode toggle; background/foreground | each integration tag |
| Performance | 2-minute median FPS >= 30 with 2x2 star + 4x4 stress; memory; thermal | Int 3, Int 5, freeze |
| Hand input | 10 placement/drag trials; false-gesture counts; tracking-loss recovery; lighting conditions | G1, G2, freeze |
| Regression sketch check | 1.4 table re-verified with screenshots | Int 3, Int 5, freeze |

#### Evaluation plan (from the proposal)

3-5 users; tasks: (1) place the board, (2) build a stamped motif, (3) build the eight-pointed star via the lesson, (4) hide guides, (5) tile/repeat, (6) edit, undo, (7) save and reload. Record via telemetry + observer sheet: task completion, time on task, false gestures, tracking losses. Targets (not promises): 8 of 10 successful placement/drag trials; median >= 30 FPS over two minutes; guided seam positions within 0.00001 x cell side. Run each user in Screen mode and, if G2 passed, in Hand mode. Short post-task questionnaire (ease, clarity of lesson). Document limitations: free designs may have gaps or overlaps.

---

## 4. Dependency / integration timeline

Relative weeks. The proposal gives three milestones and no calendar dates; assumed 10 weeks split roughly 3 / 4 / 3 (D-19). If the course gives different length, scale each phase proportionally and keep integration ordering.

### 4.1 Milestones (aligned with the proposal)

| Milestone | End of | Proposal content | Our acceptance |
|---|---|---|---|
| M1 | Week 3 | signed iPhone build, board placement, working hand marker, basic line/circle tools | Int 3: device build with placed board, hand cursor visible (G1), Line and Circle tools through touch, undo/redo, quick menubar as in sketch 1 |
| M2 | Week 7 | gesture construction, snapping, eight-pointed lesson with connected repeats | Int 5: pinch drawing (or documented fallback), snapping, all 9 tools, layers, lesson 1-7, seam within tolerance, Tools mode (sketches 2/4) |
| M3 | Week 10 | persistence, profiling, user testing, demo prep | Freeze/RC: save/reload on new board, >= 30 FPS evidence, 3-5 user sessions, demo script |

### 4.2 Integration checkpoints (named)

| ID | When | What is merged/proven | Who must sync |
|---|---|---|---|
| Day 0 | Day 1, 3 h | `contracts-v0` + mocks + skeleton | all 4 |
| Int 1 | End W1 | Each module compiles, sandbox runs with mocks, first slices: A signed hello-AR build + golden boards rendered flat; B model/commands/snap/Line/Circle tests green; C quick menubar + Tools panel on mock; D touch source + serializer round trip + Vision bridge compiles. No cross-module runtime yet | all 4, 30 min |
| Int 2 | End W2 | **Real engine plugged into UI + touch input + renderer in `Flat.unity`**: draw lines/circles, place snap points, undo/redo, layers, 2x2 pill in Editor. A's `BoardView` renders live session | B+C+A+D (D touch) |
| Int 3 (=M1) | End W3 | **AR board + device**: `Main.unity` composes the same prefabs; flat code runs unchanged on iPhone with `ARBoardSpace`; hand marker overlay on device | A leads; all 4 |
| Int 4 | End W5 | Full tool set (Stamps, Select/Move, Rotate, Scale, Duplicate, Erase), lesson steps 1-7, Settings sheet, hand pinch drawing in flat/Mac and device, in `Flat.unity` | B+C+D, A for render |
| Int 5 (=M2) | End W7 | Everything on device; G2 decision; seam golden tests; perf pass 1 | all 4 |
| Freeze | End W8 | Feature freeze; only bugs; persistence + autosave integrated (D-2 delivered earlier, hooked into Settings in W3-W6) | all 4 |
| RC | Mid W9 | Release candidate build for user sessions | all 4 |
| Demo | W10 | Demo build + script | all 4 |

Everything else is parallel. Unavoidable syncs: Day 0, Int 1-5, contract-change announcements, and the G1/G2 hand gates.

### 4.3 Gantt-like layout

```
Week:        W1      W2      W3 (M1) W4      W5      W6      W7 (M2) W8      W9      W10 (M3)
Day 0 [all]  X
A  AR/render A0-A1   A2-A3   A4-A5   A5-A6   A7      A7-A8   A8      freeze  bugs    demo build
              signed  place+   spaces+ vertical perf    perf    device  bugs    RC      build
              build   render   anchor  plane    batching profile QA                       
B  Engine    B0-B2   B3-B4   B5      B5-B6   B6-B7   B8      B8/B9   bugs    bugs    docs
              model,  session, stamps, golden  perf    lesson  P2 link  
              cmds    snap/    select  seams    tests   support
              Line/Cir layers
C  UI        C0-C1   C2-C4   C5-C7   C8      C8-C9   C10     polish  UX test bugs    screenshots
              header  menubar, stamps, rotate/ duplic/  polish          sessions
              +gear   tools    flat    scale    erase+   
              quick   panel,   scene,          lesson UI
              menubar layers   settings
D  Input/    D1-D2   D2-D4   D4-D5   D3      D3/D5   D6-D7   G2 + D9 D9      D9      demo script
   Lesson/    touch,  serial., vision  lesson   lesson+ telemetry hand   user    user    
   Persist/   router  vision  bridge  steps    pinch   persist trials  tests   tests
   QA                 bridge  cursor  (replay) draw    hooks           
Integr.      Int1    Int2    Int3=M1 .       Int4    .       Int5=M2 Freeze  RC      Demo
Gates                        G1 hand marker                  G2 pinch 8/10
```

### 4.4 Hand-tracking gates (risk control)

* **G1 (end W3):** hand marker (fingertip cursor) follows the fingertip within ~40 px at 5 screen positions on the physical phone. If not met, D gets 1 extra week; touch + buttons carry the demo regardless.
* **G2 (end W6/7):** >= 8/10 pinch place/drag trials. If met, Hand Gestures mode is the default (toggle knob at left as drawn). If not, the toggle stays visible as drawn, Hand mode is labelled experimental, default input = Screen, and D moves hand-tracking time to lesson/QA (D-02). The proposal explicitly allows this scope hedge.
* Hand work is capped at ~40% of D's time overall.

### 4.5 Dependency map (who waits for whom)

```
Contracts (Day 0) -> everyone starts in parallel.
A BoardView   needs: Board model + fixtures only.            -> no wait
C UI          needs: IEditorSession interface + mock.        -> no wait
D touch/hand  needs: IBoardSpace interface + FlatBoardSpace. -> no wait
B engine      needs: nothing but its own tests.              -> no wait
D lesson      needs: B commands/session (first usable ~mid W2): until then scripted mock; the first real dependency (soft).
C transform tools (W3+) need: B's helpers FitToCell/hit-test (B-1, B-5): soft dependency, B delivers B-1 in W1.
Int 2: B session + C UI + D touch + A BoardView  -> first hard sync.
Int 3: A ARBoardSpace + everything above           -> device.
```

---

## 5. Git, branching, merge strategy (Unity, 4 people)

### 5.1 Repository setup (A on Day 0)

* One Git repo; Unity project at the root. **Force Text** asset serialization, **Visible Meta Files**; commit `.meta` files always.
* `.gitignore`: standard Unity ignore (Library/, Temp/, Obj/, Logs/, UserSettings/, Build/, Builds/, `*.csproj`, `*.sln`, `.vs/`, `.idea/`, `xcuserdata/`, generated Xcode project folder `Builds/iOS/`, `*.keystore` if any, `Assets/Editor/Local/` for per-person signing config). Keep `Plugins/iOS/*.mm|*.h` committed.
* **Git LFS** via `.gitattributes`: `*.png *.jpg *.psd *.fbx *.obj *.wav *.mp3 *.mp4 *.ttf *.otf *.a *.dylib *.bundle *.unitypackage *.exr *.hdr` -> `filter=lfs diff=lfs merge=lfs -text`. (Sketch images from `/tmp/pdfpages` go in `docs/sketches/` via LFS.)
* **UnityYAMLMerge** for YAML assets: in `.gitattributes`: `*.unity merge=unityyamlmerge eol=lf`, same for `*.prefab *.asset *.mat *.controller *.anim`. Per-machine config (document in `docs/GIT_SETUP.md`):
  ```
  git config merge.tool unityyamlmerge
  git config mergetool.unityyamlmerge.trustExitCode false
  git config mergetool.unityyamlmerge.cmd '"<UNITY_PATH>/Data/Tools/UnityYAMLMerge" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
  git config merge.unityyamlmerge.name "Unity SmartMerge"
  git config merge.unityyamlmerge.driver '"<UNITY_PATH>/Data/Tools/UnityYAMLMerge" merge -p %O %B %A %P'
  ```
* `.editorconfig` (LF, UTF-8, 4 spaces), `CODEOWNERS`, PR template with the checklist in 5.4.

### 5.2 Ownership rules (this is what prevents merge conflicts)

1. **Folder ownership is exclusive** (section 2.2). Do not edit a file in someone else's folder except through that owner's PR review (or the owner's explicit OK for a one-line fix).
2. **Scenes:** only A edits `Main.unity`; only C edits `Flat.unity`; each person edits only their own `Sandbox_<X>_*` scenes. Everything shared is a **prefab**, owned by one person (table in 2.2). Scenes contain only prefab instances and no per-module scene objects.
3. **Prefabs are small and additive:** one root per module; sub-structure as nested prefabs (e.g. `UI/QuickMenubar.prefab`, `UI/ToolsPanel.prefab`, `UI/LayerWidget.prefab`, `UI/SettingsSheet.prefab`) so two UI edits rarely touch the same YAML. Prefer code-created hierarchies for dynamic content.
4. **Project-wide files** (`ProjectSettings/*`, `Packages/manifest.json`, Tags & Layers, Quality, Player/XR settings) belong to A; others request changes in chat/PR. Tags/layers/sorting layers are defined on Day 0.
5. **Contracts**: protocol in 2.8. **Assembly definitions** are never shared between owners (no one edits another's `.asmdef`; to add a reference, ask the owner).
6. No binary/auto-generated churn in PRs: no reformatting `.meta`/scenes, no Library, no Xcode output.

### 5.3 Branching

* Trunk-based: `main` is always green (opens in Unity, EditMode tests pass, `Flat.unity` plays). Short-lived feature branches `feat/<area>-<topic>` (e.g. `feat/core-snap-service`), `fix/...`, `chore/...`; target < 2 days, rebase on `main` daily.
* Squash-merge; commit message `area: summary` (areas: `core`, `ar`, `render`, `ui`, `input`, `hands`, `lesson`, `persist`, `contracts`, `build`, `docs`).
* Integration tags `contracts-v0`, `int-1` ... `int-5`, `freeze`, `rc`, `demo`; each tag gets a device build by A.
* If a module is not ready for others, ship it behind its own prefab/installer (default off in Main/Flat) rather than keeping a long branch.

### 5.4 Review rules

* Every PR needs 1 approval from a person other than the author; PRs touching another person's folder need that owner's approval; `contract`-labelled PRs need one approval per consumer module (section 2.8).
* PR checklist: compiles; EditMode tests pass; no scene/prefab changes outside your ownership; new UI strings/elements are in 1.4 or 6.2; screenshots for UI/render changes; no `Debug.Log` spam in hot paths.
* Review SLA: respond within the same day; PRs under ~400 lines changed.
* Pre-merge automation: GitHub Actions + GameCI (`unity-test-runner` EditMode, needs a Unity licence) or a local `./tools/check.sh` running `Unity -batchmode -runTests -testPlatform EditMode` if CI licensing is not available.

### 5.5 Naming conventions

* C#: namespaces `GeoART.<Module>`; types `PascalCase`; interfaces `IThing`; private fields `_camelCase`; one public type per file, file = type name; installers `XxxInstaller`; commands `XxxCommand`; tools `XxxTool`; mocks `MockXxx`.
* Assets: `Prefabs/<Owner area>/Name.prefab`, scenes `Sandbox_<Letter>_<Topic>.unity`, UI sprites `ui_<screen>_<name>.png`, materials `mat_<name>`, shaders `GeoART/<Name>`.
* Coordinates in code: variables ending `...Board`, `...Px`, `...World` for the three spaces; never mix.

---

## 6. Risks, mitigations, assumptions

### 6.1 Risks

| Risk | Likelihood / impact | Mitigation |
|---|---|---|
| **Hand tracking** (Vision latency, jitter, orientation mapping, false pinches; "the biggest risk" in the proposal) | High / High | Touch + buttons first-class from Day 1; hand source behind `IInputSource`; replay tests; macOS webcam testing; G1/G2 gates; time cap; toggle stays as drawn either way (D-02) |
| Signing/provisioning (free profile expires in 7 days; Xcode problems late) | Medium / High | A-1 in week 1; weekly re-sign reminder; env-var Team ID per developer; one documented build path; keep two devices if possible |
| Only some teammates have a Mac/iPhone | Medium / High | Rotate "device builds" through A/D; B and C work Editor-only; build every tag |
| **AR drift / tracking loss** moves the board under the user | Medium / Medium | ARAnchor on the board; textured-surface guidance text; `TrackingLost` state pauses input (cancels unfinished shapes); re-place button in Settings; test on 3 surfaces |
| Wall support (sketch says wall) harder than table | Medium / Low | Table is the MVP; vertical plane is P1 (A-6) and documented |
| **Performance** < 30 FPS (many meshes, Vision frame copies, thick lines) | Medium / High | Batched meshes per layer, dirty rebuild only, shape cap 500, grid cap 4x4; downsample Vision frames, one job at a time; profile at Int 3 and Int 5, not only at the end |
| **Merge conflicts** in scenes/prefabs | Medium / Medium | Section 5.2: exclusive ownership, prefab nesting, YAML merge, A alone edits Main |
| Contract churn after Day 0 | Medium / High | Freeze + additive-only rule; review by consumers; fixtures and contract tests |
| Ambiguity in sketches vs prose (tiling model, "click", Settings) | High / Medium | Explicit decisions D-xx below; Day 0 sign-off; fidelity checklist 1.4 |
| Engine edge cases (tangent/parallel/zero-length, snap flicker) | Medium / Medium | B-1 unit tests, minimum size rules, hysteresis in snap |
| B is on the critical path (everything needs a working session by Int 2) | Medium / High | Mocks keep others unblocked; B delivers `EditorSession` with Line/Circle/Undo by end of W2 (Int 2); C takes transform tools to relieve B |
| Scope creep (radial helper, connectors, linked repeat) | Medium / Medium | Marked out/P2 in 1.2; stretch only after Freeze criteria are met |
| User testing too late | Medium / Medium | Informal internal UX runs W6-W7; evaluation protocol written in W3; RC in mid W9 |

### 6.2 Assumptions and decisions (not in the sketches, or interpretations)

Everything here is ASSUMED unless noted; confirm or change on Day 0. Sketched elements are never altered by these.

| ID | Topic | Decision |
|---|---|---|
| D-01 | Settings (gear) content | Single sheet: palette (8 colours), line width (Thin/Medium/Thick), Start Eight-Pointed lesson, Save, Load, Clear board (confirm), Re-place board, Show FPS. No other new screens. |
| D-02 | Hand/Screen default | Toggle drawn as sketch (knob left = Hand Gestures). Default value = Hand only if gate G2 passes; otherwise default Screen and Hand labelled experimental. Toggle is always present. |
| D-03 | Screen-mode input | Touch on board = pointer events, cursor = last touch; buttons act at cursor. No finger offset in MVP (configurable constant). |
| D-04 | Board size and look | 40 cm square drawing area, thin canvas frame outside it (sketch 1 shows an outer and inner quad), dashed dividers per cell. |
| D-05 | Tools mode navigation | Tools panel replaces header/layers/pill/toggle/buttons (sketches 2/4); tap tool = select and stay; tap "Tools" title bar = back. Undo/Redo only from the quick menubar. |
| D-06 | "Snap" tool cell | Toggles magnetic snapping on/off (default on). |
| D-07 | Grid sizes | 1x1 to 4x4 square grids (cap 4 for performance); pill shows "NxN"; default 2x2. |
| D-08 | Palette | 8 default swatches (black, white, red, orange, yellow, green, blue, purple); default colour black, default width Medium; sketch 4's heavy strokes = Thick. |
| D-09 | Wall vs table | Table (horizontal plane) mandatory; wall (vertical plane) P1. |
| D-10 | Layer widget | Segments = layers (bottom-to-top), tap = active, drag = reorder ("Arrange"), double-tap = show/hide, "+" adds top layer, "-" deletes the bottom layer with its content and snap points (min 1 layer, undoable). New content goes on the active layer. |
| D-11 | Stamps options | Triangle, Square, Hexagon in a popover; stamps take the current palette colour/width; no other stamp types. |
| D-12 | Sketch 2 diagonal double line | Interpreted as the table edge in the camera view; not an app element. |
| D-13 | Header presence | Shown in quick-menubar mode (sketch 1); sketch 3 omits it (not read as intentional). |
| D-14 | Duplicate semantics | Tap = copy of the selection offset to the next free cell (snapped); double tap = fill all cells (bounded N^2, one undo). Dragging copies afterwards uses Select/Move + snap (sketch 4). This reconciles prose "repeat across grid" with sketch 4's manual tiling without adding UI. |
| D-15 | Save/reload UI | In Settings; 1 autosave + named designs; load always rebuilds the design on the currently placed board (prompts to place if none). |
| D-16 | Caps | Max 500 shapes, 4x4 grid; beyond that show a toast and block adds. |
| D-17 | Connectors | Not built. The proposal's "connected repeats" is achieved in the guided star by Scale-fit so diamond tips land on cell-edge midpoints (visible in sketch 4). Auto connector segments are stretch. |
| D-18 | Lesson UI | Coach card (title, instruction, hint, Back/Next/Skip/Exit) at the top of the viewport only while the lesson runs; started from Settings. Lesson builds the sketch-3 figure on the full board, then step 7 scales/tiles into sketch 4. Free Design is simply "no lesson". |
| D-19 | Time frame | 10 weeks, M1 end W3, M2 end W7, M3 end W10 (scale if the real course schedule differs). |
| D-20 | "click" vs double ring | One button (the sketch-1 "click" word is drawn on the ring in sketch 3's slot). It commits the active tool; Circle is chosen in the Tools grid. |
| D-21 | Placement UX | Launch shows scanning coach text; reticle on a detected plane; tap places; no confirm step; Settings > Re-place board resets. |
| D-22 | Rotate/Scale feel | Rotate: 15-degree detents when Snap is on; Scale: uniform, snap-fits the selection's bbox to the cell side when Snap is on. |
| D-23 | Line b/w Snap points | Joins latest and previous snap points; placing a snap point on an existing point re-selects it as latest, which lets users close polygons (needed for lesson steps 4-5). |
| D-24 | Radial 4/8 helper | Out of MVP (not in required list, not sketched). |
| D-25 | Stroke widths | Thin / Medium / Thick presets, board-relative; sketch-4 heavy strokes = Thick. |
| D-26 | Export/share | Not in MVP. |

---

## 7. Verification note (plan vs proposal)

Cross-checked against `PROPOSAL_SUMMARY.md` and the page renders: all 4 sketches mapped (section 1.4); all required-feature list items covered (one board, stamps/lines/circles, palette + width, pinch + touch, snapping + guide layer, guided lesson, bounded square-grid repetition, undo, save/reload); the three proposal milestones are the three milestones in 4.1; the stretch and out-of-scope lists are in 1.2; the hedge that hand gestures may be dropped is operationalised by G1/G2 and D-02.
