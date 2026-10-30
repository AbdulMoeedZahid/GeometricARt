# Layers

Layer sidebar on the middle-left of the screen, plus the engine commands behind it.

## Gestures (sidebar)

| Where | Gesture | Effect |
|---|---|---|
| `+` (top) | tap | Add "Layer N" at the TOP and select it. Max 12 layers (toast "Max 12 layers"). |
| `-` (bottom) | tap | Delete the BOTTOM layer and everything on it (no undo yet). The last layer can't be deleted (toast). |
| dash | tap | Select the layer (new shapes go there). Happens on release, with no double-tap delay. |
| dash | double tap | Toggle the layer's visibility. Second tap must start within 0.3 s of the first release. |
| dash | hold 0.45 s | Select + open the opacity popup next to the dash: name, 0-100% slider (applied live), close `x`. A tap anywhere outside closes it. |
| dash | slide up/down | After more than 8 ref px of movement (before the hold fires) the dash lifts, follows the finger, and the other dashes open a gap. Release commits the move. |

One dash per layer, top layer at the top (reverse of `Board.Layers`). Selected is a long orange dash; a hidden layer is a hollow,
dimmed dash; a visible dash's alpha follows the layer opacity (30-100%). A short name label appears to the right when the
selection changes ("Layer 3", "Layer 3 hidden", "Deleted Layer 1").

Layout (390x844 reference, inside `Screen.safeArea`): pill 50 wide, 6 from the left edge, vertically centred, at most 46% of the
screen height. `+`/`-` are 42 tall; dash rows are 34 tall, shrinking to fit (down to 22 at 12 layers). sortingOrder 110.
Toasts: top-centre about 24% down, 1.8 s with fade, sortingOrder 120, never block touches.

## Engine (`EditorSession.Layers.cs`, Board.Layers index 0 = bottom)

- `AddLayer()` adds at the top, makes it active, names it "Layer N" (N higher than any existing "Layer k" and than any N
  already used on this board; restarts after LoadBoard/ClearBoard). Returns `EntityId.None` and toasts at 12 layers.
- `SelectLayer(id)` sets `ActiveLayerId` if the layer exists.
- `MoveLayer(id, newIndex)` clamps the index to `[0, Count-1]`; no-op (no events) if unchanged.
- `DeleteBottomLayer()` removes `Layers[0]` with its shapes and snap points (and drops its shapes from the selection). With
  one layer left it toasts "Can't delete the only layer". If the deleted layer was active, the new bottom becomes active.
- `SetLayerVisible(id, bool)`, `SetLayerOpacity(id, double)` (clamped to [0,1], NaN ignored).
- Every change raises `BoardChanged(Layers)` (delete adds `Shapes|SnapPoints`); add/select/delete also raise `StateChanged`.
  Unknown ids and unchanged values raise nothing.

## Files

- `Assets/GeoART/Engine/EditorSession.Layers.cs` engine commands
- `Assets/GeoART/App/LayerSidebar.cs` the sidebar, gestures and opacity popup
- `Assets/GeoART/App/LayerPointerRelay.cs` forwards EventSystem pointer events from dash rows / popup blocker
- `Assets/GeoART/App/LayerUiKit.cs` runtime uGUI helpers (canvas, procedural rounded sprite, scale factor), also used by ToastView
- `Assets/GeoART/App/ToastView.cs` toast display
- `tools/enginecheck/LayerChecks.cs` engine checks (run with the enginecheck command in the collab PROTOCOL)
