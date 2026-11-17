using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    // OWNER: select tool.
    /// <summary>
    /// The Select tool (<see cref="ToolId.SelectMove"/>; Move/Scale/Duplicate: EditorSession.Transform.cs). It only chooses shapes: it never edits the
    /// model, never raises BoardChanged and never adds an undo step. One gesture at a time, routed from
    /// EditorSession.Tools.cs:
    ///   Down    on the board, also when the active layer is hidden. Remembers the selection, so Cancel can restore it.
    ///   Move    inside the tap slop: nothing is drawn. Past it: a marquee from the raw Down point to the current
    ///           (filtered, never snapped) point, drawn in Preview as 4 dotted lines, and the selection is REPLACED by the
    ///           shapes it fully encloses (live; StateChanged only when the set changes).
    ///   Up      a drag keeps the enclosed set and toasts the count (<see cref="NothingSelectedToast"/> when empty). A tap
    ///           selects the shape whose stroke is nearest the Down point within <see cref="PickTolerance"/> (topmost wins
    ///           a tie) and toasts "1 shape selected"; a tap on empty board clears the selection without a toast.
    ///   Cancel  restores the selection from before the gesture and removes the marquee.
    /// Only shapes on visible layers are selectable. Every model change prunes ids that are no longer on a visible layer
    /// (<see cref="PruneSelection"/>, from RaiseBoardChanged), so showing a layer again never re-selects them. Switching to
    /// a tool that does not use the selection (<see cref="KeepsSelection"/>) clears it; so do ClearBoard and LoadBoard.
    /// Everything on the Move path is allocation-free (reused shapes and sets, index loops).
    /// </summary>
    public sealed partial class EditorSession
    {
        public const string SelectHintToast = "Select: drag a box around whole shapes, or tap one";
        public const string NothingSelectedToast = "Nothing selected: the box must enclose whole shapes";

        /// <summary>"1 shape selected" or "N shapes selected".</summary>
        public static string SelectedCountToast(int n) => n == 1 ? "1 shape selected" : n + " shapes selected";

        /// <summary>Board-distance cap for tap-picking a stroke, in board units (as <see cref="MaxSnapBoardDistance"/>).</summary>
        public const double MaxPickBoardDistance = 0.03;

        const double PickProbe = 0.01;   // board step used to measure px per board unit at the tap point

        readonly HashSet<EntityId> _selectionBefore = new HashSet<EntityId>();   // restored on Cancel
        readonly HashSet<EntityId> _selectScratch = new HashSet<EntityId>();     // enclosed set of the current marquee
        readonly LineShape[] _marqueeEdges = { new LineShape(), new LineShape(), new LineShape(), new LineShape() };

        /// <summary>Number of selected shapes.</summary>
        public int SelectionCount => Selection.Ids.Count;

        /// <summary>A selected shape and the layer it is on.</summary>
        public readonly struct SelectedShape
        {
            public readonly Layer Layer;
            public readonly Shape Shape;

            public SelectedShape(Layer layer, Shape shape) { Layer = layer; Shape = shape; }
        }

        /// <summary>
        /// Clears <paramref name="into"/> and fills it with the selected shapes on visible layers, bottom layer first and
        /// in each layer's shape order (drawing order).
        /// </summary>
        public void GetSelectedShapes(List<SelectedShape> into)
        {
            if (into == null) return;
            into.Clear();
            var ids = Selection.Ids;
            if (ids.Count == 0) return;
            var layers = _board.Layers;
            for (int li = 0; li < layers.Count; li++)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = 0; si < shapes.Count; si++)
                {
                    Shape s = shapes[si];
                    if (s != null && ids.Contains(s.Id)) into.Add(new SelectedShape(layer, s));
                }
            }
        }

        /// <summary>Union of the selected shapes' bounds (board units). False if nothing with bounds is selected.</summary>
        public bool TryGetSelectionBounds(out BoardRect bounds)
        {
            bounds = default;
            var ids = Selection.Ids;
            if (ids.Count == 0) return false;
            bool any = false;
            var layers = _board.Layers;
            for (int li = 0; li < layers.Count; li++)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = 0; si < shapes.Count; si++)
                {
                    Shape s = shapes[si];
                    if (s == null || !ids.Contains(s.Id) || !SelectionGeometry.TryGetBounds(s, out BoardRect b)) continue;
                    bounds = any ? BoardRect.Union(bounds, b) : b;
                    any = true;
                }
            }
            return any;
        }

        /// <summary>
        /// Replaces the selection with <paramref name="ids"/>, keeping only ids of shapes on visible layers (null = empty).
        /// Raises StateChanged only if the selection changed. Not a model edit.
        /// </summary>
        public void SetSelection(IEnumerable<EntityId> ids)
        {
            var wanted = ids == null ? new HashSet<EntityId>() : new HashSet<EntityId>(ids);
            var next = new HashSet<EntityId>();
            var layers = _board.Layers;
            for (int li = 0; li < layers.Count; li++)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = 0; si < shapes.Count; si++)
                {
                    Shape s = shapes[si];
                    if (s != null && wanted.Contains(s.Id)) next.Add(s.Id);
                }
            }
            if (ReplaceSelection(next)) RaiseStateChanged();
        }

        /// <summary>Empties the selection; raises StateChanged only if it was not empty.</summary>
        public void ClearSelection()
        {
            if (ClearSelectionNoEvent()) RaiseStateChanged();
        }

        /// <summary>Tools that work on the selection; switching to any other tool clears it.</summary>
        static bool KeepsSelection(ToolId t) => t == ToolId.SelectMove || IsTransformTool(t);

        /// <summary>Makes the selection equal to <paramref name="next"/>. No event; true if it changed. Allocation-free.</summary>
        bool ReplaceSelection(HashSet<EntityId> next)
        {
            var ids = Selection.Ids;
            if (ids.Count == next.Count)
            {
                bool same = true;
                foreach (EntityId id in next)
                    if (!ids.Contains(id)) { same = false; break; }
                if (same) return false;
            }
            ids.Clear();
            foreach (EntityId id in next) ids.Add(id);
            return true;
        }

        /// <summary>Empties the selection. No event; true if it was not empty.</summary>
        bool ClearSelectionNoEvent()
        {
            if (Selection.Ids.Count == 0) return false;
            Selection.Ids.Clear();
            return true;
        }

        /// <summary>
        /// Drops ids that are not a shape on a visible layer (hidden or deleted layer, removed shape). No event; true if
        /// anything was dropped. Runs after every model change (RaiseBoardChanged).
        /// </summary>
        bool PruneSelection()
        {
            var ids = Selection.Ids;
            if (ids.Count == 0) return false;
            int kept = 0;
            var layers = _board.Layers;
            for (int li = 0; li < layers.Count; li++)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = 0; si < shapes.Count; si++)
                {
                    Shape s = shapes[si];
                    if (s != null && ids.Contains(s.Id)) kept++;
                }
            }
            if (kept == ids.Count) return false;   // ids are unique per board, so every id is still there

            _selectScratch.Clear();
            for (int li = 0; li < layers.Count; li++)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = 0; si < shapes.Count; si++)
                {
                    Shape s = shapes[si];
                    if (s != null && ids.Contains(s.Id)) _selectScratch.Add(s.Id);
                }
            }
            return ReplaceSelection(_selectScratch);
        }

        /// <summary>
        /// Tap-pick tolerance at <paramref name="p"/> in board units: <see cref="ISnapMetric.TolerancePx"/> converted with
        /// the local screen scale (the smaller of the two board axes' px per unit, measured over <c>PickProbe</c> toward
        /// the board centre), capped at <see cref="MaxPickBoardDistance"/>. Without a metric, or if the projection fails,
        /// <see cref="FallbackSnapTolerance"/>.
        /// </summary>
        double PickTolerance(Vec2 p)
        {
            ISnapMetric metric = SnapMetric;
            if (metric != null && p.IsFinite)
            {
                const double h = PickProbe;
                double hx = p.X <= 0.5 ? h : -h, hy = p.Y <= 0.5 ? h : -h;
                if (metric.TryToScreen(p, out Vec2 s0) && s0.IsFinite
                    && metric.TryToScreen(new Vec2(p.X + hx, p.Y), out Vec2 sx) && sx.IsFinite
                    && metric.TryToScreen(new Vec2(p.X, p.Y + hy), out Vec2 sy) && sy.IsFinite)
                {
                    double pxPerUnit = Math.Min(Vec2.Distance(sx, s0), Vec2.Distance(sy, s0)) / h;
                    double tol = metric.TolerancePx / pxPerUnit;
                    if (pxPerUnit > 1e-9 && !double.IsInfinity(pxPerUnit) && !double.IsNaN(tol) && !double.IsInfinity(tol))
                        return Math.Min(MaxPickBoardDistance, tol);
                }
            }
            return FallbackSnapTolerance;
        }

        /// <summary>Opens a Select gesture at the raw Down point (never snapped). The caller checked the board position.</summary>
        void BeginSelectGesture(in PointerEvent e)
        {
            _gestureOpen = true;
            _gestureTool = ToolId.SelectMove;
            _gestureLayer = EntityId.None;
            _lastRaw = _anchor = _current = e.BoardPos;
            _cursor = _anchor;
            _downHit = _hoverOn = false;
            StartTapDetection(e.BoardPos);

            _selectionBefore.Clear();
            foreach (EntityId id in Selection.Ids) _selectionBefore.Add(id);
            RebuildGhost();
        }

        /// <summary>
        /// Select gesture ghost (from RebuildGhost): nothing while the pointer is within the tap slop, then the dotted
        /// marquee; the selection follows the enclosed set live. Raises PreviewChanged.
        /// </summary>
        void RebuildMarquee()
        {
            _preview.Clear();
            if (!_stillTap)
            {
                BoardRect r = BoardRect.FromCorners(_anchor, _current);
                if (r.Width > 0 || r.Height > 0)
                {
                    var style = new StrokeStyle(_style.Color, LineWidth.Medium, LineStyle.Dotted);
                    var bl = new Vec2(r.MinX, r.MinY);
                    var br = new Vec2(r.MaxX, r.MinY);
                    var tr = new Vec2(r.MaxX, r.MaxY);
                    var tl = new Vec2(r.MinX, r.MaxY);
                    SetEdge(0, bl, br, style);
                    SetEdge(1, br, tr, style);
                    SetEdge(2, tr, tl, style);
                    SetEdge(3, tl, bl, style);
                }
                SelectionGeometry.CollectEnclosed(_board, r, _selectScratch);
                if (ReplaceSelection(_selectScratch)) RaiseStateChanged();
            }
            RaisePreviewChanged();
        }

        void SetEdge(int i, Vec2 a, Vec2 b, StrokeStyle style)
        {
            LineShape edge = _marqueeEdges[i];
            edge.A = a;
            edge.B = b;
            edge.Style = style;
            _preview.Add(edge);
        }

        /// <summary>
        /// Closes a Select gesture (from EndGesture, after the marquee was removed from Preview). commit = Up: a tap picks,
        /// a drag keeps the enclosed set, both toast. Otherwise (Cancel, tool switch, board replace, lost Up) the selection
        /// from before the gesture comes back. StateChanged is raised when the selection changed and
        /// <paramref name="raiseState"/> is true (SetTool passes false and raises its own single event).
        /// </summary>
        void FinishSelectGesture(bool commit, bool raiseState)
        {
            bool changed;
            if (!commit)
            {
                changed = ReplaceSelection(_selectionBefore);
                changed |= PruneSelection();   // a layer hidden or deleted mid-gesture stays out
                if (changed && raiseState) RaiseStateChanged();
                return;
            }

            if (_stillTap)
            {
                if (SelectionGeometry.TryPick(_board, _anchor, PickTolerance(_anchor), out EntityId id))
                {
                    _selectScratch.Clear();
                    _selectScratch.Add(id);
                    if (ReplaceSelection(_selectScratch) && raiseState) RaiseStateChanged();
                    RaiseToast(SelectedCountToast(1));
                }
                else if (ClearSelectionNoEvent() && raiseState) RaiseStateChanged();
                return;
            }

            SelectionGeometry.CollectEnclosed(_board, BoardRect.FromCorners(_anchor, _current), _selectScratch);
            changed = ReplaceSelection(_selectScratch);
            if (changed && raiseState) RaiseStateChanged();
            int n = Selection.Ids.Count;
            RaiseToast(n == 0 ? NothingSelectedToast : SelectedCountToast(n));
        }
    }
}
