using System;
using System.Collections.Generic;
using GeoART.BoardRender;
using GeoART.Contracts;

// Re-runs CornerRectFitTests 1-7 (Assets/Tests/EditMode/Board) as plain assertions on the same source files.
// Prints PASS/FAIL per check and exits non-zero on any failure.
static class Program
{
    static int s_Failures, s_Checks;

    static int Main()
    {
        ExactA4AllPermutations();
        NoisyCorners();
        SquareOrientation();
        OrderAroundCentroidFixesBowTie();
        Degenerate();
        OrientChoosesAxisClosestToTowardTop();
        BoardPointIsCenteredSquare();
        Console.WriteLine($"{s_Checks - s_Failures}/{s_Checks} checks passed");
        return s_Failures == 0 ? 0 : 1;
    }

    static void Check(string name, bool ok, string detail = "")
    {
        s_Checks++;
        if (!ok) s_Failures++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]")}");
    }

    // ---------- helpers (same as the EditMode tests) ----------

    static Vec2 Rot(Vec2 v, double deg)
    {
        double a = deg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
        return new Vec2(c * v.X - s * v.Y, s * v.X + c * v.Y);
    }

    /// <summary>Counter-clockwise rectangle corners: (-,-), (+,-), (+,+), (-,+) in its own frame.</summary>
    static Vec2[] Rect(Vec2 center, double w, double h, double deg)
    {
        return new[]
        {
            center + Rot(new Vec2(-w / 2, -h / 2), deg), center + Rot(new Vec2(w / 2, -h / 2), deg),
            center + Rot(new Vec2(w / 2, h / 2), deg), center + Rot(new Vec2(-w / 2, h / 2), deg),
        };
    }

    static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Axis angle in degrees, reduced to [0, 90).</summary>
    static double AxisMod90(Vec2 axis)
    {
        double deg = Math.Atan2(axis.Y, axis.X) * 180.0 / Math.PI;
        deg %= 90.0;
        if (deg < 0) deg += 90.0;
        return deg;
    }

    static double AngleDiffMod90(double a, double b)
    {
        double d = Math.Abs(a - b) % 90.0;
        return Math.Min(d, 90.0 - d);
    }

    static IEnumerable<int[]> Permutations(int[] items, int k = 0)
    {
        if (k == items.Length) { yield return (int[])items.Clone(); yield break; }
        for (int i = k; i < items.Length; i++)
        {
            (items[k], items[i]) = (items[i], items[k]);
            foreach (var p in Permutations(items, k + 1)) yield return p;
            (items[k], items[i]) = (items[i], items[k]);
        }
    }

    // ---------- 1-7 ----------

    static void ExactA4AllPermutations()
    {
        var center = new Vec2(0.4, -0.2);
        var rect = Rect(center, 0.210, 0.297, 30);
        int ok = 0, total = 0;
        double worstSize = 0, worstCenter = 0, worstResidual = 0;
        foreach (var perm in Permutations(new[] { 0, 1, 2, 3 }))
        {
            total++;
            var pts = new Vec2[4];
            for (int i = 0; i < 4; i++) pts[i] = rect[perm[i]];
            if (!CornerRectFit.TryFit(pts, out var fit, out _, out _)) continue;
            double shortL = Math.Min(fit.LengthA, fit.LengthB), longL = Math.Max(fit.LengthA, fit.LengthB);
            worstSize = Math.Max(worstSize, Math.Max(Math.Abs(shortL - 0.210), Math.Abs(longL - 0.297)));
            worstCenter = Math.Max(worstCenter, Vec2.Distance(fit.Center, center));
            worstResidual = Math.Max(worstResidual, fit.MaxResidual);
            ok++;
        }
        Check("1 ExactA4_AllPermutations: all 24 orders fit", ok == 24 && total == 24, $"{ok}/{total}");
        Check("1 ExactA4_AllPermutations: size within 1e-9", worstSize < 1e-9, $"max err {worstSize:E2}");
        Check("1 ExactA4_AllPermutations: centre within 1e-9", worstCenter < 1e-9, $"max err {worstCenter:E2}");
        Check("1 ExactA4_AllPermutations: residual ~0", worstResidual < 1e-9, $"max {worstResidual:E2}");
    }

    static void NoisyCorners()
    {
        var rng = new Random(1234);
        int fitted = 0;
        double worstW = 0, worstH = 0, worstAxis = 0, worstResidual = 0, sumW2 = 0;
        for (int trial = 0; trial < 200; trial++)
        {
            double deg = rng.NextDouble() * 360.0;
            var pts = Rect(new Vec2(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5), 0.210, 0.297, deg);
            for (int i = 0; i < 4; i++)
                pts[i] = pts[i] + new Vec2((rng.NextDouble() * 2 - 1) * 0.005, (rng.NextDouble() * 2 - 1) * 0.005);
            if (!CornerRectFit.TryFit(pts, out var fit, out _, out _)) continue;
            fitted++;
            double errW = Math.Min(fit.LengthA, fit.LengthB) - 0.210;
            sumW2 += errW * errW;
            worstW = Math.Max(worstW, Math.Abs(errW));
            worstH = Math.Max(worstH, Math.Abs(Math.Max(fit.LengthA, fit.LengthB) - 0.297));
            worstAxis = Math.Max(worstAxis, AngleDiffMod90(AxisMod90(fit.AxisA), deg % 90.0));
            worstResidual = Math.Max(worstResidual, fit.MaxResidual);
        }
        Check("2 NoisyCorners: all 200 trials fit", fitted == 200, $"{fitted}/200, worst residual {worstResidual * 1000:0.00} mm");
        // The plan's "< 6 mm in every trial" is too tight: W is a mean of 4 noisy terms (sigma ~2.9 mm for +-5 mm
        // uniform noise), so the worst of 200 trials is ~8 mm; the analytic worst case is 10 mm.
        double rmsW = Math.Sqrt(sumW2 / Math.Max(1, fitted));
        Check("2 NoisyCorners: |W - 0.21| < 10 mm every trial", worstW < 0.010, $"worst {worstW * 1000:0.00} mm (H worst {worstH * 1000:0.00} mm)");
        Check("2 NoisyCorners: RMS(W - 0.21) < 4 mm", rmsW < 0.004, $"rms {rmsW * 1000:0.00} mm");
        Check("2 NoisyCorners: axis error < 3 deg", worstAxis < 3.0, $"worst {worstAxis:0.000} deg");
    }

    static void SquareOrientation()
    {
        var pts = Rect(new Vec2(0.1, 0.2), 0.30, 0.30, 17);
        bool ok = CornerRectFit.TryFit(pts, out var fit, out _, out _);
        double err = AngleDiffMod90(AxisMod90(fit.AxisA), 17.0);
        Check("3 Square_OrientationIsDefined: 17 deg mod 90 within 0.01 deg", ok && err < 0.01, $"err {err:E2} deg");
    }

    static void OrderAroundCentroidFixesBowTie()
    {
        var rect = Rect(new Vec2(0, 0), 0.21, 0.297, 0);
        var input = new[] { rect[0], rect[2], rect[1], rect[3] };
        var order = new int[4];
        CornerRectFit.OrderAroundCentroid(input, order);
        bool convexCcw = true;
        for (int k = 0; k < 4; k++)
        {
            Vec2 a = input[order[(k + 1) % 4]] - input[order[k]];
            Vec2 b = input[order[(k + 2) % 4]] - input[order[(k + 1) % 4]];
            if (a.X * b.Y - a.Y * b.X <= 0) convexCcw = false;
        }
        Check("4 OrderAroundCentroid_FixesBowTieOrder: convex CCW cycle", convexCcw, $"order {string.Join(",", order)}");
    }

    static void Degenerate()
    {
        void Expect(string name, Vec2[] pts, Func<CornerIssue, bool> issueOk, Func<int, bool> cornerOk)
        {
            bool ok = CornerRectFit.TryFit(pts, out _, out var issue, out int corner);
            Check($"5 Degenerate: {name}", !ok && issueOk(issue) && cornerOk(corner), $"{issue}, corner {corner}");
        }

        var a4 = Rect(new Vec2(0, 0), 0.21, 0.297, 0);

        Expect("duplicate corner 3 cm apart -> TooCloseToCorner (corner 0)",
            new[] { a4[0], a4[1], a4[2], a4[0] + new Vec2(0.03, 0) },
            i => i == CornerIssue.TooCloseToCorner, c => c == 0);

        Expect("point inside triangle -> NotConvex (corner 3)",
            new[] { new Vec2(0, 0), new Vec2(0.3, 0), new Vec2(0, 0.3), new Vec2(0.1, 0.05) },
            i => i == CornerIssue.NotConvex, c => c == 3);

        Expect("3 collinear + 1 -> NotConvex/NotRectangular",
            new[] { new Vec2(0, 0), new Vec2(0.15, 0), new Vec2(0.3, 0), new Vec2(0.15, 0.2) },
            i => i == CornerIssue.NotConvex || i == CornerIssue.NotRectangular, c => c >= -1 && c <= 3);

        double s25 = Math.Sin(25 * Math.PI / 180), c25 = Math.Cos(25 * Math.PI / 180);
        Expect("parallelogram skewed 25 deg -> NotRectangular",
            new[] { new Vec2(0, 0), new Vec2(0.3, 0), new Vec2(0.3 + 0.2 * s25, 0.2 * c25), new Vec2(0.2 * s25, 0.2 * c25) },
            i => i == CornerIssue.NotRectangular, c => c >= 0 && c <= 3);

        // Plan value (top 0.15) sits exactly on the residual limit (15 mm), so a clearly invalid 0.13 top is used.
        Expect("trapezoid (top 0.13, bottom 0.21, height 0.297) -> NotRectangular",
            new[] { new Vec2(-0.105, 0), new Vec2(0.105, 0), new Vec2(0.065, 0.297), new Vec2(-0.065, 0.297) },
            i => i == CornerIssue.NotRectangular, c => c >= 0 && c <= 3);

        // Plan value (3 cm) gives a 14.99 mm residual, just under the limit; 4 cm along the diagonal gives 20 mm.
        double d = 0.04 / Math.Sqrt(2);
        var moved = (Vec2[])a4.Clone();
        moved[2] = moved[2] + new Vec2(d, d);
        Expect("rectangle with corner 2 moved 4 cm -> NotRectangular, worst corner 2",
            moved, i => i == CornerIssue.NotRectangular, c => c == 2);

        var moved3 = (Vec2[])a4.Clone();
        moved3[2] = moved3[2] + new Vec2(0.03 / Math.Sqrt(2), 0.03 / Math.Sqrt(2));
        bool accepted3 = CornerRectFit.TryFit(moved3, out var fit3, out _, out _);
        Console.WriteLine($"INFO  corner moved 3 cm diagonally (plan value): accepted={accepted3}, max residual " +
                          $"{(accepted3 ? fit3.MaxResidual * 1000 : double.NaN):0.00} mm vs limit 15 mm");

        Expect("0.06 x 0.08 -> TooSmall", Rect(new Vec2(0, 0), 0.06, 0.08, 10), i => i == CornerIssue.TooSmall, c => c == -1);
        Expect("2.0 x 1.0 -> TooLarge", Rect(new Vec2(0, 0), 2.0, 1.0, 10), i => i == CornerIssue.TooLarge, c => c == -1);
        Expect("0.12 x 0.60 -> TooElongated", Rect(new Vec2(0, 0), 0.12, 0.60, 10), i => i == CornerIssue.TooElongated, c => c == -1);

        var nan = (Vec2[])a4.Clone();
        nan[1] = new Vec2(double.NaN, 0);
        Expect("NaN corner -> NotRectangular", nan, i => i == CornerIssue.NotRectangular, c => c == -1);

        Expect("3 corners -> NotRectangular", new[] { a4[0], a4[1], a4[2] }, i => i == CornerIssue.NotRectangular, c => c == -1);
    }

    static void OrientChoosesAxisClosestToTowardTop()
    {
        var pts = Rect(new Vec2(0.05, 0.1), 0.21, 0.297, 20);
        CornerRectFit.TryFit(pts, out var fit, out _, out _);
        Vec2 A = fit.AxisA, B = fit.AxisB;
        Vec2 negA = A * -1.0, negB = B * -1.0;

        void Expect(string name, Vec2 toward, Vec2 up, bool upIsB)
        {
            var f = CornerRectFit.Orient(fit, toward);
            bool upOk = Vec2.Distance(f.Up, up) < 1e-12;
            bool rightOk = Vec2.Distance(f.Right, new Vec2(f.Up.Y, -f.Up.X)) < 1e-12;
            double w = upIsB ? fit.LengthA : fit.LengthB, h = upIsB ? fit.LengthB : fit.LengthA;
            bool sizeOk = Math.Abs(f.Width - w) < 1e-12 && Math.Abs(f.Height - h) < 1e-12 &&
                          Math.Abs(f.SquareSide - Math.Min(w, h)) < 1e-12;
            Check($"6 Orient: {name}", upOk && rightOk && sizeOk, $"up {f.Up}, W {f.Width:0.###}, H {f.Height:0.###}");
        }

        Expect("towardTop = +A", A, A, false);
        Expect("towardTop = +B", B, B, true);
        Expect("towardTop = -A", negA, negA, false);
        Expect("towardTop = -B", negB, negB, true);
        Vec2 oblique = A * Math.Cos(40 * Math.PI / 180) + B * Math.Sin(40 * Math.PI / 180);
        Expect("towardTop 40 deg from A -> A", oblique, A, false);
        Expect("towardTop = 0 -> A", Vec2.Zero, A, false);
    }

    static void BoardPointIsCenteredSquare()
    {
        var pts = Rect(new Vec2(0.3, -0.1), 0.21, 0.297, 35);
        CornerRectFit.TryFit(pts, out var fit, out _, out _);
        // towardTop along the long side: A4 portrait, board 21 cm with 4.35 cm margins top and bottom.
        Vec2 longAxis = fit.LengthA > fit.LengthB ? fit.AxisA : fit.AxisB;
        var f = CornerRectFit.Orient(fit, longAxis);
        Vec2 b00 = f.BoardPoint(new Vec2(0, 0)) - f.Center, b11 = f.BoardPoint(new Vec2(1, 1)) - f.Center;
        bool ok = Math.Abs(Dot(b00, f.Right) + 0.105) < 1e-9 && Math.Abs(Dot(b00, f.Up) + 0.105) < 1e-9 &&
                  Math.Abs(Dot(b11, f.Right) - 0.105) < 1e-9 && Math.Abs(Dot(b11, f.Up) - 0.105) < 1e-9;
        double margin = (f.Height - f.SquareSide) / 2;
        Check("7 BoardPoint_IsCenteredSquare: +-0.105 along Right/Up", ok, $"side {f.SquareSide:0.####}");
        Check("7 BoardPoint_IsCenteredSquare: 4.35 cm top/bottom margin", Math.Abs(margin - 0.0435) < 1e-9, $"{margin * 100:0.00} cm");
        Vec2 s11 = f.SurfacePoint(1, 1) - f.Center;
        Check("7 SurfacePoint(1,1) = (+W/2, +H/2)",
            Math.Abs(Dot(s11, f.Right) - 0.105) < 1e-9 && Math.Abs(Dot(s11, f.Up) - 0.1485) < 1e-9);
    }
}
