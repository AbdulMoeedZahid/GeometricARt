using System;
using System.Collections.Generic;
using GeoART.BoardRender;
using GeoART.Contracts;
using NUnit.Framework;

namespace GeoART.Tests
{
    /// <summary>
    /// Pure-math checks for corner placement (CORNER_PLACEMENT_PLAN 5, tests 1-7). tools/mathcheck re-runs the same
    /// checks with the dotnet CLI, because the Unity test runner cannot run over SSH.
    /// </summary>
    public class CornerRectFitTests
    {
        const double W = 0.210, H = 0.297;   // A4

        static Vec2 Rot(Vec2 v, double deg)
        {
            double a = deg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            return new Vec2(c * v.X - s * v.Y, s * v.X + c * v.Y);
        }

        /// <summary>Counter-clockwise rectangle corners: (-,-), (+,-), (+,+), (-,+) in its own frame.</summary>
        static Vec2[] Rect(Vec2 center, double w, double h, double deg) => new[]
        {
            center + Rot(new Vec2(-w / 2, -h / 2), deg), center + Rot(new Vec2(w / 2, -h / 2), deg),
            center + Rot(new Vec2(w / 2, h / 2), deg), center + Rot(new Vec2(-w / 2, h / 2), deg),
        };

        static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        static double AxisMod90(Vec2 axis)
        {
            double deg = Math.Atan2(axis.Y, axis.X) * 180.0 / Math.PI % 90.0;
            return deg < 0 ? deg + 90.0 : deg;
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

        static void AssertFails(Vec2[] pts, CornerIssue expected, int expectedCorner)
        {
            Assert.IsFalse(CornerRectFit.TryFit(pts, out _, out var issue, out int corner));
            Assert.That(issue, Is.EqualTo(expected));
            Assert.That(corner, Is.EqualTo(expectedCorner));
        }

        [Test]
        public void ExactA4_AllPermutations_RecoverSizeAndCenter()
        {
            var center = new Vec2(0.4, -0.2);
            var rect = Rect(center, W, H, 30);
            int count = 0;
            foreach (var perm in Permutations(new[] { 0, 1, 2, 3 }))
            {
                var pts = new Vec2[4];
                for (int i = 0; i < 4; i++) pts[i] = rect[perm[i]];
                Assert.IsTrue(CornerRectFit.TryFit(pts, out var fit, out var issue, out _), $"order {string.Join(",", perm)}: {issue}");
                Assert.That(Math.Min(fit.LengthA, fit.LengthB), Is.EqualTo(W).Within(1e-9));
                Assert.That(Math.Max(fit.LengthA, fit.LengthB), Is.EqualTo(H).Within(1e-9));
                Assert.That(Vec2.Distance(fit.Center, center), Is.LessThan(1e-9));
                Assert.That(fit.MaxResidual, Is.LessThan(1e-9));
                count++;
            }
            Assert.That(count, Is.EqualTo(24));
        }

        /// <summary>
        /// Deviation from the plan's "&lt; 6 mm in every trial": the fitted width is a mean of 4 noisy terms, so with
        /// +-5 mm uniform noise its sigma is ~2.9 mm and the worst of 200 trials is ~8 mm (analytic worst case 10 mm).
        /// </summary>
        [Test]
        public void NoisyCorners_SizeWithin6mm_AngleWithin3deg()
        {
            var rng = new Random(1234);
            double sumW2 = 0;
            for (int trial = 0; trial < 200; trial++)
            {
                double deg = rng.NextDouble() * 360.0;
                var pts = Rect(new Vec2(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5), W, H, deg);
                for (int i = 0; i < 4; i++)
                    pts[i] = pts[i] + new Vec2((rng.NextDouble() * 2 - 1) * 0.005, (rng.NextDouble() * 2 - 1) * 0.005);
                Assert.IsTrue(CornerRectFit.TryFit(pts, out var fit, out var issue, out _), $"trial {trial}: {issue}");
                double errW = Math.Min(fit.LengthA, fit.LengthB) - W;
                sumW2 += errW * errW;
                Assert.That(Math.Abs(errW), Is.LessThan(0.010), $"trial {trial}");
                Assert.That(AngleDiffMod90(AxisMod90(fit.AxisA), deg % 90.0), Is.LessThan(3.0), $"trial {trial}");
            }
            Assert.That(Math.Sqrt(sumW2 / 200), Is.LessThan(0.004), "RMS width error");
        }

        [Test]
        public void Square_OrientationIsDefined()
        {
            Assert.IsTrue(CornerRectFit.TryFit(Rect(new Vec2(0.1, 0.2), 0.30, 0.30, 17), out var fit, out _, out _));
            Assert.That(AngleDiffMod90(AxisMod90(fit.AxisA), 17.0), Is.LessThan(0.01));
        }

        [Test]
        public void OrderAroundCentroid_FixesBowTieOrder()
        {
            var rect = Rect(Vec2.Zero, W, H, 0);
            var input = new[] { rect[0], rect[2], rect[1], rect[3] };
            var order = new int[4];
            CornerRectFit.OrderAroundCentroid(input, order);
            for (int k = 0; k < 4; k++)
            {
                Vec2 a = input[order[(k + 1) % 4]] - input[order[k]];
                Vec2 b = input[order[(k + 2) % 4]] - input[order[(k + 1) % 4]];
                Assert.That(a.X * b.Y - a.Y * b.X, Is.GreaterThan(0.0), "convex counter-clockwise cycle");
            }
        }

        [Test]
        public void Degenerate_DuplicateCorner_TooCloseToCorner()
        {
            var r = Rect(Vec2.Zero, W, H, 0);
            AssertFails(new[] { r[0], r[1], r[2], r[0] + new Vec2(0.03, 0) }, CornerIssue.TooCloseToCorner, 0);
        }

        [Test]
        public void Degenerate_PointInsideTriangle_NotConvex() =>
            AssertFails(new[] { new Vec2(0, 0), new Vec2(0.3, 0), new Vec2(0, 0.3), new Vec2(0.1, 0.05) }, CornerIssue.NotConvex, 3);

        [Test]
        public void Degenerate_ThreeCollinear_NotConvexOrNotRectangular()
        {
            var pts = new[] { new Vec2(0, 0), new Vec2(0.15, 0), new Vec2(0.3, 0), new Vec2(0.15, 0.2) };
            Assert.IsFalse(CornerRectFit.TryFit(pts, out _, out var issue, out _));
            Assert.That(issue == CornerIssue.NotConvex || issue == CornerIssue.NotRectangular, issue.ToString());
        }

        [Test]
        public void Degenerate_Parallelogram25Deg_NotRectangular()
        {
            double s = Math.Sin(25 * Math.PI / 180), c = Math.Cos(25 * Math.PI / 180);
            var pts = new[] { new Vec2(0, 0), new Vec2(0.3, 0), new Vec2(0.3 + 0.2 * s, 0.2 * c), new Vec2(0.2 * s, 0.2 * c) };
            Assert.IsFalse(CornerRectFit.TryFit(pts, out _, out var issue, out int corner));
            Assert.That(issue, Is.EqualTo(CornerIssue.NotRectangular));
            Assert.That(corner, Is.InRange(0, 3));   // all four angles deviate equally
        }

        /// <summary>The plan's top 0.15 sits exactly on the 15 mm residual limit; 0.13 is clearly invalid.</summary>
        [Test]
        public void Degenerate_Trapezoid_NotRectangular()
        {
            var pts = new[] { new Vec2(-0.105, 0), new Vec2(0.105, 0), new Vec2(0.065, 0.297), new Vec2(-0.065, 0.297) };
            Assert.IsFalse(CornerRectFit.TryFit(pts, out _, out var issue, out int corner));
            Assert.That(issue, Is.EqualTo(CornerIssue.NotRectangular));
            Assert.That(corner, Is.InRange(0, 3));
        }

        /// <summary>The plan's 3 cm move gives a 14.99 mm residual (just under the limit); 4 cm diagonally gives 20 mm.</summary>
        [Test]
        public void Degenerate_OneCornerMoved4cm_NotRectangular_WorstIsThatCorner()
        {
            var pts = Rect(Vec2.Zero, W, H, 0);
            double d = 0.04 / Math.Sqrt(2);
            pts[2] = pts[2] + new Vec2(d, d);
            AssertFails(pts, CornerIssue.NotRectangular, 2);
        }

        [Test]
        public void Degenerate_TooSmall() => AssertFails(Rect(Vec2.Zero, 0.06, 0.08, 10), CornerIssue.TooSmall, -1);

        [Test]
        public void Degenerate_TooLarge() => AssertFails(Rect(Vec2.Zero, 2.0, 1.0, 10), CornerIssue.TooLarge, -1);

        [Test]
        public void Degenerate_TooElongated() => AssertFails(Rect(Vec2.Zero, 0.12, 0.60, 10), CornerIssue.TooElongated, -1);

        [Test]
        public void Degenerate_NaNCorner_NotRectangular()
        {
            var pts = Rect(Vec2.Zero, W, H, 0);
            pts[1] = new Vec2(double.NaN, 0);
            AssertFails(pts, CornerIssue.NotRectangular, -1);
        }

        [Test]
        public void Orient_ChoosesAxisClosestToTowardTop()
        {
            Assert.IsTrue(CornerRectFit.TryFit(Rect(new Vec2(0.05, 0.1), W, H, 20), out var fit, out _, out _));
            Vec2 a = fit.AxisA, b = fit.AxisB;

            void Expect(Vec2 toward, Vec2 up, bool upIsB)
            {
                var f = CornerRectFit.Orient(fit, toward);
                Assert.That(Vec2.Distance(f.Up, up), Is.LessThan(1e-12), $"towardTop {toward}");
                Assert.That(Vec2.Distance(f.Right, new Vec2(f.Up.Y, -f.Up.X)), Is.LessThan(1e-12));
                Assert.That(f.Width, Is.EqualTo(upIsB ? fit.LengthA : fit.LengthB).Within(1e-12));
                Assert.That(f.Height, Is.EqualTo(upIsB ? fit.LengthB : fit.LengthA).Within(1e-12));
                Assert.That(f.SquareSide, Is.EqualTo(Math.Min(f.Width, f.Height)).Within(1e-12));
            }

            Expect(a, a, false);
            Expect(b, b, true);
            Expect(a * -1.0, a * -1.0, false);
            Expect(b * -1.0, b * -1.0, true);
            Expect(a * Math.Cos(40 * Math.PI / 180) + b * Math.Sin(40 * Math.PI / 180), a, false);
            Expect(Vec2.Zero, a, false);
        }

        [Test]
        public void BoardPoint_IsCenteredSquare()
        {
            Assert.IsTrue(CornerRectFit.TryFit(Rect(new Vec2(0.3, -0.1), W, H, 35), out var fit, out _, out _));
            Vec2 longAxis = fit.LengthA > fit.LengthB ? fit.AxisA : fit.AxisB;
            var f = CornerRectFit.Orient(fit, longAxis);   // A4 portrait
            Vec2 b00 = f.BoardPoint(new Vec2(0, 0)) - f.Center, b11 = f.BoardPoint(new Vec2(1, 1)) - f.Center;
            Assert.That(Dot(b00, f.Right), Is.EqualTo(-0.105).Within(1e-9));
            Assert.That(Dot(b00, f.Up), Is.EqualTo(-0.105).Within(1e-9));
            Assert.That(Dot(b11, f.Right), Is.EqualTo(0.105).Within(1e-9));
            Assert.That(Dot(b11, f.Up), Is.EqualTo(0.105).Within(1e-9));
            Assert.That((f.Height - f.SquareSide) / 2, Is.EqualTo(0.0435).Within(1e-9), "4.35 cm margin top and bottom");
        }
    }
}
