using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;
using static GeoART.EngineCheck.BandKit;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Fill (decision 6): which regions are fillable (closed by shapes, or by shapes and the board edge; never the bare
    /// board, the area around a floating star or a spur from the edge), holes, circles closing with lines that end on
    /// them, band lattices (one region flat, pieces in interlace), triangulation (area equals the region's), and the Fill
    /// tool: fill, replace, no-op, clear, outside/hidden toasts, undo, seeds re-resolving after a Move.
    /// </summary>
    public static class FillChecks
    {
        static Vec2 V(double x, double y) => new Vec2(x, y);

        static PlanarSegment[] Square(double x0, double y0, double x1, double y1, int src = 0) => new[]
        {
            S(x0, y0, x1, y0, src), S(x1, y0, x1, y1, src), S(x1, y1, x0, y1, src), S(x0, y1, x0, y0, src),
        };

        static FillRegions Regions(params PlanarSegment[][] parts)
        {
            var all = new List<PlanarSegment>();
            foreach (var p in parts) all.AddRange(p);
            return FillRegions.Build(all);
        }

        static double TriangleArea(List<Vec2> t)
        {
            double a = 0;
            for (int i = 0; i + 2 < t.Count; i += 3)
                a += ((t[i + 1].X - t[i].X) * (t[i + 2].Y - t[i].Y) - (t[i + 1].Y - t[i].Y) * (t[i + 2].X - t[i].X)) * 0.5;
            return a;
        }

        static void AssertTriangulationExact(FillRegions r, string what)
        {
            for (int f = 0; f < r.FaceCount; f++)
            {
                if (!r.IsRegion(f)) continue;
                var tris = new List<Vec2>();
                r.Triangulate(f, tris);
                Check.That(tris.Count % 3 == 0 && tris.Count > 0, $"{what}: face {f} triangulated");
                Check.Near(TriangleArea(tris), r.RegionArea(f), $"{what}: face {f} triangles cover the region exactly", 1e-9);
            }
        }

        public static void SquareIsARegion_OutsideIsNot()
        {
            var r = Regions(Square(0.4, 0.4, 0.6, 0.6));
            int f = r.Locate(V(0.5, 0.5));
            Check.That(f >= 0, "inside the square: a region");
            Check.Near(r.RegionArea(f), 0.04, "its area", 1e-12);
            Check.That(r.Locate(V(0.2, 0.2)) < 0, "outside a floating square: not fillable");
            Check.That(FillRegions.Build(new List<PlanarSegment>()).Locate(V(0.5, 0.5)) < 0, "the bare board is not fillable");
            AssertTriangulationExact(r, "square");
        }

        public static void FrameClosesRegionsOnlyTogetherWithShapes()
        {
            var half = Regions(new[] { S(0, 0.5, 1, 0.5) });
            Check.That(half.Locate(V(0.5, 0.25)) >= 0 && half.Locate(V(0.5, 0.75)) >= 0, "a line across the board: two halves");
            Check.That(half.Locate(V(0.5, 0.25)) != half.Locate(V(0.5, 0.75)), "different halves");
            var spur = Regions(new[] { S(0, 0.5, 0.3, 0.5) });
            Check.That(spur.Locate(V(0.5, 0.5)) < 0 && spur.Locate(V(0.1, 0.4)) < 0, "a spur from the edge closes nothing");
            var floating = Regions(new[] { S(0.3, 0.5, 0.7, 0.5) });
            Check.That(floating.Locate(V(0.5, 0.6)) < 0, "a floating line closes nothing");
            AssertTriangulationExact(half, "halves");
        }

        public static void StarFacesAndTiledGaps()
        {
            var star = FillRegions.Build(StarLayer());
            int regions = 0;
            for (int f = 0; f < star.FaceCount; f++) if (star.IsRegion(f)) regions++;
            Check.That(regions == 9, $"star: the octagon and 8 tips (got {regions})");
            Check.That(star.Locate(V(0.05, 0.05)) < 0, "around the floating star: not fillable");
            AssertTriangulationExact(star, "star");

            var tiled = FillRegions.Build(TiledLayer());
            Check.That(tiled.Locate(V(0.5, 0.5)) >= 0, "the gap between four stars (touching the frame) is fillable");
            Check.That(tiled.Locate(V(0.02, 0.02)) >= 0, "and so is a corner gap closed by the board edge");
            AssertTriangulationExact(tiled, "tiled");
        }

        public static void ComponentsInsideAreHoles()
        {
            var r = Regions(Square(0.2, 0.2, 0.8, 0.8), Square(0.4, 0.4, 0.6, 0.6, 1), new[] { S(0.25, 0.3, 0.35, 0.3, 2) });
            int ring = r.Locate(V(0.3, 0.5)), inner = r.Locate(V(0.5, 0.5));
            Check.That(ring >= 0 && inner >= 0 && ring != inner, "ring and inner square are separate regions");
            Check.Near(r.RegionArea(ring), 0.36 - 0.04, "the ring's area excludes the inner square", 1e-12);
            Check.That(r.Locate(V(0.3, 0.3)) == ring, "a floating line inside does not split the ring");
            AssertTriangulationExact(r, "ring");
        }

        public static void LinesEndingOnACircleCloseRegions()
        {
            // Two lines from outside end on the circle at +-20 degrees (on the arc, not at a tessellation vertex).
            var c = V(0.45, 0.5);
            const double r = 0.3;
            Vec2 a = V(c.X + r * Math.Cos(Math.PI / 9), c.Y + r * Math.Sin(Math.PI / 9));
            Vec2 b = V(c.X + r * Math.Cos(-Math.PI / 9), c.Y + r * Math.Sin(-Math.PI / 9));
            var layer = new Layer(new EntityId(1), "L");
            layer.Shapes.Add(new CircleShape(new EntityId(2), c, r, StrokeStyle.Default));
            layer.Shapes.Add(new LineShape(new EntityId(3), V(0.95, 0.5), a, StrokeStyle.Default));
            layer.Shapes.Add(new LineShape(new EntityId(4), V(0.95, 0.5), b, StrokeStyle.Default));
            var regions = FillRegions.Build(layer);
            int wedge = regions.Locate(V(0.82, 0.5));
            Check.That(wedge >= 0, "the wedge between the lines and the arc is closed");
            Check.That(regions.Locate(V(0.45, 0.5)) >= 0 && regions.Locate(V(0.45, 0.5)) != wedge, "the disc is its own region");
            Check.That(regions.Locate(V(0.95, 0.9)) < 0, "around them: not fillable");
            Check.That(FillRegions.CircleSegments(0.5) % 8 == 0 && FillRegions.CircleSegments(0.5) <= FillRegions.MaxCircleSegments,
                "a multiple of 8 segments, capped");
            Check.That(0.5 * (1 - Math.Cos(Math.PI / FillRegions.CircleSegments(0.5))) <= FillRegions.ChordError + 1e-15,
                "chord error within budget at r = 0.5");
            AssertTriangulationExact(regions, "circle wedge");
        }

        public static void FlatBandsAreOneLatticeInterlacePiecesAreSeparate()
        {
            var flat = StarLayer();
            flat.Bands = new LayerBands { On = true, Width = 0.015 };
            var r = FillRegions.Build(flat);
            // Points on the band strips (on the structure lines, between the two outlines) at two different sides.
            var segs = SegmentsOf(StarLayer());
            Vec2 p1 = (segs[0].A + segs[0].B) * 0.5, p2 = (segs[5].A + segs[5].B) * 0.5;
            int a = r.Locate(p1), b = r.Locate(p2);
            Check.That(a >= 0 && a == b, "flat Double: one connected band lattice");
            Check.That(r.Locate(V(0.5, 0.5)) >= 0 && r.Locate(V(0.5, 0.5)) != a, "the octagon is its own region");
            AssertTriangulationExact(r, "flat lattice");

            var woven = StarLayer();
            woven.Bands = new LayerBands { On = true, Width = 0.015, Mode = BandMode.Interlace };
            var w = FillRegions.Build(woven);
            int pieces = 0;
            var seen = new HashSet<int>();
            foreach (var s in segs)
            {
                int f = w.Locate((s.A + s.B) * 0.5);
                Check.That(f >= 0, "every band piece is a region");
                if (seen.Add(f)) pieces++;
            }
            Check.That(pieces > 1, $"interlace: the band is split into pieces (got {pieces})");
            AssertTriangulationExact(w, "interlace pieces");
        }

        // ---------- the Fill tool ----------

        static EditorSession SquareSession(out LineShape left)
        {
            var s = new EditorSession();
            var sq = Square(0.4, 0.4, 0.6, 0.6);
            LineShape first = null;
            foreach (var seg in sq)
            {
                var l = new LineShape(s.Board.NewId(), seg.A, seg.B, StrokeStyle.Default);
                first ??= l;
                s.ActiveLayer.Shapes.Add(l);
            }
            left = first;
            s.SetTool(ToolId.Fill);
            return s;
        }

        static void Tap(EditorSession s, Vec2 p)
        {
            s.Pointer(new PointerEvent(PointerPhase.Down, p, true, 0));
            s.Pointer(new PointerEvent(PointerPhase.Up, p, true, 0));
        }

        public static void FillToolFillsReplacesAndClears()
        {
            var s = SquareSession(out _);
            Check.That(s.ActiveTool == ToolId.Fill, "Fill is a real tool");
            var toasts = new List<string>();
            int fills = 0;
            s.Toast += t => toasts.Add(t);
            s.BoardChanged += k => { if ((k & BoardChangeKind.Fills) != 0) fills++; };
            s.Style = s.Style.WithColor(Palette.Red);
            Tap(s, V(0.5, 0.5));
            Check.That(s.ActiveLayer.Fills.Count == 1 && s.ActiveLayer.Fills[0].Color.Equals(Palette.Red), "filled red");
            Check.That(s.NextUndoLabel == "Fill" && fills == 1, "one 'Fill' step, BoardChanged(Fills)");
            int steps = s.UndoCount;
            Tap(s, V(0.45, 0.55));
            Check.That(s.UndoCount == steps && s.ActiveLayer.Fills.Count == 1, "same colour again: nothing");
            s.Style = s.Style.WithColor(Palette.Blue);
            Tap(s, V(0.55, 0.45));
            Check.That(s.ActiveLayer.Fills.Count == 1 && s.ActiveLayer.Fills[0].Color.Equals(Palette.Blue), "replaced by blue");
            s.SetFillErase(true);
            Tap(s, V(0.5, 0.5));
            Check.That(s.ActiveLayer.Fills.Count == 0 && s.NextUndoLabel == "Clear fill", "no fill clears it");
            steps = s.UndoCount;
            Tap(s, V(0.5, 0.5));
            Check.That(s.UndoCount == steps && toasts[toasts.Count - 1] == EditorSession.NothingToClearToast, "nothing to clear: toast, no step");
            s.SetFillErase(false);
            Tap(s, V(0.1, 0.1));
            Check.That(toasts[toasts.Count - 1] == EditorSession.FillOutsideToast && s.UndoCount == steps, "outside: toast, no step");
            s.Undo();
            Check.That(s.ActiveLayer.Fills.Count == 1 && s.ActiveLayer.Fills[0].Color.Equals(Palette.Blue), "Undo brings the fill back");
            s.Redo();
            Check.That(s.ActiveLayer.Fills.Count == 0, "Redo clears it again");
        }

        public static void FillDragsDoNothingAndFillEraseResetsOnToolSwitch()
        {
            var s = SquareSession(out _);
            s.Pointer(new PointerEvent(PointerPhase.Down, V(0.45, 0.45), true, 0));
            s.Pointer(new PointerEvent(PointerPhase.Move, V(0.55, 0.55), true, 0));
            s.Pointer(new PointerEvent(PointerPhase.Up, V(0.55, 0.55), true, 0));
            Check.That(s.ActiveLayer.Fills.Count == 0 && s.ActiveLayer.Shapes.Count == 4, "a drag neither fills nor draws");
            s.SetFillErase(true);
            s.SetTool(ToolId.Line);
            Check.That(!s.FillErase, "leaving Fill resets 'no fill'");
            s.SetTool(ToolId.Fill);
            s.SetLayerVisible(s.ActiveLayer.Id, false);
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            Tap(s, V(0.5, 0.5));
            Check.That(toasts.Count == 1 && toasts[0] == EditorSession.HiddenLayerToast && s.ActiveLayer.Fills.Count == 0, "hidden layer: toast");
        }

        public static void SeedsReResolveAfterEdits()
        {
            var s = SquareSession(out LineShape left);
            Tap(s, V(0.42, 0.5));
            Check.That(s.ActiveLayer.Fills.Count == 1, "filled");
            // Split the square with a vertical line: only the half holding the seed stays filled.
            s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), V(0.5, 0.4), V(0.5, 0.6), StrokeStyle.Default));
            var r = FillRegions.Build(s.ActiveLayer);
            int seedFace = r.Locate(s.ActiveLayer.Fills[0].Point);
            Check.That(seedFace >= 0 && seedFace == r.Locate(V(0.45, 0.5)) && seedFace != r.Locate(V(0.55, 0.5)), "the seed's half only");
            // Move the whole square away: the seed stays where it was and now resolves outside (kept, not drawn).
            s.ActiveLayer.Shapes.Clear();
            foreach (var seg in Square(0.1, 0.1, 0.2, 0.2))
                s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), seg.A, seg.B, StrokeStyle.Default));
            Check.That(FillRegions.Build(s.ActiveLayer).Locate(s.ActiveLayer.Fills[0].Point) < 0, "left behind: no region");
            Check.That(s.ActiveLayer.Fills.Count == 1, "but the seed is kept");
        }

        public static void DeepCloneCopiesFills()
        {
            var l = new Layer(new EntityId(1), "L");
            l.Fills.Add(new FillSeed(V(0.3, 0.4), Palette.Green));
            var c = l.DeepClone();
            Check.That(c.Fills.Count == 1 && c.Fills[0].Point == V(0.3, 0.4) && c.Fills[0].Color.Equals(Palette.Green), "copied");
            c.Fills.Clear();
            Check.That(l.Fills.Count == 1, "not shared");
        }

        public static void TriangulatorHandlesSpursPinchesAndHoles()
        {
            // Outer square with a spur walked out and back, a pinch, and a hole.
            var outer = new List<Vec2> { V(0, 0), V(1, 0), V(1, 0.5), V(0.7, 0.5), V(1, 0.5), V(1, 1), V(0, 1) };
            var hole = new List<Vec2> { V(0.2, 0.2), V(0.2, 0.4), V(0.4, 0.4), V(0.4, 0.2) };
            var tris = new List<Vec2>();
            Triangulator.Triangulate(new List<List<Vec2>> { outer, hole }, tris);
            Check.Near(TriangleArea(tris), 1 - 0.04, "spur removed, hole subtracted", 1e-12);
            var pinched = new List<Vec2> { V(0, 0), V(0.5, 0.5), V(1, 0), V(1, 1), V(0.5, 0.5), V(0, 1) };   // two triangles meeting at a point
            tris.Clear();
            Triangulator.Triangulate(new List<List<Vec2>> { pinched }, tris);
            Check.Near(Math.Abs(TriangleArea(tris)), 0.5, "a pinch splits into two loops", 1e-12);
        }
    }
}
