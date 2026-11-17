# Save designs: implementation plan

Feature 5 of the proposal: "Ability to save patterns and designs/tiles a user has made."
Branch: `feature/save-designs` (from `main` at 0d10761). Written for a subagent to execute step by step.

## Goal

- The user can save the design on the board. Saves survive closing and reopening the app.
- The user can see the list of saved designs, load one back, and delete one.
- Same UI style as the rest of the app. No new packages.
- Works with Unity 6000.5.10f1, AR Foundation 6.5 (ARKit on iPhone). It is tested in the Editor in
  `Assets/Scenes/Sandbox/Sandbox_A_Flat.unity` with the mouse (no camera, no AR).

## What exists today (analysis)

The design is one object, `GeoART.Contracts.Board` (`Assets/GeoART/Contracts/Model/Board.cs`):

| Field | Meaning |
|---|---|
| `GridN` (1..4), `GridKind` (Square, Hex) | the tile grid ("tiles") |
| `Layers` (index 0 = bottom) | each `Layer`: `Id`, `Name`, `Visible`, `Opacity` [0,1], `Shapes`, `SnapPoints` |
| `ActiveLayerId`, `NextId` | active layer and the id counter (`EntityId` is an int, 0 = none) |

Shapes (`Shape` subclasses, all value fields): `LineShape` (A, B), `CircleShape` (Center, Radius), `PolygonShape`
(Center, Sides, Radius, RotationRad). Every shape has `Id` and `Style` (`StrokeStyle`: `RgbaColor` R/G/B/A bytes,
`LineWidth` Thin/Medium/Thick, `LineStyle` Solid/Dotted). `SnapPoint` = Id + Position. `Vec2` = two doubles in
board space [0,1]x[0,1].

- Nothing is persisted. `docs/GRID.md`: "Save/reload: no serializer exists yet; when one is added, persist `GridKind`
  next to `GridN`." No PlayerPrefs, no files, no JSON anywhere in `Assets/GeoART`.
- The engine `EditorSession` (`Assets/GeoART/Engine`) owns the board. The UI reaches it through
  `IEditorSession` from `ServiceHub` (`ServiceHub.TryResolve(out IEditorSession s)` in `Start`).
  - Read: `session.Board`.
  - Replace: `session.LoadBoard(Board b)`: cancels any gesture, swaps the board, makes sure a layer is active,
    raises `BoardChanged(Reset)` + `StateChanged`. Every view (BoardView, ToolBar, LayerSidebar) already redraws
    on `Reset`. So loading needs NO engine change.
  - New/clear: `session.ClearBoard()` (one empty "Layer 1", 2x2 square grid).
- UI is uGUI built in code at runtime (no prefabs, no art). Shared style:
  - `LayerUiKit` (internal static in GeoART.App): `CreateCanvas(parent, name, sortingOrder, raycaster)` (overlay,
    ScaleWithScreenSize 390x844, match 0.5), `NewImage` (rounded sliced sprite), `NewText` (LegacyRuntime font),
    `NewRect`, `Place`, `ScaleFactor`.
  - Colours: idle button `new Color(0.08f, 0.08f, 0.1f, 0.78f)`, accent orange `new Color(1f, 0.42f, 0f, 0.95f)`,
    destructive red chip as in `LayerSidebar`, white text. ToolBar buttons are 56x56 ref px, icon 30 px + 11 pt label.
  - Sorting orders: PlacementControls 100, ToolBar 105, LayerSidebar 110, ToastView 120.
  - Every interactive element is a raycast target; `BoardInputRouter` ignores touches that start on UI
    (EventSystem raycast), so a full-screen raycast-target blocker stops drawing behind a panel.
  - Screen layout: ToolBar column right-centre, layer sidebar left-centre, coach text 12-25% from the top
    (only shown when not placed / tracking lost), toasts at 24.5%, placement buttons at the bottom.
    The TOP-RIGHT corner is free. (Unmerged teammate branches: `Abdul-P3-Work` puts an Undo button TOP-LEFT;
    `vertical-board-placement-and-fixes` puts a ColorBar BOTTOM-LEFT. Don't use those spots.)
  - Toolbars are visible only when placed: `visible = session != null && (boardSpace == null || boardSpace.IsPlaced)`,
    refreshed on `IBoardSpace.StateChanged`.
- Scenes: `Main.unity` (AR, phone), `Sandbox_A_AR.unity` (AR), `Sandbox_A_Flat.unity` (Editor, flat board,
  mouse). Each has one `App` GameObject with `EngineInstaller`, `BoardInputRouter`, `ToolBar`, `LayerSidebar`,
  `ToastView` (+ `DevHud`, and `PlacementControls` in AR scenes). In `Sandbox_A_Flat` the board counts as placed at once.
- Project rules that apply: Play Mode can run with domain reload disabled, so statics are reset on
  `RuntimeInitializeLoadType.SubsystemRegistration` and listeners unsubscribe in `OnDestroy`; `UnityEngine.EntityId`
  exists in Unity 6.5, so in files that use both namespaces write `GeoART.Contracts.EntityId` or alias it.
- Teammate branches change `Board.cs`, `EditorSession*.cs`, `ToolBar.cs`, `IEditorSession.cs` and scenes. To avoid merge
  conflicts this feature ADDS FILES ONLY and edits no existing file.

## Design

- Storage: one JSON file per design in `Application.persistentDataPath/Designs/`, written with `JsonUtility`
  (built in, no package).
  - Windows Editor: `C:\Users\<you>\AppData\LocalLow\GeoART Team\Geometric ARt\Designs\`
  - iPhone: the app's `Documents/Designs/` (kept across launches and app updates, removed on uninstall).
- File: `{ "version": 1, "name": "Design 3", "savedUtc": "2026-11-17T20:04:05.123Z", "board": { ... } }`. File name is
  the id: `design-yyyyMMdd-HHmmss-fff.json` (add `-2`, `-3`... if it exists). Written to `<id>.json.tmp` first, then
  moved over, so a crash never leaves half a file.
- Names: automatic, "Design N" where N = 1 + the highest "Design k" in the library. No text input (no keyboard on
  the phone, keeps it simple).
- Load replaces the board through `session.LoadBoard` (this also clears undo history on the undo branch, which is right
  for "open a different document").
- UI: a "Designs" button (56x56, same look as ToolBar buttons) in the TOP-RIGHT of the safe area, visible on the same
  rule as ToolBar. It opens a centred panel:
  - title "Designs", close "x"
  - "Save current design" (accent orange) and "Clear board" (two-tap confirm: first tap turns it red "Tap again to
    clear", reverts after 3 s)
  - a status line ("Saved \"Design 3\"", "Loaded \"Design 1\"", "Deleted \"Design 2\"", errors)
  - a scrollable list, newest first: per row the name, a small line like `Nov 17, 3:04 PM · 2 layers · 14 shapes ·
    Hex 3x3`, a "Load" button and a "Delete" button (two-tap confirm like Clear). Empty list: "No saved designs yet".
  - a full-screen dim blocker (raycast target) behind the card; tapping it closes the panel. No drawing while open.
- The panel attaches itself: a `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` hook (plus `SceneManager.sceneLoaded`)
  adds `DesignsPanel` to every GameObject that has a `ToolBar` and no `DesignsPanel` yet. So no scene or prefab is
  edited and it works in Main, Sandbox_A_AR and Sandbox_A_Flat.

## Files

All new. Do not modify any existing file.

| File | What |
|---|---|
| `Assets/GeoART/App/Save/DesignFile.cs` | `[Serializable]` DTOs (public fields) + `DesignSerializer` (Board <-> DTO <-> JSON) |
| `Assets/GeoART/App/Save/DesignLibrary.cs` | file store: list / save / load / delete in a folder |
| `Assets/GeoART/App/Save/DesignsPanel.cs` | the button + panel (MonoBehaviour) and the auto-attach hook |
| `Assets/Tests/EditMode/Save/GeoART.Tests.EditMode.Save.asmdef` | test assembly (copy the Verifier EditMode asmdef settings) |
| `Assets/Tests/EditMode/Save/DesignSerializerTests.cs` | round-trip and validation tests |
| `Assets/Tests/EditMode/Save/DesignLibraryTests.cs` | file store tests in a temp folder |
| `docs/SAVE.md` | how to use it, where files go, notes for teammates |
| the `.meta` file Unity creates for each new file and folder | commit them |

The `Save` folder sits under `Assets/GeoART/App`, so it compiles into the existing `GeoART.App` assembly (no asmdef
edit needed). Classes the tests use must be `public`.

## Working rules for the implementer

- Branch `feature/save-designs` only. Three files are modified locally and must NEVER be staged or committed:
  `Packages/manifest.json`, `Packages/packages-lock.json`, `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`
  (the Unity MCP package lives there). Stage with explicit paths (`git add <file> <file>.meta ...`), never
  `git add -A` / `git add .` / `git commit -a`. Check `git status --short` before each commit. Do not push and do not merge.
- Unity is open on this project with Unity MCP at `http://127.0.0.1:8080/mcp`. Talk to it with the helper
  `python C:/Users/coola/AppData/Local/hermes/cache/scratch/umcp.py call <tool> '<json args>'`
  (`umcp.py list [tool]` prints the tools / one tool's schema). It prints Unity's memory before/after each call.
  - One MCP call at a time. If Unity goes over 8 GB, or a call hangs past 120 s, stop all MCP calls and report.
  - Useful tools: `refresh_unity` (compile after editing files on disk), `read_console` (`{"action":"get","types":["error","warning"],"count":50,"format":"plain"}`; `{"action":"clear"}`),
    `run_tests` + `get_test_job`, `manage_editor` (`play`/`stop`), `manage_scene` (`get_active`, `load`),
    `execute_code` (`{"action":"execute","code":"...C# method body, return a value..."}`), `validate_script`.
  - Make sure `Sandbox_A_Flat` is the open scene (it is now). Never save a scene; never edit scenes or prefabs.
  - If MCP says "ping not answered", Unity may be throttled in the background; wait and retry a couple of times, then report.
- Pure logic stays free of UI. Catch IO exceptions in `DesignLibrary` and return false + a message; never throw into
  the UI. A broken file is skipped in the list with ONE `Debug.LogWarning`, never an error.
- Reset statics on `SubsystemRegistration`; unsubscribe events in `OnDestroy`.
- Match the code style: file headers with a short `<summary>`, plain comments, no new packages, no TextMeshPro.

## Steps

### Step 0: baseline
1. `git branch --show-current` is `feature/save-designs`; `git status --short` shows only the three local files.
2. Over MCP: clear the console, `refresh_unity`, read errors/warnings (expect none). Run all EditMode tests
   (`run_tests` mode EditMode, poll `get_test_job`) and write down the pre-existing failures, if any, so later runs can
   be compared.

Test: you have a baseline pass/fail count.

### Step 1: DTOs and serializer (`DesignFile.cs`)
1. DTOs, all `[Serializable]` with public fields: `DesignFile { int version; string name; string savedUtc; BoardData board; }`,
   `BoardData { int gridN; int gridKind; int nextId; int activeLayerId; List<LayerData> layers; }`,
   `LayerData { int id; string name; bool visible; double opacity; List<ShapeData> shapes; List<SnapPointData> snapPoints; }`,
   `ShapeData { int kind; int id; int r, g, b, a; int width; int dash; double ax, ay, bx, by, cx, cy, radius, rotation; int sides; }`
   (unused fields stay 0 for a given kind), `SnapPointData { int id; double x, y; }`.
2. `public static class DesignSerializer`:
   - `const int Version = 1;`
   - `BoardData FromBoard(Board b)` and `Board ToBoard(BoardData d)`; `string ToJson(Board b, string name, DateTime savedUtc)`;
     `bool TryParse(string json, out DesignFile file, out string error)`.
   - `ToBoard` validates: grid N clamped with `TileGrid.ClampN`, unknown grid kind -> Square; unknown shape kind,
     non-finite numbers, circle/polygon radius <= 0, polygon sides < 3 -> that shape is skipped; opacity clamped to
     [0,1]; colour bytes clamped to 0..255; null lists treated as empty; `NextId` raised to (max id used + 1) if lower.
     A null/missing board, or `version` > `Version`, fails `TryParse` with a message.
   - Ids, names, order of layers/shapes/snap points, `ActiveLayerId` are kept exactly.
3. Doubles must survive bit-exactly (snapping compares points with ==). First check what `JsonUtility` writes for
   doubles: round-trip `CircleDivisions.Diagonal`, `1.0/3.0`, `0.1`, `TileGrid.HexRadius(3)`, `1e-17`, and
   `double.Epsilon` through a tiny `[Serializable]` class in a test. If any value does not come back with the same
   bits, store every double as a string written with `ToString("R", CultureInfo.InvariantCulture)` (parse with
   InvariantCulture) and say so in the file header comment.

Test (`DesignSerializerTests`, EditMode):
- `GoldenBoards.StarOnFullBoard()` and `GoldenBoards.Tiled2x2()`: board -> JSON -> board gives an equal board. Write
  one helper `AssertBoardsEqual(Board expected, Board actual)` comparing every field above (shape by shape, with
  `Vec2.Equals` / `==` on doubles, not a tolerance).
- A hand-made board with all three shape kinds, Hex grid n=3, a hidden layer with opacity 0.35, a red Thick Dotted
  style, snap points, `ActiveLayerId` on the middle layer, `NextId` 57: round-trips equal.
- Double exactness test from 1.3.
- Validation: unknown shape kind skipped; NaN coordinate skipped; `gridN` 9 -> 4; `nextId` too low is raised;
  garbage JSON and `version` 99 -> `TryParse` false with a non-empty error; empty `layers` -> the board loads and
  `session.LoadBoard` still gives one layer (use `new EditorSession()` from GeoART.Engine).
- Refresh over MCP, no console errors, run the `GeoART.Tests.EditMode.Save` assembly: all green.

Commit: "Save: DTOs + DesignSerializer (board <-> JSON) + tests".

### Step 2: file store (`DesignLibrary.cs`)
1. `public sealed class DesignLibrary` with `public DesignLibrary(string rootDirectory)` and
   `public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "Designs")`.
2. `public sealed class DesignEntry { string Id; string Name; DateTime SavedUtc; int LayerCount; int ShapeCount; GridKind GridKind; int GridN; }`
   (read-only properties).
3. Methods:
   - `IReadOnlyList<DesignEntry> List()` newest first (ties: by id descending); creates the folder if missing; ignores
     `*.tmp` and non-`.json` files; a file that does not parse is skipped with one `Debug.LogWarning` naming the file.
   - `string NextDefaultName()` "Design N".
   - `bool TrySave(Board board, out DesignEntry entry, out string error, string name = null)`: name defaults to
     `NextDefaultName()`; id from the UTC time as above; write tmp then move.
   - `bool TryLoad(string id, out Board board, out string error)`.
   - `bool TryDelete(string id, out string error)`.
   - Reject ids containing path separators or `..` (ids come only from our own file names, but be safe).
4. `public static string LibraryRootOverride` (null by default) used by the panel when set; reset on SubsystemRegistration.
   It exists so automated Play Mode checks write to a temp folder instead of the user's real saves.

Test (`DesignLibraryTests`, EditMode, each test in a fresh folder under `Path.GetTempPath()`, deleted in TearDown):
- Empty folder -> empty list; missing folder is created.
- Save twice -> two entries, names "Design 1", "Design 2", newest first, counts correct.
- Load returns a board equal to the saved one (reuse `AssertBoardsEqual`).
- A NEW `DesignLibrary` on the same folder lists the same entries (persistence).
- Delete removes the file and the entry; deleting a missing id returns false.
- A corrupt `.json` and a stray `.tmp` in the folder: list shows only the good files; expect the warning with
  `LogAssert.Expect(LogType.Warning, new Regex(...))`.
- Bad ids (`../x`, `a/b`) are rejected.
- Refresh, no console errors, `GeoART.Tests.EditMode.Save` all green.

Commit: "Save: DesignLibrary file store in persistentDataPath/Designs + tests".

### Step 3: UI (`DesignsPanel.cs`)
1. `[DisallowMultipleComponent] public sealed class DesignsPanel : MonoBehaviour`. Build everything in `Awake` with
   `LayerUiKit` and the colours above:
   - Canvas "DesignsButton" (sortingOrder 106, raycaster): one 56x56 rounded idle button named `DesignsButton`, top-right of
     `Screen.safeArea`, 8 ref px in from the corner, with a small procedural glyph (e.g. three stacked rounded
     outlines = a stack of sheets; draw it into a 64 px texture like ToolBar's `MakeGlyph`, written in this file) and the
     label "Designs" (11 pt, white). Re-apply the safe area in `LateUpdate` when `Screen.safeArea` / size change
     (same pattern as ToolBar).
   - Canvas "DesignsPanel" (sortingOrder 115, raycaster), disabled until opened: full-screen `Blocker` (black, alpha
     ~0.45, raycast target, click closes); centred `Card` (about 340 wide, height min(560, safe height - 40), colour
     like the sidebar pill / idle, radius ~18); `Title` "Designs"; `CloseButton` "x"; `SaveButton`
     "Save current design" (accent); `ClearButton` "Clear board" (idle; two-tap confirm); `Status` text; `List`
     = ScrollRect (vertical only) + RectMask2D viewport + content with VerticalLayoutGroup + ContentSizeFitter; one
     `Row_<id>` per entry with `Name`, `Info`, `LoadButton`, `DeleteButton` (two-tap confirm, red when armed);
     `EmptyText` "No saved designs yet".
2. `Start`: resolve `IEditorSession` and (optional) `IBoardSpace` from `ServiceHub`, subscribe to
   `IBoardSpace.StateChanged` for button visibility (same rule as ToolBar). Library =
   `new DesignLibrary(DesignLibrary.LibraryRootOverride ?? DesignLibrary.DefaultRoot)`. If the board stops being placed
   while the panel is open, close it.
3. Public API (used by the buttons and by the Play Mode check): `bool IsOpen`, `void Open()` (rebuilds the list),
   `void Close()`, `bool SaveCurrent()`, `bool LoadDesign(string id)` (calls `session.LoadBoard`, then closes the
   panel), `bool DeleteDesign(string id)` (no confirm; the row button does the two-tap confirm then calls it),
   `void ClearBoard()`, `DesignLibrary Library`, `string StatusText`.
   Loading a board while a stroke is in progress is safe: `LoadBoard` cancels it.
4. Auto-attach: `static class DesignsPanelBootstrap` in the same file, `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`
   attaches to every `ToolBar` (`FindObjectsByType<ToolBar>(FindObjectsSortMode.None)`) without a `DesignsPanel`,
   and subscribes `SceneManager.sceneLoaded` (unsubscribe first, so a disabled domain reload never subscribes twice;
   also reset on SubsystemRegistration).
5. Destroy created textures/sprites in `OnDestroy`.

Test (over MCP, `Sandbox_A_Flat`):
- Refresh, no compile errors or warnings from the new files. EditMode tests still at baseline + the new ones green.
- `manage_editor` play. With `execute_code`: exactly one `DesignsPanel`, on the `App` GameObject; `DesignsButton`
  active and inside the screen; the ToolBar column and the button do not overlap (compare world rects of `DesignsButton` and
  ToolBar's `Column`); `ScreenCapture.CaptureScreenshot("<project>/Logs/SaveScreens/closed.png")`, then `Open()`, next
  frame capture `open.png`. Look at both PNGs (paths in the report). Stop Play Mode. Console: no errors.

Commit: "Save: Designs button + panel (save, list, load, delete, clear), auto-attached next to ToolBar".

### Step 4: Play Mode end-to-end check (not committed; run over MCP)
The panel must read `DesignLibrary.LibraryRootOverride ?? DesignLibrary.DefaultRoot` lazily (each time it needs the
library, e.g. in `Open()` / `SaveCurrent()`), because the override is reset when Play starts. So: press Play, then set
the override to a temp folder (e.g. `<project>/Temp/SaveCheck`) with `execute_code`, then run the steps. After the
second Play in 6, set it again.
In `Sandbox_A_Flat`, Play Mode, using the real session (`ServiceHub.Resolve<IEditorSession>()`):
1. Make a design: `SetGridKind(Hex)`, `SetGrid(3)`, draw a line and a circle with `Pointer(new PointerEvent(Down/Move/Up, ...))`
   at a few board points, `AddLayer()`, draw another line, `SetLayerOpacity` on one layer. Record the board as JSON
   (`DesignSerializer.ToJson` with a fixed name/date) = `before`.
2. Save by invoking the real `SaveButton`'s `Button.onClick`. One entry in the list, one file in the temp folder.
3. Clear via the `ClearButton` (two clicks). Board has 1 empty layer, Square 2x2.
4. Load via the row's `LoadButton`. Board JSON == `before` exactly. Panel closed.
5. Delete via the row's `DeleteButton` (two clicks). List empty, folder has no `.json`.
6. Persistence: save again, stop Play, press Play again (override still set), `Open()`: the entry is listed; load it;
   JSON == `before`. Delete it.
7. `read_console` errors and warnings: none from this feature (list anything else and say if it was in the baseline).
8. Remove the temp folder and reset the override.
Report each sub-step as PASS/FAIL with the evidence (counts, JSON equality, file list).

### Step 5: docs and wrap-up
1. `docs/SAVE.md`: what it does; how to use it (button, panel, two-tap delete/clear); where files are stored
   (Windows Editor path, iPhone Documents); file format and versioning; that loading resets undo history; that
   `DesignsPanel` attaches itself to the GameObject with `ToolBar` (no scene changes) and how to remove it; the new files;
   how to run the tests; ideas left out (thumbnails, rename, share/export).
2. Final checks: `git status --short` shows only the three local files; `git log --oneline main..` shows the feature commits
   and nothing else; all new files have committed `.meta` files; no existing file changed
   (`git diff --stat main.. -- . ':!docs/SAVE.md' ':!docs/save-feature-plan.md' ':!Assets/GeoART/App/Save' ':!Assets/GeoART/App/Save.meta' ':!Assets/Tests/EditMode/Save' ':!Assets/Tests/EditMode/Save.meta'` is empty).

Commit: "Save: docs/SAVE.md".

## Done when
- EditMode: new Save tests green; existing tests unchanged versus the baseline.
- Play Mode check (step 4): every sub-step PASS, no console errors.
- Only new files on the branch, in 4 commits (+ this plan), not pushed, not merged.
