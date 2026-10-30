using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// B's real <see cref="IEditorSession"/>: owns the Board and mutates it through commands. Pure C# (no UnityEngine),
    /// so it can be exercised from a console harness. Split by owner:
    ///   EditorSession.cs         core state, events, grid, load/clear (shared; keep small)
    ///   EditorSession.Layers.cs  add/select/move/delete/visibility/opacity of layers
    ///   EditorSession.Tools.cs   pointer routing, Line and Circle tools, preview ghosts, cursor
    /// Undo/redo is not implemented yet (CanUndo/CanRedo stay false).
    /// </summary>
    public sealed partial class EditorSession : IEditorSession
    {
        Board _board;
        StrokeStyle _style = StrokeStyle.Default;
        string _toolHint = "";
        readonly List<Shape> _preview = new List<Shape>();
        readonly List<SnapCandidate> _candidates = new List<SnapCandidate>();   // snapping comes later: always empty

        public EditorSession(Board board = null)
        {
            _board = board ?? GoldenBoards.Empty();
            EnsureActiveLayer();
        }

        public Board Board => _board;
        public Selection Selection { get; } = new Selection();
        public IReadOnlyList<Shape> Preview => _preview;
        public IReadOnlyList<SnapCandidate> SnapCandidates => _candidates;
        public string ToolHint => _toolHint;
        public bool CanUndo => false;
        public bool CanRedo => false;

        public StrokeStyle Style
        {
            get => _style;
            set
            {
                if (_style.Equals(value)) return;
                _style = value;
                RaiseStateChanged();
            }
        }

        public event Action<BoardChangeKind> BoardChanged;
        public event Action StateChanged;
        public event Action PreviewChanged;
        public event Action<string> Toast;

        /// <summary>The layer new shapes go into; null only if the board has no layers.</summary>
        public Layer ActiveLayer => _board.FindLayer(_board.ActiveLayerId);

        public void SetGrid(int n)
        {
            n = Math.Max(Board.MinGridN, Math.Min(Board.MaxGridN, n));
            if (_board.GridN == n) return;
            _board.GridN = n;
            RaiseBoardChanged(BoardChangeKind.Grid);
        }

        public void Undo() { }
        public void Redo() { }

        public void ClearBoard() => LoadBoard(GoldenBoards.Empty());

        public void LoadBoard(Board b)
        {
            OnBoardReplaced();
            _board = b ?? GoldenBoards.Empty();
            EnsureActiveLayer();
            Selection.Ids.Clear();
            RaiseBoardChanged(BoardChangeKind.Reset);
            RaiseStateChanged();
            RaisePreviewChanged();
        }

        /// <summary>Guarantees at least one layer and a valid ActiveLayerId.</summary>
        void EnsureActiveLayer()
        {
            if (_board.Layers.Count == 0)
                _board.Layers.Add(new Layer(_board.NewId(), "Layer 1"));
            if (_board.FindLayer(_board.ActiveLayerId) == null)
                _board.ActiveLayerId = _board.Layers[_board.Layers.Count - 1].Id;
        }

        /// <summary>Tools partial: abort any unfinished gesture and clear ghosts before the board is swapped.</summary>
        partial void OnBoardReplaced();

        void SetToolHint(string hint)
        {
            hint = hint ?? "";
            if (_toolHint == hint) return;
            _toolHint = hint;
            RaiseStateChanged();
        }

        void RaiseBoardChanged(BoardChangeKind kind) => BoardChanged?.Invoke(kind);
        void RaiseStateChanged() => StateChanged?.Invoke();
        void RaisePreviewChanged() => PreviewChanged?.Invoke();
        void RaiseToast(string message) => Toast?.Invoke(message);
    }
}
