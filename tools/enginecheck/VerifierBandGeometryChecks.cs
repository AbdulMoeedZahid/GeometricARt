using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// The only place the checks touch the builder's band types (BandGraph.Build(shapes), BandOutline.Build(graph,
    /// options)); reconciled with the builder's API at P2. Output is normalised to plain lists:
    ///   runs      polylines (closed: last vertex not repeated), collapsed count, MaxSafeWidth.
    /// </summary>
    static class BandApi
    {
        public sealed class Run
        {
            public readonly List<Vec2> Pts = new List<Vec2>();
            public bool Closed;
        }

        public sealed class Out
        {
            public readonly List<Run> Runs = new List<Run>();
            public int Collapsed, SelfIntersections;
            public double MaxSafe;
        }

        /// <summary>Flat Double bands of <paramref name="width"/> (board units, full width), structure shown, no inner lines.</summary>
        public static Out Compute(Layer layer, double width)
        {
            BandGraph graph = BandGraph.Build(layer.Shapes);
            var opt = new BandOutlineOptions { Width = width };   // raw width: Build does not sanitize
            BandOutline r = BandOutline.Build(graph, opt);
            var o = new Out { Collapsed = r.Collapsed, MaxSafe = r.MaxSafeWidth, SelfIntersections = r.SelfIntersections };
            foreach (var run in r.Runs)
            {
                var x = new Run { Closed = run.Closed };
                for (int i = 0; i < run.Count; i++) x.Pts.Add(r.Points[run.Start + i]);
                if (x.Closed && x.Pts.Count > 1 && Vec2.Distance(x.Pts[0], x.Pts[x.Pts.Count - 1]) <= 1e-15)
                    x.Pts.RemoveAt(x.Pts.Count - 1);
                o.Runs.Add(x);
            }
            return o;
        }
    }

    /// <summary>
    /// VERIFIER (ui-line-plan 3.6, P2): band outline geometry, written from the plan and the book pages only.
    /// Analytic corners (h cot(theta/2) along the edge, h / sin(theta/2) along the bisector), true mitres down to the
    /// 16-fold 22.5 degree tip, bevels only under ~5 degrees, near-straight joints without spikes, MaxSafeWidth
    /// (= 2 x inradius for a triangle; r (3 sqrt2 - 4) for the two-square star), collapse counting around it, the open
    /// crossing square, T-junctions, collinear merging, the near-junction weld, frame-cut runs, circles ignored, polygon
    /// edges banded, 45/90 degree symmetry of the golden boards, order independence, "outline is clear of every band",
    /// a seeded fuzz of snapped designs (no NaN, finite, no exception) and a loose performance guard.
    /// Width w is the FULL band width; h = w / 2.
    /// </summary>
    public static class VerifierBandGeometryChecks
    {
        const double Exact = 1e-12;

        // ================= model (Bands.cs) =================

        public static void V_BandModeOrdinalsAreStable()
        {
            Check.That((int)BandMode.Double == 0 && (int)BandMode.Interlace == 1, "BandMode { Double, Interlace } append-only");
        }

        public static void V_DefaultStructMeansNoBands()
        {
            LayerBands z = default;
            Check.That(!z.On, "default(LayerBands).On is false: old saves and old snapshots mean no bands");
            Check.That(!z.HideStructure && !z.InnerLines && !z.FlipWeave, "every flag false by default");
        }

        public static void V_LayerBandsDefaultValues()
        {
            LayerBands d = LayerBands.Default;
            Check.Near(d.Width, 0.015, "default width 0.015 board units (6 mm on 40 cm)", 0);
            Check.That(d.Mode == BandMode.Double, "default mode Double");
            Check.That(d.OutlineWidth == LineWidth.Thin, "default outline Thin");
            Check.That(d.OutlineColor.Equals(Palette.Black), $"default outline colour Palette.Black, got {d.OutlineColor}");
            Check.That(!d.HideStructure && !d.InnerLines && !d.FlipWeave, "structure shown, no inner lines, no flip");
        }

        public static void V_SanitizedWidth()
        {
            foreach (double bad in new[] { double.NaN, 0.0, -3.0, -0.0 })
            {
                LayerBands b = LayerBands.Default;
                b.Width = bad;
                Check.That(b.Sanitized().Width == 0.015, $"width {bad} -> default 0.015, got {b.Sanitized().Width}");
            }
            var cases = new (double w, double e)[] { (0.001, 0.004), (0.004, 0.004), (0.02, 0.02), (0.05, 0.05), (0.2, 0.05) };
            foreach (var c in cases)
            {
                LayerBands b = LayerBands.Default;
                b.Width = c.w;
                Check.That(b.Sanitized().Width == c.e, $"width {c.w} -> {c.e}, got {b.Sanitized().Width}");
            }
        }

        public static void V_SanitizedEnumsAndKeepsValidFields()
        {
            LayerBands b = LayerBands.Default;
            b.On = true;
            b.Mode = (BandMode)99;
            b.OutlineWidth = (LineWidth)99;
            b.HideStructure = true;
            b.InnerLines = true;
            b.FlipWeave = true;
            b.OutlineColor = Palette.Red;
            b.Width = 0.02;
            LayerBands s = b.Sanitized();
            Check.That(s.Mode == BandMode.Double, $"unknown mode -> Double, got {s.Mode}");
            Check.That(s.OutlineWidth == LineWidth.Thin, $"unknown outline width -> Thin, got {s.OutlineWidth}");
            Check.That(s.On && s.HideStructure && s.InnerLines && s.FlipWeave, "flags kept");
            Check.That(s.OutlineColor.Equals(Palette.Red) && s.Width == 0.02, "valid colour and width kept");
            LayerBands i = LayerBands.Default;
            i.Mode = BandMode.Interlace;
            i.OutlineWidth = LineWidth.Thick;
            Check.That(i.Sanitized().Mode == BandMode.Interlace && i.Sanitized().OutlineWidth == LineWidth.Thick, "valid enums kept");
        }

        public static void V_SanitizedTransparentOutlineIsBlack()
        {
            // Plan 3.4: "OutlineColor; A == 0 -> Palette.Black". Asserted on Sanitized(); if the builder applies it only at
            // draw time, this is a NIT-level deviation (the save path still reads 0,0,0,0 for old files).
            LayerBands b = LayerBands.Default;
            b.OutlineColor = new RgbaColor(0, 0, 0, 0);
            Check.That(b.Sanitized().OutlineColor.Equals(Palette.Black), $"A == 0 -> Black, got {b.Sanitized().OutlineColor}");
        }

        // ================= helpers =================

        static Vec2 V(double x, double y) => new Vec2(x, y);
        static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        static string P(Vec2 v) => "(" + R(v.X) + ", " + R(v.Y) + ")";

        static Layer NewLayer() => new Layer(new EntityId(1), "bands");

        static int s_id = 100;
        static Layer L(params (double ax, double ay, double bx, double by)[] segs)
        {
            var l = NewLayer();
            foreach (var s in segs) l.Shapes.Add(new LineShape(new EntityId(++s_id), V(s.ax, s.ay), V(s.bx, s.by), StrokeStyle.Default));
            return l;
        }

        static void AddLine(Layer l, Vec2 a, Vec2 b) => l.Shapes.Add(new LineShape(new EntityId(++s_id), a, b, StrokeStyle.Default));

        static void AddStar(Layer l, Vec2 c, double r)
        {
            for (int start = 0; start < 2; start++)
                for (int i = 0; i < 4; i++)
                    AddLine(l, CircleDivisions.Point(c, r, start + 2 * i), CircleDivisions.Point(c, r, start + 2 * ((i + 1) % 4)));
        }

        /// <summary>Structure segments the bands follow: lines and polygon edges (circles never).</summary>
        static List<(Vec2 A, Vec2 B)> Structure(Layer l)
        {
            var segs = new List<(Vec2, Vec2)>();
            foreach (var s in l.Shapes)
            {
                if (s is LineShape ln) segs.Add((ln.A, ln.B));
                else if (s is PolygonShape pg && pg.Sides >= 3)
                    for (int k = 0; k < pg.Sides; k++) segs.Add((SnapIndex.PolygonVertex(pg, k), SnapIndex.PolygonVertex(pg, (k + 1) % pg.Sides)));
            }
            return segs;
        }

        static double DistSeg(Vec2 p, Vec2 a, Vec2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 > 0 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2 : 0;
            t = Math.Max(0, Math.Min(1, t));
            return Vec2.Distance(p, V(a.X + t * dx, a.Y + t * dy));
        }

        static double Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        /// <summary>Drops repeated vertices and vertices where the run does not turn (collinear, same direction).</summary>
        static List<Vec2> Corners(BandApi.Run run, double tol = Exact)
        {
            var p = new List<Vec2>();
            foreach (var v in run.Pts)
                if (p.Count == 0 || Vec2.Distance(p[p.Count - 1], v) > tol) p.Add(v);
            if (run.Closed && p.Count > 1 && Vec2.Distance(p[0], p[p.Count - 1]) <= tol) p.RemoveAt(p.Count - 1);
            bool changed = true;
            while (changed && p.Count > 2)
            {
                changed = false;
                int n = p.Count;
                for (int i = 0; i < n; i++)
                {
                    if (!run.Closed && (i == 0 || i == n - 1)) continue;
                    Vec2 u = p[(i - 1 + n) % n], v = p[i], w = p[(i + 1) % n];
                    double lu = Vec2.Distance(u, v), lw = Vec2.Distance(v, w);
                    double dot = (v.X - u.X) * (w.X - v.X) + (v.Y - u.Y) * (w.Y - v.Y);
                    if (Math.Abs(Cross(u, v, w)) <= 1e-12 * lu * lw && dot > 0)
                    {
                        p.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
            return p;
        }

        static (long, long) Key(Vec2 v) => ((long)Math.Round(v.X * 1e9), (long)Math.Round(v.Y * 1e9));
        static int Cmp((long, long) a, (long, long) b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2);

        /// <summary>Order-free form: each run starts at its smallest vertex in a fixed direction; runs sorted.</summary>
        static List<(bool Closed, List<Vec2> Pts)> Canon(BandApi.Out o)
        {
            var res = new List<(bool, List<Vec2>)>();
            foreach (var run in o.Runs)
            {
                var c = Corners(run);
                if (c.Count == 0) continue;
                if (run.Closed)
                {
                    int m = 0;
                    for (int i = 1; i < c.Count; i++) if (Cmp(Key(c[i]), Key(c[m])) < 0) m = i;
                    var r = new List<Vec2>();
                    for (int i = 0; i < c.Count; i++) r.Add(c[(m + i) % c.Count]);
                    if (r.Count > 2 && Cmp(Key(r[r.Count - 1]), Key(r[1])) < 0)
                    {
                        var rev = new List<Vec2> { r[0] };
                        for (int i = r.Count - 1; i >= 1; i--) rev.Add(r[i]);
                        r = rev;
                    }
                    res.Add((true, r));
                }
                else
                {
                    if (Cmp(Key(c[c.Count - 1]), Key(c[0])) < 0) c.Reverse();
                    res.Add((false, c));
                }
            }
            res.Sort((a, b) =>
            {
                if (a.Item1 != b.Item1) return a.Item1 ? 1 : -1;
                int k = Cmp(Key(a.Item2[0]), Key(b.Item2[0]));
                return k != 0 ? k : a.Item2.Count.CompareTo(b.Item2.Count);
            });
            return res;
        }

        static string Dump(BandApi.Out o)
        {
            var sb = new StringBuilder();
            foreach (var r in Canon(o))
            {
                sb.Append(r.Closed ? "[closed " : "[open ");
                foreach (var v in r.Pts) sb.Append(P(v)).Append(' ');
                sb.Append("] ");
            }
            return sb.ToString();
        }

        static void SameOutline(BandApi.Out a, BandApi.Out b, double tol, string what)
        {
            var ca = Canon(a);
            var cb = Canon(b);
            Check.That(ca.Count == cb.Count, $"{what}: {ca.Count} vs {cb.Count} runs. A: {Dump(a)} B: {Dump(b)}");
            for (int i = 0; i < ca.Count; i++)
            {
                Check.That(ca[i].Closed == cb[i].Closed && ca[i].Pts.Count == cb[i].Pts.Count,
                    $"{what}: run {i} shape differs. A: {Dump(a)} B: {Dump(b)}");
                for (int k = 0; k < ca[i].Pts.Count; k++)
                    Check.That(Vec2.Distance(ca[i].Pts[k], cb[i].Pts[k]) <= tol,
                        $"{what}: run {i} vertex {k} {P(ca[i].Pts[k])} vs {P(cb[i].Pts[k])}");
            }
        }

        /// <summary>The outline is exactly <paramref name="expected"/> (one run per array, corners in any rotation/direction).</summary>
        static void ExpectRuns(BandApi.Out o, string what, params (bool closed, Vec2[] pts)[] expected)
        {
            var e = new BandApi.Out();
            foreach (var x in expected)
            {
                var r = new BandApi.Run { Closed = x.closed };
                r.Pts.AddRange(x.pts);
                e.Runs.Add(r);
            }
            SameOutline(o, e, Exact, what);
        }

        static List<Vec2> AllVertices(BandApi.Out o)
        {
            var l = new List<Vec2>();
            foreach (var r in o.Runs) l.AddRange(r.Pts);
            return l;
        }

        static bool HasVertex(BandApi.Out o, Vec2 p, double tol = Exact)
        {
            foreach (var v in AllVertices(o)) if (Vec2.Distance(v, p) <= tol) return true;
            return false;
        }

        static void AllFinite(BandApi.Out o, string what)
        {
            foreach (var r in o.Runs)
            {
                Check.That(r.Pts.Count >= 2 || (r.Closed && r.Pts.Count >= 1), $"{what}: a run with {r.Pts.Count} points");
                foreach (var v in r.Pts) Check.That(v.IsFinite, $"{what}: non-finite vertex {P(v)}");
            }
            Check.That(!double.IsNaN(o.MaxSafe) && o.MaxSafe > 0, $"{what}: MaxSafeWidth {R(o.MaxSafe)} must be > 0 (inf allowed)");
            Check.That(o.Collapsed >= 0, $"{what}: Collapsed >= 0");
        }

        /// <summary>
        /// The outline is the boundary of the union of the bands: no vertex, and no point of a non-cap segment, lies
        /// strictly inside any band (distance &lt; h to a structure segment). Cap segments (through a free end) are skipped.
        /// Valid when nothing collapsed.
        /// </summary>
        static void ClearOfBands(BandApi.Out o, Layer l, double h, double eps, string what)
        {
            var segs = Structure(l);
            foreach (var r in o.Runs)
            {
                int n = r.Pts.Count;
                int m = r.Closed ? n : n - 1;
                for (int i = 0; i < n; i++)
                    foreach (var s in segs)
                        Check.That(OnFrame(r.Pts[i]) || DistSeg(r.Pts[i], s.A, s.B) >= h - eps,
                            $"{what}: vertex {P(r.Pts[i])} is {R(DistSeg(r.Pts[i], s.A, s.B))} from {P(s.A)}-{P(s.B)} (< h {R(h)})");
                for (int i = 0; i < m; i++)
                {
                    Vec2 a = r.Pts[i], b = r.Pts[(i + 1) % n];
                    Vec2 mid = V((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    bool cap = false;
                    // End segments: a step (a frame line meeting the frame) crosses its own end point; a < 5 degree bevel
                    // (plan: the two plain offset points) passes within h cos(87.5 deg) = 0.044 h of the apex.
                    foreach (var s in segs)
                        if (DistSeg(s.A, a, b) < h * 0.05 + 1e-12 || DistSeg(s.B, a, b) < h * 0.05 + 1e-12) cap = true;
                    if (cap) continue;
                    foreach (double t in new[] { 0.25, 0.5, 0.75 })
                    {
                        Vec2 q = V(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                        if (OnFrame(q)) continue;   // bands are clipped to the board: a clipped side runs along the frame
                        foreach (var s in segs)
                            Check.That(DistSeg(q, s.A, s.B) >= h - eps,
                                $"{what}: segment {P(a)}-{P(b)} enters the band of {P(s.A)}-{P(s.B)} at {P(q)}");
                    }
                }
            }
        }

        static bool OnFrame(Vec2 p) => Math.Min(Math.Min(p.X, 1 - p.X), Math.Min(p.Y, 1 - p.Y)) <= 1e-12;

        static Vec2 Rot(Vec2 p, Vec2 c, double ang)
        {
            double cs = Math.Cos(ang), sn = Math.Sin(ang), x = p.X - c.X, y = p.Y - c.Y;
            return V(c.X + cs * x - sn * y, c.Y + sn * x + cs * y);
        }

        static void SymmetricVertexSet(BandApi.Out o, Func<Vec2, Vec2> map, double tol, string what)
        {
            var vs = AllVertices(o);
            Check.That(vs.Count > 0, $"{what}: no vertices");
            foreach (var v in vs)
            {
                Vec2 w = map(v);
                bool found = false;
                foreach (var u in vs) if (Vec2.Distance(u, w) <= tol) { found = true; break; }
                Check.That(found, $"{what}: image {P(w)} of vertex {P(v)} is not a vertex");
            }
        }

        // ================= single segment, corners, mitres =================

        public static void V_SingleSegmentIsOneRectangle()
        {
            var o = BandApi.Compute(L((0.3, 0.5, 0.7, 0.5)), 0.04);
            ExpectRuns(o, "single segment, w 0.04",
                (true, new[] { V(0.28, 0.48), V(0.72, 0.48), V(0.72, 0.52), V(0.28, 0.52) }));   // square caps extend h
            Check.That(o.Collapsed == 0, "nothing collapsed");
        }

        public static void V_RightAngleCornerIsAtH()
        {
            // A (0.3, 0.3) -> B (0.6, 0.3) -> C (0.6, 0.6), h = 0.02. Inner corner t = h cot(45) = h; outer = the mitre.
            var o = BandApi.Compute(L((0.3, 0.3, 0.6, 0.3), (0.6, 0.3, 0.6, 0.6)), 0.04);
            ExpectRuns(o, "L corner",
                (true, new[] { V(0.28, 0.28), V(0.62, 0.28), V(0.62, 0.62), V(0.58, 0.62), V(0.58, 0.32), V(0.28, 0.32) }));
        }

        /// <summary>V with apex (0.5, 0.3), arms of length 0.4 at +-theta/2 from straight up.</summary>
        static Layer Vee(double thetaDeg)
        {
            double half = thetaDeg * Math.PI / 360;
            Vec2 apex = V(0.5, 0.3);
            var l = NewLayer();
            AddLine(l, apex, V(0.5 + 0.4 * Math.Sin(half), 0.3 + 0.4 * Math.Cos(half)));
            AddLine(l, apex, V(0.5 - 0.4 * Math.Sin(half), 0.3 + 0.4 * Math.Cos(half)));
            return l;
        }

        public static void V_MitreTipsMatchAnalyticDistances()
        {
            // 8-, 12- and 16-fold star tips: a TRUE mitre at h / sin(theta/2) on the bisector (h cot(theta/2) along each edge).
            foreach (double deg in new[] { 90.0, 45.0, 30.0, 22.5, 10.0 })
            {
                double w = 0.02, h = w / 2, half = deg * Math.PI / 360;
                var o = BandApi.Compute(Vee(deg), w);
                Vec2 apex = V(0.5, 0.3);
                double d = h / Math.Sin(half);
                Vec2 inner = V(0.5, 0.3 + d), outer = V(0.5, 0.3 - d);
                Check.That(HasVertex(o, inner), $"{deg} deg: inner mitre at apex + {R(d)} on the bisector. {Dump(o)}");
                Check.That(HasVertex(o, outer), $"{deg} deg: outer tip at apex - {R(d)} (no bevel, no mitre limit). {Dump(o)}");
                // Along-edge distance of the inner corner: h cot(theta/2).
                Vec2 foot = V(0.5 + Math.Sin(half) * (h / Math.Tan(half)), 0.3 + Math.Cos(half) * (h / Math.Tan(half)));
                Check.Near(Vec2.Distance(inner, foot), h, $"{deg} deg: inner corner is h off the edge at t = h cot", 1e-12);
                foreach (var v in AllVertices(o))
                    if (Vec2.Distance(v, apex) <= d * 1.01)
                        Check.That(Vec2.Distance(v, inner) <= Exact || Vec2.Distance(v, outer) <= Exact,
                            $"{deg} deg: extra vertex {P(v)} near the apex (bevel or spike)");
                Check.That(o.Collapsed == 0, $"{deg} deg: nothing collapsed");
            }
        }

        public static void V_BevelOnlyBelowAboutFiveDegrees()
        {
            double w = 0.01, h = w / 2;
            Vec2 apex = V(0.5, 0.3);
            {
                double half = 7 * Math.PI / 360;
                var o = BandApi.Compute(Vee(7), w);
                Check.That(HasVertex(o, V(0.5, 0.3 - h / Math.Sin(half))), $"7 deg: still a true mitre ({R(h / Math.Sin(half) / h)} h). {Dump(o)}");
            }
            {
                double half = 3 * Math.PI / 360;
                var o = BandApi.Compute(Vee(3), w);
                Vec2 b1 = V(apex.X + h * Math.Cos(half), apex.Y - h * Math.Sin(half));    // apex + h n (right arm, outer side)
                Vec2 b2 = V(apex.X - h * Math.Cos(half), apex.Y - h * Math.Sin(half));
                Check.That(HasVertex(o, b1) && HasVertex(o, b2), $"3 deg: bevel = the two plain offset points at the node. {Dump(o)}");
                foreach (var v in AllVertices(o))
                    Check.That(!(v.Y < apex.Y - h * 1.0001), $"3 deg: no outer spike, got {P(v)}");
                foreach (var v in AllVertices(o)) Check.That(v.IsFinite, "finite");
            }
        }

        public static void V_NearStraightJointsHaveNoSpikes()
        {
            double w = 0.04, h = 0.02;
            foreach (double turn in new[] { 0.0, 1e-9, 1e-7, 1e-6, 1e-5, 1e-3 })
            {
                var l = NewLayer();
                Vec2 a = V(0.2, 0.5), b = V(0.5, 0.5), c = V(0.5 + 0.3 * Math.Cos(turn), 0.5 + 0.3 * Math.Sin(turn));
                AddLine(l, a, b);
                AddLine(l, b, c);
                var o = BandApi.Compute(l, w);
                AllFinite(o, $"turn {turn}");
                Check.That(o.Runs.Count == 1 && o.Runs[0].Closed, $"turn {turn}: one closed run. {Dump(o)}");
                foreach (var v in AllVertices(o))
                {
                    if (Vec2.Distance(v, a) < 1.5 * h || Vec2.Distance(v, c) < 1.5 * h) continue;   // the square caps
                    double d = Math.Min(DistSeg(v, a, b), DistSeg(v, b, c));
                    Check.That(d <= h * (1 + 1e-6) + 1e-12 && d >= h * (1 - 1e-6) - 1e-12,
                        $"turn {turn}: vertex {P(v)} at {R(d)} from the structure (h = {R(h)}): spike or dent");
                }
            }
            // Exactly collinear pieces: merged, so a plain rectangle.
            var m = BandApi.Compute(L((0.2, 0.5, 0.5, 0.5), (0.5, 0.5, 0.8, 0.5)), w);
            ExpectRuns(m, "collinear A-B + B-C", (true, new[] { V(0.18, 0.48), V(0.82, 0.48), V(0.82, 0.52), V(0.18, 0.52) }));
        }

        // ================= MaxSafeWidth and collapse =================

        static Layer Triangle(Vec2 a, Vec2 b, Vec2 c)
        {
            var l = NewLayer();
            AddLine(l, a, b);
            AddLine(l, b, c);
            AddLine(l, c, a);
            return l;
        }

        static double Inradius(Vec2 a, Vec2 b, Vec2 c)
        {
            double la = Vec2.Distance(b, c), lb = Vec2.Distance(a, c), lc = Vec2.Distance(a, b);
            double area = Math.Abs(Cross(a, b, c)) / 2;
            return area / ((la + lb + lc) / 2);
        }

        public static void V_TriangleMaxSafeWidthIsTwiceTheInradius()
        {
            // L / (cot(a/2) + cot(b/2)) is the inradius for every side, so MaxSafeWidth = 2 r (outer sides are reflex: excluded).
            Vec2 a = V(0.2, 0.2), b = V(0.8, 0.25), c = V(0.4, 0.7);
            var o = BandApi.Compute(Triangle(a, b, c), 0.01);
            double exp = 2 * Inradius(a, b, c);
            Check.That(Math.Abs(o.MaxSafe - exp) <= 1e-12 * exp + 1e-15, $"scalene triangle: MaxSafeWidth {R(o.MaxSafe)} vs 2r {R(exp)}");
        }

        public static void V_TriangleCollapsesAroundMaxSafe()
        {
            Vec2 a = V(0.4, 0.4), b = V(0.48, 0.405), c = V(0.43, 0.46);   // 2r = 0.042
            double ms = 2 * Inradius(a, b, c);
            Check.That(ms > 0.008 && ms < 0.045, $"precondition: inside the slider range ({R(ms)})");
            var below = BandApi.Compute(Triangle(a, b, c), ms * 0.99);
            var above = BandApi.Compute(Triangle(a, b, c), ms * 1.01);
            Check.That(below.Collapsed == 0, $"1% below MaxSafeWidth: nothing collapsed, got {below.Collapsed}");
            Check.That(above.Collapsed > 0, $"1% above: collapse reported, got {above.Collapsed}");
            AllFinite(above, "collapsed triangle");
            ClearOfBands(below, Triangle(a, b, c), ms * 0.99 / 2, 1e-12, "triangle just below");
        }

        public static void V_RectangleCollapsesOnTheShortSideFirst()
        {
            // 0.1 x 0.025: inner wedges 90 deg -> L / 2 per side, MaxSafeWidth = 2 * 0.025 / 2 = 0.025.
            var l = L((0.4, 0.45, 0.5, 0.45), (0.5, 0.45, 0.5, 0.475), (0.5, 0.475, 0.4, 0.475), (0.4, 0.475, 0.4, 0.45));
            var o = BandApi.Compute(l, 0.01);
            Check.Near(o.MaxSafe, 0.025, "rectangle MaxSafeWidth", 1e-12);
            Check.That(BandApi.Compute(l, 0.0245).Collapsed == 0, "w 0.0245: no collapse");
            var c = BandApi.Compute(l, 0.0255);
            Check.That(c.Collapsed > 0, $"w 0.0255: the short sides collapse, got {c.Collapsed}");
            AllFinite(c, "collapsed rectangle");
            // The run stays continuous: the inner run of the collapsed face still exists and is finite.
            Check.That(c.Runs.Count == BandApi.Compute(l, 0.0245).Runs.Count, "collapse keeps the run count (a point, not a hole)");
        }

        public static void V_StarMaxSafeWidthIsAnalytic()
        {
            // Two-square 8-point star of radius r: the tip triangles (right isosceles, legs r(sqrt2 - 1)) bind:
            // 2 x inradius = r (3 sqrt2 - 4).
            var b = GoldenBoards.StarOnFullBoard();
            var star = b.FindLayer(b.ActiveLayerId);
            Check.That(star.Name == GoldenBoards.StarLayerName, "precondition: the star layer");
            var o = BandApi.Compute(star, 0.015);
            double exp = GoldenBoards.StarRadius * (3 * Math.Sqrt(2) - 4);
            Check.That(Math.Abs(o.MaxSafe - exp) <= 1e-12, $"star MaxSafeWidth {R(o.MaxSafe)} vs r(3 sqrt2 - 4) = {R(exp)}");
        }

        public static void V_SmallStarCollapseAroundMaxSafe()
        {
            var l = NewLayer();
            AddStar(l, V(0.5, 0.5), 0.15);
            double ms = 0.15 * (3 * Math.Sqrt(2) - 4);   // 0.0364: inside the slider range
            Check.That(BandApi.Compute(l, ms * 0.99).Collapsed == 0, "1% below: none");
            var above = BandApi.Compute(l, ms * 1.01);
            Check.That(above.Collapsed > 0, $"1% above: collapse reported (8 tip triangles), got {above.Collapsed}");
            AllFinite(above, "collapsed star");
        }

        // ================= crossings, T, merge, weld, frame =================

        public static void V_FlatRightCrossingSquareIsOpen()
        {
            var o = BandApi.Compute(L((0.2, 0.5, 0.8, 0.5), (0.5, 0.2, 0.5, 0.8)), 0.04);
            ExpectRuns(o, "plus outline (book p22: each band stops at the other's edge)", (true, new[]
            {
                V(0.18, 0.48), V(0.48, 0.48), V(0.48, 0.18), V(0.52, 0.18), V(0.52, 0.48), V(0.82, 0.48),
                V(0.82, 0.52), V(0.52, 0.52), V(0.52, 0.82), V(0.48, 0.82), V(0.48, 0.52), V(0.18, 0.52),
            }));
            foreach (var r in o.Runs)
                for (int i = 0; i < r.Pts.Count; i++)
                {
                    Vec2 a = r.Pts[i], b = r.Pts[(i + 1) % r.Pts.Count];
                    Vec2 m = V((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    Check.That(!(Math.Abs(m.X - 0.5) < 0.02 - 1e-9 && Math.Abs(m.Y - 0.5) < 0.02 - 1e-9),
                        $"a segment {P(a)}-{P(b)} crosses the open crossing square");
                }
        }

        public static void V_Flat45CrossingRhombusIsOpen()
        {
            double w = 0.03, h = w / 2, s2 = Math.Sqrt(2);
            var l = L((0.2, 0.5, 0.8, 0.5), (0.3, 0.3, 0.7, 0.7));
            var o = BandApi.Compute(l, w);
            // Corners: y = 0.5 +- h meets x - y = +-h sqrt2.
            foreach (var p in new[] { V(0.5 + h + h * s2, 0.5 + h), V(0.5 + h - h * s2, 0.5 + h), V(0.5 - h + h * s2, 0.5 - h), V(0.5 - h - h * s2, 0.5 - h) })
                Check.That(HasVertex(o, p), $"45 deg crossing corner {P(p)}. {Dump(o)}");
            ClearOfBands(o, l, h, 1e-12, "45 deg crossing");
        }

        public static void V_TJunctionOutline()
        {
            var o = BandApi.Compute(L((0.2, 0.5, 0.8, 0.5), (0.5, 0.5, 0.5, 0.8)), 0.04);
            ExpectRuns(o, "T", (true, new[]
            {
                V(0.18, 0.48), V(0.82, 0.48), V(0.82, 0.52), V(0.52, 0.52), V(0.52, 0.82), V(0.48, 0.82), V(0.48, 0.52), V(0.18, 0.52),
            }));
        }

        public static void V_CollinearOverlapsAndDuplicatesMerge()
        {
            var reference = BandApi.Compute(L((0.2, 0.5, 0.8, 0.5)), 0.04);
            var pieces = BandApi.Compute(L((0.2, 0.5, 0.45, 0.5), (0.45, 0.5, 0.8, 0.5), (0.2, 0.5, 0.8, 0.5), (0.45, 0.5, 0.2, 0.5),
                (0.3, 0.5, 0.6, 0.5), (0.2, 0.5, 0.45, 0.5)), 0.04);
            SameOutline(pieces, reference, Exact, "A-B + B-C + A-C + reversed and exact duplicates + an inner overlap");
            // Diagonal, coordinates not exactly representable on the line.
            var dref = BandApi.Compute(L((0.1, 0.1, 0.7, 0.7)), 0.02);
            var dpieces = BandApi.Compute(L((0.1, 0.1, 0.35, 0.35), (0.35, 0.35, 0.7, 0.7), (0.2, 0.2, 0.6, 0.6), (0.7, 0.7, 0.1, 0.1)), 0.02);
            SameOutline(dpieces, dref, 1e-12, "diagonal collinear pieces");
            // A zero-length line is dropped.
            var z = BandApi.Compute(L((0.2, 0.5, 0.8, 0.5), (0.4, 0.3, 0.4, 0.3)), 0.04);
            SameOutline(z, reference, Exact, "zero-length line dropped");
        }

        public static void V_NearJunctionWeldMakesAT()
        {
            var t = BandApi.Compute(L((0.2, 0.5, 0.8, 0.5), (0.5, 0.5, 0.5, 0.8)), 0.04);
            foreach (double off in new[] { 1e-6, -1e-6, 5e-6 })
            {
                var o = BandApi.Compute(L((0.2, 0.5, 0.8, 0.5), (0.5, 0.5 + off, 0.5, 0.8)), 0.04);
                SameOutline(o, t, 1e-9, $"stub end {off} off the line welds onto it (no micro cap)");
            }
        }

        public static void V_RunsAreCutByTheFrame()
        {
            var full = BandApi.Compute(L((0, 0.5, 1, 0.5)), 0.04);
            ExpectRuns(full, "frame to frame: two open runs",
                (false, new[] { V(0, 0.48), V(1, 0.48) }), (false, new[] { V(0, 0.52), V(1, 0.52) }));
            var half = BandApi.Compute(L((0, 0.5, 0.5, 0.5)), 0.04);
            ExpectRuns(half, "frame to interior: one open run around the free end",
                (false, new[] { V(0, 0.48), V(0.52, 0.48), V(0.52, 0.52), V(0, 0.52) }));
            double h = 0.02, s2 = Math.Sqrt(2);
            var diag = BandApi.Compute(L((0, 0, 0.5, 0.5)), 0.04);
            ExpectRuns(diag, "diagonal into the frame corner: ends flush on both frame edges",
                (false, new[] { V(h * s2, 0), V(0.5 + h * s2, 0.5), V(0.5, 0.5 + h * s2), V(0, h * s2) }));   // cap extended h along the line
            foreach (var r in diag.Runs)
                foreach (var e in new[] { r.Pts[0], r.Pts[r.Pts.Count - 1] })
                    Check.That(Math.Abs(e.X) <= Exact || Math.Abs(e.Y) <= Exact, $"run end {P(e)} lies on the frame");
        }

        public static void V_CirclesAreNeverBanded()
        {
            var reference = BandApi.Compute(L((0.2, 0.5, 0.8, 0.5)), 0.04);
            var l = L((0.2, 0.5, 0.8, 0.5));
            l.Shapes.Add(new CircleShape(new EntityId(++s_id), V(0.5, 0.5), 0.2, StrokeStyle.Default));
            l.Shapes.Insert(0, new CircleShape(new EntityId(++s_id), V(0.3, 0.4), 0.1, StrokeStyle.Default));
            SameOutline(BandApi.Compute(l, 0.04), reference, Exact, "circles change nothing");
            var only = NewLayer();
            only.Shapes.Add(new CircleShape(new EntityId(++s_id), V(0.5, 0.5), 0.2, StrokeStyle.Default));
            var o = BandApi.Compute(only, 0.04);
            Check.That(o.Runs.Count == 0 && o.Collapsed == 0, "circles only: no runs");
        }

        public static void V_PolygonEdgesAreBandedLikeLines()
        {
            var poly = new PolygonShape(new EntityId(++s_id), V(0.5, 0.5), 6, 0.2, 0.3, StrokeStyle.Default);
            var lp = NewLayer();
            lp.Shapes.Add(poly);
            var ll = NewLayer();
            for (int k = 0; k < 6; k++) AddLine(ll, SnapIndex.PolygonVertex(poly, k), SnapIndex.PolygonVertex(poly, (k + 1) % 6));
            SameOutline(BandApi.Compute(lp, 0.02), BandApi.Compute(ll, 0.02), Exact, "hexagon stamp == its six edges as lines");
            var dotted = NewLayer();
            dotted.Shapes.Add(new LineShape(new EntityId(++s_id), V(0.2, 0.5), V(0.8, 0.5), new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted)));
            SameOutline(BandApi.Compute(dotted, 0.04), BandApi.Compute(L((0.2, 0.5, 0.8, 0.5)), 0.04), Exact, "dotted lines are banded too");
        }

        public static void V_NonFiniteAndDegenerateShapesAreSkipped()
        {
            // A garbage save can load NaN coordinates ("a null number string becomes NaN", plan 1). They must be skipped,
            // never poison the planar graph.
            var reference = BandApi.Compute(L((0.2, 0.5, 0.8, 0.5)), 0.04);
            var l = L((0.2, 0.5, 0.8, 0.5), (double.NaN, 0.3, 0.6, 0.6), (0.3, double.PositiveInfinity, 0.3, 0.1));
            l.Shapes.Add(null);
            l.Shapes.Add(new PolygonShape(new EntityId(++s_id), V(0.5, 0.5), 2, 0.1, 0, StrokeStyle.Default));          // < 3 sides
            l.Shapes.Add(new PolygonShape(new EntityId(++s_id), V(0.5, 0.5), 5, double.NaN, 0, StrokeStyle.Default));
            BandApi.Out o;
            try { o = BandApi.Compute(l, 0.04); }
            catch (Exception e) { throw new Exception($"garbage shapes threw {e.GetType().Name}: {e.Message}"); }
            SameOutline(o, reference, Exact, "NaN/Inf lines, a null entry and degenerate polygons are ignored");
            var empty = BandApi.Compute(NewLayer(), 0.04);
            Check.That(empty.Runs.Count == 0 && empty.Collapsed == 0, "empty layer: nothing");
        }

        // ================= golden boards =================

        public static void V_StarOutlineShapeAndTips()
        {
            var b = GoldenBoards.StarOnFullBoard();
            var star = b.FindLayer(b.ActiveLayerId);
            double w = 0.015, h = w / 2;
            var o = BandApi.Compute(star, w);
            AllFinite(o, "star");
            Check.That(o.Collapsed == 0, "default width: nothing collapsed");
            int closed = 0;
            foreach (var r in o.Runs) if (r.Closed) closed++;
            Check.That(o.Runs.Count == 10 && closed == 10,
                $"10 closed runs (octagon, 8 tip triangles, outer star outline), got {o.Runs.Count} ({closed} closed). {Dump(o)}");
            var canon = Canon(o);
            int tri = 0, oct = 0, outer = 0;
            foreach (var r in canon) { if (r.Pts.Count == 3) tri++; else if (r.Pts.Count == 8) oct++; else if (r.Pts.Count == 16) outer++; }
            Check.That(tri == 8 && oct == 1 && outer == 1, $"corner counts 8x3, 1x8, 1x16; got {tri}/{oct}/{outer}");
            // Tip k = 1 is a 90-degree square corner at P1: inner corner P1 - (h, h), outer tip P1 + (h, h).
            Vec2 p1 = GoldenBoards.StarPoint(GoldenBoards.BoardCenter, GoldenBoards.StarRadius, 1);
            Check.That(HasVertex(o, V(p1.X - h, p1.Y - h)), "tip 1 inner corner");
            Check.That(HasVertex(o, V(p1.X + h, p1.Y + h)), "tip 1 outer mitre");
            ClearOfBands(o, star, h, 1e-12, "star");
        }

        public static void V_StarRunsAreSymmetricUnder45Degrees()
        {
            var b = GoldenBoards.StarOnFullBoard();
            var o = BandApi.Compute(b.FindLayer(b.ActiveLayerId), 0.015);
            Vec2 c = GoldenBoards.BoardCenter;
            for (int k = 1; k < 8; k++)
            {
                double ang = k * Math.PI / 4;
                SymmetricVertexSet(o, v => Rot(v, c, ang), 1e-12, $"rotation by {k * 45} deg");
            }
            SymmetricVertexSet(o, v => V(1 - v.X, v.Y), 1e-12, "mirror x");
        }

        public static void V_Tiled2x2IsSymmetricAndClear()
        {
            var b = GoldenBoards.Tiled2x2();
            var layer = b.FindLayer(b.ActiveLayerId);
            double w = 0.015;
            var o = BandApi.Compute(layer, w);
            AllFinite(o, "Tiled2x2");
            Check.That(o.Collapsed == 0, "Tiled2x2 at 6 mm: nothing collapsed");
            SymmetricVertexSet(o, v => V(1 - v.Y, v.X), 1e-12, "Tiled2x2 rotation 90");
            SymmetricVertexSet(o, v => V(1 - v.X, v.Y), 1e-12, "Tiled2x2 mirror x");
            SymmetricVertexSet(o, v => V(v.Y, v.X), 1e-12, "Tiled2x2 mirror diagonal");
            ClearOfBands(o, layer, w / 2, 1e-12, "Tiled2x2");
            bool openRun = false;
            foreach (var r in o.Runs)
                if (!r.Closed)
                {
                    openRun = true;
                    foreach (var e in new[] { r.Pts[0], r.Pts[r.Pts.Count - 1] })
                        Check.That(Math.Abs(e.X) <= Exact || Math.Abs(e.Y) <= Exact || Math.Abs(e.X - 1) <= Exact || Math.Abs(e.Y - 1) <= Exact,
                            $"open run ends on the frame, got {P(e)}");
                }
            Check.That(openRun, "the tips on the frame cut some runs open");
        }

        // ================= determinism, fuzz, performance =================

        public static void V_SameShapesInAnyOrderGiveTheSameOutline()
        {
            var b = GoldenBoards.StarOnFullBoard();
            var star = b.FindLayer(b.ActiveLayerId);
            var a = BandApi.Compute(star, 0.015);
            var again = BandApi.Compute(star, 0.015);
            SameOutline(again, a, 0, "same input twice: bit-identical");
            var rnd = new Random(7);
            for (int trial = 0; trial < 5; trial++)
            {
                var shuffled = NewLayer();
                var list = new List<Shape>(star.Shapes);
                for (int i = list.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
                foreach (var s in list)
                {
                    var ln = (LineShape)s;
                    shuffled.Shapes.Add(rnd.Next(2) == 0 ? ln : new LineShape(ln.Id, ln.B, ln.A, ln.Style));   // also flipped
                }
                SameOutline(BandApi.Compute(shuffled, 0.015), a, Exact, $"shuffle {trial} (order and direction)");
            }
        }

        /// <summary>Random "snapped" design: endpoints from a 1/8 lattice, circle divisions and earlier endpoints.</summary>
        static Layer RandomDesign(Random rnd, int lines)
        {
            var l = NewLayer();
            var pts = new List<Vec2>();
            for (int i = 0; i <= 8; i++) for (int j = 0; j <= 8; j++) pts.Add(V(i / 8.0, j / 8.0));
            for (int c = 0; c < 2; c++)
            {
                Vec2 cc = pts[rnd.Next(pts.Count)];
                double r = (1 + rnd.Next(3)) / 8.0;
                for (int k = 0; k < 8; k++)
                {
                    Vec2 p = CircleDivisions.Point(cc, r, k);
                    if (p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1) pts.Add(p);
                }
            }
            for (int i = 0; i < lines; i++)
            {
                Vec2 a = pts[rnd.Next(pts.Count)], b = pts[rnd.Next(pts.Count)];
                if (rnd.Next(10) == 0) b = a;   // a zero-length line now and then
                AddLine(l, a, b);
                pts.Add(a);
            }
            if (rnd.Next(4) == 0)
                l.Shapes.Add(new PolygonShape(new EntityId(++s_id), V(0.2 + 0.6 * rnd.NextDouble(), 0.2 + 0.6 * rnd.NextDouble()),
                    3 + rnd.Next(4), 0.1, rnd.NextDouble(), StrokeStyle.Default));   // inside the board
            if (rnd.Next(4) == 0)
                l.Shapes.Add(new CircleShape(new EntityId(++s_id), V(0.5, 0.5), 0.25, StrokeStyle.Default));
            return l;
        }

        public static void V_FuzzSnappedDesignsStayFinite()
        {
            var rnd = new Random(20261124);
            int checkedClear = 0;
            for (int t = 0; t < 150; t++)
            {
                var l = RandomDesign(rnd, 3 + rnd.Next(25));
                BandApi.Out o;
                try { o = BandApi.Compute(l, 0.015); }
                catch (Exception e) { throw new Exception($"design {t}: {e.GetType().Name} {e.Message}"); }
                AllFinite(o, $"design {t}");
                double w = double.IsInfinity(o.MaxSafe) ? 0.015 : Math.Min(0.015, 0.5 * o.MaxSafe);
                if (w < 0.004) continue;   // below the slider's minimum: the width would be sanitized up
                var ok = BandApi.Compute(l, w);
                AllFinite(ok, $"design {t} at w {R(w)}");
                Check.That(ok.Collapsed == 0, $"design {t}: half of MaxSafeWidth ({R(w)}) collapsed {ok.Collapsed}");
                // Bands of features that do not touch may overlap (per-face offsets, not a boolean union); the engine
                // reports that as SelfIntersections. With none reported, the outline must be clear of every band.
                if (ok.SelfIntersections > 0) continue;
                if (NearMiss(l, w)) continue;   // covered (and failing) in V_NearMissBandsAreReported
                ClearOfBands(ok, l, w / 2, 2e-5, $"design {t}");
                checkedClear++;
            }
            Check.That(checkedClear >= 30, $"precondition: enough designs with a usable width ({checkedClear})");
        }

        /// <summary>Two structure segments that do not touch but come closer than the band width.</summary>
        static bool NearMiss(Layer l, double w)
        {
            var segs = Structure(l);
            for (int i = 0; i < segs.Count; i++)
                for (int j = i + 1; j < segs.Count; j++)
                {
                    var a = segs[i];
                    var b = segs[j];
                    double d = Math.Min(Math.Min(DistSeg(a.A, b.A, b.B), DistSeg(a.B, b.A, b.B)),
                        Math.Min(DistSeg(b.A, a.A, a.B), DistSeg(b.B, a.A, a.B)));
                    bool touch = d <= 1e-7 || Intersect.SegmentSegment(a.A, a.B, b.A, b.B, out _) == 1;
                    if (!touch && d < w + 1e-9) return true;
                }
            return false;
        }

        public static void V_NearMissBandsAreReported()
        {
            // Features that do not touch but sit closer than the band width: their outlines cross (here the stub's cap
            // even crosses the other line's structure). Plan 3.3/3.6: the width warning must fire before shapes break;
            // the builder's SelfIntersections claims to catch split events. Expect SelfIntersections > 0 or
            // MaxSafeWidth <= the gap (0.011).
            var stub = BandApi.Compute(L((0.1, 0.3, 0.6, 0.3), (0.3, 0.311, 0.3, 0.6)), 0.015);
            Check.That(stub.SelfIntersections > 0 || stub.MaxSafe <= 0.011 + 1e-9,
                $"stub 0.011 above a line at w 0.015: SelfIntersections {stub.SelfIntersections}, MaxSafeWidth {R(stub.MaxSafe)}. {Dump(stub)}");
            var island = BandApi.Compute(L((0.1, 0.1, 0.6, 0.1), (0.6, 0.1, 0.6, 0.6), (0.6, 0.6, 0.1, 0.6), (0.1, 0.6, 0.1, 0.1),
                (0.2, 0.111, 0.5, 0.111)), 0.015);
            Check.That(island.SelfIntersections > 0 || island.MaxSafe <= 0.011 + 1e-9,
                $"square with an inner line 0.011 from a side (one face, two cycles): SelfIntersections {island.SelfIntersections}, MaxSafeWidth {R(island.MaxSafe)}");
        }

        public static void V_FuzzOrderIndependence()
        {
            var rnd = new Random(4111);
            for (int t = 0; t < 40; t++)
            {
                var l = RandomDesign(rnd, 3 + rnd.Next(15));
                var rev = NewLayer();
                for (int i = l.Shapes.Count - 1; i >= 0; i--) rev.Shapes.Add(l.Shapes[i]);
                SameOutline(BandApi.Compute(rev, 0.01), BandApi.Compute(l, 0.01), 1e-12, $"design {t} reversed");
            }
        }

        public static void V_FiveHundredSegmentsAreFastEnough()
        {
            // Plan 3.6 / 7: 500 segments planarize in well under 50 ms on a desktop (loose: catches O(n^3), not tuning).
            var rnd = new Random(500);
            var shortSegs = NewLayer();
            for (int i = 0; i < 500; i++)
            {
                Vec2 a = V(rnd.NextDouble(), rnd.NextDouble());
                double ang = rnd.NextDouble() * Math.PI, len = 0.02 + 0.08 * rnd.NextDouble();
                Vec2 b = V(Math.Max(0, Math.Min(1, a.X + len * Math.Cos(ang))), Math.Max(0, Math.Min(1, a.Y + len * Math.Sin(ang))));
                AddLine(shortSegs, a, b);
            }
            // Long lattice lines: ~10^4 crossings. Only an O(n^3) guard (2 s); the time is printed for the report.
            var lattice = RandomDesign(new Random(9), 500);
            foreach (var (l, name, limit) in new[] { (shortSegs, "500 short random", 100.0), (lattice, "500 long lattice lines", 2000.0) })
            {
                BandApi.Compute(l, 0.01);   // JIT warm-up
                var sw = Stopwatch.StartNew();
                var o = BandApi.Compute(l, 0.01);
                sw.Stop();
                AllFinite(o, name);
                Console.WriteLine($"INFO {name}: {sw.Elapsed.TotalMilliseconds:F1} ms, {o.Runs.Count} runs");
                Check.That(sw.Elapsed.TotalMilliseconds < limit, $"{name}: {sw.Elapsed.TotalMilliseconds:F1} ms (limit {limit})");
            }
        }
    }
}
