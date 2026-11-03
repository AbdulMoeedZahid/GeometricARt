# Snap Points and H/V Lock: Implementation Plan

**Status:** Planned. Created 2026-11-01, revised 2026-11-03 (scope, Android, then rebased on `grid-and-android`). Nothing below is implemented yet.
**Baseline:** branch `grid-and-android` at `9270ba3` (Ratul, 2026-11-02: square/hex `TileGrid`, ToolBar grid buttons, Android/ARCore build support verified on a Galaxy S24 Ultra), which sits on `main` at `d625c26`. Build on that branch, not on `main`: it already changes `ToolBar`, `BoardView`, `IEditorSession`, `EditorSession`, `StubEditorSession` and project settings, so starting from `main` guarantees conflicts.
**Plan owner:** Luis. Written for an orchestrator agent (Claude Code) that hands tickets to subagents, with one agent driving the Unity Editor through a Unity MCP server.

---

## 1. The request

Sources (team Discord):
- Haytham, 2026-10-30: snap points wherever a line intersects a circle or two lines intersect, added automatically, easy to select and connect via a line; a tool to keep a line horizontal or vertical relative to the plane; later, scale and duplicate of a selected pattern.
- Teammate, 2026-11-02: snap points must also be present at **grid intersections**.
- Haytham, 2026-11-03: **Select and Duplicate are stretch goals for P3.** Snap points done is enough for him to complete the demo.
- Haytham: **Claude must write a report detailing what changed**, so that later upgrades or changes can be made with an understanding of the code.

Scope for this task:

| # | Deliverable | Priority |
|---|---|---|
| F1 | Automatic snap candidates: line/line, line/circle, circle/circle intersections; **grid intersections** (tile corners, square and hex); endpoints, centers, circle divisions, user points, tile edge midpoints and centers | **Demo-critical** |
| F2 | Line and Circle snap to candidates; candidates are visible dots; tap a dot then another dot to connect them with a line (drag still works); Snap on/off toggle | **Demo-critical** |
| R1 | Change report: `docs/SNAP_CHANGE_REPORT.md` (section 3.6) | **Required** |
| A0 | Android: **already done** by Ratul on `grid-and-android` (`docs/BUILD_ANDROID.md`). Device verification by teammates Ratul (Galaxy S24 Ultra) or Haytham; Luis's phone lacks ARCore (Q12) | Verify only, teammates |
| F3 | H/V lock: constrain new lines to horizontal or vertical in board axes, with alignment snapping along the axis | Requested, after F1/F2/R1 pass |
| P3 | Select/Move, Scale, Duplicate, Undo | **Deferred to P3** (appendix A keeps the design) |

Order of importance: F1 + F2 working in the Editor, then R1, then F3. If time runs short, cut F3, never R1.

## 2. How the project works today (read this instead of the code)

**Stack:** Unity 6000.5.10f1, URP 17.5, AR Foundation 6.5 with ARKit (iOS) and ARCore (Android) providers, Input System 1.20, uGUI built at runtime. Teammates build for iPhone on a Mac. **Luis works on Windows and tests on an Android phone; he does no Apple/iOS work.** All runtime code uses cross-platform AR Foundation APIs. Android setup and build: `docs/BUILD_ANDROID.md` (menus **GeoART > Setup > 3. Configure Android AR Project** and **GeoART > Build > Build and Run on Android Phone**). Android renders with Vulkan first + GLES3 fallback and needs `ARCommandBufferSupportRendererFeature` on the URP renderers (already added).

**Assemblies (`Assets/GeoART/`):**

| Assembly | Role | Unity types? |
|---|---|---|
| `Contracts` | Model (`Board` incl. `GridN` + `GridKind` Square/Hex, `TileGrid` = pure grid geometry with `Cells`, `UniqueEdges`, `SnapPoints`, `Layer`, `Shape`: `LineShape`/`CircleShape`/`PolygonShape`, `SnapPoint`), `Vec2` (doubles), `IEditorSession`, `IBoardSpace`, `ServiceHub`, `ToolTypes` (`ToolId`, `PointerEvent`, `SnapKind`, `SnapCandidate`, `Selection`), `GoldenBoards` fixtures | No |
| `Engine` | `EditorSession` (partial class: `.cs` core, `.Layers.cs`, `.Tools.cs`), `OneEuroFilter2D` | No |
| `App` | `BoardInputRouter` (touch/mouse to `IEditorSession.Pointer`, smoothing, UI exclusion, cancel rules), `ToolBar` (right-edge column, 6 buttons: Line, Circle, Dotted, Width, grid kind `Square`/`Hex`, tiles `2x2`), `LayerSidebar`, `ToastView`, `EngineInstaller`, `DevHud`, `PlacementControls` | Yes |
| `BoardRender` | `BoardView` (base frame/grid, shapes, overlay = snap candidate dots + preview ghosts + cursor), `BoardMeshBuilder`, corner placement | Yes |
| `ARBoard` | `ARBoardSpace` (`IBoardSpace` on device) | Yes |
| `Mocks` | `FlatBoardSpace`, `StubEditorSession` | Yes |

**Coordinates:** board space is `[0,1] x [0,1]`, origin bottom-left, +X right, +Y toward the board top, square board, `GridN` in 1..4 (default 2), `GridKind` Square (cell side `1/GridN`) or Hex (`GridN x GridN` whole pointy-top hexagons, centred with margins; see `docs/GRID.md`). `IBoardSpace.TryBoardToScreen` gives perspective-correct screen pixels; use it for every tolerance.

**Data flow:** `BoardInputRouter` -> `IEditorSession.Pointer(Down/Move/Up/Cancel)` with filtered board positions -> `EditorSession` mutates `Board` -> `BoardChanged` -> `BoardView` rebuilds shapes. Ghosts and cursor go through `Preview`/`Cursor` + `PreviewChanged` (overlay only, never in the model).

**What exists vs. what is stubbed (at `d625c26`):**
- Works: Line and Circle drag tools, dotted toggle, width cycle, layers (add/select/move/delete bottom/visibility/opacity), min stroke length 0.01, gesture cancel rules, board edge clamping, One Euro smoothing.
- Stubbed: `SnapEnabled` is hard `false`, `SnapCandidates` always empty, `PlaceSnapPoint`/`ClickAtCursor`/`LineBetweenSnapPoints`/`SetStamp`/`SetSnapEnabled` are no-ops, `Undo`/`Redo` empty, `SetTool` toasts "Coming soon" for anything but Line/Circle.
- Already wired for this work: `BoardView.RebuildOverlay` draws a disc per `SnapCandidate` when `SnapEnabled`; `Layer.SnapPoints` exists; **`TileGrid.SnapPoints(kind, n, list)` already returns every tile corner (`CellCorner`), edge midpoint (`CellEdgeMid`) and centre (`CellCenter`), deduplicated, for square and hex**. `StubEditorSession` already uses it.

**Dev scene:** `Assets/Scenes/Sandbox/Sandbox_A_Flat.unity` has the real `EngineInstaller`, `ToolBar` and `BoardInputRouter` on a `FlatBoardSpace`, and starts on an empty board. Mouse input drives the real engine with no AR and no phone. All Editor verification happens here.

**Checks available:**
- `tools/enginecheck`: .NET 8 console harness compiling Contracts + Engine directly. Every `public static void` in a `*Checks` class runs. No Unity needed. Run: `dotnet run --project tools/enginecheck -c Release`.
- EditMode tests (`Assets/Tests/EditMode/...`) and PlayMode tests (`Assets/Tests/PlayMode/Verifier/VerifierInputRouterPlayTests.cs`).
- `GeoART.EditorTools.ToolsRenderCheck.RunBatch` writes PNGs of a test board to `Logs/GeoARtRenderCheck/` (needs graphics, not `-nographics`). It compiles the shader for Metal only; optional extra: also compile for Vulkan/GLES3.
- `GeoART.EditorTools.PlayerScriptCheck.CompileIOSBatch` compiles iOS player scripts without a build. It needs iOS Build Support, so on Luis's machine it is **not** run; the Mac teammate runs it before anything merges to `main`. There is no Android script check yet; for Luis the Android build itself (GeoART > Build) is the device compile check.

**Things that break when contracts change (grep before merging):**
- Every `IEditorSession` implementer: `Engine/EditorSession`, `Mocks/ForBoard/StubEditorSession.cs`, the wrapper session in `Tests/PlayMode/Verifier/VerifierInputRouterPlayTests.cs` (~line 390), and any other `: IEditorSession`.
- `tools/enginecheck/ToolChecks.cs`: `DefaultToolIsLine` asserts snapping off and no candidates.
- `tools/enginecheck/GridChecks.cs` (9 grid checks) must keep passing.
- `VerifierToolBarToastTests` also has `ToolBarGridButtonsSwitchKindAndCycleTiles`; it finds the grid buttons by the names `Square` and `2x2`.
- `Tests/EditMode/Verifier/VerifierToolBarToastTests.cs` finds ToolBar buttons by GameObject name ("Line", "Circle"). Keep those names.

## 3. Design

### 3.1 Contract changes (small, append-only)

1. `SnapKind`: append `Intersection` **at the end** (ordinals must not change). Grid intersections reuse the existing `CellCorner` kind, which `TileGrid.SnapPoints` already emits.
2. New `Contracts/Session/ISnapMetric.cs`:
   ```csharp
   public interface ISnapMetric
   {
       /// Board -> screen pixels, perspective-correct. False if not projectable.
       bool TryToScreen(Vec2 board, out Vec2 screenPx);
       /// Tolerance radius in screen pixels for snapping and tap detection.
       double TolerancePx { get; }
   }
   ```
   `EditorSession` gets a concrete (not interface) property `public ISnapMetric SnapMetric { get; set; }`. When null (enginecheck, flat tests), the engine uses a board-unit fallback: tolerance `0.02` board units with identity projection. The App sets the real metric. This keeps Engine Unity-free and avoids an `IEditorSession` change for wiring.
3. `IEditorSession` additions (update every implementer in the same commit):
   ```csharp
   bool OrthoLock { get; }
   void SetOrthoLock(bool on);
   ```
   `SetSnapEnabled`, `SnapEnabled`, `SnapCandidates` already exist; implement them, do not re-declare. No selection/duplicate members in this task (P3).
4. No new render contract. Hover feedback goes through `Preview` (small `CircleShape` ring); candidate dots already render from `SnapCandidates`.

### 3.2 Snap candidates (F1)

Built by a pure `SnapIndex`. Sources and priority (high to low, used for ties and dedup):

1. `UserPoint` (`Layer.SnapPoints`, visible layers)
2. `Endpoint` (line ends, polygon vertices)
3. `Intersection` (shape/shape crossings, visible layers)
4. `CellCorner` = **grid intersections**: every tile corner from `TileGrid.SnapPoints(board.GridKind, board.GridN, list)`. Square: all `(i/N, j/N)`, so `(N+1)^2` points (9 on the default 2x2, including board corners and where dividers meet the frame). Hex: every hexagon vertex, shared vertices once. Present on an empty board, independent of layers, regenerated on `BoardChanged(Grid | Reset)`. Do not re-derive grid geometry: always call `TileGrid`.
5. `Center` (circle and polygon centers)
6. `CircleDivision` (8 per circle at angle `k*pi/4`, needed for the star)
7. `CellEdgeMid`, `CellCenter` (also from `TileGrid.SnapPoints`)

Intersections: segment/segment (endpoint touches count; parallel and collinear give none), segment/circle (0, 1 tangent, or 2), circle/circle (0, 1, or 2; concentric gives none). Polygon edges act as segments. Discard non-finite points and points outside `[0,1]^2` (with 1e-9 slack).

Not included by default: crossings of drawn shapes with grid dividers. Leave a single flag (`IncludeShapeGridCrossings`, default false) so it is a one-line change if the team wants it.

Dedup: points within `1e-7` board units merge; the higher-priority kind wins. Cache: rebuild lazily when the board, layer visibility, grid size or grid kind changes (dirty flag), never per pointer Move. 300 shapes = 45k pair tests per rebuild, which is fine.

`SnapEnabled` defaults to **true**. When false, `SnapCandidates` is empty and nothing snaps.

### 3.3 Snapping rule and tap-to-connect (F2)

`Snap(p)`: project `p` and every candidate with `SnapMetric`; take the nearest candidate within `TolerancePx`; ties go by priority, then candidate order (deterministic). Applied to Line start (Down), Line end (Move/Up), Circle center (Down), Circle radius point (Move/Up). The cursor shows the snapped point; a hover ring (preview `CircleShape`, radius about 0.012, Thin) marks the candidate in use. App tolerance: 24 reference px (390x844 reference) scaled by the canvas scale factor.

**Tap-to-connect:** in the Line tool, a Down+Up whose screen movement is under 10 reference px is a **tap**. Tap on a candidate with no pending start: set pending start (ring stays). Tap on a different candidate with a pending start: commit a line between the two exact candidate positions and clear pending. Tap off any candidate, a drag, Cancel, tool switch, or board replace: clear pending. A tap never commits a zero-length shape. Drag-to-draw is unchanged apart from snapping.

Snapped coordinates must be **exactly** the candidate coordinates (`==`, not near). Pattern seams depend on this.

### 3.4 H/V lock (F3)

- Toggle `OrthoLock` (default off). Line tool only.
- End point = anchor plus the drag projected on the dominant axis (`|dx| >= |dy|` -> horizontal, else vertical), clamped to the board.
- Alignment snap along the axis, nearest within tolerance wins: (a) candidates whose perpendicular screen distance to the axis line is within tolerance contribute their along-axis coordinate; (b) intersections of the axis line with visible shapes are exact candidates. The start point snaps normally.
- "Relative to a plane" = the board plane's X/Y axes (decided Q1). They stay fixed to the real surface regardless of camera angle.

### 3.5 Rendering

- Candidate dots readable and tappable: slightly larger than now, tinted by kind (grid intersections subtle, shape intersections and endpoints stronger).
- Hover ring and pending tap-to-connect start ring come from `Preview`.

### 3.6 Change report (R1)

`docs/SNAP_CHANGE_REPORT.md`, written by the agents and finalized by the orchestrator. Audience: a teammate (or a future Claude session) who needs to upgrade or change snapping without re-reading everything. It must match the merged code, not this plan; where the code differs from the plan, the report follows the code and says so.

Required sections:
1. **Summary:** what features now exist, in a few sentences, with the final commit SHA(s).
2. **Files changed:** every added/modified file with a one-line reason, grouped by assembly.
3. **Contract changes:** exact new/changed public members (`SnapKind.Intersection`, `ISnapMetric`, `IEditorSession` additions, `EditorSession.SnapMetric`) and which implementers were updated.
4. **How it works:** data flow from pointer to snapped commit; where `SnapIndex` is rebuilt and what invalidates it; candidate kinds and priority; tolerance math (screen space via `ISnapMetric`, fallback in board units); tap-to-connect state machine; H/V lock algorithm.
5. **Invariants:** exact coordinates on snap, no per-frame allocations in the Move path, hidden layers contribute nothing, grid intersections always present for square and hex, grid geometry only from `TileGrid`, deterministic tie-breaks.
6. **Tuning knobs:** every constant (tolerance px, tap threshold, dedup epsilon, hover ring size, dot sizes) with file and member name.
7. **How to extend:** steps for adding a new candidate source, a new `SnapKind`, a new intersection type, and how Select/Move/Scale (P3) should reuse `SnapIndex` and `ISnapMetric` (point at appendix A).
8. **Tests:** enginecheck checks, EditMode/PlayMode tests, render-check scenarios, and how to run each on Windows (Luis) and on the Mac (teammates).
9. **Known limitations and untested areas:** especially anything verified only in the Editor, or on only one of Android and iPhone. For this task it must say **"Editor-verified only"** and list the device checks still owed (touch tolerance feel, toolbar fit on hardware, frame rate with candidates) and who owns them (Ratul or Haytham on Android, the Mac teammate on iPhone).

Each ticket's agent appends its subsection to sections 2, 3, 4, 6 and 8 as part of "done". The orchestrator does a final consistency pass against `git diff <baseline>..HEAD` before handing back.

## 4. Work breakdown (tickets)

Each ticket lists owned files. A subagent may only edit its owned files, new check files, and its subsection of the report; anything else is a coordination request back to the orchestrator.

| Ticket | Title | Owns | Depends on | Unity? |
|---|---|---|---|---|
| C0 | Contract changes | `Contracts/Tools/ToolTypes.cs` (append `Intersection`), new `Contracts/Session/ISnapMetric.cs`, `Contracts/Session/IEditorSession.cs`, all `IEditorSession` implementers (stub members only), `ToolChecks` default-snap assertion, report skeleton | none | compile check only |
| E1 | Geometry + SnapIndex (incl. grid intersections) | new `Engine/Geometry/Intersect.cs`, new `Engine/Snap/SnapIndex.cs`, new `tools/enginecheck/SnapChecks.cs` | C0 | no |
| E2 | Snapping in Line/Circle + tap-to-connect + Snap toggle | `Engine/EditorSession.Tools.cs`, new `Engine/EditorSession.Snap.cs`, new `tools/enginecheck/SnapToolChecks.cs`, new `tools/enginecheck/PatternChecks.cs` | E1 | no |
| E3 | H/V lock | `Engine/EditorSession.Tools.cs` (after E2 merges), new `tools/enginecheck/OrthoChecks.cs` | E2 | no |
| U1 | Screen snap metric wiring | new `App/ScreenSnapMetric.cs`, `App/EngineInstaller.cs` | C0 | yes |
| U2 | ToolBar: Snap toggle, H/V toggle (column grows from 6 to 8) | `App/ToolBar.cs`, `Tests/EditMode/Verifier/VerifierToolBarToastTests.cs` | C0 | yes |
| U3 | Overlay: candidate dots by kind, hover ring | `BoardRender/BoardView.cs` | C0 | yes |
| U4 | PlayMode + render verification | `Tests/PlayMode/Verifier/*`, `Editor/Build/ToolsRenderCheck.cs` | U1-U3, E2, E3 | yes |
| A0 | Android device verification only, by Ratul or Haytham (Q12) | nothing; report findings | U4 | yes |
| D1 | User docs | `docs/TOOLS.md` (replace "Snap points come later"), new `docs/SNAPPING.md` | as features land | no |
| R1 | Change report final pass | `docs/SNAP_CHANGE_REPORT.md` | everything | no |

`EditorSession.cs` (shared core) changes only in C0. Other tickets add partial-class files.

### Ticket acceptance

**C0.** Append `SnapKind.Intersection`. Add `ISnapMetric`. Add `OrthoLock`/`SetOrthoLock` to `IEditorSession` and stub them in every implementer. Add `SnapMetric` and a `SnapEnabled` backing field (default true) to `EditorSession`; candidates stay empty until E1. Update `ToolChecks.DefaultToolIsLine`. Create `docs/SNAP_CHANGE_REPORT.md` with the 9 section headings. Done when enginecheck passes and Unity compiles with zero console errors (including the existing `GridChecks`).

**E1.** Pure static functions with doubles. Checks (minimum): perpendicular crossing at a known point; T-junction endpoint touch; parallel and collinear give none; segment through circle gives 2 exact points; tangent gives 1; segment inside circle gives 0; circle/circle 0/1/2 and concentric; **an empty square 2x2 board has exactly 9 `CellCorner` candidates at `(i/2, j/2)`; `GridN` 3 gives 16; `GridN` 1 gives 4; a hex board's `CellCorner` set equals `TileGrid.SnapPoints`' corners exactly; switching `GridKind` rebuilds the set**; the star fixture (`GoldenBoards.StarOnFullBoard`) yields the 8 division points and the 8 inner star vertices (square/square crossings); a hidden layer contributes nothing; dedup keeps the highest-priority kind (a line endpoint on a grid point reports `Endpoint`).

**E2.** Checks: a drag ending within tolerance commits the candidate's exact coordinates; outside tolerance commits the raw point; Snap off commits raw; deterministic tie-break; snapping to a grid intersection on an empty board, square and hex; tap-tap on two candidates commits one line and clears pending; tap on empty clears pending; Cancel and tool switch clear pending; candidates refresh after a commit (a new line creates new intersections). Use a fake `ISnapMetric` (scale + offset, and one non-uniform perspective-like mapping) to prove tolerances are in screen space.
**Golden test `SnapBuildsTiled2x2`** (in `PatternChecks`): using only pointer events on a 2x2 grid, for each of the 4 cells draw a circle centered on the snapped cell center with radius snapped to that cell's edge midpoint (r = 0.25), then connect the 8 division points as two squares with tap-to-connect. The 32 star lines must equal `GoldenBoards.Tiled2x2()` coordinates exactly (order-independent). This is the demo path without Select/Duplicate.

**E3.** Checks: a diagonal drag with lock commits an exactly horizontal or vertical line (`A.Y == B.Y` or `A.X == B.X`); dominant-axis choice; alignment snap to an off-axis candidate; axis/circle intersection snap; ending on a grid intersection; lock off restores free lines; Circle tool unaffected.

**U1.** `ScreenSnapMetric : ISnapMetric` wraps `IBoardSpace.TryBoardToScreen`; `TolerancePx` = 24 reference px times the canvas scale factor used by ToolBar; set on the `EditorSession` instance in `EngineInstaller` after both services resolve. Works with `FlatBoardSpace`.

**U2.** Add `Snap` and `HV` toggle buttons (procedural glyphs, orange when on) as a new group. Keep the `Line`/`Circle`/`Square`/`2x2` GameObject names. Eight buttons (~530 reference px) must fit the safe area at 390x844 and on a tall Android screen; shrink `ButtonH` if needed. Check fit with a screenshot. Update `VerifierToolBarToastTests`.

**U3.** Dots per 3.5; the overlay refreshes on `BoardChanged`, `StateChanged`, `PreviewChanged` (already handled).

**U4.** Extend `ToolsRenderCheck` with a board showing grid intersections, shape intersections and a hover ring; inspect the PNGs. Add PlayMode tests on the flat sandbox driving `BoardInputRouter` with simulated mouse input (follow `VerifierInputRouterPlayTests`): drag-snap a line to a shape intersection; drag-snap to a grid intersection; tap-tap connect; H/V lock. Run all EditMode and PlayMode tests.
Because there is no device run on Luis's machine (Q12), also: in `Sandbox_A_Flat`, verify snapping and tap-to-connect with the camera tilted ~60 degrees and at the far board edge; use the Device Simulator to screenshot the ToolBar and layer sidebar on a small Android phone, a tall Android phone and an iPhone, confirming all 8 buttons fit inside the safe area.

**A0.** Owned by Ratul (Galaxy S24 Ultra) or Haytham, not Luis (Q12). Verify only, no code: after U4, follow `docs/BUILD_ANDROID.md` on Windows (adb lives under `<Unity install>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools`, not the macOS path in that doc). Run GeoART > Setup > 3, then GeoART > Build > Build and Run on Android Phone. Never commit the `preloadedAssets` change the Android build writes to `ProjectSettings.asset`. If Windows steps differ from the doc, append a Windows section to `BUILD_ANDROID.md`.

**D1.** Gestures, defaults, tolerances, and checks in the style of `LAYERS.md`.

**R1.** Section 3.6. Final pass against the actual diff.

## 5. Orchestration

### 5.1 Dependency graph and waves

```
C0 ──> E1 ──> E2 ──> E3
 ├──> U1 ─┐
 ├──> U2 ─┼──> U4 (after E2, E3)
 └──> U3 ─┘
D1 and the report grow with each merge; R1 is the last step; A0 (Android device run by Ratul or Haytham, Q12) after U4.
```

- Wave 1 (serial): C0, by the orchestrator or one agent, merged before anything else.
- Wave 2 (parallel): E1 (engine agent, worktree) and the Unity agent in the main tree doing U1+U2+U3 against stubbed engine behaviour.
- Wave 3: E2 (engine agent). **Demo checkpoint:** after E2 + U1-U3 merge, play `Sandbox_A_Flat` and confirm snapping, grid-intersection snapping and tap-to-connect work. Stop and report to Luis.
- Wave 4: E3, then U4.
- Wave 5: D1 + R1 final pass.

### 5.2 Rules for subagents

- **Engine agents never open Unity.** They work in git worktrees and verify only with enginecheck.
- **Only one agent drives Unity at a time**, in the main working tree (a Unity worktree needs its own multi-GB `Library` import). Unity agents get engine work by merging finished E-tickets.
- Each subagent prompt includes: section 2, the ticket's section 3 design, its owned files, its acceptance checks, "do not edit files outside your ownership; report needed changes instead", and "append your subsection to `docs/SNAP_CHANGE_REPORT.md` and update user docs for what you shipped". No bulk-reading of the codebase.
- Exact snapped coordinates (3.3). No per-frame allocations in pointer paths (reuse ghost shapes and lists).
- Commit per ticket on `feature/snap-pattern`. Do not push or merge to `main` unless Luis says so.

### 5.3 Unity MCP usage

No Unity MCP package is in `Packages/manifest.json`. Pre-flight confirms which MCP server Claude Code has connected and lists its tools; the steps below are capabilities to map onto that server's tools. **Do not commit** the manifest/lock changes the MCP package makes (decided Q6).

After every merge, the Unity agent:
1. Refreshes assets / requests compilation and waits for it to finish.
2. Reads the console: zero errors and no new warnings from GeoART assemblies.
3. Runs EditMode, then PlayMode tests.
4. Opens `Sandbox_A_Flat.unity`, enters Play Mode, screenshots the Game view at 390x844 (ToolBar fit, candidate dots, hover ring). Exits Play Mode before editing anything.
5. Runs the render check and reads the PNGs in `Logs/GeoARtRenderCheck/`.
6. Does **not** switch the Editor to the Android target during feature work (the first switch re-imports everything); the Android build happens once at the end (A0, on a teammate's machine and phone; Q12). The iOS script check is the Mac teammate's job.

Fallback without MCP: Unity batch mode (`Unity.exe -batchmode -projectPath <repo> -runTests -testPlatform EditMode -testResults <file>`) with the Editor closed.

Never run `SceneBuilder` without checking what it overwrites: it rewrites A-owned prefabs and scenes.

### 5.4 Pre-flight (orchestrator, before C0)

- [ ] `git fetch`; branch `feature/snap-pattern` from `origin/grid-and-android`. If that branch moved past `9270ba3` or was merged to `main`, branch from the newest of those and record the SHA in the report. If teammates changed `EditorSession`, `ToolBar`, `BoardView`, `TileGrid` or `ToolTypes` since, re-read those files and adjust the tickets before starting.
- [ ] Unity 6000.5.10f1 with **Android Build Support** (OpenJDK, Android SDK & NDK) installed; the project opens with no console errors. iOS Build Support is not needed.
- [ ] Unity MCP server connected; list its tools.
- [ ] `dotnet --version` can build `net8.0`; enginecheck passes on the baseline.
- [ ] EditMode + PlayMode tests pass on the baseline; record counts.
- [x] Q1-Q8 decided (section 6).

### 5.5 Device verification

**Luis's phone does not support ARCore (Q12), so Luis does no device testing.** All of his verification happens in the Editor: `Sandbox_A_Flat`, the Device Simulator, PlayMode tests and the render check.

**Ratul (Galaxy S24 Ultra) or Haytham, Android:** in A0, build `Main.unity` to an ARCore-capable Android phone (USB, `docs/BUILD_ANDROID.md`) and check: camera feed and plane found on a textured table; Quick place and corner placement; candidate dots visible and tappable at 25-60 cm; tap-to-connect on a board seen at an angle; snap tolerance at the far edge; grid-intersection snapping; H/V lines aligned with the physical sheet; no drawing through the ToolBar; smooth frame rate with four stars plus candidates. Confirm first that the phone is on Google's ARCore supported-devices list.

**Teammates, iPhone:** the Mac owner builds the same commit for iOS before it merges to `main`, to prove nothing broke there. Luis does no Apple-side work.

The report's "Known limitations" section lists whichever platform has not been device-tested yet.

## 6. Decisions (confirmed by Luis 2026-11-01, updated 2026-11-03)

| # | Question | Decision |
|---|---|---|
| Q1 | Meaning of "horizontal or vertical relative to a plane" | Board X/Y axes |
| Q2 | Duplicate behaviour | Copy in place + drag with snapping (P3) |
| Q3 | Undo/Redo | Out of scope (P3) |
| Q4 | Ownership of `BoardView`, `ToolBar`, `EngineInstaller` | Luis edits them for this feature and tells the team |
| Q5 | Circle/circle intersections | Included |
| Q6 | Commit a Unity MCP package to the shared repo | No, keep it local |
| Q7 | Select, Scale, Duplicate | Deferred to P3 (Haytham, 2026-11-03) |
| Q8 | Grid intersections as snap points | Required (teammate, 2026-11-02) |
| Q9 | Platform for Luis | Android (ARCore) on Windows; no Apple tooling. iOS stays supported by teammates; Android changes must not alter iOS settings |
| Q10 | Android setup | Ratul's `grid-and-android` implementation (Vulkan first + `ARCommandBufferSupportRendererFeature`, verified on device). Supersedes the earlier `ANDROID_SUPPORT.md` audit |
| Q11 | Grid intersections source | `TileGrid.SnapPoints` `CellCorner` points, square and hex |
| Q12 | Device testing | Luis's phone lacks ARCore; device testing by teammates (Ratul, Galaxy S24 Ultra, or Haytham). Luis verifies in the Editor only |

## 7. Progress log

- **2026-11-01**: Plan written from code at `d625c26` and Haytham's 2026-10-30 request. Q1-Q6 accepted. Editor-only development on Windows.
- **2026-11-03**: Found Ratul's `grid-and-android` branch (TileGrid square/hex, Android build verified). Rebased the plan on it: A0 becomes verify-only, grid intersections come from `TileGrid`, ToolBar grows from 6 buttons.
- **2026-11-03**: Luis switches to Android. Static audit (`docs/ANDROID_SUPPORT.md`): runtime code is cross-platform; project config is iOS-only. Added A0; removed iOS steps from Luis's workflow.
- **2026-11-03**: Revised: grid intersections required; Select/Scale/Duplicate moved to P3; change report (R1) added as a required deliverable; golden demo test rebuilt around snapping only.
- **2026-11-03**: Editor-only verification (Q12): Luis's phone lacks ARCore, so A0 is reassigned to Ratul or Haytham. U4 gains a tilted-camera/far-edge sandbox check and Device Simulator screenshots (small Android, tall Android, iPhone). The report must say "Editor-verified only" and list the device checks still owed.
- **2026-11-03**: Baseline at `2a0f6b4`: enginecheck 80/80; EditMode 75/75; PlayMode 12/12; console clean. `ToolBarOnRightEdge` failed (74/75) with the Game view at 1920x1080 landscape and passes at 390x844 portrait, so the Game view is set to a custom "GeoART 390x844" size for all test runs.
- **2026-11-03**: C0 `863c3a9` (contracts; snap state plus `partial void` hooks in the core, so E2 never edits `EditorSession.cs`). U1 `721c036` (`ScreenSnapMetric`, verified in Play Mode with `FlatBoardSpace`: 24 px, board centre at screen centre). U2 `87271f5` (Snap + H/V buttons, 8 buttons fit at 390x844). U3 `cb53ede` (candidate dots by kind in their own mesh part). E1 `7773ca9` (cherry-picked from the worktree branch; 102/102 checks). Unity tests after U3: EditMode 77/77, PlayMode 12/12.

## 8. Setbacks

- **2026-11-03**: The EditMode test `ToolBarOnRightEdge` depends on the Game view's aspect (its canvas comes from the Game view size). In a 1920x1080 landscape Game view the 400 px column puts the Line button at y=0.897, outside the 0.2..0.8 range the test allows. Not a code defect. Fix: run tests with a portrait 390x844 Game view. Also, the MCP PlayMode test runner flips `ProjectSettings/EditorSettings.asset` `m_EnterPlayModeOptions`; that change is churn and is never committed.
- **2026-11-03**: The Agent tool's worktree for E1 was created from `main` (`d625c26`), not `feature/snap-pattern`, so the agent first reset its own branch to `863c3a9`. Later subagent prompts tell the agent to check `git log` and reset to the given base SHA before starting.
- **2026-11-03**: The MCP `manage_camera screenshot` always renders through the camera, which leaves out Screen Space Overlay canvases (ToolBar, sidebar). Workaround in Play Mode (`execute_code`): switch the overlay canvases to Screen Space Camera, render `Camera.main` into a RenderTexture, save the PNG to `Logs/SnapScreens/`, and restore the canvases. These are runtime objects only, and nothing is saved.

---

## Appendix A: P3 design notes (not part of this task)

Kept so P3 can start from here. Reuse `SnapIndex`, `Intersect`, and `ISnapMetric` from this task.

- **Select/Move** (`ToolId.SelectMove`): screen-space hit-test against lines, circle outlines, polygon edges. Tap toggles a shape in `Selection`; tap on empty clears; drag on empty = box select (fully enclosed visible shapes); drag on a selected shape moves the group. Move snapping: selection key points (endpoints, centers, vertices) against candidates from non-selected shapes plus grid points; apply the exact delta of the closest pair within tolerance; clamp so bounds stay in `[0,1]^2`; ghost in `Preview`, commit once on Up. Add `SelectLayerShapes(layerId)` and `ClearSelection()` to `IEditorSession`.
- **Scale** (`ToolId.Scale`): needs a selection. Pivot = bounds center, or a tapped candidate. Factor = `|p - pivot| / |down - pivot|`, clamped to `[0.05, 20]`. Snap the factor so the selection fits the cell or board side, or so a key point lands on a candidate along its pivot ray. Stroke widths do not scale; block commits that shrink shapes below `MinStrokeLength`.
- **Duplicate** (`DuplicateSelection()`): clone with fresh IDs into the same layers in place, select the copies, switch to SelectMove, toast "Copied N shapes: drag to place"; 300-shape cap.
- **Undo/Redo:** command stack (`AddShapes`, `RemoveShapes`, `TransformShapes` with before/after clones, layer commands); selection is not history; a new command clears redo.
- **P3 golden test:** a star of radius 0.25 in one cell, box-selected, duplicated and dragged into the other 3 cells with snapping, must equal `GoldenBoards.Tiled2x2()` exactly; a star of radius 0.5 scaled about (0,0) with fit-to-cell snap must equal the bottom-left cell star.
