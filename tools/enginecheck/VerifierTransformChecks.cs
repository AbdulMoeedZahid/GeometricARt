using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER: Move, Scale and Duplicate (p3-transform-briefs B1/B2), written from the spec only. Covers ShapeTransform
    /// and TransformGeometry, SnapIndex.ExcludedShapes, SetTool rules and strings, Move (reference point, exact
    /// translation, exact landing on snaps, self-exclusion, the "never worse" clamp), Scale (pivot, finger factor,
    /// ScaleGrabMin, sMin / sMax, radial snap with exact pins), Duplicate (layers, ids, selection, switch to Move, zero
    /// drag, chaining onto the original), Cancel bit-identity for every tool and cancel path, taps in transform tools,
    /// NormalizeTool fallback, hidden layers, screen metrics, and allocation-free Moves.
    /// Exact (==) assertions only where the spec promises exactness or the inputs are dyadic; Near(1e-12) elsewhere.
    /// </summary>
    public static class VerifierTransformChecks
    {
        const string MoveHint = "Move: drag to move the selection; tap a shape to switch";
        const string ScaleHint = "Scale: drag away from the centre mark to grow, toward it to shrink";
        const string DupHint = "Duplicate: drag to place a copy";
        const string SelectHint = "Select: drag a box around whole shapes, or tap one";
        const string SelectFirst = "Select shapes first";
        const string NoCopy = "Drag to place the copy";
        const double GrabRadius = 0.08, GrabMin = 0.05;   // MoveGrabRadius, ScaleGrabMin (spec values)

        static readonly ToolId[] Transforms = { ToolId.Move, ToolId.Scale, ToolId.Duplicate };

        // ================= helpers =================

        static double s_T;
        static PointerEvent Ev(PointerPhase p, Vec2 v, bool on = true) => new PointerEvent(p, v, on, s_T += 0.016);
        static Vec2 V(double x, double y) => new Vec2(x, y);
        static string P(Vec2 v) => $"({v.X:R}, {v.Y:R})";

        static void Down(EditorSession s, Vec2 p) => s.Pointer(Ev(PointerPhase.Down, p));
        static void MoveTo(EditorSession s, Vec2 p, bool on = true) => s.Pointer(Ev(PointerPhase.Move, p, on));
        static void Up(EditorSession s, Vec2 p, bool on = true) => s.Pointer(Ev(PointerPhase.Up, p, on));
        static void Cancel(EditorSession s) => s.Pointer(Ev(PointerPhase.Cancel, V(0, 0), false));

        /// <summary>Down at a, then <paramref name="steps"/> Moves along the segment ending exactly at b. No Up.</summary>
        static void DragNoUp(EditorSession s, Vec2 a, Vec2 b, int steps = 6)
        {
            Down(s, a);
            for (int i = 1; i < steps; i++)
            {
                double k = (double)i / steps;
                MoveTo(s, V(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k));
            }
            MoveTo(s, b);
        }

        static void Drag(EditorSession s, Vec2 a, Vec2 b, int steps = 6)
        {
            DragNoUp(s, a, b, steps);
            Up(s, b);
        }

        static void Tap(EditorSession s, Vec2 p)
        {
            Down(s, p);
            Up(s, p);
        }

        static LineShape AddLine(Board b, Layer l, double ax, double ay, double bx, double by, StrokeStyle? st = null)
        {
            var sh = new LineShape(b.NewId(), V(ax, ay), V(bx, by), st ?? StrokeStyle.Default);
            l.Shapes.Add(sh);
            return sh;
        }

        static CircleShape AddCircle(Board b, Layer l, double cx, double cy, double r, StrokeStyle? st = null)
        {
            var sh = new CircleShape(b.NewId(), V(cx, cy), r, st ?? StrokeStyle.Default);
            l.Shapes.Add(sh);
            return sh;
        }

        static PolygonShape AddPoly(Board b, Layer l, double cx, double cy, int sides, double r, double rot, StrokeStyle? st = null)
        {
            var sh = new PolygonShape(b.NewId(), V(cx, cy), sides, r, rot, st ?? StrokeStyle.Default);
            l.Shapes.Add(sh);
            return sh;
        }

        static readonly StrokeStyle RedThickDotted = new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted);
        static readonly StrokeStyle BlueThin = new StrokeStyle(Palette.Blue, LineWidth.Thin, LineStyle.Solid);

        /// <summary>Two layers, bottom then top; the top is active.</summary>
        static Board TwoLayerBoard(out Layer bottom, out Layer top)
        {
            var b = new Board();
            bottom = new Layer(b.NewId(), "Bottom");
            top = new Layer(b.NewId(), "Top");
            b.Layers.Add(bottom);
            b.Layers.Add(top);
            b.ActiveLayerId = top.Id;
            return b;
        }

        static int ShapeCount(EditorSession s)
        {
            int n = 0;
            foreach (var l in s.Board.Layers) n += l.Shapes.Count;
            return n;
        }

        /// <summary>Select exactly <paramref name="shapes"/> (via Select + SetSelection) and enter tool t.</summary>
        static void Enter(EditorSession s, ToolId t, params Shape[] shapes)
        {
            s.SetTool(ToolId.SelectMove);
            var ids = new List<EntityId>();
            foreach (var sh in shapes) ids.Add(sh.Id);
            s.SetSelection(ids);
            Check.That(s.SelectionCount == shapes.Length, $"precondition: {shapes.Length} selected, got {s.SelectionCount}");
            s.SetTool(t);
            Check.That(s.ActiveTool == t, $"precondition: tool {t} accepted with a selection, got {s.ActiveTool}");
        }

        static void Select(EditorSession s, params Shape[] shapes)
        {
            var ids = new List<EntityId>();
            foreach (var sh in shapes) ids.Add(sh.Id);
            s.SetSelection(ids);
        }

        static string Ids(IEnumerable<EntityId> ids)
        {
            var l = new List<int>();
            foreach (var id in ids) l.Add(id.Value);
            l.Sort();
            return "{" + string.Join(",", l) + "}";
        }

        static void Selected(EditorSession s, string what, params Shape[] shapes)
        {
            var want = new HashSet<EntityId>();
            foreach (var sh in shapes) want.Add(sh.Id);
            Check.That(s.Selection.Ids.SetEquals(want), $"{what}: selection {Ids(s.Selection.Ids)}, expected {Ids(want)}");
        }

        static void Exact(Vec2 actual, Vec2 expected, string what) =>
            Check.That(actual == expected, $"{what}: expected exactly {P(expected)}, got {P(actual)}");

        static void Near(Vec2 actual, Vec2 expected, string what, double eps = 1e-12)
        {
            Check.Near(actual.X, expected.X, what + " X", eps);
            Check.Near(actual.Y, expected.Y, what + " Y", eps);
        }

        static Vec2 Plus(Vec2 p, Vec2 t) => V(p.X + t.X, p.Y + t.Y);
        static Vec2 Minus(Vec2 a, Vec2 b) => V(a.X - b.X, a.Y - b.Y);
        static Vec2 About(Vec2 p, Vec2 c, double s) => V(c.X + s * (p.X - c.X), c.Y + s * (p.Y - c.Y));

        static Vec2 Unit(Vec2 v)
        {
            double l = v.Length;
            return V(v.X / l, v.Y / l);
        }

        /// <summary>Centre of the selection's union bounds (the spec's pivot).</summary>
        static Vec2 Pivot(EditorSession s)
        {
            Check.That(s.TryGetSelectionBounds(out BoardRect b), "selection has bounds");
            return V(0.5 * (b.MinX + b.MaxX), 0.5 * (b.MinY + b.MaxY));
        }

        /// <summary>The spec's finger factor s_raw for a Scale drag from f0 to f about p.</summary>
        static double FingerScale(Vec2 p, Vec2 f0, Vec2 f)
        {
            double r0 = Vec2.Distance(f0, p), rg = Math.Max(r0, GrabMin);
            return Math.Max(1e-6, 1 + (Vec2.Distance(f, p) - r0) / rg);
        }

        static bool HasCand(EditorSession s, Vec2 p)
        {
            var list = s.SnapCandidates;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Position == p) return true;
            return false;
        }

        static bool HasCandNear(EditorSession s, Vec2 p, double eps)
        {
            var list = s.SnapCandidates;
            for (int i = 0; i < list.Count; i++)
                if (Vec2.Distance(list[i].Position, p) <= eps) return true;
            return false;
        }

        static bool Has(IReadOnlyList<SnapCandidate> list, Vec2 p, double eps = 0)
        {
            for (int i = 0; i < list.Count; i++)
                if (Vec2.Distance(list[i].Position, p) <= eps) return true;
            return false;
        }

        /// <summary>The engine's snap hover ring in Preview (Thin solid circle of HoverRingRadius), or null.</summary>
        static CircleShape HoverRing(EditorSession s)
        {
            foreach (var sh in s.Preview)
                if (sh is CircleShape c && c.Radius == EditorSession.HoverRingRadius && c.Style.Width == LineWidth.Thin
                    && c.Style.Dash == LineStyle.Solid)
                    return c;
            return null;
        }

        static List<T> Ghosts<T>(EditorSession s) where T : Shape
        {
            var l = new List<T>();
            foreach (var sh in s.Preview)
                if (sh is T t && !(sh is CircleShape c && c.Radius == EditorSession.HoverRingRadius)) l.Add(t);
            return l;
        }

        /// <summary>Bit-identical geometry, id and style (== on every field).</summary>
        static void Same(Shape now, Shape before, string what)
        {
            Check.That(now.GetType() == before.GetType(), $"{what}: type changed");
            Check.That(now.Id == before.Id, $"{what}: id changed {before.Id} -> {now.Id}");
            Check.That(now.Style.Equals(before.Style), $"{what}: style changed");
            switch (now)
            {
                case LineShape l:
                    var ol = (LineShape)before;
                    Check.That(l.A == ol.A && l.B == ol.B, $"{what}: line {P(ol.A)}-{P(ol.B)} became {P(l.A)}-{P(l.B)}");
                    break;
                case CircleShape c:
                    var oc = (CircleShape)before;
                    Check.That(c.Center == oc.Center && c.Radius == oc.Radius,
                        $"{what}: circle {P(oc.Center)} r {oc.Radius:R} became {P(c.Center)} r {c.Radius:R}");
                    break;
                case PolygonShape g:
                    var og = (PolygonShape)before;
                    Check.That(g.Center == og.Center && g.Radius == og.Radius && g.Sides == og.Sides && g.RotationRad == og.RotationRad,
                        $"{what}: polygon changed");
                    break;
            }
        }

        static List<Shape> Snapshot(IEnumerable<Shape> shapes)
        {
            var l = new List<Shape>();
            foreach (var s in shapes) l.Add(s.Clone());
            return l;
        }

        static void SameAll(IList<Shape> now, IList<Shape> before, string what)
        {
            Check.That(now.Count == before.Count, $"{what}: {before.Count} shapes became {now.Count}");
            for (int i = 0; i < now.Count; i++) Same(now[i], before[i], $"{what} [{i}]");
        }

        /// <summary>Counts every session event; Log keeps the order (B = BoardChanged, S = StateChanged, T = Toast).</summary>
        sealed class Rec
        {
            public int States, Previews;
            public readonly List<BoardChangeKind> Boards = new List<BoardChangeKind>();
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> Log = new List<string>();

            public Rec(EditorSession s)
            {
                s.StateChanged += () => { States++; Log.Add("S"); };
                s.PreviewChanged += () => Previews++;
                s.BoardChanged += k => { Boards.Add(k); Log.Add("B"); };
                s.Toast += t => { Toasts.Add(t); Log.Add("T"); };
            }

            public void Reset()
            {
                States = Previews = 0;
                Boards.Clear();
                Toasts.Clear();
                Log.Clear();
            }

            public string T => "[" + string.Join(" | ", Toasts) + "]";
            public bool AnyShapesChange => Boards.Contains(BoardChangeKind.Shapes);
        }

        // ================= contracts, strings =================

        public static void V_ToolIdMoveAppendedAndStrings()
        {
            Check.That((int)ToolId.None == 0 && (int)ToolId.Line == 1 && (int)ToolId.SelectMove == 2 && (int)ToolId.Rotate == 3
                       && (int)ToolId.Circle == 4 && (int)ToolId.Snap == 5 && (int)ToolId.Scale == 6 && (int)ToolId.Stamps == 7
                       && (int)ToolId.Erase == 8 && (int)ToolId.Duplicate == 9, "existing ToolId ordinals unchanged");
            Check.That((int)ToolId.Move == 10, $"Move appended last (10), got {(int)ToolId.Move}");
            Check.That(Enum.GetValues(typeof(ToolId)).Length == 11, "11 ToolId values");

            Check.That(EditorSession.MoveHintToast == MoveHint, $"MoveHintToast '{EditorSession.MoveHintToast}'");
            Check.That(EditorSession.ScaleHintToast == ScaleHint, $"ScaleHintToast '{EditorSession.ScaleHintToast}'");
            Check.That(EditorSession.DuplicateHintToast == DupHint, $"DuplicateHintToast '{EditorSession.DuplicateHintToast}'");
            Check.That(EditorSession.SelectFirstToast == SelectFirst, $"SelectFirstToast '{EditorSession.SelectFirstToast}'");
            Check.That(EditorSession.NoCopyToast == NoCopy, $"NoCopyToast '{EditorSession.NoCopyToast}'");
            Check.That(EditorSession.DuplicatedToast(1) == "Copy placed: drag to move it", EditorSession.DuplicatedToast(1));
            Check.That(EditorSession.DuplicatedToast(2) == "2 copies placed: drag to move them", EditorSession.DuplicatedToast(2));
            Check.That(EditorSession.DuplicatedToast(13) == "13 copies placed: drag to move them", EditorSession.DuplicatedToast(13));
            Check.That(EditorSession.ScaledToast(1.5) == "Scaled to 150%", EditorSession.ScaledToast(1.5));
            Check.That(EditorSession.ScaledToast(0.5) == "Scaled to 50%", EditorSession.ScaledToast(0.5));
            Check.That(EditorSession.ScaledToast(1.0 / 3) == "Scaled to 33%", EditorSession.ScaledToast(1.0 / 3));
            Check.That(EditorSession.ScaledToast(1.236) == "Scaled to 124%", EditorSession.ScaledToast(1.236));
            Check.That(EditorSession.ScaledToast(2.004) == "Scaled to 200%", EditorSession.ScaledToast(2.004));
            Check.That(EditorSession.ScaledToast(0.0123) == "Scaled to 1%", EditorSession.ScaledToast(0.0123));
        }

        // ================= ShapeTransform / TransformGeometry =================

        public static void V_ShapeTransformTranslationAndScale()
        {
            var t = ShapeTransform.Translation(V(0.1, -0.05));
            Check.That(t.Offset == V(0.1, -0.05) && t.Scale == 1, "Translation: Offset t, Scale 1");
            Check.That(!t.IsIdentity, "a real translation is not the identity");
            Vec2 p = V(0.3, 0.7);
            Exact(t.Apply(p), V(0.3 + 0.1, 0.7 + -0.05), "translation is exactly p + Offset");
            Check.That(ShapeTransform.Translation(Vec2.Zero).IsIdentity, "Translation(0,0) is the identity");
            Exact(ShapeTransform.Translation(Vec2.Zero).Apply(p), p, "identity translation leaves p bit-identical");

            Vec2 pivot = V(0.4, 0.5);
            var s = ShapeTransform.ScaleAbout(pivot, 2.0);
            Check.That(s.Pivot == pivot && s.Scale == 2.0 && s.Offset == Vec2.Zero, "ScaleAbout: Pivot, Scale, no Offset");
            Check.That(!s.IsIdentity, "scale 2 is not the identity");
            Near(s.Apply(V(0.5, 0.75)), V(0.6, 1.0), "scale 2 about the pivot");
            Near(s.Apply(pivot), pivot, "the pivot is fixed", 1e-15);
            Near(ShapeTransform.ScaleAbout(pivot, 0.5).Apply(V(0.2, 0.1)), V(0.3, 0.3), "scale 0.5 shrinks toward the pivot");
            Check.That(ShapeTransform.ScaleAbout(pivot, 1.0).IsIdentity, "ScaleAbout(p, 1) is the identity");
        }

        public static void V_ApplyLineCirclePolygonIntoDstAndInPlace()
        {
            var tr = ShapeTransform.Translation(V(0.125, -0.0625));
            var sc = ShapeTransform.ScaleAbout(V(0.4, 0.4), 0.5);

            // Line into a different dst: geometry written, dst's Id and Style untouched, src untouched.
            var line = new LineShape(new EntityId(7), V(0.2, 0.3), V(0.6, 0.5), RedThickDotted);
            var dst = new LineShape(new EntityId(99), V(9, 9), V(9, 9), StrokeStyle.Default);
            Check.That(TransformGeometry.Apply(line, tr, dst), "line -> line");
            Exact(dst.A, V(0.2 + 0.125, 0.3 + -0.0625), "translated A");
            Exact(dst.B, V(0.6 + 0.125, 0.5 + -0.0625), "translated B");
            Check.That(dst.Id == new EntityId(99) && dst.Style.Equals(StrokeStyle.Default), "dst Id and Style untouched");
            Check.That(line.A == V(0.2, 0.3) && line.B == V(0.6, 0.5), "src untouched");
            Check.That(TransformGeometry.Apply(line, sc, dst), "scale line");
            Near(dst.A, V(0.3, 0.35), "scaled A");
            Near(dst.B, V(0.5, 0.45), "scaled B");
            Check.That(TransformGeometry.Apply(line, sc, line), "src == dst");
            Check.That(line.A == dst.A && line.B == dst.B, "in place gives the same result as into a separate dst (reads before writes)");
            Check.That(line.Id == new EntityId(7) && line.Style.Equals(RedThickDotted), "in place: Id and Style kept");

            // Circle: centre transformed, radius times Scale; translation keeps the radius bit-identical.
            var circle = new CircleShape(new EntityId(8), V(0.5, 0.5), 0.2, BlueThin);
            var dc = new CircleShape(new EntityId(98), V(0, 0), 1, StrokeStyle.Default);
            Check.That(TransformGeometry.Apply(circle, tr, dc), "circle translate");
            Exact(dc.Center, V(0.5 + 0.125, 0.5 + -0.0625), "translated centre");
            Check.That(dc.Radius == 0.2, $"translation keeps the radius, got {dc.Radius:R}");
            Check.That(TransformGeometry.Apply(circle, sc, dc), "circle scale");
            Near(dc.Center, V(0.45, 0.45), "scaled centre");
            Check.Near(dc.Radius, 0.1, "radius times Scale", 1e-12);
            Check.That(dc.Id == new EntityId(98) && dc.Style.Equals(StrokeStyle.Default), "circle dst Id/Style untouched");
            Check.That(TransformGeometry.Apply(circle, sc, circle), "circle in place");
            Check.That(circle.Center == dc.Center && circle.Radius == dc.Radius, "circle in place == separate dst");

            // Polygon: centre transformed, radius scaled, Sides and RotationRad copied; vertices scale geometrically.
            var poly = new PolygonShape(new EntityId(9), V(0.5, 0.5), 6, 0.2, 0.3, StrokeStyle.Default);
            var before = new Vec2[6];
            for (int k = 0; k < 6; k++) before[k] = SnapIndex.PolygonVertex(poly, k);
            var dp = new PolygonShape(new EntityId(97), V(0, 0), 3, 1, 1.0, BlueThin);
            Check.That(TransformGeometry.Apply(poly, sc, dp), "polygon scale");
            Near(dp.Center, V(0.45, 0.45), "polygon centre");
            Check.Near(dp.Radius, 0.1, "polygon radius", 1e-12);
            Check.That(dp.Sides == 6 && dp.RotationRad == 0.3, $"Sides and RotationRad copied, got {dp.Sides} / {dp.RotationRad:R}");
            Check.That(dp.Id == new EntityId(97) && dp.Style.Equals(BlueThin), "polygon dst Id/Style untouched");
            for (int k = 0; k < 6; k++)
                Near(SnapIndex.PolygonVertex(dp, k), About(before[k], V(0.4, 0.4), 0.5), $"vertex {k} scaled about the pivot");
            Check.That(TransformGeometry.Apply(poly, tr, poly), "polygon translate in place");
            Exact(poly.Center, V(0.5 + 0.125, 0.5 + -0.0625), "polygon translated centre");
            Check.That(poly.Radius == 0.2 && poly.Sides == 6 && poly.RotationRad == 0.3, "translation: radius, sides, rotation unchanged");

            // Mismatch and null: false, nothing written.
            var untouched = new CircleShape(new EntityId(5), V(0.1, 0.1), 0.05, StrokeStyle.Default);
            Check.That(!TransformGeometry.Apply(line, tr, untouched), "line into circle: false");
            Check.That(untouched.Center == V(0.1, 0.1) && untouched.Radius == 0.05, "mismatch writes nothing");
            Check.That(!TransformGeometry.Apply(null, tr, dst), "null src: false");
            Check.That(!TransformGeometry.Apply(line, tr, null), "null dst: false");
        }

        public static void V_SizeOfShapes()
        {
            Check.Near(TransformGeometry.Size(new LineShape(new EntityId(1), V(0.2, 0.3), V(0.5, 0.7), StrokeStyle.Default)), 0.5, "line length", 1e-12);
            Check.That(TransformGeometry.Size(new CircleShape(new EntityId(2), V(0.5, 0.5), 0.2, StrokeStyle.Default)) == 0.2, "circle radius");
            Check.That(TransformGeometry.Size(new PolygonShape(new EntityId(3), V(0.5, 0.5), 5, 0.15, 0.1, StrokeStyle.Default)) == 0.15, "polygon radius");
            Check.That(TransformGeometry.Size(null) == 0, "null: 0");
            Check.That(TransformGeometry.Size(new CircleShape(new EntityId(4), V(0.5, 0.5), -0.1, StrokeStyle.Default)) == 0, "negative radius: 0");
            Check.That(TransformGeometry.Size(new CircleShape(new EntityId(4), V(0.5, 0.5), 0, StrokeStyle.Default)) == 0, "zero radius: 0");
            Check.That(TransformGeometry.Size(new PolygonShape(new EntityId(5), V(0.5, 0.5), 2, 0.1, 0, StrokeStyle.Default)) == 0, "2-sided polygon: 0");
            Check.That(TransformGeometry.Size(new LineShape(new EntityId(6), V(double.NaN, 0.3), V(0.5, 0.7), StrokeStyle.Default)) == 0, "NaN line: 0");
        }

        public static void V_TrySupportPoint()
        {
            Vec2 pt; GrabKind kind; int vtx; double along;
            var line = new LineShape(new EntityId(1), V(0.2, 0.5), V(0.6, 0.5), StrokeStyle.Default);
            Vec2 pv = V(0.4, 0.5);
            Check.That(TransformGeometry.TrySupportPoint(line, pv, V(1, 0), out pt, out kind, out vtx, out along), "line +x");
            Check.That(pt == line.B && kind == GrabKind.LineB, $"+x: B / LineB, got {P(pt)} {kind}");
            Check.Near(along, 0.2, "+x along", 1e-12);
            Check.That(TransformGeometry.TrySupportPoint(line, pv, V(-1, 0), out pt, out kind, out vtx, out along), "line -x");
            Check.That(pt == line.A && kind == GrabKind.LineA, $"-x: A / LineA, got {P(pt)} {kind}");
            Check.Near(along, 0.2, "-x along", 1e-12);
            Check.That(TransformGeometry.TrySupportPoint(line, pv, V(0, 1), out pt, out kind, out vtx, out along), "line +y");
            Check.That(pt == line.A && kind == GrabKind.LineA, $"perpendicular is a tie: A wins, got {P(pt)} {kind}");

            var circle = new CircleShape(new EntityId(2), V(0.5, 0.5), 0.1, StrokeStyle.Default);
            Check.That(TransformGeometry.TrySupportPoint(circle, V(0.3, 0.5), V(0, 1), out pt, out kind, out vtx, out along), "circle");
            Check.That(kind == GrabKind.Ring, $"circle support is a Ring point, got {kind}");
            Near(pt, V(0.5, 0.6), "c + r*dir");
            Check.Near(along, 0.1, "along = (point - pivot) . dir", 1e-12);
            Vec2 d = Unit(V(1, 1));
            Check.That(TransformGeometry.TrySupportPoint(circle, V(0.3, 0.5), d, out pt, out kind, out vtx, out along), "circle diag");
            Near(pt, V(0.5 + 0.1 * d.X, 0.5 + 0.1 * d.Y), "diagonal ring point");
            Check.Near(along, (pt.X - 0.3) * d.X + (pt.Y - 0.5) * d.Y, "diagonal along", 1e-12);

            var square = new PolygonShape(new EntityId(3), V(0.5, 0.5), 4, 0.1, 0, StrokeStyle.Default);
            Check.That(TransformGeometry.TrySupportPoint(square, V(0.5, 0.5), V(1, 0), out pt, out kind, out vtx, out along), "square +x");
            Check.That(kind == GrabKind.Vertex && vtx == 0, $"square +x: vertex 0, got {kind} {vtx}");
            Near(pt, SnapIndex.PolygonVertex(square, 0), "vertex 0", 1e-15);
            Check.Near(along, 0.1, "square along", 1e-12);
            Check.That(TransformGeometry.TrySupportPoint(square, V(0.5, 0.5), V(0, -1), out pt, out kind, out vtx, out along), "square -y");
            Check.That(kind == GrabKind.Vertex && vtx == 3, $"square -y: vertex 3, got {kind} {vtx}");
            Near(pt, V(0.5, 0.4), "vertex 3");

            Check.That(!TransformGeometry.TrySupportPoint(null, pv, V(1, 0), out _, out _, out _, out _), "null: false");
            Check.That(!TransformGeometry.TrySupportPoint(new CircleShape(new EntityId(4), V(0.5, 0.5), 0, StrokeStyle.Default), pv, V(1, 0),
                out _, out _, out _, out _), "zero radius circle: false");
            Check.That(!TransformGeometry.TrySupportPoint(new PolygonShape(new EntityId(5), V(0.5, 0.5), 2, 0.1, 0, StrokeStyle.Default), pv,
                V(1, 0), out _, out _, out _, out _), "2-sided polygon: false");
        }

        public static void V_TryNearestKeyPoint()
        {
            Vec2 pt; GrabKind kind; int vtx; double dist;
            var line = new LineShape(new EntityId(1), V(0.2, 0.5), V(0.6, 0.5), StrokeStyle.Default);
            Check.That(TransformGeometry.TryNearestKeyPoint(line, V(0.25, 0.52), out pt, out kind, out vtx, out dist), "line near A");
            Check.That(pt == line.A && kind == GrabKind.LineA, $"A / LineA, got {P(pt)} {kind}");
            Check.Near(dist, Vec2.Distance(V(0.25, 0.52), line.A), "distance to A", 1e-12);
            Check.That(TransformGeometry.TryNearestKeyPoint(line, V(0.58, 0.47), out pt, out kind, out vtx, out dist), "line near B");
            Check.That(pt == line.B && kind == GrabKind.LineB, $"B / LineB, got {P(pt)} {kind}");

            var circle = new CircleShape(new EntityId(2), V(0.5, 0.5), 0.2, StrokeStyle.Default);
            Check.That(TransformGeometry.TryNearestKeyPoint(circle, V(0.69, 0.5), out pt, out kind, out vtx, out dist), "circle");
            Check.That(pt == circle.Center && kind == GrabKind.Center, $"the ring is not a key point: centre, got {P(pt)} {kind}");
            Check.Near(dist, 0.19, "distance to the centre", 1e-12);

            var hex = new PolygonShape(new EntityId(3), V(0.5, 0.5), 6, 0.2, 0, StrokeStyle.Default);
            Vec2 v2 = SnapIndex.PolygonVertex(hex, 2);
            Check.That(TransformGeometry.TryNearestKeyPoint(hex, V(v2.X + 0.01, v2.Y), out pt, out kind, out vtx, out dist), "hex vertex");
            Check.That(kind == GrabKind.Vertex && vtx == 2, $"vertex 2, got {kind} {vtx}");
            Near(pt, v2, "vertex 2 position", 1e-15);
            Check.Near(dist, 0.01, "distance to vertex 2", 1e-12);
            Check.That(TransformGeometry.TryNearestKeyPoint(hex, V(0.51, 0.51), out pt, out kind, out vtx, out dist), "hex centre");
            Check.That(kind == GrabKind.Center && pt == hex.Center, $"polygon centre is a key point, got {kind}");

            Check.That(!TransformGeometry.TryNearestKeyPoint(null, V(0.5, 0.5), out _, out _, out _, out _), "null: false");
        }

        public static void V_TranslationLimitsNeverWorse()
        {
            TransformGeometry.TranslationLimits(new BoardRect(0.2, 0.3, 0.6, 0.7), out double lx, out double hx, out double ly, out double hy);
            Check.Near(lx, -0.2, "inside: loX", 1e-15); Check.Near(hx, 0.4, "inside: hiX", 1e-15);
            Check.Near(ly, -0.3, "inside: loY", 1e-15); Check.Near(hy, 0.3, "inside: hiY", 1e-15);

            TransformGeometry.TranslationLimits(new BoardRect(-0.1, 0.2, 0.5, 1.2), out lx, out hx, out ly, out hy);
            Check.That(lx == 0, $"sticking out left: cannot go further left (loX 0), got {lx:R}");
            Check.Near(hx, 0.5, "sticking out left: may move right", 1e-15);
            Check.Near(ly, -0.2, "sticking out top: may move down", 1e-15);
            Check.That(hy == 0, $"sticking out top: cannot go further up (hiY 0), got {hy:R}");

            TransformGeometry.TranslationLimits(new BoardRect(-0.1, -0.1, 1.1, 1.1), out lx, out hx, out ly, out hy);
            Check.That(lx == 0 && hx == 0 && ly == 0 && hy == 0, "bigger than the board: frozen, never forced in");

            TransformGeometry.TranslationLimits(new BoardRect(0, 0, 1, 1), out lx, out hx, out ly, out hy);
            Check.That(lx == 0 && hx == 0 && ly == 0 && hy == 0, "exactly the board: no room");
        }

        public static void V_MaxScaleInBoard()
        {
            Check.Near(TransformGeometry.MaxScaleInBoard(new BoardRect(0.4, 0.4, 0.6, 0.6), V(0.5, 0.5)), 5, "centred 0.2 box: 5", 1e-12);
            Check.Near(TransformGeometry.MaxScaleInBoard(new BoardRect(0.1, 0.4, 0.3, 0.5), V(0.2, 0.45)), 2, "left side limits: 0.2 / 0.1", 1e-12);
            Check.That(TransformGeometry.MaxScaleInBoard(new BoardRect(-0.1, 0.4, 0.5, 0.6), V(0.2, 0.5)) == 1,
                "already sticking out: never worse, never below 1");
            Check.Near(TransformGeometry.MaxScaleInBoard(new BoardRect(0.2, 0.5, 0.6, 0.5), V(0.4, 0.5)), 2,
                "zero-height bounds: the Y sides are skipped", 1e-12);
            Check.That(TransformGeometry.MaxScaleInBoard(new BoardRect(0.5, 0.5, 0.5, 0.5), V(0.5, 0.5)) >= 1, "a point: >= 1, no NaN");
        }

        public static void V_SnapIndexExcludedShapesNeedMarkDirty()
        {
            var b = new Board();
            var layer = new Layer(b.NewId(), "L");
            b.Layers.Add(layer);
            b.ActiveLayerId = layer.Id;
            var line = AddLine(b, layer, 0.11, 0.13, 0.37, 0.41);
            var circle = AddCircle(b, layer, 0.24, 0.27, 0.07);   // crosses the line twice
            var hex = AddPoly(b, layer, 0.71, 0.63, 6, 0.06, 0.1);
            var cross = AddLine(b, layer, 0.61, 0.63, 0.83, 0.65);   // crosses the hexagon
            var sp = new SnapPoint(b.NewId(), V(0.613, 0.887));
            layer.SnapPoints.Add(sp);

            var idx = new SnapIndex();
            Check.That(idx.ExcludedShapes == null, "ExcludedShapes is null by default");
            Check.That(idx.Sync(b), "first Sync rebuilds");
            var c = idx.Candidates;
            int all = c.Count;
            Vec2 div0 = CircleDivisions.Point(circle.Center, circle.Radius, 0);
            Check.That(Has(c, line.A) && Has(c, line.B) && Has(c, circle.Center) && Has(c, div0), "precondition: line ends, centre, division");
            int crossings = 0;
            foreach (var k in c) if (k.Kind == SnapKind.Intersection) crossings++;
            Check.That(crossings >= 4, $"precondition: line x circle and line x hexagon crossings, got {crossings}");

            idx.ExcludedShapes = new HashSet<EntityId> { line.Id, circle.Id };
            Check.That(!idx.Sync(b), "changing the set alone does not rebuild: the caller must MarkDirty");
            Check.That(Has(idx.Candidates, line.A), "still the old list");
            idx.MarkDirty();
            Check.That(idx.Sync(b), "MarkDirty then Sync rebuilds");
            c = idx.Candidates;
            Check.That(!Has(c, line.A) && !Has(c, line.B), "excluded line: no endpoints");
            Check.That(!Has(c, circle.Center), "excluded circle: no centre");
            for (int k = 0; k < 8; k++)
                Check.That(!Has(c, CircleDivisions.Point(circle.Center, circle.Radius, k)), $"excluded circle: no division {k}");
            int left = 0;
            foreach (var k in c)
                if (k.Kind == SnapKind.Intersection)
                {
                    left++;
                    Check.That(Vec2.Distance(k.Position, circle.Center) > 0.06, $"no crossing involving the excluded shapes, got {P(k.Position)}");
                }
            Check.That(left >= 1, "the hexagon x line crossings (both not excluded) stay");
            for (int k = 0; k < 6; k++)
                Check.That(Has(c, SnapIndex.PolygonVertex(hex, k)), $"hexagon vertex {k} stays");
            Check.That(Has(c, hex.Center) && Has(c, cross.A) && Has(c, cross.B), "other shapes' points stay");
            Check.That(Has(c, sp.Position), "user snap points are never excluded");
            Check.That(Has(c, V(0.5, 0.5)) && Has(c, V(0, 0)), "grid points stay");

            idx.ExcludedShapes = null;
            idx.MarkDirty();
            idx.Sync(b);
            Check.That(idx.Candidates.Count == all && Has(idx.Candidates, line.A) && Has(idx.Candidates, div0), "null again: everything back");

            idx.ExcludedShapes = new HashSet<EntityId>();
            idx.MarkDirty();
            idx.Sync(b);
            Check.That(idx.Candidates.Count == all, "an empty set excludes nothing");
        }

        // ================= SetTool =================

        public static void V_TransformToolsRefusedWithEmptySelection()
        {
            var s = new EditorSession();
            AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.4, 0.4);
            var r = new Rec(s);
            foreach (var start in new[] { ToolId.Line, ToolId.SelectMove, ToolId.Circle })
            {
                s.SetTool(start);
                foreach (var t in Transforms)
                {
                    r.Reset();
                    s.SetTool(t);
                    Check.That(s.ActiveTool == start, $"{start} -> {t} with nothing selected: refused, got {s.ActiveTool}");
                    Check.That(r.Toasts.Count == 1 && r.Toasts[0] == SelectFirst, $"{t}: '{SelectFirst}', got {r.T}");
                    Check.That(r.States == 0 && r.Boards.Count == 0, $"{t}: no events when refused");
                }
            }
        }

        public static void V_TransformToolsAcceptedHintOneStateChangedKeepSelection()
        {
            var s = new EditorSession();
            var line = AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.4, 0.4);
            var circle = AddCircle(s.Board, s.ActiveLayer, 0.6, 0.6, 0.1);
            s.SetTool(ToolId.SelectMove);
            Select(s, line, circle);
            var r = new Rec(s);
            var steps = new[] { (ToolId.Move, MoveHint), (ToolId.Scale, ScaleHint), (ToolId.Duplicate, DupHint), (ToolId.Move, MoveHint) };
            foreach (var (t, hint) in steps)
            {
                r.Reset();
                s.SetTool(t);
                Check.That(s.ActiveTool == t, $"{t} accepted");
                Check.That(r.States == 1, $"{t}: exactly one StateChanged, got {r.States}");
                Check.That(r.Toasts.Count == 1 && r.Toasts[0] == hint, $"{t}: hint toast, got {r.T}");
                Check.That(s.ToolHint == hint, $"{t}: ToolHint '{s.ToolHint}'");
                Selected(s, $"{t} keeps the selection", line, circle);
                Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, $"{t}: selection adornments are not in Preview");
                Check.That(r.Boards.Count == 0, $"{t}: no BoardChanged");
            }
            r.Reset();
            s.SetTool(ToolId.SelectMove);
            Check.That(s.ActiveTool == ToolId.SelectMove && r.States == 1, "back to Select: one StateChanged");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == SelectHint, $"Select hint, got {r.T}");
            Selected(s, "Select keeps the selection", line, circle);

            s.SetTool(ToolId.Scale);
            r.Reset();
            s.SetTool(ToolId.Line);
            Check.That(s.ActiveTool == ToolId.Line && s.SelectionCount == 0, "Line clears the selection");
            Check.That(r.States == 1, $"Scale -> Line: one StateChanged, got {r.States}");
        }

        public static void V_OtherToolsStillComingSoonInTransformTools()
        {
            var s = new EditorSession();
            var line = AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.4, 0.4);
            foreach (var mode in Transforms)
            {
                Enter(s, mode, line);
                var r = new Rec(s);
                foreach (var t in new[] { ToolId.Rotate, ToolId.Stamps, ToolId.Erase, ToolId.Snap })
                {
                    r.Reset();
                    s.SetTool(t);
                    Check.That(s.ActiveTool == mode, $"{mode} -> {t}: refused");
                    Check.That(r.Toasts.Count == 1 && r.Toasts[0] == EditorSession.ComingSoonToast, $"{t}: Coming soon, got {r.T}");
                    Check.That(r.States == 0, $"{t}: no StateChanged");
                    Selected(s, $"{t} keeps the selection", line);
                }
            }
        }

        // ================= Move =================

        public static void V_MoveTranslatesExactlyWithSnapOff()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Board b = s.Board;
            Layer layer = s.ActiveLayer;
            var L = AddLine(b, layer, 0.1875, 0.1875, 0.375, 0.3125, RedThickDotted);
            var C = AddCircle(b, layer, 0.3125, 0.625, 0.0625, BlueThin);
            var U = AddLine(b, layer, 0.75, 0.75, 0.875, 0.875);
            var uBefore = U.Clone();
            int nextId = b.NextId;
            Enter(s, ToolId.Move, L, C);
            Vec2 pivot = Pivot(s);
            Exact(pivot, V(0.28125, 0.4375), "precondition: pivot (dyadic)");
            var r = new Rec(s);

            Vec2 f0 = V(0.625, 0.375), mid = V(0.6875, 0.40625), f = V(0.75, 0.4375);
            Down(s, f0);
            Check.That(s.GestureActive, "Down anywhere on the board opens the gesture (not on the shapes)");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "nothing drawn at Down");
            MoveTo(s, mid);
            var lines = Ghosts<LineShape>(s);
            var circles = Ghosts<CircleShape>(s);
            Check.That(lines.Count == 1 && circles.Count == 1 && s.Preview.Count == 2, $"one ghost per selected shape, got {s.Preview.Count}");
            Check.That(!ReferenceEquals(lines[0], L) && !ReferenceEquals(circles[0], C), "ghosts are not the model shapes");
            Vec2 tm = V(0.0625, 0.03125);
            Exact(lines[0].A, Plus(L.A, tm), "line ghost A");
            Exact(lines[0].B, Plus(L.B, tm), "line ghost B");
            Exact(circles[0].Center, Plus(C.Center, tm), "circle ghost centre");
            Check.That(circles[0].Radius == C.Radius, "circle ghost radius");
            Check.That(lines[0].Style.Equals(RedThickDotted) && circles[0].Style.Equals(BlueThin), "ghosts keep each shape's width and dash");
            Exact(s.Cursor ?? V(-1, -1), Plus(pivot, tm), "far grab: the cursor is the pivot plus t");
            Check.That(L.A == V(0.1875, 0.1875) && C.Center == V(0.3125, 0.625), "the model is untouched while dragging");
            Check.That(r.Boards.Count == 0 && r.Toasts.Count == 0, "no BoardChanged or toast during Moves");

            MoveTo(s, V(0.7, 0.42));
            MoveTo(s, f);
            Up(s, f);
            Vec2 t = V(0.125, 0.0625);
            Exact(L.A, Plus(V(0.1875, 0.1875), t), "L.A moved by exactly t");
            Exact(L.B, Plus(V(0.375, 0.3125), t), "L.B moved by exactly t");
            Exact(C.Center, Plus(V(0.3125, 0.625), t), "C centre moved by exactly t");
            Check.That(C.Radius == 0.0625, "radius unchanged");
            Check.That(L.Style.Equals(RedThickDotted) && C.Style.Equals(BlueThin), "styles unchanged");
            Check.That(ReferenceEquals(layer.Shapes[0], L) && ReferenceEquals(layer.Shapes[1], C), "mutated in place, same order");
            Same(U, uBefore, "unselected shape");
            Check.That(r.Boards.Count == 1 && r.Boards[0] == BoardChangeKind.Shapes, $"exactly one BoardChanged(Shapes), got {r.Boards.Count}");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue && !s.GestureActive, "Preview empty and Cursor null after Up");
            Check.That(ShapeCount(s) == 3 && b.NextId == nextId, "no shape added, no id used");
            Selected(s, "the selection is kept", L, C);
            Check.That(s.ActiveTool == ToolId.Move, "still Move");

            Drag(s, V(0.875, 0.125), V(0.8125, 0.125));
            Exact(L.A, V(0.25, 0.25), "a second Move drag continues from the new geometry");
            Check.That(r.Boards.Count == 2, "one BoardChanged per commit");
        }

        public static void V_MovePolygonExactAndIgnoresHVLock()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            s.SetOrthoLock(true);
            var g = AddPoly(s.Board, s.ActiveLayer, 0.5, 0.5, 6, 0.125, 0.3, RedThickDotted);
            Enter(s, ToolId.Move, g);
            Drag(s, V(0.875, 0.875), V(0.75, 0.9375));   // diagonal: the H/V lock would make it horizontal
            Exact(g.Center, V(0.375, 0.5625), "polygon centre moved by the diagonal t (H/V lock ignored)");
            Check.That(g.Radius == 0.125 && g.Sides == 6 && g.RotationRad == 0.3 && g.Style.Equals(RedThickDotted),
                "radius, sides, rotation and style unchanged");
        }

        public static void V_MoveReferencePointNearestKeyPointOrPivot()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var L = AddLine(s.Board, s.ActiveLayer, 0.25, 0.25, 0.5, 0.375);
            var C = AddCircle(s.Board, s.ActiveLayer, 0.75, 0.625, 0.0625);
            Enter(s, ToolId.Move, L, C);
            Vec2 pivot = Pivot(s);
            Vec2 t = V(0.0625, -0.0625);
            var before = Snapshot(new Shape[] { L, C });

            void Probe(Vec2 f0, Vec2 expectRef, string what)
            {
                Down(s, f0);
                MoveTo(s, Plus(f0, V(t.X * 0.5, t.Y * 0.5)));
                MoveTo(s, Plus(f0, t));
                Check.That(s.Cursor.HasValue, $"{what}: cursor shown while dragging");
                Near(s.Cursor.Value, Plus(expectRef, t), what);
                Cancel(s);
                SameAll(new Shape[] { L, C }, before, what + " (cancelled)");
            }

            Probe(V(0.53125, 0.390625), L.B, "grab 0.035 from the line end: that end");
            Probe(V(0.71875, 0.65625), C.Center, "grab near the circle centre: the centre");
            Probe(V(0.125, 0.875), pivot, "grab far from every key point: the pivot");
            Probe(V(0.5 + 0.09, 0.375), pivot, "0.09 from the end (> MoveGrabRadius 0.08): the pivot");
            Probe(V(0.5 + 0.07, 0.375), L.B, "0.07 from the end (< 0.08): the end");
            Probe(V(0.25 - 0.01, 0.25 - 0.02), L.A, "near the other end: A");
            Check.That(GrabRadius == 0.08, "spec constant");
        }

        public static void V_MoveNothingUntilSlopAndOffBoardKeepsLastState()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var L = AddLine(s.Board, s.ActiveLayer, 0.25, 0.25, 0.5, 0.375);
            Enter(s, ToolId.Move, L);
            var r = new Rec(s);

            s.Pointer(Ev(PointerPhase.Down, V(0.5, 0.5), on: false));
            Check.That(!s.GestureActive, "an off-board Down opens no gesture");
            s.Pointer(Ev(PointerPhase.Up, V(0.5, 0.5), on: false));

            Down(s, V(0.75, 0.75));
            MoveTo(s, V(0.754, 0.75));   // 0.004 < slop (10/24 * 0.02 = 0.0083)
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "inside the tap slop: nothing drawn");
            MoveTo(s, V(0.8125, 0.8125));
            Check.That(s.Preview.Count == 1, "past the slop: the ghost");
            Exact(Ghosts<LineShape>(s)[0].A, Plus(L.A, V(0.0625, 0.0625)), "ghost at t");
            s.Pointer(Ev(PointerPhase.Move, V(0.99, 0.2), on: false));
            s.Pointer(Ev(PointerPhase.Move, V(double.NaN, 0.2)));
            Exact(Ghosts<LineShape>(s)[0].A, Plus(L.A, V(0.0625, 0.0625)), "off-board / NaN Moves keep the last state");
            s.Pointer(Ev(PointerPhase.Up, V(0.99, 0.2), on: false));
            Exact(L.A, V(0.3125, 0.3125), "an off-board Up commits the last on-board translation");
            Check.That(r.Boards.Count == 1, "one BoardChanged");
        }

        public static void V_MoveSnapLandsExactlyAndPins()
        {
            var s = new EditorSession();   // snapping on, 2x2 grid: candidates on the 0.25 lattice
            Board b = s.Board;
            Layer layer = s.ActiveLayer;
            var L = AddLine(b, layer, 0.21, 0.23, 0.33, 0.29);
            var Q = AddCircle(b, layer, 0.61, 0.83, 0.05);
            var G = AddPoly(b, layer, 0.37, 0.63, 6, 0.06, 0.2);
            Enter(s, ToolId.Move, L);
            var r = new Rec(s);

            // Line end B grabbed, released 0.005 from the grid corner (0.5, 0.5).
            Vec2 f0 = V(0.335, 0.285);
            Vec2 f = V(f0.X + (0.504 - 0.33), f0.Y + (0.497 - 0.29));
            DragNoUp(s, f0, f, 8);
            Near(s.Cursor ?? V(-1, -1), V(0.5, 0.5), "the reference snapped: cursor on the corner");
            var ring = HoverRing(s);
            Check.That(ring != null, "hover ring shown when the reference snapped");
            Near(ring.Center, V(0.5, 0.5), "hover ring at the snapped reference");
            Check.That(r.Boards.Count == 0 && L.B == V(0.33, 0.29), "model untouched before Up");
            Up(s, f);
            Exact(L.B, V(0.5, 0.5), "the grabbed end lands EXACTLY on the corner");
            Near(L.A, V(0.21 + (0.5 - 0.33), 0.23 + (0.5 - 0.29)), "the other end moves by the same t");
            Check.That(r.Boards.Count == 1 && r.Boards[0] == BoardChangeKind.Shapes, "one BoardChanged(Shapes)");
            Check.That(HasCand(s, L.A) && HasCand(s, L.B), "snap points rebuilt: the moved ends are candidates");
            Check.That(!HasCandNear(s, V(0.21, 0.23), 1e-9) && !HasCandNear(s, V(0.33, 0.29), 1e-9), "the old ends are gone");

            // Circle centre grabbed, lands exactly on the tile centre (0.75, 0.75); radius bit-identical.
            Select(s, Q);
            f0 = V(0.615, 0.83);
            f = V(f0.X + (0.756 - 0.61), f0.Y + (0.745 - 0.83));
            Drag(s, f0, f, 8);
            Exact(Q.Center, V(0.75, 0.75), "circle centre lands exactly");
            Check.That(Q.Radius == 0.05, "radius bit-identical");

            // Polygon centre grabbed, lands exactly on (0.25, 0.75).
            Select(s, G);
            f0 = V(0.375, 0.63);
            f = V(f0.X + (0.254 - 0.37), f0.Y + (0.747 - 0.63));
            Drag(s, f0, f, 8);
            Exact(G.Center, V(0.25, 0.75), "polygon centre lands exactly");
            Check.That(G.Radius == 0.06 && G.Sides == 6 && G.RotationRad == 0.2, "polygon radius, sides, rotation unchanged");

            // Polygon vertex grabbed: the vertex is computed, so it lands within rounding of the snap point.
            Vec2 v1 = SnapIndex.PolygonVertex(G, 1);
            f0 = V(v1.X + 0.003, v1.Y + 0.003);
            f = V(f0.X + (0.504 - v1.X), f0.Y + (0.747 - v1.Y));
            Drag(s, f0, f, 8);
            Near(SnapIndex.PolygonVertex(G, 1), V(0.5, 0.75), "grabbed vertex lands on the snap point");
            Check.That(r.Boards.Count == 4, $"one BoardChanged per commit, got {r.Boards.Count}");
        }

        public static void V_MoveExcludesOwnSnapPointsWhileDragging()
        {
            var s = new EditorSession();
            Board b = s.Board;
            Layer layer = s.ActiveLayer;
            var L = AddLine(b, layer, 0.11, 0.13, 0.37, 0.41);
            var M = AddLine(b, layer, 0.11, 0.41, 0.37, 0.13);
            var U = AddCircle(b, layer, 0.7, 0.7, 0.1);
            Vec2 x = default;
            bool found = false;
            foreach (var c in s.SnapCandidates)
                if (c.Kind == SnapKind.Intersection && Vec2.Distance(c.Position, V(0.24, 0.27)) < 1e-6) { x = c.Position; found = true; }
            Check.That(found, "precondition: L x M crossing is a candidate");
            Check.That(HasCand(s, L.A) && HasCand(s, L.B) && HasCand(s, M.A) && HasCand(s, U.Center), "precondition: points");
            Enter(s, ToolId.Move, L);
            var r = new Rec(s);

            Down(s, V(0.8, 0.2));
            MoveTo(s, V(0.803, 0.2));
            Check.That(HasCand(s, L.A) && r.States == 0, "inside the slop: no exclusion yet");
            MoveTo(s, V(0.85, 0.25));
            Check.That(!HasCand(s, L.A) && !HasCand(s, L.B), "first drag frame: the selection's own ends are not candidates");
            Check.That(!HasCandNear(s, x, 1e-9), "the crossing with the moving shape disappears");
            Check.That(HasCand(s, M.A) && HasCand(s, M.B) && HasCand(s, U.Center), "other shapes' points stay");
            Check.That(HasCand(s, V(0.5, 0.5)), "grid points stay");
            Check.That(r.States == 1, $"one StateChanged when exclusion starts (dots rebuilt once), got {r.States}");
            MoveTo(s, V(0.86, 0.26));
            MoveTo(s, V(0.87, 0.27));
            Check.That(r.States == 1, $"never per Move, got {r.States}");
            Vec2 oldA = L.A;
            Up(s, V(0.87, 0.27));
            Check.That(r.Boards.Count == 1, "committed");
            Check.That(HasCand(s, L.A) && HasCand(s, L.B), "after Up: the moved shape's ends are candidates again");
            Check.That(!HasCandNear(s, oldA, 1e-9), "the old position is not");

            // Cancel restores the exclusion too, with one StateChanged for it.
            r.Reset();
            Down(s, V(0.8, 0.2));
            MoveTo(s, V(0.85, 0.15));
            Check.That(!HasCand(s, L.A), "excluded again");
            Cancel(s);
            Check.That(HasCand(s, L.A) && HasCand(s, L.B), "after Cancel: own points back");
            Check.That(r.States == 2, $"one StateChanged at the start, one at Cancel, got {r.States}");
            Check.That(r.Boards.Count == 0, "Cancel: no BoardChanged");

            // An identity drag (back to the Down point) commits nothing, but the exclusion is cleared.
            r.Reset();
            Down(s, V(0.8, 0.2));
            MoveTo(s, V(0.85, 0.15));
            MoveTo(s, V(0.8, 0.2));
            Up(s, V(0.8, 0.2));
            Check.That(r.Boards.Count == 0, "identity: no BoardChanged");
            Check.That(HasCand(s, L.A), "identity: own points back");
            Check.That(r.States == 2, $"identity: StateChanged at start and end, got {r.States}");

            // Scale excludes as well; Duplicate never does.
            s.SetTool(ToolId.Scale);
            Down(s, V(0.8, 0.2));
            MoveTo(s, V(0.85, 0.15));
            Check.That(!HasCand(s, L.A) && !HasCand(s, L.B), "Scale excludes the selection's points");
            Cancel(s);
            Check.That(HasCand(s, L.A), "Scale Cancel: back");
            s.SetTool(ToolId.Duplicate);
            r.Reset();
            Down(s, V(0.8, 0.2));
            MoveTo(s, V(0.85, 0.15));
            Check.That(HasCand(s, L.A) && HasCand(s, L.B), "Duplicate keeps the originals' points");
            Check.That(r.States == 0, $"Duplicate: no exclusion StateChanged, got {r.States}");
            Cancel(s);
        }

        public static void V_MoveClampAllFourEdges()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var L = AddLine(s.Board, s.ActiveLayer, 0.375, 0.375, 0.625, 0.5);
            Enter(s, ToolId.Move, L);

            void Bounds(string what)
            {
                Check.That(s.TryGetSelectionBounds(out BoardRect bb), "bounds");
                Check.That(bb.MinX >= -1e-12 && bb.MaxX <= 1 + 1e-12 && bb.MinY >= -1e-12 && bb.MaxY <= 1 + 1e-12,
                    $"{what}: selection stays inside the board, got {bb}");
            }

            Drag(s, V(0.5, 0.125), V(0.99, 0.125));
            Check.Near(L.B.X, 1, "far right: MaxX stops at 1", 1e-12);
            Check.Near(L.A.X, 0.75, "the other end follows", 1e-12);
            Check.That(L.A.Y == 0.375 && L.B.Y == 0.5, "no vertical drift");
            Bounds("right");
            Drag(s, V(0.9, 0.125), V(0.01, 0.125));
            Check.Near(L.A.X, 0, "far left: MinX stops at 0", 1e-12);
            Bounds("left");
            Drag(s, V(0.875, 0.25), V(0.875, 0.99));
            Check.Near(L.B.Y, 1, "far up: MaxY stops at 1", 1e-12);
            Bounds("up");
            Drag(s, V(0.875, 0.9), V(0.875, 0.01));
            Check.Near(L.A.Y, 0, "far down: MinY stops at 0", 1e-12);
            Bounds("down");
            Check.Near(Vec2.Distance(L.A, L.B), Vec2.Distance(V(0.375, 0.375), V(0.625, 0.5)), "shape unchanged", 1e-12);
        }

        public static void V_MoveClampNeverWorseForShapesStickingOut()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var big = AddCircle(s.Board, s.ActiveLayer, 0.125, 0.5, 0.25);   // x from -0.125: already out on the left
            Enter(s, ToolId.Move, big);
            Drag(s, V(0.5, 0.5), V(0.25, 0.625));
            Exact(big.Center, V(0.125, 0.625), "cannot be pushed further out (x frozen), y moves freely");
            Drag(s, V(0.5, 0.5), V(0.5625, 0.5));
            Exact(big.Center, V(0.1875, 0.625), "moving inward is allowed and it is not forced in");
            Drag(s, V(0.5, 0.25), V(0.5, 0.99));
            Check.Near(big.Center.Y + big.Radius, 1, "the top still clamps at 1", 1e-12);
            Check.That(big.Radius == 0.25, "radius unchanged");
        }

        public static void V_MoveClampClearsTheSnapHit()
        {
            var s = new EditorSession();
            var L = AddLine(s.Board, s.ActiveLayer, 0.4, 0.4, 0.6, 0.45);
            var O = AddLine(s.Board, s.ActiveLayer, 0.85, 0.4, 0.85, 0.2);   // its end (0.85, 0.4) would need t.X 0.45 > hiX 0.4
            Enter(s, ToolId.Move, L);
            Vec2 f0 = V(0.405, 0.4);
            Vec2 f = V(f0.X + (0.849 - 0.4), f0.Y + 0.001);
            DragNoUp(s, f0, f, 8);
            Check.That(HoverRing(s) == null, "the clamp changed t: no hover ring");
            Near(s.Cursor ?? V(-1, -1), V(0.8, 0.4), "cursor at the clamped reference");
            Up(s, f);
            Near(L.A, V(0.8, 0.4), "clamped, not pinned to the out-of-reach snap point");
            Check.That(L.A != O.A, "not landed on the snap point");
            Check.Near(L.B.X, 1, "MaxX at the board edge", 1e-12);
        }

        // ================= Scale =================

        public static void V_ScaleAboutPivotGrowsAndShrinks()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Board b = s.Board;
            Layer layer = s.ActiveLayer;
            var C = AddCircle(b, layer, 0.5, 0.5, 0.125, RedThickDotted);
            Enter(s, ToolId.Scale, C);
            Exact(Pivot(s), V(0.5, 0.5), "pivot is the bounds centre");
            var r = new Rec(s);

            Drag(s, V(0.75, 0.5), V(0.875, 0.5));
            Check.Near(C.Radius, 0.1875, "away from the pivot: grows (s 1.5)", 1e-12);
            Near(C.Center, V(0.5, 0.5), "the pivot stays fixed");
            Check.That(C.Style.Equals(RedThickDotted), "stroke width and dash unchanged");
            Check.That(r.Boards.Count == 1 && r.Boards[0] == BoardChangeKind.Shapes, "one BoardChanged(Shapes)");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "Scaled to 150%", $"toast, got {r.T}");

            r.Reset();
            Drag(s, V(0.75, 0.5), V(0.625, 0.5));
            Check.Near(C.Radius, 0.09375, "toward the pivot: shrinks (s 0.5)", 1e-12);
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "Scaled to 50%", $"toast, got {r.T}");

            // Two lines: both ends of each scale about the union's centre.
            var L1 = AddLine(b, layer, 0.25, 0.25, 0.5, 0.375, RedThickDotted);
            var L2 = AddLine(b, layer, 0.5, 0.625, 0.75, 0.5);
            Select(s, L1, L2);
            Check.That(s.ActiveTool == ToolId.Scale, "still Scale");
            Vec2 p = Pivot(s);
            Exact(p, V(0.5, 0.4375), "two-line pivot");
            var before = Snapshot(new Shape[] { L1, L2 });
            Vec2 f0 = V(0.75, 0.4375), f = V(0.8125, 0.4375);
            double k = FingerScale(p, f0, f);
            Check.Near(k, 1.25, "precondition: s 1.25", 1e-12);
            Drag(s, f0, f);
            Near(L1.A, About(((LineShape)before[0]).A, p, k), "L1.A about the pivot");
            Near(L1.B, About(((LineShape)before[0]).B, p, k), "L1.B");
            Near(L2.A, About(((LineShape)before[1]).A, p, k), "L2.A");
            Near(L2.B, About(((LineShape)before[1]).B, p, k), "L2.B");
            Check.That(L1.Style.Equals(RedThickDotted), "line width unchanged");

            // Odd polygon: its bounds centre is not its own centre, so the centre moves about the pivot.
            var tri = AddPoly(b, layer, 0.5, 0.5, 3, 0.125, Math.PI / 2);
            Select(s, tri);
            p = Pivot(s);
            Check.Near(p.Y, 0.53125, "precondition: triangle pivot above its centre", 1e-12);
            f0 = V(p.X + 0.25, p.Y);
            f = V(p.X + 0.375, p.Y);
            Drag(s, f0, f);
            Near(tri.Center, About(V(0.5, 0.5), p, 1.5), "triangle centre scaled about the pivot (not about itself)");
            Check.Near(tri.Radius, 0.1875, "triangle radius times 1.5", 1e-12);
            Check.That(tri.Sides == 3 && tri.RotationRad == Math.PI / 2, "rotation unchanged");
        }

        public static void V_ScaleStartsAtOneAndIdentityCommitsNothing()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var C = AddCircle(s.Board, s.ActiveLayer, 0.5, 0.5, 0.125);
            Enter(s, ToolId.Scale, C);
            var r = new Rec(s);
            Down(s, V(0.75, 0.5));
            MoveTo(s, V(0.8, 0.5));
            var g = Ghosts<CircleShape>(s);
            Check.That(g.Count == 1, "one ghost");
            Check.Near(g[0].Radius, 0.125 * 1.2, "ghost radius at s 1.2", 1e-12);
            Check.That(C.Radius == 0.125, "model untouched while dragging");
            Check.That(s.Cursor.HasValue, "the handle is shown");
            Near(s.Cursor.Value, V(0.5 + 0.125 * 1.2, 0.5), "cursor = the handle (ring point toward the finger), scaled");
            MoveTo(s, V(0.85, 0.55));
            MoveTo(s, V(0.75, 0.5));
            Up(s, V(0.75, 0.5));
            Check.That(C.Radius == 0.125 && C.Center == V(0.5, 0.5), "back at the Down point: s exactly 1, nothing changed");
            Check.That(r.Boards.Count == 0 && r.Toasts.Count == 0, $"identity commits nothing and toasts nothing, got {r.T}");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue && !s.GestureActive, "cleared");
        }

        public static void V_ScaleGrabNearPivotUsesScaleGrabMin()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var C = AddCircle(s.Board, s.ActiveLayer, 0.5, 0.5, 0.125);
            Enter(s, ToolId.Scale, C);
            Drag(s, V(0.5, 0.5), V(0.5, 0.525));   // Down exactly on the pivot: r0 0, Rg 0.05
            Check.Near(C.Radius, 0.1875, "from the pivot: s = 1 + 0.025 / 0.05", 1e-12);
            Near(C.Center, V(0.5, 0.5), "centre fixed");
            Drag(s, V(0.51, 0.5), V(0.535, 0.5));   // r0 0.01 < 0.05
            Check.Near(C.Radius, 0.28125, "near the pivot: s = 1 + 0.025 / 0.05 (not 0.035 / 0.01)", 1e-12);
            Check.That(GrabMin == 0.05, "spec constant");
        }

        public static void V_ScaleMinClampKeepsEveryShapeCommittable()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var shortLine = AddLine(s.Board, s.ActiveLayer, 0.44, 0.5, 0.46, 0.5);
            var big = AddCircle(s.Board, s.ActiveLayer, 0.5, 0.5, 0.2);
            Enter(s, ToolId.Scale, shortLine, big);
            var r = new Rec(s);
            Drag(s, V(0.5, 0.75), V(0.5, 0.5), 10);   // finger onto the pivot: s_raw -> 0, floored, then clamped to sMin 0.5
            Check.Near(TransformGeometry.Size(shortLine), EditorSession.MinStrokeLength, "the short line stops at MinStrokeLength", 1e-12);
            Check.Near(big.Radius, 0.1, "the circle shrinks by the same factor", 1e-12);
            Near(shortLine.A, V(0.47, 0.5), "about the pivot");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "Scaled to 50%", $"toast, got {r.T}");
            Check.That(!double.IsNaN(shortLine.A.X) && !double.IsNaN(big.Radius), "no NaN");
        }

        public static void V_ScaleMaxClampKeepsSelectionInsideBoard()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var L = AddLine(s.Board, s.ActiveLayer, 0.4, 0.45, 0.6, 0.55);
            Enter(s, ToolId.Scale, L);
            var r = new Rec(s);
            Drag(s, V(0.55, 0.5), V(0.95, 0.95), 10);   // s_raw about 12.7; sMax 5
            Near(L.A, V(0.0, 0.25), "left end at the board edge");
            Near(L.B, V(1.0, 0.75), "right end at the board edge");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "Scaled to 500%", $"toast, got {r.T}");
        }

        public static void V_ScaleBoardEdgeCircleNeverWorse()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var C = AddCircle(s.Board, s.ActiveLayer, 0.875, 0.5, 0.25);   // reaches x 1.125
            Enter(s, ToolId.Scale, C);
            var r = new Rec(s);
            Drag(s, V(0.875, 0.75), V(0.875, 0.875));
            Check.That(C.Radius == 0.25 && C.Center == V(0.875, 0.5), "already past the edge: cannot grow (sMax 1)");
            Check.That(r.Boards.Count == 0 && r.Toasts.Count == 0, "clamped to 1: identity, nothing committed");
            Drag(s, V(0.875, 0.75), V(0.875, 0.625));
            Check.Near(C.Radius, 0.125, "shrinking is allowed", 1e-12);
            Check.That(r.Boards.Count == 1 && r.Toasts.Count == 1 && r.Toasts[0] == "Scaled to 50%", $"toast, got {r.T}");

            // The same circle in Move: it may move inward, not further out.
            s.SetTool(ToolId.Move);
            var C2 = AddCircle(s.Board, s.ActiveLayer, 0.875, 0.25, 0.25);
            Select(s, C2);
            Drag(s, V(0.5, 0.9), V(0.75, 0.9));
            Check.That(C2.Center == V(0.875, 0.25), "Move: not pushed further out");
        }

        public static void V_ScaleSnapMakesCircleRadiusExact()
        {
            var s = new EditorSession();
            Board b = s.Board;
            Layer layer = s.ActiveLayer;
            const double r = 0.1171875;   // 15/128: the bounds and so the pivot are exact, the radius is not a power of two
            var C = AddCircle(b, layer, 0.5, 0.5, r);
            Vec2 target = V(0.7213, 0.6187);
            layer.SnapPoints.Add(new SnapPoint(b.NewId(), target));
            Enter(s, ToolId.Scale, C);
            Vec2 p = V(0.5, 0.5);
            Exact(Pivot(s), p, "precondition: exact pivot == centre");
            var rec = new Rec(s);

            Vec2 d = Unit(Minus(target, p));
            double D = Vec2.Distance(target, p);
            Vec2 f0 = V(p.X + 0.2 * d.X, p.Y + 0.2 * d.Y);
            double want = (D + 0.003) / r;   // s_raw that puts the raw handle 0.003 past the dot
            Vec2 f = V(p.X + 0.2 * want * d.X, p.Y + 0.2 * want * d.Y);
            DragNoUp(s, f0, f, 10);
            Check.That(HoverRing(s) != null, "the handle snapped: hover ring");
            Near(s.Cursor ?? V(-1, -1), target, "handle on the dot (it lies on the handle's ray)");
            Near(HoverRing(s).Center, s.Cursor ?? V(-1, -1), "ring at the cursor");
            Up(s, f);
            Check.That(C.Radius == Vec2.Distance(target, p), $"radius EXACTLY the pivot-dot distance {D:R}, got {C.Radius:R}");
            Near(C.Center, p, "centre fixed");
            Check.That(rec.Toasts.Count == 1 && rec.Toasts[0] == EditorSession.ScaledToast(D / r), $"toast, got {rec.T}");
        }

        public static void V_ScaleSnapPinsLineEndpointExactly()
        {
            var s = new EditorSession();
            var L = AddLine(s.Board, s.ActiveLayer, 0.3, 0.5, 0.7, 0.5);
            Enter(s, ToolId.Scale, L);
            Vec2 p = Pivot(s);
            Vec2 f0 = V(0.68, 0.52);   // toward B: B is the handle
            Vec2 f = V(p.X + 1.23 * (f0.X - p.X), p.Y + 1.23 * (f0.Y - p.Y));
            DragNoUp(s, f0, f, 8);
            Check.That(HoverRing(s) != null, "the handle snapped to (0.75, 0.5)");
            Check.That(!HasCand(s, V(0.7, 0.5)) && !HasCand(s, V(0.3, 0.5)), "own ends excluded while scaling");
            Up(s, f);
            Exact(L.B, V(0.75, 0.5), "handle end pinned EXACTLY to the snap point");
            Near(L.A, About(V(0.3, 0.5), p, Vec2.Distance(V(0.75, 0.5), p) / Vec2.Distance(V(0.7, 0.5), p)), "the other end scaled by s");
        }

        public static void V_ScaleClampClearsTheSnapHit()
        {
            var s = new EditorSession();
            var C = AddCircle(s.Board, s.ActiveLayer, 0.5, 0.5, 0.125);
            AddLine(s.Board, s.ActiveLayer, 0.889, 0.889, 0.95, 0.75);   // its end needs s 4.4 > sMax 4
            Enter(s, ToolId.Scale, C);
            Vec2 p = V(0.5, 0.5), d = Unit(V(1, 1));
            Vec2 f0 = V(p.X + 0.06 * d.X, p.Y + 0.06 * d.Y);
            double want = (Vec2.Distance(V(0.889, 0.889), p) + 0.002) / 0.125;
            Vec2 f = V(p.X + 0.06 * want * d.X, p.Y + 0.06 * want * d.Y);
            DragNoUp(s, f0, f, 10);
            Check.That(HoverRing(s) == null, "clamped to sMax: not a hit, no hover ring");
            Near(s.Cursor ?? V(-1, -1), V(p.X + 0.5 * d.X, p.Y + 0.5 * d.Y), "cursor at the clamped handle");
            Up(s, f);
            Check.Near(C.Radius, 0.5, "radius clamped to the board (sMax 4)", 1e-12);
        }

        public static void V_ScaleDegenerateSelectionStaysFinite()
        {
            var s = new EditorSession();
            var dot = AddLine(s.Board, s.ActiveLayer, 0.4, 0.4, 0.4, 0.4);   // zero length: handle == pivot, rho 0
            Enter(s, ToolId.Scale, dot);
            Drag(s, V(0.6, 0.6), V(0.8, 0.8));
            Check.That(dot.A.IsFinite && dot.B.IsFinite, "no NaN or infinity from a zero-size selection");
            Near(dot.A, V(0.4, 0.4), "a point scaled about itself stays put");
        }

        // ================= Duplicate =================

        public static void V_DuplicateTwoLayersCopiesBecomeSelectionInMove()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var L1 = AddLine(b, bottom, 0.125, 0.125, 0.25, 0.1875, RedThickDotted);
            var C1 = AddCircle(b, bottom, 0.375, 0.25, 0.0625, BlueThin);
            var L2 = AddLine(b, top, 0.125, 0.5, 0.25, 0.625);
            var U = AddLine(b, top, 0.75, 0.75, 0.875, 0.875);
            var G = AddPoly(b, top, 0.375, 0.5, 5, 0.0625, 0.25, BlueThin);
            var s = new EditorSession(b);
            s.SetSnapEnabled(false);
            var originals = new Shape[] { L1, C1, L2, U, G };
            var before = Snapshot(originals);
            var oldIds = new HashSet<EntityId>();
            foreach (var sh in originals) oldIds.Add(sh.Id);
            int nextId = b.NextId;
            Enter(s, ToolId.Duplicate, L1, C1, L2, G);
            var r = new Rec(s);

            Vec2 f0 = V(0.625, 0.25), f = V(0.875, 0.375), t = V(0.25, 0.125);
            DragNoUp(s, f0, f);
            Check.That(s.Preview.Count == 4, $"four ghosts, got {s.Preview.Count}");
            Check.That(bottom.Shapes.Count == 2 && top.Shapes.Count == 3 && r.Boards.Count == 0, "nothing added before Up");
            Check.That(r.States == 0, "Duplicate sets no exclusion: no StateChanged while dragging");
            Up(s, f);

            Check.That(bottom.Shapes.Count == 4 && top.Shapes.Count == 5, $"copies on their originals' layers: {bottom.Shapes.Count}/{top.Shapes.Count}");
            Check.That(ReferenceEquals(bottom.Shapes[0], L1) && ReferenceEquals(bottom.Shapes[1], C1), "bottom originals first");
            Check.That(ReferenceEquals(top.Shapes[0], L2) && ReferenceEquals(top.Shapes[1], U) && ReferenceEquals(top.Shapes[2], G), "top originals first");
            var l1c = bottom.Shapes[2] as LineShape;
            var c1c = bottom.Shapes[3] as CircleShape;
            var l2c = top.Shapes[3] as LineShape;
            var gc = top.Shapes[4] as PolygonShape;
            Check.That(l1c != null && c1c != null && l2c != null && gc != null, "copies appended in drawing order, same kinds");
            Exact(l1c.A, Plus(L1.A, t), "L1 copy A"); Exact(l1c.B, Plus(L1.B, t), "L1 copy B");
            Exact(c1c.Center, Plus(C1.Center, t), "C1 copy centre");
            Check.That(c1c.Radius == C1.Radius, "C1 copy radius");
            Exact(l2c.A, Plus(L2.A, t), "L2 copy A"); Exact(l2c.B, Plus(L2.B, t), "L2 copy B");
            Exact(gc.Center, Plus(G.Center, t), "polygon copy centre");
            Check.That(gc.Radius == G.Radius && gc.Sides == 5 && gc.RotationRad == 0.25, "polygon copy radius, sides, rotation");
            Check.That(l1c.Style.Equals(RedThickDotted) && c1c.Style.Equals(BlueThin) && gc.Style.Equals(BlueThin), "copies keep styles");
            SameAll(originals, before, "originals unchanged");

            var copies = new Shape[] { l1c, c1c, l2c, gc };
            var newIds = new HashSet<EntityId>();
            foreach (var c in copies)
            {
                Check.That(!c.Id.IsNone && !oldIds.Contains(c.Id), $"copy id {c.Id} is new");
                newIds.Add(c.Id);
            }
            Check.That(newIds.Count == 4, "copy ids unique");
            Check.That(b.NextId == nextId + 4, $"four ids used, NextId {nextId} -> {b.NextId}");

            Check.That(r.Boards.Count == 1 && r.Boards[0] == BoardChangeKind.Shapes, $"one BoardChanged(Shapes), got {r.Boards.Count}");
            Selected(s, "the copies are the selection", copies);
            Check.That(s.ActiveTool == ToolId.Move && s.ToolHint == MoveHint, $"switched to Move, got {s.ActiveTool}");
            Check.That(r.States == 1, $"selection + tool change folded into one StateChanged, got {r.States}");
            Check.That(r.Log.IndexOf("B") < r.Log.IndexOf("S"), $"BoardChanged before StateChanged, log {string.Join(",", r.Log)}");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "4 copies placed: drag to move them", $"toast, got {r.T}");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "Preview cleared");

            // Next drag (Move) moves only the copies.
            var copiesBefore = Snapshot(copies);
            Drag(s, V(0.125, 0.875), V(0.0625, 0.875));
            SameAll(originals, before, "originals untouched by the follow-up Move");
            Exact(l1c.A, Plus(((LineShape)copiesBefore[0]).A, V(-0.0625, 0)), "the copy moved");

            // Duplicate again: copies of the copies, on the same layers.
            s.SetTool(ToolId.Duplicate);
            Check.That(s.ActiveTool == ToolId.Duplicate, "Duplicate again with the copies selected");
            Drag(s, V(0.875, 0.875), V(0.875, 0.8125));
            Check.That(bottom.Shapes.Count == 6 && top.Shapes.Count == 7, "a second set of copies");
            Check.That(s.SelectionCount == 4 && !s.Selection.Ids.Contains(l1c.Id), "the newest copies are selected");
        }

        public static void V_DuplicateSingleToastAndCopySnapPoints()
        {
            var s = new EditorSession();
            var L = AddLine(s.Board, s.ActiveLayer, 0.21, 0.23, 0.33, 0.29);
            Enter(s, ToolId.Duplicate, L);
            var r = new Rec(s);
            Drag(s, V(0.7, 0.1), V(0.8, 0.2));   // R0 = pivot; the reference passes no candidate closely
            Check.That(s.ActiveLayer.Shapes.Count == 2, "one copy");
            var copy = (LineShape)s.ActiveLayer.Shapes[1];
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "Copy placed: drag to move it", $"single-copy toast, got {r.T}");
            Check.That(HasCand(s, copy.A) && HasCand(s, copy.B), "copies get snap points after Up");
            Check.That(HasCand(s, L.A) && HasCand(s, L.B), "originals keep theirs");
        }

        public static void V_DuplicateZeroDragTapAndCancelCreateNothing()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var L = AddLine(s.Board, s.ActiveLayer, 0.25, 0.25, 0.5, 0.375);
            Enter(s, ToolId.Duplicate, L);
            var r = new Rec(s);
            int nextId = s.Board.NextId;

            Down(s, V(0.625, 0.25));
            MoveTo(s, V(0.7, 0.3));
            MoveTo(s, V(0.625, 0.25));
            Up(s, V(0.625, 0.25));
            Check.That(ShapeCount(s) == 1 && s.Board.NextId == nextId, "a drag back to the start (t = 0) creates nothing");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == NoCopy, $"'{NoCopy}', got {r.T}");
            Check.That(r.Boards.Count == 0 && s.ActiveTool == ToolId.Duplicate, "no BoardChanged, still Duplicate");
            Selected(s, "selection kept", L);

            r.Reset();
            Down(s, V(0.625, 0.25));
            MoveTo(s, V(0.7, 0.3));
            Cancel(s);
            Check.That(ShapeCount(s) == 1 && r.Boards.Count == 0 && r.Toasts.Count == 0, "Cancel creates nothing, no toast");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue && !s.GestureActive, "ghosts gone");
            Up(s, V(0.7, 0.3));
            Check.That(ShapeCount(s) == 1, "a stray Up after Cancel creates nothing");

            r.Reset();
            Tap(s, V(0.875, 0.875));
            Check.That(ShapeCount(s) == 1 && r.Toasts.Count == 0 && r.States == 0 && r.Boards.Count == 0,
                $"a tap on empty board does nothing (no NoCopy toast), got {r.T}");

            Down(s, V(0.625, 0.25));
            MoveTo(s, V(0.7, 0.3));
            Down(s, V(0.875, 0.875));   // the Up was lost
            Up(s, V(0.875, 0.875));
            Check.That(ShapeCount(s) == 1, "a lost Up never commits the copy");

            Down(s, V(0.625, 0.25));
            MoveTo(s, V(0.7, 0.3));
            s.SetTool(ToolId.Move);
            Up(s, V(0.7, 0.3));
            Check.That(ShapeCount(s) == 1 && s.Board.NextId == nextId, "a tool switch mid-drag creates nothing");
        }

        public static void V_DuplicateCopySnapsOntoOriginalChaining()
        {
            var s = new EditorSession();
            var L = AddLine(s.Board, s.ActiveLayer, 0.21, 0.23, 0.33, 0.29);
            Enter(s, ToolId.Duplicate, L);
            Vec2 f0 = V(0.214, 0.23);   // grab A
            Vec2 f = V(f0.X + (0.334 - 0.21), f0.Y + (0.287 - 0.23));   // A's copy released 0.005 from B
            DragNoUp(s, f0, f, 8);
            Check.That(HasCand(s, L.A) && HasCand(s, L.B), "no self-exclusion in Duplicate");
            Near(s.Cursor ?? V(-1, -1), L.B, "the copy's reference snapped to the original's end");
            Check.That(HoverRing(s) != null, "hover ring");
            Up(s, f);
            Check.That(s.ActiveLayer.Shapes.Count == 2, "one copy");
            var copy = (LineShape)s.ActiveLayer.Shapes[1];
            Exact(copy.A, L.B, "chained: the copy's start is EXACTLY the original's end");
            Near(copy.B, V(0.33 + 0.12, 0.29 + 0.06), "the copy's other end");
            Check.That(L.A == V(0.21, 0.23) && L.B == V(0.33, 0.29), "original unchanged");
        }

        public static void V_DuplicateClampedInsideBoard()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var L = AddLine(s.Board, s.ActiveLayer, 0.5, 0.5, 0.75, 0.625);
            Enter(s, ToolId.Duplicate, L);
            Drag(s, V(0.5, 0.125), V(0.99, 0.125));
            var copy = (LineShape)s.ActiveLayer.Shapes[1];
            Check.Near(copy.B.X, 1, "the copy stops at the board edge", 1e-12);
            Check.That(L.B == V(0.75, 0.625), "original unchanged");
        }

        // ================= Cancel =================

        enum Abort { CancelEvent, LostUp, ToolLine, ToolSelect, ToolOtherTransform, ClearBoard, LoadBoard }

        public static void V_CancelRevertsExactlyForEveryToolAndPath()
        {
            foreach (var tool in Transforms)
                foreach (Abort how in Enum.GetValues(typeof(Abort)))
                {
                    string what = $"{tool}/{how}";
                    var s = new EditorSession();   // snapping on: exclusion must be cleared too
                    Board b = s.Board;
                    Layer layer = s.ActiveLayer;
                    var L = AddLine(b, layer, 0.21, 0.23, 0.33, 0.29);
                    var C = AddCircle(b, layer, 0.61, 0.37, 0.07);
                    var G = AddPoly(b, layer, 0.43, 0.71, 5, 0.06, 0.1);
                    var O = AddLine(b, layer, 0.8, 0.8, 0.9, 0.85);
                    var all = new Shape[] { L, C, G, O };
                    var before = Snapshot(all);
                    int nextId = b.NextId;
                    Enter(s, tool, L, C, G);
                    var r = new Rec(s);
                    Down(s, V(0.85, 0.15));
                    MoveTo(s, V(0.9, 0.3));
                    MoveTo(s, V(0.95, 0.35));
                    Check.That(s.Preview.Count >= 3, $"{what}: precondition ghosts drawn");
                    Check.That(tool == ToolId.Duplicate || !HasCand(s, L.A), $"{what}: precondition exclusion on");

                    switch (how)
                    {
                        case Abort.CancelEvent: Cancel(s); break;
                        case Abort.LostUp: Down(s, V(0.95, 0.05)); break;
                        case Abort.ToolLine: s.SetTool(ToolId.Line); break;
                        case Abort.ToolSelect: s.SetTool(ToolId.SelectMove); break;
                        case Abort.ToolOtherTransform: s.SetTool(tool == ToolId.Move ? ToolId.Scale : ToolId.Move); break;
                        case Abort.ClearBoard: s.ClearBoard(); break;
                        case Abort.LoadBoard: s.LoadBoard(GoldenBoards.StarOnFullBoard()); break;
                    }

                    SameAll(all, before, what);
                    Check.That(!r.AnyShapesChange, $"{what}: no BoardChanged(Shapes)");
                    Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, $"{what}: Preview empty, Cursor null");
                    Check.That(r.Toasts.TrueForAll(m => !m.StartsWith("Scaled") && m != NoCopy && !m.Contains("placed")),
                        $"{what}: no commit toast, got {r.T}");
                    if (how == Abort.ClearBoard || how == Abort.LoadBoard)
                    {
                        Check.That(layer.Shapes.Count == 4 && b.NextId == nextId, $"{what}: the old board was not edited");
                        Check.That(s.SelectionCount == 0 && s.ActiveTool == ToolId.SelectMove,
                            $"{what}: selection gone, tool falls back to Select, got {s.ActiveTool}");
                        continue;
                    }
                    Check.That(layer.Shapes.Count == 4 && b.NextId == nextId, $"{what}: nothing added");
                    Check.That(HasCand(s, L.A) && HasCand(s, L.B) && HasCand(s, C.Center), $"{what}: exclusion cleared");
                    if (how == Abort.LostUp)
                    {
                        Check.That(s.GestureActive, $"{what}: the new press is a gesture of its own");
                        Up(s, V(0.95, 0.05));   // a tap on empty board: nothing
                    }
                    else
                    {
                        Check.That(!s.GestureActive, $"{what}: gesture closed");
                        Up(s, V(0.95, 0.35));   // stray Up
                    }
                    SameAll(all, before, what + " after the stray Up");
                    Check.That(layer.Shapes.Count == 4 && !r.AnyShapesChange, $"{what}: stray Up commits nothing");
                }
        }

        public static void V_ToolSwitchMidDragRaisesExactlyOneStateChanged()
        {
            foreach (var (src, dst) in new[] { (ToolId.Move, ToolId.Scale), (ToolId.Scale, ToolId.SelectMove), (ToolId.Duplicate, ToolId.Move),
                                               (ToolId.Move, ToolId.Line) })
            {
                var s = new EditorSession();
                var L = AddLine(s.Board, s.ActiveLayer, 0.21, 0.23, 0.33, 0.29);
                Enter(s, src, L);
                Down(s, V(0.8, 0.2));
                MoveTo(s, V(0.85, 0.25));
                var r = new Rec(s);
                s.SetTool(dst);
                Check.That(s.ActiveTool == dst, $"{src} -> {dst} mid-drag accepted");
                Check.That(r.States == 1, $"{src} -> {dst} mid-drag: exactly one StateChanged, got {r.States}");
                Check.That(!s.GestureActive && s.Preview.Count == 0, "gesture dropped");
                Check.That(L.A == V(0.21, 0.23), "nothing moved");
                Check.That(HasCand(s, L.A), "exclusion cleared");
            }
        }

        // ================= taps =================

        public static void V_TapInTransformToolPicksAnotherShapeEmptyTapKeeps()
        {
            foreach (var tool in Transforms)
            {
                var s = new EditorSession();
                s.SetSnapEnabled(false);
                var L = AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.4, 0.2);
                var M = AddLine(s.Board, s.ActiveLayer, 0.2, 0.6, 0.4, 0.6);
                var before = Snapshot(new Shape[] { L, M });
                Enter(s, tool, L);
                var r = new Rec(s);

                Tap(s, V(0.3, 0.61));
                Selected(s, $"{tool}: a tap on another stroke switches the target", M);
                Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "1 shape selected", $"{tool}: toast, got {r.T}");
                Check.That(r.States == 1 && r.Boards.Count == 0, $"{tool}: one StateChanged, no BoardChanged");
                Check.That(s.ActiveTool == tool, $"{tool}: tool kept");

                r.Reset();
                Tap(s, V(0.8, 0.9));
                Selected(s, $"{tool}: a tap on empty board keeps the selection", M);
                Check.That(r.States == 0 && r.Boards.Count == 0 && r.Toasts.Count == 0 && r.Previews == 0,
                    $"{tool}: empty tap raises no events (S {r.States}, P {r.Previews}, T {r.T})");

                r.Reset();
                Tap(s, V(0.3, 0.6));
                Selected(s, $"{tool}: tap on the selected stroke", M);
                Check.That(r.States == 0 && r.Toasts.Count == 1, $"{tool}: same selection: toast, no StateChanged");

                Select(s, L, M);
                Tap(s, V(0.3, 0.205));
                Selected(s, $"{tool}: a tap replaces a multi-selection", L);
                SameAll(new Shape[] { L, M }, before, $"{tool}: taps never edit geometry");
                Check.That(ShapeCount(s) == 2, $"{tool}: taps never copy");
            }
        }

        // ================= NormalizeTool =================

        public static void V_EmptySelectionFallsBackToSelect()
        {
            // Hiding the only selected shape's layer in Move.
            var b = TwoLayerBoard(out var bottom, out var top);
            var L = AddLine(b, bottom, 0.2, 0.2, 0.4, 0.4);
            var C = AddCircle(b, top, 0.7, 0.7, 0.1);
            var s = new EditorSession(b);
            Enter(s, ToolId.Move, L);
            var r = new Rec(s);
            s.SetLayerVisible(bottom.Id, false);
            Check.That(s.SelectionCount == 0, "pruned");
            Check.That(s.ActiveTool == ToolId.SelectMove && s.ToolHint == SelectHint, $"Move falls back to Select, got {s.ActiveTool}");
            Check.That(r.States == 1, $"one StateChanged (from the prune), got {r.States}");
            Check.That(r.Toasts.Count == 0, $"no toast from the fallback, got {r.T}");

            // Partial prune: still something selected, tool kept.
            s.SetLayerVisible(bottom.Id, true);
            Enter(s, ToolId.Move, L, C);
            s.SetLayerVisible(bottom.Id, false);
            Selected(s, "partial prune", C);
            Check.That(s.ActiveTool == ToolId.Move, "a non-empty selection keeps Move");
            s.SetLayerVisible(bottom.Id, true);

            // ClearSelection in Scale.
            Enter(s, ToolId.Scale, C);
            r.Reset();
            s.ClearSelection();
            Check.That(s.ActiveTool == ToolId.SelectMove && r.States == 1, $"Scale + ClearSelection -> Select, one StateChanged ({r.States})");

            // SetSelection(empty) in Duplicate.
            Enter(s, ToolId.Duplicate, C);
            s.SetSelection(new EntityId[0]);
            Check.That(s.ActiveTool == ToolId.SelectMove, "Duplicate + SetSelection(empty) -> Select");

            // DeleteBottomLayer with the selection on it.
            Enter(s, ToolId.Move, L);
            s.DeleteBottomLayer();
            Check.That(s.SelectionCount == 0 && s.ActiveTool == ToolId.SelectMove, "deleted layer -> Select");

            // ClearBoard and LoadBoard.
            Enter(s, ToolId.Scale, C);
            s.ClearBoard();
            Check.That(s.ActiveTool == ToolId.SelectMove, "ClearBoard -> Select");
            s.LoadBoard(GoldenBoards.StarOnFullBoard());
            Enter(s, ToolId.Duplicate, s.Board.Layers[0].Shapes[0]);
            s.LoadBoard(GoldenBoards.Empty());
            Check.That(s.ActiveTool == ToolId.SelectMove, "LoadBoard -> Select");

            // Not a transform tool: untouched.
            s.SetTool(ToolId.Line);
            s.ClearSelection();
            s.SetSnapEnabled(false);
            Check.That(s.ActiveTool == ToolId.Line, "Line stays Line on StateChanged");
        }

        public static void V_LayerHiddenMidDragIsNeverEdited()
        {
            foreach (var tool in Transforms)
            {
                var b = TwoLayerBoard(out var bottom, out var top);
                var L = AddLine(b, bottom, 0.2, 0.2, 0.4, 0.4);
                var s = new EditorSession(b);
                s.SetSnapEnabled(false);
                Enter(s, tool, L);
                var before = L.Clone();
                Down(s, V(0.8, 0.2));
                MoveTo(s, V(0.85, 0.25));
                s.SetLayerVisible(bottom.Id, false);   // engine-level: a real second touch would cancel first
                MoveTo(s, V(0.9, 0.3));
                Up(s, V(0.9, 0.3));
                Same(L, before, $"{tool}: a shape whose layer was hidden mid-drag is not edited");
                Check.That(bottom.Shapes.Count == 1 && top.Shapes.Count == 0, $"{tool}: no copies anywhere");
                Check.That(s.SelectionCount == 0, $"{tool}: pruned");
            }
        }

        public static void V_LayerHiddenMidDragThenUpDoesNotStrandTheTool()
        {
            foreach (var tool in Transforms)
            {
                var b = TwoLayerBoard(out var bottom, out var top);
                var L = AddLine(b, bottom, 0.2, 0.2, 0.4, 0.4);
                var s = new EditorSession(b);
                s.SetSnapEnabled(false);
                Enter(s, tool, L);
                Down(s, V(0.8, 0.2));
                MoveTo(s, V(0.85, 0.25));
                s.SetLayerVisible(bottom.Id, false);
                Up(s, V(0.9, 0.3));
                Check.That(!(s.SelectionCount == 0 && (s.ActiveTool == ToolId.Move || s.ActiveTool == ToolId.Scale || s.ActiveTool == ToolId.Duplicate)),
                    $"{tool}: selection emptied mid-drag; after Up the tool must not stay {s.ActiveTool} with nothing selected");

                // The Cancel path (what a real second finger does).
                s.SetLayerVisible(bottom.Id, true);
                Enter(s, tool, L);
                Down(s, V(0.8, 0.2));
                MoveTo(s, V(0.85, 0.25));
                s.SetLayerVisible(bottom.Id, false);
                Cancel(s);
                Check.That(s.ActiveTool == ToolId.SelectMove, $"{tool}: hidden mid-drag then Cancel -> Select, got {s.ActiveTool}");
            }
        }

        // ================= hidden layers, routing =================

        public static void V_HiddenLayerShapesNeverTransformedEvenIfPlanted()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var H = AddLine(b, bottom, 0.2, 0.2, 0.4, 0.3);
            var Vs = AddLine(b, top, 0.5, 0.5, 0.625, 0.625);
            var s = new EditorSession(b);
            s.SetSnapEnabled(false);
            s.SetLayerVisible(bottom.Id, false);
            var hBefore = H.Clone();
            foreach (var tool in Transforms)
            {
                if (s.ActiveTool != tool) Enter(s, tool, Vs);
                s.Selection.Ids.Add(H.Id);   // planted directly: no event, no prune
                Down(s, V(0.875, 0.125));
                MoveTo(s, V(0.8125, 0.1875));
                Check.That(s.Preview.Count == 1, $"{tool}: a ghost for the visible shape only, got {s.Preview.Count}");
                MoveTo(s, V(0.8, 0.2));
                Up(s, V(0.8, 0.2));
                Same(H, hBefore, $"{tool}: hidden-layer shape untouched");
                Check.That(bottom.Shapes.Count == 1, $"{tool}: never copied");
            }
            Check.That(top.Shapes.Count == 2, "Duplicate copied the visible shape only");
        }

        public static void V_HiddenActiveLayerDoesNotBlockTransforms()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var L = AddLine(b, bottom, 0.25, 0.25, 0.5, 0.375);
            var s = new EditorSession(b);
            s.SetSnapEnabled(false);
            s.SetLayerVisible(top.Id, false);   // the active layer
            Enter(s, ToolId.Move, L);
            var r = new Rec(s);
            Drag(s, V(0.75, 0.75), V(0.8125, 0.75));
            Exact(L.A, V(0.3125, 0.25), "moved although the active layer is hidden");
            Check.That(!r.Toasts.Contains(EditorSession.HiddenLayerToast), "no hidden-layer toast");
            s.SetTool(ToolId.Duplicate);
            Drag(s, V(0.75, 0.75), V(0.8125, 0.75));
            Check.That(bottom.Shapes.Count == 2 && top.Shapes.Count == 0, "the copy goes to the original's (visible) layer");
        }

        public static void V_LineAndCircleStillWorkAfterTransforms()
        {
            var s = new EditorSession();
            var L = AddLine(s.Board, s.ActiveLayer, 0.21, 0.23, 0.33, 0.29);
            Enter(s, ToolId.Move, L);
            Drag(s, V(0.7, 0.1), V(0.73, 0.13));
            Vec2 end = L.B;
            s.SetTool(ToolId.Line);
            Check.That(s.SelectionCount == 0, "Line clears the selection");
            Drag(s, V(0.6, 0.9), V(end.X + 0.004, end.Y - 0.003));
            var drawn = s.ActiveLayer.Shapes[s.ActiveLayer.Shapes.Count - 1] as LineShape;
            Check.That(drawn != null && drawn != L, "a line was drawn");
            Exact(drawn.B, end, "the new line snaps exactly to the moved line's end");
            s.SetTool(ToolId.Circle);
            Drag(s, V(0.5, 0.5), V(0.6, 0.5));
            Check.That(s.ActiveLayer.Shapes[s.ActiveLayer.Shapes.Count - 1] is CircleShape, "Circle still commits");
        }

        // ================= screen metrics =================

        sealed class AffineMetric : ISnapMetric
        {
            public double Scale = 1000, Tol = 24;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                screenPx = new Vec2(board.X * Scale + 11, board.Y * Scale - 7);
                return true;
            }
        }

        /// <summary>w = 1 + 0.8 y, screen = 1000 (x, y) / w: anisotropic and varying with depth (as in SnapToolChecks).</summary>
        sealed class PerspectiveMetric : ISnapMetric
        {
            public double Tol = 24;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                double w = 1 + 0.8 * board.Y;
                screenPx = new Vec2(1000 * board.X / w, 1000 * board.Y / w);
                return true;
            }
        }

        public static void V_MoveSnapAndTapWithAffineMetric()
        {
            var s = new EditorSession { SnapMetric = new AffineMetric { Scale = 1000, Tol = 24 } };
            var L = AddLine(s.Board, s.ActiveLayer, 0.21, 0.23, 0.33, 0.29);
            var M = AddLine(s.Board, s.ActiveLayer, 0.61, 0.13, 0.83, 0.17);
            Enter(s, ToolId.Move, L);
            Vec2 f0 = V(0.335, 0.285);
            Vec2 f = V(f0.X + (0.515 - 0.33), f0.Y + (0.515 - 0.29));   // the end 0.0212 = 21 px from (0.5, 0.5)
            Drag(s, f0, f, 8);
            Exact(L.B, V(0.5, 0.5), "24 px tolerance snaps at 0.0212 board units (the 0.02 fallback would not)");

            // Tap-pick tolerance in Move is in pixels too: 0.0216 from M's stroke picks it.
            Tap(s, V(0.72, 0.172));
            Selected(s, "tap 22 px from M in Move", M);

            // A short drag of 9 px (< 10 px slop) is still a tap: no move.
            Vec2 mA = M.A;
            Down(s, V(0.9, 0.9));
            MoveTo(s, V(0.909, 0.9));
            Up(s, V(0.909, 0.9));
            Check.That(M.A == mA, "inside the pixel slop: a tap, nothing moves");
        }

        public static void V_MoveSnapWithPerspectiveMetric()
        {
            var s = new EditorSession { SnapMetric = new PerspectiveMetric { Tol = 24 } };
            var L = AddLine(s.Board, s.ActiveLayer, 0.31, 0.37, 0.43, 0.41);
            Enter(s, ToolId.Move, L);
            Vec2 f0 = V(0.434, 0.414);
            Vec2 f = V(f0.X + (0.5 - 0.43), f0.Y + (0.525 - 0.41));   // 0.025 board, about 13.5 px at this depth
            Drag(s, f0, f, 8);
            Exact(L.B, V(0.5, 0.5), "perspective: within 24 px and 0.03 board units -> exact");

            Vec2 b0 = L.B;
            f0 = V(0.504, 0.504);
            f = V(f0.X + 0.0, f0.Y + 0.035);   // 0.035 board > MaxSnapBoardDistance, though only about 18 px
            Drag(s, f0, f, 8);
            Near(L.B, V(0.5, 0.535), "beyond the 0.03 board cap: no snap, plain translation");
            Check.That(L.B != b0, "moved");
        }

        // ================= allocation =================

        static int s_previews;

        public static void V_TransformMovePathAllocatesNothing()
        {
            foreach (bool withMetric in new[] { false, true })
                foreach (var tool in Transforms)
                {
                    var board = GoldenBoards.StarOnFullBoard();
                    var top = board.Layers[board.Layers.Count - 1];
                    var ids = new List<EntityId>();
                    for (int i = 0; i < 12; i++)
                    {
                        ids.Add(AddLine(board, top, 0.05 + 0.07 * i, 0.05, 0.08 + 0.07 * i, 0.12).Id);
                        ids.Add(AddCircle(board, top, 0.08 + 0.07 * i, 0.2, 0.02).Id);
                    }
                    var s = new EditorSession(board);
                    if (withMetric) s.SnapMetric = new PerspectiveMetric { Tol = 24 };
                    s.PreviewChanged += () => s_previews++;
                    s.SetTool(ToolId.SelectMove);
                    s.SetSelection(ids);
                    s.SetTool(tool);
                    Check.That(s.ActiveTool == tool, $"{tool} entered");

                    var moves = new PointerEvent[200];
                    for (int i = 0; i < moves.Length; i++)
                    {
                        double t = i / 199.0;
                        moves[i] = new PointerEvent(PointerPhase.Move,
                            new Vec2(0.5 + 0.35 * Math.Sin(5 * t), 0.35 + 0.3 * Math.Abs(Math.Sin(3 * t + 0.4))), true, i);
                    }
                    var down = new PointerEvent(PointerPhase.Down, new Vec2(0.5, 0.35), true, 0);
                    var cancel = new PointerEvent(PointerPhase.Cancel, new Vec2(0.5, 0.35), false, 0);

                    // Warm-up gesture: pools, exclusion set and candidate lists reach full size.
                    s.Pointer(down);
                    for (int i = 0; i < moves.Length; i++) s.Pointer(moves[i]);
                    s.Pointer(cancel);

                    s.Pointer(down);
                    s.Pointer(moves[0]);
                    s.Pointer(moves[1]);
                    int previews0 = s_previews;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 2; i < moves.Length; i++) s.Pointer(moves[i]);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    int previews = s_previews - previews0;
                    s.Pointer(cancel);
                    Check.That(previews > 20, $"{tool}, metric {withMetric}: the ghosts followed ({previews})");
                    Check.That(bytes == 0, $"{tool}, metric {withMetric}: 198 Moves allocated {bytes} bytes");
                }
        }
    }
}
