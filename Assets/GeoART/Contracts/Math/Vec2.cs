using System;

namespace GeoART.Contracts
{
    /// <summary>
    /// Board-space vector in doubles. The board is [0,1] x [0,1]: origin at the bottom-left corner
    /// chosen at placement, +X to the right, +Y toward the board top (MVP_PLAN 2.1).
    /// Also used for screen pixels by <see cref="IBoardSpace"/> so Contracts stays engine-free.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly double X, Y;

        public Vec2(double x, double y) { X = x; Y = y; }

        public static Vec2 Zero => new Vec2(0, 0);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        public double Length => Math.Sqrt(X * X + Y * Y);
        public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

        public bool IsFinite =>
            !double.IsNaN(X) && !double.IsInfinity(X) && !double.IsNaN(Y) && !double.IsInfinity(Y);

        public bool Equals(Vec2 o) => X.Equals(o.X) && Y.Equals(o.Y);
        public override bool Equals(object obj) => obj is Vec2 o && Equals(o);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.#####}, {Y:0.#####})";
    }
}
