using System;
using GeoART.Contracts;

// Pure C# (System + GeoART.Contracts only, no UnityEngine, C# 9 at most): this file is also compiled by
// tools/mathcheck, a dotnet console harness that runs the fit checks without the Unity test runner.
namespace GeoART.BoardRender
{
    /// <summary>Rectangle in 2D plane coordinates (metres). AxisB = Perp(AxisA) = (-A.Y, A.X).</summary>
    public readonly struct FittedRect
    {
        public readonly Vec2 Center, AxisA, AxisB;
        public readonly double LengthA, LengthB, MaxResidual, RmsResidual;
        /// <summary>Input index of the corner with the largest residual.</summary>
        public readonly int WorstCorner;

        public FittedRect(Vec2 center, Vec2 axisA, double lengthA, double lengthB, double maxResidual, double rmsResidual, int worstCorner)
        {
            Center = center;
            AxisA = axisA;
            AxisB = new Vec2(-axisA.Y, axisA.X);
            LengthA = lengthA;
            LengthB = lengthB;
            MaxResidual = maxResidual;
            RmsResidual = rmsResidual;
            WorstCorner = worstCorner;
        }
    }

    /// <summary>
    /// Oriented board frame on the plane: Up = board +v, Right = board +u = (Up.Y, -Up.X).
    /// Plane 2D (x, y) behaves like board-local (x, z), so this is the same handedness as BoardGeometry.
    /// </summary>
    public readonly struct RectFrame
    {
        public readonly Vec2 Center, Right, Up;
        /// <summary>Extent along Right (Width) and along Up (Height), metres.</summary>
        public readonly double Width, Height;

        public RectFrame(Vec2 center, Vec2 up, double width, double height)
        {
            Center = center;
            Up = up;
            Right = new Vec2(up.Y, -up.X);
            Width = width;
            Height = height;
        }

        /// <summary>Side of the largest centred square inside the rectangle (D1): the board side.</summary>
        public double SquareSide => Math.Min(Width, Height);

        /// <summary>(s, t) in [0,1]^2 over the whole registered surface.</summary>
        public Vec2 SurfacePoint(double s, double t) => Center + Right * ((s - 0.5) * Width) + Up * ((t - 0.5) * Height);

        /// <summary>Board (u, v) in [0,1]^2 over the centred square.</summary>
        public Vec2 BoardPoint(Vec2 uv) => Center + Right * ((uv.X - 0.5) * SquareSide) + Up * ((uv.Y - 0.5) * SquareSide);
    }

    /// <summary>
    /// Fits a rectangle to 4 picked corners (CORNER_PLACEMENT_PLAN 4.2). Orientation comes from the 4-fold circular
    /// mean of the edge directions (works for squares, unlike PCA, and does not depend on tap order); extents are the
    /// least-squares fit for that orientation. Allocation-free.
    /// </summary>
    public static class CornerRectFit
    {
        public const double MinCornerSeparation = 0.05, MinSide = 0.10, MaxSide = 1.50, MaxAspect = 4.0;
        public const double MaxAngleDeviationDeg = 15.0, MinResidualLimit = 0.015, RelResidualLimit = 0.05;

        const double ConvexEpsilon = 1e-6;

        static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Fills order[0..3] with input indices sorted counter-clockwise by atan2 around the centroid.</summary>
        public static void OrderAroundCentroid(ReadOnlySpan<Vec2> pts, Span<int> order)
        {
            int n = Math.Min(pts.Length, Math.Min(order.Length, 4));
            double cx = 0, cy = 0;
            for (int i = 0; i < n; i++) { cx += pts[i].X; cy += pts[i].Y; }
            if (n > 0) { cx /= n; cy /= n; }

            Span<double> angle = stackalloc double[4];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                angle[i] = Math.Atan2(pts[i].Y - cy, pts[i].X - cx);
            }
            for (int i = 1; i < n; i++)   // insertion sort, 4 items
            {
                int idx = order[i];
                double a = angle[i];
                int j = i - 1;
                while (j >= 0 && angle[j] > a)
                {
                    order[j + 1] = order[j];
                    angle[j + 1] = angle[j];
                    j--;
                }
                order[j + 1] = idx;
                angle[j + 1] = a;
            }
        }

        /// <summary>
        /// corners.Length must be 4 (pick order). On failure <paramref name="issue"/> says why and
        /// <paramref name="issueCorner"/> is the input index to blame, or -1.
        /// </summary>
        public static bool TryFit(ReadOnlySpan<Vec2> corners, out FittedRect rect, out CornerIssue issue, out int issueCorner)
        {
            rect = default;
            issueCorner = -1;

            // 1. Shape of the input.
            if (corners.Length != 4) { issue = CornerIssue.NotRectangular; return false; }
            for (int i = 0; i < 4; i++)
                if (!corners[i].IsFinite) { issue = CornerIssue.NotRectangular; return false; }

            // 2. Separation: blame the earlier corner of the pair.
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (Vec2.Distance(corners[i], corners[j]) < MinCornerSeparation)
                    {
                        issue = CornerIssue.TooCloseToCorner;
                        issueCorner = i;
                        return false;
                    }

            // 3. Order counter-clockwise around the centroid.
            Span<int> idx = stackalloc int[4];
            OrderAroundCentroid(corners, idx);
            Vec2 c = new Vec2((corners[0].X + corners[1].X + corners[2].X + corners[3].X) * 0.25,
                              (corners[0].Y + corners[1].Y + corners[2].Y + corners[3].Y) * 0.25);
            Span<Vec2> q = stackalloc Vec2[4];
            Span<Vec2> e = stackalloc Vec2[4];
            for (int k = 0; k < 4; k++) q[k] = corners[idx[k]];
            for (int k = 0; k < 4; k++) e[k] = q[(k + 1) & 3] - q[k];

            // 4. Convexity: every turn is a left turn.
            for (int k = 0; k < 4; k++)
                if (!(Cross(e[k], e[(k + 1) & 3]) > ConvexEpsilon))
                {
                    issue = CornerIssue.NotConvex;
                    issueCorner = idx[(k + 1) & 3];
                    return false;
                }

            // 5. Interior angles near 90 degrees.
            double worstDev = -1;
            int worstAngleCorner = -1;
            for (int k = 0; k < 4; k++)
            {
                Vec2 a = e[k], b = e[(k + 1) & 3];
                double cos = -Dot(a, b) / (a.Length * b.Length);
                cos = Math.Max(-1.0, Math.Min(1.0, cos));
                double dev = Math.Abs(Math.Acos(cos) * (180.0 / Math.PI) - 90.0);
                if (dev > worstDev) { worstDev = dev; worstAngleCorner = idx[(k + 1) & 3]; }
            }
            if (worstDev > MaxAngleDeviationDeg)
            {
                issue = CornerIssue.NotRectangular;
                issueCorner = worstAngleCorner;
                return false;
            }

            // 6. Orientation: 4-fold circular mean of the edge directions, weighted by length.
            double sx = 0, sy = 0, sumL = 0;
            for (int k = 0; k < 4; k++)
            {
                double len = e[k].Length;
                double phi4 = 4.0 * Math.Atan2(e[k].Y, e[k].X);
                sx += len * Math.Cos(phi4);
                sy += len * Math.Sin(phi4);
                sumL += len;
            }
            if (Math.Sqrt(sx * sx + sy * sy) < 1e-9 * sumL) { issue = CornerIssue.NotRectangular; return false; }
            double theta = Math.Atan2(sy, sx) * 0.25;
            Vec2 axisA = new Vec2(Math.Cos(theta), Math.Sin(theta));
            Vec2 axisB = new Vec2(-axisA.Y, axisA.X);

            // 7. Least-squares extents for that orientation.
            Span<double> x = stackalloc double[4];
            Span<double> y = stackalloc double[4];
            int posX = 0, posY = 0, signMask = 0;
            double xMin = 0, xMax = 0, yMin = 0, yMax = 0;
            for (int k = 0; k < 4; k++)
            {
                Vec2 d = q[k] - c;
                x[k] = Dot(d, axisA);
                y[k] = Dot(d, axisB);
                bool px = x[k] > 0, py = y[k] > 0;
                if (px) { posX++; xMax += x[k]; } else xMin += x[k];
                if (py) { posY++; yMax += y[k]; } else yMin += y[k];
                signMask |= 1 << ((px ? 1 : 0) | (py ? 2 : 0));
            }
            if (posX != 2 || posY != 2 || signMask != 0xF) { issue = CornerIssue.NotRectangular; return false; }
            xMin *= 0.5; xMax *= 0.5; yMin *= 0.5; yMax *= 0.5;
            Vec2 center = c + axisA * ((xMin + xMax) * 0.5) + axisB * ((yMin + yMax) * 0.5);
            double lengthA = xMax - xMin, lengthB = yMax - yMin;

            // 8. Residuals against the fitted corners.
            double maxR = -1, sumR2 = 0;
            int worst = -1;
            for (int k = 0; k < 4; k++)
            {
                double dx = x[k] - (x[k] > 0 ? xMax : xMin);
                double dy = y[k] - (y[k] > 0 ? yMax : yMin);
                double r2 = dx * dx + dy * dy;
                sumR2 += r2;
                double r = Math.Sqrt(r2);
                if (r > maxR) { maxR = r; worst = idx[k]; }
            }
            double rms = Math.Sqrt(sumR2 * 0.25);
            double shorter = Math.Min(lengthA, lengthB), longer = Math.Max(lengthA, lengthB);
            rect = new FittedRect(center, axisA, lengthA, lengthB, maxR, rms, worst);
            if (maxR > Math.Max(MinResidualLimit, RelResidualLimit * shorter))
            {
                issue = CornerIssue.NotRectangular;
                issueCorner = worst;
                rect = default;
                return false;
            }

            // 9. Size limits.
            if (shorter < MinSide) issue = CornerIssue.TooSmall;
            else if (longer > MaxSide) issue = CornerIssue.TooLarge;
            else if (longer / shorter > MaxAspect) issue = CornerIssue.TooElongated;
            else { issue = CornerIssue.None; return true; }
            rect = default;
            return false;
        }

        /// <summary>Picks Up in {A, B, -A, -B} with max dot(towardTop); |towardTop| &lt; 1e-9 gives Up = AxisA.</summary>
        public static RectFrame Orient(in FittedRect rect, Vec2 towardTop)
        {
            Vec2 a = rect.AxisA, b = rect.AxisB;
            Vec2 up = a;
            bool upIsB = false;
            if (towardTop.Length >= 1e-9)
            {
                double best = Dot(a, towardTop);
                double db = Dot(b, towardTop);
                if (db > best) { best = db; up = b; upIsB = true; }
                double dna = -Dot(a, towardTop);
                if (dna > best) { best = dna; up = a * -1.0; upIsB = false; }
                double dnb = -db;
                if (dnb > best) { up = b * -1.0; upIsB = true; }
            }
            double width = upIsB ? rect.LengthA : rect.LengthB;
            double height = upIsB ? rect.LengthB : rect.LengthA;
            return new RectFrame(rect.Center, up, width, height);
        }
    }
}
