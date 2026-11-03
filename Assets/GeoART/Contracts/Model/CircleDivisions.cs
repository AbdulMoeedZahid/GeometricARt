namespace GeoART.Contracts
{
    /// <summary>
    /// The 8 division points of a circle, at angles k * 45 degrees (k = 0..7, counter-clockwise from +X), from an exact
    /// table instead of Math.Cos/Math.Sin. The table uses 0, +-1 and ONE constant <see cref="Diagonal"/> (sqrt(2)/2) for
    /// both cos and sin, so:
    ///   - points at multiples of 90 degrees are exact (Math.Sin(Math.PI) is about 1.2e-16, not 0), and
    ///   - neighbouring circles that touch (e.g. two r = 0.25 circles in a 2x2 grid) produce bit-identical shared points,
    ///     so patterns built by snapping meet seamlessly and compare with ==.
    /// Single source of truth for <see cref="GoldenBoards.StarPoint"/> and the engine's snap candidates
    /// (Engine/Snap/SnapIndex.cs). Pure C#.
    /// </summary>
    public static class CircleDivisions
    {
        public const int Count = 8;

        /// <summary>sqrt(2)/2 rounded to the nearest double; used for both cos(45) and sin(45).</summary>
        public const double Diagonal = 0.70710678118654752440;

        static readonly double[] s_Cos = { 1, Diagonal, 0, -Diagonal, -1, -Diagonal, 0, Diagonal };
        static readonly double[] s_Sin = { 0, Diagonal, 1, Diagonal, 0, -Diagonal, -1, -Diagonal };

        /// <summary>cos(k * 45 degrees) from the exact table; k is taken modulo 8 (negative k allowed).</summary>
        public static double Cos(int k) => s_Cos[Wrap(k)];

        /// <summary>sin(k * 45 degrees) from the exact table; k is taken modulo 8 (negative k allowed).</summary>
        public static double Sin(int k) => s_Sin[Wrap(k)];

        /// <summary>Division point k of the circle (center, radius): center + radius * (Cos(k), Sin(k)).</summary>
        public static Vec2 Point(Vec2 center, double radius, int k)
        {
            int i = Wrap(k);
            return new Vec2(center.X + radius * s_Cos[i], center.Y + radius * s_Sin[i]);
        }

        static int Wrap(int k) => ((k % Count) + Count) % Count;
    }
}
