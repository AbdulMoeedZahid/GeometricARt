using System;
using System.Collections.Generic;

namespace GeoART.Contracts
{
    /// <summary>
    /// The central seam: engine facade used by UI (C), input (D), lesson (D) and rendering (A).
    /// Real provider: B's EditorSession. Stand-ins: StubEditorSession (A), MockEditorSession (C), LoggingEditorSession (D).
    /// </summary>
    public interface IEditorSession
    {
        Board Board { get; }                                      // read-only by convention
        ToolId ActiveTool { get; }
        StampKind ActiveStamp { get; }
        bool SnapEnabled { get; }
        bool OrthoLock { get; }                                   // Line tool: new lines horizontal/vertical in board axes
        bool DottedLine { get; }
        bool FillErase { get; }                                   // Fill tool: taps clear fills instead of filling ("no fill")
        StrokeStyle Style { get; set; }                           // palette colour + width (Settings)
        Selection Selection { get; }
        Vec2? Cursor { get; }
        IReadOnlyList<Shape> Preview { get; }                     // transient tool ghosts, never in the model
        IReadOnlyList<SnapCandidate> SnapCandidates { get; }      // user + derived points (for dots)
        string ToolHint { get; }

        // pointer path (called by touch input)
        void SetCursor(Vec2 boardPos, bool onBoard);
        void Pointer(in PointerEvent e);

        // quick menubar (sketches 1/3)
        void PlaceSnapPoint();
        void ClickAtCursor();
        void LineBetweenSnapPoints();
        void ToggleDottedLine();

        // tools grid (sketches 2/4)
        void SetTool(ToolId t);
        void SetStamp(StampKind k);
        void SetSnapEnabled(bool on);
        void SetOrthoLock(bool on);                               // raises StateChanged when it changes
        void ClearSelection();                                    // empties the selection (StateChanged if it was not empty)
        void DeleteSelection();                                   // removes the selected shapes (visible layers); one "Delete" step
        void ExtendSelection();                                   // pushes the selected lines' ends to the next drawn shape; one "Extend" step (none if nothing moved)

        // layers + grid
        EntityId AddLayer();
        void SelectLayer(EntityId id);
        void MoveLayer(EntityId id, int newIndex);
        void DeleteBottomLayer();
        void SetLayerVisible(EntityId id, bool visible);
        void SetLayerOpacity(EntityId id, double opacity);       // clamped to [0,1]
        void SetLayerBands(EntityId id, LayerBands bands);       // sanitized; one undo step ("Bands", or "Band width" merged per layer)
        void RestyleSelection(StyleParts parts, StrokeStyle style);   // selected shapes on visible layers; one "Style" step
        void SetFillErase(bool erase);                            // the colour picker's "no fill"; StateChanged; reset when leaving Fill
        void SetGrid(int n);                                      // tiles per side, clamped to [MinGridN, MaxGridN]
        void SetGridKind(GridKind kind);                          // square or hex tiles; read back from Board.GridKind

        // history + lifecycle
        bool CanUndo { get; }                                     // true after any undoable edit (strokes, transforms, layer and grid edits, ClearBoard); changes only together with BoardChanged
        bool CanRedo { get; }
        void Undo();                                              // reverts the last edit (cancels an open gesture first); toasts "Nothing to undo" when empty
        void Redo();                                              // re-applies the last undone edit; any new edit clears it
        void ClearBoard();                                        // undoable
        void LoadBoard(Board b);                                  // resets history (CanUndo/CanRedo false)

        event Action<BoardChangeKind> BoardChanged;               // model changed; Undo/Redo raise it with BoardChangeKind.History
        event Action StateChanged;                                // tool/selection/style/hint; also after Undo/Redo (undo availability: see CanUndo)
        event Action PreviewChanged;                              // ghosts/cursor (high frequency, renderer only)
        event Action<string> Toast;                               // UI shows toasts
    }
}
