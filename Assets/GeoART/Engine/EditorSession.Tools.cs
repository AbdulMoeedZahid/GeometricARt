using GeoART.Contracts;

namespace GeoART.Engine
{
    // OWNER: line/circle tools builder. Snapping is out of scope for now (SnapEnabled false, candidates empty).
    /// <summary>
    /// Pointer routing for the Line and Circle tools. One gesture at a time:
    ///   Down (on the board)  Line: start point A.  Circle: centre.
    ///   Move                 ghost in Preview (Line: A to p. Circle: radius |p - centre| plus a thin radius line).
    ///   Up                   commits to the layer that was active at Down if the line length / radius is at least
    ///                        <see cref="MinStrokeLength"/>, else discards. Raises BoardChanged(Shapes).
    ///   Cancel               discards.
    /// Off-board or non-finite Move/Up positions keep the last valid point. The cursor follows the pointer during a
    /// gesture and clears afterwards. A hidden or missing active layer refuses the gesture with a toast.
    /// </summary>
    public sealed partial class EditorSession
    {
        /// <summary>Shortest line / smallest radius that commits, in board units (4 mm on the 40 cm board).</summary>
        public const double MinStrokeLength = 0.01;

        public const string HiddenLayerToast = "Layer is hidden: double-tap it in the sidebar to show it";
        public const string ComingSoonToast = "Coming soon";

        ToolId _tool = ToolId.Line;
        Vec2? _cursor;
        bool _gestureOpen;
        ToolId _gestureTool;
        EntityId _gestureLayer;
        Vec2 _anchor, _current;

        // Ghosts are reused (never in the model), so a drag allocates nothing per Move.
        readonly LineShape _ghostLine = new LineShape();
        readonly CircleShape _ghostCircle = new CircleShape();
        readonly LineShape _ghostRadius = new LineShape();

        public ToolId ActiveTool => _tool;
        public StampKind ActiveStamp => StampKind.Square;
        public bool SnapEnabled => false;
        /// <summary>Single source of truth is <see cref="Style"/>.Dash; the quick toggle flips it.</summary>
        public bool DottedLine => _style.Dash == LineStyle.Dotted;
        public Vec2? Cursor => _cursor;

        /// <summary>True between a Down that started a stroke and its Up/Cancel.</summary>
        public bool GestureActive => _gestureOpen;

        public void SetCursor(Vec2 boardPos, bool onBoard)
        {
            Vec2? next = onBoard && boardPos.IsFinite ? boardPos : (Vec2?)null;
            if (next == _cursor) return;
            _cursor = next;
            RaisePreviewChanged();
        }

        public void Pointer(in PointerEvent e)
        {
            switch (e.Phase)
            {
                case PointerPhase.Down: BeginGesture(e); break;
                case PointerPhase.Move: if (_gestureOpen) TrackGesture(e); break;
                case PointerPhase.Up:
                    if (!_gestureOpen) break;
                    TrackGesture(e);
                    EndGesture(true);
                    break;
                default: EndGesture(false); break;   // Cancel
            }
        }

        void BeginGesture(in PointerEvent e)
        {
            if (_gestureOpen) EndGesture(false);   // a lost Up: never commit a stale stroke
            if (!e.OnBoard || !e.BoardPos.IsFinite) return;
            if (_tool != ToolId.Line && _tool != ToolId.Circle) return;

            Layer layer = ActiveLayer;
            if (layer == null || !layer.Visible)
            {
                RaiseToast(HiddenLayerToast);
                return;
            }
            _gestureOpen = true;
            _gestureTool = _tool;
            _gestureLayer = layer.Id;
            _anchor = _current = e.BoardPos;
            _cursor = e.BoardPos;
            RebuildGhost();
        }

        void TrackGesture(in PointerEvent e)
        {
            if (!e.OnBoard || !e.BoardPos.IsFinite) return;
            if (e.BoardPos == _current) return;
            _current = e.BoardPos;
            _cursor = e.BoardPos;
            RebuildGhost();
        }

        /// <summary>Closes the open gesture (commit or discard) and clears ghosts and cursor. No-op without a gesture.</summary>
        void EndGesture(bool commit)
        {
            bool hadGesture = _gestureOpen;
            _gestureOpen = false;
            bool hadVisuals = _preview.Count > 0 || _cursor.HasValue;
            _preview.Clear();
            _cursor = null;
            if (hadGesture && commit) Commit();
            if (hadVisuals) RaisePreviewChanged();
        }

        void Commit()
        {
            double size = Vec2.Distance(_anchor, _current);
            if (size < MinStrokeLength) return;

            Layer layer = _board.FindLayer(_gestureLayer);
            if (layer == null) return;   // deleted mid-gesture
            if (!layer.Visible)
            {
                RaiseToast(HiddenLayerToast);
                return;
            }
            Shape shape = _gestureTool == ToolId.Circle
                ? new CircleShape(_board.NewId(), _anchor, size, _style)
                : (Shape)new LineShape(_board.NewId(), _anchor, _current, _style);
            layer.Shapes.Add(shape);
            RaiseBoardChanged(BoardChangeKind.Shapes);
        }

        void RebuildGhost()
        {
            _preview.Clear();
            if (_gestureTool == ToolId.Circle)
            {
                double r = Vec2.Distance(_anchor, _current);
                if (r > 0)
                {
                    _ghostCircle.Center = _anchor;
                    _ghostCircle.Radius = r;
                    _ghostCircle.Style = _style;
                    _preview.Add(_ghostCircle);
                    _ghostRadius.A = _anchor;
                    _ghostRadius.B = _current;
                    _ghostRadius.Style = new StrokeStyle(_style.Color, LineWidth.Thin, LineStyle.Dotted);
                    _preview.Add(_ghostRadius);
                }
            }
            else if (_current != _anchor)
            {
                _ghostLine.A = _anchor;
                _ghostLine.B = _current;
                _ghostLine.Style = _style;
                _preview.Add(_ghostLine);
            }
            RaisePreviewChanged();
        }

        /// <summary>The board is about to be swapped (LoadBoard/ClearBoard): drop the open gesture without committing.</summary>
        partial void OnBoardReplaced() => EndGesture(false);

        public void SetTool(ToolId t)
        {
            if (t != ToolId.None && t != ToolId.Line && t != ToolId.Circle)
            {
                RaiseToast(ComingSoonToast);
                return;
            }
            if (t == _tool) return;
            EndGesture(false);
            _tool = t;
            string hint = HintFor(t);
            if (hint != _toolHint) SetToolHint(hint);
            else RaiseStateChanged();
        }

        static string HintFor(ToolId t)
        {
            switch (t)
            {
                case ToolId.Line: return "Line: drag from start to end";
                case ToolId.Circle: return "Circle: drag from centre outwards";
                default: return "";
            }
        }

        public void ToggleDottedLine() =>
            Style = _style.WithDash(DottedLine ? LineStyle.Solid : LineStyle.Dotted);   // raises StateChanged

        // Snap points and stamps come later.
        public void PlaceSnapPoint() { }
        public void ClickAtCursor() { }
        public void LineBetweenSnapPoints() { }
        public void SetStamp(StampKind k) { }
        public void SetSnapEnabled(bool on) { }
    }
}
