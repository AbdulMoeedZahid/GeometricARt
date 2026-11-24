using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>Shared helpers for the band checks (BandGraphChecks, BandOutlineChecks, BandWeaveChecks, BandPerfChecks).</summary>
    static class BandKit
    {
        public const double InsetFloor = 1.5 / 400.0;                    // 1.5 mm on the 40 cm board
        public const double InnerMinHalf = 0.0017 / 0.4 / 2 + 1.0 / 400.0; // Medium core half + 1 mm

        public static PlanarSegment S(double x0, double y0, double x1, double y1, int src = 0) =>
            new PlanarSegment(new Vec2(x0, y0), new Vec2(x1, y1), src);

        public static BandGraph Graph(params PlanarSegment[] segs) => BandGraph.Build(segs);

        public static BandOutlineOptions Opt(double width, bool interlace = false, bool inner = false, bool flip = false) =>
            new BandOutlineOptions
            {
                Width = width, Interlace = interlace, InnerLines = inner, FlipWeave = flip,
                InnerInsetFloor = InsetFloor, InnerMinHalf = InnerMinHalf,
            };

        public static BandOutline Outline(BandGraph g, double width, bool interlace = false, bool inner = false, bool flip = false) =>
            BandOutline.Build(g, Opt(width, interlace, inner, flip));

        /// <summary>Line segments of a board layer as planar segments (source = shape index).</summary>
        public static List<PlanarSegment> SegmentsOf(Layer layer)
        {
            var list = new List<PlanarSegment>();
            for (int i = 0; i < layer.Shapes.Count; i++)
                if (layer.Shapes[i] is LineShape l) list.Add(new PlanarSegment(l.A, l.B, i));
            return list;
        }

        public static Layer StarLayer()
        {
            var b = GoldenBoards.StarOnFullBoard();
            return b.Layers[b.Layers.Count - 1];
        }

        public static Layer TiledLayer() => GoldenBoards.Tiled2x2().Layers[0];

        public static List<Vec2> RunPoints(BandOutline o, BandRun r)
        {
            var list = new List<Vec2>(r.Count);
            for (int i = 0; i < r.Count; i++) list.Add(o.Points[r.Start + i]);
            return list;
        }

        /// <summary>Every drawn segment (outer runs, inner runs, bridges) as point pairs.</summary>
        public static List<(Vec2, Vec2)> Segments(BandOutline o, bool outer = true, bool inner = false, bool bridges = false)
        {
            var list = new List<(Vec2, Vec2)>();
            void AddRuns(List<BandRun> runs)
            {
                foreach (var r in runs)
                {
                    for (int i = 0; i + 1 < r.Count; i++) list.Add((o.Points[r.Start + i], o.Points[r.Start + i + 1]));
                    if (r.Closed) list.Add((o.Points[r.Start + r.Count - 1], o.Points[r.Start]));
                }
            }
            if (outer) AddRuns(o.Runs);
            if (inner) AddRuns(o.InnerRuns);
            if (bridges)
            {
                for (int i = 0; i < o.Bridges.Count; i += 2) list.Add((o.Bridges[i], o.Bridges[i + 1]));
                for (int i = 0; i < o.InnerBridges.Count; i += 2) list.Add((o.InnerBridges[i], o.InnerBridges[i + 1]));
            }
            return list;
        }

        public static bool HasPoint(BandOutline o, Vec2 p, double eps = 1e-12)
        {
            foreach (var q in o.Points)
                if (Vec2.Distance(p, q) <= eps) return true;
            return false;
        }

        /// <summary>Distance from p to the infinite line through a and b.</summary>
        public static double LineDist(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 d = b - a;
            return Math.Abs(d.X * (p.Y - a.Y) - d.Y * (p.X - a.X)) / d.Length;
        }

        public static double SegDist(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 d = b - a;
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / (d.X * d.X + d.Y * d.Y)));
            return Vec2.Distance(p, new Vec2(a.X + d.X * t, a.Y + d.Y * t));
        }

        /// <summary>Runs well-formed: at least 2 points, all finite, no consecutive duplicates.</summary>
        public static void AssertWellFormed(BandOutline o, string label)
        {
            foreach (var list in new[] { o.Runs, o.InnerRuns })
                foreach (var r in list)
                {
                    Check.That(r.Count >= 2, $"{label}: run has >= 2 points");
                    for (int i = 0; i < r.Count; i++)
                    {
                        Vec2 p = o.Points[r.Start + i];
                        Check.That(p.IsFinite, $"{label}: finite point");
                        if (i > 0) Check.That(p != o.Points[r.Start + i - 1], $"{label}: no consecutive duplicates");
                    }
                }
            foreach (var p in o.Bridges) Check.That(p.IsFinite, $"{label}: finite bridge");
            foreach (var p in o.InnerBridges) Check.That(p.IsFinite, $"{label}: finite inner bridge");
        }

        public static bool SameOutput(BandOutline a, BandOutline b)
        {
            if (a.Points.Count != b.Points.Count || a.Runs.Count != b.Runs.Count || a.Bridges.Count != b.Bridges.Count ||
                a.InnerRuns.Count != b.InnerRuns.Count || a.InnerBridges.Count != b.InnerBridges.Count) return false;
            for (int i = 0; i < a.Points.Count; i++) if (a.Points[i] != b.Points[i]) return false;
            for (int i = 0; i < a.Runs.Count; i++)
                if (a.Runs[i].Start != b.Runs[i].Start || a.Runs[i].Count != b.Runs[i].Count || a.Runs[i].Closed != b.Runs[i].Closed) return false;
            for (int i = 0; i < a.Bridges.Count; i++) if (a.Bridges[i] != b.Bridges[i]) return false;
            for (int i = 0; i < a.InnerBridges.Count; i++) if (a.InnerBridges[i] != b.InnerBridges[i]) return false;
            return true;
        }
    }
}
