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
        // ---- Snapping state (C0). The snap partial (EditorSession.Snap.cs) fills _candidates lazily in EnsureCandidates
        // and reacts to OnModelChanged / OnSnapSettingsChanged; until it exists the list stays empty.
        readonly List<SnapCandidate> _candidates = new List<SnapCandidate>();
        static readonly SnapCandidate[] NoCandidates = new SnapCandidate[0];
        bool _snapEnabled = true;
        bool _orthoLock;

        public EditorSession(Board board = null)
        {
            _board = board ?? GoldenBoards.Empty();
            EnsureActiveLayer();
        }

        public Board Board => _board;
        public Selection Selection { get; } = new Selection();
        public IReadOnlyList<Shape> Preview => _preview;
        public string ToolHint => _toolHint;

        /// <summary>
        /// Board -> screen measure for snap tolerances and tap detection, set by the App (ScreenSnapMetric). Null (console
        /// harness, flat tests) means board units: tolerance <see cref="FallbackSnapTolerance"/> with identity projection.
        /// Concrete property, not on IEditorSession, so wiring needs no contract change.
        /// </summary>
        public ISnapMetric SnapMetric { get; set; }

        /// <summary>Snap tolerance in board units when <see cref="SnapMetric"/> is null.</summary>
        public const double FallbackSnapTolerance = 0.02;

        /// <summary>On by default. When off, <see cref="SnapCandidates"/> is empty and nothing snaps.</summary>
        public bool SnapEnabled => _snapEnabled;

        /// <summary>Line tool H/V lock (board axes). Off by default.</summary>
        public bool OrthoLock => _orthoLock;

        /// <summary>Visible snap candidates (for the dots); empty when snapping is off. Never rebuilt per pointer Move.</summary>
        public IReadOnlyList<SnapCandidate> SnapCandidates
        {
            get
            {
                if (!_snapEnabled) return NoCandidates;
                EnsureCandidates();
                return _candidates;
            }
        }

        public void SetSnapEnabled(bool on)
        {
            if (_snapEnabled == on) return;
            _snapEnabled = on;
            OnSnapSettingsChanged();
            RaiseStateChanged();
        }

        public void SetOrthoLock(bool on)
        {
            if (_orthoLock == on) return;
            _orthoLock = on;
            OnSnapSettingsChanged();
            RaiseStateChanged();
        }

        /// <summary>Snap partial: bring _candidates up to date (lazy, dirty-flag driven). Absent = stays empty.</summary>
        partial void EnsureCandidates();

        /// <summary>Snap partial: SnapEnabled or OrthoLock changed (e.g. clear a pending tap-to-connect start).</summary>
        partial void OnSnapSettingsChanged();

        /// <summary>Snap partial: the model changed (called before BoardChanged listeners run), e.g. mark candidates dirty.</summary>
        partial void OnModelChanged(BoardChangeKind kind);
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

        public void SetGridKind(GridKind kind)
        {
            if (kind != GridKind.Square && kind != GridKind.Hex) return;
            if (_board.GridKind == kind) return;
            _board.GridKind = kind;
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

        void RaiseBoardChanged(BoardChangeKind kind)
        {
            OnModelChanged(kind);
            BoardChanged?.Invoke(kind);
        }
        void RaiseStateChanged() => StateChanged?.Invoke();
        void RaisePreviewChanged() => PreviewChanged?.Invoke();
        void RaiseToast(string message) => Toast?.Invoke(message);
    }
}
