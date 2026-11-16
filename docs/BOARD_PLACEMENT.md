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

## Vertical surfaces (walls, screens, whiteboards)

The same corner flow (and Quick place) works on a **vertical** surface: a wall, a projector screen, a whiteboard, a framed picture, or a board someone holds upright. There is no Table/Wall switch: AR plane detection looks for horizontal **and** vertical planes, and whichever plane is under the **+** is used. Tapping corner 1 locks that plane, so corners 2 to 4 cannot jump to the floor or to a different wall.

### On the phone

1. Stand about 30-80 cm from the wall and move the phone slowly across it. The coach text reads "Move your phone slowly over a textured table or wall". Once the wall is found it reads "Aim the + at a corner on the wall and tap (1 of 4)".
2. Aim the **+** at each of the 4 corners of the area (poster, screen, whiteboard) and tap, as on a table. Face the wall roughly straight on: if the view is too oblique the coach text says "Face the wall more directly: view is too flat".
3. After the 4th corner, tap or press **Place board**. The board is the largest centred square inside the rectangle, as on a table.

How a wall is found (so it starts as reliably as a table): ARKit/ARCore detect vertical planes later and less often than horizontal ones, so `ARBoardSpace` uses three sources, in order:

1. A detected plane under the **+** (same as tables).
2. A detected wall plane's extension, in both modes (wall polygons are small; for tables this stays Corners-only, as before).
3. A wall **estimated from feature points** (`WallEstimator`), with a wider search when points are sparse, and **remembered walls** for plain areas (see Limits): points in a cone around the **+** are fitted with a vertical plane (RANSAC, then least squares), filtered over time and held through short gaps. It only ever returns vertical surfaces and rejects floors, table tops, clutter, posts and door frames, so tables are unaffected. Once a corner is picked on an estimated wall, the wall switches to ARKit/ARCore's detected plane as soon as one appears there.

An `ARPointCloudManager` (no visuals) supplies the feature points. `SceneBuilder` adds it, and `ARBoardSpace` adds one at runtime if the scene has none. Like plane detection, it is switched off once the board is placed and on again with **Re-place board**.

On walls, the "Hold steady" check allows about 0.9° of hand tremor (8 mm at 0.5 m, 16 mm at 1 m, 24 mm at 1.5 m), because walls are aimed at from farther away. Tables keep the fixed 8 mm.

Orientation: on a wall the board's **top always points up** (world up projected onto the wall), so the pattern is never sideways or upside down, whether you hold the phone above, below or to the side. On a table nothing changes: the edge nearest you is the bottom. Quick place (40 cm) also places an upright board on a wall.

Limits:

- Tip: move the phone **sideways** along the wall for a second or two before aiming. Feature points (and wall planes) come from sideways parallax, not from pushing the phone toward the wall.
- **Plain white walls:** the tracker only sees surfaces where there is visual detail, so the crosshair cannot appear on a blank patch by itself. Aim once at **any textured spot on the same wall** (its edge or corner, a socket, a light switch, a picture, the border of the screen or whiteboard). The wall is then remembered, and the crosshair stays on it when you move to the plain area, up to about 1.5 m sideways and 1.2 m up or down from where it was found. If nothing appears after 5 s, the coach text says so. A completely featureless wall with no edge in view still cannot be found.
- A board held up by hand is detected like any other vertical plane, but the placed board is anchored in the room: if the board is moved afterwards, use **Re-place board**.
- Only horizontal (upward) and vertical planes are accepted. Tilted easels count as vertical while they are within about 30° of upright and ARKit/ARCore classify them as vertical.
- To go back to tables only, set `ARBoardSpace.allowVerticalSurfaces = false` on `BoardRoot.prefab` and `requestedDetectionMode = Horizontal` in `SceneBuilder.BuildARScene` (and on the ARPlaneManager in `Main.unity`).

### Contract additions for C and D (additive)

- `ICornerPlacement.IsVerticalSurface` (new): true while the surface being aimed at, picked on, or holding the placed board is vertical; changes raise `CornersChanged`. Use it for coach text ("wall" vs "table"). `FlatBoardSpace` returns false. Board space, drawing, snapping and layers do not depend on it: projection always uses the board's own plane.

- `PlacementState` has one new value, **appended at the end**: `PickingCorners`. The board is **not placed** in this state: `IsPlaced` is false and `TryScreenToBoard` returns false. D's input already blocks drawing unless `IsPlaced`, so D needs no change. C's coach text needs a case for it.
- New `ICornerPlacement` (`Assets/GeoART/Contracts/Board/ICornerPlacement.cs`), registered in `ServiceHub` by `ARBoardSpace` and, as a no-op (QuickPlace, never picking), by `FlatBoardSpace`. It is optional for consumers, so resolve it with `ServiceHub.TryResolve`. It provides the mode, the corner count, `CanAddCorner`/`CanConfirm`, `Issue` + `IssueCorner` (0-based, pick order), the live edge length, the surface and board size, the actions `TryAddCorner`/`UndoCorner`/`ClearCorners`/`ConfirmCorners`/`SetMode`, and a `CornersChanged` event (not raised per frame for the edge length). `IBoardSpace` still owns `State`/`StateChanged`.
- Board space is still `[0,1] x [0,1]` and the board is still square. Only its physical size changes (0.40 m for quick place, `min(W, H)` for corners). Model coordinates, `GridN`, snapping, fixtures and lessons are unaffected. Stroke widths scale with the board (clamped to 0.4x-2x of the 40 cm look).
- C: bind your real placement UI to `ICornerPlacement` + `IBoardSpace`, then remove `PlacementControls` from `Main.unity` (SceneBuilder adds it) and turn off `DevHud.showCoach`. Use uGUI/UI Toolkit, not IMGUI, so the tap handler's `EventSystem` hit-test ignores your buttons.

### Checks

- EditMode (Unity GUI: Window > General > Test Runner > EditMode > Run All): `BoardGeometryTests`, `CornerRectFitTests`, `CornerPickSessionTests`, `WallEstimatorTests`.
- Pure math without Unity, on the Mac:
  ```bash
  cd "/Users/root1/Desktop/Undergraduate Degree/Fall 26/CAP 4111/GeometricARt" && DOTNET_CLI_HOME=$HOME/GeoARtTmp/dotnethome DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 /Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet run --project tools/mathcheck -c Release
  ```
- iOS player scripts compile check, without building a player: `-executeMethod GeoART.EditorTools.PlayerScriptCheck.CompileIOSBatch` (menu GeoART/Build/Check iOS Player Scripts).
- The device checklist is in `CORNER_PLACEMENT_PLAN.md`, section 7 (C-01 to C-14).

## iOS build note: ARKit loader define

`ProjectSetup` (GeoART/Setup/1) adds the iOS scripting define `UNITY_XR_ARKIT_LOADER_ENABLED`. ARKit's own editor check, which normally writes this define, does nothing in batch mode. Without the define, an Xcode export from a project that has only been opened in batch mode contains no ARKit native plugin, and the app shows a black screen with no camera prompt. After any export, check that `grep -c libUnityARKit Unity-iPhone.xcodeproj/project.pbxproj` is greater than 0.
