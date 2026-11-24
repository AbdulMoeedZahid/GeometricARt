using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;
using static GeoART.EngineCheck.BandKit;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Interlace and inner lines: over/under alternates along every thread (closed threads too), FlipWeave swaps every
    /// crossing, bridges belong to the over band only, junctions of degree 6 stay flat, impossible alternations are
    /// counted, inner lines mitre at corners, stop on the neighbour's outer line at junctions and crossings, and bridge
    /// straight through on the over band.
    /// </summary>
    public static class BandWeaveChecks
    {
        /// <summary>Over (true) / under along each thread, at every crossing it passes (wrap included for closed threads).</summary>
        static List<List<bool>> OverSequences(BandGraph g, bool flip)
        {
            var result = new List<List<bool>>();
            var pg = g.Graph;
            for (int t = 0; t < g.ThreadCount; t++)
            {
                var seq = new List<bool>();
                int n = g.ThreadLength(t);
                int steps = g.ThreadClosed(t) ? n : n - 1;
                for (int k = 0; k < steps; k++)
                {
                    int h = g.ThreadHalfEdge(t, k);
                    int c = g.CrossingOf(pg.Dest(h));
                    if (c < 0) continue;
                    int pass = g.StructureRingIndex(PlanarGraph.Twin(h)) & 1;
                    seq.Add(pass == (g.OverPass(c) ^ (flip ? 1 : 0)));
                }
                result.Add(seq);
            }
            return result;
        }

        static void AssertAlternates(BandGraph g, string label)
        {
            var seqs = OverSequences(g, false);
            for (int t = 0; t < seqs.Count; t++)
            {
                var s = seqs[t];
                for (int i = 1; i < s.Count; i++) Check.That(s[i] != s[i - 1], $"{label}: thread {t} alternates at visit {i}");
                if (g.ThreadClosed(t) && s.Count > 1) Check.That(s[0] != s[s.Count - 1], $"{label}: thread {t} alternates across the wrap");
            }
        }

        public static void StarAndTiledFixtures_AlternateWithoutConflicts()
        {
            var star = BandGraph.Build(SegmentsOf(StarLayer()));
            Check.That(star.CrossingCount == 8 && star.ThreadCount == 2, $"star: 8 crossings, 2 squares (got {star.CrossingCount}, {star.ThreadCount})");
            Check.That(star.WeaveConflicts == 0, "star: no conflicts");
            AssertAlternates(star, "star");
            var tiled = BandGraph.Build(SegmentsOf(TiledLayer()));
            Check.That(tiled.WeaveConflicts == 0, "tiled: no conflicts (tip-to-tip contacts weave like crossings)");
            Check.That(tiled.CrossingCount == 36, $"tiled: 32 star crossings + 4 tip contacts (got {tiled.CrossingCount})");
            AssertAlternates(tiled, "tiled");
        }

        public static void Pentagram_ClosedSelfCrossingThreadAlternates()
        {
            var c = new Vec2(0.5, 0.5);
            var pts = new Vec2[5];
            for (int k = 0; k < 5; k++)
            {
                double a = Math.PI / 2 + 2 * Math.PI * k / 5;
                pts[k] = new Vec2(c.X + 0.35 * Math.Cos(a), c.Y + 0.35 * Math.Sin(a));
            }
            var segs = new PlanarSegment[5];
            for (int k = 0; k < 5; k++) segs[k] = new PlanarSegment(pts[k], pts[(k + 2) % 5], k);
            var g = BandGraph.Build(segs);
            Check.That(g.CrossingCount == 5 && g.ThreadCount == 1 && g.ThreadClosed(0), "one closed thread through 5 crossings");
            Check.That(g.WeaveConflicts == 0, "10 visits: consistent");
            AssertAlternates(g, "pentagram");
        }

        public static void OddClosedThread_ReportsExactlyOneConflict()
        {
            // A triangle crossed once by a line that starts inside it: one visit on a closed thread can't alternate.
            var g = Graph(S(0.3, 0.3, 0.7, 0.3), S(0.7, 0.3, 0.5, 0.7), S(0.5, 0.7, 0.3, 0.3), S(0.45, 0.4, 0.45, 0.9));
            Check.That(g.CrossingCount == 1, "one crossing");
            Check.That(g.WeaveConflicts == 1, $"exactly one conflict (got {g.WeaveConflicts})");
            var o = Outline(g, 0.02, interlace: true);
            Check.That(o.WeaveConflicts == 1, "reported on the outline in Interlace");
            Check.That(Outline(g, 0.02).WeaveConflicts == 0, "not reported in Double");
        }

        public static void DegreeSixCentre_IsDrawnFlat()
        {
            var g = Graph(S(0.2, 0.5, 0.8, 0.5), S(0.5, 0.2, 0.5, 0.8), S(0.25, 0.25, 0.75, 0.75));
            int centre = -1;
            for (int v = 0; v < g.Graph.NodeCount; v++)
                if (Vec2.Distance(g.Graph.Node(v), new Vec2(0.5, 0.5)) < 1e-12) centre = v;
            Check.That(centre >= 0 && g.StructureDegree(centre) == 6, "degree 6 centre");
            Check.That(g.CrossingOf(centre) < 0 && g.CrossingCount == 0, "no weave bit there");
            var o = Outline(g, 0.02, interlace: true);
            Check.That(o.Bridges.Count == 0, "no bridges: flat mitred junction");
            AssertWellFormed(o, "rosette");
        }

        public static void BridgesBelongToTheOverBand_AndFlipSwapsEveryCrossing()
        {
            var g = BandGraph.Build(SegmentsOf(StarLayer()));
            const double h = 0.0075;
            foreach (bool flip in new[] { false, true })
            {
                var o = Outline(g, 2 * h, interlace: true, flip: flip);
                Check.That(o.Bridges.Count == 4 * g.CrossingCount, "two bridges (4 points) per crossing");
                for (int c = 0; c < g.CrossingCount; c++)
                {
                    int v = g.CrossingNode(c);
                    Vec2 at = g.Graph.Node(v);
                    int p = g.OverPass(c) ^ (flip ? 1 : 0);
                    Vec2 over = g.Graph.Dir(g.StructureOut(v, p)), under = g.Graph.Dir(g.StructureOut(v, p + 1));
                    int found = 0;
                    for (int i = 0; i < o.Bridges.Count; i += 2)
                    {
                        Vec2 a = o.Bridges[i], b = o.Bridges[i + 1];
                        if (Vec2.Distance(a, at) > 4 * h) continue;
                        found++;
                        Vec2 d = b - a;
                        Check.Near(d.X * over.Y - d.Y * over.X, 0, "bridge runs along the over band", 1e-12);
                        Check.That(Math.Abs(d.X * under.Y - d.Y * under.X) > 1e-6, "not along the under band");
                        Check.Near(LineDist(a, at, at + over), h, "on the over band's edge", 1e-12);
                        Check.Near(LineDist(b, at, at + over), h, "on the over band's edge", 1e-12);
                    }
                    Check.That(found == 2, $"crossing {c}: two bridges (got {found})");
                }
            }
        }

        public static void TipTouchingALine_IsATurnNotACrossing()
        {
            // A V tip resting on a horizontal line: degree 4, but the line goes straight on and the tip turns.
            var g = Graph(S(0.2, 0.3, 0.8, 0.3), S(0.5, 0.3, 0.4, 0.5), S(0.5, 0.3, 0.6, 0.5));
            int v = -1;
            for (int n = 0; n < g.Graph.NodeCount; n++)
                if (g.Graph.Node(n) == new Vec2(0.5, 0.3)) v = n;
            Check.That(v >= 0 && g.StructureDegree(v) == 4, "degree 4 at the touch point");
            Check.That(g.CrossingCount == 0, "not a crossing");
            Check.That(g.ThreadCount == 2, $"two threads: the line and the V (got {g.ThreadCount})");
            const double h = 0.01;
            var o = Outline(g, 2 * h, interlace: true, inner: true);
            Check.That(o.Bridges.Count == 0 && o.InnerBridges.Count == 0, "no bridges cut through the line's band");
            double half = Math.Atan2(0.1, 0.2);
            Check.That(HasPoint(o, new Vec2(0.5, 0.3 + o.InnerHalfWidth / Math.Sin(half)), 1e-12), "the tip's inner lines mitre (same band)");
            AssertWellFormed(o, "tip on line");
        }

        public static void StarInASquareTouchingItsTips_WeavesOnlyRealCrossings()
        {
            var segs = SegmentsOf(StarLayer());
            const double lo = 0.15, hi = 0.85;   // a square whose sides touch the star's four cardinal tips
            segs.Add(S(lo, lo, hi, lo, 100)); segs.Add(S(hi, lo, hi, hi, 101));
            segs.Add(S(hi, hi, lo, hi, 102)); segs.Add(S(lo, hi, lo, lo, 103));
            var g = BandGraph.Build(segs);
            Check.That(g.CrossingCount == 8, $"only the star's 8 crossings weave (got {g.CrossingCount})");
            Check.That(g.WeaveConflicts == 0, "no conflicts");
            AssertAlternates(g, "star in square");
            var o = Outline(g, 0.015, interlace: true);
            for (int c = 0; c < g.CrossingCount; c++)
            {
                int v = g.CrossingNode(c);
                Vec2 over = g.Graph.Dir(g.StructureOut(v, g.OverPass(c)));
                for (int i = 0; i < o.Bridges.Count; i += 2)
                {
                    if (Vec2.Distance(o.Bridges[i], g.Graph.Node(v)) > 0.03) continue;
                    Vec2 d = o.Bridges[i + 1] - o.Bridges[i];
                    Check.Near(d.X * over.Y - d.Y * over.X, 0, "every bridge runs along its over band", 1e-12);
                }
            }
        }

        public static void InnerLines_MitreAtCornersAndAreSkippedWhenTooThin()
        {
            var g = Graph(S(0.3, 0.3, 0.6, 0.3), S(0.6, 0.3, 0.6, 0.6));
            var o = Outline(g, 0.02, inner: true);
            Check.That(o.InnerShown, "8 mm: inner lines shown");
            double hi = o.InnerHalfWidth;
            Check.Near(hi, 0.01 - 0.004, "inset = max(0.2 width, 1.5 mm)", 1e-15);
            Check.That(HasPoint(o, new Vec2(0.6 - hi, 0.3 + hi)) && HasPoint(o, new Vec2(0.6 + hi, 0.3 - hi)), "inner mitres at h_in");
            Check.That(o.InnerRuns.Count == 1 && o.InnerRuns[0].Closed, "one closed inner outline");
            var thin = Outline(g, LayerBands.DefaultWidth, inner: true);
            Check.That(!thin.InnerShown && thin.InnerRuns.Count == 0, "6 mm: inner lines would touch the core, skipped");
        }

        public static void InnerLinesAtAT_StopOnTheOtherBandsOuterEdge()
        {
            var g = Graph(S(0.2, 0.5, 0.8, 0.5), S(0.5, 0.5, 0.5, 0.8));
            const double h = 0.01;
            var o = Outline(g, 2 * h, inner: true);
            double hi = o.InnerHalfWidth;
            Check.That(HasPoint(o, new Vec2(0.5 - h, 0.5 + hi)) && HasPoint(o, new Vec2(0.5 + h, 0.5 + hi)),
                "the bar's upper inner line stops at the stem's outer edges");
            Check.That(HasPoint(o, new Vec2(0.5 - hi, 0.5 + h)) && HasPoint(o, new Vec2(0.5 + hi, 0.5 + h)),
                "the stem's inner lines stop at the bar's outer edge");
            Check.That(HasPoint(o, new Vec2(0.5, 0.5 - hi)), "the bar's lower inner line passes straight under the T");
            AssertWellFormed(o, "T inner");
        }

        public static void InnerLinesAtAWovenCrossing_UnderStopsOnOverOuterOverBridges()
        {
            var g = BandGraph.Build(SegmentsOf(StarLayer()));
            const double h = 0.01;
            var o = Outline(g, 2 * h, interlace: true, inner: true);
            Check.That(o.InnerShown, "inner lines shown at 8 mm");
            double hi = o.InnerHalfWidth;
            Check.That(o.InnerBridges.Count == 4 * g.CrossingCount, "two inner bridges per crossing");
            for (int c = 0; c < g.CrossingCount; c++)
            {
                int v = g.CrossingNode(c);
                Vec2 at = g.Graph.Node(v);
                int p = g.OverPass(c);
                Vec2 over = g.Graph.Dir(g.StructureOut(v, p)), under = g.Graph.Dir(g.StructureOut(v, p + 1));
                int underEnds = 0, overEnds = 0;
                foreach (var r in o.InnerRuns)
                {
                    if (r.Closed) continue;
                    foreach (var q in new[] { o.Points[r.Start], o.Points[r.Start + r.Count - 1] })
                    {
                        if (Vec2.Distance(q, at) > 4 * h) continue;
                        double du = LineDist(q, at, at + under), dv = LineDist(q, at, at + over);
                        if (Math.Abs(du - hi) < 1e-12 && Math.Abs(dv - h) < 1e-12) underEnds++;   // under: stops on over's outer edge
                        else if (Math.Abs(dv - hi) < 1e-12 && Math.Abs(du - h) < 1e-12)
                        {
                            overEnds++;   // over: continues through an inner bridge from this very point
                            Check.That(o.InnerBridges.Contains(q), "an over inner line end is joined by an inner bridge");
                        }
                        else Check.That(false, $"crossing {c}: unexpected inner end {q} (du {du}, dv {dv})");
                    }
                }
                Check.That(underEnds == 4 && overEnds == 4, $"crossing {c}: 4 under and 4 over inner ends (got {underEnds}, {overEnds})");
                int bridges = 0;
                for (int i = 0; i < o.InnerBridges.Count; i += 2)
                {
                    Vec2 a = o.InnerBridges[i], b = o.InnerBridges[i + 1];
                    if (Vec2.Distance(a, at) > 4 * h) continue;
                    bridges++;
                    Check.Near(LineDist(a, at, at + over), hi, "over inner bridge at h_in", 1e-12);
                    Check.Near(LineDist(b, at, at + over), hi, "over inner bridge at h_in", 1e-12);
                    Check.Near(LineDist(a, at, at + under), h, "it starts on the under band's outer edge", 1e-12);
                }
                Check.That(bridges == 2, $"crossing {c}: two inner bridges (got {bridges})");
            }
        }
    }
}
