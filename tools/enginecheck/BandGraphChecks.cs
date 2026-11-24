using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;
using static GeoART.EngineCheck.BandKit;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Band topology (PlanarGraph + BandGraph) and the LayerBands model value: planarize, merge, weld, frame, caps,
    /// clipping, polygons, determinism.
    /// </summary>
    public static class BandGraphChecks
    {
        static int DegreeAt(BandGraph g, Vec2 p)
        {
            for (int v = 0; v < g.Graph.NodeCount; v++)
                if (Vec2.Distance(g.Graph.Node(v), p) < 1e-9) return g.StructureDegree(v);
            return -1;
        }

        public static void LayerBands_DefaultsAndOldValuesMeanNoBands()
        {
            Check.That(!default(LayerBands).On, "default(LayerBands) is off");
            LayerBands d = LayerBands.Default;
            Check.That(!d.On && d.Mode == BandMode.Double && d.Width == 0.015, "Default: off, Double, 6 mm");
            Check.That(!d.HideStructure && !d.InnerLines && !d.FlipWeave, "Default: structure shown, no inner lines, no flip");
            Check.That(d.OutlineColor.Equals(Palette.Black) && d.OutlineWidth == LineWidth.Thin, "Default: black Thin outlines");
            LayerBands z = default(LayerBands).Sanitized();
            Check.That(z.Width == LayerBands.DefaultWidth, "zero width reads as the default");
            Check.That(z.OutlineColor.Equals(Palette.Black), "transparent outline colour reads as black");
            Check.That(z == new LayerBands { Width = LayerBands.DefaultWidth, OutlineColor = Palette.Black }, "equality is by value");
        }

        public static void LayerBands_SanitizedClampsGarbage()
        {
            var b = new LayerBands { On = true, Mode = (BandMode)99, Width = double.NaN, OutlineWidth = (LineWidth)7 };
            LayerBands s = b.Sanitized();
            Check.That(s.On, "On kept");
            Check.That(s.Mode == BandMode.Double, "unknown mode -> Double");
            Check.That(s.Width == LayerBands.DefaultWidth, "NaN -> default width");
            Check.That(s.OutlineWidth == LineWidth.Thin, "unknown outline width -> Thin");
            Check.That(new LayerBands { Width = -3 }.Sanitized().Width == LayerBands.DefaultWidth, "negative -> default");
            Check.That(new LayerBands { Width = double.PositiveInfinity }.Sanitized().Width == LayerBands.DefaultWidth, "inf -> default");
            Check.That(new LayerBands { Width = 1 }.Sanitized().Width == LayerBands.MaxWidth, "too wide -> max");
            Check.That(new LayerBands { Width = 0.001 }.Sanitized().Width == LayerBands.MinWidth, "too narrow -> min");
            Check.That(new LayerBands { Width = 0.02, Mode = BandMode.Interlace }.Sanitized().Mode == BandMode.Interlace, "Interlace kept");
        }

        public static void SingleSegment_IsOneClosedRectangleWithSquareCaps()
        {
            var g = Graph(S(0.3, 0.5, 0.7, 0.5));
            Check.That(g.StructureEdgeCount == 1, "one structure edge");
            var o = Outline(g, 0.02);
            Check.That(o.Runs.Count == 1 && o.Runs[0].Closed, "one closed run");
            var pts = RunPoints(o, o.Runs[0]);
            Check.That(pts.Count == 4, $"a rectangle (got {pts.Count} points)");
            foreach (var c in new[] { new Vec2(0.29, 0.49), new Vec2(0.71, 0.49), new Vec2(0.71, 0.51), new Vec2(0.29, 0.51) })
                Check.That(HasPoint(o, c, 1e-12), $"corner {c} (caps extend by h)");
            Check.That(o.Collapsed == 0 && o.SelfIntersections == 0, "clean");
        }

        public static void CollinearOverlapsAndDuplicatesMergeIntoOneEdge()
        {
            var merged = Graph(S(0.2, 0.5, 0.4, 0.5), S(0.4, 0.5, 0.6, 0.5), S(0.3, 0.5, 0.8, 0.5), S(0.3, 0.5, 0.8, 0.5),
                S(0.8, 0.5, 0.25, 0.5));
            Check.That(merged.StructureEdgeCount == 1, $"one edge chain (got {merged.StructureEdgeCount})");
            var single = Graph(S(0.2, 0.5, 0.8, 0.5));
            Check.That(SameOutput(Outline(merged, 0.02), Outline(single, 0.02)), "same outline as one 0.2..0.8 line");
        }

        public static void NearJunctionWeld_JoinsUnderAndOvershoot()
        {
            foreach (double end in new[] { 0.5 - 1e-6, 0.5 + 1e-6 })
            {
                var g = Graph(S(0.5, 0.2, 0.5, 0.8), S(0.2, 0.5, end, 0.5));
                Check.That(DegreeAt(g, new Vec2(0.5, 0.5)) == 3, $"end {end}: T-junction of degree 3");
                int free = 0;
                for (int v = 0; v < g.Graph.NodeCount; v++) if (g.StructureDegree(v) == 1) free++;
                Check.That(free == 3, $"end {end}: only the three far ends are free (no stub), got {free}");
            }
        }

        public static void EndpointWeld_MergesNearbyEnds()
        {
            var g = Graph(S(0.2, 0.2, 0.5, 0.5), S(0.5 + 3e-6, 0.5 - 2e-6, 0.8, 0.2));
            Check.That(DegreeAt(g, new Vec2(0.5, 0.5)) == 2, "ends 4e-6 apart become one degree-2 corner");
        }

        public static void TJunctionAndXCrossingDegrees()
        {
            var t = Graph(S(0.2, 0.5, 0.8, 0.5), S(0.5, 0.5, 0.5, 0.8));
            Check.That(DegreeAt(t, new Vec2(0.5, 0.5)) == 3, "T-junction -> degree 3");
            var x = Graph(S(0.2, 0.5, 0.8, 0.5), S(0.5, 0.2, 0.5, 0.8));
            Check.That(DegreeAt(x, new Vec2(0.5, 0.5)) == 4, "X crossing -> degree 4");
            Check.That(x.CrossingCount == 1, "one crossing");
        }

        public static void FreeEnd_GetsACapAroundTheEnd()
        {
            var g = Graph(S(0.5, 0.2, 0.5, 0.8), S(0.5, 0.5, 0.8, 0.5));   // T with a free end at (0.8, 0.5)
            var o = Outline(g, 0.02);
            Check.That(HasPoint(o, new Vec2(0.81, 0.49)) && HasPoint(o, new Vec2(0.81, 0.51)), "cap corners h beyond the end");
            AssertWellFormed(o, "T");
        }

        public static void FrameSplitsRunsAndBandsEndFlush()
        {
            var g = Graph(S(0, 0.5, 1, 0.5));
            var o = Outline(g, 0.02);
            Check.That(o.Runs.Count == 2, $"two open runs, above and below (got {o.Runs.Count})");
            foreach (var r in o.Runs)
            {
                Check.That(!r.Closed, "open at the frame");
                var pts = RunPoints(o, r);
                Vec2 a = pts[0], b = pts[pts.Count - 1];
                Check.That((a.X == 0 && b.X == 1) || (a.X == 1 && b.X == 0), $"ends exactly on the frame ({a}, {b})");
                Check.That(Math.Abs(Math.Abs(a.Y - 0.5) - 0.01) < 1e-15, "at h from the line");
            }
        }

        public static void LinesOutsideTheBoardAreClippedOrDropped()
        {
            var g = Graph(S(-0.2, 0.5, 0.5, 0.5), S(1.2, 1.3, 1.5, 1.6), S(0.3, 0.3 + 1e-6, 0.3, 1e-6));
            Check.That(g.StructureEdgeCount == 2, $"the line wholly outside is dropped (got {g.StructureEdgeCount})");
            var o = Outline(g, 0.02);
            bool flushLeft = false, flushBottom = false;
            foreach (var p in o.Points)
            {
                Check.That(p.X >= 0 && p.Y >= 0, $"nothing outside the board ({p})");
                if (p.X == 0) flushLeft = true;
                if (p.Y == 0) flushBottom = true;
            }
            Check.That(flushLeft, "clipped line ends exactly on the left side");
            Check.That(flushBottom, "an end 1e-6 inside is welded onto the bottom side");
        }

        public static void NearAxisLinesAtTheFrame_AreKeptWhole()
        {
            foreach (var seg in new[] { S(-1.1e-16, 0.3, 0.6e-16, 0.7), S(-1e-6, 0.3, -1e-6, 0.7), S(1 + 2e-16, 0.2, 1 - 1e-16, 0.6) })
            {
                var g = Graph(seg);
                Check.That(g.StructureEdgeCount == 1, $"{seg.A}-{seg.B}: kept");
                double lenSum = 0;
                for (int h = 0; h < g.Graph.HalfEdgeCount; h += 2)
                    if (!g.Graph.IsFrame(h)) lenSum += g.Graph.Length(h);
                Check.Near(lenSum, Vec2.Distance(seg.A, seg.B), $"{seg.A}-{seg.B}: whole length kept", 1e-9);
                var o = Outline(g, 0.02);
                AssertWellFormed(o, "near-axis");
                foreach (var p in o.Points) Check.That(p.X >= 0 && p.X <= 1, $"inside the board ({p})");
            }
        }

        public static void ShallowLineFromTheFrame_StaysInsideTheBoard()
        {
            var g = Graph(S(0.5, 0, 0.9, 0.001));
            var o = Outline(g, 0.02);
            AssertWellFormed(o, "shallow");
            foreach (var p in o.Points)
                Check.That(p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1, $"no corner flies outside the board ({p})");
        }

        public static void StructureLineOnTheFrame_OnlyItsInnerSideIsDrawn()
        {
            var g = Graph(S(0.2, 0, 0.8, 0), S(0.5, 0, 0.5, 0.4));
            var o = Outline(g, 0.02);
            AssertWellFormed(o, "on frame");
            foreach (var p in o.Points) Check.That(p.Y >= 0, $"no point below the frame ({p})");
            Check.That(HasPoint(o, new Vec2(0.2, 0.01)) || HasPoint(o, new Vec2(0.2, 0)), "inner side reaches the end");
            Check.That(o.Collapsed == 0, "no collapse");
        }

        public static void CirclesAndNullsAreIgnored_PolygonsContributeEdges()
        {
            var shapes = new List<Shape>
            {
                new CircleShape(new EntityId(1), new Vec2(0.5, 0.5), 0.2, StrokeStyle.Default),
                null,
                new PolygonShape(new EntityId(2), new Vec2(0.5, 0.5), 4, 0.2, Math.PI / 4, StrokeStyle.Default),
            };
            var g = BandGraph.Build(shapes);
            Check.That(g.StructureEdgeCount == 4, $"a square stamp gives 4 edges (got {g.StructureEdgeCount})");
            var o = Outline(g, 0.02);
            Check.That(o.Runs.Count == 2 && o.Runs[0].Closed && o.Runs[1].Closed, "one closed run inside and one outside");
            var none = BandGraph.Build(new List<Shape> { shapes[0] });
            Check.That(none.StructureEdgeCount == 0 && Outline(none, 0.02).Runs.Count == 0, "a circle alone has no bands");
        }

        public static void SameShapesInAnyOrderOrDirection_GiveIdenticalOutput()
        {
            foreach (var layer in new[] { StarLayer(), TiledLayer() })
            {
                var segs = SegmentsOf(layer);
                var shuffled = new List<PlanarSegment>(segs);
                var rng = new Random(7);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    var t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t;
                }
                for (int i = 0; i < shuffled.Count; i += 2)
                    shuffled[i] = new PlanarSegment(shuffled[i].B, shuffled[i].A, shuffled[i].Source);
                foreach (bool weave in new[] { false, true })
                {
                    var a = Outline(BandGraph.Build(segs), 0.015, weave, weave);
                    var b = Outline(BandGraph.Build(shuffled), 0.015, weave, weave);
                    Check.That(a.Runs.Count > 0 && SameOutput(a, b), $"{layer.Name}: bit-identical output (weave {weave})");
                }
            }
        }

        public static void FacesAreClosedCyclesWithTheirAreas()
        {
            var g = BandGraph.Build(SegmentsOf(StarLayer())).Graph;
            double total = 0;
            int outer = 0;
            for (int f = 0; f < g.FaceCount; f++)
            {
                double a = g.FaceArea(f);
                if (a < 0) outer++;
                total += a;
                for (int k = 0; k < g.FaceSize(f); k++)
                {
                    int h = g.FaceHalfEdge(f, k);
                    Check.That(g.FaceOf(h) == f && g.Prev(g.Next(h)) == h, "cycle bookkeeping");
                    Check.That(g.Dest(h) == g.Origin(g.Next(h)), "cycle is connected");
                }
            }
            Check.That(outer == 2, $"two CW cycles: the frame's outside and the floating star's outline (got {outer})");
            Check.Near(total, 0, "bounded faces add up to the frame's area", 1e-12);
        }
    }
}
