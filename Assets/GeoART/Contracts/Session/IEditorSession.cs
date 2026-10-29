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
        bool DottedLine { get; }
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

        // layers + grid
        EntityId AddLayer();
        void SelectLayer(EntityId id);
        void MoveLayer(EntityId id, int newIndex);
        void DeleteBottomLayer();
        void SetLayerVisible(EntityId id, bool visible);
        void SetGrid(int n);

        // history + lifecycle
        bool CanUndo { get; }
        bool CanRedo { get; }
        void Undo();
        void Redo();
        void ClearBoard();
        void LoadBoard(Board b);                                  // resets history

        event Action<BoardChangeKind> BoardChanged;               // model changed
        event Action StateChanged;                                // tool/selection/style/undo availability/hint
        event Action PreviewChanged;                              // ghosts/cursor (high frequency, renderer only)
        event Action<string> Toast;                               // UI shows toasts
    }
}
