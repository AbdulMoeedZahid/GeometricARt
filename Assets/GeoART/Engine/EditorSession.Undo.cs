// OWNER: undo history.
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// Undo/redo by board snapshots (memento), not commands. Before every undoable edit the session stores a deep clone of
    /// the whole Board plus the selection ids and a label (<see cref="RecordUndo"/>, called after every refusal guard and
    /// before the first mutation, NewId included). Undo/Redo never clone: the live board moves onto the other stack as-is
    /// and the stored one becomes live, so ids, order, doubles, layers, grid and NextId come back exactly.
    ///   Steps        strokes, Move/Scale/Duplicate, add/delete/reorder/hide/show layer, layer opacity (consecutive edits
    ///                of one layer merge until anything else happens or SelectLayer is called), grid type, tiles,
    ///                ClearBoard. Never: SelectLayer, tool/selection/style/snap changes, refused or identity actions.
    ///   Cap          <see cref="MaxUndoSteps"/>; the oldest step is dropped (then <see cref="HistoryLimitToast"/>).
    ///   Scope        from session creation; LoadBoard clears it; ClearBoard is a step; a new step clears redo.
    ///   Ids          NextId never decreases between LoadBoards (the larger one wins on every swap): no id is reissued.
    ///   Restore      cancels any open gesture and pending tap-to-connect start; keeps the current active layer if it
    ///                still exists; restores the step's selection only in a selection tool; then exactly one
    ///                BoardChanged(<see cref="HistoryChangeKind"/>), one StateChanged and one toast.
    ///   Events       pushing a step raises nothing: CanUndo/CanRedo only ever change together with a BoardChanged.
    /// </summary>
    public sealed partial class EditorSession
    {
        public const int MaxUndoSteps = 200;
        public const string NothingToUndoToast = "Nothing to undo";
        public const string NothingToRedoToast = "Nothing to redo";
        public const string HistoryLimitToast = "Nothing more to undo: only the last 200 actions are kept";

        /// <summary>Raised by Undo/Redo: every content flag (all listeners refresh) plus History.</summary>
        public const BoardChangeKind HistoryChangeKind = BoardChangeKind.Shapes | BoardChangeKind.Layers
            | BoardChangeKind.SnapPoints | BoardChangeKind.Grid | BoardChangeKind.History;

        public static string UndidToast(string label) => "Undid: " + label;
        public static string RedidToast(string label) => "Redid: " + label;

        /// <summary>Step labels (toast "Undid: label").</summary>
        public static class UndoLabels
        {
            public const string Line = "Line";
            public const string Circle = "Circle";
            public const string Move = "Move";
            public const string Scale = "Scale";
            public const string Duplicate = "Duplicate";
            public const string AddLayer = "Add layer";
            public const string DeleteLayer = "Delete layer";
            public const string LayerOrder = "Layer order";
            public const string HideLayer = "Hide layer";
            public const string ShowLayer = "Show layer";
            public const string LayerOpacity = "Layer opacity";
            public const string GridType = "Grid type";
            public const string Tiles = "Tiles";
            public const string ClearBoard = "Clear board";
        }

        /// <summary>A stored board (never live while stored), the selection ids at that time, and the step's label.</summary>
        sealed class HistoryStep
        {
            public readonly Board Board;
            public readonly HashSet<EntityId> Selection;
            public readonly string Label;

            public HistoryStep(Board board, HashSet<EntityId> selection, string label)
            {
                Board = board;
                Selection = selection;
                Label = label;
            }
        }

        readonly List<HistoryStep> _undo = new List<HistoryStep>(), _redo = new List<HistoryStep>();
        bool _historyTrimmed;              // the cap dropped a step since the last LoadBoard
        EntityId _opacityStepLayer;        // layer whose opacity edits merge into the top step; None = no merge

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Stored undo steps (concrete only, for tests and debugging).</summary>
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;

        /// <summary>Label of the step Undo/Redo would revert or re-apply; null when there is none.</summary>
        public string NextUndoLabel => _undo.Count > 0 ? _undo[_undo.Count - 1].Label : null;
        public string NextRedoLabel => _redo.Count > 0 ? _redo[_redo.Count - 1].Label : null;

        public void Undo() => StepHistory(_undo, _redo, true);
        public void Redo() => StepHistory(_redo, _undo, false);

        /// <summary>Snapshot of the live board and selection. Touches no stack.</summary>
        HistoryStep CaptureStep(string label) =>
            new HistoryStep(_board.DeepClone(), new HashSet<EntityId>(Selection.Ids), label);

        /// <summary>Stores a step (dropping the oldest at the cap), clears redo, ends opacity merging. Raises no event.</summary>
        void PushStep(HistoryStep step)
        {
            if (_undo.Count >= MaxUndoSteps)
            {
                _undo.RemoveAt(0);
                _historyTrimmed = true;
            }
            _undo.Add(step);
            _redo.Clear();
            _opacityStepLayer = EntityId.None;
        }

        void RecordUndo(string label) => PushStep(CaptureStep(label));

        void ClearHistory()
        {
            _undo.Clear();
            _redo.Clear();
            _historyTrimmed = false;
            _opacityStepLayer = EntityId.None;
        }

        void StepHistory(List<HistoryStep> from, List<HistoryStep> to, bool undo)
        {
            if (from.Count == 0)
            {
                RaiseToast(!undo ? NothingToRedoToast : _historyTrimmed ? HistoryLimitToast : NothingToUndoToast);
                return;
            }
            EndGesture(false, false);   // cancel any gesture and the pending start; StateChanged comes once, below
            HistoryStep s = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            // The live board moves over as-is: no clone; nobody else holds it after the events below.
            to.Add(new HistoryStep(_board, new HashSet<EntityId>(Selection.Ids), s.Label));
            _opacityStepLayer = EntityId.None;
            RestoreStep(s);
            RaiseToast(undo ? UndidToast(s.Label) : RedidToast(s.Label));
        }

        /// <summary>Makes the step's board live (NextId floor, active-layer and selection rules); one BoardChanged, one StateChanged.</summary>
        void RestoreStep(HistoryStep s)
        {
            int floor = _board.NextId;
            EntityId active = _board.ActiveLayerId;
            _board = s.Board;
            if (_board.NextId < floor) _board.NextId = floor;
            // The active layer is a UI choice, not history: keep the user's current one while it exists.
            if (_board.FindLayer(active) != null) _board.ActiveLayerId = active;
            EnsureActiveLayer();

            Selection.Ids.Clear();
            if (KeepsSelection(_tool))
                foreach (EntityId id in s.Selection) Selection.Ids.Add(id);
            PruneSelection();   // now, so the prune in RaiseBoardChanged finds nothing and adds no StateChanged

            RaiseBoardChanged(HistoryChangeKind);
            RaiseStateChanged();   // NormalizeTool: a transform tool with an empty restored selection falls back to Select
        }
    }
}
