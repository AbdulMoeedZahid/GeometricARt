using System.Collections.Generic;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// Extend (Select tool, action bar): every selected line has each end pushed out along its own direction to the first
    /// drawn thing in the way: another line, a circle, or a polygon edge on a visible layer, selected or not. The new end is
    /// the exact crossing from <see cref="Intersect"/>. The board edge, the grid and snap points are not boundaries; an end
    /// with nothing in the way stays where it is. Circles and polygons in the selection are not extended.
    /// </summary>
    public sealed partial class EditorSession
    {
        /// <summary>Toast when no end moved.</summary>
        public const string NothingToExtendToast = "Nothing in the way to extend to";

        /// <summary>"Extended 1 end" or "Extended N ends".</summary>
        public static string ExtendedToast(int n) => n == 1 ? "Extended 1 end" : "Extended " + n + " ends";

        /// <summary>A hit closer than this (board units) to the end, or behind it, is not "in the way" (the end already sits on it).</summary>
        const double ExtendEps = 1e-9;

        readonly List<SelectedShape> _extendScratch = new List<SelectedShape>();
        readonly List<LineShape> _extendLines = new List<LineShape>();
        readonly List<Vec2> _extendEnds = new List<Vec2>();   // new A, new B per line in _extendLines

        /// <summary>
        /// Extends the selected lines (visible layers). Every end is worked out against the board as it was before the
        /// action, so the result does not depend on the order of the lines. If no end moves: no step, no event, toast
        /// <see cref="NothingToExtendToast"/>. Otherwise one "Extend" undo step, one BoardChanged(Shapes) and a toast with
        /// the number of ends moved. Ids, styles, layers, the selection and the tool are kept.
        /// </summary>
        public void ExtendSelection()
        {
            EndGesture(false, false);
            GetSelectedShapes(_extendScratch);
            _extendLines.Clear();
            _extendEnds.Clear();
            int moved = 0;
            for (int i = 0; i < _extendScratch.Count; i++)
            {
                if (!(_extendScratch[i].Shape is LineShape line)) continue;
                Vec2 d = line.B - line.A;
                double len = d.Length;
                if (!(len > 0) || double.IsInfinity(len)) continue;
                Vec2 u = new Vec2(d.X / len, d.Y / len);
                bool movedA = TryExtendEnd(line, line.A, new Vec2(-u.X, -u.Y), out Vec2 a);
                bool movedB = TryExtendEnd(line, line.B, u, out Vec2 b);
                if (!movedA && !movedB) continue;
                moved += (movedA ? 1 : 0) + (movedB ? 1 : 0);
                _extendLines.Add(line);
                _extendEnds.Add(movedA ? a : line.A);
                _extendEnds.Add(movedB ? b : line.B);
            }
            _extendScratch.Clear();
            if (moved == 0)
            {
                RaiseToast(NothingToExtendToast);
                return;
            }
            RecordUndo(UndoLabels.Extend);
            // RecordUndo cloned the board for history; the live lines are still the ones collected above.
            for (int i = 0; i < _extendLines.Count; i++)
            {
                _extendLines[i].A = _extendEnds[2 * i];
                _extendLines[i].B = _extendEnds[2 * i + 1];
            }
            _extendLines.Clear();
            _extendEnds.Clear();
            RaiseBoardChanged(BoardChangeKind.Shapes);
            RaiseToast(ExtendedToast(moved));
        }

        /// <summary>
        /// The first boundary along the ray end + unit * s, s &gt; <see cref="ExtendEps"/>, over every shape on a visible
        /// layer except <paramref name="self"/>. Lines and polygon edges parallel or collinear with the ray are skipped
        /// (<see cref="Intersect.LineSegment"/> gives 0 for them).
        /// </summary>
        bool TryExtendEnd(LineShape self, Vec2 end, Vec2 unit, out Vec2 hit)
        {
            hit = end;
            double best = double.PositiveInfinity;
            var layers = _board.Layers;
            for (int li = 0; li < layers.Count; li++)
            {
                Layer layer = layers[li];
                if (layer == null || !layer.Visible) continue;
                var shapes = layer.Shapes;
                for (int si = 0; si < shapes.Count; si++)
                {
                    Shape s = shapes[si];
                    if (s == null || ReferenceEquals(s, self)) continue;
                    Vec2 p, q;
                    switch (s)
                    {
                        case LineShape l:
                            if (Intersect.LineSegment(end, unit, l.A, l.B, out p) == 1) Consider(end, unit, Exact(p, end, unit, l.A, l.B), ref best, ref hit);
                            break;
                        case CircleShape c:
                        {
                            int n = Intersect.LineCircle(end, unit, c.Center, c.Radius, out p, out q);
                            if (n >= 1) Consider(end, unit, p, ref best, ref hit);
                            if (n == 2) Consider(end, unit, q, ref best, ref hit);
                            break;
                        }
                        case PolygonShape g:
                        {
                            if (g.Sides < 3 || !(g.Radius > 0)) break;
                            Vec2 first = SnapIndex.PolygonVertex(g, 0), prev = first;
                            for (int k = 1; k <= g.Sides; k++)
                            {
                                Vec2 next = k == g.Sides ? first : SnapIndex.PolygonVertex(g, k);
                                if (Intersect.LineSegment(end, unit, prev, next, out p) == 1) Consider(end, unit, Exact(p, end, unit, prev, next), ref best, ref hit);
                                prev = next;
                            }
                            break;
                        }
                    }
                }
            }
            return !double.IsPositiveInfinity(best);
        }

        /// <summary>
        /// <see cref="Intersect.LineSegment"/> computes the crossing from the ray, so only the ray's own fixed coordinate is
        /// exact. When the boundary segment is axis-aligned its fixed coordinate is copied in too, so a horizontal line
        /// extended to a vertical one ends exactly on x = that line's x (0.4 - 0.2 is not 0.2 in doubles). Ends of the
        /// boundary are returned exactly by Intersect already.
        /// </summary>
        static Vec2 Exact(Vec2 p, Vec2 end, Vec2 unit, Vec2 b0, Vec2 b1)
        {
            if (p == b0 || p == b1) return p;
            double x = p.X, y = p.Y;
            if (b0.X == b1.X) x = b0.X;
            else if (unit.X == 0) x = end.X;
            if (b0.Y == b1.Y) y = b0.Y;
            else if (unit.Y == 0) y = end.Y;
            return new Vec2(x, y);
        }

        static void Consider(Vec2 end, Vec2 unit, Vec2 p, ref double best, ref Vec2 hit)
        {
            double s = (p.X - end.X) * unit.X + (p.Y - end.Y) * unit.Y;
            if (!(s > ExtendEps) || !(s < best)) return;
            best = s;
            hit = p;
        }
    }
}
