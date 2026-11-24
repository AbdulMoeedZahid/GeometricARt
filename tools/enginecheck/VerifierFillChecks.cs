#if VERIFIER_FILL   // Fill is paused (user decision): define VERIFIER_FILL to run these again when Fill resumes
using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER (decision 6, P6): the Fill tool, written from the decision text and FILL.md's contract.
    ///   Regions   closed faces of the active layer's lines, stamps and circles (tessellated); the frame closes a region
    ///             only together with a shape; spurs and trees close nothing; near-misses beyond the weld leave a gap.
    ///   Triangles area(triangles) == region area, no two triangles overlap, holes stay empty, pinches handled.
    ///   Steps     one per fill / refill / clear; a same-colour tap and a tap outside add none; erase resets on leaving
    ///             Fill; seeds survive Undo, Redo, DeepClone and follow their shapes after an edit.
    /// </summary>
    public static class VerifierFillChecks
    {
        static Vec2 V(double x, double y) => new Vec2(x, y);
        static double s_T;
        static PointerEvent Ev(PointerPhase p, Vec2 v) => new PointerEvent(p, v, true, s_T += 0.016);

        static int s_id = 5000;
        static LineShape Ln(Vec2 a, Vec2 b) => new LineShape(new EntityId(++s_id), a, b, StrokeStyle.Default);

        static void Square(Layer l, double x0, double y0, double x1, double y1)
        {
            l.Shapes.Add(Ln(V(x0, y0), V(x1, y0)));
            l.Shapes.Add(Ln(V(x1, y0), V(x1, y1)));
            l.Shapes.Add(Ln(V(x1, y1), V(x0, y1)));
            l.Shapes.Add(Ln(V(x0, y1), V(x0, y0)));
        }

        static Layer NewLayer() => new Layer(new EntityId(++s_id), "fill");

        static bool Closed(Layer l, Vec2 p) => FillRegions.Build(l).Locate(p) >= 0;

        static List<string> s_toasts = new List<string>();

        static EditorSession Session(Action<Layer> build)
        {
            var b = GoldenBoards.Empty();
            build(b.Layers[0]);
            var s = new EditorSession(b);
            s.SetSnapEnabled(false);
            s.Toast += m => s_toasts.Add(m);
            s.SetTool(ToolId.Fill);
            return s;
        }

        static void Tap(EditorSession s, Vec2 p)
        {
            s.Pointer(Ev(PointerPhase.Down, p));
            s.Pointer(Ev(PointerPhase.Up, p));
        }

        // ================= region classification =================

        public static void V_ClosedSquareInsideOutside()
        {
            var l = NewLayer();
            Square(l, 0.3, 0.3, 0.7, 0.7);
            Check.That(Closed(l, V(0.5, 0.5)), "inside the square: a region");
            Check.That(!Closed(l, V(0.1, 0.1)), "around a floating square: not a region (frame alone never closes)");
            Check.That(!Closed(NewLayer(), V(0.5, 0.5)), "empty layer: nothing");
        }

        public static void V_NearMissGapBeyondTheWeldIsOpen()
        {
            foreach (var (gap, closed) in new[] { (0.0, true), (5e-6, true), (5e-5, false), (1e-3, false) })
            {
                var l = NewLayer();
                l.Shapes.Add(Ln(V(0.3, 0.3), V(0.7, 0.3)));
                l.Shapes.Add(Ln(V(0.7, 0.3), V(0.7, 0.7)));
                l.Shapes.Add(Ln(V(0.7, 0.7), V(0.3, 0.7)));
                l.Shapes.Add(Ln(V(0.3, 0.7), V(0.3, 0.3 + gap)));   // the last side stops short of the corner
                Check.That(Closed(l, V(0.5, 0.5)) == closed, $"corner gap {gap}: closed {closed}");
            }
        }

        public static void V_LinesEndingOnACircleSplitIt()
        {
            // A chord at a non-division angle: its ends are on the circle but not tessellation vertices.
            Vec2 c = V(0.5, 0.5);
            double r = 0.2, a0 = 0.3, a1 = 2.5;
            var l = NewLayer();
            l.Shapes.Add(new CircleShape(new EntityId(++s_id), c, r, StrokeStyle.Default));
            l.Shapes.Add(Ln(V(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), V(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1))));
            var f = FillRegions.Build(l);
            int small = f.Locate(V(0.5, 0.68)), big = f.Locate(V(0.5, 0.4));
            Check.That(small >= 0 && big >= 0 && small != big, $"the chord splits the disc into two regions ({small}, {big})");
            double total = f.RegionArea(small) + f.RegionArea(big);
            Check.Near(total, Math.PI * r * r, "the two pieces add up to the disc", 1e-4);
            double seg = 0.5 * r * r * ((a1 - a0) - Math.Sin(a1 - a0));
            Check.Near(f.RegionArea(small), seg, "circular segment area", 1e-4);
            Check.That(!Closed(l, V(0.1, 0.9)), "outside the circle: not a region");
        }

        public static void V_SpursAndTreesCloseNothing()
        {
            var spur = NewLayer();
            spur.Shapes.Add(Ln(V(0, 0.5), V(0.6, 0.5)));                    // from the board edge into the middle
            spur.Shapes.Add(Ln(V(0.3, 0.5), V(0.3, 0.8)));                  // a tree
            Check.That(!Closed(spur, V(0.2, 0.7)) && !Closed(spur, V(0.5, 0.2)), "a tree from the frame closes nothing");
            var across = NewLayer();
            across.Shapes.Add(Ln(V(0, 0.4), V(1, 0.6)));                    // frame to frame
            Check.That(Closed(across, V(0.5, 0.8)) && Closed(across, V(0.5, 0.2)), "a line across the board: two regions with the frame");
            var inside = NewLayer();
            Square(inside, 0.2, 0.2, 0.8, 0.8);
            inside.Shapes.Add(Ln(V(0.2, 0.5), V(0.5, 0.5)));                // a spur into the square
            inside.Shapes.Add(Ln(V(0.5, 0.5), V(0.5, 0.65)));
            var f = FillRegions.Build(inside);
            int face = f.Locate(V(0.6, 0.3));
            Check.That(face >= 0 && f.Locate(V(0.35, 0.6)) == face, "a spur inside a square does not split it");
            Check.Near(f.RegionArea(face), 0.36, "the spur has no area", 1e-9);
            var tris = new List<Vec2>();
            f.Triangulate(face, tris);
            Check.Near(TriArea(tris), 0.36, "triangulated area with the spur inside", 1e-9);
        }

        public static void V_StarFacesAndBandPieces()
        {
            var b = GoldenBoards.StarOnFullBoard();
            var star = b.FindLayer(b.ActiveLayerId);
            var f = FillRegions.Build(star);
            Vec2 c = GoldenBoards.BoardCenter;
            Check.That(f.Locate(c) >= 0, "the centre octagon");
            Vec2 p1 = GoldenBoards.StarPoint(c, GoldenBoards.StarRadius, 1);
            Vec2 tip = V(p1.X - 0.02, p1.Y - 0.02);
            Check.That(f.Locate(tip) >= 0 && f.Locate(tip) != f.Locate(c), "a tip triangle is its own region");
            Check.That(f.Locate(V(0.05, 0.05)) < 0, "around the star: no region");
            // Banded: Double is one lattice (crossing squares open), Interlace splits it into pieces.
            LayerBands d = LayerBands.Default; d.On = true; d.Width = 0.02;
            star.Bands = d;
            var fd = FillRegions.Build(star);
            Vec2 onBandA = V(c.X, c.Y + GoldenBoards.StarRadius * CircleDivisions.Diagonal);              // middle of B's top edge
            Vec2 onBandB = V(c.X + GoldenBoards.StarRadius * CircleDivisions.Diagonal, c.Y);              // middle of B's right edge
            Check.That(fd.Locate(onBandA) >= 0 && fd.Locate(onBandA) == fd.Locate(onBandB), "Double: the band lattice is one region");
            Check.That(fd.Locate(c) >= 0 && fd.Locate(c) != fd.Locate(onBandA), "the octagon inside the bands is separate");
            d.Mode = BandMode.Interlace;
            star.Bands = d;
            var fi = FillRegions.Build(star);
            Check.That(fi.Locate(onBandA) >= 0 && fi.Locate(onBandA) != fi.Locate(onBandB), "Interlace: bridges split the lattice into pieces");
        }

        // ================= triangulation =================

        static double TriArea(List<Vec2> t)
        {
            double a = 0;
            for (int i = 0; i + 2 < t.Count; i += 3)
                a += 0.5 * ((t[i + 1].X - t[i].X) * (t[i + 2].Y - t[i].Y) - (t[i + 2].X - t[i].X) * (t[i + 1].Y - t[i].Y));
            return a;
        }

        static bool InTri(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
        {
            double d1 = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            double d2 = (c.X - b.X) * (p.Y - b.Y) - (c.Y - b.Y) * (p.X - b.X);
            double d3 = (a.X - c.X) * (p.Y - c.Y) - (a.Y - c.Y) * (p.X - c.X);
            const double e = 1e-12;
            return (d1 > e && d2 > e && d3 > e) || (d1 < -e && d2 < -e && d3 < -e);   // strictly inside
        }

        /// <summary>Area equality, every triangle CCW and non-degenerate-sum, no point strictly inside two triangles, coverage = region.</summary>
        static void CheckTriangulation(FillRegions f, int face, Func<Vec2, bool> inRegion, double expectedArea, string what)
        {
            var t = new List<Vec2>();
            f.Triangulate(face, t);
            Check.That(t.Count % 3 == 0 && t.Count > 0, $"{what}: triangles");
            Check.Near(TriArea(t), expectedArea, $"{what}: triangulated area", 1e-9);
            Check.Near(f.RegionArea(face), expectedArea, $"{what}: region area", 1e-9);
            for (int i = 0; i < t.Count; i += 3)
            {
                double a = 0.5 * ((t[i + 1].X - t[i].X) * (t[i + 2].Y - t[i].Y) - (t[i + 2].X - t[i].X) * (t[i + 1].Y - t[i].Y));
                Check.That(a >= -1e-15, $"{what}: triangle {i / 3} is clockwise ({a})");
            }
            var rnd = new Random(31);
            for (int k = 0; k < 4000; k++)
            {
                var p = V(rnd.NextDouble(), rnd.NextDouble());
                int n = 0;
                for (int i = 0; i < t.Count; i += 3) if (InTri(p, t[i], t[i + 1], t[i + 2])) n++;
                Check.That(n <= 1, $"{what}: {P(p)} is inside {n} triangles (overlap)");
                if (n == 1) Check.That(inRegion(p), $"{what}: {P(p)} is covered but outside the region (a hole filled?)");
            }
        }

        static string P(Vec2 v) => $"({v.X:R}, {v.Y:R})";

        public static void V_TriangulationWithManyHoles()
        {
            var l = NewLayer();
            Square(l, 0.1, 0.1, 0.9, 0.9);
            var holes = new List<(double, double, double, double)>();
            for (int j = 0; j < 5; j++)
                for (int i = 0; i < 5; i++)
                {
                    double x = 0.15 + 0.15 * i, y = 0.15 + 0.15 * j, s = 0.06 + 0.01 * ((i + j) % 3);
                    Square(l, x, y, x + s, y + s);
                    holes.Add((x, y, x + s, y + s));
                }
            l.Shapes.Add(new CircleShape(new EntityId(++s_id), V(0.5, 0.5), 0.0, StrokeStyle.Default));   // degenerate: ignored
            var f = FillRegions.Build(l);
            int face = f.Locate(V(0.13, 0.5));
            Check.That(face >= 0, "the frame square with 25 holes is a region");
            double area = 0.64;
            foreach (var h in holes) area -= (h.Item3 - h.Item1) * (h.Item4 - h.Item2);
            CheckTriangulation(f, face, p =>
            {
                if (p.X <= 0.1 || p.X >= 0.9 || p.Y <= 0.1 || p.Y >= 0.9) return false;
                foreach (var h in holes) if (p.X > h.Item1 && p.X < h.Item3 && p.Y > h.Item2 && p.Y < h.Item4) return false;
                return true;
            }, area, "25 holes");
            Check.That(f.Locate(V(0.18, 0.18)) >= 0 && f.Locate(V(0.18, 0.18)) != face, "each hole is its own region");
        }

        public static void V_TriangulationOfPinchedRegions()
        {
            // An inner square touching the outer one at a corner: the ring between them is pinched at that vertex.
            var l = NewLayer();
            Square(l, 0.2, 0.2, 0.8, 0.8);
            Square(l, 0.2, 0.2, 0.5, 0.5);
            var f = FillRegions.Build(l);
            int ring = f.Locate(V(0.7, 0.7));
            Check.That(ring >= 0, "the L-shaped ring is a region");
            CheckTriangulation(f, ring, p => p.X > 0.2 && p.X < 0.8 && p.Y > 0.2 && p.Y < 0.8 && !(p.X < 0.5 && p.Y < 0.5), 0.36 - 0.09, "corner-touching hole");
            // A hole touching the outer boundary at one vertex from inside (diamond hole on the edge midpoint).
            var l2 = NewLayer();
            Square(l2, 0.2, 0.2, 0.8, 0.8);
            l2.Shapes.Add(Ln(V(0.5, 0.2), V(0.6, 0.35)));
            l2.Shapes.Add(Ln(V(0.6, 0.35), V(0.5, 0.5)));
            l2.Shapes.Add(Ln(V(0.5, 0.5), V(0.4, 0.35)));
            l2.Shapes.Add(Ln(V(0.4, 0.35), V(0.5, 0.2)));
            var f2 = FillRegions.Build(l2);
            int r2 = f2.Locate(V(0.7, 0.7));
            Func<Vec2, bool> inDiamond = p => Math.Abs(p.X - 0.5) / 0.1 + Math.Abs(p.Y - 0.35) / 0.15 < 1;
            CheckTriangulation(f2, r2, p => p.X > 0.2 && p.X < 0.8 && p.Y > 0.2 && p.Y < 0.8 && !inDiamond(p), 0.36 - 0.03, "hole pinched to the outer edge");
            // Two squares sharing one corner (bowtie): two separate regions.
            var l3 = NewLayer();
            Square(l3, 0.2, 0.2, 0.5, 0.5);
            Square(l3, 0.5, 0.5, 0.8, 0.8);
            var f3 = FillRegions.Build(l3);
            Check.That(f3.Locate(V(0.3, 0.3)) >= 0 && f3.Locate(V(0.7, 0.7)) >= 0 && f3.Locate(V(0.3, 0.3)) != f3.Locate(V(0.7, 0.7)),
                "corner-sharing squares: two regions");
            CheckTriangulation(f3, f3.Locate(V(0.3, 0.3)), p => p.X > 0.2 && p.X < 0.5 && p.Y > 0.2 && p.Y < 0.5, 0.09, "bowtie half");
        }

        public static void V_TriangulationOfStarAndTiledFaces()
        {
            foreach (var b in new[] { GoldenBoards.StarOnFullBoard(), GoldenBoards.Tiled2x2() })
            {
                var layer = b.FindLayer(b.ActiveLayerId);
                var f = FillRegions.Build(layer);
                for (int face = 0; face < f.FaceCount; face++)
                {
                    if (!f.IsRegion(face)) continue;
                    var t = new List<Vec2>();
                    f.Triangulate(face, t);
                    Check.Near(TriArea(t), f.RegionArea(face), $"{layer.Name} face {face}: triangle area == region area", 1e-12);
                }
            }
        }

        // ================= the tool: steps, colours, erase =================

        public static void V_OneStepPerFillAndNoneForTheSameColour()
        {
            var s = Session(l => Square(l, 0.3, 0.3, 0.7, 0.7));
            var layer = s.ActiveLayer;
            s.Style = s.Style.WithColor(Palette.Red);
            Tap(s, V(0.5, 0.5));
            Check.That(layer.Fills.Count == 1 && layer.Fills[0].Color.Equals(Palette.Red), "filled red");
            Check.That(s.UndoCount == 1 && s.NextUndoLabel == "Fill", $"one \"Fill\" step, got {s.UndoCount} {s.NextUndoLabel}");
            Tap(s, V(0.45, 0.55));
            Check.That(s.UndoCount == 1 && layer.Fills.Count == 1, "same colour again elsewhere in the region: no step, no second seed");
            s.Style = s.Style.WithColor(Palette.Blue);
            Tap(s, V(0.55, 0.45));
            Check.That(s.UndoCount == 2 && layer.Fills.Count == 1 && layer.Fills[0].Color.Equals(Palette.Blue), "refill: replaces, one step");
            s_toasts.Clear();
            Tap(s, V(0.1, 0.1));
            Check.That(s.UndoCount == 2 && s_toasts.Contains(EditorSession.FillOutsideToast), "outside: toast, no step");
            s.Pointer(Ev(PointerPhase.Down, V(0.4, 0.4)));
            s.Pointer(Ev(PointerPhase.Move, V(0.6, 0.6)));
            s.Pointer(Ev(PointerPhase.Up, V(0.6, 0.6)));
            Check.That(s.UndoCount == 2 && s.ActiveLayer.Shapes.Count == 4, "a drag in Fill does nothing");
        }

        public static void V_EraseClearsOneStepAndResetsOnLeavingFill()
        {
            var s = Session(l => { Square(l, 0.2, 0.2, 0.5, 0.5); Square(l, 0.5, 0.5, 0.8, 0.8); });
            var layer = s.ActiveLayer;
            Tap(s, V(0.3, 0.3));
            Tap(s, V(0.7, 0.7));
            int steps = s.UndoCount;
            s.SetFillErase(true);
            Check.That(s.FillErase, "no fill on");
            Tap(s, V(0.3, 0.3));
            Check.That(layer.Fills.Count == 1 && s.UndoCount == steps + 1 && s.NextUndoLabel != "Fill", "clear: one step, the other fill stays");
            s_toasts.Clear();
            Tap(s, V(0.3, 0.3));
            Check.That(s.UndoCount == steps + 1 && s_toasts.Contains(EditorSession.NothingToClearToast), "clear an empty region: toast, no step");
            s.SetTool(ToolId.Line);
            Check.That(!s.FillErase, "leaving Fill resets no fill");
            s.SetTool(ToolId.Fill);
            Tap(s, V(0.3, 0.3));
            Check.That(layer.Fills.Count == 2, "back in Fill: taps fill again");
        }

        public static void V_HiddenActiveLayerRefuses()
        {
            var s = Session(l => Square(l, 0.3, 0.3, 0.7, 0.7));
            s.SetLayerVisible(s.ActiveLayer.Id, false);
            int steps = s.UndoCount;
            s_toasts.Clear();
            Tap(s, V(0.5, 0.5));
            Check.That(s.UndoCount == steps && s.ActiveLayer.Fills.Count == 0 && s_toasts.Contains(EditorSession.HiddenLayerToast),
                "hidden active layer: hidden-layer toast, nothing filled");
        }

        public static void V_SeedsSurviveUndoRedoCloneAndFollowEdits()
        {
            var s = Session(l => Square(l, 0.3, 0.3, 0.7, 0.7));
            var id = s.ActiveLayer.Id;
            Tap(s, V(0.5, 0.5));
            var seed = s.ActiveLayer.Fills[0];
            Check.That(s.Board.DeepClone().FindLayer(id).Fills.Count == 1, "Board.DeepClone copies fills");
            var clone = s.ActiveLayer.DeepClone();
            clone.Fills.Clear();
            Check.That(s.ActiveLayer.Fills.Count == 1, "the clone's list is its own");
            s.Undo();
            Check.That(s.Board.FindLayer(id).Fills.Count == 0, "Undo removes the fill");
            s.Redo();
            var back = s.Board.FindLayer(id).Fills;
            Check.That(back.Count == 1 && back[0].Point == seed.Point && back[0].Color.Equals(seed.Color), "Redo brings the exact seed back");
            // Move the whole square: the seed's point stays, so it now lies outside and draws nothing (kept, not deleted).
            s.SetTool(ToolId.SelectMove);
            var ids = new List<EntityId>();
            foreach (var sh in s.Board.FindLayer(id).Shapes) ids.Add(sh.Id);
            s.SetSelection(ids);
            s.SetTool(ToolId.Move);
            s.Pointer(Ev(PointerPhase.Down, V(0.3, 0.3)));
            for (int i = 1; i <= 6; i++) s.Pointer(Ev(PointerPhase.Move, V(0.3 + 0.0 * i, 0.3 - 0.04 * i)));
            s.Pointer(Ev(PointerPhase.Up, V(0.3, 0.06)));
            var layer = s.Board.FindLayer(id);
            Check.That(layer.Fills.Count == 1, "an edit never deletes seeds");
            var f = FillRegions.Build(layer);
            Check.That(f.Locate(layer.Fills[0].Point) >= 0 == (layer.Fills[0].Point.Y < 0.7 - 0.24 + 1e-9), "the seed resolves against the moved square");
        }

        public static void V_MergedRegionsKeepBothSeedsAndOneTapUnifies()
        {
            // Two filled halves; deleting the dividing line merges them: both seeds are in one region. A tap in the
            // current colour should then not need two fills to agree (one step that leaves one seed).
            var s = Session(l => { Square(l, 0.2, 0.2, 0.8, 0.8); l.Shapes.Add(Ln(V(0.5, 0.2), V(0.5, 0.8))); });
            var layer = s.ActiveLayer;
            s.Style = s.Style.WithColor(Palette.Red);
            Tap(s, V(0.3, 0.5));
            s.Style = s.Style.WithColor(Palette.Blue);
            Tap(s, V(0.7, 0.5));
            layer.Shapes.RemoveAt(4);                                  // engine-side edit stand-in (Delete arrives in P7)
            s.LoadBoard(s.Board);
            var f = FillRegions.Build(s.ActiveLayer);
            Check.That(f.Locate(V(0.3, 0.5)) == f.Locate(V(0.7, 0.5)), "precondition: merged");
            Check.That(s.ActiveLayer.Fills.Count == 2, "both seeds kept");
            s.SetTool(ToolId.Fill);
            Tap(s, V(0.5, 0.5));                                      // blue, the colour of one of the two seeds
            Check.That(s.ActiveLayer.Fills.Count == 1 && s.ActiveLayer.Fills[0].Color.Equals(Palette.Blue), "one tap unifies the region");
        }
    }
}
#endif
