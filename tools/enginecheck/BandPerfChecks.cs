using System;
using System.Collections.Generic;
using System.Diagnostics;
using GeoART.Contracts;
using GeoART.Engine;
using static GeoART.EngineCheck.BandKit;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Band budget (plan 3.6): 500 tile-like snapped segments planarize and outline well inside 50 ms on the desktop.
    /// A loose limit that catches an accidental O(n^3), not a benchmark.
    /// </summary>
    public static class BandPerfChecks
    {
        public static void FiveHundredTileSegments_BuildUnderFiftyMilliseconds()
        {
            var rng = new Random(99);
            var segs = new List<PlanarSegment>(500);
            const int lattice = 40;   // 0.025 spacing
            while (segs.Count < 500)
            {
                int x0 = rng.Next(lattice + 1), y0 = rng.Next(lattice + 1);
                int x1 = Math.Max(0, Math.Min(lattice, x0 + rng.Next(-4, 5)));
                int y1 = Math.Max(0, Math.Min(lattice, y0 + rng.Next(-4, 5)));
                if (x0 == x1 && y0 == y1) continue;
                segs.Add(new PlanarSegment(new Vec2(x0 / (double)lattice, y0 / (double)lattice),
                    new Vec2(x1 / (double)lattice, y1 / (double)lattice), segs.Count));
            }
            double best = double.MaxValue;
            BandOutline o = null;
            for (int run = 0; run < 4; run++)   // the first run warms up the JIT
            {
                var sw = Stopwatch.StartNew();
                var g = BandGraph.Build(segs);
                o = Outline(g, 0.015, interlace: true, inner: true);
                sw.Stop();
                if (run > 0) best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"  band build, 500 segments: {best:0.0} ms, {o.Points.Count} points");
            Check.That(best < 50, $"500 segments in {best:0.0} ms (limit 50)");
            AssertWellFormed(o, "perf");
        }
    }
}
