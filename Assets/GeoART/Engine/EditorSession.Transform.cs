using System;
using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    // OWNER: transform tools.
    /// <summary>
    /// Move, Scale and Duplicate (<see cref="ToolId.Move"/>, <see cref="ToolId.Scale"/>, <see cref="ToolId.Duplicate"/>):
    /// modes that act on the selection (see EditorSession.Select.cs), routed from EditorSession.Tools.cs before the
    /// hidden-layer check. Entering one needs a selection (<see cref="SelectFirstToast"/>). One gesture at a time:
    ///   Down    anywhere on the board (not necessarily on the shapes). Fixes the pivot (the centre of the selection's
    ///           union bounds) and the reference point: Move/Duplicate grab the selection's key point (line end, centre,
    ///           polygon vertex) nearest the finger within <see cref="MoveGrabRadius"/>, else the pivot; Scale grabs the
    ///           support point (the geometry's farthest point from the pivot in the finger's direction).
    ///   Move    inside the tap slop: nothing. Past it: orange ghosts of every selected shape at the transformed position
    ///           in Preview, the reference (Move/Duplicate) or handle (Scale) as the cursor, and the hover ring when it
    ///           snapped. Move/Scale exclude the selection's own shapes from the snap candidates while dragging.
    ///           Move: translation = finger delta, the reference snapped. Scale: uniform about the pivot, factor
    ///           1 + (|F - P| - r0) / max(r0, <see cref="ScaleGrabMin"/>), the handle snapped radially. Both are clamped
    ///           "never worse" (the selection never leaves the board further than it already does; Scale never shrinks a
    ///           shape below <see cref="MinStrokeLength"/>); a clamped value is not a snap.
    ///   Up      a tap picks the stroke under the finger as the new selection (a tap on empty board does nothing). A
    ///           drag commits once (<see cref="CommitTransform"/>): Move/Scale edit the shapes in place, Duplicate appends
    ///           transformed copies to their originals' layers, selects them and switches to Move. One BoardChanged.
    ///           A snapped commit lands the grabbed line end or centre exactly (==) on the candidate. Identity: nothing.
    ///   Cancel  drops the ghosts; the model was never touched.
    /// The H/V lock does not apply. If the selection empties while a transform tool is active (outside a gesture), the
    /// tool falls back to Select (<see cref="NormalizeTool"/>, run by every StateChanged). The Move path is allocation-free:
    /// ghosts come from pools grown only at Down.
    /// </summary>
    public sealed partial class EditorSession
    {
        /// <summary>Move/Duplicate: a key point this close to the Down point (board units, 3.2 cm on 40 cm) is grabbed.</summary>
        public const double MoveGrabRadius = 0.08;

        /// <summary>Scale: the finger distance from the pivot that one unit of scale needs at least (board units).</summary>
        public const double ScaleGrabMin = 0.05;

        /// <summary>Scale: a line-end handle this close to the candidate is set exactly onto it.</summary>
        public const double ExactPinEpsilon = 1e-9;

        public const string MoveHintToast = "Move: drag to move the selection; tap a shape to switch";
        public const string ScaleHintToast = "Scale: drag away from the centre mark to grow, toward it to shrink";
        public const string DuplicateHintToast = "Duplicate: drag to place a copy";
        public const string SelectFirstToast = "Select shapes first";
        public const string NoCopyToast = "Drag to place the copy";

        public static string DuplicatedToast(int n) =>
            n == 1 ? "Copy placed: drag to move it" : n + " copies placed: drag to move them";

        public static string ScaledToast(double s) => "Scaled to " + Math.Round(s * 100) + "%";

        const double MinScale = 1e-6;   // floor of the finger factor
        const double MinHandle = 1e-6;  // a shorter handle (pivot to support point) does not snap

        // All reused: the Move path never allocates.
        readonly List<SelectedShape> _xfItems = new List<SelectedShape>();
        readonly List<Shape> _xfGhosts = new List<Shape>();   // parallel to _xfItems (null for an unknown shape type)
        readonly List<LineShape> _xfLinePool = new List<LineShape>();
        readonly List<CircleShape> _xfCirclePool = new List<CircleShape>();
        readonly List<PolygonShape> _xfPolyPool = new List<PolygonShape>();
        readonly HashSet<EntityId> _xfExcluded = new HashSet<EntityId>();
        readonly HashSet<EntityId> _xfNewIds = new HashSet<EntityId>();
        BoardRect _xfBounds;
        Vec2 _xfPivot, _xfDown, _xfRef0, _xfRefNow, _xfSnapPos;
        GrabKind _xfKind;
        int _xfShape = -1;   // index into _xfItems of the grabbed shape, -1 for the pivot
        double _xfRho, _xfR0, _xfRg, _xfSMin, _xfSMax;
        double _xfLoX, _xfHiX, _xfLoY, _xfHiY;
        bool _xfHit, _xfExcluding, _xfTracked;
        ShapeTransform _xf = ShapeTransform.Identity;

        static bool IsTransformTool(ToolId t) => t == ToolId.Move || t == ToolId.Scale || t == ToolId.Duplicate;

        /// <summary>
        /// A transform tool with an empty selection falls back to Select (outside a gesture). No event, no toast: it runs
        /// first in every RaiseStateChanged, and every path that empties the selection raises StateChanged.
        /// </summary>
        void NormalizeTool()
        {
            if (_gestureOpen || !IsTransformTool(_tool) || Selection.Ids.Count != 0) return;
            _tool = ToolId.SelectMove;
            _toolHint = HintFor(ToolId.SelectMove);
        }

        /// <summary>Opens a transform gesture at the raw Down point. Nothing selected on a visible layer: no gesture.</summary>
        void BeginTransformGesture(in PointerEvent e)
        {
            GetSelectedShapes(_xfItems);
            if (_xfItems.Count == 0 || !TryGetSelectionBounds(out _xfBounds))
            {
                _xfItems.Clear();
                return;
            }
            _gestureOpen = true;
            _gestureTool = _tool;
            _gestureLayer = EntityId.None;
            _lastRaw = _xfDown = _anchor = _current = e.BoardPos;
            _downHit = _hoverOn = false;
            StartTapDetection(e.BoardPos);

            _xfPivot = new Vec2((_xfBounds.MinX + _xfBounds.MaxX) * 0.5, (_xfBounds.MinY + _xfBounds.MaxY) * 0.5);
            FillGhosts();
            if (_gestureTool == ToolId.Scale) PickScaleHandle(e.BoardPos);
            else PickMoveReference(e.BoardPos);

            TransformGeometry.TranslationLimits(_xfBounds, out _xfLoX, out _xfHiX, out _xfLoY, out _xfHiY);
            double need = 0;
            for (int i = 0; i < _xfItems.Count; i++)
            {
                double size = TransformGeometry.Size(_xfItems[i].Shape);
                if (size > 0) need = Math.Max(need, MinStrokeLength / size);
            }
            _xfSMin = Math.Min(1.0, Math.Max(need, MinScale));
            _xfSMax = TransformGeometry.MaxScaleInBoard(_xfBounds, _xfPivot);

            _xf = ShapeTransform.Identity;
            _xfHit = _xfExcluding = _xfTracked = false;
            _xfRefNow = _xfRef0;
            if (_cursor.HasValue || _preview.Count > 0)
            {
                _cursor = null;
                _preview.Clear();
                RaisePreviewChanged();
            }
        }

        /// <summary>One ghost per selected shape, same concrete type and style, from the pools (grown here only).</summary>
        void FillGhosts()
        {
            _xfGhosts.Clear();
            int nl = 0, nc = 0, np = 0;
            for (int i = 0; i < _xfItems.Count; i++)
            {
                Shape s = _xfItems[i].Shape;
                Shape g;
                switch (s)
                {
                    case LineShape _:
                        if (nl == _xfLinePool.Count) _xfLinePool.Add(new LineShape());
                        g = _xfLinePool[nl++];
                        break;
                    case CircleShape _:
                        if (nc == _xfCirclePool.Count) _xfCirclePool.Add(new CircleShape());
                        g = _xfCirclePool[nc++];
                        break;
                    case PolygonShape _:
                        if (np == _xfPolyPool.Count) _xfPolyPool.Add(new PolygonShape());
                        g = _xfPolyPool[np++];
                        break;
                    default:
                        g = null;
                        break;
                }
                if (g != null)
                {
                    g.Style = s.Style;
                    TransformGeometry.Apply(s, ShapeTransform.Identity, g);
                }
                _xfGhosts.Add(g);
            }
        }

        /// <summary>Move/Duplicate reference: the nearest key point within <see cref="MoveGrabRadius"/> of F0, else the pivot.</summary>
        void PickMoveReference(Vec2 f0)
        {
            _xfRef0 = _xfPivot;
            _xfKind = GrabKind.Pivot;
            _xfShape = -1;
            _xfRho = _xfR0 = _xfRg = 0;
            double best = MoveGrabRadius;
            bool found = false;
            for (int i = 0; i < _xfItems.Count; i++)
            {
                if (!TransformGeometry.TryNearestKeyPoint(_xfItems[i].Shape, f0, out Vec2 p, out GrabKind kind,
                        out _, out double d)) continue;
                if (found ? !(d < best) : !(d <= best)) continue;
                found = true;
                best = d;
                _xfRef0 = p;
                _xfKind = kind;
                _xfShape = i;
            }
        }

        /// <summary>Scale handle: the support point in the direction pivot -&gt; F0 (or +X), and the finger factor's radii.</summary>
        void PickScaleHandle(Vec2 f0)
        {
            Vec2 pv = _xfPivot;
            double dx = f0.X - pv.X, dy = f0.Y - pv.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            Vec2 dir = len < 1e-9 ? new Vec2(1, 0) : new Vec2(dx / len, dy / len);
            _xfRef0 = pv;
            _xfKind = GrabKind.Pivot;
            _xfShape = -1;
            double best = double.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < _xfItems.Count; i++)
            {
                if (!TransformGeometry.TrySupportPoint(_xfItems[i].Shape, pv, dir, out Vec2 p, out GrabKind kind,
                        out _, out double along)) continue;
                if (found && !(along > best)) continue;
                found = true;
                best = along;
                _xfRef0 = p;
                _xfKind = kind;
                _xfShape = i;
            }
            _xfRho = found ? Vec2.Distance(_xfRef0, pv) : 0;
            _xfR0 = len;
            _xfRg = Math.Max(len, ScaleGrabMin);
        }

        /// <summary>One pointer sample of a transform gesture (from TrackGesture, after MeasureTap). Allocation-free.</summary>
        void TrackTransform(in PointerEvent e)
        {
            if (!e.OnBoard || !e.BoardPos.IsFinite) return;   // keep the last state
            if (_stillTap) return;
            if (_xfTracked && e.BoardPos == _lastRaw) return;
            _lastRaw = e.BoardPos;

            if (!_xfExcluding && (_gestureTool == ToolId.Move || _gestureTool == ToolId.Scale))
            {
                // First drag frame: the selection's own points stop being candidates (rebuilt once, not per Move).
                _xfExcluded.Clear();
                for (int i = 0; i < _xfItems.Count; i++) _xfExcluded.Add(_xfItems[i].Shape.Id);
                _snapIndex.ExcludedShapes = _xfExcluded;
                _snapIndex.MarkDirty();
                _xfExcluding = true;
                RaiseStateChanged();   // the dots are redrawn without them
            }
            _xfTracked = true;

            if (_gestureTool == ToolId.Scale) TrackScale(e.BoardPos);
            else TrackMove(e.BoardPos);

            _preview.Clear();
            for (int i = 0; i < _xfItems.Count; i++)
            {
                Shape g = _xfGhosts[i];
                if (g == null) continue;
                TransformGeometry.Apply(_xfItems[i].Shape, _xf, g);
                _preview.Add(g);
            }
            if (_xfHit)
            {
                _hoverRing.Center = _xfRefNow;
                _hoverRing.Radius = HoverRingRadius;
                _hoverRing.Style = RingStyle;
                _preview.Add(_hoverRing);
            }
            _hoverOn = _xfHit;
            _current = _xfRefNow;
            _cursor = _xfRefNow;
            RaisePreviewChanged();
        }

        /// <summary>Move/Duplicate: translation by the finger delta, the reference point snapped, then clamped.</summary>
        void TrackMove(Vec2 f)
        {
            double tx = f.X - _xfDown.X, ty = f.Y - _xfDown.Y;
            bool hit = TrySnap(new Vec2(_xfRef0.X + tx, _xfRef0.Y + ty), out SnapCandidate c);
            if (hit)
            {
                tx = c.Position.X - _xfRef0.X;
                ty = c.Position.Y - _xfRef0.Y;
            }
            double cx = Clamp(tx, _xfLoX, _xfHiX), cy = Clamp(ty, _xfLoY, _xfHiY);
            if (cx != tx || cy != ty) hit = false;   // a clamped translation is not a snap
            _xfHit = hit;
            _xfSnapPos = hit ? c.Position : default;
            _xf = ShapeTransform.Translation(new Vec2(cx, cy));
            _xfRefNow = hit ? c.Position : new Vec2(_xfRef0.X + cx, _xfRef0.Y + cy);
        }

        /// <summary>Scale: the finger factor, the handle snapped radially, then clamped to [sMin, sMax].</summary>
        void TrackScale(Vec2 f)
        {
            Vec2 pv = _xfPivot;
            double sRaw = 1.0 + (Vec2.Distance(f, pv) - _xfR0) / _xfRg;
            if (!(sRaw >= MinScale)) sRaw = MinScale;
            double s = sRaw;
            bool hit = false;
            SnapCandidate c = default;
            if (_xfRho >= MinHandle)
            {
                var hRaw = new Vec2(pv.X + sRaw * (_xfRef0.X - pv.X), pv.Y + sRaw * (_xfRef0.Y - pv.Y));
                if (TrySnap(hRaw, out c))
                {
                    s = Vec2.Distance(c.Position, pv) / _xfRho;
                    hit = true;
                }
            }
            double sc = Clamp(s, _xfSMin, _xfSMax);
            if (sc != s) hit = false;   // a clamped factor is not a snap
            _xfHit = hit;
            _xfSnapPos = hit ? c.Position : default;
            _xf = ShapeTransform.ScaleAbout(pv, sc);
            _xfRefNow = _xf.Apply(_xfRef0);
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>
        /// Closes a transform gesture (from EndGesture, after Preview and the cursor were cleared). The snap exclusion is
        /// always lifted. commit = Up: a tap picks a stroke, a drag commits the transform (identity: nothing). Otherwise
        /// (Cancel, tool switch, board replace) the ghosts are simply dropped. <paramref name="raiseState"/> false defers
        /// StateChanged to the caller (SetTool).
        /// </summary>
        void FinishTransformGesture(bool commit, bool raiseState)
        {
            bool excluded = _xfExcluding;
            if (excluded)
            {
                _snapIndex.ExcludedShapes = null;
                _snapIndex.MarkDirty();
                _xfExcluding = false;
            }

            if (!commit)
            {
                if (excluded && raiseState) RaiseStateChanged();
            }
            else if (_stillTap)
            {
                if (SelectionGeometry.TryPick(_board, _xfDown, PickTolerance(_xfDown), out EntityId id))
                {
                    _selectScratch.Clear();
                    _selectScratch.Add(id);
                    if ((ReplaceSelection(_selectScratch) || excluded) && raiseState) RaiseStateChanged();
                    RaiseToast(SelectedCountToast(1));
                }
                else if (excluded && raiseState) RaiseStateChanged();   // a tap on empty board keeps the selection
            }
            else if (_xf.IsIdentity)
            {
                if (_gestureTool == ToolId.Duplicate) RaiseToast(NoCopyToast);
                if (excluded && raiseState) RaiseStateChanged();
            }
            else CommitTransform(excluded && raiseState);

            _xfItems.Clear();
            _xfGhosts.Clear();
            _xf = ShapeTransform.Identity;
            _xfHit = _xfTracked = false;
            // The selection emptied mid-gesture (a layer hidden or deleted): fall back to Select now.
            if (raiseState && IsTransformTool(_tool) && Selection.Ids.Count == 0) RaiseStateChanged();
        }

        /// <summary>
        /// Applies <c>_xf</c> once: Move/Scale edit each still-selected shape on a visible layer in place, Duplicate appends
        /// a transformed clone with a new id to the original's layer. Snapped commits pin the grabbed point exactly. Then
        /// one BoardChanged(Shapes); Duplicate also selects the copies and switches to Move with one StateChanged.
        /// </summary>
        void CommitTransform(bool raiseStateIfNothing)
        {
            bool dup = _gestureTool == ToolId.Duplicate;
            if (dup) _xfNewIds.Clear();
            // One undo step per commit, captured before any edit or NewId; pushed only if something changed.
            HistoryStep step = CaptureStep(dup ? UndoLabels.Duplicate
                : _gestureTool == ToolId.Scale ? UndoLabels.Scale : UndoLabels.Move);
            int n = 0;
            for (int i = 0; i < _xfItems.Count; i++)
            {
                SelectedShape item = _xfItems[i];
                Shape src = item.Shape;
                Layer layer = item.Layer;
                // Normally unreachable (any second touch cancels), but never touch a shape that left the selection.
                if (src == null || !Selection.Ids.Contains(src.Id)) continue;
                if (layer == null || !layer.Visible || _board.FindLayer(layer.Id) != layer) continue;
                if (dup)
                {
                    Shape copy = src.Clone();
                    if (!TransformGeometry.Apply(src, _xf, copy)) continue;
                    copy.Id = _board.NewId();
                    Pin(i, copy);
                    layer.Shapes.Add(copy);
                    _xfNewIds.Add(copy.Id);
                }
                else
                {
                    if (!TransformGeometry.Apply(src, _xf, src)) continue;
                    Pin(i, src);
                }
                n++;
            }
            if (n == 0)
            {
                if (raiseStateIfNothing) RaiseStateChanged();
                return;
            }

            PushStep(step);
            RaiseBoardChanged(BoardChangeKind.Shapes);   // snap candidates are rebuilt for the new geometry
            if (dup)
            {
                ReplaceSelection(_xfNewIds);
                _tool = ToolId.Move;
                _toolHint = MoveHintToast;
                RaiseStateChanged();
                RaiseToast(DuplicatedToast(n));
            }
            else if (_gestureTool == ToolId.Scale) RaiseToast(ScaledToast(_xf.Scale));
        }

        /// <summary>
        /// Snapped commit: puts the grabbed point of item <paramref name="i"/>'s result <paramref name="dst"/> exactly on
        /// the candidate. Move/Duplicate: a line end or a centre. Scale: a line end within <see cref="ExactPinEpsilon"/>,
        /// or a ring handle's radius when that circle is centred on the pivot.
        /// </summary>
        void Pin(int i, Shape dst)
        {
            if (!_xfHit || i != _xfShape) return;
            Vec2 c = _xfSnapPos;
            if (_gestureTool == ToolId.Scale)
            {
                switch (_xfKind)
                {
                    case GrabKind.LineA:
                        if (dst is LineShape la && Vec2.Distance(la.A, c) <= ExactPinEpsilon) la.A = c;
                        break;
                    case GrabKind.LineB:
                        if (dst is LineShape lb && Vec2.Distance(lb.B, c) <= ExactPinEpsilon) lb.B = c;
                        break;
                    case GrabKind.Ring:
                        if (dst is CircleShape ring && ring.Center == _xfPivot) ring.Radius = Vec2.Distance(c, _xfPivot);
                        break;
                }
                return;
            }
            switch (_xfKind)
            {
                case GrabKind.LineA:
                    if (dst is LineShape la) la.A = c;
                    break;
                case GrabKind.LineB:
                    if (dst is LineShape lb) lb.B = c;
                    break;
                case GrabKind.Center:
                    if (dst is CircleShape circle) circle.Center = c;
                    else if (dst is PolygonShape poly) poly.Center = c;
                    break;
            }
        }
    }
}
