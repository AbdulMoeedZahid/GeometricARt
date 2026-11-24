using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;
using static GeoART.EngineCheck.BandKit;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Band outlines for one width: mitred corners (h cot(theta/2) along the edge, h / sin(theta/2) along the bisector),
    /// true mitres at star tips, bevel only under 5 degrees, no spikes at near-straight joints, MaxSafeWidth and collapse,
    /// open crossing squares, symmetry, frame corners, split events, fuzz.
    /// </summary>
    public static class BandOutlineChecks
    {
        static Vec2 Polar(Vec2 c, double r, double deg) =>
            new Vec2(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));

        public static void RightAngleCorner_IsAtHAlongBothEdges()
        {
            var g = Graph(S(0.3, 0.3, 0.6, 0.3), S(0.6, 0.3, 0.6, 0.6));
            var o = Outline(g, 0.02);
            Check.That(HasPoint(o, new Vec2(0.59, 0.31)), "inner corner (h, h) in from the node");
            Check.That(HasPoint(o, new Vec2(0.61, 0.29)), "outer corner (h, h) out from the node");
            Check.That(o.Runs.Count == 1 && o.Runs[0].Closed, "an L band is one closed outline");
        }

        public static void StarTipMitres_MatchTheAnalyticDistances()
        {
            foreach (double tip in new[] { 45.0, 30.0, 22.5 })
            {
                var apex = new Vec2(0.5, 0.3);
                Vec2 a = Polar(apex, 0.3, 90 + tip / 2), b = Polar(apex, 0.3, 90 - tip / 2);
                var g = Graph(new PlanarSegment(apex, a, 0), new PlanarSegment(apex, b, 1));
                const double h = 0.005;
                var o = Outline(g, 2 * h);
                double half = tip / 2 * Math.PI / 180;
                Vec2 inner = new Vec2(apex.X, apex.Y + h / Math.Sin(half));   // along the bisector: h / sin(theta/2)
                Vec2 outer = new Vec2(apex.X, apex.Y - h / Math.Sin(half));
                Check.That(HasPoint(o, inner, 1e-12), $"{tip} deg: inner mitre on the bisector");
                Check.That(HasPoint(o, outer, 1e-12), $"{tip} deg: outer tip is a true mitre (not blunted)");
                Vec2 d = (a - apex) * (1 / 0.3);
                double along = (inner.X - apex.X) * d.X + (inner.Y - apex.Y) * d.Y;
                Check.Near(along, h / Math.Tan(half), $"{tip} deg: along the edge h cot(theta/2)", 1e-12);
                Check.That(o.Collapsed == 0, $"{tip} deg: nothing collapses");
            }
        }

        public static void TipsUnderFiveDegrees_AreBevelled()
        {
            var apex = new Vec2(0.5, 0.2);
            const double h = 0.005;
            var g = Graph(new PlanarSegment(apex, Polar(apex, 0.6, 92), 0), new PlanarSegment(apex, Polar(apex, 0.6, 88), 1));
            var o = Outline(g, 2 * h);
            double lowest = double.MaxValue;
            foreach (var p in o.Points) lowest = Math.Min(lowest, p.Y);
            Check.That(lowest >= apex.Y - h - 1e-12, $"no long spike below a 4 deg tip (lowest {lowest})");
            Check.That(HasPoint(o, apex + new Vec2(Math.Sin(88 * Math.PI / 180), -Math.Cos(88 * Math.PI / 180)) * h, 1e-12),
                "bevel point is the plain offset point at the apex");
            AssertWellFormed(o, "4 deg");

            var g5 = Graph(new PlanarSegment(apex, Polar(apex, 0.6, 93), 0), new PlanarSegment(apex, Polar(apex, 0.6, 87), 1));
            var o5 = Outline(g5, 2 * h);
            Check.That(HasPoint(o5, new Vec2(apex.X, apex.Y - h / Math.Sin(3 * Math.PI / 180)), 1e-11), "6 deg tip keeps its mitre");
        }

        public static void NearStraightJoints_HaveNoSpikes()
        {
            foreach (double kink in new[] { 1e-12, 1e-9, 1e-7 })
            {
                var m = new Vec2(0.5, 0.5 + kink);
                var g = Graph(S(0.2, 0.5, m.X, m.Y), S(m.X, m.Y, 0.8, 0.5));
                var o = Outline(g, 0.02);
                AssertWellFormed(o, $"kink {kink}");
                foreach (var p in o.Points)
                {
                    double d = Math.Min(SegDist(p, new Vec2(0.2, 0.5), m), SegDist(p, m, new Vec2(0.8, 0.5)));
                    Check.That(d <= 0.01 * Math.Sqrt(2) + 1e-9, $"kink {kink}: point {p} stays within the band");
                }
            }
        }

        public static void MaxSafeWidth_OfAnEquilateralTriangleIsTwiceItsInradius()
        {
            var c = new Vec2(0.5, 0.5);
            const double side = 0.3;
            double r = side / Math.Sqrt(3);   // circumradius
            Vec2 p0 = Polar(c, r, 90), p1 = Polar(c, r, 210), p2 = Polar(c, r, 330);
            var g = Graph(new PlanarSegment(p0, p1, 0), new PlanarSegment(p1, p2, 1), new PlanarSegment(p2, p0, 2));
            var o = Outline(g, 0.01);
            Check.Near(o.MaxSafeWidth, side / Math.Sqrt(3), "2 x inradius = side / sqrt 3", 1e-9);
            Check.That(Outline(g, o.MaxSafeWidth * 0.999).Collapsed == 0, "just below: nothing collapses");
            Check.That(Outline(g, o.MaxSafeWidth * 1.001).Collapsed > 0, "just above: the inner triangle collapses");
            AssertWellFormed(Outline(g, o.MaxSafeWidth * 1.5), "collapsed triangle");
        }

        public static void MaxSafeWidth_OfTheStarSeparatesCollapse()
        {
            var g = BandGraph.Build(SegmentsOf(StarLayer()));
            double safe = Outline(g, 0.015).MaxSafeWidth;
            Check.That(safe > 0.015 && safe < 0.5, $"star limit is finite and above the default width ({safe})");
            Check.That(Outline(g, safe * 0.999).Collapsed == 0, "below: no collapse");
            Check.That(Outline(g, safe * 1.001).Collapsed > 0, "above: collapse reported");
        }

        public static void TwoParallelLinesCloserThanTheWidth_AreFlaggedAtTheFrame()
        {
            var g = Graph(S(0.5, 0, 0.5, 1), S(0.51, 0, 0.51, 1));
            var o = Outline(g, 0.02);
            Check.Near(o.MaxSafeWidth, 0.01, "the limit is their distance", 1e-12);
            Check.That(o.Collapsed > 0, "the frame piece between them collapses");
            Check.That(Outline(g, 0.009).Collapsed == 0, "narrower than their distance: fine");
        }

        public static void LineMeetingTheFrameAtAnAngle_EndsOnTheFrameAtH()
        {
            var a = new Vec2(0.3, 0);
            var b = new Vec2(0.6, 0.5);
            var o = Outline(Graph(new PlanarSegment(a, b, 0)), 0.02);
            int onFrame = 0;
            foreach (var p in o.Points)
            {
                if (p.Y != 0) continue;
                onFrame++;
                Check.Near(LineDist(p, a, b), 0.01, "corner on the frame lies on the offset line", 1e-12);
            }
            Check.That(onFrame == 2, $"both band edges end on the frame (got {onFrame})");
        }

        public static void FlatCrossing_LeavesTheCrossingSquareOpen()
        {
            var g = Graph(S(0.2, 0.5, 0.8, 0.5), S(0.5, 0.2, 0.5, 0.8));
            const double h = 0.01;
            var o = Outline(g, 2 * h);
            Check.That(o.Runs.Count == 1 && o.Runs[0].Closed, $"a floating plus is one closed outline round its caps (got {o.Runs.Count})");
            foreach (var (p, q) in Segments(o))
                for (int i = 1; i < 10; i++)
                {
                    Vec2 m = p + (q - p) * (i / 10.0);
                    bool inside = Math.Abs(m.X - 0.5) < h - 1e-12 && Math.Abs(m.Y - 0.5) < h - 1e-12;
                    Check.That(!inside, $"no outline inside the crossing square ({m})");
                }
            Check.That(HasPoint(o, new Vec2(0.51, 0.51)) && HasPoint(o, new Vec2(0.49, 0.49)), "corners at (+-h, +-h)");
        }

        public static void StarOutline_IsSymmetricUnder45DegreeRotation()
        {
            var o = Outline(BandGraph.Build(SegmentsOf(StarLayer())), 0.015);
            double c = Math.Cos(Math.PI / 4), s = Math.Sin(Math.PI / 4);
            foreach (var p in o.Points)
            {
                double x = p.X - 0.5, y = p.Y - 0.5;
                var r = new Vec2(0.5 + c * x - s * y, 0.5 + s * x + c * y);
                Check.That(HasPoint(o, r, 1e-12), $"rotated point {r} is also on the outline");
            }
        }

        public static void TiledOutline_IsSymmetricUnderMirrorsAndQuarterTurns()
        {
            var o = Outline(BandGraph.Build(SegmentsOf(TiledLayer())), 0.015);
            foreach (var p in o.Points)
            {
                Check.That(HasPoint(o, new Vec2(1 - p.X, p.Y), 1e-12), $"mirror x of {p}");
                Check.That(HasPoint(o, new Vec2(p.X, 1 - p.Y), 1e-12), $"mirror y of {p}");
                Check.That(HasPoint(o, new Vec2(1 - p.Y, p.X), 1e-12), $"90 deg rotation of {p}");
            }
        }

        public static void DefaultFixtures_HaveNoCollapseOrSelfIntersection()
        {
            foreach (var layer in new[] { StarLayer(), TiledLayer() })
            {
                var o = Outline(BandGraph.Build(SegmentsOf(layer)), LayerBands.DefaultWidth, true, false);
                Check.That(o.Collapsed == 0 && o.SelfIntersections == 0, $"{layer.Name}: clean at the default width");
                AssertWellFormed(o, layer.Name);
            }
        }

        public static void SplitEvent_IsCaughtBySelfIntersections()
        {
            // A rectangle-ish face with a dent whose reflex corner sits 0.05 above the bottom side.
            var g = Graph(S(0.2, 0.2, 0.8, 0.2), S(0.8, 0.2, 0.8, 0.8), S(0.8, 0.8, 0.5, 0.25), S(0.5, 0.25, 0.2, 0.8),
                S(0.2, 0.8, 0.2, 0.2));
            var wide = Outline(g, 0.04);
            Check.That(wide.Collapsed == 0, "no offset segment collapses");
            Check.That(wide.SelfIntersections > 0, "but the reflex corner's offset crosses the bottom side's");
            Check.That(Outline(g, 0.01).SelfIntersections == 0, "narrow: clean");
        }

        public static void Fuzz_SnappedDesignsGiveWellFormedOutlines()
        {
            var cands = new List<Vec2>();
            for (int i = 0; i <= 8; i++)
                for (int j = 0; j <= 8; j++) cands.Add(new Vec2(i / 8.0, j / 8.0));
            for (int k = 0; k < 8; k++) cands.Add(CircleDivisions.Point(new Vec2(0.5, 0.5), 0.3, k));
            var rng = new Random(12345);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = 3 + rng.Next(18);
                var segs = new PlanarSegment[n];
                for (int i = 0; i < n; i++)
                    segs[i] = new PlanarSegment(cands[rng.Next(cands.Count)], cands[rng.Next(cands.Count)], i);
                var g = BandGraph.Build(segs);
                double w = LayerBands.MinWidth + rng.NextDouble() * (LayerBands.MaxWidth - LayerBands.MinWidth);
                var o = Outline(g, w, rng.Next(2) == 0, rng.Next(2) == 0, rng.Next(2) == 0);
                AssertWellFormed(o, $"trial {trial}");
                Check.That(!double.IsNaN(o.MaxSafeWidth), $"trial {trial}: MaxSafeWidth is a number");
            }
        }
    }
}
