using GeoART.Contracts;

namespace GeoART.Engine
{
    // OWNER: line/circle tools builder. Snap state (SnapEnabled, OrthoLock, SnapMetric) is in EditorSession.cs; the snap
    // query (TrySnap) and the candidate cache are in EditorSession.Snap.cs.
    /// <summary>
    /// Pointer routing for the Line and Circle tools. One gesture at a time:
    ///   Down (on the board)  Line: start point A.  Circle: centre. Snapped (see EditorSession.Snap.cs).
    ///   Move                 ghost in Preview (Line: A to p. Circle: radius |p - centre| plus a thin radius line), with p
    ///                        snapped. A hover ring marks the candidate the current point snapped to.
    ///   Up                   a drag commits to the layer that was active at Down if the line length / radius is at least
    ///                        <see cref="MinStrokeLength"/>, else discards. Raises BoardChanged(Shapes).
    ///                        A tap (the pointer never left the tap slop) never goes through that commit: in the Line
    ///                        tool it drives tap-to-connect, in the Circle tool it does nothing.
    ///   Cancel               discards.
    /// Off-board or non-finite Move/Up positions keep the last valid point. The cursor follows the snapped pointer during
    /// a gesture and clears afterwards. A hidden or missing active layer refuses the gesture with a toast.
    ///
    /// Tap-to-connect (Line tool): a tap on a candidate with no pending start makes it the pending start (a ring stays
    /// in Preview). A tap on a different candidate commits a line between the two exact candidate positions and clears
    /// pending; a tap on the same candidate clears it. A tap off any candidate, a drag, Cancel, a tool switch, a board
    /// replace or turning snapping off clears pending.
    ///
    /// H/V lock (E3, <see cref="OrthoLock"/>, Line tool only): the start snaps as usual; the end is the anchor plus the
    /// drag along the dominant board axis (|dx| &gt;= |dy| horizontal), copying the anchor's other coordinate exactly,
    /// clamped to the board and alignment-snapped along the axis (TryOrthoEnd in EditorSession.Snap.cs). Toggling it
    /// mid-gesture re-tracks the ghost at once. With the lock on, tap-to-connect commits only a pair that shares X or Y
    /// exactly; any other pair commits nothing, clears pending and raises <see cref="OrthoLockToast"/>.
    /// </summary>
    public sealed partial class EditorSession
    {
        /// <summary>Shortest line / smallest radius that commits, in board units (4 mm on the 40 cm board).</summary>
        public const double MinStrokeLength = 0.01;

        public const string HiddenLayerToast = "Layer is hidden: double-tap it in the sidebar to show it";
        public const string ComingSoonToast = "Coming soon";
        public const string OrthoLockToast = "H/V lock is on: tap two points in the same row or column";

        ToolId _tool = ToolId.Line;
        Vec2? _cursor;
        bool _gestureOpen;
        ToolId _gestureTool;
        EntityId _gestureLayer;
        Vec2 _anchor, _current;   // snapped start and current point
        Vec2 _lastRaw;            // last raw on-board pointer position of the gesture

        // Snap state of the open gesture.
        bool _downHit;            // the Down position snapped
        SnapCandidate _downCand;  // ... to this candidate (the tap's candidate)
        bool _hoverOn;            // the current point snapped: hover ring at _current

        // Tap detection: measured from the raw Down position, in screen px with a metric, else board units.
        bool _stillTap;
        bool _tapScreen;
        ISnapMetric _tapMetric;
        Vec2 _tapOrigin;
        double _tapSlop2;

        // Tap-to-connect.
        bool _hasPending;
        Vec2 _pending;

        // Ghosts and rings are reused (never in the model), so a drag allocates nothing per Move.
        readonly LineShape _ghostLine = new LineShape();
        readonly CircleShape _ghostCircle = new CircleShape();
        readonly LineShape _ghostRadius = new LineShape();
        readonly CircleShape _hoverRing = new CircleShape();
        readonly CircleShape _pendingRing = new CircleShape();

        public ToolId ActiveTool => _tool;
        public StampKind ActiveStamp => StampKind.Square;
        /// <summary>Single source of truth is <see cref="Style"/>.Dash; the quick toggle flips it.</summary>
        public bool DottedLine => _style.Dash == LineStyle.Dotted;
        public Vec2? Cursor => _cursor;

        /// <summary>True between a Down that started a stroke and its Up/Cancel.</summary>
        public bool GestureActive => _gestureOpen;

        /// <summary>The pending tap-to-connect start (an exact candidate position), or null.</summary>
        public Vec2? PendingConnectStart => _hasPending ? _pending : (Vec2?)null;

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
            if (!e.OnBoard || !e.BoardPos.IsFinite || (_tool != ToolId.Line && _tool != ToolId.Circle))
            {
                if (DropPending()) RaisePreviewChanged();   // a tap off the board is a tap off any candidate
                return;
            }

            Layer layer = ActiveLayer;
            if (layer == null || !layer.Visible)
            {
                RaiseToast(HiddenLayerToast);
                if (DropPending()) RaisePreviewChanged();
                return;
            }
            _gestureOpen = true;
            _gestureTool = _tool;
            _gestureLayer = layer.Id;
            _lastRaw = e.BoardPos;
            _downHit = TrySnap(e.BoardPos, out _downCand);
            _hoverOn = _downHit;
            _anchor = _current = _downHit ? _downCand.Position : e.BoardPos;
            _cursor = _anchor;

            _stillTap = true;
            _tapMetric = SnapMetric;
            _tapScreen = TryScreenSpace(e.BoardPos, out _tapOrigin, out double tol);
            double slop = TapSlopFraction * tol;
            _tapSlop2 = slop * slop;

            RebuildGhost();
        }

        void TrackGesture(in PointerEvent e)
        {
            bool dropped = MeasureTap(e);
            if (!e.OnBoard || !e.BoardPos.IsFinite)
            {
                if (dropped) RebuildGhost();
                return;
            }
            if (e.BoardPos == _lastRaw && !dropped) return;
            _lastRaw = e.BoardPos;
            Vec2 p = ResolvePoint(e.BoardPos, out bool hit);
            if (p == _current && hit == _hoverOn && !dropped) return;   // nothing visible changed
            _current = p;
            _cursor = p;
            _hoverOn = hit;
            RebuildGhost();
        }

        /// <summary>
        /// The current point for a raw pointer position: the H/V-locked end for the Line tool with the lock on, else
        /// the plain snap. <paramref name="hit"/> is true if it snapped (hover ring).
        /// </summary>
        Vec2 ResolvePoint(Vec2 raw, out bool hit)
        {
            if (_gestureTool == ToolId.Line && _orthoLock)
            {
                hit = TryOrthoEnd(raw, out Vec2 end);
                return end;
            }
            hit = TrySnap(raw, out SnapCandidate c);
            return hit ? c.Position : raw;
        }

        /// <summary>
        /// The H/V lock was toggled: an open Line gesture re-tracks from its last raw pointer position at once (ghost,
        /// cursor, hover ring) and raises PreviewChanged. Circle gestures and idle sessions are unaffected.
        /// </summary>
        void OnOrthoLockToggled()
        {
            if (!_gestureOpen || _gestureTool != ToolId.Line) return;
            Vec2 p = ResolvePoint(_lastRaw, out bool hit);
            _current = p;
            _cursor = p;
            _hoverOn = hit;
            RebuildGhost();
        }

        /// <summary>
        /// Updates tap detection with one pointer sample. Once the pointer leaves the slop (or the board, or stops being
        /// projectable) the gesture is a drag for good, and a pending tap-to-connect start is dropped. Returns true if
        /// that dropped a pending start.
        /// </summary>
        bool MeasureTap(in PointerEvent e)
        {
            if (!_stillTap) return false;
            bool inside = false;
            if (e.OnBoard && e.BoardPos.IsFinite)
            {
                Vec2 q = e.BoardPos;
                bool ok = !_tapScreen || (_tapMetric.TryToScreen(e.BoardPos, out q) && q.IsFinite);
                if (ok)
                {
                    double dx = q.X - _tapOrigin.X, dy = q.Y - _tapOrigin.Y;
                    inside = dx * dx + dy * dy < _tapSlop2;
                }
            }
            if (inside) return false;
            _stillTap = false;
            return DropPending();
        }

        /// <summary>
        /// Closes the open gesture (commit = Up, else discard) and clears ghosts, hover ring and cursor. A discard
        /// (Cancel, tool switch, board replace, lost Up) also clears the pending tap-to-connect start, gesture or not.
        /// The pending ring, if any, stays in Preview.
        /// </summary>
        void EndGesture(bool commit)
        {
            bool hadGesture = _gestureOpen;
            _gestureOpen = false;
            bool hadVisuals = _preview.Count > 0 || _cursor.HasValue;
            _preview.Clear();
            _cursor = null;
            _hoverOn = false;
            if (!commit) _hasPending = false;
            else if (hadGesture)
            {
                if (_stillTap) Tap();
                else Commit();
            }
            AddPendingRing();
            if (hadVisuals || _preview.Count > 0) RaisePreviewChanged();
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

        /// <summary>Tap-to-connect step for a finished tap. Never commits a zero-length (or too short) line.</summary>
        void Tap()
        {
            if (_gestureTool != ToolId.Line) return;   // Circle: a tap draws nothing
            if (!_downHit)
            {
                _hasPending = false;                   // tap off any candidate (also: snapping off)
                return;
            }
            Vec2 q = _downCand.Position;
            if (!_hasPending)
            {
                _hasPending = true;
                _pending = q;
                return;
            }
            Vec2 a = _pending;
            _hasPending = false;
            if (a == q) return;                         // same candidate again: toggle off
            if (_orthoLock && a.X != q.X && a.Y != q.Y)
            {
                RaiseToast(OrthoLockToast);             // H/V lock: only a pair in the same row or column connects
                return;
            }
            if (Vec2.Distance(a, q) < MinStrokeLength) return;

            Layer layer = _board.FindLayer(_gestureLayer);
            if (layer == null || !layer.Visible)
            {
                RaiseToast(HiddenLayerToast);
                return;
            }
            layer.Shapes.Add(new LineShape(_board.NewId(), a, q, _style));
            RaiseBoardChanged(BoardChangeKind.Shapes);
        }

        /// <summary>Clears the pending start and removes its ring. True if there was one (caller raises PreviewChanged).</summary>
        bool DropPending()
        {
            if (!_hasPending) return false;
            _hasPending = false;
            _preview.Remove(_pendingRing);
            return true;
        }

        StrokeStyle RingStyle => new StrokeStyle(_style.Color, LineWidth.Thin, LineStyle.Solid);

        void AddPendingRing()
        {
            if (!_hasPending) return;
            _pendingRing.Center = _pending;
            _pendingRing.Radius = HoverRingRadius;
            _pendingRing.Style = RingStyle;
            _preview.Add(_pendingRing);
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
            if (_hoverOn)
            {
                _hoverRing.Center = _current;
                _hoverRing.Radius = HoverRingRadius;
                _hoverRing.Style = RingStyle;
                _preview.Add(_hoverRing);
            }
            AddPendingRing();
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
                case ToolId.Line: return "Line: drag from start to end, or tap two points";
                case ToolId.Circle: return "Circle: drag from centre outwards";
                default: return "";
            }
        }

        public void ToggleDottedLine() =>
            Style = _style.WithDash(DottedLine ? LineStyle.Solid : LineStyle.Dotted);   // raises StateChanged

        // Quick-menubar snap actions and stamps come later. SnapEnabled/SetSnapEnabled/OrthoLock live in EditorSession.cs.
        public void PlaceSnapPoint() { }
        public void ClickAtCursor() { }
        public void LineBetweenSnapPoints() { }
        public void SetStamp(StampKind k) { }
    }
}
