using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>Move, Scale and Duplicate: TransformGeometry, tool switching, gestures, snapping, clamps, cancel, allocation.</summary>
    public static class TransformChecks
    {
        static PointerEvent Ev(PointerPhase phase, double x, double y, bool onBoard = true, double t = 0) =>
            new PointerEvent(phase, new Vec2(x, y), onBoard, t);

        static void Drag(EditorSession s, double ax, double ay, double bx, double by, int moves = 5)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= moves; i++)
            {
                double k = (double)i / moves;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * k, ay + (by - ay) * k));
            }
            s.Pointer(Ev(PointerPhase.Up, bx, by));
        }

        /// <summary>Down and Moves to (bx, by), no Up: the gesture stays open.</summary>
        static void DragOpen(EditorSession s, double ax, double ay, double bx, double by, int moves = 5)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= moves; i++)
            {
                double k = (double)i / moves;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * k, ay + (by - ay) * k));
            }
        }

        static void Tap(EditorSession s, double x, double y)
        {
            s.Pointer(Ev(PointerPhase.Down, x, y));
            s.Pointer(Ev(PointerPhase.Up, x, y));
        }

        static readonly StrokeStyle St = StrokeStyle.Default;
        static readonly StrokeStyle Fancy = new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted);

        static LineShape AddLine(Board b, Layer l, double ax, double ay, double bx, double by, StrokeStyle? style = null)
        {
            var line = new LineShape(b.NewId(), new Vec2(ax, ay), new Vec2(bx, by), style ?? St);
            l.Shapes.Add(line);
            return line;
        }

        static CircleShape AddCircle(Board b, Layer l, double cx, double cy, double r, StrokeStyle? style = null)
        {
            var c = new CircleShape(b.NewId(), new Vec2(cx, cy), r, style ?? St);
            l.Shapes.Add(c);
            return c;
        }

        static bool HasCandidate(EditorSession s, Vec2 p)
        {
            var list = s.SnapCandidates;
            for (int i = 0; i < list.Count; i++)
                if (Vec2.Distance(list[i].Position, p) < 1e-9) return true;
            return false;
        }

        /// <summary>One layer, session with snapping off (unless asked), counters for events and toasts.</summary>
        sealed class Fixture
        {
            public readonly Board B = new Board();
            public readonly Layer L;
            public EditorSession S;
            public readonly List<string> Toasts = new List<string>();
            public readonly List<BoardChangeKind> Changes = new List<BoardChangeKind>();
            public int States, Previews;

            public Fixture()
            {
                L = new Layer(B.NewId(), "Layer 1");
                B.Layers.Add(L);
                B.ActiveLayerId = L.Id;
            }

            public EditorSession Start(bool snap = false, ISnapMetric metric = null)
            {
                S = new EditorSession(B) { SnapMetric = metric };
                S.SetSnapEnabled(snap);
                S.Toast += t => Toasts.Add(t);
                S.StateChanged += () => States++;
                S.PreviewChanged += () => Previews++;
                S.BoardChanged += k => Changes.Add(k);
                return S;
            }

            public void Select(ToolId tool, params Shape[] shapes)
            {
                var ids = new List<EntityId>();
                foreach (var sh in shapes) ids.Add(sh.Id);
                S.SetTool(ToolId.SelectMove);
                S.SetSelection(ids);
                if (tool != ToolId.SelectMove) S.SetTool(tool);
                Check.That(S.ActiveTool == tool, $"precondition: tool {tool} (got {S.ActiveTool})");
                Reset();
            }

            public void Reset()
            {
                Toasts.Clear();
                Changes.Clear();
                States = 0;
                Previews = 0;
            }

            public bool Selected(params Shape[] shapes)
            {
                if (S.Selection.Ids.Count != shapes.Length) return false;
                foreach (var sh in shapes)
                    if (!S.Selection.Ids.Contains(sh.Id)) return false;
                return true;
            }

            public string Last => Toasts.Count == 0 ? "<none>" : Toasts[Toasts.Count - 1];
        }

        // ---------- TransformGeometry ----------

        public static void ApplyTranslatesAndScalesEveryShapeKind()
        {
            var line = new LineShape(new EntityId(7), new Vec2(0.2, 0.3), new Vec2(0.4, 0.1), Fancy);
            var t = ShapeTransform.Translation(new Vec2(0.25, -0.125));
            Check.That(!t.IsIdentity && ShapeTransform.Identity.IsIdentity, "identity flag");
            Check.That(ShapeTransform.Translation(Vec2.Zero).IsIdentity && ShapeTransform.ScaleAbout(new Vec2(3, 4), 1).IsIdentity,
                "zero translation and scale 1 are identities");
            Check.That(TransformGeometry.Apply(line, t, line), "line in place");
            Check.That(line.A == new Vec2(0.2 + 0.25, 0.3 - 0.125) && line.B == new Vec2(0.4 + 0.25, 0.1 - 0.125), $"translated exactly {line.A} {line.B}");
            Check.That(line.Id == new EntityId(7) && line.Style.Equals(Fancy), "id and style untouched");

            var circle = new CircleShape(new EntityId(8), new Vec2(0.5, 0.5), 0.1, Fancy);
            var sc = ShapeTransform.ScaleAbout(new Vec2(0.4, 0.5), 2);
            var dst = new CircleShape();
            Check.That(TransformGeometry.Apply(circle, sc, dst), "circle into another");
            Check.Near(dst.Center.X, 0.6, "centre x", 1e-15);
            Check.That(dst.Center.Y == 0.5 && dst.Radius == 0.2, $"centre y and radius {dst.Center} {dst.Radius}");
            Check.That(circle.Center == new Vec2(0.5, 0.5) && circle.Radius == 0.1, "src untouched");
            Check.That(dst.Style.Equals(StrokeStyle.Default) && dst.Id == EntityId.None, "dst keeps its own id and style");

            var poly = new PolygonShape(new EntityId(9), new Vec2(0.3, 0.3), 5, 0.1, 0.7, Fancy);
            Check.That(TransformGeometry.Apply(poly, ShapeTransform.ScaleAbout(new Vec2(0.5, 0.5), 0.5), poly), "polygon in place");
            Check.Near(poly.Center.X, 0.4, "poly centre", 1e-15);
            Check.Near(poly.Radius, 0.05, "poly radius", 1e-15);
            Check.That(poly.Sides == 5 && poly.RotationRad == 0.7 && poly.Style.Equals(Fancy), "sides, rotation, style kept");

            Check.That(!TransformGeometry.Apply(line, t, circle), "type mismatch");
            Check.That(!TransformGeometry.Apply(null, t, line) && !TransformGeometry.Apply(line, t, null), "null");

            Check.Near(TransformGeometry.Size(new LineShape(EntityId.None, new Vec2(0, 0), new Vec2(0.3, 0.4), St)), 0.5, "line size", 1e-15);
            Check.That(TransformGeometry.Size(circle) == 0.1 && TransformGeometry.Size(poly) == 0.05, "circle / polygon size");
            Check.That(TransformGeometry.Size(new CircleShape(EntityId.None, Vec2.Zero, 0, St)) == 0, "radius 0: size 0");
            Check.That(TransformGeometry.Size(new PolygonShape(EntityId.None, Vec2.Zero, 2, 0.1, 0, St)) == 0, "2 sides: size 0");
            Check.That(TransformGeometry.Size(null) == 0, "null: size 0");
        }

        public static void SupportAndNearestKeyPoints()
        {
            var pv = new Vec2(0.5, 0.5);
            var line = new LineShape(EntityId.None, new Vec2(0.3, 0.5), new Vec2(0.7, 0.5), St);
            Check.That(TransformGeometry.TrySupportPoint(line, pv, new Vec2(1, 0), out Vec2 p, out GrabKind k, out _, out double along)
                       && p == line.B && k == GrabKind.LineB, "line: +x picks B");
            Check.Near(along, 0.2, "along", 1e-15);
            Check.That(TransformGeometry.TrySupportPoint(line, pv, new Vec2(0, 1), out p, out k, out _, out _) && p == line.A && k == GrabKind.LineA,
                "line: a tie picks A");
            var circle = new CircleShape(EntityId.None, new Vec2(0.6, 0.5), 0.1, St);
            Check.That(TransformGeometry.TrySupportPoint(circle, pv, new Vec2(0, 1), out p, out k, out _, out along) && k == GrabKind.Ring,
                "circle ring");
            Check.That(p == new Vec2(0.6, 0.6), $"circle c + r d {p}");
            var poly = new PolygonShape(EntityId.None, new Vec2(0.5, 0.5), 4, 0.1, 0, St);   // vertices at 0, 90, 180, 270 deg
            Check.That(TransformGeometry.TrySupportPoint(poly, pv, new Vec2(0, -1), out p, out k, out int v, out _) && k == GrabKind.Vertex && v == 3,
                $"polygon: lowest vertex (got {v})");
            Check.That(!TransformGeometry.TrySupportPoint(new CircleShape(), pv, new Vec2(1, 0), out _, out _, out _, out _), "non-drawable");

            Check.That(TransformGeometry.TryNearestKeyPoint(line, new Vec2(0.66, 0.52), out p, out k, out _, out double d)
                       && p == line.B && k == GrabKind.LineB, "nearest: B");
            Check.Near(d, Math.Sqrt(0.04 * 0.04 + 0.02 * 0.02), "distance", 1e-15);
            Check.That(TransformGeometry.TryNearestKeyPoint(circle, new Vec2(0.7, 0.5), out p, out k, out _, out _)
                       && p == circle.Center && k == GrabKind.Center, "circle: only the centre");
            Check.That(TransformGeometry.TryNearestKeyPoint(poly, new Vec2(0.61, 0.5), out p, out k, out v, out _) && k == GrabKind.Vertex && v == 0,
                "polygon vertex 0");
            Check.That(TransformGeometry.TryNearestKeyPoint(poly, new Vec2(0.51, 0.5), out p, out k, out _, out _) && k == GrabKind.Center,
                "polygon centre");
            Check.That(!TransformGeometry.TryNearestKeyPoint(new CircleShape(), pv, out _, out _, out _, out _), "non-drawable: none");
        }

        public static void ClampLimitsAreNeverWorse()
        {
            TransformGeometry.TranslationLimits(new BoardRect(0.2, 0.3, 0.6, 0.9), out double loX, out double hiX, out double loY, out double hiY);
            Check.That(loX == -0.2 && loY == -0.3, "inside: down to the edges");
            Check.Near(hiX, 0.4, "hiX", 1e-15);
            Check.Near(hiY, 0.1, "hiY", 1e-15);
            TransformGeometry.TranslationLimits(new BoardRect(-0.1, 0.3, 1.2, 0.9), out loX, out hiX, out _, out _);
            Check.That(loX == 0 && hiX == 0, $"sticking out on both sides: frozen in X ({loX}, {hiX})");
            TransformGeometry.TranslationLimits(new BoardRect(-0.1, 0.3, 0.3, 0.9), out loX, out hiX, out _, out _);
            Check.That(loX == 0 && hiX == 0.7, $"sticking out left: can't go further left, may go right ({loX}, {hiX})");

            Check.Near(TransformGeometry.MaxScaleInBoard(new BoardRect(0.3, 0.5, 0.5, 0.5), new Vec2(0.4, 0.5)), 4, "horizontal line", 1e-12);
            Check.Near(TransformGeometry.MaxScaleInBoard(new BoardRect(0.4, 0.4, 0.6, 0.6), new Vec2(0.5, 0.5)), 5, "centred box", 1e-12);
            Check.That(TransformGeometry.MaxScaleInBoard(new BoardRect(-0.1, -0.1, 1.1, 1.1), new Vec2(0.5, 0.5)) == 1, "already outside: 1");
        }

        // ---------- tool switching ----------

        public static void TransformToolsNeedASelection()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.2, 0.2, 0.4, 0.4);
            var s = f.Start();
            s.SetTool(ToolId.SelectMove);
            f.Reset();
            foreach (var t in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
            {
                s.SetTool(t);
                Check.That(s.ActiveTool == ToolId.SelectMove, $"{t} refused without a selection");
                Check.That(f.Last == EditorSession.SelectFirstToast && f.States == 0, $"{t}: SelectFirstToast, no event");
                f.Reset();
            }

            s.SetSelection(new[] { line.Id });
            var hints = new Dictionary<ToolId, string>
            {
                { ToolId.Move, EditorSession.MoveHintToast },
                { ToolId.Scale, EditorSession.ScaleHintToast },
                { ToolId.Duplicate, EditorSession.DuplicateHintToast },
            };
            foreach (var t in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
            {
                f.Reset();
                s.SetTool(t);
                Check.That(s.ActiveTool == t, $"{t} accepted");
                Check.That(f.States == 1, $"{t}: exactly one StateChanged (got {f.States})");
                Check.That(f.Toasts.Count == 1 && f.Toasts[0] == hints[t] && s.ToolHint == hints[t], $"{t}: hint toast and ToolHint");
                Check.That(f.Selected(line), $"{t} keeps the selection");
            }
            f.Reset();
            s.SetTool(ToolId.Duplicate);
            Check.That(f.States == 0 && f.Toasts.Count == 0, "same tool again: nothing");
            foreach (var t in new[] { ToolId.Rotate, ToolId.Stamps, ToolId.Erase, ToolId.Snap })
            {
                f.Reset();
                s.SetTool(t);
                Check.That(s.ActiveTool == ToolId.Duplicate && f.Last == EditorSession.ComingSoonToast, $"{t} still coming soon");
            }
            s.SetTool(ToolId.SelectMove);
            Check.That(f.Selected(line), "back to Select keeps the selection");
            s.SetTool(ToolId.Line);
            Check.That(s.SelectionCount == 0, "Line clears it");
        }

        // ---------- Move ----------

        public static void MoveTranslatesTheSelectionOnUp()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.2, 0.2, 0.3, 0.4, Fancy);
            var circle = AddCircle(f.B, f.L, 0.6, 0.5, 0.1);
            var other = AddLine(f.B, f.L, 0.1, 0.9, 0.2, 0.9);
            var s = f.Start();
            f.Select(ToolId.Move, line, circle);

            // Far from every key point: the pivot is the reference.
            DragOpen(s, 0.45, 0.05, 0.55, 0.10);
            Check.That(f.Changes.Count == 0, "no BoardChanged while dragging");
            Check.That(s.Preview.Count == 2 && s.Preview[0] is LineShape && s.Preview[1] is CircleShape, $"two ghosts (got {s.Preview.Count})");
            Check.That(line.A == new Vec2(0.2, 0.2) && circle.Center == new Vec2(0.6, 0.5), "model untouched while dragging");
            Check.That(s.Preview[0].Style.Equals(Fancy), "ghost keeps the shape's style");
            Check.That(s.Cursor.HasValue, "cursor during the drag");
            Check.Near(s.Cursor.Value.X, 0.45 + 0.1, "cursor = pivot + t (x)", 1e-12);   // bounds 0.2..0.7, 0.2..0.6
            Check.Near(s.Cursor.Value.Y, 0.4 + 0.05, "cursor = pivot + t (y)", 1e-12);
            s.Pointer(Ev(PointerPhase.Up, 0.55, 0.10));

            Check.That(f.Changes.Count == 1 && f.Changes[0] == BoardChangeKind.Shapes, "one BoardChanged(Shapes)");
            Check.Near(line.A.X, 0.3, "A.x", 1e-12); Check.Near(line.A.Y, 0.25, "A.y", 1e-12);
            Check.Near(line.B.X, 0.4, "B.x", 1e-12); Check.Near(line.B.Y, 0.45, "B.y", 1e-12);
            Check.Near(circle.Center.X, 0.7, "centre x", 1e-12); Check.Near(circle.Center.Y, 0.55, "centre y", 1e-12);
            Check.That(circle.Radius == 0.1 && line.Style.Equals(Fancy), "radius and style unchanged");
            Check.That(other.A == new Vec2(0.1, 0.9), "unselected shape untouched");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "Preview empty and no cursor after Up");
            Check.That(f.Selected(line, circle) && s.ActiveTool == ToolId.Move, "still selected, still Move");
            Check.That(f.Toasts.Count == 0, "Move commits silently");
        }

        public static void MoveGrabsTheNearestKeyPoint()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.2, 0.2, 0.5, 0.2);
            var s = f.Start();
            f.Select(ToolId.Move, line);
            DragOpen(s, 0.52, 0.23, 0.62, 0.33);   // near B
            Check.Near(s.Cursor.Value.X, 0.6, "reference is B (x)", 1e-12);
            Check.Near(s.Cursor.Value.Y, 0.3, "reference is B (y)", 1e-12);
            s.Pointer(Ev(PointerPhase.Cancel, 0.62, 0.33));
            Check.That(line.B == new Vec2(0.5, 0.2), "cancel");

            DragOpen(s, 0.35, 0.5, 0.45, 0.6);     // 0.3 from the line: the pivot (0.35, 0.2)
            Check.Near(s.Cursor.Value.X, 0.45, "reference is the pivot (x)", 1e-12);
            Check.Near(s.Cursor.Value.Y, 0.3, "reference is the pivot (y)", 1e-12);
            s.Pointer(Ev(PointerPhase.Cancel, 0.45, 0.6));
        }

        public static void MoveSnapLandsExactlyAndExcludesOwnPoints()
        {
            var f = new Fixture();
            var moving = AddLine(f.B, f.L, 0.1, 0.1, 0.4, 0.3);
            var cross = AddLine(f.B, f.L, 0.1, 0.3, 0.4, 0.1);
            var s = f.Start(snap: true);   // 2x2 grid: corners at multiples of 0.5
            var crossing = new Vec2(0.25, 0.2);
            Check.That(HasCandidate(s, moving.A) && HasCandidate(s, moving.B) && HasCandidate(s, crossing), "precondition: own points");
            f.Select(ToolId.Move, moving);

            // Grab A, drag it near the grid corner (0.5, 0.5).
            s.Pointer(Ev(PointerPhase.Down, 0.11, 0.1));
            s.Pointer(Ev(PointerPhase.Move, 0.3, 0.3));
            Check.That(f.States == 1, $"one StateChanged when the exclusion starts (got {f.States})");
            Check.That(!HasCandidate(s, new Vec2(0.1, 0.1)) && !HasCandidate(s, new Vec2(0.4, 0.3)), "own endpoints excluded");
            Check.That(!HasCandidate(s, crossing), "crossing with the moving shape excluded");
            Check.That(HasCandidate(s, cross.A) && HasCandidate(s, cross.B), "other shape's endpoints stay");
            s.Pointer(Ev(PointerPhase.Move, 0.497, 0.508));
            Check.That(s.Cursor == new Vec2(0.5, 0.5), $"cursor on the corner ({s.Cursor})");
            Check.That(s.Preview.Count == 2, "ghost + hover ring");
            Check.That(f.States == 1, "no StateChanged per Move");
            s.Pointer(Ev(PointerPhase.Up, 0.497, 0.508));

            Check.That(moving.A == new Vec2(0.5, 0.5), $"A lands exactly on the corner ({moving.A.X:R}, {moving.A.Y:R})");
            Check.Near(moving.B.X, 0.8, "B moved by the same t (x)", 1e-12);
            Check.Near(moving.B.Y, 0.7, "B moved by the same t (y)", 1e-12);
            Check.That(f.Changes.Count == 1, "one BoardChanged");
            Check.That(HasCandidate(s, moving.A) && HasCandidate(s, moving.B), "the moved shape's endpoints are candidates again");
            Check.That(!HasCandidate(s, new Vec2(0.1, 0.1)), "old endpoint gone");
        }

        public static void MoveIsClampedToTheBoard()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.6, 0.5, 0.8, 0.5);
            var s = f.Start();
            f.Select(ToolId.Move, line);
            Drag(s, 0.5, 0.1, 0.99, 0.12);
            Check.Near(line.B.X, 1.0, "stops at the right edge", 1e-12);
            Check.Near(line.A.X, 0.8, "keeps its length", 1e-12);
            Check.Near(line.A.Y, 0.52, "y follows", 1e-12);
            Drag(s, 0.5, 0.5, 0.5, -0.4);   // off-board Up position: last on-board sample kept, clamp at the bottom
            Check.That(line.A.Y >= -1e-12 && line.A.Y <= 0.52, $"inside at the bottom ({line.A.Y})");

            // A circle that already sticks out on the left: never pushed further, never forced in.
            var f2 = new Fixture();
            var big = AddCircle(f2.B, f2.L, 0.1, 0.5, 0.2);
            var s2 = f2.Start();
            f2.Select(ToolId.Move, big);
            Drag(s2, 0.6, 0.9, 0.4, 0.9);
            Check.That(big.Center == new Vec2(0.1, 0.5) && f2.Changes.Count == 0, "left: not moved (identity, no BoardChanged)");
            Drag(s2, 0.6, 0.9, 0.65, 0.9);
            Check.Near(big.Center.X, 0.15, "right: moves freely, no jump", 1e-12);
        }

        // ---------- Scale ----------

        public static void ScaleGrowsAndShrinksAboutThePivot()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.3, 0.5, 0.5, 0.5, Fancy);
            var s = f.Start();
            f.Select(ToolId.Scale, line);
            Drag(s, 0.6, 0.5, 0.7, 0.5);   // pivot (0.4, 0.5), r0 0.2 -> 0.3: s = 1.5
            Check.Near(line.A.X, 0.25, "A about the pivot", 1e-12);
            Check.Near(line.B.X, 0.55, "B about the pivot", 1e-12);
            Check.That(line.A.Y == 0.5 && line.B.Y == 0.5 && line.Style.Equals(Fancy), "on the pivot's row, width unchanged");
            Check.That(f.Changes.Count == 1 && f.Last == EditorSession.ScaledToast(1.5) && f.Last == "Scaled to 150%", $"toast {f.Last}");

            f.Reset();
            Drag(s, 0.7, 0.5, 0.55, 0.5);   // pivot still (0.4, 0.5); r0 0.3 -> 0.15: s = 0.5
            Check.Near(line.A.X, 0.325, "shrunk A", 1e-12);
            Check.Near(line.B.X, 0.475, "shrunk B", 1e-12);
            Check.That(f.Last == "Scaled to 50%", f.Last);

            // A circle scales its centre and radius about the selection's pivot.
            var f2 = new Fixture();
            var c = AddCircle(f2.B, f2.L, 0.4, 0.4, 0.1);
            var l2 = AddLine(f2.B, f2.L, 0.5, 0.5, 0.6, 0.6);   // bounds 0.3..0.6: pivot (0.45, 0.45)
            var s2 = f2.Start();
            f2.Select(ToolId.Scale, c, l2);
            DragOpen(s2, 0.65, 0.45, 0.75, 0.45);   // r0 0.2 -> 0.3: s 1.5
            Check.That(s2.Preview.Count == 2, "two ghosts");
            Check.That(c.Radius == 0.1, "model untouched while dragging");
            s2.Pointer(Ev(PointerPhase.Up, 0.75, 0.45));
            Check.Near(c.Radius, 0.15, "radius", 1e-12);
            Check.Near(c.Center.X, 0.375, "centre x", 1e-12);
            Check.Near(c.Center.Y, 0.375, "centre y", 1e-12);
            Check.Near(l2.B.X, 0.675, "line end", 1e-12);
        }

        public static void ScaleStartsAtOneAndIdentityCommitsNothing()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.3, 0.5, 0.5, 0.5);
            var s = f.Start();
            f.Select(ToolId.Scale, line);
            s.Pointer(Ev(PointerPhase.Down, 0.62, 0.53));
            s.Pointer(Ev(PointerPhase.Move, 0.8, 0.53));
            s.Pointer(Ev(PointerPhase.Move, 0.62, 0.53));
            Check.That(s.Preview.Count == 1, "ghost");
            var g = (LineShape)s.Preview[0];
            Check.That(g.A == line.A && g.B == line.B, "back at F0: the ghost is the original exactly (s = 1)");
            s.Pointer(Ev(PointerPhase.Up, 0.62, 0.53));
            Check.That(f.Changes.Count == 0 && f.Toasts.Count == 0, "identity: no BoardChanged, no toast");
            Check.That(line.A == new Vec2(0.3, 0.5), "unchanged");
        }

        public static void ScaleIsClampedBySizeAndBoard()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.3, 0.5, 0.4, 0.5);   // length 0.1: sMin 0.1
            var s = f.Start();
            f.Select(ToolId.Scale, line);
            Drag(s, 0.75, 0.5, 0.36, 0.5);   // toward the pivot (0.35, 0.5): would go far below
            Check.Near(Vec2.Distance(line.A, line.B), EditorSession.MinStrokeLength, "never below MinStrokeLength", 1e-12);
            Check.Near((line.A.X + line.B.X) * 0.5, 0.35, "pivot fixed", 1e-12);

            var f2 = new Fixture();
            var l2 = AddLine(f2.B, f2.L, 0.3, 0.5, 0.5, 0.5);   // pivot (0.4, 0.5): sMax 4 (left side)
            var s2 = f2.Start();
            f2.Select(ToolId.Scale, l2);
            Drag(s2, 0.45, 0.5, 0.999, 0.5, 20);   // r0 = ScaleGrabMin: s_raw reaches about 12
            Check.Near(l2.A.X, 0.0, "stops at the left edge", 1e-12);
            Check.Near(l2.B.X, 0.8, "uniform", 1e-12);

            var f3 = new Fixture();
            var big = AddCircle(f3.B, f3.L, 0.5, 0.5, 0.6);   // already outside: may shrink, never grow
            var s3 = f3.Start();
            f3.Select(ToolId.Scale, big);
            Drag(s3, 0.9, 0.5, 0.99, 0.5);
            Check.That(big.Radius == 0.6 && f3.Changes.Count == 0, "can't grow");
            Drag(s3, 0.9, 0.5, 0.7, 0.5);
            Check.Near(big.Radius, 0.6 * 0.5, "can shrink", 1e-12);
        }

        public static void ScaleSnapPutsTheRingThroughTheCandidate()
        {
            var f = new Fixture();
            var c = AddCircle(f.B, f.L, 0.5, 0.5, 0.2);
            var s = f.Start(snap: true);   // 2x2: edge midpoint (0.75, 0.5)
            f.Select(ToolId.Scale, c);
            s.Pointer(Ev(PointerPhase.Down, 0.7, 0.5));
            s.Pointer(Ev(PointerPhase.Move, 0.72, 0.5));
            Check.That(!HasCandidate(s, new Vec2(0.7, 0.5)), "own division point excluded");
            s.Pointer(Ev(PointerPhase.Move, 0.743, 0.507));
            Check.That(s.Preview.Count == 2, "ghost + hover ring");
            s.Pointer(Ev(PointerPhase.Up, 0.743, 0.507));
            Check.That(c.Radius == 0.25 && c.Center == new Vec2(0.5, 0.5), $"radius exactly 0.25 ({c.Radius:R}), centre fixed");
            Check.That(f.Last == "Scaled to 125%", f.Last);
            double k = 0.25 * Math.Sqrt(0.5);
            Check.That(HasCandidate(s, new Vec2(0.5 + k, 0.5 + k)), "new division points after Up");
        }

        public static void ScaledToastText()
        {
            Check.That(EditorSession.ScaledToast(1.5) == "Scaled to 150%", "150");
            Check.That(EditorSession.ScaledToast(1.0 / 3) == "Scaled to 33%", "33");
            Check.That(EditorSession.ScaledToast(0.1) == "Scaled to 10%", "10");
            Check.That(EditorSession.DuplicatedToast(1) == "Copy placed: drag to move it", "1 copy");
            Check.That(EditorSession.DuplicatedToast(3) == "3 copies placed: drag to move them", "3 copies");
        }

        // ---------- Duplicate ----------

        public static void DuplicatePlacesCopiesOnTheirLayersAndSwitchesToMove()
        {
            var f = new Fixture();
            var top = new Layer(f.B.NewId(), "Layer 2");
            f.B.Layers.Add(top);
            var l1 = AddLine(f.B, f.L, 0.1, 0.1, 0.3, 0.3, Fancy);
            var c2 = AddCircle(f.B, top, 0.6, 0.6, 0.1);
            var poly = new PolygonShape(f.B.NewId(), new Vec2(0.3, 0.6), 6, 0.05, 0.2, St);
            top.Shapes.Add(poly);
            var s = f.Start();
            f.Select(ToolId.Duplicate, l1, c2, poly);
            var before = new HashSet<EntityId> { l1.Id, c2.Id, poly.Id, f.L.Id, top.Id };

            DragOpen(s, 0.9, 0.05, 0.95, 0.07);   // far: the pivot; t = (0.05, 0.02)
            Check.That(s.Preview.Count == 3 && f.Changes.Count == 0, "three ghosts, nothing committed");
            s.Pointer(Ev(PointerPhase.Up, 0.95, 0.07));

            Check.That(f.L.Shapes.Count == 2 && top.Shapes.Count == 4, "one copy on the bottom layer, two on top");
            var l1c = (LineShape)f.L.Shapes[1];
            var c2c = (CircleShape)top.Shapes[2];
            var pc = (PolygonShape)top.Shapes[3];
            foreach (Shape copy in new Shape[] { l1c, c2c, pc })
                Check.That(copy.Id != EntityId.None && before.Add(copy.Id), $"copy id {copy.Id} is new and unique");
            Check.Near(l1c.A.X, 0.15, "copy A x", 1e-12); Check.Near(l1c.A.Y, 0.12, "copy A y", 1e-12);
            Check.Near(c2c.Center.X, 0.65, "copy centre", 1e-12);
            Check.That(c2c.Radius == 0.1 && l1c.Style.Equals(Fancy), "radius and style copied");
            Check.That(pc.Sides == 6 && pc.RotationRad == 0.2 && pc.Radius == 0.05, "polygon copied");
            Check.Near(pc.Center.X, 0.35, "polygon centre moved", 1e-12);
            Check.That(l1.A == new Vec2(0.1, 0.1) && c2.Center == new Vec2(0.6, 0.6) && poly.Center == new Vec2(0.3, 0.6), "originals untouched");
            Check.That(f.Changes.Count == 1 && f.Changes[0] == BoardChangeKind.Shapes, "one BoardChanged(Shapes)");
            Check.That(f.Selected(l1c, c2c, pc), "the copies are the selection");
            Check.That(s.ActiveTool == ToolId.Move && s.ToolHint == EditorSession.MoveHintToast, "tool is Move");
            Check.That(f.States == 1, $"one StateChanged (got {f.States})");
            Check.That(f.Toasts.Count == 1 && f.Last == EditorSession.DuplicatedToast(3), $"toast {f.Last}");

            // The next drag moves only the copies.
            f.Reset();
            Drag(s, 0.9, 0.05, 0.9, 0.1);
            Check.Near(l1c.A.Y, 0.17, "copy moved again", 1e-12);
            Check.That(l1.A == new Vec2(0.1, 0.1), "original still untouched");
        }

        public static void DuplicateZeroDragOrCancelCreatesNothing()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.2, 0.2, 0.4, 0.4);
            var s = f.Start();
            f.Select(ToolId.Duplicate, line);
            s.Pointer(Ev(PointerPhase.Down, 0.8, 0.8));
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.8));
            s.Pointer(Ev(PointerPhase.Move, 0.8, 0.8));
            s.Pointer(Ev(PointerPhase.Up, 0.8, 0.8));
            Check.That(f.L.Shapes.Count == 1 && f.Changes.Count == 0, "zero drag: no copy");
            Check.That(f.Last == EditorSession.NoCopyToast && s.ActiveTool == ToolId.Duplicate, "NoCopyToast, still Duplicate");

            f.Reset();
            DragOpen(s, 0.8, 0.8, 0.9, 0.9);
            s.Pointer(Ev(PointerPhase.Cancel, 0.9, 0.9));
            Check.That(f.L.Shapes.Count == 1 && f.Changes.Count == 0 && s.Preview.Count == 0, "cancel: no copy, no ghosts");
            Check.That(f.Toasts.Count == 0, "cancel is silent");
        }

        public static void DuplicateSnapsOntoTheOriginalAndCopiesBecomeCandidates()
        {
            var f = new Fixture();
            var line = AddLine(f.B, f.L, 0.1, 0.1, 0.2, 0.15);
            var s = f.Start(snap: true);
            f.Select(ToolId.Duplicate, line);
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.11));        // grab A
            s.Pointer(Ev(PointerPhase.Move, 0.15, 0.13));
            Check.That(HasCandidate(s, line.B) && f.States == 0, "no exclusion in Duplicate (no StateChanged)");
            s.Pointer(Ev(PointerPhase.Move, 0.205, 0.158));    // A near the original's B
            s.Pointer(Ev(PointerPhase.Up, 0.205, 0.158));
            var copy = (LineShape)f.L.Shapes[1];
            Check.That(copy.A == line.B, $"chained end-to-end exactly ({copy.A.X:R}, {copy.A.Y:R})");
            Check.That(HasCandidate(s, copy.B), "the copy's far end is a candidate after Up");
        }

        // ---------- cancel ----------

        public static void CancelLeavesGeometryBitIdentical()
        {
            foreach (var tool in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
                for (int how = 0; how < 3; how++)
                {
                    var f = new Fixture();
                    var line = AddLine(f.B, f.L, 0.21, 0.33, 0.47, 0.29);
                    var circle = AddCircle(f.B, f.L, 0.61, 0.57, 0.13);
                    var s = f.Start(snap: true);
                    int candidates = s.SnapCandidates.Count;
                    f.Select(tool, line, circle);
                    DragOpen(s, 0.8, 0.85, 0.9, 0.95);
                    Check.That(s.Preview.Count >= 2, $"{tool}/{how}: ghosts");
                    if (tool != ToolId.Duplicate) Check.That(s.SnapCandidates.Count < candidates, $"{tool}: excluding");
                    if (how == 0) s.Pointer(Ev(PointerPhase.Cancel, 0.9, 0.95));
                    else if (how == 1) s.SetTool(ToolId.Line);
                    else s.ClearBoard();
                    Check.That(line.A == new Vec2(0.21, 0.33) && line.B == new Vec2(0.47, 0.29), $"{tool}/{how}: line identical");
                    Check.That(circle.Center == new Vec2(0.61, 0.57) && circle.Radius == 0.13, $"{tool}/{how}: circle identical");
                    Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, $"{tool}/{how}: Preview empty");
                    if (how < 2)
                    {
                        Check.That(f.Changes.Count == 0 && f.L.Shapes.Count == 2, $"{tool}/{how}: no BoardChanged, no copies");
                        Check.That(s.SnapCandidates.Count == candidates, $"{tool}/{how}: own candidates back");
                    }
                    if (how == 1) Check.That(s.ActiveTool == ToolId.Line && f.States == (tool == ToolId.Duplicate ? 1 : 2),
                        $"{tool}: SetTool(Line) raises one StateChanged (plus the exclusion start), got {f.States}");
                }
        }

        // ---------- taps, fallback, hidden layers ----------

        public static void TapsSwitchTheTargetOrDoNothing()
        {
            var f = new Fixture();
            var a = AddLine(f.B, f.L, 0.1, 0.2, 0.4, 0.2);
            var b = AddCircle(f.B, f.L, 0.7, 0.7, 0.1);
            var s = f.Start();
            f.Select(ToolId.Move, a);
            Tap(s, 0.8, 0.7);   // on b's ring
            Check.That(f.Selected(b) && f.States == 1 && f.Last == EditorSession.SelectedCountToast(1), "tap picks b");
            Check.That(s.ActiveTool == ToolId.Move && f.Changes.Count == 0, "still Move, nothing moved");
            f.Reset();
            Tap(s, 0.5, 0.5);   // empty board
            Check.That(f.Selected(b) && f.States == 0 && f.Toasts.Count == 0 && f.Changes.Count == 0, "empty tap: nothing");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "no visuals after a tap");
        }

        public static void EmptySelectionFallsBackToSelect()
        {
            var f = new Fixture();
            var top = new Layer(f.B.NewId(), "Layer 2");
            f.B.Layers.Add(top);
            var a = AddLine(f.B, top, 0.1, 0.2, 0.4, 0.2);
            var s = f.Start();
            f.Select(ToolId.Move, a);
            s.SetLayerVisible(top.Id, false);
            Check.That(s.SelectionCount == 0 && s.ActiveTool == ToolId.SelectMove, "hidden: Select");
            Check.That(f.States == 1 && s.ToolHint == EditorSession.SelectHintToast, $"one StateChanged (got {f.States})");
            Check.That(f.Toasts.Count == 0, "no toast");

            s.SetLayerVisible(top.Id, true);
            f.Select(ToolId.Scale, a);
            s.ClearSelection();
            Check.That(s.ActiveTool == ToolId.SelectMove && f.States == 1, "ClearSelection in Scale: Select, one event");

            f.Select(ToolId.Duplicate, a);
            s.SetSelection(null);
            Check.That(s.ActiveTool == ToolId.SelectMove, "SetSelection(empty): Select");

            f.Select(ToolId.Move, a);
            s.ClearBoard();
            Check.That(s.ActiveTool == ToolId.SelectMove, "ClearBoard: Select");

            // Mid-gesture the tool stays; it falls back once the gesture ends.
            var f2 = new Fixture();
            var top2 = new Layer(f2.B.NewId(), "Layer 2");
            f2.B.Layers.Add(top2);
            var b = AddLine(f2.B, top2, 0.1, 0.2, 0.4, 0.2);
            var s2 = f2.Start();
            f2.Select(ToolId.Move, b);
            DragOpen(s2, 0.5, 0.5, 0.6, 0.6);
            s2.SetLayerVisible(top2.Id, false);
            Check.That(s2.ActiveTool == ToolId.Move, "still Move during the gesture");
            s2.Pointer(Ev(PointerPhase.Up, 0.6, 0.6));
            Check.That(s2.ActiveTool == ToolId.SelectMove && b.A == new Vec2(0.1, 0.2), "Select after Up; nothing moved");
        }

        public static void HiddenLayersAreNeverTransformed()
        {
            foreach (var tool in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
            {
                var f = new Fixture();
                var hidden = new Layer(f.B.NewId(), "Hidden") { Visible = false };
                f.B.Layers.Add(hidden);
                var v = AddLine(f.B, f.L, 0.2, 0.2, 0.4, 0.2);
                var h = AddLine(f.B, hidden, 0.2, 0.6, 0.4, 0.6);
                var s = f.Start();
                f.Select(tool, v);
                s.Selection.Ids.Add(h.Id);   // planted directly, no prune yet
                Drag(s, 0.7, 0.3, 0.8, 0.35);
                Check.That(h.A == new Vec2(0.2, 0.6) && h.B == new Vec2(0.4, 0.6), $"{tool}: hidden shape untouched");
                Check.That(hidden.Shapes.Count == 1, $"{tool}: no copy on the hidden layer");
                Check.That(f.Changes.Count == 1, $"{tool}: the visible shape committed");
            }
        }

        // ---------- allocation ----------

        sealed class ScaleMetric : ISnapMetric
        {
            public double PxPerUnit = 900, Tol = 24;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                screenPx = new Vec2(board.X * PxPerUnit + 5, board.Y * PxPerUnit - 3);
                return true;
            }
        }

        static int s_events;

        public static void TransformMovesAllocateNothing()
        {
            foreach (bool withMetric in new[] { false, true })
                foreach (var tool in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
                {
                    var s = new EditorSession(GoldenBoards.StarOnFullBoard());
                    if (withMetric) s.SnapMetric = new ScaleMetric();
                    s.PreviewChanged += () => s_events++;
                    s.StateChanged += () => s_events++;
                    var all = new List<EntityId>();
                    foreach (var l in s.Board.Layers)
                        foreach (var sh in l.Shapes)
                            if (sh is CircleShape || all.Count < 6) all.Add(sh.Id);
                    s.SetTool(ToolId.SelectMove);
                    s.SetSelection(all);
                    s.SetTool(tool);
                    Check.That(s.ActiveTool == tool, "precondition");

                    var moves = new PointerEvent[200];
                    for (int i = 0; i < moves.Length; i++)
                    {
                        double t = i / 199.0;
                        moves[i] = new PointerEvent(PointerPhase.Move,
                            new Vec2(0.5 + 0.35 * Math.Cos(7 * t), 0.5 + 0.35 * Math.Sin(5 * t)), true, i);
                    }
                    for (int round = 0; round < 2; round++)   // warm-up, then measured
                    {
                        s.Pointer(Ev(PointerPhase.Down, 0.5 + 0.35, 0.5));
                        for (int i = 0; i < 4; i++) s.Pointer(moves[i]);   // moves[0] is the Down point: drag Moves 1..3
                        int before = s_events;
                        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                        for (int i = 4; i < moves.Length; i++) s.Pointer(moves[i]);
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
                        int events = s_events - before;
                        s.Pointer(Ev(PointerPhase.Cancel, 0.5, 0.5));
                        if (round == 0) continue;
                        Check.That(events > 150, $"{tool}: the moves did work ({events} events)");
                        Check.That(bytes == 0, $"{tool}, metric {withMetric}: 196 Moves allocated {bytes} bytes");
                    }
                }
        }
    }
}
