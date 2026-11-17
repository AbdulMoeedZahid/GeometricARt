using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER: the Select tool (ToolId.SelectMove), written from the spec only. Covers SelectionGeometry (BoardRect,
    /// bounds, enclosure, stroke distance, collect, pick) and the session behaviour: entering the tool, marquee drag with
    /// live selection, inclusive edges, tap pick and its tolerance (fallback, capped, perspective metric), replacement,
    /// Cancel / lost Up / tool switch / Clear / Load, hidden layers and pruning, the exact toasts, no model edits, and an
    /// allocation-free Move path.
    /// </summary>
    public static class VerifierSelectChecks
    {
        const string HintLiteral = "Select: drag a box around whole shapes, or tap one";
        const string NothingLiteral = "Nothing selected: the box must enclose whole shapes";

        static double s_T;
        static PointerEvent Ev(PointerPhase p, double x, double y, bool on = true) =>
            new PointerEvent(p, new Vec2(x, y), on, s_T += 0.016);

        static void Drag(EditorSession s, double ax, double ay, double bx, double by, int steps = 6)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * t, ay + (by - ay) * t));
            }
            s.Pointer(Ev(PointerPhase.Up, bx, by));
        }

        static void Tap(EditorSession s, double x, double y)
        {
            s.Pointer(Ev(PointerPhase.Down, x, y));
            s.Pointer(Ev(PointerPhase.Up, x, y));
        }

        static int ShapeCount(EditorSession s)
        {
            int n = 0;
            foreach (var l in s.Board.Layers) n += l.Shapes.Count;
            return n;
        }

        static LineShape AddLine(Board b, Layer l, double ax, double ay, double bx, double by, StrokeStyle? style = null)
        {
            var sh = new LineShape(b.NewId(), new Vec2(ax, ay), new Vec2(bx, by), style ?? StrokeStyle.Default);
            l.Shapes.Add(sh);
            return sh;
        }

        static CircleShape AddCircle(Board b, Layer l, double cx, double cy, double r)
        {
            var sh = new CircleShape(b.NewId(), new Vec2(cx, cy), r, StrokeStyle.Default);
            l.Shapes.Add(sh);
            return sh;
        }

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

        static EditorSession SelectSession(Board b, List<string> toasts = null)
        {
            var s = new EditorSession(b);
            s.SetTool(ToolId.SelectMove);
            Check.That(s.ActiveTool == ToolId.SelectMove, $"Select tool accepted, got {s.ActiveTool}");
            if (toasts != null) s.Toast += t => toasts.Add(t);
            return s;
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
            Check.That(s.SelectionCount == want.Count, $"{what}: SelectionCount {s.SelectionCount}, expected {want.Count}");
        }

        static void Rect(BoardRect r, double minX, double minY, double maxX, double maxY, string what)
        {
            Check.Near(r.MinX, minX, what + " MinX"); Check.Near(r.MinY, minY, what + " MinY");
            Check.Near(r.MaxX, maxX, what + " MaxX"); Check.Near(r.MaxY, maxY, what + " MaxY");
        }

        // ================= SelectionGeometry =================

        public static void V_RectFromCornersAnyOrder()
        {
            Vec2 bl = new Vec2(0.2, 0.3), tr = new Vec2(0.6, 0.7), tl = new Vec2(0.2, 0.7), br = new Vec2(0.6, 0.3);
            Rect(BoardRect.FromCorners(bl, tr), 0.2, 0.3, 0.6, 0.7, "bl,tr");
            Rect(BoardRect.FromCorners(tr, bl), 0.2, 0.3, 0.6, 0.7, "tr,bl");
            Rect(BoardRect.FromCorners(tl, br), 0.2, 0.3, 0.6, 0.7, "tl,br");
            Rect(BoardRect.FromCorners(br, tl), 0.2, 0.3, 0.6, 0.7, "br,tl");
            var r = new BoardRect(0.2, 0.3, 0.6, 0.7);
            Rect(r, 0.2, 0.3, 0.6, 0.7, "ctor");
            Check.Near(r.Width, 0.4, "Width", 1e-12);
            Check.Near(r.Height, 0.4, "Height", 1e-12);
            Check.That(r.IsFinite, "finite rect");
            Check.That(!new BoardRect(double.NaN, 0.3, 0.6, 0.7).IsFinite, "NaN side is not finite");
            Check.That(!new BoardRect(0, 0, double.PositiveInfinity, 1).IsFinite, "infinite side is not finite");
            Check.That(SelectionGeometry.EdgeEpsilon == 1e-9, $"EdgeEpsilon 1e-9, got {SelectionGeometry.EdgeEpsilon:R}");
        }

        public static void V_RectContainsIsInclusiveWithEpsilon()
        {
            var r = new BoardRect(0.2, 0.3, 0.6, 0.7);
            Check.That(r.Contains(new Vec2(0.2, 0.3)) && r.Contains(new Vec2(0.6, 0.7)), "corners inside");
            Check.That(r.Contains(new Vec2(0.2, 0.5)) && r.Contains(new Vec2(0.6, 0.5)), "left/right edges inside");
            Check.That(r.Contains(new Vec2(0.4, 0.3)) && r.Contains(new Vec2(0.4, 0.7)), "bottom/top edges inside");
            Check.That(r.Contains(new Vec2(0.6 + 5e-10, 0.5)), "within EdgeEpsilon outside still counts");
            Check.That(!r.Contains(new Vec2(0.6 + 1e-6, 0.5)), "1e-6 right of the edge is outside");
            Check.That(!r.Contains(new Vec2(0.2 - 1e-6, 0.5)), "1e-6 left is outside");
            Check.That(!r.Contains(new Vec2(0.4, 0.3 - 1e-6)), "1e-6 below is outside");
            Check.That(!r.Contains(new Vec2(0.4, 0.7 + 1e-6)), "1e-6 above is outside");

            Check.That(r.Contains(r), "a rect contains itself");
            Check.That(r.Contains(new BoardRect(0.3, 0.4, 0.5, 0.6)), "inner rect");
            Check.That(!r.Contains(new BoardRect(0.3, 0.4, 0.6 + 1e-6, 0.6)), "inner rect poking 1e-6 out");
            Check.That(!r.Contains(new BoardRect(0.1, 0.1, 0.9, 0.9)), "outer rect");

            Rect(BoardRect.Union(new BoardRect(0, 0.05, 0.1, 0.1), new BoardRect(0.5, 0.6, 0.7, 0.8)), 0, 0.05, 0.7, 0.8, "union");
            Rect(BoardRect.Union(new BoardRect(0.5, 0.6, 0.7, 0.8), new BoardRect(0, 0.05, 0.1, 0.1)), 0, 0.05, 0.7, 0.8, "union swapped");
        }

        public static void V_ShapeBounds()
        {
            BoardRect b;
            Check.That(SelectionGeometry.TryGetBounds(new LineShape(new EntityId(1), new Vec2(0.7, 0.2), new Vec2(0.3, 0.6), StrokeStyle.Default), out b), "line bounds");
            Rect(b, 0.3, 0.2, 0.7, 0.6, "line");
            Check.That(SelectionGeometry.TryGetBounds(new CircleShape(new EntityId(2), new Vec2(0.5, 0.4), 0.1, StrokeStyle.Default), out b), "circle bounds");
            Rect(b, 0.4, 0.3, 0.6, 0.5, "circle");
            // Triangle pointing up: vertices at 90, 210, 330 degrees. Y spans [c - r/2, c + r]; X spans c +- r cos 30.
            var tri = new PolygonShape(new EntityId(3), new Vec2(0.5, 0.5), 3, 0.2, Math.PI / 2, StrokeStyle.Default);
            Check.That(SelectionGeometry.TryGetBounds(tri, out b), "triangle bounds");
            double hx = 0.2 * Math.Cos(Math.PI / 6);
            Rect(b, 0.5 - hx, 0.4, 0.5 + hx, 0.7, "triangle (vertices, not the circumscribed circle)");

            Check.That(!SelectionGeometry.TryGetBounds(null, out _), "null");
            Check.That(!SelectionGeometry.TryGetBounds(new LineShape(new EntityId(4), new Vec2(double.NaN, 0.2), new Vec2(0.3, 0.6), StrokeStyle.Default), out _), "NaN line");
            Check.That(!SelectionGeometry.TryGetBounds(new LineShape(new EntityId(4), new Vec2(0.1, 0.2), new Vec2(double.PositiveInfinity, 0.6), StrokeStyle.Default), out _), "infinite line");
            foreach (double r in new[] { 0.0, -0.1, double.NaN, double.PositiveInfinity })
                Check.That(!SelectionGeometry.TryGetBounds(new CircleShape(new EntityId(5), new Vec2(0.5, 0.5), r, StrokeStyle.Default), out _), $"circle radius {r}");
            Check.That(!SelectionGeometry.TryGetBounds(new CircleShape(new EntityId(5), new Vec2(0.5, double.NaN), 0.1, StrokeStyle.Default), out _), "NaN centre");
            Check.That(!SelectionGeometry.TryGetBounds(new PolygonShape(new EntityId(6), new Vec2(0.5, 0.5), 2, 0.1, 0, StrokeStyle.Default), out _), "polygon with 2 sides");
            Check.That(!SelectionGeometry.TryGetBounds(new PolygonShape(new EntityId(6), new Vec2(0.5, 0.5), 4, 0.0, 0, StrokeStyle.Default), out _), "polygon radius 0");
            Check.That(!SelectionGeometry.TryGetBounds(new PolygonShape(new EntityId(6), new Vec2(double.NaN, 0.5), 4, 0.1, 0, StrokeStyle.Default), out _), "polygon NaN centre");
        }

        public static void V_IsEnclosedRules()
        {
            var r = new BoardRect(0.3, 0.3, 0.7, 0.7);
            var touching = new CircleShape(new EntityId(1), new Vec2(0.5, 0.5), 0.2, StrokeStyle.Default);
            Check.That(SelectionGeometry.IsEnclosed(touching, r), "circle touching all four sides is enclosed");
            Check.That(!SelectionGeometry.IsEnclosed(touching, new BoardRect(0.3 + 1e-6, 0.3, 0.7, 0.7)), "left side 1e-6 in");
            Check.That(!SelectionGeometry.IsEnclosed(touching, new BoardRect(0.3, 0.3 + 1e-6, 0.7, 0.7)), "bottom side 1e-6 in");
            Check.That(!SelectionGeometry.IsEnclosed(touching, new BoardRect(0.3, 0.3, 0.7 - 1e-6, 0.7)), "right side 1e-6 in");
            Check.That(!SelectionGeometry.IsEnclosed(touching, new BoardRect(0.3, 0.3, 0.7, 0.7 - 1e-6)), "top side 1e-6 in");
            Check.That(!SelectionGeometry.IsEnclosed(new CircleShape(new EntityId(2), new Vec2(0.5, 0.5), 0.21, StrokeStyle.Default), r),
                "centre inside but the ring pokes out: not enclosed");

            Check.That(SelectionGeometry.IsEnclosed(new LineShape(new EntityId(3), new Vec2(0.3, 0.3), new Vec2(0.7, 0.7), StrokeStyle.Default), r), "diagonal corner to corner");
            Check.That(!SelectionGeometry.IsEnclosed(new LineShape(new EntityId(3), new Vec2(0.4, 0.4), new Vec2(0.8, 0.4), StrokeStyle.Default), r), "one endpoint out");
            Check.That(!SelectionGeometry.IsEnclosed(new LineShape(new EntityId(3), new Vec2(0.1, 0.5), new Vec2(0.9, 0.5), StrokeStyle.Default), r), "crossing the box");
            Check.That(SelectionGeometry.IsEnclosed(new LineShape(new EntityId(3), new Vec2(0.5, 0.35), new Vec2(0.5, 0.65), StrokeStyle.Default),
                new BoardRect(0.5, 0.3, 0.5, 0.7)), "vertical line in a zero-width box (inclusive edges)");

            var tri = new PolygonShape(new EntityId(4), new Vec2(0.5, 0.5), 3, 0.2, Math.PI / 2, StrokeStyle.Default);
            Check.That(SelectionGeometry.IsEnclosed(tri, new BoardRect(0.32, 0.39, 0.68, 0.71)), "triangle: all vertices inside (circle bounds are not)");
            Check.That(!SelectionGeometry.IsEnclosed(tri, new BoardRect(0.32, 0.41, 0.68, 0.71)), "triangle: bottom vertices outside");

            var all = new BoardRect(-1, -1, 2, 2);
            Check.That(!SelectionGeometry.IsEnclosed(new CircleShape(new EntityId(5), new Vec2(0.5, 0.5), 0, StrokeStyle.Default), all), "radius 0 never enclosed");
            Check.That(!SelectionGeometry.IsEnclosed(new CircleShape(new EntityId(5), new Vec2(0.5, 0.5), -0.1, StrokeStyle.Default), all), "negative radius never enclosed");
            Check.That(!SelectionGeometry.IsEnclosed(new LineShape(new EntityId(6), new Vec2(double.NaN, 0.5), new Vec2(0.5, 0.5), StrokeStyle.Default), all), "NaN line never enclosed");
            Check.That(!SelectionGeometry.IsEnclosed(null, all), "null never enclosed");
        }

        public static void V_DistanceToStroke()
        {
            var line = new LineShape(new EntityId(1), new Vec2(0.2, 0.5), new Vec2(0.8, 0.5), StrokeStyle.Default);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.6), line), 0.1, "perpendicular", 1e-12);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.9, 0.5), line), 0.1, "beyond the end: endpoint distance", 1e-12);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(1.0, 0.8), line), Math.Sqrt(0.2 * 0.2 + 0.3 * 0.3), "diagonal from the end", 1e-12);

            var circle = new CircleShape(new EntityId(2), new Vec2(0.5, 0.5), 0.3, StrokeStyle.Default);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.5), circle), 0.3, "centre is r from the ring", 1e-12);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.85), circle), 0.05, "outside the ring", 1e-12);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.75), circle), 0.05, "inside the ring", 1e-12);

            // Axis-aligned square of half side 0.2: 4 sides, circumradius 0.2*sqrt2, rotated 45 degrees.
            var square = new PolygonShape(new EntityId(3), new Vec2(0.5, 0.5), 4, 0.2 * Math.Sqrt(2), Math.PI / 4, StrokeStyle.Default);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.5), square), 0.2, "square centre: apothem", 1e-9);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.75), square), 0.05, "above the top edge", 1e-9);
            Check.Near(SelectionGeometry.DistanceToStroke(new Vec2(0.8, 0.8), square), 0.1 * Math.Sqrt(2), "beyond a corner", 1e-9);

            Check.That(double.IsPositiveInfinity(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.5),
                new CircleShape(new EntityId(4), new Vec2(0.5, 0.5), 0, StrokeStyle.Default))), "radius 0: +Infinity");
            Check.That(double.IsPositiveInfinity(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.5),
                new LineShape(new EntityId(5), new Vec2(double.NaN, 0.5), new Vec2(0.6, 0.5), StrokeStyle.Default))), "NaN line: +Infinity");
            Check.That(double.IsPositiveInfinity(SelectionGeometry.DistanceToStroke(new Vec2(0.5, 0.5),
                new PolygonShape(new EntityId(6), new Vec2(0.5, 0.5), 2, 0.1, 0, StrokeStyle.Default))), "2-sided polygon: +Infinity");
        }

        public static void V_CollectEnclosedVisibleOnlyClearsAndNullSafe()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var a = AddLine(b, bottom, 0.1, 0.1, 0.2, 0.2);
            var c = AddCircle(b, top, 0.5, 0.5, 0.1);
            AddLine(b, top, 0.1, 0.9, 0.9, 0.9);   // outside the box
            var into = new HashSet<EntityId> { new EntityId(999) };
            SelectionGeometry.CollectEnclosed(b, new BoardRect(0, 0, 0.7, 0.7), into);
            Check.That(into.SetEquals(new[] { a.Id, c.Id }), $"both layers, stale id cleared: {Ids(into)}");

            bottom.Visible = false;
            SelectionGeometry.CollectEnclosed(b, new BoardRect(0, 0, 1, 1), into);
            Check.That(into.Count == 2 && !into.Contains(a.Id), $"hidden layer skipped: {Ids(into)}");

            SelectionGeometry.CollectEnclosed(null, new BoardRect(0, 0, 1, 1), into);
            Check.That(into.Count == 0, "null board: cleared, no throw");
        }

        public static void V_TryPickTopmostNearestStrict()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var low = AddLine(b, bottom, 0.2, 0.5, 0.8, 0.5);
            var high = AddLine(b, top, 0.2, 0.5, 0.8, 0.5);
            Check.That(SelectionGeometry.TryPick(b, new Vec2(0.5, 0.51), 0.02, out var id) && id == high.Id, $"tie: top layer wins, got {id}");

            var later = AddLine(b, top, 0.2, 0.5, 0.8, 0.5);
            Check.That(SelectionGeometry.TryPick(b, new Vec2(0.5, 0.51), 0.02, out id) && id == later.Id, $"tie in a layer: later shape wins, got {id}");

            var near = AddLine(b, bottom, 0.2, 0.3, 0.8, 0.3);
            AddLine(b, top, 0.2, 0.32, 0.8, 0.32);
            Check.That(SelectionGeometry.TryPick(b, new Vec2(0.5, 0.305), 0.02, out id) && id == near.Id, $"nearer bottom stroke beats a farther top one, got {id}");

            Check.That(!SelectionGeometry.TryPick(b, new Vec2(0.5, 0.9), 0.02, out _), "nothing within tolerance");

            var e = new Board();
            var l = new Layer(e.NewId(), "L");
            e.Layers.Add(l);
            AddCircle(e, l, 0.5, 0.5, 0.4);
            Check.That(!SelectionGeometry.TryPick(e, new Vec2(0.5, 0.5), 0.02, out _), "inside a big circle, away from its ring");
            Check.That(SelectionGeometry.TryPick(e, new Vec2(0.5, 0.89), 0.02, out _), "near its ring");
        }

        // ================= entering the tool =================

        public static void V_SetToolSelectHintToastAndOneStateChanged()
        {
            Check.That(EditorSession.SelectHintToast == HintLiteral, $"hint text, got '{EditorSession.SelectHintToast}'");
            Check.That(EditorSession.NothingSelectedToast == NothingLiteral, $"nothing text, got '{EditorSession.NothingSelectedToast}'");
            Check.That(EditorSession.SelectedCountToast(1) == "1 shape selected", EditorSession.SelectedCountToast(1));
            Check.That(EditorSession.SelectedCountToast(2) == "2 shapes selected", EditorSession.SelectedCountToast(2));
            Check.That(EditorSession.SelectedCountToast(17) == "17 shapes selected", EditorSession.SelectedCountToast(17));
            Check.That(EditorSession.MaxPickBoardDistance == 0.03, "MaxPickBoardDistance 0.03");

            var s = new EditorSession();
            AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.3, 0.3);
            var toasts = new List<string>();
            int states = 0;
            s.Toast += t => toasts.Add(t);
            s.StateChanged += () => states++;

            s.SetTool(ToolId.SelectMove);
            Check.That(s.ActiveTool == ToolId.SelectMove, $"tool SelectMove, got {s.ActiveTool}");
            Check.That(states == 1, $"exactly one StateChanged entering Select, got {states}");
            Check.That(toasts.Count == 1 && toasts[0] == HintLiteral, $"hint toast once, got [{string.Join(" | ", toasts)}]");
            Check.That(s.ToolHint == HintLiteral, $"ToolHint, got '{s.ToolHint}'");

            Drag(s, 0.1, 0.1, 0.4, 0.4);
            Check.That(s.SelectionCount == 1, "precondition: one shape selected");
            states = 0;
            s.SetTool(ToolId.Line);
            Check.That(states == 1, $"exactly one StateChanged leaving Select with a selection, got {states}");
            Check.That(s.SelectionCount == 0 && s.Selection.Ids.Count == 0, "Line clears the selection");

            states = 0;
            s.SetTool(ToolId.Circle);
            s.SetTool(ToolId.SelectMove);
            Check.That(states == 2, $"Circle then Select: one StateChanged each, got {states}");

            s.SetTool(ToolId.None);
            states = 0;
            s.SetTool(ToolId.SelectMove);
            Check.That(states == 1 && s.ActiveTool == ToolId.SelectMove, "None -> Select: one StateChanged");
        }

        public static void V_OtherToolsStillComingSoonAndKeepSelection()
        {
            var s = new EditorSession();
            var line = AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.3, 0.3);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.1, 0.1, 0.4, 0.4);
            Selected(s, "precondition", line);
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            foreach (var t in new[] { ToolId.Stamps, ToolId.Rotate, ToolId.Erase })
            {
                toasts.Clear();
                s.SetTool(t);
                Check.That(s.ActiveTool == ToolId.SelectMove, $"{t} refused, still Select");
                Check.That(toasts.Count == 1 && toasts[0] == EditorSession.ComingSoonToast, $"{t}: Coming soon");
                Selected(s, $"{t} keeps the selection", line);
            }
            s.SetTool(ToolId.SelectMove);
            Check.That(s.ActiveTool == ToolId.SelectMove, "re-selecting Select");
            Selected(s, "re-selecting Select keeps the selection", line);

            s.SetTool(ToolId.None);
            Check.That(s.SelectionCount == 0, "None clears the selection");
        }

        // ================= marquee =================

        public static void V_MarqueeSelectsAcrossVisibleLayersWithCountToast()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var line = AddLine(b, bottom, 0.2, 0.2, 0.4, 0.3);
            var circle = AddCircle(b, top, 0.5, 0.4, 0.1);
            AddLine(b, top, 0.3, 0.5, 0.9, 0.5);        // partly inside
            AddCircle(b, bottom, 0.85, 0.85, 0.05);     // outside
            var toasts = new List<string>();
            var s = SelectSession(b, toasts);
            int boards = 0;
            s.BoardChanged += _ => boards++;
            int shapes = ShapeCount(s), nextId = b.NextId;

            Drag(s, 0.1, 0.1, 0.65, 0.55);
            Selected(s, "line on bottom + circle on top", line, circle);
            Check.That(toasts.Count == 1 && toasts[0] == "2 shapes selected", $"count toast, got [{string.Join(" | ", toasts)}]");
            Check.That(!s.GestureActive && s.Preview.Count == 0 && !s.Cursor.HasValue, "rectangle and cursor gone after Up");

            toasts.Clear();
            Drag(s, 0.15, 0.15, 0.45, 0.35);
            Selected(s, "one line", line);
            Check.That(toasts.Count == 1 && toasts[0] == "1 shape selected", $"singular toast, got [{string.Join(" | ", toasts)}]");

            Check.That(boards == 0, "selection never raises BoardChanged");
            Check.That(ShapeCount(s) == shapes && b.NextId == nextId, "no shape added or removed, no ids used");
        }

        public static void V_PartialAndCentreOnlyShapesNotSelected()
        {
            var s = new EditorSession();
            var l = s.ActiveLayer;
            AddLine(s.Board, l, 0.2, 0.2, 0.7, 0.2);                 // half out of the box
            AddCircle(s.Board, l, 0.4, 0.4, 0.101);                 // centre deep inside, ring 0.001 out
            var toasts = new List<string>();
            s.SetTool(ToolId.SelectMove);
            s.Toast += t => toasts.Add(t);
            Drag(s, 0.1, 0.1, 0.5, 0.5);
            Check.That(s.SelectionCount == 0, $"nothing enclosed, got {Ids(s.Selection.Ids)}");
            Check.That(toasts.Count == 1 && toasts[0] == NothingLiteral, $"nothing toast, got [{string.Join(" | ", toasts)}]");
        }

        public static void V_EdgesInclusiveAndOneMicroOutside()
        {
            var s = new EditorSession();
            var line = AddLine(s.Board, s.ActiveLayer, 0.2, 0.3, 0.6, 0.3);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.2, 0.3, 0.6, 0.5);
            Selected(s, "endpoints exactly on the left, right and bottom edges", line);
            Drag(s, 0.2 + 1e-6, 0.3, 0.6, 0.5);
            Check.That(s.SelectionCount == 0, "left edge 1e-6 inside the start: excluded");
            Drag(s, 0.2, 0.3 + 1e-6, 0.6, 0.5);
            Check.That(s.SelectionCount == 0, "bottom edge 1e-6 above the line: excluded");

            var t = new EditorSession();
            var c = AddCircle(t.Board, t.ActiveLayer, 0.5, 0.5, 0.2);
            t.SetTool(ToolId.SelectMove);
            Drag(t, 0.3, 0.3, 0.7, 0.7);
            Selected(t, "circle touching all four sides", c);
            Drag(t, 0.3 + 1e-6, 0.3, 0.7, 0.7);
            Check.That(t.SelectionCount == 0, "left side 1e-6 in");
            Drag(t, 0.3, 0.3 + 1e-6, 0.7, 0.7);
            Check.That(t.SelectionCount == 0, "bottom side 1e-6 in");
            Drag(t, 0.3, 0.3, 0.7 - 1e-6, 0.7);
            Check.That(t.SelectionCount == 0, "right side 1e-6 in");
            Drag(t, 0.3, 0.3, 0.7, 0.7 - 1e-6);
            Check.That(t.SelectionCount == 0, "top side 1e-6 in");
        }

        public static void V_ReversedCornersSelectTheSame()
        {
            var s = new EditorSession();
            var line = AddLine(s.Board, s.ActiveLayer, 0.3, 0.3, 0.5, 0.4);
            var circle = AddCircle(s.Board, s.ActiveLayer, 0.6, 0.6, 0.05);
            AddLine(s.Board, s.ActiveLayer, 0.1, 0.9, 0.9, 0.9);
            s.SetTool(ToolId.SelectMove);
            double[][] drags =
            {
                new[] { 0.2, 0.2, 0.7, 0.7 }, new[] { 0.7, 0.7, 0.2, 0.2 },
                new[] { 0.2, 0.7, 0.7, 0.2 }, new[] { 0.7, 0.2, 0.2, 0.7 },
            };
            foreach (var d in drags)
            {
                s.ClearSelection();
                Drag(s, d[0], d[1], d[2], d[3]);
                Selected(s, $"drag ({d[0]}, {d[1]}) -> ({d[2]}, {d[3]})", line, circle);
            }
        }

        public static void V_NewMarqueeReplacesAndEmptyMarqueeClears()
        {
            var s = new EditorSession();
            var a = AddLine(s.Board, s.ActiveLayer, 0.1, 0.1, 0.2, 0.2);
            var b = AddLine(s.Board, s.ActiveLayer, 0.6, 0.6, 0.7, 0.7);
            s.SetTool(ToolId.SelectMove);
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            Drag(s, 0.05, 0.05, 0.25, 0.25);
            Selected(s, "a", a);
            Drag(s, 0.55, 0.55, 0.75, 0.75);
            Selected(s, "b replaces a (no additive mode)", b);
            toasts.Clear();
            int states = 0;
            s.StateChanged += () => states++;
            Drag(s, 0.3, 0.3, 0.45, 0.45);
            Check.That(s.SelectionCount == 0, "a drag around nothing leaves nothing selected");
            Check.That(states == 1, $"one StateChanged for the change, got {states}");
            Check.That(toasts.Count == 1 && toasts[0] == NothingLiteral, $"nothing toast, got [{string.Join(" | ", toasts)}]");
        }

        public static void V_LiveSelectionAndStateChangedOnlyOnChange()
        {
            var s = new EditorSession();
            var l1 = AddLine(s.Board, s.ActiveLayer, 0.15, 0.2, 0.3, 0.25);
            var l2 = AddLine(s.Board, s.ActiveLayer, 0.5, 0.2, 0.6, 0.3);
            s.SetTool(ToolId.SelectMove);
            var toasts = new List<string>();
            int states = 0;
            s.Toast += t => toasts.Add(t);
            s.StateChanged += () => states++;

            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.1));
            (double x, double y, int states, Shape[] sel)[] steps =
            {
                (0.2, 0.4, 0, new Shape[0]),          // past slop, nothing enclosed: no change
                (0.25, 0.4, 0, new Shape[0]),
                (0.35, 0.4, 1, new Shape[] { l1 }),
                (0.4, 0.4, 1, new Shape[] { l1 }),
                (0.65, 0.4, 2, new Shape[] { l1, l2 }),
                (0.7, 0.45, 2, new Shape[] { l1, l2 }),
                (0.4, 0.4, 3, new Shape[] { l1 }),
                (0.12, 0.12, 4, new Shape[0]),
                (0.65, 0.4, 5, new Shape[] { l1, l2 }),
            };
            foreach (var st in steps)
            {
                s.Pointer(Ev(PointerPhase.Move, st.x, st.y));
                Selected(s, $"live at ({st.x}, {st.y})", st.sel);
                Check.That(states == st.states, $"at ({st.x}, {st.y}): {st.states} StateChanged expected, got {states}");
                Check.That(toasts.Count == 0, "no toast during the drag");
            }

            // The rectangle: 4 dotted Medium edges in the current colour, corners (0.1, 0.1) and (0.65, 0.4).
            Check.That(s.Preview.Count == 4, $"4 marquee edges, got {s.Preview.Count}");
            var corners = new HashSet<Vec2>();
            foreach (var sh in s.Preview)
            {
                var e = sh as LineShape;
                Check.That(e != null, "edges are LineShapes");
                Check.That(e.Style.Dash == LineStyle.Dotted && e.Style.Width == LineWidth.Medium, "dotted Medium");
                Check.That(e.Style.Color.Equals(s.Style.Color), "current colour");
                Check.That(e.A.X == e.B.X || e.A.Y == e.B.Y, "axis aligned");
                corners.Add(e.A);
                corners.Add(e.B);
            }
            var want = new HashSet<Vec2> { new Vec2(0.1, 0.1), new Vec2(0.65, 0.1), new Vec2(0.65, 0.4), new Vec2(0.1, 0.4) };
            Check.That(corners.SetEquals(want), "edges meet at the four corners");
            Check.That(s.Cursor == new Vec2(0.65, 0.4), $"cursor on the moving corner, got {s.Cursor}");

            s.Pointer(Ev(PointerPhase.Up, 0.65, 0.4));
            Check.That(states == 5, $"Up with no change raises nothing more, got {states}");
            Check.That(toasts.Count == 1 && toasts[0] == "2 shapes selected", $"toast on Up only, got [{string.Join(" | ", toasts)}]");
            Selected(s, "kept after Up", l1, l2);
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "rectangle removed");
        }

        public static void V_NoRectangleOrLiveChangeWithinTapSlop()
        {
            var s = new EditorSession();
            var a = AddLine(s.Board, s.ActiveLayer, 0.1, 0.1, 0.2, 0.2);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.05, 0.05, 0.25, 0.25);
            Selected(s, "precondition", a);
            int previews = 0;
            s.PreviewChanged += () => previews++;

            s.Pointer(Ev(PointerPhase.Down, 0.6, 0.6));
            Check.That(s.GestureActive, "gesture open");
            s.Pointer(Ev(PointerPhase.Move, 0.605, 0.6));   // 0.005 < slop 0.02 * 10/24
            Check.That(s.Preview.Count == 0, $"nothing drawn within tap slop, got {s.Preview.Count}");
            Selected(s, "no live replacement within tap slop", a);
            s.Pointer(Ev(PointerPhase.Move, 0.65, 0.65));
            Check.That(s.Preview.Count == 4, $"rectangle after slop, got {s.Preview.Count}");
            Check.That(s.SelectionCount == 0, "live replacement after slop (empty box)");
            Check.That(previews > 0, "PreviewChanged raised");
            s.Pointer(Ev(PointerPhase.Up, 0.65, 0.65));
            Check.That(s.Preview.Count == 0 && !s.GestureActive, "cleared after Up");
        }

        public static void V_MarqueeNeverSnapsAndIgnoresHVLock()
        {
            var s = new EditorSession();   // snapping on; (0.5, 0.5) is a grid corner candidate
            Check.That(s.SnapEnabled, "precondition: snapping on");
            var l = AddLine(s.Board, s.ActiveLayer, 0.3, 0.3, 0.499, 0.45);
            var m = AddLine(s.Board, s.ActiveLayer, 0.501, 0.6, 0.8, 0.8);
            s.SetTool(ToolId.SelectMove);

            Drag(s, 0.1, 0.1, 0.498, 0.498);     // a snapped end (0.5, 0.5) would enclose l
            Check.That(s.SelectionCount == 0, $"moving corner is raw, got {Ids(s.Selection.Ids)}");
            Drag(s, 0.1, 0.1, 0.4995, 0.498);
            Selected(s, "control: raw corner just past the endpoint", l);
            Drag(s, 0.502, 0.502, 0.9, 0.9);     // a snapped anchor (0.5, 0.5) would enclose m
            Check.That(s.SelectionCount == 0, $"anchor is the raw Down point, got {Ids(s.Selection.Ids)}");
            Drag(s, 0.5005, 0.502, 0.9, 0.9);
            Selected(s, "control: raw anchor just left of the endpoint", m);

            var t = new EditorSession();
            var c = AddCircle(t.Board, t.ActiveLayer, 0.5, 0.5, 0.1);
            t.SetOrthoLock(true);
            t.SetTool(ToolId.SelectMove);
            Drag(t, 0.35, 0.36, 0.65, 0.62);
            Selected(t, "diagonal box with H/V lock on is still a box", c);
        }

        public static void V_OffBoardDownIgnoredAndOffBoardMoveKeepsLastPoint()
        {
            var s = new EditorSession();
            var a = AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.3, 0.3);
            var b = AddLine(s.Board, s.ActiveLayer, 0.2, 0.7, 0.3, 0.8);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.1, 0.1, 0.4, 0.4);
            Selected(s, "precondition", a);

            s.Pointer(Ev(PointerPhase.Down, 1.3, 0.5, on: false));
            Check.That(!s.GestureActive, "no gesture from an off-board Down");
            s.Pointer(Ev(PointerPhase.Up, 1.3, 0.5, on: false));
            Selected(s, "an off-board press is not an empty-board tap", a);

            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.6));
            s.Pointer(Ev(PointerPhase.Move, 0.25, 0.75));
            s.Pointer(Ev(PointerPhase.Move, 0.4, 0.9));
            s.Pointer(Ev(PointerPhase.Move, 1.4, 0.9, on: false));
            s.Pointer(Ev(PointerPhase.Up, double.NaN, double.NaN, on: false));
            Selected(s, "box uses the last on-board point", b);
        }

        // ================= tap pick =================

        public static void V_TapPicksStrokeWithinFallbackTolerance()
        {
            var s = new EditorSession();
            var line = AddLine(s.Board, s.ActiveLayer, 0.2, 0.5, 0.8, 0.5);
            s.SetTool(ToolId.SelectMove);
            var toasts = new List<string>();
            int states = 0;
            s.Toast += t => toasts.Add(t);
            s.StateChanged += () => states++;

            Tap(s, 0.5, 0.519);
            Selected(s, "0.019 from the stroke (fallback tolerance 0.02)", line);
            Check.That(toasts.Count == 1 && toasts[0] == "1 shape selected", $"tap toast, got [{string.Join(" | ", toasts)}]");
            Check.That(states == 1, $"one StateChanged, got {states}");

            Tap(s, 0.5, 0.481);
            Selected(s, "tap below, same shape", line);
            Check.That(toasts.Count == 2 && toasts[1] == "1 shape selected", "toasts again even when unchanged");
            Check.That(states == 1, $"no StateChanged when unchanged, got {states}");

            Tap(s, 0.5, 0.521);
            Check.That(s.SelectionCount == 0, "0.021 away: an empty tap clears (fallback is 0.02, not the 0.03 cap)");
            Check.That(toasts.Count == 2, "an empty tap has no toast");
            Check.That(states == 2, $"one StateChanged for the clear, got {states}");

            Tap(s, 0.9, 0.9);
            Check.That(states == 2 && toasts.Count == 2, "empty tap with nothing selected: no event, no toast");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "no ghost after taps");
        }

        public static void V_TapInsideBigCircleOrPolygonDoesNotSelect()
        {
            var s = new EditorSession();
            var small = AddLine(s.Board, s.ActiveLayer, 0.01, 0.01, 0.05, 0.03);
            var big = AddCircle(s.Board, s.ActiveLayer, 0.5, 0.5, 0.4);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.0, 0.0, 0.07, 0.07);
            Selected(s, "precondition", small);

            Tap(s, 0.5, 0.5);
            Check.That(s.SelectionCount == 0, $"tap at the centre of a big circle clears, got {Ids(s.Selection.Ids)}");
            Tap(s, 0.5, 0.889);
            Selected(s, "0.011 inside the ring", big);
            Tap(s, 0.5, 0.75);
            Check.That(s.SelectionCount == 0, "0.15 inside the ring: nothing");

            var p = new EditorSession();
            var square = new PolygonShape(p.Board.NewId(), new Vec2(0.5, 0.5), 4, 0.2 * Math.Sqrt(2), Math.PI / 4, StrokeStyle.Default);
            p.ActiveLayer.Shapes.Add(square);
            p.SetTool(ToolId.SelectMove);
            Tap(p, 0.5, 0.5);
            Check.That(p.SelectionCount == 0, "inside a polygon, away from its edges: nothing");
            Tap(p, 0.5, 0.71);
            Selected(p, "near a polygon edge", square);
            Drag(p, 0.25, 0.25, 0.75, 0.75);
            Selected(p, "polygon boxed by its vertices", square);
        }

        public static void V_TapTopmostWinsHiddenSkippedHiddenActiveDoesNotBlock()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var low = AddLine(b, bottom, 0.2, 0.5, 0.8, 0.5);
            var high = AddLine(b, top, 0.2, 0.5, 0.8, 0.5);
            var toasts = new List<string>();
            var s = SelectSession(b, toasts);

            Tap(s, 0.5, 0.505);
            Selected(s, "identical strokes: the top layer's", high);

            s.SetLayerVisible(top.Id, false);   // the ACTIVE layer
            Check.That(s.ActiveLayer == top && !top.Visible, "precondition: active layer hidden");
            Check.That(s.SelectionCount == 0, "hiding the layer pruned the selection");
            toasts.Clear();
            Tap(s, 0.5, 0.505);
            Selected(s, "hidden top skipped: the bottom's", low);
            Check.That(!toasts.Contains(EditorSession.HiddenLayerToast), "no hidden-layer toast in Select");
            Check.That(toasts.Count == 1 && toasts[0] == "1 shape selected", $"tap toast, got [{string.Join(" | ", toasts)}]");

            var later = AddLine(b, bottom, 0.2, 0.5, 0.8, 0.5);
            Tap(s, 0.5, 0.495);
            Selected(s, "same layer tie: the later shape", later);
        }

        public static void V_HiddenActiveLayerDoesNotBlockMarquee()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var a = AddLine(b, bottom, 0.2, 0.2, 0.4, 0.4);
            var c = AddCircle(b, bottom, 0.6, 0.6, 0.05);
            AddLine(b, top, 0.25, 0.2, 0.35, 0.3);   // on the hidden active layer
            var s = new EditorSession(b);
            s.SetLayerVisible(top.Id, false);
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.1, 0.1, 0.8, 0.8);
            Selected(s, "visible layer's shapes only", a, c);
            Check.That(!toasts.Contains(EditorSession.HiddenLayerToast), "no hidden-layer toast");
            Check.That(toasts.Count == 2 && toasts[0] == HintLiteral && toasts[1] == "2 shapes selected",
                $"hint then count, got [{string.Join(" | ", toasts)}]");
        }

        public static void V_TapReplacesMultiSelection()
        {
            var s = new EditorSession();
            var a = AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.4, 0.2);
            var c = AddCircle(s.Board, s.ActiveLayer, 0.3, 0.6, 0.1);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.1, 0.1, 0.5, 0.8);
            Selected(s, "both", a, c);
            Tap(s, 0.3, 0.705);
            Selected(s, "a tap on one stroke replaces with just that shape", c);
        }

        sealed class AffineMetric : ISnapMetric
        {
            public double Scale = 1000, Tol = 24, FailAboveY = double.PositiveInfinity;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                if (board.Y >= FailAboveY) { screenPx = default; return false; }
                screenPx = new Vec2(board.X * Scale + 11, board.Y * Scale - 7);
                return true;
            }
        }

        /// <summary>w = 1 + 0.8 y, screen = 1000 (x, y) / w: anisotropic and varying with depth (as in SnapToolChecks).</summary>
        sealed class PerspectiveMetric : ISnapMetric
        {
            public double Tol = 6;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                double w = 1 + 0.8 * board.Y;
                screenPx = new Vec2(1000 * board.X / w, 1000 * board.Y / w);
                return true;
            }
        }

        public static void V_PickToleranceWithMetricIsInPixelsAndCapped()
        {
            // Scale 500, 24 px: 0.048 board units, capped at MaxPickBoardDistance 0.03.
            var s = new EditorSession { SnapMetric = new AffineMetric { Scale = 500, Tol = 24 } };
            var line = AddLine(s.Board, s.ActiveLayer, 0.2, 0.5, 0.8, 0.5);
            s.SetTool(ToolId.SelectMove);
            Tap(s, 0.5, 0.528);
            Selected(s, "0.028 = 14 px: picked (fallback 0.02 would not)", line);
            Tap(s, 0.5, 0.532);
            Check.That(s.SelectionCount == 0, "0.032 = 16 px: inside 24 px but beyond the 0.03 cap");

            // Scale 1000, 10 px: 0.01 board units.
            var t = new EditorSession { SnapMetric = new AffineMetric { Scale = 1000, Tol = 10 } };
            var l2 = AddLine(t.Board, t.ActiveLayer, 0.2, 0.5, 0.8, 0.5);
            t.SetTool(ToolId.SelectMove);
            Tap(t, 0.5, 0.509);
            Selected(t, "9 px: picked", l2);
            Tap(t, 0.5, 0.512);
            Check.That(t.SelectionCount == 0, "12 px > 10 px: not picked (fallback 0.02 would)");

            // Not projectable at the tap point: board-unit fallback 0.02.
            var u = new EditorSession { SnapMetric = new AffineMetric { Scale = 1000, Tol = 5, FailAboveY = 0.9 } };
            var l3 = AddLine(u.Board, u.ActiveLayer, 0.2, 0.95, 0.8, 0.95);
            u.SetTool(ToolId.SelectMove);
            Tap(u, 0.5, 0.965);
            Selected(u, "unprojectable: fallback 0.02 picks 0.015", l3);
        }

        /// <summary>The spec's PickTolerance formula, for the expected value under a metric.</summary>
        static double ExpectedPickTolerance(ISnapMetric m, Vec2 p)
        {
            const double h = 0.01;
            double hx = p.X <= 0.5 ? h : -h, hy = p.Y <= 0.5 ? h : -h;
            m.TryToScreen(p, out Vec2 s0);
            m.TryToScreen(new Vec2(p.X + hx, p.Y), out Vec2 sx);
            m.TryToScreen(new Vec2(p.X, p.Y + hy), out Vec2 sy);
            double px = Math.Min(Vec2.Distance(sx, s0), Vec2.Distance(sy, s0)) / h;
            return Math.Min(EditorSession.MaxPickBoardDistance, m.TolerancePx / px);
        }

        public static void V_PickToleranceWithPerspectiveMetric()
        {
            var metric = new PerspectiveMetric { Tol = 6 };
            double tol0 = ExpectedPickTolerance(metric, new Vec2(0.5, 0.5));
            Check.That(tol0 > 0.009 && tol0 < 0.013, $"precondition: about 0.011 board units, got {tol0:R}");

            var s = new EditorSession { SnapMetric = metric };
            var h = AddLine(s.Board, s.ActiveLayer, 0.2, 0.5, 0.8, 0.5);
            var v = AddLine(s.Board, s.ActiveLayer, 0.5, 0.65, 0.5, 0.9);
            s.SetTool(ToolId.SelectMove);

            Vec2 inY = new Vec2(0.4, 0.5 + 0.85 * tol0), outY = new Vec2(0.4, 0.5 + 1.15 * tol0);
            Check.That(0.85 * tol0 < ExpectedPickTolerance(metric, inY) && 1.15 * tol0 > ExpectedPickTolerance(metric, outY),
                "precondition: offsets straddle the local tolerance");
            Tap(s, inY.X, inY.Y);
            Selected(s, "85% of the metric tolerance above the horizontal line", h);
            Tap(s, outY.X, outY.Y);
            Check.That(s.SelectionCount == 0, $"115% of it ({1.15 * tol0:0.0000} < fallback 0.02): not picked");

            double tolV = ExpectedPickTolerance(metric, new Vec2(0.5, 0.8));
            Check.That(0.85 * tolV < ExpectedPickTolerance(metric, new Vec2(0.5 - 0.85 * tolV, 0.8))
                       && 1.15 * tolV > ExpectedPickTolerance(metric, new Vec2(0.5 - 1.15 * tolV, 0.8))
                       && 1.15 * tolV < EditorSession.FallbackSnapTolerance,
                "precondition: offsets straddle the local tolerance, below the fallback");
            Tap(s, 0.5 - 0.85 * tolV, 0.8);
            Selected(s, "85% beside the vertical line, deeper on the board", v);
            Tap(s, 0.5 - 1.15 * tolV, 0.8);
            Check.That(s.SelectionCount == 0, "115% beside it: not picked");
        }

        // ================= cancel / switch / clear =================

        public static void V_CancelRestoresPreviousSelection()
        {
            var s = new EditorSession();
            var a = AddLine(s.Board, s.ActiveLayer, 0.1, 0.1, 0.2, 0.2);
            var b = AddLine(s.Board, s.ActiveLayer, 0.6, 0.6, 0.7, 0.7);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.05, 0.05, 0.25, 0.25);
            Selected(s, "precondition", a);
            var toasts = new List<string>();
            int states = 0;
            s.Toast += t => toasts.Add(t);
            s.StateChanged += () => states++;

            s.Pointer(Ev(PointerPhase.Down, 0.55, 0.55));
            s.Pointer(Ev(PointerPhase.Move, 0.65, 0.65));
            s.Pointer(Ev(PointerPhase.Move, 0.75, 0.75));
            Selected(s, "live: b", b);
            int live = states;
            s.Pointer(Ev(PointerPhase.Cancel, 0.75, 0.75, on: false));
            Selected(s, "Cancel restores a", a);
            Check.That(states == live + 1, $"one StateChanged for the restore, got {states - live}");
            Check.That(toasts.Count == 0, "no toast on Cancel");
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue && !s.GestureActive, "rectangle removed");
            s.Pointer(Ev(PointerPhase.Up, 0.75, 0.75));
            Selected(s, "a stray Up after Cancel changes nothing", a);
            Check.That(toasts.Count == 0, "no toast from the stray Up");

            s.ClearSelection();
            s.Pointer(Ev(PointerPhase.Down, 0.55, 0.55));
            s.Pointer(Ev(PointerPhase.Move, 0.75, 0.75));
            Selected(s, "live: b again", b);
            s.Pointer(Ev(PointerPhase.Cancel, 0.75, 0.75, on: false));
            Check.That(s.SelectionCount == 0, "Cancel restores an empty selection too");
        }

        public static void V_LostUpDiscardsLikeCancel()
        {
            var s = new EditorSession();
            var a = AddLine(s.Board, s.ActiveLayer, 0.1, 0.1, 0.2, 0.2);
            var b = AddLine(s.Board, s.ActiveLayer, 0.6, 0.6, 0.7, 0.7);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.05, 0.05, 0.25, 0.25);
            s.Pointer(Ev(PointerPhase.Down, 0.55, 0.55));
            s.Pointer(Ev(PointerPhase.Move, 0.75, 0.75));
            Selected(s, "live: b", b);
            s.Pointer(Ev(PointerPhase.Down, 0.9, 0.1));   // the Up was lost
            Selected(s, "the unfinished box is discarded: a restored", a);
            s.Pointer(Ev(PointerPhase.Up, 0.9, 0.1));
            Check.That(s.SelectionCount == 0, "then the new press is an empty tap: cleared");
        }

        public static void V_ToolSwitchMidDragClearsAndNeverCommits()
        {
            var s = new EditorSession();
            AddLine(s.Board, s.ActiveLayer, 0.6, 0.6, 0.7, 0.7);
            s.SetTool(ToolId.SelectMove);
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            int shapes = ShapeCount(s);
            s.Pointer(Ev(PointerPhase.Down, 0.55, 0.55));
            s.Pointer(Ev(PointerPhase.Move, 0.75, 0.75));
            Check.That(s.SelectionCount == 1, "live selection");
            s.SetTool(ToolId.Circle);
            Check.That(s.SelectionCount == 0, "Circle clears the selection");
            Check.That(!s.GestureActive && s.Preview.Count == 0 && !s.Cursor.HasValue, "gesture closed, rectangle gone");
            s.Pointer(Ev(PointerPhase.Move, 0.85, 0.85));
            s.Pointer(Ev(PointerPhase.Up, 0.85, 0.85));
            Check.That(ShapeCount(s) == shapes, "the old Select gesture never commits a circle");
            Check.That(toasts.Count == 0, $"no select toasts, got [{string.Join(" | ", toasts)}]");
        }

        public static void V_ClearAndLoadResetSelection()
        {
            var s = new EditorSession();
            AddLine(s.Board, s.ActiveLayer, 0.1, 0.1, 0.2, 0.2);
            AddLine(s.Board, s.ActiveLayer, 0.6, 0.6, 0.7, 0.7);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.05, 0.05, 0.25, 0.25);
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            s.Pointer(Ev(PointerPhase.Down, 0.55, 0.55));
            s.Pointer(Ev(PointerPhase.Move, 0.75, 0.75));
            s.ClearBoard();
            Check.That(s.SelectionCount == 0 && s.Selection.Ids.Count == 0, "ClearBoard mid-drag: empty (the restore must not survive)");
            Check.That(!s.GestureActive && s.Preview.Count == 0 && !s.Cursor.HasValue, "gesture aborted");
            Check.That(s.ActiveTool == ToolId.SelectMove, "tool kept");
            s.Pointer(Ev(PointerPhase.Move, 0.8, 0.8));
            s.Pointer(Ev(PointerPhase.Up, 0.8, 0.8));
            Check.That(toasts.Count == 0 && s.SelectionCount == 0, "the stale gesture never finishes");

            // A loaded board whose ids collide with the old selection: still empty.
            var nb = new Board();
            var layer = new Layer(nb.NewId(), "L");
            nb.Layers.Add(layer);
            nb.ActiveLayerId = layer.Id;
            for (int i = 0; i < 6; i++) AddLine(nb, layer, 0.1 + 0.1 * i, 0.1, 0.15 + 0.1 * i, 0.2);
            Drag(s, 0.0, 0.0, 1.0, 1.0);   // nothing on the empty board
            s.LoadBoard(GoldenBoards.StarOnFullBoard());
            Drag(s, 0.0, 0.0, 1.0, 1.0);
            Check.That(s.SelectionCount > 0, "precondition: the star board selected");
            s.LoadBoard(nb);
            Check.That(s.SelectionCount == 0 && s.Selection.Ids.Count == 0, "LoadBoard clears the selection");
        }

        // ================= layers and pruning =================

        public static void V_HiddenLayerShapesCannotBeBoxed()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            AddLine(b, bottom, 0.2, 0.2, 0.3, 0.3);
            var visible = AddLine(b, top, 0.6, 0.6, 0.7, 0.7);
            var s = new EditorSession(b);
            s.SetLayerVisible(bottom.Id, false);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.0, 0.0, 1.0, 1.0);
            Selected(s, "only the visible layer", visible);
        }

        public static void V_PruneOnHideBeforeBoardChangedNotRestoredOnShow()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var a = AddLine(b, bottom, 0.2, 0.2, 0.3, 0.3);
            var c = AddCircle(b, top, 0.6, 0.6, 0.05);
            var s = SelectSession(b);
            Drag(s, 0.0, 0.0, 1.0, 1.0);
            Selected(s, "precondition", a, c);

            var events = new List<string>();
            int seenInBoardChanged = -1;
            s.BoardChanged += k => { events.Add("B"); seenInBoardChanged = s.Selection.Ids.Count; };
            s.StateChanged += () => events.Add("S");
            s.SetLayerVisible(bottom.Id, false);
            Selected(s, "hidden layer's shape pruned", c);
            Check.That(seenInBoardChanged == 1, $"pruned before BoardChanged listeners run, they saw {seenInBoardChanged}");
            Check.That(string.Join(",", events) == "B,S", $"BoardChanged then one StateChanged, got [{string.Join(",", events)}]");

            events.Clear();
            s.SetLayerVisible(bottom.Id, true);
            Selected(s, "showing it again does not re-select", c);
            Check.That(string.Join(",", events) == "B", $"no StateChanged when nothing was pruned, got [{string.Join(",", events)}]");

            Drag(s, 0.0, 0.0, 1.0, 1.0);
            events.Clear();
            s.SetLayerOpacity(bottom.Id, 0.3);
            s.MoveLayer(bottom.Id, 1);
            s.SetGrid(3);
            s.SetGridKind(GridKind.Hex);
            Selected(s, "opacity, reorder and grid changes keep the selection", a, c);
            Check.That(!events.Contains("S"), $"and raise no StateChanged, got [{string.Join(",", events)}]");
            s.AddLayer();
            Selected(s, "AddLayer keeps the selection", a, c);
        }

        public static void V_PruneOnDeleteBottomLayer()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var a = AddLine(b, bottom, 0.2, 0.2, 0.3, 0.3);
            var c = AddCircle(b, top, 0.6, 0.6, 0.05);
            var s = SelectSession(b);
            Drag(s, 0.0, 0.0, 1.0, 1.0);
            int states = 0;
            s.StateChanged += () => states++;
            s.DeleteBottomLayer();
            Selected(s, "deleted layer's shape dropped", c);
            Check.That(states >= 1, "StateChanged raised");
            var list = new List<EditorSession.SelectedShape>();
            s.GetSelectedShapes(list);
            Check.That(list.Count == 1 && list[0].Shape == c && list[0].Layer == top, "GetSelectedShapes agrees");
        }

        public static void V_PruneWhenShapeDisappearsFromTheModel()
        {
            var s = new EditorSession();
            var a = AddLine(s.Board, s.ActiveLayer, 0.2, 0.2, 0.3, 0.3);
            var c = AddCircle(s.Board, s.ActiveLayer, 0.6, 0.6, 0.05);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.0, 0.0, 1.0, 1.0);
            int states = 0;
            s.StateChanged += () => states++;
            s.ActiveLayer.Shapes.Remove(a);   // a future erase/undo; any model change event prunes
            s.SetGrid(3);
            Selected(s, "vanished shape pruned on the next model change", c);
            Check.That(states == 1, $"one StateChanged for the prune, got {states}");
        }

        // ================= API =================

        public static void V_SelectionApi()
        {
            var b = TwoLayerBoard(out var bottom, out var top);
            var c = AddCircle(b, top, 0.6, 0.6, 0.1);
            var l1 = AddLine(b, bottom, 0.1, 0.2, 0.3, 0.25);
            var l2 = AddLine(b, bottom, 0.35, 0.25, 0.4, 0.3);
            var other = AddLine(b, top, 0.9, 0.9, 0.95, 0.95);
            var s = SelectSession(b);
            int states = 0, boards = 0;
            s.StateChanged += () => states++;
            s.BoardChanged += _ => boards++;

            Check.That(!s.TryGetSelectionBounds(out _), "no bounds for an empty selection");
            var list = new List<EditorSession.SelectedShape> { default, default };
            s.GetSelectedShapes(list);
            Check.That(list.Count == 0, "GetSelectedShapes clears the list");

            s.SetSelection(new[] { c.Id, l2.Id, l1.Id, new EntityId(999) });
            Selected(s, "SetSelection keeps only existing ids", c, l1, l2);
            Check.That(states == 1, $"one StateChanged, got {states}");
            s.SetSelection(new List<EntityId> { l1.Id, l2.Id, c.Id });
            Check.That(states == 1, "same set again: no StateChanged");

            s.GetSelectedShapes(list);
            Check.That(list.Count == 3, $"three selected shapes, got {list.Count}");
            Check.That(list[0].Shape == l1 && list[0].Layer == bottom, "bottom layer first, in shape order (l1)");
            Check.That(list[1].Shape == l2 && list[1].Layer == bottom, "then l2");
            Check.That(list[2].Shape == c && list[2].Layer == top, "then the top layer's circle");

            Check.That(s.TryGetSelectionBounds(out BoardRect r), "bounds");
            Rect(r, 0.1, 0.2, 0.7, 0.7, "union of the line and circle bounds");

            bottom.Visible = false;   // direct model edit: no event, so no prune yet
            s.GetSelectedShapes(list);
            Check.That(list.Count == 1 && list[0].Shape == c, "GetSelectedShapes lists visible layers only");
            s.SetSelection(new[] { l1.Id, other.Id });
            Selected(s, "SetSelection drops ids on hidden layers", other);
            bottom.Visible = true;

            states = 0;
            s.ClearSelection();
            Check.That(s.SelectionCount == 0 && states == 1, "ClearSelection: one StateChanged");
            s.ClearSelection();
            Check.That(states == 1, "ClearSelection when empty: no StateChanged");
            Check.That(boards == 0, "the API never raises BoardChanged");
        }

        public static void V_SelectNeverEditsTheModel()
        {
            var board = GoldenBoards.StarOnFullBoard();
            var s = new EditorSession(board);
            int boards = 0;
            s.BoardChanged += _ => boards++;
            int shapes = ShapeCount(s), nextId = board.NextId;
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.0, 0.0, 1.0, 1.0);
            Check.That(s.SelectionCount == shapes, $"every shape of the star board fits the full board ({s.SelectionCount}/{shapes})");
            Drag(s, 0.3, 0.3, 0.31, 0.31);
            Drag(s, 0.5, 0.5, 0.505, 0.5);
            Tap(s, 0.5, 0.5);
            Tap(s, 0.02, 0.97);
            s.Pointer(Ev(PointerPhase.Down, 0.2, 0.2));
            s.Pointer(Ev(PointerPhase.Move, 0.8, 0.8));
            s.Pointer(Ev(PointerPhase.Cancel, 0.8, 0.8, on: false));
            s.SetSelection(new[] { board.Layers[0].Shapes[0].Id });
            s.ClearSelection();
            s.SetTool(ToolId.Line);
            Check.That(boards == 0, $"no BoardChanged from selecting, got {boards}");
            Check.That(ShapeCount(s) == shapes && board.NextId == nextId, "no shape added or removed, no id used");
            Check.That(!s.CanUndo, "no undo entry");
        }

        public static void V_LineToolStillDrawsAndSnapsAfterSelect()
        {
            var s = new EditorSession();
            AddLine(s.Board, s.ActiveLayer, 0.1, 0.8, 0.3, 0.9);
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.0, 0.7, 0.4, 1.0);
            s.SetTool(ToolId.Line);
            Drag(s, 0.1, 0.12, 0.503, 0.498);
            var line = s.ActiveLayer.Shapes[s.ActiveLayer.Shapes.Count - 1] as LineShape;
            Check.That(ShapeCount(s) == 2 && line != null, "a line committed");
            Check.That(line.B == new Vec2(0.5, 0.5), "end snapped to the grid corner exactly");
            Tap(s, 0.252, 0.247);
            Tap(s, 0.748, 0.253);
            Check.That(ShapeCount(s) == 3, "tap-to-connect still commits");
            s.SetTool(ToolId.Circle);
            Drag(s, 0.5, 0.5, 0.6, 0.5);
            Check.That(ShapeCount(s) == 4 && s.ActiveLayer.Shapes[3] is CircleShape, "Circle still commits");
        }

        // ================= allocation =================

        static int s_states, s_previews;

        public static void V_SelectMovePathAllocatesNothing()
        {
            foreach (bool withMetric in new[] { false, true })
            {
                var board = GoldenBoards.StarOnFullBoard();
                var top = board.Layers[board.Layers.Count - 1];
                for (int i = 0; i < 12; i++)
                {
                    AddLine(board, top, 0.05 + 0.07 * i, 0.05, 0.08 + 0.07 * i, 0.12);
                    AddCircle(board, top, 0.08 + 0.07 * i, 0.9, 0.02);
                }
                var s = new EditorSession(board);
                if (withMetric) s.SnapMetric = new PerspectiveMetric { Tol = 24 };
                s.StateChanged += () => s_states++;
                s.PreviewChanged += () => s_previews++;
                s.SetTool(ToolId.SelectMove);
                Drag(s, 0.0, 0.0, 1.0, 1.0, 40);   // warm-up: every shape selected, sets at full size
                Drag(s, 0.02, 0.02, 0.6, 0.98, 40);
                Tap(s, 0.5, 0.5);

                s.Pointer(Ev(PointerPhase.Down, 0.01, 0.01));
                var moves = new PointerEvent[200];
                for (int i = 0; i < moves.Length; i++)
                {
                    // The moving corner sweeps the board: the enclosed set grows and shrinks again and again.
                    double t = i / 199.0;
                    moves[i] = new PointerEvent(PointerPhase.Move,
                        new Vec2(0.15 + 0.84 * Math.Abs(Math.Sin(5 * t)), 0.15 + 0.84 * Math.Abs(Math.Sin(3 * t + 0.4))), true, i);
                }
                s.Pointer(moves[0]);
                int states0 = s_states, previews0 = s_previews;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < moves.Length; i++) s.Pointer(moves[i]);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                int changes = s_states - states0, previews = s_previews - previews0;
                s.Pointer(Ev(PointerPhase.Up, 0.9, 0.5));
                Check.That(changes > 10, $"metric {withMetric}: the sweep changed the live selection often ({changes})");
                Check.That(previews > 100, $"metric {withMetric}: the rectangle followed ({previews})");
                Check.That(bytes == 0, $"metric {withMetric}: 200 Select Moves allocated {bytes} bytes");
            }
        }
    }
}
