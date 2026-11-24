# Saving designs

The user can save the design on the board, see the saved designs, load one back and delete one. Saves stay on the
device when the app is closed. Nothing else in the app changed: the feature is only new files.

## How to use
1. Place the board. A **Designs** button (a small stack of sheets) shows up in the top-right corner. Like the tool bar,
   it is hidden while the board is not placed.
   On a short screen, where the tool bar column would reach the corner, the button moves one column to the left.
2. Tap it. A panel opens in the middle of the screen and the rest of the screen goes dark. You can't draw while it is
   open. Tap the dark area or the **x** to close it.
3. In the panel:
   - The text field at the top is the name for the next save. Tap it and type (the phone keyboard opens), then press
     **Save current design**. Leave it empty and the design is called "Design N" (one more than the highest "Design"
     number already saved); the field's grey hint shows which name that will be. Names are up to 40 characters; line
     breaks and extra spaces are removed.
   - **Clear board** starts over with an empty board (one layer, 2x2 square grid). The first tap turns the button red
     and changes it to "Tap again to clear". A second tap within 3 seconds clears the board. Otherwise it goes back to normal.
   - The list shows every saved design, newest first: the name, when it was saved, and a line like
     `2 layers · 3 shapes · Hex 3x3`.
     - **Tap a name to rename it.** A text field opens over the name with the old name selected. Type the new name and
       press Enter (Done on the phone), or tap anywhere else. Esc cancels. An empty name is refused. Renaming only
       changes the name: the board, the save time, the file and the position in the list stay the same.
     - **Load** replaces the board with that design and closes the panel. A short "Loaded ..." toast confirms it.
     - **Delete** works like Clear: the first tap turns it red ("Sure?"), a second tap within 3 seconds deletes the file.
   - The grey line under the buttons says what just happened ("Saved "Design 3"", "Deleted "Design 2"") or what went wrong.
4. Loading or clearing replaces the whole board. Undo history does not carry over (`LoadBoard` resets it), the same as
   opening a different document. Save first if you want to keep what is on the board.

## Where the files go
One JSON file per design in `Application.persistentDataPath/Designs/`:

| Platform | Folder |
|---|---|
| Windows Editor | `C:\Users\<you>\AppData\LocalLow\GeoART Team\Geometric ARt\Designs\` |
| iPhone | the app's `Documents/Designs/`. Kept across launches and app updates, removed when the app is deleted. |

The file name is the design's id: `design-yyyyMMdd-HHmmss-fff.json` (UTC time of the save, `-2`, `-3`... added if two
saves land in the same millisecond). A save is written to `<id>.json.tmp` first and then renamed, so a crash in the
middle never leaves a half-written design. Leftover `.tmp` files and anything that isn't `.json` are ignored.

To get rid of all saves in the Editor, delete that folder. You can also copy design files between machines by hand.

## File format
```json
{
    "version": 1,
    "name": "Design 3",
    "savedUtc": "2026-11-17T20:04:05.123Z",
    "board": {
        "gridN": 3, "gridKind": 1, "nextId": 8, "activeLayerId": 1,
        "layers": [
            { "id": 1, "name": "Layer 1", "visible": true, "opacity": "0.4",
              "bandOn": true, "bandMode": 0, "bandWidth": "0.015", "bandHideStructure": false, "bandInner": false,
              "bandFlip": false, "bandR": 20, "bandG": 20, "bandB": 24, "bandA": 255, "bandLineWidth": 0,
              "shapes": [ { "kind": 0, "id": 2, "r": 20, "g": 20, "b": 24, "a": 255, "width": 1, "dash": 0,
                            "ax": "0.2", "ay": "0.2", "bx": "0.8", "by": "0.3",
                            "cx": "0", "cy": "0", "radius": "0", "rotation": "0", "sides": 0 } ],
              "snapPoints": [ { "id": 7, "x": "0.5", "y": "0.5" } ] }
        ]
    }
}
```
(The real file is written by `JsonUtility` with one field per line.)

- It mirrors `Board` one to one: `gridKind`, shape `kind`, `width` and `dash` are the enum numbers (`GridKind`,
  `ShapeKind`, `LineWidth`, `LineStyle`). Layers are bottom first, like `Board.Layers`. Ids, names and order are kept
  exactly.
- Every shape uses the same record. A line uses `ax..by`, a circle `cx, cy, radius`, a polygon `cx, cy, radius,
  rotation, sides`. The fields a shape doesn't use are 0.
- **Doubles are strings**, written with `ToString("R", InvariantCulture)`. Snapping compares points with `==`, so a
  loaded point must have exactly the same bits as the saved one. `JsonUtility` writes plain numbers almost exactly, but we
  checked and it reads `5e-324` (`double.Epsilon`) back as 0, so we don't rely on it. A missing or unreadable number
  counts as NaN.
- Loading never trusts the file. Grid size is clamped to 1..4, an unknown grid kind becomes Square, opacity is
  clamped to 0..1 (missing means 1), colour values to 0..255. A shape with an unknown kind, a NaN/infinite number, a
  radius <= 0 or fewer than 3 sides is dropped. `nextId` is raised above the highest id in the file. A board with no
  layers gets one empty layer from `LoadBoard`.
- **Versioning**: `version` is `DesignSerializer.Version` (1). Files with a higher version are refused with a message
  ("saved by a newer version of the app"). If you change the format in a way old code can't read, bump the version and
  convert old files in `DesignSerializer.ToBoard`. Adding a new field doesn't need a bump: old files just read the
  default value (0, or NaN for a number stored as text, so give a new number a fallback in `ToBoard`).
- **Bands** (`docs/BANDS.md`): each layer stores its `LayerBands` as `bandOn`, `bandMode` (`BandMode`), `bandWidth`
  (a double as text), `bandHideStructure`, `bandInner`, `bandFlip`, the outline colour `bandR..bandA` and
  `bandLineWidth` (`LineWidth`). The saved value is the sanitized one, and loading reads it back through
  `LayerBands.Sanitized()`: an unknown mode becomes Double, a missing or bad width the default 6 mm, a too large one
  20 mm, a transparent colour black. No version bump: a file from before bands has none of these fields and loads with
  bands off; an older app reading a new file ignores them and shows the design unbanded (nothing is lost on disk unless
  that old app saves over it).
- **Fills** (`docs/FILL.md`): each layer has a `fills` list of `{ "x", "y" (doubles as text), "r", "g", "b", "a" }`, the
  seed point and colour of each fill, in order. Older files have no list and load without fills; an unreadable point
  drops that fill; colour values are clamped.
- A file that can't be read is skipped in the list with one warning in the console. It is never deleted.

## How it is wired (notes for teammates)
- `DesignsPanel` attaches itself. `DesignsPanelBootstrap` (same file) runs after the first scene loads and on every
  later scene load, and adds a `DesignsPanel` to each GameObject that has a `ToolBar` and no panel yet. So it works in
  `Main`, `Sandbox_A_AR` and `Sandbox_A_Flat` without touching any scene or prefab.
  - To turn it off, delete `Assets/GeoART/App/Save/DesignsPanel.cs` (with its `.meta`), or remove the
    `[RuntimeInitializeOnLoadMethod]` attribute from `DesignsPanelBootstrap.AfterFirstScene`.
  - If you would rather have it in the scene, add the `DesignsPanel` component to the `App` GameObject by hand. The
    bootstrap then sees it and adds nothing.
- It talks to the engine only through `IEditorSession` from `ServiceHub`: it reads `session.Board` to save and calls
  `session.LoadBoard(board)` / `session.ClearBoard()`. Every view already redraws on `BoardChanged(Reset)`. No engine,
  contract or tool bar code was changed.
- The name fields are the legacy uGUI `InputField` (no TextMeshPro, no new package). They work with the Input System
  UI module the scenes already use and open the iOS keyboard on the phone.
- uGUI built in code, like the rest of the app: canvases `DesignsButton` (sorting order 106, just above the tool bar)
  and `DesignsPanel` (115, above the layer sidebar, below toasts). The layout uses the 390x844 reference and follows
  `Screen.safeArea`.
- If you change `Board` (a new field, a new shape kind), update `DesignSerializer.FromBoard` / `ToBoard` and the DTOs in
  `DesignFile.cs`, and add the field to `BoardAssert.AreEqual` in the tests. The round-trip tests will fail if you forget
  a field that the comparison checks.
- `DesignLibrary.LibraryRootOverride` points the panel at a different folder. It is only for automated checks, so they
  don't touch real saves. It is cleared when Play Mode starts.

## Files
| File | What |
|---|---|
| `Assets/GeoART/App/Save/DesignFile.cs` | file DTOs (`DesignFile`, `BoardData`, `LayerData`, `ShapeData`, `SnapPointData`) and `DesignSerializer` (Board <-> JSON, checks) |
| `Assets/GeoART/App/Save/DesignLibrary.cs` | `DesignLibrary` (list / save / load / rename / delete in a folder, name clean-up) and `DesignEntry` |
| `Assets/GeoART/App/Save/DesignsPanel.cs` | the Designs button and panel, and `DesignsPanelBootstrap` |
| `Assets/Tests/EditMode/Save/GeoART.Tests.EditMode.Save.asmdef` | test assembly |
| `Assets/Tests/EditMode/Save/DesignSerializerTests.cs` | round trips (golden boards, a hand-made board, exact doubles) and bad-file handling |
| `Assets/Tests/EditMode/Save/DesignLibraryTests.cs` | file store tests, each in its own temp folder |

The `Save` folder sits under `Assets/GeoART/App`, so the code compiles into the existing `GeoART.App` assembly.

## Tests
Window > General > Test Runner > EditMode, then run `GeoART.Tests.EditMode.Save` (27 tests). They don't need a scene.
The library tests write to a fresh folder under the system temp folder and delete it afterwards.

To check it by hand: open `Assets/Scenes/Sandbox/Sandbox_A_Flat.unity`, press Play, draw something, then
Designs > Save current design > Clear board (twice) > Load. The board comes back as it was.

## Left out for now
- Thumbnails in the list.
- Sharing or exporting a design (share sheet, Files app), and importing one.
- Overwriting an existing save ("Save" vs "Save as"). Every save makes a new file.
