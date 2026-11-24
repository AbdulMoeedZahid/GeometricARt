using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;
using static GeoART.EngineCheck.BandKit;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Interlace with the structure line shown (ui-line-plan 3.7, decided in P4): the under band's structure line is cut
    /// where the over band covers it, ending on the over band's edges; the over line is never cut; Flip swaps; collinear
    /// duplicates are both cut; welded lines still match; a kinked under thread is cut on both of its segments; nothing
    /// is cut in Double or with the structure hidden; the shared ForBoard options.
    /// </summary>
    public static class StructureCutChecks
    {
        static BandOutline Woven(BandGraph g, double width, bool flip = false, bool structure = true)
        {
            var o = Opt(width, interlace: true, flip: flip);
            o.StructureShown = structure;
            return BandOutline.Build(g, o);
        }

        static List<StructureCut> CutsOf(BandOutline o, int shape)
        {
            var list = new List<StructureCut>();
            foreach (var c in o.StructureCuts) if (c.Shape == shape) list.Add(c);
            return list;
        }

        public static void XCrossing_UnderLineIsCutToTheOverBandsEdges()
        {
            var g = Graph(S(0.2, 0.5, 0.8, 0.5, 0), S(0.5, 0.2, 0.5, 0.8, 1));
            const double h = 0.01;
            var o = Woven(g, 2 * h);
            Check.That(o.StructureCuts.Count == 1, $"one cut (got {o.StructureCuts.Count})");
            StructureCut cut = o.StructureCuts[0];
            int under = cut.Shape;
            // Both lines are 0.6 long and centred on the node: the cut is h either side, in the line's own parameter.
            Check.Near(cut.T0, 0.5 - h / 0.6, "cut starts on one over edge", 1e-12);
            Check.Near(cut.T1, 0.5 + h / 0.6, "and ends on the other", 1e-12);
            Vec2 overDir = under == 0 ? new Vec2(0, 1) : new Vec2(1, 0);
            Check.Near(Math.Abs(cut.Dir0.X * overDir.Y - cut.Dir0.Y * overDir.X), 0, "end 0 runs along the over band", 1e-12);
            Check.Near(Math.Abs(cut.Dir1.X * overDir.Y - cut.Dir1.Y * overDir.X), 0, "end 1 too", 1e-12);
            Check.That(CutsOf(o, 1 - under).Count == 0, "the over line is not cut");

            var flipped = Woven(g, 2 * h, flip: true);
            Check.That(flipped.StructureCuts.Count == 1 && flipped.StructureCuts[0].Shape == 1 - under, "Flip cuts the other line");
        }

        public static void NothingIsCutInDoubleOrWhenTheStructureIsHidden()
        {
            var g = Graph(S(0.2, 0.5, 0.8, 0.5, 0), S(0.5, 0.2, 0.5, 0.8, 1));
            Check.That(Outline(g, 0.02).StructureCuts.Count == 0, "Double: no cuts");
            Check.That(Woven(g, 0.02, structure: false).StructureCuts.Count == 0, "structure hidden: no cuts");
        }

        public static void ObliqueCrossing_CutLengthIsHOverSin()
        {
            // The star's square sides cross at 45 degrees: each under cut is h / sin 45 either side of the node.
            var segs = SegmentsOf(StarLayer());
            var g = BandGraph.Build(segs);
            const double h = 0.0075;
            var o = Woven(g, 2 * h);
            Check.That(o.StructureCuts.Count == g.CrossingCount, $"one cut per crossing (got {o.StructureCuts.Count})");
            foreach (var c in o.StructureCuts)
            {
                PlanarSegment s = segs[c.Shape];
                double len = Vec2.Distance(s.A, s.B);
                Check.Near((c.T1 - c.T0) * len, 2 * h / Math.Sin(Math.PI / 4), $"shape {c.Shape}: 2h / sin 45", 1e-12);
                Check.That(c.T0 > 0 && c.T1 < 1, "inside the side");
            }
            // Every square side passes two crossings; it goes over one and under the other, so it has exactly one cut.
            var perShape = new Dictionary<int, int>();
            foreach (var c in o.StructureCuts) perShape[c.Shape] = perShape.TryGetValue(c.Shape, out int n) ? n + 1 : 1;
            foreach (var kv in perShape) Check.That(kv.Value == 1, $"shape {kv.Key}: alternates (one under per side)");
        }

        public static void CollinearDuplicatesAreAllCut()
        {
            var g = Graph(S(0.2, 0.5, 0.8, 0.5, 0), S(0.3, 0.5, 0.7, 0.5, 2), S(0.5, 0.2, 0.5, 0.8, 1));
            var o = Woven(g, 0.02);
            int horizontalUnder = CutsOf(o, 0).Count + CutsOf(o, 2).Count;
            int verticalUnder = CutsOf(o, 1).Count;
            Check.That((horizontalUnder == 2 && verticalUnder == 0) || (horizontalUnder == 0 && verticalUnder == 1),
                $"both copies of the under line are cut (h {horizontalUnder}, v {verticalUnder})");
        }

        public static void WeldedLineStillMatches()
        {
            // The vertical line's lower end sits 4e-6 off x = 0.5 (welded straight by the planarizer, not in the shape).
            var g = Graph(S(0.2, 0.5, 0.8, 0.5, 0), S(0.5 + 4e-6, 0.2, 0.5, 0.8, 1));
            var o = Woven(g, 0.02);
            var o2 = Woven(g, 0.02, flip: true);
            Check.That(o.StructureCuts.Count == 1 && o2.StructureCuts.Count == 1, "cut in both weave senses");
            Check.That(o.StructureCuts[0].Shape != o2.StructureCuts[0].Shape, "one per line");
        }

        public static void KinkedUnderThreadIsCutOnBothSegments()
        {
            // Two segments meeting at (0.5, 0.5) at a slight kink form one thread through a vertical line.
            var g = Graph(S(0.2, 0.47, 0.5, 0.5, 0), S(0.5, 0.5, 0.8, 0.52, 1), S(0.5, 0.2, 0.5, 0.8, 2));
            Check.That(g.CrossingCount == 1, "a crossing");
            foreach (bool flip in new[] { false, true })
            {
                var o = Woven(g, 0.02, flip: flip);
                bool kinkedUnder = CutsOf(o, 2).Count == 0;
                if (kinkedUnder)
                {
                    var a = CutsOf(o, 0);
                    var b = CutsOf(o, 1);
                    Check.That(a.Count == 1 && b.Count == 1, "both segments of the kinked thread are cut");
                    Check.Near(a[0].T1, 1, "segment 0's cut reaches its end at the node", 1e-12);
                    Check.Near(b[0].T0, 0, "segment 1's cut starts at the node", 1e-12);
                }
                else Check.That(o.StructureCuts.Count == 1 && o.StructureCuts[0].Shape == 2, "or the vertical line is cut");
            }
        }

        public static void PolygonEdgesCarryTheirEdgeIndex()
        {
            var shapes = new List<Shape>
            {
                new PolygonShape(new EntityId(1), new Vec2(0.5, 0.5), 4, 0.2, Math.PI / 4, StrokeStyle.Default),
                new LineShape(new EntityId(2), new Vec2(0.1, 0.5), new Vec2(0.9, 0.5), StrokeStyle.Default),
            };
            var g = BandGraph.Build(shapes);
            foreach (bool flip in new[] { false, true })
            {
                var o = Woven(g, 0.02, flip: flip);
                foreach (var c in o.StructureCuts)
                {
                    if (c.Shape != 0) continue;
                    Vec2 a = BandGraph.PolygonVertex((PolygonShape)shapes[0], c.Edge);
                    Vec2 b = BandGraph.PolygonVertex((PolygonShape)shapes[0], (c.Edge + 1) % 4);
                    Vec2 m = a + (b - a) * ((c.T0 + c.T1) / 2);
                    Check.Near(m.Y, 0.5, $"edge {c.Edge}: the cut is centred where the line crosses it", 1e-9);
                }
            }
        }

        public static void ForBoardSharesTheInnerFormula()
        {
            var b = LayerBands.Default;
            b.On = true;
            b.InnerLines = true;
            var o = BandOutlineOptions.ForBoard(b, 0.40);
            Check.Near(o.InnerInsetFloor, 0.002 / 0.40, "2 mm floor on the 40 cm board", 1e-15);
            Check.Near(o.InnerMinHalf, (0.00085 + 0.001) / 0.40, "Medium core half + 1 mm", 1e-15);
            Check.That(o.StructureShown && o.InnerLines, "structure shown by default, inner lines as set");
            var big = BandOutlineOptions.ForBoard(b, 1.2);   // a corner-placed 1.2 m board: strokes scale 2x
            Check.Near(big.InnerInsetFloor, 0.002 * 2 / 1.2, "the floor scales like the strokes", 1e-15);
            // 6 mm: no room for inner lines; 10 mm (the bump): room, with 2.15 mm from a Medium core.
            var g = BandGraph.Build(SegmentsOf(StarLayer()));
            b.Width = 0.006 / 0.40;
            Check.That(!BandOutline.Build(g, BandOutlineOptions.ForBoard(b, 0.40)).InnerShown, "6 mm: too narrow");
            b.Width = BandOutlineOptions.InnerLinesMinWidthMeters / 0.40;
            var wide = BandOutline.Build(g, BandOutlineOptions.ForBoard(b, 0.40));
            Check.That(wide.InnerShown, "10 mm: inner lines shown");
            Check.Near(wide.InnerHalfWidth * 0.40 - 0.00085, 0.00215, "2.15 mm between inner line and core", 1e-12);
        }
    }
}
