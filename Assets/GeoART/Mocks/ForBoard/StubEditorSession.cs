using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Mocks
{
    /// <summary>
    /// A's stand-in for B's EditorSession: serves a fixed board so BoardView can be built and checked before the
    /// engine exists. It never draws. Cursor, grid, layer visibility and Load/Clear work so rendering paths can be
    /// exercised; every other command is a no-op. Not for production scenes once EngineHost is available.
    /// </summary>
    public sealed class StubEditorSession : IEditorSession
    {
        Board _board;
        Vec2? _cursor;
        readonly List<SnapCandidate> _candidates = new List<SnapCandidate>();
        static readonly IReadOnlyList<Shape> s_NoPreview = Array.Empty<Shape>();

        public StubEditorSession(Board board)
        {
            _board = board ?? GoldenBoards.Empty();
            RebuildCandidates();
        }

        public Board Board => _board;
        public ToolId ActiveTool => ToolId.None;
        public StampKind ActiveStamp => StampKind.Square;
        public bool SnapEnabled { get; private set; } = true;
        public bool DottedLine => false;
        public StrokeStyle Style { get; set; } = StrokeStyle.Default;
        public Selection Selection { get; } = new Selection();
        public Vec2? Cursor => _cursor;
        public IReadOnlyList<Shape> Preview => s_NoPreview;
        public IReadOnlyList<SnapCandidate> SnapCandidates => _candidates;
        public string ToolHint => "Stub session (A sandbox): rendering only";
        public bool CanUndo => false;
        public bool CanRedo => false;

        public event Action<BoardChangeKind> BoardChanged;
        public event Action StateChanged;
        public event Action PreviewChanged;
#pragma warning disable 67
        public event Action<string> Toast;
#pragma warning restore 67

        public void SetCursor(Vec2 boardPos, bool onBoard)
        {
            _cursor = onBoard ? boardPos : (Vec2?)null;
            PreviewChanged?.Invoke();
        }

        public void Pointer(in PointerEvent e) => SetCursor(e.BoardPos, e.OnBoard && e.Phase != PointerPhase.Cancel);

        public void SetSnapEnabled(bool on)
        {
            SnapEnabled = on;
            StateChanged?.Invoke();
        }

        public void SetGrid(int n)
        {
            _board.GridN = Math.Max(Board.MinGridN, Math.Min(Board.MaxGridN, n));
            RebuildCandidates();
            BoardChanged?.Invoke(BoardChangeKind.Grid);
        }

        public void SetLayerVisible(EntityId id, bool visible)
        {
            var layer = _board.FindLayer(id);
            if (layer == null) return;
            layer.Visible = visible;
            RebuildCandidates();
            BoardChanged?.Invoke(BoardChangeKind.Layers);
        }

        public void ClearBoard() => LoadBoard(GoldenBoards.Empty());

        public void LoadBoard(Board b)
        {
            _board = b ?? GoldenBoards.Empty();
            RebuildCandidates();
            BoardChanged?.Invoke(BoardChangeKind.Reset);
            StateChanged?.Invoke();
        }

        // Engine commands: not implemented by the stub.
        public void PlaceSnapPoint() { }
        public void ClickAtCursor() { }
        public void LineBetweenSnapPoints() { }
        public void ToggleDottedLine() { }
        public void SetTool(ToolId t) { }
        public void SetStamp(StampKind k) { }
        public EntityId AddLayer() => EntityId.None;
        public void SelectLayer(EntityId id) { }
        public void MoveLayer(EntityId id, int newIndex) { }
        public void DeleteBottomLayer() { }
        public void Undo() { }
        public void Redo() { }

        /// <summary>Cell corners, edge midpoints and centres plus user points on visible layers (rendering preview only).</summary>
        void RebuildCandidates()
        {
            _candidates.Clear();
            int n = Math.Max(1, _board.GridN);
            for (int j = 0; j <= 2 * n; j++)
                for (int i = 0; i <= 2 * n; i++)
                {
                    var kind = (i % 2 == 0 && j % 2 == 0) ? SnapKind.CellCorner
                             : (i % 2 == 1 && j % 2 == 1) ? SnapKind.CellCenter
                             : SnapKind.CellEdgeMid;
                    _candidates.Add(new SnapCandidate(new Vec2(i / (2.0 * n), j / (2.0 * n)), kind));
                }
            foreach (var layer in _board.Layers)
                if (layer.Visible)
                    foreach (var p in layer.SnapPoints)
                        _candidates.Add(new SnapCandidate(p.Position, SnapKind.UserPoint));
        }
    }
}
