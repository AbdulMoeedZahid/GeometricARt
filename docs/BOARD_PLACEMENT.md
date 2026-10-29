# Board placement (A)

Design and rationale are in `CORNER_PLACEMENT_PLAN.md`. This page covers how to use placement on the phone and what C and D need to know.

## Corner placement

The board can be registered to a real flat object on a table, such as a sheet of paper, a canvas or a taped area. You aim at its 4 corners and the board then lies on that object. **Corners** is the default mode. The one-tap 40 cm **Quick place** is still available.

### On the phone

1. Put the sheet on a **textured** table (wood grain, a patterned cloth). On a plain white table ARKit finds no plane. Use good light.
2. Move the phone slowly until the coach text reads "Aim the + at a corner of your sheet and tap (1 of 4)". A small ring with a **+** sits on the table at the centre of the screen.
3. Hold the phone about 25-40 cm away, roughly above the corner. Put the **+** on a corner, hold steady and tap **anywhere** on the screen. The corner is taken under the **+**, not under your finger. Repeat for the other 3 corners, in any order. The coach text shows the live edge length (for example "edge 21.0 cm").
4. After the 4th corner:
   - **Green outline:** the corners fit a rectangle. The preview shows the rectangle, the square board inside it, and an arrow on the edge that will become the **top**. The edge nearest you becomes the bottom. To make another side the bottom, walk to that side before confirming. Tap anywhere, or press **Place board**.
   - **Red outline or a red corner:** the coach text names the problem, for example "Corner 2 doesn't fit a rectangle". Press **Undo corner** and re-aim, or **Restart**.
5. Once placed, the HUD shows "Surface W x H cm · board S cm". The board is the **largest centred square** inside the sheet (an A4 sheet gives a 21 cm board with about 4.35 cm margins top and bottom). A dashed cyan outline shows the registered sheet.
6. **Re-place board** removes the board and starts again.

Buttons (temporary uGUI in `PlacementControls`, replaced later by C's UI):

| Button | When it is shown |
|---|---|
| Mode: Corners / Quick | Not placed, no corner picked yet |
| Quick place 40 cm | Corners mode, surface found, no corner picked yet |
| Undo corner, Restart | While picking corners |
| Place board | 4 corners with a valid rectangle |
| Re-place board | Placed |

Taps on these buttons never add a corner: the tap handler ignores taps that start on uGUI.

Rejections and what they mean:

| Coach text | Cause |
|---|---|
| Hold steady, then tap | The aim moved more than 8 mm over the last 6 frames |
| That is corner k: aim at the next corner | Within 5 cm of an existing corner |
| Corner k doesn't fit a rectangle | An angle is more than 15° off 90°, or one corner is about 3 cm or more off a rectangle (residual > max(15 mm, 5% of the short side)) |
| Too small / Too large / Too narrow | Short side < 10 cm, long side > 150 cm, or aspect > 4:1 |
| Tilt the phone down / Move closer | View ray within 15° of the table, or the aim point is more than 2.5 m away |
| Tracking interrupted: corners cleared | Tracking was not normal for more than 1 s (or the app went to the background) while picking |

Tips: aim from roughly above each corner. Corners off ARKit's plane polygon are fine, because corners 2 to 4 use the plane's infinite extension. Thin paper is assumed: a thick canvas (about 2 cm) seen at an angle shifts the corners by about its thickness.

To get the old behaviour exactly, set `ARBoardSpace.defaultMode = QuickPlace` on `BoardRoot.prefab` (or use the Mode button).

### Contract additions for C and D (additive)

- `PlacementState` has one new value, **appended at the end**: `PickingCorners`. The board is **not placed** in this state: `IsPlaced` is false and `TryScreenToBoard` returns false. D's input already blocks drawing unless `IsPlaced`, so D needs no change. C's coach text needs a case for it.
- New `ICornerPlacement` (`Assets/GeoART/Contracts/Board/ICornerPlacement.cs`), registered in `ServiceHub` by `ARBoardSpace` and, as a no-op (QuickPlace, never picking), by `FlatBoardSpace`. It is optional for consumers, so resolve it with `ServiceHub.TryResolve`. It provides the mode, the corner count, `CanAddCorner`/`CanConfirm`, `Issue` + `IssueCorner` (0-based, pick order), the live edge length, the surface and board size, the actions `TryAddCorner`/`UndoCorner`/`ClearCorners`/`ConfirmCorners`/`SetMode`, and a `CornersChanged` event (not raised per frame for the edge length). `IBoardSpace` still owns `State`/`StateChanged`.
- Board space is still `[0,1] x [0,1]` and the board is still square. Only its physical size changes (0.40 m for quick place, `min(W, H)` for corners). Model coordinates, `GridN`, snapping, fixtures and lessons are unaffected. Stroke widths scale with the board (clamped to 0.4x-2x of the 40 cm look).
- C: bind your real placement UI to `ICornerPlacement` + `IBoardSpace`, then remove `PlacementControls` from `Main.unity` (SceneBuilder adds it) and turn off `DevHud.showCoach`. Use uGUI/UI Toolkit, not IMGUI, so the tap handler's `EventSystem` hit-test ignores your buttons.

### Checks

- EditMode (Unity GUI: Window > General > Test Runner > EditMode > Run All): `BoardGeometryTests`, `CornerRectFitTests`, `CornerPickSessionTests`.
- Pure math without Unity, on the Mac:
  ```bash
  cd "/Users/root1/Desktop/Undergraduate Degree/Fall 26/CAP 4111/GeometricARt" && DOTNET_CLI_HOME=$HOME/GeoARtTmp/dotnethome DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 /Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet run --project tools/mathcheck -c Release
  ```
- iOS player scripts compile check, without building a player: `-executeMethod GeoART.EditorTools.PlayerScriptCheck.CompileIOSBatch` (menu GeoART/Build/Check iOS Player Scripts).
- The device checklist is in `CORNER_PLACEMENT_PLAN.md`, section 7 (C-01 to C-14).

## iOS build note: ARKit loader define

`ProjectSetup` (GeoART/Setup/1) adds the iOS scripting define `UNITY_XR_ARKIT_LOADER_ENABLED`. ARKit's own editor check, which normally writes this define, does nothing in batch mode. Without the define, an Xcode export from a project that has only been opened in batch mode contains no ARKit native plugin, and the app shows a black screen with no camera prompt. After any export, check that `grep -c libUnityARKit Unity-iPhone.xcodeproj/project.pbxproj` is greater than 0.
